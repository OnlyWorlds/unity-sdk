using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Successive statuses, beside <see cref="Bodies"/>; <see cref="Status"/> once it is empty.</summary>
        public Queue<int> Statuses = new Queue<int>();

        public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            var body = Bodies.Count > 0 ? Bodies.Dequeue() : ResponseBody;
            return Task.FromResult(new OWResponse
            {
                Status = Statuses.Count > 0 ? Statuses.Dequeue() : Status,
                Body = body,
                Headers = ResponseHeaders,
            });
        }

        public OWRequest Last => Calls[Calls.Count - 1];
        public JObject LastBody => JObject.Parse(Last.Body);
    }

    public class OWClientTests
    {
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "0000")
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
            Assert.AreEqual("0000", t.Last.Headers["API-Pin"]);
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

        // -- The six read-only fields ----------------------------------------

        [Test]
        public async Task Patch_StripsAllSixReadOnlyFields()
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
                ["known_of"] = false,
            });

            var sent = t.LastBody;
            foreach (var f in new[] { "world", "type", "created_at", "updated_at", "change_seq", "known_of" })
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

        // -- string-empty-is-unset (rulings.yaml) ------------------------------

        [Test]
        public async Task Patch_SendsANullScalarStringAsEmpty()
        {
            // "" IS the wire's unset for a string; keel stores no null. A client must not send a
            // distinction the wire cannot carry.
            var t = new FakeTransport();
            await Make(t).PatchAsync<JObject>("character", "x", new JObject
            {
                ["description"] = null,
                ["physicality"] = null,
                ["supertype"] = "",
            });

            var sent = t.LastBody;
            Assert.AreEqual(JTokenType.String, sent["description"].Type, "A base scalar string.");
            Assert.AreEqual("", sent["description"].ToString());
            // Token TYPE, not only ToString(): a null JValue prints as "" and would pass a value check.
            Assert.AreEqual(JTokenType.String, sent["physicality"].Type, "A type's own scalar string.");
            Assert.AreEqual("", sent["supertype"].ToString(), "An empty string is already the unset.");
        }

        [Test]
        public async Task Patch_LeavesLinkIntExtensionAndAbsentKeysAlone()
        {
            var t = new FakeTransport();
            await Make(t).PatchAsync<JObject>("character", "x", new JObject
            {
                ["location"] = null,
                ["species"] = new JArray(),
                ["level"] = null,
                ["x_tool_note"] = null,
            });

            var sent = t.LastBody;
            Assert.AreEqual(JTokenType.Null, sent["location"].Type, "null is a single link's unset; \"\" is not an id.");
            Assert.AreEqual(JTokenType.Null, sent["level"].Type, "An int keeps three states: unset is not 0 and not \"\".");
            Assert.AreEqual(JTokenType.Null, sent["x_tool_note"].Type,
                "An extension value is another tool's; the ruling binds schema strings only.");
            Assert.IsFalse(sent.ContainsKey("description"),
                "An absent key on a PATCH means leave it alone. Inventing \"\" for it would clear it.");
        }

        [Test]
        public async Task Create_FromATypedModel_SendsEmptyStringsNeverNullOnes()
        {
            // A model read from a folder file can hold a null string; a fresh one starts at "".
            // Either way the POST says "" for every scalar string, and null only where null means
            // something (links, ints).
            var fromFolder = OWJson.Deserialize<OWLocation>(@"{""name"":""Motya"",""customs"":null,""parent_location"":null}");
            var t = new FakeTransport();
            await Make(t).CreateAsync<JObject>("location", fromFolder);

            var sent = t.LastBody;
            foreach (var field in OWElementTypes.StringFieldNames["location"].Concat(OWElementTypes.BaseStringFieldNames))
            {
                Assert.AreEqual(JTokenType.String, sent[field]?.Type,
                    $"location.{field} is a scalar string and must go out as a string.");
            }

            Assert.AreEqual("", sent["customs"].ToString());
            Assert.AreEqual(JTokenType.Null, sent["parent_location"].Type);
            Assert.AreEqual(JTokenType.Null, sent["elevation"].Type);
        }

        // -- Create: minted ids + idempotency --------------------------------

        [Test]
        public async Task Create_MintsUuid_WhenAbsent()
        {
            var t = new FakeTransport();
            await Make(t).CreateAsync<JObject>("character", new JObject { ["name"] = "New" });

            var id = t.LastBody["id"].ToString();
            Assert.IsTrue(System.Guid.TryParse(id, out _), $"Expected a minted UUID, got '{id}'.");
            Assert.AreEqual('7', id[14], $"The standard mints new ids as UUIDv7, got '{id}'.");
            Assert.IsFalse(t.Last.Headers.ContainsKey("Idempotency-Key"),
                "No key unless the caller gives one: keel replays a keyed create's first answer for 24 h, "
                + "so a re-create of a deleted id under its own id as the key would write nothing.");
        }

        [Test]
        public async Task Create_SendsTheCallersIdempotencyKey()
        {
            var t = new FakeTransport();
            await Make(t).CreateAsync<JObject>("character", new JObject { ["name"] = "New" }, idempotencyKey: "k-1");

            Assert.AreEqual("k-1", t.Last.Headers["Idempotency-Key"]);
            Assert.AreNotEqual("k-1", t.LastBody["id"].ToString(), "The key is the caller's; the id is still minted.");
        }

        [Test]
        public async Task Create_WithAKey_DerivesTheIdSoARetrySendsTheSameBody()
        {
            // A fresh id per call made a keyed retry a second body, and keel answers that with 409
            // idempotency_error, not the replay (Kael, h37 #5777; npm 4.27.1 and Python derive it the same way).
            var t = new FakeTransport();
            var client = Make(t);
            await client.CreateAsync<JObject>("character", new JObject { ["name"] = "New" }, idempotencyKey: "retry-1");
            var first = t.LastBody.ToString();
            await client.CreateAsync<JObject>("character", new JObject { ["name"] = "New" }, idempotencyKey: "retry-1");

            Assert.AreEqual(first, t.LastBody.ToString());
            Assert.AreEqual("1b88c4e4-831f-503c-8149-cece1519ca32", t.LastBody["id"].ToString(),
                "Python's uuid5 of 'retry-1' at index 0.");
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

            // keel's route (elements/api.py, `/{id}/links/{field}`). Until 2026-10-16 this posted to
            // `/editLinks/`, which keel never served: production answered 404 there and 401 here.
            StringAssert.EndsWith("/character/c1/links/friends", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.IsNull(t.LastBody["field"], "The field rides in the path, not the body.");
            Assert.AreEqual("a1", t.LastBody["add"][0].ToString());
            Assert.AreEqual("r1", t.LastBody["remove"][0].ToString());
        }

        // -- The changes feed ----------------------------------------------------

        [Test]
        public async Task Changes_PassesTheCursorAsSince()
        {
            var t = new FakeTransport { ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}" };
            await Make(t).ChangesAsync("4:069620b9-0794-708d-8000-42b0ea685f38");

            // keel's feed takes `since`; `cursor=` is a 422 (production, 2026-10-16).
            StringAssert.Contains("/changes/?since=4%3A069620b9-0794-708d-8000-42b0ea685f38", t.Last.Url);
            StringAssert.DoesNotContain("cursor=", t.Last.Url);
        }

        // -- The agent door (keel #299) ----------------------------------------

        private static OWClient MakeAgent(FakeTransport t, bool agent)
            => new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test",
                ApiPin = "0000",
                Transport = t,
                BaseUrl = "https://example.test/api/v2",
                Agent = agent,
            });

        /// <summary>Every element write the client has, each recorded as its own call.</summary>
        private static async Task RunEveryWrite(OWClient c)
        {
            await c.CreateAsync<JObject>("character", new JObject { ["name"] = "N" });
            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" });
            await c.DeleteAsync("character", "c1");
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" });
            await c.BulkAsync(new[] { new OWBulkItem { Type = "character", Element = new JObject { ["name"] = "B" } } });
        }

        [Test]
        public async Task Agent_EveryWriteCarriesTheHeader()
        {
            var t = new FakeTransport();
            await RunEveryWrite(MakeAgent(t, true));

            Assert.AreEqual(5, t.Calls.Count);
            foreach (var call in t.Calls)
            {
                Assert.IsTrue(call.Headers.ContainsKey("X-OW-Agent"), $"{call.Method} {call.Url} should declare the agent.");
                Assert.AreEqual("1", call.Headers["X-OW-Agent"]);
            }
        }

        [Test]
        public async Task Agent_ReadsNeverCarryTheHeader()
        {
            var t = new FakeTransport();
            var c = MakeAgent(t, true);
            await c.GetWorldAsync();
            await c.GetAsync<JObject>("character", "c1");
            t.ResponseBody = @"{""data"":[],""has_more"":false}";
            await c.ListAsync<JObject>("character");
            t.ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}";
            await c.ChangesAsync();

            Assert.AreEqual(4, t.Calls.Count);
            foreach (var call in t.Calls)
            {
                Assert.IsFalse(call.Headers.ContainsKey("X-OW-Agent"),
                    $"{call.Method} {call.Url}: keel's document declares the header on writes only.");
            }
        }

        [Test]
        public async Task NotAgent_NoWriteCarriesTheHeader()
        {
            var t = new FakeTransport();
            await RunEveryWrite(MakeAgent(t, false));

            Assert.AreEqual(5, t.Calls.Count);
            Assert.IsTrue(t.Calls.All(call => !call.Headers.ContainsKey("X-OW-Agent")),
                "Off by default: a tool people use never declares itself an agent.");
        }

        [Test]
        public void WriteDoor_AgentDoorsAreSeatMcpAndDeclared()
        {
            Assert.IsTrue(OWWriteDoor.IsAgent("seat"));
            Assert.IsTrue(OWWriteDoor.IsAgent("mcp"));
            Assert.IsTrue(OWWriteDoor.IsAgent("declared"));
            foreach (var door in new[] { "api", "v1", "sync", "server", null, "", "SEAT" })
            {
                Assert.IsFalse(OWWriteDoor.IsAgent(door), $"'{door}' is not an agent's door.");
            }
        }

        [Test]
        public void ChangeFeed_ReadsWhoDeletedAndThroughWhichDoor()
        {
            var page = OWJson.Deserialize<OWChangesPage>(
                @"{""changes"":[{""op"":""delete"",""type"":""character"",""id"":""c1"",""change_seq"":9,
                    ""deleted_at"":""2026-10-16T00:00:00Z"",""deleted_by"":""m1"",""deleted_via"":""mcp""}],
                  ""has_more"":false,""head"":9}");

            Assert.AreEqual("m1", page.Changes[0].DeletedBy);
            Assert.AreEqual("mcp", page.Changes[0].DeletedVia);
            Assert.IsTrue(OWWriteDoor.IsAgent(page.Changes[0].DeletedVia));
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

        [Test]
        public void Errors_CarryRetryAfter_FromTheHeader()
        {
            // keel's admission gate (core/admission.py): 503 server_busy, lowercase retry-after: 5.
            var t = new FakeTransport
            {
                Status = 503,
                ResponseBody = @"{""error"":{""type"":""api_error"",""code"":""server_busy"",""message"":""The server is busy. Retry after a few seconds.""}}",
                ResponseHeaders = new Dictionary<string, string> { ["retry-after"] = "5" },
            };

            var ex = Assert.ThrowsAsync<OWApiError>(async () => await Make(t).GetWorldAsync());

            Assert.AreEqual(5, ex.RetryAfter);
            Assert.IsTrue(ex.IsRetryable);

            t.ResponseHeaders = new Dictionary<string, string>();
            Assert.IsNull(Assert.ThrowsAsync<OWApiError>(async () => await Make(t).GetWorldAsync()).RetryAfter);
        }

        [Test]
        public void Errors_CarryRetryAfter_EvenWithoutAnEnvelope()
        {
            var t = new FakeTransport
            {
                Status = 503,
                ResponseBody = "<html>busy</html>",
                ResponseHeaders = new Dictionary<string, string> { ["Retry-After"] = "30" },
            };

            Assert.AreEqual(30, Assert.ThrowsAsync<OWApiError>(async () => await Make(t).GetWorldAsync()).RetryAfter);
        }

        [TestCase("5", ExpectedResult = 5)]
        [TestCase(" 7 ", ExpectedResult = 7)]
        [TestCase("0", ExpectedResult = 0)]
        [TestCase("1.5", ExpectedResult = null)]
        [TestCase("-1", ExpectedResult = null)]
        [TestCase("+5", ExpectedResult = null)]
        [TestCase("", ExpectedResult = null)]
        [TestCase(null, ExpectedResult = null)]
        [TestCase("soon", ExpectedResult = null)]
        [TestCase("Wed, 21 Oct 2015 07:28:10 GMT", ExpectedResult = 10)]
        [TestCase("Wed, 21 Oct 2015 07:28:00 GMT", ExpectedResult = 0)]
        [TestCase("Wed, 21 Oct 2015 07:27:00 GMT", ExpectedResult = 0)]
        public int? RetryAfter_ParsesLikeTheNpmSdk(string value)
        {
            // npm errors.ts parseRetryAfter: digits are seconds, a value with a letter is an HTTP
            // date (a past one is 0), anything else is null -- never "retry now".
            var now = new System.DateTimeOffset(2015, 10, 21, 7, 28, 0, System.TimeSpan.Zero);
            return OWApiError.ParseRetryAfter(value, now);
        }

        // keel's registry (core/errors.py ERROR_CODES) and npm's per-code docs (errors.ts).
        [TestCase(409, "idempotency_in_flight", ExpectedResult = true)]
        [TestCase(409, "upload_pending", ExpectedResult = true)]
        [TestCase(409, "removal_in_flight", ExpectedResult = true)]
        [TestCase(409, "idempotency_error", ExpectedResult = false)]
        [TestCase(409, "id_conflict", ExpectedResult = false)]
        [TestCase(409, "resync_required", ExpectedResult = false)]
        [TestCase(429, "rate_limited", ExpectedResult = true)]
        [TestCase(429, "quota_exceeded", ExpectedResult = false)]
        [TestCase(503, "server_busy", ExpectedResult = true)]
        [TestCase(503, "ask_unavailable", ExpectedResult = false)]
        [TestCase(503, "media_unavailable", ExpectedResult = false)]
        [TestCase(502, "ask_failed", ExpectedResult = true)]
        [TestCase(500, "api_error", ExpectedResult = true)]
        [TestCase(504, null, ExpectedResult = true)]
        [TestCase(413, "payload_too_large", ExpectedResult = false)]
        [TestCase(422, "invalid_request", ExpectedResult = false)]
        [TestCase(401, "invalid_credentials", ExpectedResult = false)]
        [TestCase(403, "not_author", ExpectedResult = false)]
        [TestCase(404, "not_found", ExpectedResult = false)]
        public bool IsRetryable_ByCode(int status, string code)
            => new OWApiError((System.Net.HttpStatusCode)status, "m", code: code).IsRetryable;

        // npm errors.ts isAuthError: the three auth codes, not the status.
        [TestCase(401, "invalid_credentials", ExpectedResult = true)]
        [TestCase(401, "key_revoked", ExpectedResult = true)]
        [TestCase(401, "world_gone", ExpectedResult = true)]
        [TestCase(403, "not_author", ExpectedResult = false)]
        [TestCase(403, "owner_only", ExpectedResult = false)]
        [TestCase(403, "permission_error", ExpectedResult = false)]
        [TestCase(401, null, ExpectedResult = false)]
        public bool IsAuthError_ByCode(int status, string code)
            => new OWApiError((System.Net.HttpStatusCode)status, "m", code: code).IsAuthError;

        [TestCase(404, "not_found", ExpectedResult = true)]
        [TestCase(404, null, ExpectedResult = false)]
        [TestCase(410, "not_found", ExpectedResult = false)]
        public bool IsNotFound_ByCode(int status, string code)
            => new OWApiError((System.Net.HttpStatusCode)status, "m", code: code).IsNotFound;

        [Test]
        public void NotAuthor_IsAValidKeyRefused_NotAnAuthError()
        {
            // keel's envelope for a contributor's write on someone else's element (core/errors.py).
            var t = new FakeTransport
            {
                Status = 403,
                ResponseBody = @"{""error"":{""type"":""permission_error"",""code"":""not_author"",""message"":""Not yours.""}}",
            };

            var ex = Assert.ThrowsAsync<OWApiError>(async () =>
                await Make(t).PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" }));

            Assert.IsTrue(ex.IsNotAuthor);
            Assert.IsFalse(ex.IsOwnerOnly);
            Assert.IsFalse(ex.IsAuthError, "The key is valid; checking it will not help.");
            Assert.IsTrue(new OWApiError(System.Net.HttpStatusCode.Forbidden, "m", code: "owner_only").IsOwnerOnly);
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

        [Test]
        public async Task List_SendsNameAndTypeFilters()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
            await Make(t).ListAsync<OWElement>("character", new OWListParams
            {
                NameContains = "fluff ington",
                Name = "Admiral Fluffington",
                Supertype = "Admiral",
                Subtype = "Puddle Navy",
            });

            StringAssert.Contains("name__icontains=fluff%20ington", t.Last.Url);
            StringAssert.Contains("name=Admiral%20Fluffington", t.Last.Url);
            StringAssert.Contains("supertype=Admiral", t.Last.Url);
            StringAssert.Contains("subtype=Puddle%20Navy", t.Last.Url);
        }

        [Test]
        public async Task ListAll_CarriesFiltersOntoEveryPage()
        {
            var t = new FakeTransport();
            t.Bodies.Enqueue(@"{""data"":[{""name"":""a""}],""has_more"":true,""next_cursor"":""c1""}");
            t.Bodies.Enqueue(@"{""data"":[{""name"":""b""}],""has_more"":false,""next_cursor"":null}");

            await Make(t).ListAllAsync<OWElement>("character", new OWListParams { NameContains = "admiral" });

            Assert.AreEqual(2, t.Calls.Count);
            StringAssert.Contains("name__icontains=admiral", t.Calls[0].Url);
            StringAssert.Contains("name__icontains=admiral", t.Calls[1].Url, "A filter dropped on page two widens the list silently.");
            StringAssert.Contains("cursor=c1", t.Calls[1].Url);
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
            StringAssert.DoesNotContain("limit=", t.Last.Url, "A tip read has no page to size.");
        }

        [Test]
        public async Task Changes_ReadsTheOwnersNumericCursor_AndAGuestsEpoch()
        {
            // keel's head=true answers an owner with the bare change_seq as a JSON number
            // (core/changes.py compute_head), and a guest with a token and its view epoch.
            var t = new FakeTransport { ResponseBody = @"{""cursor"":202,""changes"":[],""has_more"":false,""head"":202,""epoch"":null}" };
            var owner = await Make(t).ChangesAsync(headOnly: true);

            Assert.AreEqual("202", owner.Cursor);
            Assert.IsNull(owner.Epoch);

            t.ResponseBody = @"{""cursor"":""202:ffffffff-ffff-ffff-ffff-ffffffffffff:5"",""changes"":[],""has_more"":false,""head"":202,""epoch"":5}";
            var guest = await Make(t).ChangesAsync(headOnly: true);

            Assert.AreEqual("202:ffffffff-ffff-ffff-ffff-ffffffffffff:5", guest.Cursor);
            Assert.AreEqual(5, guest.Epoch);
        }

        [Test]
        public async Task Changes_ReadsTheElementAnUpsertCarries()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""cursor"":""201:069620b9-0794-708d-8000-42b0ea685f38"",""has_more"":false,""head"":201,""epoch"":null,
                    ""changes"":[{""op"":""upsert"",""type"":""character"",""id"":""069620b9-0794-708d-8000-42b0ea685f38"",
                    ""change_seq"":201,""updated_at"":""2026-10-16T00:00:00Z"",""element"":{""id"":""069620b9-0794-708d-8000-42b0ea685f38"",""name"":""Ama""}}]}",
            };

            var page = await Make(t).ChangesAsync();

            Assert.AreEqual("Ama", page.Changes[0].Element["name"].ToString());
        }

        [Test]
        public async Task Changes_SendsAPageSize_TheFeedAccepts()
        {
            var t = new FakeTransport { ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}" };
            await Make(t).ChangesAsync("4:069620b9-0794-708d-8000-42b0ea685f38");

            StringAssert.Contains("limit=100", t.Last.Url, "npm sends 100; keel's default is 500.");

            // The element lists clamp a large limit; the feed refuses one over 1000 with a 422.
            var big = new OWClient(new OWClientConfig { ApiKey = "ow_w_test", Transport = t, PageSize = 5000 });
            await big.ChangesAsync();

            StringAssert.Contains("limit=1000", t.Last.Url);
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
