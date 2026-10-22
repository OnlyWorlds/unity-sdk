using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    public class OWSyncTests
    {
        private OWWorldCache _cache;
        private FakeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _cache = ScriptableObject.CreateInstance<OWWorldCache>();
            _cache.Initialize(OWWorldKey.FromApi("w-1"), "Test World");
            _transport = new FakeTransport();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_cache);

        private OWClient Client() => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "1234",
            Transport = _transport,
            BaseUrl = "https://example.test/api/v2",
        });

        // The shapes below are keel's (core/changes.py compute_head, compute_changes): an owner's tip
        // cursor is the bare change_seq as a number; a guest's is "seq:<max uuid>:epoch", with the
        // epoch beside it; a page's cursor is "seq:id" (a guest's "seq:id:epoch").
        private static string Head(long head) =>
            $@"{{""cursor"":{head},""head"":{head},""changes"":[],""has_more"":false,""epoch"":null}}";

        private const string MaxUuid = "ffffffff-ffff-ffff-ffff-ffffffffffff";

        private static string GuestTip(long head, long epoch) => $"{head}:{MaxUuid}:{epoch}";

        private static string GuestHead(long head, long epoch) =>
            $@"{{""cursor"":""{GuestTip(head, epoch)}"",""head"":{head},""changes"":[],""has_more"":false,""epoch"":{epoch}}}";

        private const string IdA = "069620b9-0794-708d-8000-00000000000a";
        private const string IdB = "069620b9-0794-708d-8000-00000000000b";

        private static string Upsert(string id, long seq, string name) =>
            $@"{{""op"":""upsert"",""type"":""character"",""id"":""{id}"",""change_seq"":{seq},""updated_at"":""2026-10-16T00:00:00Z"",
                ""element"":{{""id"":""{id}"",""name"":""{name}"",""level"":3}}}}";

        private static string EmptyPage => @"{""data"":[],""has_more"":false,""next_cursor"":null}";

        private void EnqueueBaseline(string head)
        {
            _transport.Bodies.Enqueue(head);
            for (var i = 0; i < OWSync.ElementTypes.Length; i++) _transport.Bodies.Enqueue(EmptyPage);
        }

        private static string Since(OWRequest call)
        {
            foreach (var pair in new System.Uri(call.Url).Query.TrimStart('?').Split('&'))
            {
                if (pair.StartsWith("since=")) return System.Uri.UnescapeDataString(pair.Substring(6));
            }

            return null;
        }

        // -- Baseline ---------------------------------------------------------

        [Test]
        public async Task Baseline_ReadsTheTipBeforeFetching()
        {
            // Order matters: taking the tip AFTER the walk means any change landing during it sits
            // silently below the recorded cursor and never syncs again.
            _transport.Bodies.Enqueue(Head(500));
            for (var i = 0; i < OWSync.ElementTypes.Length; i++) _transport.Bodies.Enqueue(EmptyPage);

            var result = await OWSync.BaselineAsync(Client(), _cache);

            StringAssert.Contains("head=true", _transport.Calls[0].Url,
                "The tip check must be the FIRST request.");
            Assert.AreEqual(500, result.Cursor);
            Assert.AreEqual(500, _cache.Cursor);
        }

        [Test]
        public async Task Baseline_WalksEveryElementType()
        {
            _transport.Bodies.Enqueue(Head(1));
            for (var i = 0; i < OWSync.ElementTypes.Length; i++) _transport.Bodies.Enqueue(EmptyPage);

            await OWSync.BaselineAsync(Client(), _cache);

            // 1 tip check + one list call per type.
            Assert.AreEqual(OWSync.ElementTypes.Length + 1, _transport.Calls.Count);
            Assert.AreEqual(22, OWSync.ElementTypes.Length, "The standard has 22 element types.");
        }

        [Test]
        public async Task Baseline_StoresElementsAndClearsFirst()
        {
            _cache.Upsert("stale", "character", @"{""id"":""stale"",""name"":""Old""}");

            _transport.Bodies.Enqueue(Head(10));
            foreach (var type in OWSync.ElementTypes)
            {
                _transport.Bodies.Enqueue(type == "character"
                    ? @"{""data"":[{""id"":""c1"",""name"":""Fresh""}],""has_more"":false}"
                    : EmptyPage);
            }

            var result = await OWSync.BaselineAsync(Client(), _cache);

            Assert.AreEqual(1, result.Fetched);
            Assert.IsTrue(result.WasRebaselined);
            Assert.IsFalse(_cache.Contains("stale"), "A baseline replaces; it does not merge.");
            Assert.AreEqual("Fresh", _cache.Get<OWCharacter>("c1").Name);
        }

        [Test]
        public async Task Baseline_StoresTheTipsOwnCursorAndEpoch()
        {
            EnqueueBaseline(GuestHead(40, 3));

            await OWSync.BaselineAsync(Client(), _cache);

            Assert.AreEqual(40, _cache.Cursor);
            Assert.AreEqual(GuestTip(40, 3), _cache.CursorToken,
                "The next sync resumes from the server's cursor; a guest's carries its epoch.");
            Assert.AreEqual(3, _cache.Epoch);
        }

        // -- Incremental ------------------------------------------------------

        [Test]
        public async Task Incremental_WithNoCursor_FallsBackToBaseline()
        {
            _transport.Bodies.Enqueue(Head(5));
            for (var i = 0; i < OWSync.ElementTypes.Length; i++) _transport.Bodies.Enqueue(EmptyPage);

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined, "Nothing cached yet means there is nothing to increment from.");
        }

        [Test]
        public async Task Incremental_AtTheTip_DoesNothing()
        {
            _cache.SetCursor(100, "t");
            _transport.Bodies.Enqueue(Head(100));

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsFalse(result.ChangedAnything);
            Assert.AreEqual(1, _transport.Calls.Count, "A no-op sync costs exactly one tip check.");
        }

        [Test]
        public async Task Incremental_AppliesUpserts_FromTheElementTheyCarry()
        {
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(101));
            _transport.Bodies.Enqueue(
                $@"{{""cursor"":""101:{IdA}"",""has_more"":false,""head"":101,""epoch"":null,
                   ""changes"":[{Upsert(IdA, 101, "Updated")}]}}");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(1, result.Fetched);
            Assert.AreEqual("Updated", _cache.Get<OWCharacter>(IdA).Name);
            Assert.AreEqual(3, _cache.Get<OWCharacter>(IdA).Level.Value);
            Assert.AreEqual(2, _transport.Calls.Count,
                "An upsert carries the element whole: the tip and one page, no GET per element.");
        }

        [Test]
        public async Task Incremental_FetchesTheBody_OnlyWhenAnOpHasNone()
        {
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(101));
            _transport.Bodies.Enqueue(
                $@"{{""cursor"":""101:{IdA}"",""has_more"":false,""head"":101,""epoch"":null,
                   ""changes"":[{{""op"":""upsert"",""type"":""character"",""id"":""{IdA}"",""change_seq"":101}}]}}");
            _transport.Bodies.Enqueue($@"{{""id"":""{IdA}"",""name"":""Fetched""}}");

            await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual("Fetched", _cache.Get<OWCharacter>(IdA).Name);
            StringAssert.EndsWith($"/character/{IdA}/", _transport.Last.Url);
        }

        [Test]
        public async Task Incremental_ResumesFromTheStoredCursor_NotFromTheSeq()
        {
            var token = $"100:{IdA}:3";
            _cache.SetCursor(100, token, 3, "t");

            _transport.Bodies.Enqueue(GuestHead(101, 3));
            _transport.Bodies.Enqueue(
                $@"{{""cursor"":""101:{IdB}:3"",""has_more"":false,""head"":101,""epoch"":3,
                   ""changes"":[{Upsert(IdB, 101, "Seen")}]}}");

            await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(token, Since(_transport.Calls[1]),
                "The cursor goes back as the server sent it; a bare 100 from a guest is a 409.");
            Assert.AreEqual($"101:{IdB}:3", _cache.CursorToken, "The last page's cursor is the next resume point.");
            Assert.AreEqual(101, _cache.Cursor);
        }

        [Test]
        public async Task Incremental_ACacheWithNoToken_ResumesFromItsBareSeq()
        {
            // A cache synced before the token was kept: it resumes exactly as it did.
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(101));
            _transport.Bodies.Enqueue(
                $@"{{""cursor"":""101:{IdA}"",""has_more"":false,""head"":101,""epoch"":null,
                   ""changes"":[{Upsert(IdA, 101, "A")}]}}");

            await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual("100", Since(_transport.Calls[1]));
            Assert.AreEqual($"101:{IdA}", _cache.CursorToken);
        }

        [Test]
        public async Task Incremental_AfterBaseliningAnEmptyWorld_IsANoOp()
        {
            // Head 0 used to read as "no cursor", so an empty world re-baselined on every sync.
            EnqueueBaseline(Head(0));
            await OWSync.BaselineAsync(Client(), _cache);
            _transport.Calls.Clear();

            _transport.Bodies.Enqueue(Head(0));
            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsFalse(result.ChangedAnything);
            Assert.AreEqual(1, _transport.Calls.Count);
        }

        // -- The resync rule --------------------------------------------------

        // What a reset keeps (keel PR 104, #421; the published changes doc, "What a reset keeps"): a 409 walks the
        // world again from the start; what the copy holds that the new walk does not name is KEPT on `role` or on no
        // reason (sharing is forward-only: out of view, not gone) and DROPPED on `view`. A reset never empties a copy
        // on its own. The vectors are the crew's room feed's (Workshops parlor/feed.test.ts: view drops, role keeps),
        // with the published doc's no-reason case; the npm SDK and the Python package state the rule but sync nothing.

        private static string Resync(string reason) =>
            @"{""error"":{""type"":""invalid_request"",""code"":""resync_required"",""param"":""since"",""message"":""m"""
            + (reason == null ? "" : @",""reason"":""" + reason + @"""") + "}}";

        /// <summary>A tip, then a 409 on the walk from the cache's cursor, then a baseline naming only <paramref name="named"/>.</summary>
        private void EnqueueReset(string tip, string resync, string named)
        {
            _transport.Bodies.Enqueue(tip);
            _transport.Statuses.Enqueue(200);
            _transport.Bodies.Enqueue(resync);
            _transport.Statuses.Enqueue(409);
            _transport.Bodies.Enqueue(tip);
            _transport.Statuses.Enqueue(200);
            foreach (var type in OWSync.ElementTypes)
            {
                _transport.Bodies.Enqueue(type == "character"
                    ? $@"{{""data"":[{{""id"":""{named}"",""name"":""Fresh""}}],""has_more"":false}}"
                    : EmptyPage);
                _transport.Statuses.Enqueue(200);
            }
        }

        /// <summary>A copy holding two Characters: one the next walk names again (a), one it no longer names (b).</summary>
        private void CacheTwo(long cursor, string token, long? epoch)
        {
            _cache.Upsert("a", "character", @"{""id"":""a"",""name"":""Old A""}");
            _cache.Upsert("b", "character", @"{""id"":""b"",""name"":""Out of view""}");
            _cache.SetCursor(cursor, token, epoch, "t");
        }

        [Test]
        public async Task Incremental_A409ForTheView_DropsWhatTheNewWalkDoesNotName()
        {
            CacheTwo(100, GuestTip(100, 2), 2);
            EnqueueReset(GuestHead(101, 3), Resync("view"), "a");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined);
            Assert.AreEqual("view", result.ResyncReason);
            Assert.AreEqual(0, result.Kept);
            Assert.IsTrue(_cache.Contains("a"));
            Assert.AreEqual("Fresh", _cache.Get<OWCharacter>("a").Name, "the walk's body replaces the old one");
            Assert.IsFalse(_cache.Contains("b"), "view: something left this key's view, so it goes");
            Assert.AreEqual(GuestTip(101, 3), _cache.CursorToken);
            Assert.AreEqual(3, _cache.Epoch);
        }

        [Test]
        public async Task Incremental_A409ForTheRole_KeepsWhatTheNewWalkDoesNotName()
        {
            CacheTwo(100, GuestTip(100, 2), 2);
            EnqueueReset(GuestHead(101, 3), Resync("role"), "a");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined);
            Assert.AreEqual("role", result.ResyncReason);
            Assert.AreEqual(1, result.Kept);
            Assert.AreEqual("Fresh", _cache.Get<OWCharacter>("a").Name, "what the walk names, it refreshes");
            Assert.IsTrue(_cache.Contains("b"), "role: out of this key's view now, not gone; sharing is forward-only");
            Assert.AreEqual("Out of view", _cache.Get<OWCharacter>("b").Name);
            Assert.AreEqual(GuestTip(101, 3), _cache.CursorToken, "the cursor is the new walk's");
            Assert.AreEqual(3, _cache.Epoch);
        }

        [Test]
        public async Task Incremental_A409WithNoReason_IsKeptLikeRole()
        {
            // A guest key with a cache synced before the token was kept: keel answers its bare since=100 with 409
            // resync_required (core/changes.py parse_cursor). A server older than 2026-10-20 sends no reason.
            CacheTwo(100, null, null);
            EnqueueReset(GuestHead(101, 2), Resync(null), "a");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined);
            StringAssert.Contains("resync_required", result.RebaselineReason);
            Assert.IsNull(result.ResyncReason);
            Assert.AreEqual(1, result.Kept);
            Assert.IsTrue(_cache.Contains("b"), "no reason: kept, as for role; a reset never empties a copy");
            Assert.AreEqual(GuestTip(101, 2), _cache.CursorToken);
            Assert.AreEqual(2, _cache.Epoch);
        }

        [Test]
        public async Task Incremental_AtTheHead_ButAMovedEpoch_IsNotCurrent()
        {
            // A Character given or taken moves a guest's epoch with head unmoved.
            CacheTwo(100, GuestTip(100, 3), 3);
            EnqueueReset(GuestHead(100, 4), Resync("view"), "a");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined, "Same head, another epoch: the walk answers 409 and the cache rebuilds.");
            Assert.AreEqual(4, _cache.Epoch);
            Assert.IsFalse(_cache.Contains("b"));
        }

        [Test]
        public void Incremental_AResetWhoseWalkFails_LeavesTheCopyAndItsCursorAsTheyWere()
        {
            // A tool never empties a copy on its own: a reset that cannot finish its walk changes nothing.
            CacheTwo(100, GuestTip(100, 2), 2);
            _transport.Bodies.Enqueue(GuestHead(101, 3));
            _transport.Statuses.Enqueue(200);
            _transport.Bodies.Enqueue(Resync("view"));
            _transport.Statuses.Enqueue(409);
            _transport.Bodies.Enqueue(GuestHead(101, 3));
            _transport.Statuses.Enqueue(200);
            _transport.Bodies.Enqueue(@"{""error"":{""type"":""api_error"",""code"":""server_busy"",""message"":""busy""}}");
            _transport.Statuses.Enqueue(503);

            Assert.ThrowsAsync<OWApiError>(() => OWSync.IncrementalAsync(Client(), _cache));

            Assert.AreEqual(2, _cache.Count);
            Assert.AreEqual("Old A", _cache.Get<OWCharacter>("a").Name);
            Assert.IsTrue(_cache.Contains("b"));
            Assert.AreEqual(100, _cache.Cursor);
            Assert.AreEqual(GuestTip(100, 2), _cache.CursorToken);
            Assert.AreEqual(2, _cache.Epoch);
        }

        [Test]
        public async Task Incremental_ADeleteRowForAHide_IsDropped()
        {
            // A contributor learns of a hide from a delete op with reason hidden: dropped, as for view.
            _cache.Upsert("c1", "character", @"{""id"":""c1"",""name"":""Hidden now""}");
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(102));
            _transport.Bodies.Enqueue(
                @"{""cursor"":""c-1"",""has_more"":false,""head"":102,
                   ""changes"":[{""op"":""delete"",""type"":""character"",""id"":""c1"",""change_seq"":102,""reason"":""hidden""}]}");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(1, result.Removed);
            Assert.IsFalse(_cache.Contains("c1"));
        }

        [Test]
        public async Task Incremental_AppliesDeletes()
        {
            _cache.Upsert("c1", "character", @"{""id"":""c1"",""name"":""Doomed""}");
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(102));
            _transport.Bodies.Enqueue(
                @"{""cursor"":""c-1"",""has_more"":false,""head"":102,
                   ""changes"":[{""op"":""delete"",""type"":""character"",""id"":""c1"",""change_seq"":102}]}");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(1, result.Removed);
            Assert.IsFalse(_cache.Contains("c1"));
        }

        // -- Paging the changes feed ------------------------------------------

        [Test]
        public async Task Incremental_DoesNotClaimTheTip_WhenTheFeedBreaksEarly()
        {
            // A server that says "more changes exist" but hands back no cursor to reach them. The
            // client guards the infinite loop by breaking -- correct -- but breaking means changes
            // below the tip were never applied. Recording tip.Head anyway would put them
            // permanently below the cursor: lost silently, with a cache that looks current.
            _cache.SetCursor(10, "t");

            _transport.Bodies.Enqueue(Head(900));   // the tip is far ahead
            _transport.Bodies.Enqueue(
                $@"{{""changes"":[{Upsert(IdA, 11, "One")}],""has_more"":true,""cursor"":""11:{IdA}"",""head"":900,""epoch"":null}}");
            _transport.Bodies.Enqueue(
                $@"{{""changes"":[{Upsert(IdB, 12, "Two")}],""has_more"":true,""cursor"":null,""head"":900,""epoch"":null}}");   // more exists, no way to reach it

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(2, result.Fetched);
            Assert.AreEqual($"11:{IdA}", _cache.CursorToken,
                "The next sync resumes from the last cursor the server gave, and re-reads what came after it.");
            Assert.AreEqual(10, _cache.Cursor,
                "The seq stays put: 12 is no claim (a bulk import puts many changes at one seq), and "
                + "claiming the tip would silently lose every change between 12 and 900.");
        }

        [Test]
        public async Task Incremental_WalksEveryPage_AndTakesTheTipWhenItFinishes()
        {
            // The happy multi-page path, which had no coverage at all: two pages, both applied,
            // and only then is the tip a legitimate claim.
            _cache.SetCursor(10, "t");

            // NB: the changes envelope names its cursor "cursor", not "next_cursor" -- that is the
            // ELEMENT list envelope's field. A fixture using the wrong name hands the client a null
            // cursor, it stops after one page, and the test fails for a reason that has nothing to
            // do with what it is testing. (It did, first run.)
            _transport.Bodies.Enqueue(Head(30));
            _transport.Bodies.Enqueue(
                $@"{{""changes"":[{Upsert(IdA, 11, "One")}],""has_more"":true,""cursor"":""11:{IdA}"",""head"":30,""epoch"":null}}");
            _transport.Bodies.Enqueue(
                $@"{{""changes"":[{Upsert(IdB, 12, "Two")}],""has_more"":false,""cursor"":""12:{IdB}"",""head"":30,""epoch"":null}}");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(2, result.Fetched, "Both pages must be applied.");
            Assert.IsTrue(_cache.Contains(IdA));
            Assert.IsTrue(_cache.Contains(IdB));
            Assert.AreEqual($"11:{IdA}", Since(_transport.Calls[2]), "Page two starts at page one's cursor, as is.");
            Assert.AreEqual(30, _cache.Cursor,
                "Having reached the end of the feed, the tip is the correct cursor.");
            Assert.AreEqual($"12:{IdB}", _cache.CursorToken);
        }

        // -- The rewind rule --------------------------------------------------

        [Test]
        public async Task Incremental_RebaselinesWhenCursorIsAheadOfHead_AndKeepsWhatTheWalkDoesNotName()
        {
            // A cursor ahead of head means the server was restored from a backup: our cursor points
            // at changes that no longer exist, so every future incremental would skip real data. The
            // world is walked again, and what the copy holds that the walk does not name is kept: a
            // reset never empties a copy (the Captain's ruling, keel PR 104; the room feed's "a head
            // below ours walks again from zero and keeps what it no longer names", parlor/feed.test.ts).
            _cache.Upsert("stale", "character", @"{""id"":""stale"",""name"":""Old""}");
            _cache.SetCursor(9999, "t");

            _transport.Bodies.Enqueue(Head(100));      // tip check -- head is BEHIND our cursor
            _transport.Bodies.Enqueue(Head(100));      // baseline's own tip check
            for (var i = 0; i < OWSync.ElementTypes.Length; i++) _transport.Bodies.Enqueue(EmptyPage);

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined);
            Assert.AreEqual(100, _cache.Cursor, "The cursor must come down to the server's truth.");
            Assert.IsTrue(_cache.Contains("stale"), "a restore rewinds the server, not what this copy read");
            Assert.AreEqual(1, result.Kept);
            Assert.IsNull(result.ResyncReason, "no 409 was involved");

            // Assert the NUMBERS, not just the prose. Checking only the static suffix passes
            // whatever the interpolation says -- and it did: this message used to read
            // "Cursor 100 was ahead of server head 100" because the cursor was re-read after the
            // rebaseline had already overwritten it. The rewound value is the whole point of the
            // diagnostic.
            StringAssert.Contains("restored from a backup", result.RebaselineReason);
            StringAssert.Contains("9999", result.RebaselineReason,
                "The diagnostic must report the cursor we actually rewound FROM.");
            StringAssert.Contains("head 100", result.RebaselineReason,
                "...and the head we rewound TO.");
        }

        // -- Frozen snapshots -------------------------------------------------

        [Test]
        public void Sync_RefusesAReadOnlyCache()
        {
            var snapshot = ScriptableObject.CreateInstance<OWWorldCache>();
            snapshot.Initialize(OWWorldKey.FromFolder("w-1", "C:/ch01"), "Chapter 1", readOnly: true);

            var ex = Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
                await OWSync.IncrementalAsync(Client(), snapshot));

            StringAssert.Contains("read-only", ex.Message);
            Assert.AreEqual(0, _transport.Calls.Count, "It must refuse before touching the network.");

            Object.DestroyImmediate(snapshot);
        }

        // -- Cheap tip check --------------------------------------------------

        [Test]
        public async Task HasChanges_ComparesHeadToCursor()
        {
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(100));
            Assert.IsFalse(await OWSync.HasChangesAsync(Client(), _cache));

            _transport.Bodies.Enqueue(Head(101));
            Assert.IsTrue(await OWSync.HasChangesAsync(Client(), _cache));
        }

        [Test]
        public async Task HasChanges_SeesAGuestsMovedEpoch()
        {
            _cache.SetCursor(100, GuestTip(100, 3), 3, "t");

            _transport.Bodies.Enqueue(GuestHead(100, 3));
            Assert.IsFalse(await OWSync.HasChangesAsync(Client(), _cache));

            _transport.Bodies.Enqueue(GuestHead(100, 4));
            Assert.IsTrue(await OWSync.HasChangesAsync(Client(), _cache), "Head unmoved, view changed: a sync is due.");
        }
    }
}
