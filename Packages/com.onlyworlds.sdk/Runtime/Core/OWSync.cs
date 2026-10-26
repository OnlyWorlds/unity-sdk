using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>What a sync did.</summary>
    public struct OWSyncResult
    {
        public int Fetched;
        public int Removed;
        public long Cursor;

        /// <summary>True when the cache was rebuilt from scratch rather than updated.</summary>
        public bool WasRebaselined;

        /// <summary>Why a re-baseline happened, or null.</summary>
        public string RebaselineReason;

        /// <summary>
        /// On a re-baseline the server asked for (409 <c>resync_required</c>): its reason, <c>role</c> or <c>view</c>
        /// (<see cref="OWApiError.ResyncReason"/>), or null when it sent none or asked for nothing.
        /// </summary>
        public string ResyncReason;

        /// <summary>
        /// On such a re-baseline for <c>role</c> or no reason, and on one for a cursor ahead of the server's head: how
        /// many elements the cache held that the new walk did not name, and kept (out of view or rewound, not gone).
        /// Zero on <c>view</c>, which drops them.
        /// </summary>
        public int Kept;

        public bool ChangedAnything => Fetched > 0 || Removed > 0 || WasRebaselined;
    }

    /// <summary>
    /// Fills and updates an <see cref="OWWorldCache"/> from the API.
    /// </summary>
    /// <remarks>
    /// Separate from both the client and the cache on purpose: the client knows the wire, the cache
    /// knows storage, and the policy connecting them -- when to re-baseline, what a rewound cursor
    /// means -- belongs to neither. Keeping it here means the other two stay testable without it.
    /// </remarks>
    public static class OWSync
    {
        /// <summary>Every element type in the standard.</summary>
        public static readonly string[] ElementTypes =
        {
            "ability", "character", "collective", "construct", "creature", "event", "family",
            "institution", "language", "law", "location", "map", "marker", "narrative", "object",
            "phenomenon", "pin", "relation", "species", "title", "trait", "zone",
        };

        /// <summary>
        /// Walks every type and replaces the cache's contents.
        /// </summary>
        /// <remarks>
        /// Uses the per-type list endpoints rather than <c>/changes</c>. The tip is read first, and
        /// its <c>cursor</c> and <c>epoch</c> are stored with its <c>head</c>, so the next
        /// incremental sync resumes exactly where the baseline sits.
        /// </remarks>
        public static async Task<OWSyncResult> BaselineAsync(
            OWClient client, OWWorldCache cache,
            Action<string, int> onType = null, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            GuardWritable(cache);

            // Read the tip BEFORE fetching. Taking it afterwards would mean any change landing
            // during the walk is silently below the recorded cursor and never syncs again.
            var tip = await client.ChangesAsync(headOnly: true, ct: ct).ConfigureAwait(false);

            cache.Clear();
            var fetched = 0;

            foreach (var type in ElementTypes)
            {
                ct.ThrowIfCancellationRequested();

                var elements = await client.ListAllAsync<JObject>(type, ct: ct).ConfigureAwait(false);
                foreach (var element in elements)
                {
                    var id = element["id"]?.ToString();
                    if (string.IsNullOrEmpty(id)) continue;

                    cache.Upsert(id, type, element.ToString(Newtonsoft.Json.Formatting.None));
                    fetched++;
                }

                onType?.Invoke(type, elements.Count);
            }

            cache.SetCursor(tip.Head, tip.Cursor, tip.Epoch, NowIso());

            return new OWSyncResult
            {
                Fetched = fetched,
                Cursor = tip.Head,
                WasRebaselined = true,
                RebaselineReason = "Full baseline requested.",
            };
        }

        /// <summary>
        /// Applies everything that changed since the cache's cursor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Resumes from the server's own cursor (<see cref="OWWorldCache.CursorToken"/>), passed back
        /// as is, never one built from a number: a guest key's cursor carries its view epoch, and the
        /// feed answers a bare number above 0 from a guest with 409 <c>resync_required</c>. A cache
        /// older than the token resumes from its bare <see cref="OWWorldCache.Cursor"/>, as before.
        /// </para>
        /// <para>
        /// Re-baselines instead of updating when the cache has no cursor, when the persisted
        /// cursor is AHEAD of the server's head, or when the feed answers 409
        /// <c>resync_required</c>. A cursor ahead of head means the server was restored from a
        /// backup: our cursor points at changes that no longer exist, so every incremental sync
        /// from here would silently skip real data. A cache that is confidently wrong is worse
        /// than one that admits it needs rebuilding. That walk keeps what the cache held and the
        /// restored server no longer names (<see cref="OWSyncResult.Kept"/>): a restore rewinds the
        /// server, not what this copy read.
        /// </para>
        /// <para>
        /// A resync means the key's view changed (a guest's or a contributor's, or its role), and
        /// a guest is never sent deletes, so the world is walked again from the start. What the
        /// cache held that the new walk does not name follows the 409's reason:
        /// <c>view</c> drops it; <c>role</c>, or no reason, keeps it, since sharing is forward-only
        /// and what a key read is out of its view now, not gone. <see cref="OWSyncResult.ResyncReason"/>
        /// and <see cref="OWSyncResult.Kept"/> say which happened. A reset whose walk fails puts the
        /// cache and its cursor back as they were: a reset never empties a copy on its own.
        /// </para>
        /// <para>
        /// World-meta edits do NOT appear in <c>/changes</c> and do not bump <c>change_seq</c>.
        /// Poll <c>GetWorldAsync</c> separately if meta freshness matters.
        /// </para>
        /// </remarks>
        public static async Task<OWSyncResult> IncrementalAsync(
            OWClient client, OWWorldCache cache, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            GuardWritable(cache);

            if (cache.Cursor <= 0 && string.IsNullOrEmpty(cache.CursorToken))
            {
                return await BaselineAsync(client, cache, ct: ct).ConfigureAwait(false);
            }

            var tip = await client.ChangesAsync(headOnly: true, ct: ct).ConfigureAwait(false);

            if (cache.Cursor > tip.Head)
            {
                // Capture BEFORE the rebaseline: BaselineAsync clears the cache (cursor -> 0) and
                // then sets it to the new tip, so reading cache.Cursor afterwards reports the tip
                // and the message renders "Cursor 100 was ahead of server head 100". The rewound
                // value is the only number an operator actually needs here, and it is the one that
                // is gone by the time the string is built.
                var staleCursor = cache.Cursor;

                // Walked again and KEPT: a restore rewinds the server, not what this copy read (a reset
                // never empties a copy, Titus's ruling).
                var result = await RewalkAsync(client, cache, keep: true, ct).ConfigureAwait(false);
                result.RebaselineReason =
                    $"Cursor {staleCursor} was ahead of server head {tip.Head} -- the server was "
                    + "likely restored from a backup. Walked again rather than trusting the cursor, and kept "
                    + $"{result.Kept} element(s) the new walk no longer names.";
                return result;
            }

            // Current only when the epoch stands too: a guest's view can change with head unmoved.
            if (cache.Cursor == tip.Head && cache.Epoch == tip.Epoch)
            {
                return new OWSyncResult { Cursor = cache.Cursor };
            }

            var fetched = 0;
            var removed = 0;
            var since = string.IsNullOrEmpty(cache.CursorToken)
                ? cache.Cursor.ToString(CultureInfo.InvariantCulture)
                : cache.CursorToken;
            long highest = cache.Cursor;
            var epoch = tip.Epoch;
            var walkedToTheEnd = false;

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var page = await client.ChangesAsync(since, ct: ct).ConfigureAwait(false);
                    if (page?.Changes == null) break;

                    // A page that says "no more" is the only honest end of the feed.
                    if (!page.HasMore) walkedToTheEnd = true;
                    epoch = page.Epoch;

                    foreach (var change in page.Changes)
                    {
                        // A WATERMARK MAXIMUM ONLY -- never an identity or a dedupe key. The published
                        // ruling table (change-seq-not-unique) records that a bulk import stamps every
                        // element it creates with a single seq: one measured world had all 164 elements
                        // at seq 1. Anything keying on seq breaks on exactly that world. Taking the max
                        // is safe because it asks "how far have we read", not "which element is this".
                        if (change.ChangeSeq > highest) highest = change.ChangeSeq;

                        if (IsDelete(change.Op))
                        {
                            if (cache.Remove(change.Id)) removed++;
                            continue;
                        }

                        // An upsert carries the element whole. The GET is only for an op that doesn't.
                        var element = change.Element ?? await client
                            .GetAsync<JObject>(change.Type, change.Id, ct)
                            .ConfigureAwait(false);

                        if (element == null) continue;

                        cache.Upsert(change.Id, change.Type, element.ToString(Newtonsoft.Json.Formatting.None));
                        fetched++;
                    }

                    // The server's cursor is the resume point, as is; keep the last one it gave.
                    if (!string.IsNullOrEmpty(page.Cursor)) since = page.Cursor;
                    if (!page.HasMore || string.IsNullOrEmpty(page.Cursor)) break;
                }
            }
            catch (OWApiError e) when (e.IsResyncRequired)
            {
                return await ResetAsync(client, cache, e.ResyncReason, ct).ConfigureAwait(false);
            }

            // Only claim the tip if we actually reached the end of the feed. If the loop broke on
            // the cursor-less "more" guard above, changes below tip.Head exist that we never
            // applied -- recording tip.Head anyway would put them permanently below the cursor and
            // lose them silently, with a cache that looks current. This is the same hazard the
            // baseline's read-tip-before-walk ordering exists to prevent, arriving on the other
            // path. On an early break the change_seq stays where it was (the highest one read is
            // no claim: a bulk import puts many changes at one seq, and the rest of it may be
            // unread), and the next sync resumes from the last cursor the server gave.
            cache.SetCursor(walkedToTheEnd ? Math.Max(highest, tip.Head) : cache.Cursor, since, epoch, NowIso());

            return new OWSyncResult
            {
                Fetched = fetched,
                Removed = removed,
                Cursor = cache.Cursor,
            };
        }

        /// <summary>
        /// The answer to a 409 <c>resync_required</c>: walk the world again from the start, then settle what the cache
        /// held that the new walk does not name by the server's reason (the published changes doc,
        /// "What a reset keeps"). <c>view</c>: something left this key's view, so it goes. <c>role</c>, or no reason (a
        /// server older than 2026-10-20): this key's own role or membership moved, and what it read is out of view now,
        /// not gone (sharing is forward-only), so it stays. A reset never empties a copy on its own: when the walk
        /// fails, the cache and its cursor are put back as they were and the failure is thrown.
        /// </summary>
        private static async Task<OWSyncResult> ResetAsync(
            OWClient client, OWWorldCache cache, string reason, CancellationToken ct)
        {
            var keep = reason != "view";
            var result = await RewalkAsync(client, cache, keep, ct).ConfigureAwait(false);

            result.ResyncReason = reason;
            result.RebaselineReason = keep
                ? "The server asked for a full resync (409 resync_required, "
                  + (reason == null ? "no reason given, kept as for role" : "reason role: this key's role or membership moved")
                  + $"). Walked again from the start and kept {result.Kept} element(s) the new walk no longer names: "
                  + "out of view now, not gone."
                : "The server asked for a full resync (409 resync_required, reason view): something left this key's "
                  + "view. Walked again from the start and dropped what the new walk no longer names.";
            return result;
        }

        /// <summary>
        /// A baseline that puts back, when <paramref name="keep"/>, every element the cache held that the new walk does not
        /// name (counted in <see cref="OWSyncResult.Kept"/>); and that, when the walk throws, restores the cache and its
        /// cursor as they were and rethrows. Both resets (a 409, a cursor ahead of head) go through it.
        /// </summary>
        private static async Task<OWSyncResult> RewalkAsync(
            OWClient client, OWWorldCache cache, bool keep, CancellationToken ct)
        {
            var held = cache.Snapshot();
            var heldCursor = cache.Cursor;
            var heldToken = cache.CursorToken;
            var heldEpoch = cache.Epoch;
            var heldAt = cache.SyncedAt;

            OWSyncResult result;
            try
            {
                result = await BaselineAsync(client, cache, ct: ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                cache.Clear();
                foreach (var element in held) cache.Upsert(element.Id, element.TypeSlug, element.RawJson);
                cache.SetCursor(heldCursor, heldToken, heldEpoch, heldAt);
                throw;
            }

            if (keep)
            {
                foreach (var element in held)
                {
                    if (cache.Contains(element.Id)) continue;

                    cache.Upsert(element.Id, element.TypeSlug, element.RawJson);
                    result.Kept++;
                }
            }

            return result;
        }

        /// <summary>
        /// Cheap check for whether a sync would do anything, without fetching bodies.
        /// </summary>
        /// <remarks>
        /// True in BOTH directions on purpose. A head above our cursor means new changes; a head
        /// BELOW it means the server was restored from a backup and
        /// <see cref="IncrementalAsync"/> would re-baseline. A moved epoch means a guest key's view
        /// changed, and the sync would rebuild. All are pending work, so a caller polling this and
        /// syncing on true behaves correctly in each case -- but note that "true" here does not
        /// mean "new content exists", it means "a sync is not a no-op".
        /// </remarks>
        public static async Task<bool> HasChangesAsync(
            OWClient client, OWWorldCache cache, CancellationToken ct = default)
        {
            var tip = await client.ChangesAsync(headOnly: true, ct: ct).ConfigureAwait(false);
            return tip.Head != cache.Cursor || tip.Epoch != cache.Epoch;
        }

        private static bool IsDelete(string op)
            => string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase)
               || string.Equals(op, "remove", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Refuses to sync into a cache that declared itself frozen.
        /// </summary>
        /// <remarks>
        /// <c>writable: false</c> is advisory in the folder spec, and consumers MUST honour it. A
        /// snapshot exists to stay what it was; syncing live data into one destroys the thing it
        /// was made to preserve.
        /// </remarks>
        private static void GuardWritable(OWWorldCache cache)
        {
            if (!cache.ReadOnly) return;

            throw new InvalidOperationException(
                $"Cache '{cache.Key}' is marked read-only (a frozen snapshot). Syncing would "
                + "overwrite the capture it exists to preserve.");
        }

        private static string NowIso() => DateTime.UtcNow.ToString("o");
    }
}
