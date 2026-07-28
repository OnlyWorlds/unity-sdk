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

        private static string Head(long head) => $@"{{""head"":{head},""changes"":[],""has_more"":false}}";

        private static string EmptyPage => @"{""data"":[],""has_more"":false,""next_cursor"":null}";

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
        public async Task Incremental_AppliesUpsertsByFetchingBodies()
        {
            _cache.SetCursor(100, "t");

            _transport.Bodies.Enqueue(Head(101));
            _transport.Bodies.Enqueue(
                @"{""cursor"":""c-1"",""has_more"":false,""head"":101,
                   ""changes"":[{""op"":""upsert"",""type"":""character"",""id"":""c1"",""change_seq"":101}]}");
            _transport.Bodies.Enqueue(@"{""id"":""c1"",""name"":""Updated"",""level"":3}");

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.AreEqual(1, result.Fetched);
            Assert.AreEqual("Updated", _cache.Get<OWCharacter>("c1").Name);
            Assert.AreEqual(3, _cache.Get<OWCharacter>("c1").Level.Value,
                "A change record carries identity only -- the body must be fetched.");
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

        // -- The rewind rule --------------------------------------------------

        [Test]
        public async Task Incremental_RebaselinesWhenCursorIsAheadOfHead()
        {
            // A cursor ahead of head means the server was restored from a backup: our cursor points
            // at changes that no longer exist, so every future incremental would skip real data.
            _cache.Upsert("stale", "character", @"{""id"":""stale"",""name"":""Old""}");
            _cache.SetCursor(9999, "t");

            _transport.Bodies.Enqueue(Head(100));      // tip check -- head is BEHIND our cursor
            _transport.Bodies.Enqueue(Head(100));      // baseline's own tip check
            for (var i = 0; i < OWSync.ElementTypes.Length; i++) _transport.Bodies.Enqueue(EmptyPage);

            var result = await OWSync.IncrementalAsync(Client(), _cache);

            Assert.IsTrue(result.WasRebaselined);
            StringAssert.Contains("restored from a backup", result.RebaselineReason);
            Assert.AreEqual(100, _cache.Cursor, "The cursor must come down to the server's truth.");
            Assert.IsFalse(_cache.Contains("stale"));
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
    }
}
