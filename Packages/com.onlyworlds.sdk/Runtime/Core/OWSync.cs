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
        /// from here would silently skip real data. A resync means the key's view changed (a
        /// guest's, or its role), and a guest is never sent deletes, so only a rebuild drops what
        /// it can no longer see. A cache that is confidently wrong is worse than one that admits
        /// it needs rebuilding.
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

                var result = await BaselineAsync(client, cache, ct: ct).ConfigureAwait(false);
                result.RebaselineReason =
                    $"Cursor {staleCursor} was ahead of server head {tip.Head} -- the server was "
                    + "likely restored from a backup. Rebuilt rather than trusting the cursor.";
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
                var result = await BaselineAsync(client, cache, ct: ct).ConfigureAwait(false);
                result.RebaselineReason =
                    "The server asked for a full resync (409 resync_required): this key's view of the "
                    + "world changed, or its role did. Rebuilt from the start.";
                return result;
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
