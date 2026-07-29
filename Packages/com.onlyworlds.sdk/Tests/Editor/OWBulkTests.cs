using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Bulk writes, against the shapes the TypeScript SDK's tests pin.
    /// </summary>
    /// <remarks>
    /// The fixture bodies below mirror npm's <c>test/v2-client.test.mjs</c> P2a/P2c, which are
    /// themselves copied from live staging traffic recorded 2026-07-18. That provenance is the
    /// point: a fixture I invent speaks my dialect by construction and agrees with my
    /// implementation for free. These agree with the server.
    /// </remarks>
    public class OWBulkTests
    {
        private FakeTransport _transport;

        [SetUp]
        public void SetUp() => _transport = new FakeTransport();

        private OWClient Client() => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "1234",
            Transport = _transport,
            BaseUrl = "https://example.test/api/v2",
        });

        private static List<OWBulkItem> TwoItems() => new List<OWBulkItem>
        {
            new OWBulkItem("character", JObject.Parse(@"{""name"":""A""}")),
            new OWBulkItem("event", JObject.Parse(@"{""name"":""B""}")),
        };

        // P2a -- every slot created.
        private const string AllSucceeded = @"{
            ""errors"": false,
            ""items"": [
                {""status"":201,""id"":""0eac22e7-0000-4000-8000-000000000001"",
                 ""created_at"":""2026-07-18T10:00:00Z"",""updated_at"":""2026-07-18T10:00:00Z""},
                {""status"":201,""id"":""ec5f6f30-0000-4000-8000-000000000002"",
                 ""created_at"":""2026-07-18T10:00:00Z"",""updated_at"":""2026-07-18T10:00:00Z""}
            ]}";

        // P2c -- one slot rejected, with the RFC-7807-shaped error envelope.
        private const string PartialFailure = @"{
            ""errors"": true,
            ""items"": [
                {""status"":201,""id"":""0eac22e7-0000-4000-8000-000000000001""},
                {""status"":400,""id"":""c24976e7-0000-4000-8000-000000000003"",
                 ""error"":{""type"":""invalid_request"",""code"":""invalid_link"",
                            ""message"":""location references Location which does not exist"",
                            ""param"":""location"",
                            ""doc_url"":""https://onlyworlds.github.io/api/errors#invalid_link""}}
            ]}";

        // -- Request shape ----------------------------------------------------

        [Test]
        public async Task Request_WrapsItems_AndDefaultsAtomicFalse()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            await Client().BulkAsync(TwoItems());

            var sent = _transport.LastBody;
            Assert.AreEqual(false, sent["atomic"].Value<bool>(),
                "atomic defaults to false -- per-slot outcomes, not all-or-nothing.");

            var items = (JArray)sent["items"];
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("character", items[0]["type"].ToString());
            Assert.AreEqual("event", items[1]["type"].ToString());
            Assert.AreEqual("A", items[0]["element"]["name"].ToString());
        }

        [Test]
        public async Task Request_ForwardsAtomicTrue()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            await Client().BulkAsync(TwoItems(), atomic: true);

            Assert.AreEqual(true, _transport.LastBody["atomic"].Value<bool>());
        }

        [Test]
        public async Task Request_SanitizesEveryElement_JustLikeASingleWrite()
        {
            // Server-owned fields must be stripped inside bulk too. A blacklist that applies to
            // single writes but not batched ones is the kind of gap that only shows up in prod.
            _transport.Bodies.Enqueue(AllSucceeded);

            var items = new List<OWBulkItem>
            {
                new OWBulkItem("character", JObject.Parse(
                    @"{""name"":""A"",""world"":""w15"",""change_seq"":7,""type"":""character"",
                       ""created_at"":""x"",""updated_at"":""y"",""x_tool_note"":""keep me""}")),
            };

            await Client().BulkAsync(items);

            var element = _transport.LastBody["items"][0]["element"];
            Assert.IsNull(element["world"], "world is server-owned.");
            Assert.IsNull(element["change_seq"], "change_seq is server-owned.");
            Assert.IsNull(element["created_at"]);
            Assert.IsNull(element["updated_at"]);
            Assert.AreEqual("A", element["name"].ToString());
            Assert.AreEqual("keep me", element["x_tool_note"].ToString(),
                "Stripping is a blacklist -- another tool's extension field must survive.");
        }

        [Test]
        public async Task Request_MintsAnIdWhenTheElementHasNone()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            await Client().BulkAsync(TwoItems());

            var id = _transport.LastBody["items"][0]["element"]["id"].ToString();
            Assert.IsFalse(string.IsNullOrEmpty(id), "A client-minted id makes a retry safe.");
            Assert.DoesNotThrow(() => System.Guid.Parse(id));
        }

        [Test]
        public async Task Request_KeepsACallerSuppliedId()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            var items = new List<OWBulkItem>
            {
                new OWBulkItem("character",
                    JObject.Parse(@"{""id"":""11111111-2222-4333-8444-555555555555"",""name"":""A""}")),
            };

            await Client().BulkAsync(items);

            Assert.AreEqual("11111111-2222-4333-8444-555555555555",
                _transport.LastBody["items"][0]["element"]["id"].ToString());
        }

        [Test]
        public async Task Request_DoesNotMutateTheCallersElement()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            var element = JObject.Parse(@"{""name"":""A"",""world"":""w15""}");
            await Client().BulkAsync(new List<OWBulkItem> { new OWBulkItem("character", element) });

            Assert.IsNotNull(element["world"],
                "Sanitizing must work on a copy -- a client that edits its caller's object is a trap.");
            Assert.IsNull(element["id"], "Nor should minting an id write back into the caller's object.");
        }

        // -- Response parsing -------------------------------------------------

        [Test]
        public async Task Result_ParsesAllSucceeded()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            var result = await Client().BulkAsync(TwoItems());

            Assert.IsFalse(result.Errors);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(201, result.Items[0].Status);
            Assert.AreEqual(2, result.SucceededCount);
            Assert.IsEmpty(new List<OWBulkSlot>(result.Failed));
            Assert.DoesNotThrow(() => result.ThrowIfAnyFailed());
        }

        [Test]
        public async Task Result_SurfacesPartialFailure_BehindAnHttp200()
        {
            // THE point of this type. The transport says 200; one slot is a 400. A caller who
            // checks only the HTTP status ships silent data loss.
            _transport.Status = 200;
            _transport.Bodies.Enqueue(PartialFailure);

            var result = await Client().BulkAsync(TwoItems());

            Assert.IsTrue(result.Errors, "The server's own errors flag must survive parsing.");
            Assert.AreEqual(1, result.SucceededCount);

            var failed = new List<OWBulkSlot>(result.Failed);
            Assert.AreEqual(1, failed.Count);
            Assert.AreEqual(400, failed[0].Status);
            Assert.AreEqual("invalid_link", failed[0].Error.Code);
            Assert.AreEqual("location", failed[0].Error.Param);
            StringAssert.Contains("errors#invalid_link", failed[0].Error.DocUrl);
        }

        [Test]
        public async Task ThrowIfAnyFailed_NamesTheSlotsAndTheReasons()
        {
            _transport.Bodies.Enqueue(PartialFailure);

            var result = await Client().BulkAsync(TwoItems());

            var e = Assert.Throws<OWBulkPartialFailureException>(() => result.ThrowIfAnyFailed());
            StringAssert.Contains("1 of 2", e.Message);
            StringAssert.Contains("invalid_link", e.Message,
                "An exception that does not say which slot failed and why is barely better than none.");
            Assert.AreSame(result, e.Result);
        }

        [Test]
        public async Task Result_ReadsTheLowercaseReplayHeader()
        {
            _transport.ResponseHeaders = new Dictionary<string, string>
            {
                { "idempotent-replay", "true" },
            };
            _transport.Bodies.Enqueue(AllSucceeded);

            var result = await Client().BulkAsync(TwoItems(), idempotencyKey: "k1");

            Assert.IsTrue(result.WasReplay,
                "The header arrives lowercase on the wire; the lookup must not be case-sensitive.");
            Assert.AreEqual("k1", _transport.Last.Headers["Idempotency-Key"]);
        }

        [Test]
        public async Task Result_ReplayIsFalseWhenTheHeaderIsAbsent()
        {
            _transport.Bodies.Enqueue(AllSucceeded);

            var result = await Client().BulkAsync(TwoItems());

            Assert.IsFalse(result.WasReplay);
        }

        // -- Guards -----------------------------------------------------------

        [Test]
        public void EmptyRequest_IsRejectedLocally()
        {
            Assert.ThrowsAsync<System.ArgumentException>(
                async () => await Client().BulkAsync(new List<OWBulkItem>()),
                "An empty bulk request is a caller bug -- fail before spending a round trip.");
        }

        [Test]
        public void ItemWithoutAType_IsRejectedLocally()
        {
            var items = new List<OWBulkItem> { new OWBulkItem(null, new JObject()) };

            Assert.ThrowsAsync<System.ArgumentException>(
                async () => await Client().BulkAsync(items));
        }
    }
}
