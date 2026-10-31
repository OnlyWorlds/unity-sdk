using System;
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

        /// <summary>Successive response headers, beside <see cref="Bodies"/>; <see cref="ResponseHeaders"/> once it is empty.</summary>
        public Queue<Dictionary<string, string>> HeaderSets = new Queue<Dictionary<string, string>>();

        public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            var body = Bodies.Count > 0 ? Bodies.Dequeue() : ResponseBody;
            return Task.FromResult(new OWResponse
            {
                Status = Statuses.Count > 0 ? Statuses.Dequeue() : Status,
                Body = body,
                Headers = HeaderSets.Count > 0 ? HeaderSets.Dequeue() : ResponseHeaders,
            });
        }

        /// <summary>Scripts one answer: its status, body and headers (an ETag, when given).</summary>
        public void Then(int status, string body, string etag = null)
        {
            Statuses.Enqueue(status);
            Bodies.Enqueue(body);
            HeaderSets.Enqueue(etag == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["etag"] = etag });
        }

        public OWRequest Last => Calls[Calls.Count - 1];
        public JObject LastBody => JObject.Parse(Last.Body);
    }

    public class OWClientTests
    {
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "0000", string world = null)
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = "https://example.test/api/v2",
                World = world,
            });

        // -- Auth shape -------------------------------------------------------

        [Test]
        public async Task WorldKey_UsesApiKey_AndApiPinOnAWrite()
        {
            var t = new FakeTransport();
            var c = Make(t);
            await c.GetWorldAsync();

            Assert.AreEqual("ow_w_test", t.Last.Headers["API-Key"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Pin"), "a read");
            Assert.IsFalse(t.Last.Headers.ContainsKey("Authorization"));

            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" });
            Assert.AreEqual("0000", t.Last.Headers["API-Pin"]);
        }

        // -- API-Pin only where keel checks it (Skeld's ruling, 2026-10-22; npm 4.59.0's vector) --
        //
        // keel verifies a PIN wherever it is sent (argon2, 64 MiB each), and reads sent with one OOM-killed
        // staging on a cold cache (2026-10-22). Each call states its own expectation for a prefixed key, so the
        // test does not re-derive the rule from the method it checks.

        // `Any`: an account token with World set sends it too (the world reads, and the two lens routes it also sends since
        // the TypeScript SDK 4.94.0); every other call is a world key's.
        private static readonly (string Name, string Verb, bool Pin, bool Any, System.Func<OWClient, Task> Run)[] PinCalls =
        {
            // the one call served with no credential: on every client, and no key, PIN or Bearer on any
            ("health", "GET", false, true, c => c.HealthAsync()),
            // reads
            ("list", "GET", false, true, c => c.ListAsync<JObject>("location")),
            ("list (name search)", "GET", false, true, c => c.ListAsync<JObject>("location", new OWListParams { NameContains = "harb" })),
            ("list (typed options)", "GET", false, true, c => c.ListAsync<JObject>("location", new OWListParams
            {
                Ids = new[] { "l1" }, CreatedBy = "owner", Expand = new[] { "zone" }, ViewAs = "public",
            })),
            ("get", "GET", false, true, c => c.GetAsync<JObject>("location", "l1")),
            ("changes", "GET", false, false, c => c.ChangesAsync()),
            ("changes head", "GET", false, false, c => c.ChangesAsync(headOnly: true)),
            ("world", "GET", false, true, c => c.GetWorldAsync()),
            ("world lenses", "GET", false, true, c => c.WorldLensesAsync("w1", resolve: true)),
            ("members", "GET", false, true, c => c.GetMembersAsync()),
            ("hidden list", "GET", false, false, c => c.ListHiddenAsync()),
            ("draught", "GET", false, false, c => c.DraughtAsync("c1")),
            ("draught (policy)", "GET", false, false, c => c.DraughtAsync("c1", new OWDraughtPolicy { Components = new[] { "self" }, Compact = true })),
            // the one read keel checks a PIN on
            ("me", "GET", true, false, c => c.GetMeAsync()),
            // writes, each verb
            ("create", "POST", true, false, c => c.CreateAsync<JObject>("location", new JObject { ["name"] = "Harbour" })),
            ("upsert", "PUT", true, false, c => c.UpsertAsync<JObject>("location", "l1", new JObject { ["name"] = "Harbour" })),
            ("adopt", "POST", true, false, c => c.AdoptAsync<JObject>("location", "l1", "m1")),
            ("hide", "PUT", true, false, c => c.HideAsync("location", "l1")),
            ("unhide", "DELETE", true, false, c => c.UnhideAsync("location", "l1")),
            ("patch", "PATCH", true, false, c => c.PatchAsync<JObject>("location", "l1", new JObject { ["description"] = "x" })),
            ("delete", "DELETE", true, false, c => c.DeleteAsync("location", "l1")),
            ("links", "POST", true, false, c => c.EditLinksAsync<JObject>("location", "l1", "zones", add: new[] { "z1" })),
            ("bulk", "POST", true, false, c => c.BulkAsync(new[] { new OWBulkItem { Type = "location", Element = new JObject { ["name"] = "B" } } })),
            ("patch world", "PATCH", true, false, c => c.PatchWorldAsync(new JObject { ["name"] = "W" })),
            ("set my lenses", "PUT", true, true, c => c.SetMyLensesAsync(new[] { "l1" })),
            ("set my character", "PUT", true, false, c => c.SetMyCharacterAsync("c1")),
            // the escape hatch keeps the same rules: a world read, and an element write
            ("request", "GET", false, true, c => c.RequestAsync("GET", "/world/", new OWRequestOptions { WorldRead = true })),
            ("request (write)", "PATCH", true, false, c => c.RequestAsync("PATCH", "/location/l1/", new OWRequestOptions
            {
                Body = new JObject { ["name"] = "Harbour" }, Write = true, ElementWrite = true,
            })),
            // the image tickets, and the two calls that spend one at the upload host (TwoStep): keel's request is checked
            // here as every other, the host's carries the ticket alone
            ("media ticket", "POST", true, false, c => c.CreateMediaTicketAsync()),
            ("upload image", "POST", true, false, c => c.UploadImageAsync(new byte[] { 1, 2, 3 }, key: "u/w1/x.png")),
            ("removal ticket", "POST", true, false, c => c.CreateRemovalTicketAsync("u/w1/x.png")),
            ("remove image", "POST", true, false, c => c.RemoveImageAsync("u/w1/x.png")),
            // files on elements: two reads, the ticket, the upload (TwoStep), the download link, the removal
            ("files", "GET", false, false, c => c.ListFilesAsync("location", "l1")),
            ("world files", "GET", false, false, c => c.WorldFilesAsync()),
            ("file ticket", "POST", true, false, c => c.CreateFileTicketAsync("location", "l1", "notes.md", 3)),
            ("upload file", "POST", true, false, c => c.UploadFileAsync("location", "l1", new byte[] { 1, 2, 3 }, name: "notes.md")),
            ("file download link", "GET", false, false, c => c.FileDownloadLinkAsync("location", "l1", "f1")),
            ("remove file", "DELETE", true, false, c => c.RemoveFileAsync("location", "l1", "f1")),
            // the account routes: an account token's alone (AccountOnly), so every other key is refused with nothing sent and
            // `Pin` is never read (an account token sends none)
            ("account worlds", "GET", false, true, c => c.ListAccountWorldsAsync()),
            ("mint world key", "POST", true, true, c => c.MintWorldKeyAsync("w1")),
            ("revoke world key", "DELETE", true, true, c => c.RevokeWorldKeyAsync("w1", "k1")),
            ("accept join", "POST", true, true, c => c.AcceptJoinAsync("ow_j_code")),
            ("create world link", "POST", true, true, c => c.CreateWorldLinkAsync("w1", new OWWorldLinkCreate { Reader = "person" })),
            ("world links", "GET", false, true, c => c.ListWorldLinksAsync("w1")),
            ("revoke world link", "DELETE", true, true, c => c.RevokeWorldLinkAsync("w1", "l1")),
            ("create seat", "POST", true, true, c => c.CreateSeatAsync("w1", "Scout")),
            ("seats", "GET", false, true, c => c.ListSeatsAsync("w1")),
            ("rekey seat", "POST", true, true, c => c.RekeySeatAsync("w1", "s1")),
            ("remove seat", "DELETE", true, true, c => c.RemoveSeatAsync("w1", "s1")),
            // the AI routes, an account token's alone too
            ("ask", "POST", true, true, c => c.AskAsync("w1", "q", new[] { "x" })),
            ("sort", "POST", true, true, c => c.SortAsync("w1", new OWSortNote[] { "x" }, new OWSortRef[0])),
            ("allowance", "GET", false, true, c => c.AllowanceAsync()),
            // signing in, on every client with no credential of its own (NoCredential): the password's mint, keel's one
            // request among its three (the sign-in and the session's end go to the sign-in service, SignInService), and
            // Sign in with OnlyWorlds' exchange. Each answer is scripted (Scripted).
            ("sign in (password)", "POST", false, true, c => c.SignInWithPasswordAsync("ana@example.org", null, "pw")),
            ("complete sign in", "POST", false, true, c =>
            {
                var pending = c.SignInWithOnlyWorlds("atlas", "https://atlas.example.test/cb");
                return c.CompleteSignInAsync(pending, "https://atlas.example.test/cb?code=c1&state=" + pending.State);
            }),
        };

        /// <summary>The password sign-in's service under the base URL: not keel's document, so not swept as keel's.</summary>
        private const string SignInService = "https://example.test/api/v2/auth/app/";

        /// <summary>The calls whose answers are scripted, each request its own answer.</summary>
        private static readonly Dictionary<string, Action<FakeTransport>> Scripted = new Dictionary<string, Action<FakeTransport>>
        {
            ["sign in (password)"] = t =>
            {
                t.Then(200, @"{""meta"":{""session_token"":""sess""}}");
                t.Then(201, @"{""id"":""tok-1"",""name"":"""",""last4"":""wxyz"",""token"":""ow_a_xwxyz""}");
                t.Then(401, "{}");
            },
            ["complete sign in"] = t => t.Then(200, @"{""token"":""ow_a_x"",""token_id"":""t1"",""account"":{""id"":""u1""}}"),
        };

        /// <summary>The calls only an account token sends: on any other key each is refused, and nothing is sent.</summary>
        private static readonly string[] AccountOnly =
        {
            "account worlds", "mint world key", "revoke world key", "accept join",
            "create world link", "world links", "revoke world link",
            "create seat", "seats", "rekey seat", "remove seat",
            "ask", "sort", "allowance",
        };

        /// <summary>The calls that send a second request, to the upload host their ticket names, after keel's.</summary>
        private static readonly string[] TwoStep = { "upload image", "remove image", "upload file" };

        /// <summary>Where the sweep's tickets send their second request: not keel, so not swept as keel's.</summary>
        private const string UploadHost = "https://upload.example.test/v1/";

        /// <summary>
        /// Every call the client may make, each through its own client; the request each sent to keel. With a world (an
        /// account token's), only the calls such a client sends; each of the others is refused with nothing sent. A
        /// <see cref="TwoStep"/> call's request to the upload host is checked here to carry the ticket alone, whatever the
        /// client holds, and is not returned.
        /// </summary>
        private static async Task<List<(string Name, bool Pin, OWRequest Sent)>> PinSweep(string key, string pin, string world = null)
        {
            var sent = new List<(string, bool, OWRequest)>();
            foreach (var call in PinCalls)
            {
                var t = new FakeTransport
                {
                    ResponseBody = @"{""data"":[],""has_more"":false,""changes"":[],""head"":0,""cursor"":""0"",""results"":[],"
                        + @"""ticket"":""tk.sig"",""upload_url"":""" + UploadHost + @"upload"",""remove_url"":""" + UploadHost
                        + @"remove"",""max_bytes"":100}",
                };

                if ((world != null && !call.Any) || (AccountOnly.Contains(call.Name) && !key.StartsWith("ow_a_")))
                {
                    Assert.ThrowsAsync<System.InvalidOperationException>(() => call.Run(Make(t, key, pin, world)), call.Name);
                    Assert.IsEmpty(t.Calls, $"{call.Name}: nothing sent");
                    continue;
                }

                if (Scripted.TryGetValue(call.Name, out var script)) script(t);
                await call.Run(Make(t, key, pin, world));

                var signIn = t.Calls.Where(r => r.Url.StartsWith(SignInService, StringComparison.Ordinal)).ToList();
                var host = t.Calls.Where(r => r.Url.StartsWith(UploadHost, StringComparison.Ordinal)).ToList();
                var keel = t.Calls.Where(r => !r.Url.StartsWith(UploadHost, StringComparison.Ordinal)
                    && !r.Url.StartsWith(SignInService, StringComparison.Ordinal)).ToList();
                Assert.AreEqual(1, keel.Count, $"{call.Name}: one request to keel");
                Assert.AreEqual(TwoStep.Contains(call.Name) ? 1 : 0, host.Count, $"{call.Name}: the upload host's request");
                foreach (var r in host)
                {
                    CollectionAssert.IsSubsetOf(r.Headers.Keys, new[] { "Authorization", "X-Key" }, $"{call.Name}: nothing of ours to the host");
                    Assert.AreEqual("Bearer tk.sig", r.Headers["Authorization"], $"{call.Name}: the ticket, never the key");
                }

                Assert.AreEqual(call.Name == "sign in (password)" ? 2 : 0, signIn.Count, $"{call.Name}: the sign-in service's requests");
                foreach (var r in signIn)
                {
                    CollectionAssert.IsSubsetOf(r.Headers.Keys, new[] { "Content-Type", "X-Session-Token" }, $"{call.Name}: nothing of ours to the sign-in service");
                }

                Assert.AreEqual(call.Verb, keel[0].Method, $"{call.Name}: sent as {call.Verb}");
                sent.Add((call.Name, call.Pin, keel[0]));
            }

            return sent;
        }

        private static string PinOf(OWRequest sent)
            => sent.Headers.TryGetValue("API-Pin", out var pin) ? pin : null;

        /// <summary>The calls sent with no credential at all, whatever the client holds.</summary>
        private static readonly string[] NoCredential = { "health", "sign in (password)", "complete sign in" };

        /// <summary>True for a call sent with no credential, after checking it carried none.</summary>
        private static bool SentWithNone(string name, OWRequest sent)
        {
            if (!NoCredential.Contains(name)) return false;
            foreach (var header in new[] { "API-Key", "API-Pin", "Authorization" })
                Assert.IsFalse(sent.Headers.ContainsKey(header), $"{name}: no {header}");
            return true;
        }

        [Test]
        public async Task ApiPin_WorldKeyWithAPin_OnEveryWriteAndOnGetMe_OnNoOtherRead()
        {
            foreach (var (name, pin, sent) in await PinSweep("ow_w_test", "1234"))
            {
                if (SentWithNone(name, sent)) continue;
                Assert.AreEqual("ow_w_test", sent.Headers["API-Key"], name);
                Assert.AreEqual(pin ? "1234" : null, PinOf(sent), $"{name} ({sent.Method} {sent.Url})");
            }
        }

        [Test]
        public async Task ApiPin_TenDigitLegacyKeyWithAPin_OnEveryRequest_ReadsIncluded()
        {
            // A legacy key needs it on reads of a world with a PIN (v1 parity).
            foreach (var (name, _, sent) in await PinSweep("0123456789", "1234"))
            {
                if (SentWithNone(name, sent)) continue;
                Assert.AreEqual("0123456789", sent.Headers["API-Key"], name);
                Assert.AreEqual("1234", PinOf(sent), $"{name} ({sent.Method} {sent.Url})");
            }
        }

        [Test]
        public async Task ApiPin_ReadKeyWithNoPin_Never()
        {
            foreach (var (name, _, sent) in await PinSweep("ow_r_test", null))
            {
                if (SentWithNone(name, sent)) continue;
                Assert.AreEqual("ow_r_test", sent.Headers["API-Key"], name);
                Assert.IsFalse(sent.Headers.ContainsKey("API-Pin"), name);
            }
        }

        [Test]
        public async Task ApiPin_AccountToken_Never_EvenWithAPinConfigured_BearerAndNoApiKey()
        {
            var all = await PinSweep("ow_a_acct", "1234", "w1");
            Assert.AreEqual(26, all.Count,
                "the health pulse, the world reads (the escape hatch's among them), the two lens routes, the roster, the "
                + "account routes, the AI routes and the two sign-ins sent");
            foreach (var (name, _, sent) in all)
            {
                if (SentWithNone(name, sent)) continue;
                Assert.AreEqual("Bearer ow_a_acct", sent.Headers["Authorization"], name);
                Assert.IsFalse(sent.Headers.ContainsKey("API-Pin"), name);
                Assert.IsFalse(sent.Headers.ContainsKey("API-Key"), name);
            }
        }

        [Test]
        public async Task AccountKey_UsesBearer()
        {
            var t = new FakeTransport();
            await Make(t, "ow_a_tok", null, "w1").GetWorldAsync();

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
            // idempotency_error, not the replay (Kael; npm 4.27.1 and Python derive it the same way).
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

        // -- The agent door ------------------------------------------------

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
            await c.UpsertAsync<JObject>("character", "c1", new JObject { ["name"] = "U" });
            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" });
            await c.DeleteAsync("character", "c1");
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" });
            await c.BulkAsync(new[] { new OWBulkItem { Type = "character", Element = new JObject { ["name"] = "B" } } });
            await c.AdoptAsync<JObject>("character", "c1", "m1");
        }

        [Test]
        public async Task Agent_EveryWriteCarriesTheHeader()
        {
            var t = new FakeTransport();
            await RunEveryWrite(MakeAgent(t, true));

            Assert.AreEqual(7, t.Calls.Count);
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

            Assert.AreEqual(7, t.Calls.Count);
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

        // -- The client's name (X-OW-Client; npm 4.56.0's vector) --

        private static OWClient MakeNamed(FakeTransport t, string client)
            => new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test",
                ApiPin = "0000",
                Transport = t,
                BaseUrl = "https://example.test/api/v2",
                Client = client,
            });

        [Test]
        public async Task Client_IsUnitySdkByDefault_OnEveryElementWrite()
        {
            var t = new FakeTransport();
            await RunEveryWrite(MakeNamed(t, null));

            Assert.AreEqual(7, t.Calls.Count);
            foreach (var call in t.Calls)
            {
                Assert.IsTrue(call.Headers.ContainsKey("X-OW-Client"), $"{call.Method} {call.Url} should name the client.");
                Assert.AreEqual("unity-sdk", call.Headers["X-OW-Client"], $"{call.Method} {call.Url}");
            }
        }

        [TestCase("atlas")]
        [TestCase("a-1")]
        [TestCase("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 32: inside the rule
        public async Task Client_AGivenName_RidesEveryElementWriteInstead(string name)
        {
            var t = new FakeTransport();
            await RunEveryWrite(MakeNamed(t, name));

            Assert.AreEqual(7, t.Calls.Count);
            foreach (var call in t.Calls) Assert.AreEqual(name, call.Headers["X-OW-Client"], $"{call.Method} {call.Url}");
        }

        [Test]
        public async Task Client_NoReadCarriesTheName()
        {
            var t = new FakeTransport();
            var c = MakeNamed(t, "atlas");
            await c.GetWorldAsync();
            await c.GetMeAsync();
            await c.GetAsync<JObject>("character", "c1");
            t.ResponseBody = @"{""data"":[],""has_more"":false}";
            await c.ListAsync<JObject>("character");
            t.ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}";
            await c.ChangesAsync();
            await c.ChangesAsync(headOnly: true);

            Assert.AreEqual(6, t.Calls.Count);
            foreach (var call in t.Calls)
            {
                Assert.IsFalse(call.Headers.ContainsKey("X-OW-Client"),
                    $"{call.Method} {call.Url}: keel reads the name on the element writes only.");
            }
        }

        [Test]
        public void Client_ANameOutsideTheRule_IsRefusedAtConstruction_NamingTheOptionAndTheRule()
        {
            var bad = new[]
            {
                "Atlas", "", new string('z', 33), "a b", " atlas", "atlas\n", "atlas_1", (char)0xE4 + "tlas", "atlas ",
            };

            foreach (var name in bad)
            {
                var t = new FakeTransport();
                var e = Assert.Throws<System.ArgumentException>(() => MakeNamed(t, name), $"'{name}'");
                StringAssert.Contains("Client", e.Message, $"'{name}'");
                StringAssert.Contains("[a-z0-9-]", e.Message, $"'{name}'");
                StringAssert.Contains("32", e.Message, $"'{name}'");
                Assert.AreEqual(0, t.Calls.Count);
            }

            Assert.DoesNotThrow(() => MakeNamed(new FakeTransport(), null));
        }

        [Test]
        public void ChangeFeed_ReadsTheDeletingClientsName()
        {
            var page = OWJson.Deserialize<OWChangesPage>(
                @"{""changes"":[{""op"":""delete"",""type"":""character"",""id"":""c1"",""change_seq"":9,
                    ""deleted_by"":null,""deleted_via"":""api"",""deleted_client"":""atlas""},
                    {""op"":""delete"",""type"":""character"",""id"":""c2"",""change_seq"":10,""deleted_client"":null}],
                  ""has_more"":false,""head"":10}");

            Assert.AreEqual("atlas", page.Changes[0].DeletedClient);
            Assert.IsNull(page.Changes[1].DeletedClient);
        }

        [Test]
        public void ChangeFeed_ReadsAHidesWhenWhoAndDoor_AndLeavesThemNullElsewhere()
        {
            // A hide's upsert row, as the readers who still see the element get it (TypeScript SDK 4.74.0's
            // OwChangeUpsert), beside a plain upsert that carries none of the three.
            var page = OWJson.Deserialize<OWChangesPage>(
                @"{""changes"":[{""op"":""upsert"",""type"":""character"",""id"":""c1"",""change_seq"":12,
                    ""element"":{""id"":""c1"",""hidden"":true,""change_seq"":7},
                    ""changed_at"":""2026-10-25T08:30:00Z"",""changed_by"":""m-42"",""changed_via"":""site""},
                    {""op"":""upsert"",""type"":""character"",""id"":""c2"",""change_seq"":13,
                    ""element"":{""id"":""c2"",""change_seq"":13}}],
                  ""has_more"":false,""head"":13}");

            Assert.AreEqual("2026-10-25T08:30:00Z", page.Changes[0].ChangedAt);
            Assert.AreEqual("m-42", page.Changes[0].ChangedBy);
            Assert.AreEqual(OWWriteDoor.Site, page.Changes[0].ChangedVia);
            Assert.AreEqual(12, page.Changes[0].ChangeSeq);
            Assert.AreEqual(7, (int)page.Changes[0].Element["change_seq"]);
            Assert.IsNull(page.Changes[1].ChangedAt);
            Assert.IsNull(page.Changes[1].ChangedBy);
            Assert.IsNull(page.Changes[1].ChangedVia);
        }

        // -- Delete takes ifMatch (npm 4.61.0's vector) -----------

        private const string Fix412 = @"{""error"":{""type"":""invalid_request"",""code"":""precondition_failed"",
            ""message"":""The element has changed since change_seq 41 (now 44)."",""param"":""If-Match"",
            ""doc_url"":""https://onlyworlds.github.io/api/errors/#precondition_failed""},
            ""current"":{""type"":""character"",""id"":""x"",""name"":""Theirs"",""change_seq"":44}}";

        [Test]
        public async Task Delete_WithIfMatch_SendsIfMatch_WithoutIt_NoHeader()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            var c = Make(t);

            await c.DeleteAsync("character", "x", ifMatch: 44);
            Assert.AreEqual("DELETE", t.Last.Method);
            Assert.AreEqual("\"44\"", t.Last.Headers["If-Match"], "a quoted change_seq");

            await c.DeleteAsync("character", "x");
            Assert.IsFalse(t.Last.Headers.ContainsKey("If-Match"));
        }

        // A Character someone plays on the roster is never deleted (keel's test_character_played).

        [Test]
        public void Delete_APlayedCharacter_IsCharacterPlayed_OnlyAsA422()
        {
            var t = new FakeTransport
            {
                Status = 422,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""character_played"",""param"":""id"",
                    ""message"":""This Character is the world's owner's on the roster: re-point or remove that member first, then delete.""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).DeleteAsync("character", "x"));
            Assert.AreEqual((true, false, false), (e.IsCharacterPlayed, e.IsCharacterTaken, e.IsNotFound));
            Assert.AreEqual("id", e.Param);

            t.Status = 409;
            var other = Assert.ThrowsAsync<OWApiError>(() => Make(t).DeleteAsync("character", "x"));
            Assert.IsFalse(other.IsCharacterPlayed, "the code on another status is not it");
        }

        // The served-trimmed mark (schema-dist message profile, serial 76): the client leaves it in, keel refuses it by
        // name (ruled 2026-10-27, one rule for every package).

        [Test]
        public async Task AMessageReadTrimmed_IsSentBackAsRead_TheMarkLeftForTheServerToRefuse()
        {
            var read = OWJson.Deserialize<OWNarrative>(
                @"{""id"":""m"",""name"":""Hello"",""supertype"":""Message"",""narrator"":null,""characters"":[],
                  ""x_served_trimmed"":[""characters"",""narrator""],""x_room_of"":""r""}");
            var t = new FakeTransport();
            await Make(t).PatchAsync<JObject>("narrative", "m", read);
            Assert.IsTrue(t.LastBody.ContainsKey("x_served_trimmed"), "the client never strips an x_ key; keel refuses this one");
            Assert.AreEqual("r", (string)t.LastBody["x_room_of"]);
        }

        [Test]
        public void AWriteCarryingTheMark_IsServedTrimmedNotWritable_OnlyAsA422()
        {
            var t = new FakeTransport
            {
                Status = 422,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""served_trimmed_not_writable"",""param"":""x_served_trimmed"",
                    ""message"":""x_served_trimmed is set by the server and is never written.""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).GetWorldAsync());
            Assert.AreEqual((true, false), (e.IsServedTrimmedNotWritable, e.IsKnownOfNotWritable));

            t.Status = 400;
            Assert.IsFalse(Assert.ThrowsAsync<OWApiError>(() => Make(t).GetWorldAsync()).IsServedTrimmedNotWritable,
                "the code on another status is not it");
        }

        // The PATCH and link-write precondition (npm c1d5d2a's vectors).

        [Test]
        public async Task Patch_WithIfMatch_SendsIfMatchAsAQuotedChangeSeq_WithoutIt_NoHeader()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""change_seq"":45}" };
            var c = Make(t);

            await c.PatchAsync<JObject>("character", "x", new JObject { ["name"] = "Mine" }, ifMatch: 44);
            Assert.AreEqual("\"44\"", t.Last.Headers["If-Match"]);
            Assert.AreEqual("PATCH", t.Last.Method);

            await c.PatchAsync<JObject>("character", "x", new JObject { ["name"] = "Mine" });
            Assert.IsFalse(t.Last.Headers.ContainsKey("If-Match"));
        }

        [Test]
        public async Task EditLinks_WithIfMatch_SendsIfMatchToo()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""change_seq"":45}" };
            var c = Make(t);

            await c.EditLinksAsync<JObject>("character", "x", "species", add: new[] { "y" }, ifMatch: 7);
            Assert.AreEqual("\"7\"", t.Last.Headers["If-Match"]);

            await c.EditLinksAsync<JObject>("character", "x", "species", add: new[] { "y" });
            Assert.IsFalse(t.Last.Headers.ContainsKey("If-Match"));
        }

        [Test]
        public void Patch_AStaleOne_IsPreconditionFailed_CarryingTheElementAsItIsNow()
        {
            var t = new FakeTransport { Status = 412, ResponseBody = Fix412 };
            var e = Assert.ThrowsAsync<OWApiError>(() =>
                Make(t).PatchAsync<JObject>("character", "x", new JObject { ["name"] = "Mine" }, ifMatch: 41));

            Assert.IsTrue(e.IsPreconditionFailed);
            Assert.AreEqual(44, (int)e.Current["change_seq"]);
        }

        [Test]
        public void PatchAndEditLinks_WithANegativeIfMatch_AreRefusedBeforeSending()
        {
            var t = new FakeTransport();
            var c = Make(t);

            Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() =>
                c.PatchAsync<JObject>("character", "x", new JObject { ["name"] = "M" }, ifMatch: -1));
            Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() =>
                c.EditLinksAsync<JObject>("character", "x", "species", add: new[] { "y" }, ifMatch: -1));
            Assert.AreEqual(0, t.Calls.Count);
        }

        [Test]
        public void Delete_WithANegativeIfMatch_IsRefusedBeforeSending()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            Assert.ThrowsAsync<System.ArgumentOutOfRangeException>(() => Make(t).DeleteAsync("character", "x", ifMatch: -1));
            Assert.AreEqual(0, t.Calls.Count);
        }

        [Test]
        public void Delete_AStaleOne_IsPreconditionFailed_CarryingTheElementAsItIsNow()
        {
            var t = new FakeTransport { Status = 412, ResponseBody = Fix412 };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).DeleteAsync("character", "x", ifMatch: 41));

            Assert.IsTrue(e.IsPreconditionFailed);
            Assert.AreEqual(44, (int)e.Current["change_seq"]);
            Assert.AreEqual("Theirs", (string)e.Current["name"]);
        }

        [Test]
        public void PreconditionFailed_IsA412WithItsCode_AndAnErrorWithoutCurrentHasNone()
        {
            var other = new FakeTransport { Status = 422, ResponseBody = @"{""error"":{""code"":""precondition_failed""}}" };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(other).DeleteAsync("character", "x"));

            Assert.IsFalse(e.IsPreconditionFailed, "the code without its status");
            Assert.IsNull(e.Current);

            var html = new FakeTransport { Status = 412, ResponseBody = "<html>Precondition Failed</html>" };
            Assert.IsNull(Assert.ThrowsAsync<OWApiError>(() => Make(html).DeleteAsync("character", "x", ifMatch: 1)).Current);
        }

        // -- The front door on /me ----------------------------------------

        [Test]
        public async Task Me_ReadsTheFrontDoor()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""world"":{""id"":""w1"",""name"":""W""},""scope"":""write"",""kind"":""agent"",""role"":""contributor"",
                    ""membership"":""m-bot"",""character"":""c-bot"",
                    ""front_door"":{""sponsor"":{""id"":null,""kind"":""owner"",""name"":null},""sponsor_character"":null,
                        ""my_character"":""c-bot"",""charter"":null,""charter_skill"":[]}}",
            };

            var door = OWFrontDoor.Of(await Make(t).GetMeAsync());

            Assert.IsNull(door.Sponsor.Id, "the world's owner has no membership id");
            Assert.AreEqual("owner", door.Sponsor.Kind);
            Assert.IsNull(door.Sponsor.Name);
            Assert.IsNull(door.SponsorCharacter);
            Assert.AreEqual("c-bot", door.MyCharacter);
            Assert.IsNull(door.Charter);
            Assert.IsEmpty(door.CharterSkill);
        }

        [Test]
        public void Me_AnOwnersKeyHasNoSponsor_AndAnAnswerWithoutTheDoorHasNone()
        {
            var owner = OWFrontDoor.Of(JObject.Parse(
                @"{""front_door"":{""sponsor"":null,""sponsor_character"":null,""my_character"":""c-own"",""charter"":""law-1"",""charter_skill"":[""o1""]}}"));

            Assert.IsNull(owner.Sponsor);
            Assert.AreEqual("law-1", owner.Charter);
            Assert.IsNull(OWFrontDoor.Of(JObject.Parse(@"{""kind"":""owner""}")), "an older keel sends none");
            Assert.IsNull(OWFrontDoor.Of(null));
        }

        [Test]
        public void Me_ListsTheSeatsTheKeysPersonAnswersFor_AndAnOlderKeelListsNone()
        {
            var door = OWFrontDoor.Of(JObject.Parse(
                @"{""front_door"":{""sponsor"":null,""sponsor_character"":null,""my_character"":""c-own"",""charter"":null,""charter_skill"":[],
                    ""my_seats"":[{""id"":""m-bot"",""kind"":""agent"",""name"":""Scribe"",""character"":""c-bot""},
                                  {""id"":""m-play"",""kind"":""player"",""name"":null,""character"":null}]}}"));

            Assert.AreEqual(2, door.MySeats.Count);
            Assert.AreEqual("m-bot", door.MySeats[0].Id, "the membership id, as GET /members lists it");
            Assert.AreEqual("agent", door.MySeats[0].Kind, "agent seats first");
            Assert.AreEqual("Scribe", door.MySeats[0].Name);
            Assert.AreEqual("c-bot", door.MySeats[0].Character);
            Assert.AreEqual("player", door.MySeats[1].Kind);
            Assert.IsNull(door.MySeats[1].Name, "a seat with no name");
            Assert.IsNull(door.MySeats[1].Character, "a Character this key cannot read");

            var older = OWFrontDoor.Of(JObject.Parse(
                @"{""front_door"":{""sponsor"":null,""sponsor_character"":null,""my_character"":null,""charter"":null,""charter_skill"":[]}}"));
            Assert.IsEmpty(older.MySeats, "a keel from before my_seats lists none");
        }

        // -- Errors -----------------------------------------------------------

        // -- The reading aids beside it -------------------------------------

        private const string ReadingAids = @"{""charter"":{""id"":""law-1"",""name"":""House rules"",""rules"":[{""rule"":""no-orphans""}],""skill"":[""obj-1""]},
            ""guide"":{""id"":""con-g"",""name"":""Start here""},
            ""domains"":[{""id"":""con-d1"",""name"":""The Harbour"",""sentence"":""Where the ships come in."",
                ""hubs"":[{""type"":""location"",""id"":""loc-1""},{""type"":""character"",""id"":""c-1""}]},
                {""id"":""con-d2"",""name"":""The Hills"",""sentence"":"""",""hubs"":[]}]}";

        [Test]
        public async Task Me_ReadsTheReadingAids()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""world"":{""id"":""w1"",""name"":""W""},""scope"":""read"",""kind"":""owner"",
                    ""front_door"":{""sponsor"":null,""sponsor_character"":null,""my_character"":null,""charter"":""law-1"",""charter_skill"":[""obj-1""]},
                    ""reading_aids"":" + ReadingAids + "}",
            };

            var aids = OWReadingAids.Of(await Make(t).GetMeAsync());

            Assert.IsNotNull(aids);
            Assert.AreEqual("law-1", aids.Charter.Id);
            Assert.AreEqual("House rules", aids.Charter.Name);
            Assert.AreEqual("no-orphans", (string)aids.Charter.Rules[0]["rule"]);
            CollectionAssert.AreEqual(new[] { "obj-1" }, aids.Charter.Skill);
            Assert.AreEqual("con-g", aids.Guide.Id);
            Assert.AreEqual("Start here", aids.Guide.Name);
            Assert.AreEqual(2, aids.Domains.Count, "the Domains in the order served");
            Assert.AreEqual("con-d1", aids.Domains[0].Id);
            Assert.AreEqual("Where the ships come in.", aids.Domains[0].Sentence);
            CollectionAssert.AreEqual(new[] { ("location", "loc-1"), ("character", "c-1") }, aids.Domains[0].Hubs);
            Assert.AreEqual("con-d2", aids.Domains[1].Id);
            Assert.IsEmpty(aids.Domains[1].Hubs);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(ReadingAids), aids.ToJson()), "read back, the packet as served");
        }

        [Test]
        public void Me_AWorldWithNoAidsReadsAnEmptyPacket_AndAnOlderKeelNone()
        {
            var none = OWReadingAids.Of(JObject.Parse(@"{""reading_aids"":{""charter"":null,""guide"":null,""domains"":[]}}"));

            Assert.IsNotNull(none, "a world with no aids still serves the packet");
            Assert.IsNull(none.Charter);
            Assert.IsNull(none.Guide);
            Assert.IsEmpty(none.Domains);
            Assert.IsNull(OWReadingAids.Of(JObject.Parse(@"{""kind"":""owner""}")), "an older keel sends none");
            Assert.IsNull(OWReadingAids.Of(null));
        }

        [Test]
        public void Me_ACharterServedWithoutRulesReadsAnEmptyList()
        {
            var aids = OWReadingAids.Of(JObject.Parse(
                @"{""reading_aids"":{""charter"":{""id"":""law-1"",""name"":""House rules"",""rules"":null,""skill"":[]},""guide"":null,""domains"":[]}}"));

            Assert.AreEqual(JTokenType.Array, aids.Charter.Rules.Type);
            Assert.IsEmpty((JArray)aids.Charter.Rules);
            Assert.IsEmpty(aids.Charter.Skill);
        }

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

        // The TypeScript SDK's listAll guard (4.88.0): a page that hands back the cursor it was sent, with more to come,
        // would be read again forever, so the walk stops with an error. The third answer here ends the walk, so without
        // the guard the test fails on a count, not a hang. Kills: the guard dropped, compared against the first cursor
        // only, or against the caller's starting cursor not at all.
        [Test]
        public void ListAll_APageThatAnswersTheCursorItWasSent_StopsTheWalk_WithATransportError()
        {
            var t = new FakeTransport();
            t.Bodies.Enqueue(@"{""data"":[{""name"":""a""}],""has_more"":true,""next_cursor"":""c1""}");
            t.Bodies.Enqueue(@"{""data"":[{""name"":""b""}],""has_more"":true,""next_cursor"":""c1""}");
            t.Bodies.Enqueue(@"{""data"":[{""name"":""c""}],""has_more"":false,""next_cursor"":null}");

            var e = Assert.ThrowsAsync<OWTransportError>(() => Make(t).ListAllAsync<OWElement>("character"));
            Assert.AreEqual("ListAllAsync: GET /character/ answered the cursor it was sent, with more to come; the walk "
                + "stopped rather than read the same page again", e.Message);
            Assert.AreEqual(2, t.Calls.Count, "the repeated page is not read a third time");

            // The caller's own starting cursor counts too.
            t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":true,""next_cursor"":""c0""}" };
            Assert.ThrowsAsync<OWTransportError>(() => Make(t).ListAllAsync<OWElement>("character", new OWListParams { Cursor = "c0" }));
            Assert.AreEqual(1, t.Calls.Count);

            // The same cursor on the last page (no more to come) is not a loop: the walk ends as it always did.
            t = new FakeTransport();
            t.Bodies.Enqueue(@"{""data"":[{""name"":""a""}],""has_more"":true,""next_cursor"":""c1""}");
            t.Bodies.Enqueue(@"{""data"":[{""name"":""b""}],""has_more"":false,""next_cursor"":""c1""}");
            Assert.DoesNotThrowAsync(() => Make(t).ListAllAsync<OWElement>("character"));
            Assert.AreEqual(2, t.Calls.Count);
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
        public async Task List_SendsSingleLinkFilters_AndAnEmptyValueForNone()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
            await Make(t).ListAsync<OWElement>("location", new OWListParams
            {
                Filter = new Dictionary<string, string> { ["zone"] = "", ["parent_location"] = "0192f1a2-0000-7000-8000-000000000001" },
            });

            StringAssert.Contains("parent_location=0192f1a2-0000-7000-8000-000000000001&zone=", t.Last.Url,
                "Sent in field order; an empty value asks for the rows with none.");
            Assert.That(t.Last.Url, Does.EndWith("zone="), "The empty value is sent, not dropped: dropping it lists every row.");
        }

        [Test]
        public void List_RefusesAFilterThatIsNotAFieldName()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
            foreach (var bad in new[] { "", "Parent", "parent location", "a&b", "x=y", "1st" })
                Assert.Throws<ArgumentException>(() => Make(t).ListAsync<OWElement>("location",
                    new OWListParams { Filter = new Dictionary<string, string> { [bad] = "x" } }), bad);
            Assert.AreEqual(0, t.Calls.Count, "Nothing reaches the wire.");
        }

        // The TypeScript SDK's list() refusal (4.88.0): `world` in a filter would go out beside the client's own world=, or
        // to a world key that 422s it. Kills: a world filter sent on any client, by ListAsync or by ListAllAsync's walk.
        [Test]
        public void List_RefusesWorldAsAFilter_OnEveryClient_NothingSent()
        {
            var clients = new (string Key, string World)[] { ("ow_w_test", null), ("ow_a_acct", "w1"), (null, "w1") };
            foreach (var (key, world) in clients)
            {
                var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
                var p = new OWListParams { Filter = new Dictionary<string, string> { ["world"] = "w2" } };

                var e = Assert.Throws<ArgumentException>(() => Make(t, key, null, world).ListAsync<OWElement>("location", p), key ?? "no key");
                StringAssert.Contains("world is not a filter", e.Message);
                StringAssert.Contains("Nothing was sent", e.Message);

                Assert.ThrowsAsync<ArgumentException>(() => Make(t, key, null, world).ListAllAsync<OWElement>("location", p), key ?? "no key");
                Assert.AreEqual(0, t.Calls.Count, $"{key ?? "no key"}: nothing reaches the wire");
            }
        }

        // -- A list's typed options (the TypeScript SDK's list(type, { ids, createdBy, expand, viewAs })) --------

        // Kills: an option dropped, misnamed (createdBy goes as created_by, viewAs as `as`), joined other than by commas or
        // left unescaped, put in another order than the TypeScript SDK's (limit, cursor, expand, fields, ids, created_by,
        // the filters, as last), an empty one sent, or world= lost beside them.
        [Test]
        public async Task List_SendsExpandIdsCreatedByAndViewAs_InTheTypeScriptOrder_AndNothingForNone()
        {
            var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
            await Make(t).ListAsync<OWElement>("location", new OWListParams
            {
                Limit = 5, Cursor = "c=", Expand = new[] { "zone", "parent_location" }, Fields = new[] { "name" },
                Ids = new[] { "a1", "b 2" }, CreatedBy = "owner", Supertype = "port", ViewAs = "m 1",
                Filter = new Dictionary<string, string> { ["zone"] = "z1" },
            });
            Assert.AreEqual("https://example.test/api/v2/location/?limit=5&cursor=c%3D&expand=zone%2Cparent_location&fields=name"
                + "&ids=a1%2Cb%202&created_by=owner&supertype=port&zone=z1&as=m%201", t.Last.Url);

            await Make(t).ListAsync<OWElement>("location", new OWListParams { ViewAs = "public" });
            Assert.AreEqual("https://example.test/api/v2/location/?limit=100&as=public", t.Last.Url);

            await Make(t).ListAsync<OWElement>("location", new OWListParams
            {
                Expand = new string[0], Ids = new string[0], CreatedBy = "", ViewAs = "",
            });
            Assert.AreEqual("https://example.test/api/v2/location/?limit=100", t.Last.Url, "an empty option sends nothing");

            await Make(t, "ow_a_acct", null, "w1").ListAsync<OWElement>("location", new OWListParams { Ids = new[] { "a1" }, ViewAs = "public" });
            Assert.AreEqual("https://example.test/api/v2/location/?limit=100&ids=a1&as=public&world=w1", t.Last.Url,
                "world= beside the list's own query on an account token");
        }

        // Kills: an option dropped on page two, which widens the walk silently.
        [Test]
        public async Task ListAll_CarriesTheTypedOptionsOntoEveryPage()
        {
            var t = new FakeTransport();
            t.Bodies.Enqueue(@"{""data"":[{""name"":""a""}],""has_more"":true,""next_cursor"":""c1""}");
            t.Bodies.Enqueue(@"{""data"":[{""name"":""b""}],""has_more"":false,""next_cursor"":null}");

            await Make(t).ListAllAsync<OWElement>("character", new OWListParams
            {
                Ids = new[] { "a", "b" }, CreatedBy = "m1", Expand = new[] { "friends" }, ViewAs = "public",
            });

            Assert.AreEqual(2, t.Calls.Count);
            foreach (var call in t.Calls)
            {
                foreach (var pair in new[] { "ids=a%2Cb", "created_by=m1", "expand=friends", "as=public" })
                    StringAssert.Contains(pair, call.Url);
            }
            StringAssert.Contains("cursor=c1", t.Calls[1].Url);
        }

        // A Filter key that a typed member also sends would go out twice (the TypeScript SDK's list lets the filter win,
        // and drops a filter's `as` for viewAs). Kills: both sent, on ListAsync or on ListAllAsync's walk; `limit`, which
        // the client always sends, let through a filter; or a filter refused when its typed member is not set.
        [Test]
        public async Task List_AFilterKeyATypedMemberSends_IsRefused_NothingSent_AndOneItDoesNotSendGoesOnce()
        {
            var clashes = new (string Key, OWListParams Params)[]
            {
                ("limit", new OWListParams()),
                ("ids", new OWListParams { Ids = new[] { "a1" } }),
                ("created_by", new OWListParams { CreatedBy = "owner" }),
                ("expand", new OWListParams { Expand = new[] { "zone" } }),
                ("as", new OWListParams { ViewAs = "public" }),
                ("cursor", new OWListParams { Cursor = "c" }),
                ("name", new OWListParams { Name = "Harbour" }),
            };

            var t = new FakeTransport { ResponseBody = @"{""data"":[],""has_more"":false}" };
            foreach (var (key, p) in clashes)
            {
                var given = p;
                given.Filter = new Dictionary<string, string> { [key] = "x" };
                var e = Assert.Throws<ArgumentException>(() => Make(t).ListAsync<OWElement>("location", given), key);
                StringAssert.Contains($"'{key}' is sent by its own OWListParams member", e.Message, key);
                StringAssert.Contains("Nothing was sent", e.Message, key);
                Assert.ThrowsAsync<ArgumentException>(() => Make(t).ListAllAsync<OWElement>("location", given), key);
            }
            Assert.AreEqual(0, t.Calls.Count, "nothing reaches the wire");

            await Make(t).ListAsync<OWElement>("location", new OWListParams { Filter = new Dictionary<string, string> { ["ids"] = "a1" } });
            Assert.AreEqual("https://example.test/api/v2/location/?limit=100&ids=a1", t.Last.Url, "a filter its member does not send goes once");
        }

        // -- A single get's query (the TypeScript SDK's get(type, id, { expand, fields, viewAs })) --------

        // Kills: an option dropped, misnamed (viewAs goes as `as`), joined other than by commas or left unescaped, put in
        // another order than the TypeScript SDK's (expand, fields, as), an empty one sent, or world= lost beside them.
        [Test]
        public async Task Get_SendsExpandFieldsAndViewAs_InTheTypeScriptOrder_AndNothingForNone()
        {
            var t = new FakeTransport();
            await Make(t).GetAsync<JObject>("character", "c1",
                expand: new[] { "friends", "location" }, fields: new[] { "name", "friends" }, viewAs: "m 1");
            Assert.AreEqual("https://example.test/api/v2/character/c1/?expand=friends%2Clocation&fields=name%2Cfriends&as=m%201",
                t.Last.Url);

            await Make(t).GetAsync<JObject>("character", "c1", viewAs: "public");
            Assert.AreEqual("https://example.test/api/v2/character/c1/?as=public", t.Last.Url);

            await Make(t).GetAsync<JObject>("character", "c1", expand: new string[0], fields: new string[0], viewAs: "");
            Assert.AreEqual("https://example.test/api/v2/character/c1/", t.Last.Url, "an empty option sends nothing");

            await Make(t, "ow_a_acct", null, "w1").GetAsync<JObject>("character", "c1", fields: new[] { "name" });
            Assert.AreEqual("https://example.test/api/v2/character/c1/?fields=name&world=w1", t.Last.Url,
                "world= beside the get's own query on an account token");
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

        // The TypeScript SDK 4.44.0's `after`: only the changes made at or after a time, a filter beside since and limit,
        // never a position. Kills: after dropped, misnamed, sent in local time, rounded up past the time asked for (which
        // would lose a change), or moved before since and limit.
        [Test]
        public async Task Changes_SendsAfter_InUtc_ToTheMicrosecond_BesideSinceAndLimit()
        {
            var t = new FakeTransport { ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}" };
            var c = Make(t);

            await c.ChangesAsync("4:c", after: new DateTimeOffset(2026, 10, 22, 10, 30, 0, TimeSpan.FromHours(2)));
            Assert.AreEqual("https://example.test/api/v2/changes/?since=4%3Ac&limit=100&after=2026-10-22T08%3A30%3A00Z", t.Last.Url);

            await c.ChangesAsync(after: new DateTimeOffset(2026, 10, 22, 8, 30, 0, TimeSpan.Zero).AddTicks(1234567));
            Assert.AreEqual("https://example.test/api/v2/changes/?limit=100&after=2026-10-22T08%3A30%3A00.123456Z", t.Last.Url,
                "a fraction kept to the microsecond, cut, never rounded up");

            await c.ChangesAsync();
            StringAssert.DoesNotContain("after=", t.Last.Url, "no after, none sent");
        }

        // Kills: an after on the tip read sent (or dropped without a word): the tip read takes no filter.
        [Test]
        public void Changes_AfterOnTheTipRead_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var e = Assert.ThrowsAsync<ArgumentException>(
                () => Make(t).ChangesAsync(headOnly: true, after: DateTimeOffset.UtcNow));
            StringAssert.Contains("after", e.Message);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.AreEqual(0, t.Calls.Count);
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

        // -- The conditional head poll (ETag "<head>:<epoch>", If-None-Match, 304; npm 4.57.0's vector) --

        private static string HeadBody(long head, string epoch = "null")
            => $@"{{""cursor"":{head},""changes"":[],""has_more"":false,""head"":{head},""epoch"":{epoch}}}";

        private const string Resync =
            @"{""error"":{""type"":""invalid_request"",""code"":""resync_required"",""message"":""Walk the feed again.""}}";

        private static string SentTag(FakeTransport t, int i)
            => t.Calls[i].Headers.TryGetValue("If-None-Match", out var tag) ? tag : null;

        [Test]
        public async Task HeadPoll_FirstReadSendsNoTag_TheNextSendsTheETagBackExactlyAsReceived()
        {
            var t = new FakeTransport();
            t.Then(200, HeadBody(821), "W/\"821:-\"");
            t.Then(200, HeadBody(821), "W/\"821:-\"");
            var c = Make(t);

            var first = await c.ChangesAsync(headOnly: true);
            Assert.IsNull(SentTag(t, 0));
            Assert.IsFalse(first.Unchanged, "every 200 says unchanged: false");

            await c.ChangesAsync(headOnly: true);
            Assert.AreEqual("W/\"821:-\"", SentTag(t, 1), "W/ and quotes included, never compared");
        }

        [Test]
        public async Task HeadPoll_A304_AnswersTheHeldHeadCursorAndEpoch_Unchanged_InOneRequest()
        {
            const string cursor = "4021:ffffffff-ffff-ffff-ffff-ffffffffffff:3";
            var t = new FakeTransport();
            t.Then(200, $@"{{""cursor"":""{cursor}"",""changes"":[],""has_more"":false,""head"":4021,""epoch"":3}}", "W/\"4021:3\"");
            t.Then(304, "", "\"4021:3\"");
            var c = Make(t);

            await c.ChangesAsync(headOnly: true);
            var tip = await c.ChangesAsync(headOnly: true);

            Assert.IsTrue(tip.Unchanged);
            Assert.AreEqual(4021, tip.Head);
            Assert.AreEqual(cursor, tip.Cursor);
            Assert.AreEqual(3, tip.Epoch);
            Assert.IsEmpty(tip.Changes);
            Assert.AreEqual(2, t.Calls.Count);
            Assert.AreEqual("W/\"4021:3\"", SentTag(t, 1));
        }

        [Test]
        public async Task HeadPoll_A200AfterA304StoresTheNewTag_A200WithoutAnETagLeavesNoneHeld()
        {
            var t = new FakeTransport();
            t.Then(200, HeadBody(821), "W/\"821:-\"");
            t.Then(304, "", "\"821:-\"");
            t.Then(200, HeadBody(822), "W/\"822:-\"");
            t.Then(200, HeadBody(823));
            t.Then(200, HeadBody(823));
            var c = Make(t);

            await c.ChangesAsync(headOnly: true);
            await c.ChangesAsync(headOnly: true);
            var moved = await c.ChangesAsync(headOnly: true);
            Assert.AreEqual(822, moved.Head);
            Assert.AreEqual("822", moved.Cursor);
            Assert.IsNull(moved.Epoch);
            Assert.IsFalse(moved.Unchanged);

            await c.ChangesAsync(headOnly: true);
            Assert.AreEqual("W/\"822:-\"", SentTag(t, 3));
            await c.ChangesAsync(headOnly: true);
            Assert.IsNull(SentTag(t, 4));
        }

        [Test]
        public async Task HeadPoll_A409ResyncRequired_FromTheFeedOrTheHeadRead_ClearsTheTag()
        {
            var t = new FakeTransport();
            t.Then(200, HeadBody(9, "2"), "W/\"9:2\"");
            t.Then(409, Resync);
            t.Then(200, HeadBody(9, "3"), "W/\"9:3\"");
            t.Then(409, Resync);
            t.Then(200, HeadBody(9, "4"));
            var c = Make(t);

            await c.ChangesAsync(headOnly: true);
            var e = Assert.ThrowsAsync<OWApiError>(() => c.ChangesAsync("9:x:2"));
            Assert.IsTrue(e.IsResyncRequired);

            var after = await c.ChangesAsync(headOnly: true);
            Assert.AreEqual(3, after.Epoch);
            Assert.IsFalse(after.Unchanged);
            Assert.IsNull(SentTag(t, 2), "the feed's 409 dropped the tag");

            e = Assert.ThrowsAsync<OWApiError>(() => c.ChangesAsync(headOnly: true));
            Assert.IsTrue(e.IsResyncRequired);
            Assert.AreEqual("W/\"9:3\"", SentTag(t, 3));

            await c.ChangesAsync(headOnly: true);
            Assert.IsNull(SentTag(t, 4), "the head read's own 409 dropped the tag");
        }

        [Test]
        public void HeadPoll_A304WhileNothingIsHeld_IsAnErrorNamingIt_NeverAnInventedHead()
        {
            var t = new FakeTransport();
            t.Then(304, "", "\"821:-\"");

            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).ChangesAsync(headOnly: true));
            Assert.AreEqual(304, e.StatusCode);
            StringAssert.Contains("nothing is held", e.Message);
        }

        [Test]
        public async Task HeadPoll_TheFeedsOwnPagesSendNoTag()
        {
            var t = new FakeTransport();
            t.Then(200, HeadBody(5), "W/\"5:-\"");
            t.Then(200, HeadBody(5), "W/\"5:-\"");
            var c = Make(t);

            await c.ChangesAsync(headOnly: true);
            var page = await c.ChangesAsync("5");

            Assert.IsNull(SentTag(t, 1), "If-None-Match rides the head read alone");
            Assert.IsFalse(page.Unchanged);
        }

        // -- Why the epoch moved (epoch_reason, a 409's reason; npm's vectors) --

        [Test]
        public async Task EpochReason_PassesThroughWhenKeelSendsIt()
        {
            const string cursor = "4021:ffffffff-ffff-ffff-ffff-ffffffffffff:3";
            var t = new FakeTransport
            {
                ResponseBody = $@"{{""cursor"":""{cursor}"",""changes"":[],""has_more"":false,""head"":4021,""epoch"":3,""epoch_reason"":""role""}}",
            };
            var guest = await Make(t).ChangesAsync(headOnly: true);
            Assert.AreEqual("role", guest.EpochReason);
            Assert.AreEqual(3, guest.Epoch);

            t.ResponseBody = @"{""cursor"":7,""changes"":[],""has_more"":false,""head"":7,""epoch"":null,""epoch_reason"":null}";
            Assert.IsNull((await Make(t).ChangesAsync(headOnly: true)).EpochReason);

            t.ResponseBody = $@"{{""cursor"":""{cursor}"",""changes"":[],""has_more"":false,""head"":4021,""epoch"":3,""epoch_reason"":""view""}}";
            Assert.AreEqual("view", (await Make(t).ChangesAsync(cursor)).EpochReason, "a feed page carries it too");
        }

        [Test]
        public async Task EpochReason_A304DropsIt_AsTheNpmSdkDoes()
        {
            var t = new FakeTransport();
            t.Then(200, @"{""cursor"":""9"",""changes"":[],""has_more"":false,""head"":9,""epoch"":3,""epoch_reason"":""view""}", "W/\"9:3\"");
            t.Then(304, "", "\"9:3\"");
            var c = Make(t);

            Assert.AreEqual("view", (await c.ChangesAsync(headOnly: true)).EpochReason);
            var tip = await c.ChangesAsync(headOnly: true);

            Assert.IsTrue(tip.Unchanged);
            Assert.AreEqual(3, tip.Epoch);
            Assert.IsNull(tip.EpochReason, "npm's 304 answer carries no epoch_reason (Python's carries the held one)");
        }

        [TestCase("role")]
        [TestCase("view")]
        public void ResyncReason_ReadsThe409EnvelopesReason(string reason)
        {
            var t = new FakeTransport
            {
                Status = 409,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""resync_required"",""message"":""m"",""reason"":""" + reason + @"""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).ChangesAsync("12:abc:3"));

            Assert.IsTrue(e.IsResyncRequired);
            Assert.AreEqual(reason, e.ResyncReason);
        }

        [Test]
        public void ResyncReason_IsNullWithoutOne_ForAnUnknownOne_AndOnAnotherError()
        {
            ExpectReason(409, "resync_required", null, null);
            ExpectReason(409, "resync_required", "something", null);
            ExpectReason(409, "id_conflict", "role", null);

            void ExpectReason(int status, string code, string reason, string expected)
            {
                var t = new FakeTransport
                {
                    Status = status,
                    ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""" + code + @""",""message"":""m"""
                        + (reason == null ? "" : @",""reason"":""" + reason + @"""") + "}}",
                };
                var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).ChangesAsync("12:abc"));
                Assert.AreEqual(expected, e.ResyncReason, $"{status} {code} reason {reason ?? "none"}");
            }
        }

        // -- World settings: PATCH /world (npm 4.65.0's and Python 0.34.0's vectors) ------------

        private static readonly JObject Origin = JObject.Parse(
            @"{""source"":{""server"":""https://www.onlyworlds.com"",""world"":""0f1e2d3c-4b5a-4968-8776-655443322110"",""at"":""2026-10-23T05:00:00Z""},
               ""producer"":{""tool"":""atlas"",""version"":""1.82.0"",""input"":""folder""}}");

        private static readonly JObject XNames = JObject.Parse(@"{""version"":1,""types"":{""character"":{""one"":""Soul"",""many"":""Souls""}}}");

        /// <summary>A GET /world body as keel serves it: every World key, the origin and an extension key.</summary>
        private static JObject WorldBody() => new JObject
        {
            ["id"] = "w", ["name"] = "V", ["description"] = "", ["image_url"] = "",
            ["time_format_names"] = new JArray(), ["time_format_equivalents"] = new JArray(), ["time_basic_unit"] = "Year",
            ["time_range_min"] = null, ["time_range_max"] = null, ["time_range_current"] = null,
            ["length_unit"] = "", ["mass_unit"] = "", ["distance_unit"] = "", ["public_read"] = false,
            ["agent_narrator_rule"] = false, ["agent_writes_proposed"] = false, ["owner_character"] = null, ["slug"] = null,
            ["public_page"] = JObject.Parse(@"{""on"":false,""palette"":null,""hero"":"""",""sections"":[],""pin"":null,""index"":false}"),
            ["origin"] = Origin.DeepClone(), ["x_names"] = XNames.DeepClone(),
            ["created_at"] = "2026-10-01T00:00:00Z", ["updated_at"] = "2026-10-23T05:20:00Z",
        };

        [Test]
        public async Task PatchWorld_AGetWorldBodySentBack_SendsNoOriginAndNoId_AndKeepsXNames()
        {
            var t = new FakeTransport { ResponseBody = WorldBody().ToString() };
            await Make(t).PatchWorldAsync(WorldBody());

            Assert.AreEqual("PATCH", t.Last.Method);
            Assert.AreEqual("https://example.test/api/v2/world/", t.Last.Url);
            var sent = t.LastBody;
            Assert.IsFalse(sent.ContainsKey("origin"), "origin is read-only: keel answers it as an unknown key");
            Assert.IsFalse(sent.ContainsKey("id"), "id is read-only: keel answers it as an unknown key");
            Assert.IsTrue(JToken.DeepEquals(XNames, sent["x_names"]));
            CollectionAssert.AreEqual(new[]
            {
                "description", "distance_unit", "image_url", "length_unit", "mass_unit", "name", "owner_character", "time_basic_unit",
                "time_format_equivalents", "time_format_names", "time_range_current", "time_range_max", "time_range_min", "x_names",
            }, sent.Properties().Select(p => p.Name).OrderBy(n => n, System.StringComparer.Ordinal));
        }

        [Test]
        public async Task PatchWorld_SendsSourceProducerAndEveryExtensionPrefix_AsGiven_NullIncluded()
        {
            // null clears source or producer, and removes an extension key.
            var t = new FakeTransport();
            var patch = new JObject
            {
                ["source"] = Origin["source"].DeepClone(), ["producer"] = null, ["x_names"] = null,
                ["atlas_view"] = JObject.Parse(@"{""zoom"":2}"), ["shadow_note"] = "kept", ["x_tool_setting"] = new JArray(1, "a"),
            };
            await Make(t).PatchWorldAsync(patch);

            Assert.AreEqual(patch.ToString(Newtonsoft.Json.Formatting.None), t.Last.Body, "every key, its value and its place");
        }

        [Test]
        public async Task PatchWorld_DropsTheReadOnlyKeysKeelRefuses_AndKeepsTheExtensionKeys()
        {
            // Python 0.34.0's two vectors in one body: a read body's refused keys out, its extension keys in, null kept.
            var t = new FakeTransport();
            await Make(t).PatchWorldAsync(new Dictionary<string, object>
            {
                ["id"] = "w", ["name"] = "W", ["public_read"] = true, ["slug"] = "bells",
                ["public_page"] = new Dictionary<string, object> { ["on"] = true }, ["agent_narrator_rule"] = false,
                ["agent_writes_proposed"] = false, ["owner_character"] = null,
                ["origin"] = new Dictionary<string, object> { ["source"] = null, ["producer"] = null },
                ["x_names"] = XNames, ["x_gone"] = null, ["atlas_calendar"] = new Dictionary<string, object> { ["months"] = 12 },
                ["shadow_note"] = "n",
            });

            Assert.AreEqual(
                @"{""name"":""W"",""owner_character"":null,""x_names"":" + XNames.ToString(Newtonsoft.Json.Formatting.None)
                + @",""x_gone"":null,""atlas_calendar"":{""months"":12},""shadow_note"":""n""}",
                t.Last.Body);
        }

        [Test]
        public async Task PatchWorld_CarriesTheUnits_AndStripsTheServerManagedKeys()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""w"",""name"":""W"",""length_unit"":""cm"",""mass_unit"":""kg"",""distance_unit"":""km""}" };
            var world = await Make(t).PatchWorldAsync(new { length_unit = "cm", mass_unit = "kg", distance_unit = "km", world = "x", change_seq = 3 });

            Assert.AreEqual(@"{""length_unit"":""cm"",""mass_unit"":""kg"",""distance_unit"":""km""}", t.Last.Body);
            Assert.AreEqual("cm", (string)world["length_unit"], "the world as keel answers it");
        }

        [Test]
        public async Task PatchWorld_LeavesItsInputAlone()
        {
            var t = new FakeTransport();
            var body = WorldBody();
            var before = body.ToString();

            await Make(t).PatchWorldAsync(body);

            Assert.AreEqual(before, body.ToString(), "the caller's body keeps its origin and id");
        }

        [Test]
        public async Task PatchWorld_SendsThePin_AndNoAgentOrClientHeader_EvenForAnAgent()
        {
            // keel reads X-OW-Agent and X-OW-Client on element writes only, and declares neither here (npm's vector).
            var t = new FakeTransport();
            var c = new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = "https://example.test/api/v2", Agent = true,
            });
            await c.PatchWorldAsync(new JObject { ["name"] = "W" });

            Assert.AreEqual("1234", t.Last.Headers["API-Pin"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("X-OW-Agent"));
            Assert.IsFalse(t.Last.Headers.ContainsKey("X-OW-Client"));
        }

        [Test]
        public void PatchWorld_AMembersKey_IsOwnerOnly_NotNotAuthor()
        {
            var t = new FakeTransport
            {
                Status = 403,
                ResponseBody = @"{""error"":{""type"":""permission_error"",""code"":""owner_only"",""message"":""Only the owner may change this.""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).PatchWorldAsync(new JObject { ["name"] = "K" }));

            Assert.IsTrue(e.IsOwnerOnly);
            Assert.IsFalse(e.IsNotAuthor);
            Assert.IsFalse(e.IsAuthError);
        }

        [Test]
        public void PatchWorld_NothingToSend_IsRefused()
        {
            var t = new FakeTransport();
            Assert.Throws<System.ArgumentNullException>(() => Make(t).PatchWorldAsync(null));
            Assert.IsEmpty(t.Calls);
        }

        // -- Construction guards ----------------------------------------------

        // The TypeScript SDK's members() (4.22.0, the account token from 4.116.0). Kills: the rows not unwrapped from
        // {data}, a missing data read as an error, world= sent on a world key or left off an account token's, a PIN on
        // the read, and an account token without World or a keyless client reaching the transport.
        [Test]
        public async Task GetMembers_ReturnsTheRoster_WorldKeyAndAccountToken_RefusedWithoutAWorldOrAKey()
        {
            const string roster = @"{""data"":[{""id"":null,""kind"":""owner"",""role"":""owner"",""character"":null,""status"":""active"",""lenses"":[],""lenses_from"":""chosen""},{""id"":""m1"",""kind"":""agent"",""role"":""contributor"",""character"":""c1"",""status"":""removed"",""lenses"":[""l1""],""lenses_from"":""titles"",""name"":""Rigger""}]}";

            var t = new FakeTransport { ResponseBody = roster };
            var rows = await Make(t, "ow_w_test", "1234").GetMembersAsync();
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(roster)["data"], rows), "the rows as served: " + rows);
            Assert.AreEqual("GET", t.Last.Method);
            Assert.AreEqual("/api/v2/members/", new Uri(t.Last.Url).AbsolutePath);
            Assert.AreEqual("", new Uri(t.Last.Url).Query, "a world key names its world: no world=");
            Assert.AreEqual("ow_w_test", t.Last.Headers["API-Key"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Pin"), "no PIN on the read");

            t.ResponseBody = "{}";
            CollectionAssert.IsEmpty(await Make(t).GetMembersAsync(), "no data: no rows");

            t.ResponseBody = roster;
            var account = await Make(t, "ow_a_test", "1234", "w 1").GetMembersAsync();
            Assert.AreEqual(2, account.Count);
            Assert.AreEqual("?world=w%201", new Uri(t.Last.Url).Query);
            Assert.AreEqual("Bearer ow_a_test", t.Last.Headers["Authorization"]);
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Pin"), "an account token sends no PIN");

            var calls = t.Calls.Count;
            var noWorld = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, "ow_a_test", null).GetMembersAsync());
            StringAssert.Contains("names the world it sends to with World", noWorld.Message);
            var keyless = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, null, null, "w1").GetMembersAsync());
            StringAssert.Contains("needs a key", keyless.Message);
            Assert.AreEqual(calls, t.Calls.Count, "neither refused call was sent");
        }

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
