using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The proposed change (keel #519): ProposeAsync, ListProposalsAsync, WorldProposalsAsync, AcceptProposalAsync,
    /// DeclineProposalAsync and WithdrawProposalAsync; a 202 on a patch or a link edit thrown as an
    /// <see cref="OWProposedError"/> (a bulk slot's 202 left in its slot); the five codes keel added, with
    /// stale_proposal's conflicts and current. The TypeScript SDK's test/proposals.test.mjs (4.58.0), ported: each
    /// route's method, path, query, headers and body against what is sent. Round 2 (keel #565): the queue's two kinds of
    /// row, ReviewNewElementAsync and its sixth code, from npm 4.66.0 (9b4d901) and py 0.35.0 (b63e693). Nothing here
    /// reaches a server.
    /// </summary>
    public class OWProposalTests
    {
        private const string Api = "https://www.onlyworlds.com/api/v2";
        private const string El = "018f0000-0000-7000-8000-0000000000e1";
        private const string Pid = "018f0000-0000-7000-8000-0000000000a1";

        /// <summary>One row as the document's Proposal component gives it: all nineteen keys (kind and omitted, keel #565).</summary>
        private static readonly JObject Proposal = new JObject
        {
            ["kind"] = "proposal", ["id"] = Pid, ["element_type"] = "location", ["element_id"] = El, ["state"] = "open",
            ["fields"] = new JObject { ["description"] = "A harbour town." }, ["omitted"] = new JArray(),
            ["base"] = new JObject { ["description"] = "" },
            ["base_seq"] = 41, ["evidence"] = "Chapter 3 names the harbour.", ["expires"] = "2026-11-21T16:00:00Z",
            ["by"] = "018f0000-0000-7000-8000-0000000000b1", ["via"] = "api", ["client"] = "unity-sdk",
            ["decided_by"] = null, ["decided_at"] = null, ["decline_reason"] = null,
            ["created_at"] = "2026-10-22T16:00:00Z", ["updated_at"] = "2026-10-22T16:00:00Z",
        };

        private const string NewEl = "018f0000-0000-7000-8000-0000000000e2";

        /// <summary>A queue row for a new element written as proposed (keel #565, the document's ProposedNewElement): all ten keys.</summary>
        private static readonly JObject NewElementRow = new JObject
        {
            ["kind"] = "new_element", ["id"] = NewEl, ["element_type"] = "character", ["element_id"] = NewEl, ["state"] = "open",
            ["by"] = "018f0000-0000-7000-8000-0000000000b1", ["evidence"] = "Met her in chapter 4.", ["expires"] = "2026-11-22",
            ["created_at"] = "2026-10-23T07:00:00Z", ["updated_at"] = "2026-10-23T07:00:00Z",
        };

        private static readonly JObject Element = new JObject
        {
            ["id"] = El, ["name"] = "Harbour", ["type"] = "location", ["description"] = "A harbour town.", ["change_seq"] = 42,
        };

        private static string Proposed(JObject row) => new JObject { ["proposal"] = row }.ToString();

        private static string Envelope(string type, string code, JObject extra = null)
        {
            var body = new JObject
            {
                ["error"] = new JObject { ["type"] = type, ["code"] = code, ["message"] = code, ["param"] = null,
                    ["doc_url"] = "https://onlyworlds.github.io/api/errors#" + code },
            };
            if (extra != null) foreach (var p in extra.Properties()) body[p.Name] = p.Value;
            return body.ToString();
        }

        private static OWClient Make(FakeTransport t, bool agent = false, int pageSize = 100)
            => new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = Api, Agent = agent, PageSize = pageSize,
            });

        private static List<string> HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        private static Dictionary<string, string> QueryOf(OWRequest r)
        {
            var query = new Uri(r.Url).Query.TrimStart('?');
            return query.Length == 0
                ? new Dictionary<string, string>()
                : query.Split('&').ToDictionary(p => Uri.UnescapeDataString(p.Split('=')[0]), p => Uri.UnescapeDataString(p.Split('=')[1]));
        }

        private static string PathOf(OWRequest r) => r.Url.Split('?')[0];

        // -- propose -----------------------------------------------------------

        [Test]
        public async Task Propose_PostsFieldsEvidenceAndExpires_AndReturnsTheRow()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Proposed(Proposal) };
            var row = await Make(t).ProposeAsync("location", El, new JObject { ["description"] = "A harbour town." },
                "Chapter 3 names the harbour.", expires: new DateTimeOffset(2026, 11, 21, 16, 0, 0, TimeSpan.Zero));

            Assert.AreEqual(Pid, row.Id);
            Assert.AreEqual("open", row.State);
            Assert.AreEqual(41, row.BaseSeq);
            Assert.AreEqual("A harbour town.", (string)row.Fields["description"]);
            Assert.AreEqual(1, t.Calls.Count);
            Assert.AreEqual($"{Api}/location/{El}/proposals", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.IsTrue(JToken.DeepEquals(
                JObject.Parse(@"{""fields"":{""description"":""A harbour town.""},""evidence"":""Chapter 3 names the harbour."",""expires"":""2026-11-21T16:00:00Z""}"),
                t.LastBody), t.Last.Body);

            // X-OW-Client is a header the document lists on this route (kept as `client`); no Idempotency-Key unless given.
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type", "X-OW-Client" }, HeaderNames(t.Last));
            Assert.AreEqual("unity-sdk", t.Last.Headers["X-OW-Client"]);
        }

        [Test]
        public async Task Propose_AnIdempotencyKeyGoesAsTheHeader_AndExpiresLeftOutIsNotSent()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Proposed(Proposal) };
            await Make(t).ProposeAsync("location", El, new JObject { ["name"] = "Harbour" }, "x", idempotencyKey: "prop-1");

            Assert.AreEqual("prop-1", t.Last.Headers["Idempotency-Key"]);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""fields"":{""name"":""Harbour""},""evidence"":""x""}"), t.LastBody), t.Last.Body);
        }

        [Test]
        public async Task Propose_FieldsAreCleanedAsAPatchBodyIs()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Proposed(Proposal) };
            await Make(t).ProposeAsync("location", El, new JObject
            {
                ["name"] = "Harbour", ["x_tangle_battle"] = 3, ["world"] = "w", ["type"] = "location",
                ["created_at"] = "a", ["updated_at"] = "b", ["change_seq"] = 9,
            }, "x");

            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""name"":""Harbour"",""x_tangle_battle"":3}"), t.LastBody["fields"]),
                t.Last.Body);
        }

        [Test]
        public void Propose_AKnownOfStub_OrNoFields_IsRefusedBeforeSending()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Proposed(Proposal) };
            var c = Make(t);

            Assert.ThrowsAsync<ArgumentException>(() =>
                c.ProposeAsync("location", El, new JObject { ["name"] = "", ["known_of"] = true }, "x"));
            Assert.ThrowsAsync<ArgumentNullException>(() => c.ProposeAsync("location", El, null, "x"));
            Assert.AreEqual(0, t.Calls.Count);
        }

        [Test]
        public async Task Propose_AndAccept_AnAgentClientSendsNoAgentHeader()
        {
            // The document lists X-OW-Client on the proposal routes and X-OW-Agent on none.
            var t = new FakeTransport { Status = 201, ResponseBody = Proposed(Proposal) };
            var c = Make(t, agent: true);
            await c.ProposeAsync("location", El, new JObject { ["name"] = "H" }, "x");
            t.ResponseBody = new JObject { ["proposal"] = Proposal, ["element"] = Element }.ToString();
            await c.AcceptProposalAsync("location", El, Pid);

            foreach (var call in t.Calls) Assert.IsFalse(call.Headers.ContainsKey("X-OW-Agent"), call.Url);
        }

        // -- the lists ---------------------------------------------------------

        private static string List() => new JObject
        {
            ["data"] = new JArray(Proposal), ["has_more"] = true, ["next_cursor"] = "c2",
        }.ToString();

        [Test]
        public async Task ListProposals_SendsStateSinceLimitAndCursor_AndReadsThePage()
        {
            var t = new FakeTransport { ResponseBody = List() };
            var page = await Make(t).ListProposalsAsync("location", El, new OWProposalQuery
            {
                State = "all", Since = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), Limit = 5, Cursor = "c1",
            });

            Assert.AreEqual(1, page.Data.Count);
            Assert.AreEqual(Pid, page.Data[0].Id);
            Assert.IsTrue(page.HasMore);
            Assert.AreEqual("c2", page.NextCursor);
            Assert.AreEqual($"{Api}/location/{El}/proposals", PathOf(t.Last));
            Assert.AreEqual("GET", t.Last.Method);
            CollectionAssert.AreEquivalent(new Dictionary<string, string>
            {
                ["state"] = "all", ["since"] = "2026-10-22T00:00:00Z", ["limit"] = "5", ["cursor"] = "c1",
            }, QueryOf(t.Last));
            Assert.IsNull(t.Last.Body);
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(t.Last), "a read: no PIN (h29 #10663)");
        }

        [Test]
        public async Task ListProposals_WithNoQuery_SendsOnlyThePageSize()
        {
            var t = new FakeTransport { ResponseBody = List() };
            await Make(t, pageSize: 25).ListProposalsAsync("location", El);

            Assert.AreEqual($"{Api}/location/{El}/proposals?limit=25", t.Last.Url);
        }

        [Test]
        public void ListProposals_RefusesTheWorldQueuesElementFilter_BeforeSending()
        {
            var t = new FakeTransport { ResponseBody = List() };
            Assert.ThrowsAsync<ArgumentException>(() =>
                Make(t).ListProposalsAsync("location", El, new OWProposalQuery { Element = El }));
            Assert.AreEqual(0, t.Calls.Count);
        }

        [Test]
        public async Task WorldProposals_SendsStateSinceElementLimitAndCursor()
        {
            var t = new FakeTransport { ResponseBody = List() };
            var page = await Make(t).WorldProposalsAsync(new OWProposalQuery
            {
                State = "open", Since = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), Element = El, Limit = 10, Cursor = "c1",
            });

            Assert.AreEqual(Pid, page.Data[0].Id);
            Assert.AreEqual($"{Api}/world/proposals", PathOf(t.Last));
            Assert.AreEqual("GET", t.Last.Method);
            CollectionAssert.AreEquivalent(new Dictionary<string, string>
            {
                ["state"] = "open", ["since"] = "2026-10-21T00:00:00Z", ["element"] = El, ["limit"] = "10", ["cursor"] = "c1",
            }, QueryOf(t.Last));

            var bare = new FakeTransport { ResponseBody = List() };
            await Make(bare).WorldProposalsAsync();
            Assert.AreEqual($"{Api}/world/proposals?limit=100", bare.Last.Url);
        }

        [Test]
        public async Task WorldProposals_BothKindsOfRowComeBackInKeelsOrder_AndTheCursorGoesBackAsItCame()
        {
            // keel #565 (npm 4.66.0's and py 0.35.0's vector): one order, both kinds; the cursor is opaque
            // (`<microseconds>.<id>` today), never the last row's id.
            var opaque = "1761202800000000." + NewEl;
            var t = new FakeTransport
            {
                ResponseBody = new JObject
                {
                    ["data"] = new JArray(NewElementRow, Proposal), ["has_more"] = true, ["next_cursor"] = opaque,
                }.ToString(),
            };
            var c = Make(t);
            var page = await c.WorldProposalsAsync(new OWProposalQuery { State = "all" });

            CollectionAssert.AreEqual(new[] { "new_element", "proposal" }, page.Data.Select(r => r.Kind));
            CollectionAssert.AreEqual(new[] { true, false }, page.Data.Select(r => r.IsNewElement));
            var marked = page.Data[0];
            Assert.AreEqual(NewEl, marked.Id);
            Assert.AreEqual("character", marked.ElementType);
            Assert.AreEqual(NewEl, marked.ElementId);
            Assert.AreEqual("open", marked.State);
            Assert.AreEqual("018f0000-0000-7000-8000-0000000000b1", marked.By);
            Assert.AreEqual("Met her in chapter 4.", marked.Evidence);
            Assert.AreEqual("2026-11-22", marked.Expires);
            Assert.AreEqual("2026-10-23T07:00:00Z", marked.CreatedAt);
            Assert.IsNull(marked.Fields);
            Assert.IsNull(marked.Omitted);
            var proposal = page.Data[1];
            Assert.AreEqual(Pid, proposal.Id);
            Assert.IsEmpty(proposal.Omitted);
            Assert.AreEqual(opaque, page.NextCursor);

            await c.WorldProposalsAsync(new OWProposalQuery { Cursor = page.NextCursor });
            Assert.AreEqual(opaque, QueryOf(t.Last)["cursor"]);
            // An old bare proposal id still reads as a cursor: sent as given.
            await c.WorldProposalsAsync(new OWProposalQuery { Cursor = Pid });
            Assert.AreEqual(Pid, QueryOf(t.Last)["cursor"]);
        }

        [Test]
        public async Task AProposalRow_ReadsItsKindAndOmitted_AndARowWithNoKindIsAProposal()
        {
            // keel #565: an upsert's proposal names the fields it would empty.
            var row = (JObject)Proposal.DeepClone();
            row["omitted"] = new JArray("supertype", "location");
            var t = new FakeTransport { Status = 201, ResponseBody = Proposed(row) };
            var read = await Make(t).ProposeAsync("location", El, new JObject { ["name"] = "H" }, "x");
            Assert.AreEqual("proposal", read.Kind);
            Assert.IsFalse(read.IsNewElement);
            CollectionAssert.AreEqual(new[] { "supertype", "location" }, read.Omitted);

            // A row from a server before #565 carries no kind and is a proposal.
            row.Remove("kind");
            t.ResponseBody = Proposed(row);
            var old = await Make(t).ProposeAsync("location", El, new JObject { ["name"] = "H" }, "x");
            Assert.IsNull(old.Kind);
            Assert.IsFalse(old.IsNewElement);
        }

        // -- a new element written as proposed (keel #565) --------------------

        [Test]
        public async Task ReviewNewElement_Accept_PostsTheDecision_WithThePinAndXOWClient_AndReturnsTheElement()
        {
            var settled = (JObject)Element.DeepClone();
            settled["id"] = NewEl;
            settled["type"] = "character";
            settled["x_review_proposed"] = null;
            var t = new FakeTransport { ResponseBody = new JObject { ["element"] = settled }.ToString() };
            var answer = await Make(t).ReviewNewElementAsync("character", NewEl, "accept");

            Assert.IsTrue(JToken.DeepEquals(settled, answer.Element));
            Assert.IsNull(answer.Deleted);
            Assert.AreEqual(1, t.Calls.Count);
            Assert.AreEqual($"{Api}/character/{NewEl}/review", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.AreEqual(@"{""decision"":""accept""}", t.Last.Body);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type", "X-OW-Client" }, HeaderNames(t.Last));
            Assert.AreEqual("unity-sdk", t.Last.Headers["X-OW-Client"]);
        }

        [Test]
        public async Task ReviewNewElement_Decline_SendsTheReasonWhenGiven_NoneWithout_AndReturnsDeleted()
        {
            var t = new FakeTransport { ResponseBody = new JObject { ["deleted"] = NewEl }.ToString() };
            var c = Make(t);
            var answer = await c.ReviewNewElementAsync("character", NewEl, "decline", "Not canon.");

            Assert.AreEqual(NewEl, answer.Deleted);
            Assert.IsNull(answer.Element);
            Assert.AreEqual(@"{""decision"":""decline"",""reason"":""Not canon.""}", t.Last.Body);
            await c.ReviewNewElementAsync("character", NewEl, "decline");
            Assert.AreEqual(@"{""decision"":""decline""}", t.Last.Body);
        }

        [Test]
        public async Task ReviewNewElement_AnAgentClientSendsNoAgentHeader_AndItsOwnClientName()
        {
            // The document lists X-OW-Client on the review route and X-OW-Agent nowhere there.
            var t = new FakeTransport { ResponseBody = new JObject { ["deleted"] = NewEl }.ToString() };
            var c = new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = Api, Agent = true, Client = "atlas",
            });
            await c.ReviewNewElementAsync("character", NewEl, "decline");

            Assert.IsFalse(t.Last.Headers.ContainsKey("X-OW-Agent"));
            Assert.AreEqual("atlas", t.Last.Headers["X-OW-Client"]);
        }

        [Test]
        public void ReviewNewElement_ADecisionOtherThanAcceptOrDecline_OrAReasonOnAnAccept_IsRefusedBeforeSending()
        {
            var t = new FakeTransport { ResponseBody = new JObject { ["deleted"] = NewEl }.ToString() };
            var c = Make(t);
            foreach (var bad in new[] { "Accept", "delete", "approve", "", null })
            {
                var e = Assert.ThrowsAsync<ArgumentException>(() => c.ReviewNewElementAsync("character", NewEl, bad), bad ?? "null");
                StringAssert.Contains("decision must be 'accept' or 'decline'", e.Message);
            }

            // keel answers a reason on an accept 422 (its body is {decision: accept} or {decision: decline, reason?}): py's
            // ValueError, refused here before sending.
            var r = Assert.ThrowsAsync<ArgumentException>(() => c.ReviewNewElementAsync("character", NewEl, "accept", "fine"));
            StringAssert.Contains("decline only", r.Message);
            Assert.AreEqual(0, t.Calls.Count);
        }

        [TestCase("accept", @"{""deleted"":""018f0000-0000-7000-8000-0000000000e2""}")]
        [TestCase("decline", @"{""element"":{}}")]
        [TestCase("accept", "{}")]
        public void ReviewNewElement_AnAnswerWithoutTheKeyItsDecisionSettles_IsRefused(string decision, string answer)
        {
            // py 0.35.0's vector: a 200 that does not say the review landed is never read as settled.
            var t = new FakeTransport { ResponseBody = answer };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).ReviewNewElementAsync("character", NewEl, decision));
            StringAssert.Contains("answered 200 without its", e.Message);
        }

        [Test]
        public void ReviewNewElement_NotProposedIsIsNotProposed_AndTheSeatTheMarkNamesGetsProposedOnly()
        {
            var np = new FakeTransport { Status = 422, ResponseBody = Envelope("invalid_request_error", "not_proposed") };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(np).ReviewNewElementAsync("character", NewEl, "accept"));
            Assert.IsTrue(e.IsNotProposed);
            Assert.IsFalse(e.IsProposedOnly);

            var po = new FakeTransport { Status = 403, ResponseBody = Envelope("permission_error", "proposed_only") };
            var p = Assert.ThrowsAsync<OWApiError>(() => Make(po).ReviewNewElementAsync("character", NewEl, "accept"));
            Assert.IsTrue(p.IsProposedOnly);
            Assert.IsFalse(p.IsNotProposed);

            // Another 422 is not it.
            var other = new FakeTransport { Status = 422, ResponseBody = Envelope("invalid_request_error", "invalid_request") };
            Assert.IsFalse(Assert.ThrowsAsync<OWApiError>(() => Make(other).ReviewNewElementAsync("character", NewEl, "accept")).IsNotProposed);
        }

        // -- deciding ----------------------------------------------------------

        [Test]
        public async Task AcceptProposal_PostsWithNoBody_AndReturnsTheRowAndTheElementAsWritten()
        {
            var accepted = (JObject)Proposal.DeepClone();
            accepted["state"] = "accepted";
            accepted["decided_by"] = "owner";
            accepted["decided_at"] = "2026-10-22T17:00:00Z";
            var t = new FakeTransport { ResponseBody = new JObject { ["proposal"] = accepted, ["element"] = Element }.ToString() };

            var outcome = await Make(t).AcceptProposalAsync("location", El, Pid);

            Assert.AreEqual("accepted", outcome.Proposal.State);
            Assert.AreEqual("owner", outcome.Proposal.DecidedBy);
            Assert.AreEqual(42, (int)outcome.Element["change_seq"]);
            Assert.AreEqual($"{Api}/location/{El}/proposals/{Pid}/accept", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.IsNull(t.Last.Body);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "X-OW-Client" }, HeaderNames(t.Last));
        }

        [Test]
        public async Task DeclineProposal_SendsTheReasonWhenGiven_NoBodyWithout_AndReturnsTheRow()
        {
            var declined = (JObject)Proposal.DeepClone();
            declined["state"] = "declined";
            declined["decided_by"] = "owner";
            declined["decline_reason"] = "Not canon.";
            var t = new FakeTransport { ResponseBody = Proposed(declined) };
            var c = Make(t);

            var row = await c.DeclineProposalAsync("location", El, Pid, "Not canon.");
            Assert.AreEqual("declined", row.State);
            Assert.AreEqual("Not canon.", row.DeclineReason);
            Assert.AreEqual($"{Api}/location/{El}/proposals/{Pid}/decline", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""reason"":""Not canon.""}"), t.LastBody), t.Last.Body);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type" }, HeaderNames(t.Last));

            await c.DeclineProposalAsync("location", El, Pid);
            Assert.IsNull(t.Last.Body);
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin" }, HeaderNames(t.Last));
        }

        [Test]
        public async Task WithdrawProposal_Deletes_204_ReturnsNothing()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).WithdrawProposalAsync("location", El, Pid);

            Assert.AreEqual($"{Api}/location/{El}/proposals/{Pid}", t.Last.Url);
            Assert.AreEqual("DELETE", t.Last.Method);
            Assert.IsNull(t.Last.Body);
        }

        // -- the 202 -----------------------------------------------------------

        [Test]
        public void A202OnPatchOrEditLinks_IsAnOWProposedErrorCarryingTheProposal_NeverAReturnedBody()
        {
            var writes = new (string Name, Func<OWClient, Task> Call)[]
            {
                ("patch", c => c.PatchAsync<JObject>("location", El, new JObject { ["description"] = "A harbour town." })),
                ("editLinks", c => c.EditLinksAsync<JObject>("location", El, "parent_location", add: new[] { Pid })),
                ("patch with ifMatch", c => c.PatchAsync<JObject>("location", El, new JObject { ["description"] = "x" }, ifMatch: 41)),
            };

            foreach (var (name, call) in writes)
            {
                var t = new FakeTransport { Status = 202, ResponseBody = Proposed(Proposal) };
                var e = Assert.ThrowsAsync<OWProposedError>(() => call(Make(t)), name);

                Assert.IsInstanceOf<OWApiError>(e, name);
                Assert.AreEqual(202, e.StatusCode, name);
                Assert.IsTrue(e.IsProposed, name);
                Assert.AreEqual(Pid, e.Proposal.Id, name);
                Assert.AreEqual("A harbour town.", (string)e.Proposal.Fields["description"], name);
                Assert.IsNull(e.Code, name);
                StringAssert.Contains("proposal", e.Message, name);
            }
        }

        [Test]
        public void A202WithoutAProposal_IsAPlainError_NeverAMadeUpOne()
        {
            var t = new FakeTransport { Status = 202, ResponseBody = @"{""queued"":true}" };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).PatchAsync<JObject>("location", El, new JObject { ["name"] = "x" }));

            Assert.AreEqual(202, e.StatusCode);
            Assert.IsFalse(e.IsProposed);
            Assert.IsNotInstanceOf<OWProposedError>(e);
        }

        [Test]
        public void IsProposed_IsFalseForEveryOtherError_A412Included()
        {
            var pre = new FakeTransport { Status = 412, ResponseBody = Envelope("invalid_request", "precondition_failed", new JObject { ["current"] = Element }) };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(pre).PatchAsync<JObject>("location", El, new JObject { ["name"] = "x" }, ifMatch: 41));
            Assert.IsTrue(e.IsPreconditionFailed);
            Assert.IsFalse(e.IsProposed);

            var nf = new FakeTransport { Status = 404, ResponseBody = Envelope("not_found", "not_found") };
            e = Assert.ThrowsAsync<OWApiError>(() => Make(nf).PatchAsync<JObject>("location", El, new JObject { ["name"] = "x" }));
            Assert.IsFalse(e.IsProposed);
            Assert.IsNotInstanceOf<OWProposedError>(e);
        }

        [Test]
        public async Task A200OnPatch_StillReturnsTheElement()
        {
            var t = new FakeTransport { ResponseBody = Element.ToString() };
            var el = await Make(t).PatchAsync<JObject>("location", El, new JObject { ["description"] = "A harbour town." });
            Assert.AreEqual(42, (int)el["change_seq"]);
        }

        [Test]
        public async Task Bulk_ASlots202StaysInItsSlotWithItsProposal_NothingIsThrown()
        {
            var t = new FakeTransport
            {
                ResponseBody = new JObject
                {
                    ["errors"] = false,
                    ["items"] = new JArray(
                        new JObject { ["status"] = 201, ["id"] = Pid, ["created_at"] = "2026-10-22T16:00:00Z", ["updated_at"] = "2026-10-22T16:00:00Z" },
                        new JObject { ["status"] = 202, ["id"] = El, ["proposal"] = Proposal }),
                }.ToString(),
            };
            var result = await Make(t).BulkAsync(new[]
            {
                new OWBulkItem("location", new JObject { ["id"] = Pid, ["name"] = "New" }),
                new OWBulkItem("location", new JObject { ["id"] = El, ["name"] = "Harbour" }),
            });

            Assert.AreEqual(202, result.Items[1].Status);
            Assert.IsTrue(result.Items[1].IsProposed);
            Assert.AreEqual(Pid, result.Items[1].Proposal.Id);
            Assert.IsNull(result.Items[0].Proposal);
            Assert.IsFalse(result.Items[0].IsProposed);
        }

        // -- the six codes -----------------------------------------------------

        private static readonly (string Name, int Status, string Type, string Code, Func<OWApiError, bool> Is)[] Codes =
        {
            ("IsFieldNotVisible", 422, "invalid_request", "field_not_visible", e => e.IsFieldNotVisible),
            ("IsTooManyOpen", 422, "invalid_request", "too_many_open", e => e.IsTooManyOpen),
            ("IsStaleProposal", 409, "invalid_request", "stale_proposal", e => e.IsStaleProposal),
            ("IsProposedOnly", 403, "permission_error", "proposed_only", e => e.IsProposedOnly),
            ("IsOwnProposal", 403, "permission_error", "own_proposal", e => e.IsOwnProposal),
            ("IsNotProposed", 422, "invalid_request", "not_proposed", e => e.IsNotProposed), // keel #565: a review of an unmarked element
        };

        [Test]
        public void TheSixProposalCodes_EachHaveAHelper_TrueForTheirOwnStatusAndCodeOnly()
        {
            foreach (var (name, status, type, code, _) in Codes)
            {
                var extra = code == "stale_proposal" ? new JObject { ["conflicts"] = new JArray(), ["current"] = Element } : null;
                var t = new FakeTransport { Status = status, ResponseBody = Envelope(type, code, extra) };
                var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AcceptProposalAsync("location", El, Pid));

                foreach (var other in Codes)
                {
                    Assert.AreEqual(other.Name == name, other.Is(e), $"{name}: {other.Name}");
                }

                // The status matters: the same code at another status is not the helper.
                var off = new FakeTransport { Status = 400, ResponseBody = Envelope(type, code) };
                var e400 = Assert.ThrowsAsync<OWApiError>(() => Make(off).AcceptProposalAsync("location", El, Pid));
                Assert.IsFalse(Codes.First(c => c.Name == name).Is(e400), $"{name} at 400");
            }
        }

        [Test]
        public void StaleProposal_CarriesConflictsAndCurrent()
        {
            var current = (JObject)Element.DeepClone();
            current["description"] = "A fishing village.";
            current["change_seq"] = 43;
            var conflicts = new JArray(new JObject { ["field"] = "description", ["base"] = "", ["current"] = "A fishing village." });
            var t = new FakeTransport
            {
                Status = 409,
                ResponseBody = Envelope("invalid_request", "stale_proposal", new JObject { ["conflicts"] = conflicts, ["current"] = current }),
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AcceptProposalAsync("location", El, Pid));

            Assert.IsTrue(e.IsStaleProposal);
            Assert.AreEqual(1, e.Conflicts.Count);
            Assert.AreEqual("description", e.Conflicts[0].Field);
            Assert.AreEqual("", (string)e.Conflicts[0].Base);
            Assert.AreEqual("A fishing village.", (string)e.Conflicts[0].Current);
            Assert.IsTrue(JToken.DeepEquals(current, e.Current));

            // A stale_proposal with neither still parses.
            var bare = new FakeTransport { Status = 409, ResponseBody = Envelope("invalid_request", "stale_proposal") };
            var b = Assert.ThrowsAsync<OWApiError>(() => Make(bare).AcceptProposalAsync("location", El, Pid));
            Assert.IsTrue(b.IsStaleProposal);
            Assert.IsEmpty(b.Conflicts);
            Assert.IsNull(b.Current);

            // Another 409 is not one, and carries neither.
            var other = new FakeTransport { Status = 409, ResponseBody = Envelope("invalid_request", "id_conflict", new JObject { ["conflicts"] = conflicts }) };
            var o = Assert.ThrowsAsync<OWApiError>(() => Make(other).CreateAsync<JObject>("location", new JObject { ["name"] = "x" }));
            Assert.IsFalse(o.IsStaleProposal);
            Assert.IsEmpty(o.Conflicts);
        }

        // -- the world's switch ------------------------------------------------

        [Test]
        public async Task GetWorld_ReadsAgentWritesProposedThrough()
        {
            // npm's patchWorld strip has no Unity half: this client has no World write.
            var t = new FakeTransport { ResponseBody = @"{""id"":""w"",""name"":""W"",""agent_writes_proposed"":true}" };
            Assert.IsTrue((bool)(await Make(t).GetWorldAsync())["agent_writes_proposed"]);
        }
    }
}
