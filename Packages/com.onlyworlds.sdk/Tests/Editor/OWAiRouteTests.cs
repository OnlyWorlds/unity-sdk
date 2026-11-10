using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The AI door (the TypeScript SDK's <c>test/ai.test.mjs</c>, 4.137.0 and 4.140.0): <see cref="OWClient.AiUsesAsync"/>
    /// reads GET /account/ai, <see cref="OWClient.AiAsync(string, JObject, string, System.Threading.CancellationToken)"/>
    /// calls POST /account/ai/{use} with the use's own body as given, the client's name, an Idempotency-Key when asked,
    /// and reads the replay header. Every refusal is thrown once, never retried.
    /// </summary>
    public class OWAiRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        private static OWClient Make(FakeTransport t, string key = "ow_a_acct", string client = null, bool agent = false)
            => new OWClient(new OWClientConfig { ApiKey = key, Transport = t, BaseUrl = Base, Client = client, Agent = agent });

        private const string Use =
            @"{""slug"":""ask"",""owner"":""lantern"",""provider"":""anthropic"",""model"":""claude-haiku-5-5"",""effort"":""low"","
            + @"""prompt_sha256"":""aaaa"",""needs_world"":true,""input_caps"":{""question"":500,""context"":12000},"
            + @"""output_cap"":600,""rating_need"":5505,""structured"":false,""price_tokens"":null,""ceiling"":null,"
            + @"""calls_left"":null,""on"":true}";

        private const string Meter =
            @"""model"":""claude-haiku-5-5"",""stop_reason"":""end_turn"",""usage"":{""input_tokens"":300,""output_tokens"":12},"
            + @"""remaining"":49,""tokens_remaining"":49000,""token_rating"":50000,""paid_by"":""rating""";

        private static OWAiAskBody Ask()
            => new OWAiAskBody { WorldId = "w1", Question = "Who holds Balkonon?", Context = new List<string> { "Mira Thel (character)" } };

        private const string AskJson = @"{""world_id"":""w1"",""question"":""Who holds Balkonon?"",""context"":[""Mira Thel (character)""]}";

        // The vector "client.aiUses: GET /account/ai with the account Bearer, the uses as served".
        [Test]
        public async Task AiUses_GetsAccountAi_WithTheBearer_TheUsesAsServed()
        {
            var t = new FakeTransport { ResponseBody = @"{""uses"":[" + Use + "]}" };
            var uses = await Make(t, client: "lantern").AiUsesAsync();

            var r = t.Calls.Single();
            Assert.AreEqual("GET", r.Method);
            Assert.AreEqual(Base + "/account/ai", r.Url);
            Assert.AreEqual("Bearer ow_a_acct", r.Headers["Authorization"]);
            Assert.IsFalse(r.Headers.ContainsKey("X-OW-Client"), "a read carries no client name");

            var u = uses.Single();
            Assert.AreEqual("ask", u.Slug);
            Assert.AreEqual("lantern", u.Owner);
            Assert.AreEqual(OWAiProvider.Anthropic, u.Provider);
            Assert.AreEqual("claude-haiku-5-5", u.Model);
            Assert.AreEqual("low", u.Effort);
            Assert.AreEqual("aaaa", u.PromptSha256);
            Assert.IsTrue(u.NeedsWorld);
            CollectionAssert.AreEquivalent(new Dictionary<string, long> { ["question"] = 500, ["context"] = 12000 }, u.InputCaps);
            Assert.AreEqual(600, u.OutputCap);
            Assert.AreEqual(5505, u.RatingNeed, "4.140.0's rating_need");
            Assert.IsFalse(u.Structured);
            Assert.IsNull(u.PriceTokens);
            Assert.IsNull(u.Ceiling);
            Assert.IsNull(u.CallsLeft);
            Assert.IsTrue(u.On);
        }

        // A use with a price and a ceiling reads its numbers; a provider a later server adds reads as itself.
        [Test]
        public async Task AiUses_APriceACeilingAndAnotherProvider_ReadAsServed()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""uses"":[" + Use.Replace(@"""price_tokens"":null,""ceiling"":null,""calls_left"":null", @"""price_tokens"":40,""ceiling"":20,""calls_left"":3")
                    .Replace(@"""anthropic""", @"""jev""") + "," + Use.Replace(@"""anthropic""", @"""later""") + "]}",
            };
            var uses = await Make(t).AiUsesAsync();
            Assert.AreEqual(40, uses[0].PriceTokens);
            Assert.AreEqual(20, uses[0].Ceiling);
            Assert.AreEqual(3, uses[0].CallsLeft);
            Assert.AreEqual(OWAiProvider.Jev, uses[0].Provider);
            Assert.AreEqual("later", uses[1].Provider);
        }

        // The vector "client.ai: POST /account/ai/{use}, the body sent as given, the client name, no key unless asked",
        // through the typed ask overload.
        [Test]
        public async Task Ai_Ask_PostsTheUse_TheBodyAsGiven_TheClientName_NoKeyUnlessAsked()
        {
            var t = new FakeTransport { ResponseBody = @"{""answer"":""Mira Thel.""," + Meter + @",""use_remaining"":null}" };
            var r = await Make(t, client: "lantern", agent: true).AiAsync(Ask());

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/ai/ask", a.Url);
            Assert.AreEqual("Bearer ow_a_acct", a.Headers["Authorization"]);
            Assert.AreEqual("lantern", a.Headers["X-OW-Client"]);
            Assert.IsFalse(a.Headers.ContainsKey("Idempotency-Key"));
            Assert.IsFalse(a.Headers.ContainsKey("X-OW-Agent"), "the document declares no agent header on the door");
            Assert.IsFalse(a.Headers.ContainsKey("API-Pin"));
            Assert.AreEqual(AskJson, a.Body);

            Assert.AreEqual("Mira Thel.", r.Answer);
            Assert.AreEqual(49, r.Remaining);
            Assert.AreEqual(OWPaidBy.Rating, r.PaidBy);
            Assert.IsNull(r.UseRemaining);
            Assert.IsFalse(r.WasReplay);
        }

        // The same call through the open overload: the same request, the answer verbatim.
        [Test]
        public async Task Ai_ByName_TheSameRequest_TheAnswerVerbatim()
        {
            var t = new FakeTransport { ResponseBody = @"{""answer"":""Mira Thel.""," + Meter + @",""use_remaining"":null}" };
            var r = await Make(t, client: "lantern").AiAsync("ask", JObject.Parse(AskJson));
            Assert.AreEqual(Base + "/account/ai/ask", t.Last.Url);
            Assert.AreEqual(AskJson, t.Last.Body);
            Assert.AreEqual("lantern", t.Last.Headers["X-OW-Client"]);
            Assert.AreEqual("Mira Thel.", (string)r.Answer["answer"]);
            Assert.AreEqual("rating", (string)r.Answer["paid_by"]);
            Assert.IsNull(r.UseRemaining);
            Assert.IsFalse(r.WasReplay);
        }

        // The vector "client.ai: any listed use by its slug, its body untouched".
        [Test]
        public async Task Ai_AnyListedUse_BySlug_ItsBodyUntouched()
        {
            var t = new FakeTransport { ResponseBody = @"{""rows"":[]," + Meter + @",""use_remaining"":2}" };
            var body = JObject.Parse(
                @"{""world_id"":""w1"",""notes"":[""a note"",{""text"":"""",""image_url"":""https://media.onlyworlds.com/u/w1/x.webp""}],"
                + @"""slice"":[{""id"":""e1"",""type"":""character"",""name"":""Mira""}],""extra_key"":1}");
            var sent = body.ToString(Newtonsoft.Json.Formatting.None);

            var r = await Make(t).AiAsync("sub-helm", body);
            Assert.AreEqual(Base + "/account/ai/sub-helm", t.Last.Url);
            Assert.AreEqual(sent, t.Last.Body, "the use's own body: nothing renamed, nothing dropped");
            Assert.AreEqual(2, r.UseRemaining);
        }

        // The typed sort: the wire's spelling, each note as SortAsync sends it, the slice optional.
        [Test]
        public async Task Ai_Sort_TheWiresSpelling_TheSliceOptional()
        {
            var t = new FakeTransport { ResponseBody = @"{""rows"":[],""not_proposed"":[]," + Meter + @",""use_remaining"":5}" };
            var c = Make(t);
            var r = await c.AiAsync(new OWAiSortBody
            {
                WorldId = "w1",
                Notes = new List<OWSortNote> { "Mira runs the ferry.", new OWSortNote { ImageUrl = "https://media.example.test/u/w1/n.png" } },
                Slice = new List<OWSortRef> { new OWSortRef { Id = "c1", Type = "character", Name = "Mira" } },
            });
            Assert.AreEqual(Base + "/account/ai/sort", t.Last.Url);
            Assert.AreEqual(
                @"{""world_id"":""w1"",""notes"":[""Mira runs the ferry."",{""image_url"":""https://media.example.test/u/w1/n.png""}],"
                + @"""slice"":[{""id"":""c1"",""type"":""character"",""name"":""Mira""}]}", t.Last.Body);
            Assert.AreEqual(5, r.UseRemaining);
            Assert.AreEqual(49, r.Remaining);

            await c.AiAsync(new OWAiSortBody { WorldId = "w1", Notes = new List<OWSortNote> { "a" } });
            Assert.AreEqual(@"{""world_id"":""w1"",""notes"":[""a""]}", t.Last.Body, "no slice given, none sent");

            Assert.Throws<ArgumentException>(() => c.AiAsync(new OWAiSortBody { WorldId = "w1", Notes = new List<OWSortNote> { null } }));
            Assert.Throws<ArgumentException>(() => c.AiAsync(new OWAiSortBody { WorldId = "w1", Notes = new List<OWSortNote> { new OWSortNote() } }));
            Assert.Throws<ArgumentException>(() => c.AiAsync(new OWAiSortBody { WorldId = "w1", Notes = new List<OWSortNote> { "a" }, Slice = new List<OWSortRef> { null } }));
            Assert.AreEqual(2, t.Calls.Count, "a note or row it cannot send is refused, nothing sent");
        }

        // The vector "client.ai: the idempotency key is sent, and a replay reads wasReplay", on all three overloads.
        [Test]
        public async Task Ai_TheIdempotencyKeyIsSent_AndAReplayReadsWasReplay()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""answer"":""x"",""rows"":[]," + Meter + "}",
                ResponseHeaders = new Dictionary<string, string> { ["idempotent-replay"] = "true" },
            };
            var c = Make(t);

            var ask = await c.AiAsync(Ask(), "k-1");
            Assert.AreEqual("k-1", t.Last.Headers["Idempotency-Key"]);
            Assert.IsTrue(ask.WasReplay);

            var sort = await c.AiAsync(new OWAiSortBody { WorldId = "w1", Notes = new List<OWSortNote> { "a" } }, "k-2");
            Assert.AreEqual("k-2", t.Last.Headers["Idempotency-Key"]);
            Assert.IsTrue(sort.WasReplay);

            var open = await c.AiAsync("subs-jev", new JObject { ["state"] = "s" }, "k-3");
            Assert.AreEqual("k-3", t.Last.Headers["Idempotency-Key"]);
            Assert.IsTrue(open.WasReplay);
            Assert.IsNull(open.UseRemaining, "an answer without use_remaining reads null");
        }

        // The vector "client.ai and aiUses: refused before sending". The TypeScript SDK's non-string slugs, non-object
        // bodies, non-string keys and unknown options are this client's compile errors; a null key here is no key.
        [Test]
        public async Task AiAndAiUses_RefusedBeforeSending()
        {
            var t = new FakeTransport { ResponseBody = "{}" };
            var acct = Make(t);
            var world = new OWClient(new OWClientConfig { ApiKey = "ow_w_key", ApiPin = "1234", Transport = t, BaseUrl = Base });

            Assert.Throws<InvalidOperationException>(() => world.AiAsync(Ask()));
            Assert.Throws<InvalidOperationException>(() => world.AiAsync("ask", JObject.Parse(AskJson)));
            Assert.Throws<InvalidOperationException>(() => world.AiAsync(new OWAiSortBody()));
            Assert.ThrowsAsync<InvalidOperationException>(() => world.AiUsesAsync());

            foreach (var slug in new[] { "", "ask/../sort", "ask%2Fx", "a b", "../ask", "ask?x=1", null })
            {
                var e = Assert.Throws<ArgumentException>(() => acct.AiAsync(slug, JObject.Parse(AskJson)), slug ?? "null");
                StringAssert.Contains("one path segment", e.Message, slug ?? "null");
                Assert.AreEqual("use", e.ParamName);
            }

            Assert.Throws<ArgumentNullException>(() => acct.AiAsync("ask", null));
            Assert.Throws<ArgumentNullException>(() => acct.AiAsync((OWAiAskBody)null));
            Assert.Throws<ArgumentNullException>(() => acct.AiAsync((OWAiSortBody)null));

            foreach (var key in new[] { "", new string('x', 201) })
            {
                var e = Assert.Throws<ArgumentException>(() => acct.AiAsync(Ask(), key), key.Length.ToString());
                StringAssert.Contains("Nothing was sent.", e.Message);
                Assert.Throws<ArgumentException>(() => acct.AiAsync("ask", JObject.Parse(AskJson), key));
                Assert.Throws<ArgumentException>(() => acct.AiAsync(new OWAiSortBody(), key));
            }

            Assert.AreEqual(0, t.Calls.Count);

            // the cap is the document's: 200 characters pass
            await acct.AiAsync(Ask(), new string('x', 200));
            Assert.AreEqual(1, t.Calls.Count);
        }

        // The vector "client.ai: each refusal by its code, thrown once, never retried", with 4.140.0's answer_not_parsed.
        // This client has no isRateLimited (429 too_many_requests): there the case holds that none of the others reads it.
        private static readonly (int Status, string Code, string Helper, string Param, string Retry)[] Refusals =
        {
            (429, "use_ceiling", "IsUseCeiling", "use", "7200"),
            (429, "use_in_flight", "IsUseInFlight", "use", "2"),
            (429, "quota_exceeded", "IsQuotaExceeded", null, "3600"),
            (429, "too_many_requests", null, null, "30"),
            (422, "own_key_refused", "IsOwnKeyRefused", null, null),
            (503, "ask_unavailable", "IsAskUnavailable", "use", null),
            (502, "ask_failed", "IsAskFailed", null, null),
            (422, "answer_not_parsed", "IsAnswerNotParsed", "answer", null),
        };

        private static readonly string[] Helpers =
            { "IsUseCeiling", "IsUseInFlight", "IsQuotaExceeded", "IsOwnKeyRefused", "IsAskUnavailable", "IsAskFailed", "IsAnswerNotParsed" };

        // By name, so a helper this client lacks is a red here, not a compile error.
        private static bool Read(OWApiError e, string helper)
        {
            var p = typeof(OWApiError).GetProperty(helper);
            Assert.IsNotNull(p, $"OWApiError has no {helper}");
            return (bool)p.GetValue(e);
        }

        [Test]
        public void Ai_EachRefusalByItsCode_ThrownOnce_NeverRetried()
        {
            foreach (var (status, code, helper, param, retry) in Refusals)
            {
                var t = new FakeTransport
                {
                    Status = status,
                    ResponseBody = $@"{{""error"":{{""type"":""x"",""code"":""{code}"",""param"":{(param == null ? "null" : "\"" + param + "\"")},""message"":""m""}}}}",
                    ResponseHeaders = retry == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Retry-After"] = retry },
                };
                var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).AiAsync(Ask(), "k"), code);
                if (helper != null) Assert.IsTrue(Read(e, helper), $"{code}: {helper}");
                foreach (var other in Helpers.Where(h => h != helper)) Assert.IsFalse(Read(e, other), $"{code}: not {other}");
                Assert.AreEqual(param, e.Param, code);
                if (retry != null) Assert.AreEqual(int.Parse(retry), e.RetryAfter, code);
                Assert.AreEqual(1, t.Calls.Count, $"{code}: sent once, never retried");
            }

            // a 409 in flight on the key is thrown as it came, the same key never re-sent by the client
            var f = new FakeTransport
            {
                Status = 409,
                ResponseBody = @"{""error"":{""type"":""x"",""code"":""idempotency_in_flight"",""message"":""m""}}",
                ResponseHeaders = new Dictionary<string, string> { ["Retry-After"] = "1" },
            };
            var inFlight = Assert.ThrowsAsync<OWApiError>(() => Make(f).AiAsync(Ask(), "k"));
            Assert.AreEqual(409, (int)inFlight.Status);
            Assert.AreEqual("idempotency_in_flight", inFlight.Code);
            Assert.IsFalse(inFlight.IsUseInFlight);
            Assert.AreEqual(1, f.Calls.Count);
        }

        // The vector "isUseNotGranted is the 403 use_not_granted and isWrongAudience the 401 wrong_audience, each on its
        // own" (4.138.0), on an AI door token calling sort.
        [Test]
        public void IsUseNotGranted_IsThe403_AndIsWrongAudience_The401_EachOnItsOwn()
        {
            var cases = new (int Status, string Code, bool NotGranted, bool WrongAudience)[]
            {
                (403, "use_not_granted", true, false),
                (401, "wrong_audience", false, true),
                (403, "permission_error", false, false),
                (401, "key_revoked", false, false),
                (404, "use_not_granted", false, false),
            };

            foreach (var (status, code, notGranted, wrongAudience) in cases)
            {
                var t = new FakeTransport
                {
                    Status = status,
                    ResponseBody = $@"{{""error"":{{""type"":""permission_error"",""code"":""{code}"",""message"":""m"",""param"":""use""}}}}",
                };
                var e = Assert.ThrowsAsync<OWApiError>(
                    () => Make(t, "ow_a_narrow").AiAsync(new OWAiSortBody { WorldId = "w1", Notes = new List<OWSortNote> { "a" } }),
                    $"{status} {code}");
                Assert.AreEqual(notGranted, Read(e, "IsUseNotGranted"), $"{status} {code}");
                Assert.AreEqual(wrongAudience, Read(e, "IsWrongAudience"), $"{status} {code}");
            }
        }

        // 4.140.0's answer_not_parsed by itself: the 422 a use answers when its model was off its shape twice, not a
        // body past the use's limits, and not worth retrying.
        [Test]
        public void IsAnswerNotParsed_IsThe422AnswerNotParsed_Only_AndNotRetryable()
        {
            var parsed = new OWApiError((System.Net.HttpStatusCode)422, "m", code: "answer_not_parsed", param: "answer");
            Assert.IsTrue(Read(parsed, "IsAnswerNotParsed"));
            Assert.IsFalse(parsed.IsRetryable);
            Assert.IsFalse(Read(new OWApiError((System.Net.HttpStatusCode)422, "m", code: "invalid_request", param: "answer"), "IsAnswerNotParsed"));
            Assert.IsFalse(Read(new OWApiError((System.Net.HttpStatusCode)502, "m", code: "answer_not_parsed"), "IsAnswerNotParsed"));
        }

        // This client's own reading of the two 429s (the TypeScript SDK has no IsRetryable): a ceiling lasts until
        // midnight UTC, as quota_exceeded does, while another call in flight clears in seconds.
        [Test]
        public void TheUseCeiling_IsNotRetryable_AndAUseInFlight_Is()
        {
            Assert.IsFalse(new OWApiError((System.Net.HttpStatusCode)429, "m", code: "use_ceiling").IsRetryable);
            Assert.IsTrue(new OWApiError((System.Net.HttpStatusCode)429, "m", code: "use_in_flight").IsRetryable);
            Assert.IsFalse(new OWApiError((System.Net.HttpStatusCode)429, "m", code: "quota_exceeded").IsRetryable);
        }
    }
}
