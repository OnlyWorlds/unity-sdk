using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Records what the client would have sent, and replies with a scripted response.
    /// </summary>
    internal class FakeTransport : IOWTransport
    {
        public readonly List<OWRequest> Calls = new List<OWRequest>();
        public int Status = 200;
        public string ResponseBody = "{}";
        public Dictionary<string, string> ResponseHeaders = new Dictionary<string, string>();

        /// <summary>Successive bodies for multi-call flows like pagination.</summary>
        public Queue<string> Bodies = new Queue<string>();

        public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            var body = Bodies.Count > 0 ? Bodies.Dequeue() : ResponseBody;
            return Task.FromResult(new OWResponse
            {
                Status = Status,
                Body = body,
                Headers = ResponseHeaders,
            });
        }

        public OWRequest Last => Calls[Calls.Count - 1];
        public JObject LastBody => JObject.Parse(Last.Body);
    }

    public class OWClientTests
    {
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "2589")
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = "https://example.test/api/v2",
            });

        // -- Auth shape -------------------------------------------------------

        [Test]
        public async Task WorldKey_UsesApiKeyAndPin()
        {
            var t = new FakeTransport();
            await Make(t).GetWorldAsync();

            Assert.AreEqual("ow_w_test", t.Last.Headers["API-Key"]);
            Assert.AreEqual("2589", t.Last.Headers["API-Pin"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("Authorization"));
        }

        [Test]
        public async Task AccountKey_UsesBearer()
        {
            var t = new FakeTransport();
            await Make(t, "ow_a_tok", null).GetWorldAsync();

            Assert.AreEqual("Bearer ow_a_tok", t.Last.Headers["Authorization"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Key"));
        }

        [Test]
        public async Task ReadKey_SendsNoPin()
        {
            var t = new FakeTransport();
            await Make(t, "ow_r_share", null).GetWorldAsync();

            Assert.AreEqual("ow_r_share", t.Last.Headers["API-Key"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Pin"), "Read keys are the share primitive -- no PIN.");
        }

        // -- The five read-only fields ---------------------------------------

        [Test]
        public async Task Patch_StripsAllFiveReadOnlyFields()
        {
            var t = new FakeTransport();
            await Make(t).PatchAsync<JObject>("character", "x", new JObject
            {
                ["name"] = "K",
                ["description"] = "kept",
                ["world"] = "w15",
                ["type"] = "character",
                ["created_at"] = "2026-01-01T00:00:00Z",
                ["updated_at"] = "2026-01-02T00:00:00Z",
                ["change_seq"] = 42,
            });

            var sent = t.LastBody;
            foreach (var f in new[] { "world", "type", "created_at", "updated_at", "change_seq" })
            {
                Assert.IsFalse(sent.ContainsKey(f), $"{f} must be stripped -- the server 422s on it.");
            }

            Assert.AreEqual("K", sent["name"].ToString());
            Assert.AreEqual("kept", sent["description"].ToString());
        }

        [Test]
        public async Task Sanitize_IsABlacklist_ExtensionFieldsSurvive()
        {
            // The law: a whitelist would silently strip x_* and corrupt every tool that
            // round-trips its own state through ours.
            var t = new FakeTransport();
            await Make(t).PatchAsync<JObject>("character", "x", new JObject
            {
                ["name"] = "K",
                ["world"] = "strip-me",
                ["x_atlas_pinned"] = true,
                ["x_tangle_battle"] = new JObject { ["round"] = 3 },
            });

            var sent = t.LastBody;
            Assert.IsFalse(sent.ContainsKey("world"));
            Assert.IsTrue(sent.ContainsKey("x_atlas_pinned"), "Extension fields must pass through writes.");
            Assert.AreEqual(3, sent["x_tangle_battle"]["round"].Value<int>());
        }

        // -- Create: minted ids + idempotency --------------------------------

        [Test]
        public async Task Create_MintsUuid_WhenAbsent()
        {
            var t = new FakeTransport();
            await Make(t).CreateAsync<JObject>("character", new JObject { ["name"] = "New" });

            var id = t.LastBody["id"].ToString();
            Assert.IsTrue(System.Guid.TryParse(id, out _), $"Expected a minted UUID, got '{id}'.");
            Assert.AreEqual(id, t.Last.Headers["Idempotency-Key"],
                "A retry must be structurally safe: same id, same key.");
        }

        [Test]
        public async Task Create_PreservesCallerSuppliedId()
        {
            var t = new FakeTransport();
            const string given = "11111111-2222-3333-4444-555555555555";
            await Make(t).CreateAsync<JObject>("character", new JObject { ["id"] = given, ["name"] = "N" });

            Assert.AreEqual(given, t.LastBody["id"].ToString());
        }

        // -- editLinks --------------------------------------------------------

        [Test]
        public async Task EditLinks_PostsAddAndRemove()
        {
            var t = new FakeTransport();
            await Make(t).EditLinksAsync<JObject>("character", "c1", "friends",
                add: new[] { "a1" }, remove: new[] { "r1" });

            StringAssert.EndsWith("/character/c1/editLinks/", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.AreEqual("friends", t.LastBody["field"].ToString());
            Assert.AreEqual("a1", t.LastBody["add"][0].ToString());
            Assert.AreEqual("r1", t.LastBody["remove"][0].ToString());
        }

        // -- Errors -----------------------------------------------------------

        [Test]
        public void NonSuccess_ThrowsWithFullEnvelope()
        {
            var t = new FakeTransport
            {
                Status = 422,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""invalid_link"",
                    ""message"":""bad link"",""param"":""location"",
                    ""doc_url"":""https://onlyworlds.github.io/api/errors#invalid_link""}}",
            };

            var ex = Assert.ThrowsAsync<OWApiError>(async () =>
                await Make(t).GetAsync<JObject>("character", "x"));

            Assert.AreEqual(422, ex.StatusCode);
            Assert.AreEqual("invalid_link", ex.Code);
            Assert.AreEqual("location", ex.Param);
            StringAssert.Contains("errors#invalid_link", ex.DocUrl);
            Assert.IsTrue(ex.IsValidationError);
            Assert.IsFalse(ex.IsRetryable, "A 422 will fail again identically.");
        }

        [Test]
        public void NonJsonErrorBody_StillThrowsCleanly()
        {
            // A gateway can return HTML. The error path must not itself throw a parse exception.
            var t = new FakeTransport { Status = 502, ResponseBody = "<html>Bad Gateway</html>" };

            var ex = Assert.ThrowsAsync<OWApiError>(async () =>
                await Make(t).GetWorldAsync());

            Assert.AreEqual(502, ex.StatusCode);
            Assert.IsTrue(ex.IsRetryable, "5xx is worth retrying.");
        }

        // -- Pagination -------------------------------------------------------

        [Test]
        public async Task ListAll_FollowsCursorsUntilHasMoreIsFalse()
        {
            var t = new FakeTransport();
            t.Bodies.Enqueue(@"{""data"":[{""name"":""a""},{""name"":""b""}],""has_more"":true,""next_cursor"":""c1""}");
            t.Bodies.Enqueue(@"{""data"":[{""name"":""c""}],""has_more"":false,""next_cursor"":null}");

            var all = await Make(t).ListAllAsync<OWElement>("character");

            Assert.AreEqual(3, all.Count, "A short page is not the end -- the server clamps silently.");
            Assert.AreEqual(2, t.Calls.Count);
            StringAssert.Contains("cursor=c1", t.Calls[1].Url);
        }

        [Test]
        public async Task ListAll_StopsWhenServerClaimsMoreButSendsNoCursor()
        {
            // Without the guard this shape is an infinite loop rather than a bad page.
            var t = new FakeTransport
            {
                ResponseBody = @"{""data"":[{""name"":""a""}],""has_more"":true,""next_cursor"":null}",
            };

            var all = await Make(t).ListAllAsync<OWElement>("character");

            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(1, t.Calls.Count);
        }

        [Test]
        public async Task List_SendsSparseFieldsets()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
            await Make(t).ListAsync<OWElement>("narrative", new OWListParams
            {
                Fields = new[] { "id", "name", "supertype" },
                Limit = 50,
            });

            StringAssert.Contains("limit=50", t.Last.Url);
            StringAssert.Contains("fields=id%2Cname%2Csupertype", t.Last.Url);
        }

        // -- Changes ----------------------------------------------------------

        [Test]
        public async Task Changes_ParsesFeedAndHead()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""cursor"":""opaque-abc"",""has_more"":false,""head"":202,
                    ""changes"":[{""op"":""upsert"",""type"":""character"",""id"":""c1"",""change_seq"":201}]}",
            };

            var page = await Make(t).ChangesAsync();

            Assert.AreEqual("opaque-abc", page.Cursor);
            Assert.AreEqual(202, page.Head);
            Assert.AreEqual("upsert", page.Changes[0].Op);
        }

        [Test]
        public async Task Changes_HeadOnly_IsACheapTipCheck()
        {
            var t = new FakeTransport { ResponseBody = @"{""head"":202,""changes"":[],""has_more"":false}" };
            await Make(t).ChangesAsync(headOnly: true);

            StringAssert.Contains("head=true", t.Last.Url);
        }

        // -- Construction guards ----------------------------------------------

        [Test]
        public void RequiresKeyAndTransport()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new OWClient(new OWClientConfig { ApiKey = "", Transport = new FakeTransport() }));

            Assert.Throws<System.ArgumentException>(() =>
                new OWClient(new OWClientConfig { ApiKey = "ow_w_x", Transport = null }));
        }
    }
}
