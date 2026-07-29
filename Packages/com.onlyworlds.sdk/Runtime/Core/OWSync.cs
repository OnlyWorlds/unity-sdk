using System;
using System.Collections.Generic;
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
        /// Uses the per-type list endpoints rather than <c>/changes</c>. <c>/changes</c> returns
        /// change RECORDS, not element bodies -- lossless as a feed, useless as an exporter. The
        /// cursor is read separately from <c>head</c> so the next incremental sync knows where the
        /// baseline sits.
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

            cache.SetCursor(tip.Head, NowIso());

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
        /// Re-baselines instead of updating when the cache has no cursor, or when the persisted
        /// cursor is AHEAD of the server's head. The second case means the server was restored from
        /// a backup: our cursor points at changes that no longer exist, so every incremental sync
        /// from here would silently skip real data. A cache that is confidently wrong is worse than
        /// one that admits it needs rebuilding.
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

            if (cache.Cursor <= 0)
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

            if (cache.Cursor == tip.Head)
            {
                return new OWSyncResult { Cursor = cache.Cursor };
            }

            var fetched = 0;
            var removed = 0;
            var cursor = cache.Cursor.ToString();
            long highest = cache.Cursor;
            var walkedToTheEnd = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var page = await client.ChangesAsync(cursor, ct: ct).ConfigureAwait(false);
                if (page?.Changes == null) break;

                // A page that says "no more" is the only honest end of the feed.
                if (!page.HasMore) walkedToTheEnd = true;

                foreach (var change in page.Changes)
                {
                    if (change.ChangeSeq > highest) highest = change.ChangeSeq;

                    if (IsDelete(change.Op))
                    {
                        if (cache.Remove(change.Id)) removed++;
                        continue;
                    }

                    // A change record carries identity, not a body -- fetch the element itself.
                    var element = await client
                        .GetAsync<JObject>(change.Type, change.Id, ct)
                        .ConfigureAwait(false);

                    if (element == null) continue;

                    cache.Upsert(change.Id, change.Type, element.ToString(Newtonsoft.Json.Formatting.None));
                    fetched++;
                }

                if (!page.HasMore || string.IsNullOrEmpty(page.Cursor)) break;
                cursor = page.Cursor;
            }

            // Only claim the tip if we actually reached the end of the feed. If the loop broke on
            // the cursor-less "more" guard above, changes below tip.Head exist that we never
            // applied -- recording tip.Head anyway would put them permanently below the cursor and
            // lose them silently, with a cache that looks current. This is the same hazard the
            // baseline's read-tip-before-walk ordering exists to prevent, arriving on the other
            // path. On an early break the last change we actually applied is the only safe claim.
            cache.SetCursor(walkedToTheEnd ? Math.Max(highest, tip.Head) : highest, NowIso());

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
        /// <see cref="IncrementalAsync"/> would re-baseline. Both are pending work, so a caller
        /// polling this and syncing on true behaves correctly in either case -- but note that
        /// "true" here does not mean "new content exists", it means "a sync is not a no-op".
        /// </remarks>
        public static async Task<bool> HasChangesAsync(
            OWClient client, OWWorldCache cache, CancellationToken ct = default)
        {
            var tip = await client.ChangesAsync(headOnly: true, ct: ct).ConfigureAwait(false);
            return tip.Head != cache.Cursor;
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
