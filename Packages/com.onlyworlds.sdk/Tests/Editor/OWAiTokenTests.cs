using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The AI door token (the TypeScript SDK's <c>test/ai-token.test.mjs</c>, 4.138.0):
    /// <see cref="OWClient.CompleteSignInAsync"/>'s AI uses and lifetime (sent in the code exchange, the answer's
    /// <c>ai_token</c> read as <see cref="OWSignInResult.AiToken"/>) and <see cref="OWClient.MintAiTokenAsync"/> (POST
    /// /account/tokens with <c>ai_uses</c>). Every list the SDK can see is wrong is refused before anything is sent, and in
    /// the sign-in before the pending sign-in is touched, so the code is never spent on it.
    /// </summary>
    public class OWAiTokenTests
    {
        private const string Base = "https://www.onlyworlds.com/api/v2";
        private const string Visit = "https://subs.onlyworlds.com/visit/";

        private static OWClient Bare(FakeTransport t) => new OWClient(new OWClientConfig { Transport = t, BaseUrl = Base });

        private static OWSignInBegun Begun() => Bare(new FakeTransport()).SignInWithOnlyWorlds("subs", Visit);

        private static string Back(OWSignInBegun p) => $"{Visit}?code=c1&state={p.State}";

        private const string Signed =
            @"""token"":""ow_a_acct"",""token_id"":""t1"",""token_type"":""bearer"",""expires_at"":null,""account"":{""id"":""u1"",""username"":""ann""}";

        private const string Ai =
            @"{""token"":""ow_a_narrow"",""token_id"":""t2"",""token_type"":""bearer"",""audience"":""ai"",""ai_uses"":[""ask"",""sort""],"
            + @"""expires_at"":""2026-11-10T00:00:00+00:00""}";

        // The vector "completeSignIn with aiUses: the exchange sends ai_uses and ai_lifetime_minutes, and aiToken comes back
        // beside the account token".
        [Test]
        public async Task CompleteSignIn_WithAiUses_SendsBoth_AndTheAiTokenComesBackBesideTheAccountToken()
        {
            var p = Begun();
            var t = new FakeTransport { ResponseBody = "{" + Signed + @",""ai_token"":" + Ai + "}" };
            var got = await Bare(t).CompleteSignInAsync(p, Back(p), aiUses: new[] { "ask", "sort" }, aiLifetimeMinutes: 480);

            var sent = JObject.Parse(t.Last.Body);
            CollectionAssert.AreEqual(new[] { "ask", "sort" }, sent["ai_uses"].Values<string>());
            Assert.AreEqual(480, (int)sent["ai_lifetime_minutes"]);
            CollectionAssert.AreEquivalent(
                new[] { "ai_lifetime_minutes", "ai_uses", "client_id", "code", "code_verifier", "grant_type", "redirect_uri" },
                sent.Properties().Select(x => x.Name));
            Assert.AreEqual("ow_a_acct", got.Token);
            Assert.AreEqual("t1", got.TokenId);

            var ai = got.AiToken;
            Assert.IsNotNull(ai);
            Assert.AreEqual(("ow_a_narrow", "t2", "bearer", "ai", "2026-11-10T00:00:00+00:00"),
                (ai.Token, ai.TokenId, ai.TokenType, ai.Audience, ai.ExpiresAt));
            CollectionAssert.AreEqual(new[] { "ask", "sort" }, ai.AiUses);
        }

        // The vector "completeSignIn without aiUses sends neither key, and aiToken is null even when the server answers one".
        [Test]
        public async Task CompleteSignIn_WithoutAiUses_SendsNeither_AndTheAiTokenIsNull_EvenWhenAnswered()
        {
            var p = Begun();
            var t = new FakeTransport { ResponseBody = "{" + Signed + @",""ai_token"":" + Ai + "}" };
            var got = await Bare(t).CompleteSignInAsync(p, Back(p));

            var sent = JObject.Parse(t.Last.Body);
            Assert.IsFalse(sent.ContainsKey("ai_uses") || sent.ContainsKey("ai_lifetime_minutes"));
            Assert.IsNull(got.AiToken);
            Assert.AreEqual("ow_a_acct", got.Token);
        }

        // The vector "completeSignIn with aiUses and a lifetime left out sends ai_uses only".
        [Test]
        public async Task CompleteSignIn_WithAiUses_NoLifetime_SendsTheUsesOnly()
        {
            var p = Begun();
            var t = new FakeTransport { ResponseBody = "{" + Signed + @",""ai_token"":" + Ai + "}" };
            await Bare(t).CompleteSignInAsync(p, Back(p), aiUses: new[] { "ask" });

            var sent = JObject.Parse(t.Last.Body);
            CollectionAssert.AreEqual(new[] { "ask" }, sent["ai_uses"].Values<string>());
            Assert.IsFalse(sent.ContainsKey("ai_lifetime_minutes"));
        }

        // The vector "completeSignIn asked for an AI token the answer lacks or malforms: the account token stands, aiToken
        // is null".
        [Test]
        public async Task CompleteSignIn_AnAiTokenTheAnswerLacksOrMalforms_TheAccountTokenStands_TheAiTokenNull()
        {
            var bodies = new[]
            {
                "{" + Signed + "}",
                "{" + Signed + @",""ai_token"":null}",
                "{" + Signed + @",""ai_token"":{}}",
                "{" + Signed + @",""ai_token"":" + Ai.Replace(@"""token"":""ow_a_narrow""", @"""token"":""""") + "}",
                "{" + Signed + @",""ai_token"":" + Ai.Replace(@"""token_id"":""t2""", @"""token_id"":7") + "}",
                "{" + Signed + @",""ai_token"":" + Ai.Replace(@"""ai_uses"":[""ask"",""sort""]", @"""ai_uses"":""ask""") + "}",
                "{" + Signed + @",""ai_token"":" + Ai.Replace(@"""ai_uses"":[""ask"",""sort""]", @"""ai_uses"":[1]") + "}",
                "{" + Signed + @",""ai_token"":" + Ai.Replace(@"""expires_at"":""2026-11-10T00:00:00+00:00""", @"""expires_at"":null") + "}",
            };

            foreach (var body in bodies)
            {
                var p = Begun();
                var got = await Bare(new FakeTransport { ResponseBody = body }).CompleteSignInAsync(p, Back(p), aiUses: new[] { "ask" });
                Assert.AreEqual("ow_a_acct", got.Token, body);
                Assert.IsNull(got.AiToken, body);
            }
        }

        // The TypeScript SDK's BAD table, the cases a C# signature can carry (a string for the list, a number for a name,
        // a fraction, a boolean or a string for the minutes are this client's compile errors), and a null name.
        private static readonly (string[] Uses, int? Minutes, string Words)[] Bad =
        {
            (new string[0], null, "1 to 8"),
            (new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" }, null, "1 to 8"),
            (new[] { "" }, null, "not an AI use"),
            (new[] { "ask/../sort" }, null, "not an AI use"),
            (new[] { ".ask" }, null, "not an AI use"),
            (new string[] { null }, null, "not an AI use"),
            (new[] { "ask" }, 0, "1 to 1440"),
            (new[] { "ask" }, 1441, "1 to 1440"),
            (null, 60, "send aiUses too"),
        };

        // The vector "completeSignIn refuses an AI token list it can see is wrong before sending, and leaves the sign-in
        // pending".
        [Test]
        public void CompleteSignIn_AnAiTokenListItCanSeeIsWrong_IsRefusedBeforeSending_TheSignInStaysPending()
        {
            foreach (var (uses, minutes, words) in Bad)
            {
                var what = (uses == null ? "null" : "[" + string.Join(",", uses) + "]") + " " + minutes;
                var p = Begun();
                var t = new FakeTransport { ResponseBody = "{" + Signed + "}" };
                var e = Assert.Throws<ArgumentException>(() => Bare(t).CompleteSignInAsync(p, Back(p), aiUses: uses, aiLifetimeMinutes: minutes), what);
                StringAssert.Contains(words, e.Message, what);
                StringAssert.Contains("Nothing was sent", e.Message, what);
                Assert.AreEqual(0, t.Calls.Count, what);
                Assert.IsTrue(p.Pending, $"{what}: the pending sign-in is kept");
            }
        }

        // The vector "completeSignIn: the server's 422 for a use it does not run reads as failed, with its status and code".
        [Test]
        public void CompleteSignIn_TheServers422ForAUseItDoesNotRun_ReadsAsFailed_WithItsStatusAndCode()
        {
            var p = Begun();
            var t = new FakeTransport
            {
                Status = 422,
                ResponseBody = @"{""error"":{""type"":""invalid_request_error"",""code"":""invalid_request"",""message"":""No AI use is named 'nope'"",""param"":""ai_uses""}}",
            };
            var e = Assert.ThrowsAsync<OWSignInError>(() => Bare(t).CompleteSignInAsync(p, Back(p), aiUses: new[] { "nope" }));
            Assert.AreEqual(OWSignInReason.Failed, e.Reason);
            Assert.AreEqual(422, e.Status);
            Assert.AreEqual("invalid_request", e.Code);
            Assert.IsFalse(p.Pending, "the code is spent: the exchange answered");
        }

        // The TypeScript SDK's 4.141.0 vector: the 422 keeps the field the server named (ai_uses, ai_lifetime_minutes),
        // and none when it named none or an empty one. Read by name, so its absence is a red here, not a compile error.
        [Test]
        public void CompleteSignIn_TheServers422_KeepsTheParamItNamed()
        {
            var cases = new (string Sent, string Want)[] { ("\"ai_uses\"", "ai_uses"), ("\"ai_lifetime_minutes\"", "ai_lifetime_minutes"), (null, null), ("\"\"", null), ("null", null) };
            foreach (var (sent, want) in cases)
            {
                var p = Begun();
                var param = sent == null ? "" : @",""param"":" + sent;
                var t = new FakeTransport
                {
                    Status = 422,
                    ResponseBody = @"{""error"":{""type"":""invalid_request_error"",""code"":""invalid_request"",""message"":""refused""" + param + "}}",
                };
                var e = Assert.ThrowsAsync<OWSignInError>(() => Bare(t).CompleteSignInAsync(p, Back(p), aiUses: new[] { "nope" }), sent ?? "absent");
                Assert.AreEqual((OWSignInReason.Failed, (int?)422, "invalid_request"), (e.Reason, e.Status, e.Code), sent ?? "absent");
                var prop = typeof(OWSignInError).GetProperty("Param");
                Assert.IsNotNull(prop, "OWSignInError has no Param");
                Assert.AreEqual(want, prop.GetValue(e), sent ?? "absent");
            }
        }

        private const string Minted =
            @"{""id"":""t3"",""name"":""subs, AI uses"",""last4"":""abcd"",""last_used_at"":null,""revoked"":false,"
            + @"""expires_at"":""2026-11-10T00:00:00+00:00"",""keys_minted"":0,""audience"":""ai"",""ai_uses"":[""ask""],""token"":""ow_a_minted""}";

        private static OWClient Account(FakeTransport t, string key = "ow_a_acct")
            => new OWClient(new OWClientConfig { ApiKey = key, ApiPin = "1234", Transport = t, BaseUrl = Base, Agent = true, Client = "subs" });

        // The vector "mintAiToken: POST /account/tokens with ai_uses, the lifetime and the name as given, the row with its
        // token as served".
        [Test]
        public async Task MintAiToken_PostsAccountTokens_WithTheUsesTheLifetimeAndTheName_TheRowWithItsToken()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Minted };
            var c = Account(t);
            var got = await c.MintAiTokenAsync(new[] { "ask" }, lifetimeMinutes: 60, name: "night");

            var r = t.Calls.Single();
            Assert.AreEqual(("POST", Base + "/account/tokens"), (r.Method, r.Url));
            Assert.AreEqual(@"{""ai_uses"":[""ask""],""ai_lifetime_minutes"":60,""name"":""night""}", r.Body);
            Assert.AreEqual("Bearer ow_a_acct", r.Headers["Authorization"]);
            CollectionAssert.AreEquivalent(new[] { "Authorization", "Content-Type" }, r.Headers.Keys, "no PIN, no write header");

            Assert.AreEqual(("t3", "subs, AI uses", "abcd", false), (got.Id, got.Name, got.Last4, got.Revoked));
            Assert.IsNull(got.LastUsedAt);
            Assert.AreEqual(("2026-11-10T00:00:00+00:00", 0L, "ai", "ow_a_minted"), (got.ExpiresAt, got.KeysMinted, got.Audience, got.Token));
            CollectionAssert.AreEqual(new[] { "ask" }, got.AiUses);

            await c.MintAiTokenAsync(new[] { "ask" });
            Assert.AreEqual(@"{""ai_uses"":[""ask""]}", t.Last.Body);
        }

        // The vector "mintAiToken refuses before sending: a wrong list, a lifetime out of range, a bad name, an unknown
        // option, a world key"; no uses at all is never a POST that would mint a full account token.
        [Test]
        public async Task MintAiToken_RefusesBeforeSending()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = Minted };
            var c = Account(t);
            foreach (var (uses, minutes, words) in Bad)
            {
                var what = (uses == null ? "null" : "[" + string.Join(",", uses) + "]") + " " + minutes;
                var e = Assert.Throws<ArgumentException>(() => c.MintAiTokenAsync(uses, minutes), what);
                StringAssert.Contains(uses == null ? "1 to 8" : words, e.Message, what);
                StringAssert.Contains("Nothing was sent", e.Message, what);
            }

            var named = Assert.Throws<ArgumentException>(() => c.MintAiTokenAsync(new[] { "ask" }, name: new string('x', 256)));
            StringAssert.Contains("name", named.Message);
            Assert.AreEqual("name", named.ParamName);

            Assert.Throws<ArgumentException>(() => c.MintAiTokenAsync(null), "no uses at all");
            Assert.Throws<ArgumentException>(() => c.MintAiTokenAsync(null, name: "x"), "no uses, a name");

            var keyed = Assert.Throws<InvalidOperationException>(() => Account(t, "ow_w_key").MintAiTokenAsync(new[] { "ask" }));
            StringAssert.Contains("needs an account token", keyed.Message);
            Assert.AreEqual(0, t.Calls.Count);

            // 255 characters, counted as keel counts them, pass: an astral character is one, as in keel.
            await c.MintAiTokenAsync(new[] { "ask" }, name: string.Concat(Enumerable.Repeat("\U0001F30A", 255)));
            Assert.AreEqual(1, t.Calls.Count);
        }
    }
}
