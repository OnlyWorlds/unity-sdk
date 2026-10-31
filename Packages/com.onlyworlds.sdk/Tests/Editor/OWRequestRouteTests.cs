using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's public escape hatch this client gained in its fourth tranche
    /// (<see cref="OWClient.RequestAsync(string, string, OWRequestOptions, System.Threading.CancellationToken)"/>): any
    /// route, on the client's own rules. What it adds (the key, the PIN where keel checks it, <c>world=</c>, the write
    /// headers it is asked for), what it never adds (a cleaned body), and what is refused before anything is sent.
    /// </summary>
    public class OWRequestRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        // An agent client with its own name and a PIN: every header a write could carry, so a request that must leave one
        // out is seen leaving it out.
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "1234", string world = null)
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = Base,
                World = world,
                Agent = true,
                Client = "atlas",
            });

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // The TypeScript SDK's request('GET', path, { query }) (public from 4.0.0): the base URL, the path, the query as
        // given; a read, so the key without its PIN; the body as served. Kills: the PIN on a read, a header added, the
        // query re-escaped, or the answer rewritten (a time re-serialized in the local zone).
        [Test]
        public async Task Request_AGet_SendsTheKeyWithoutItsPin_TheQueryAsGiven_AndAnswersTheBodyAsServed()
        {
            const string served = @"{""data"":[{""at"":""2026-10-31T05:00:00.5+00:00""}],""used_bytes"":3}";
            var t = new FakeTransport { ResponseBody = served };
            var answer = await Make(t).RequestAsync("GET", "/world/files", new OWRequestOptions { Query = "a=1&b=x%20y" });

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/world/files?a=1&b=x%20y", a.Url);
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(a));

            Assert.IsTrue(JToken.DeepEquals(OWJson.ParseObject(served), answer));
            Assert.AreEqual("2026-10-31T05:00:00.5+00:00", (string)answer["data"][0]["at"]);
        }

        // request('PATCH', path, { body, write, elementWrite, ifMatch, idempotencyKey }): the body exactly as given (the
        // escape hatch cleans nothing: no read-only key stripped, a null string stays null), the PIN on a write, and the
        // write headers as asked: write is X-OW-Agent on an agent client, elementWrite X-OW-Client. Kills: the body cleaned,
        // the PIN dropped, a write header sent unasked or the two flags crossed, If-Match unquoted.
        [Test]
        public async Task Request_AWrite_SendsTheBodyAsGiven_ThePin_AndTheWriteHeadersAsAsked()
        {
            var body = new JObject { ["id"] = "c1", ["name"] = null, ["created_at"] = "2026-01-01", ["x_tool"] = 1 };
            var given = body.ToString(Newtonsoft.Json.Formatting.None);
            var t = new FakeTransport();
            await Make(t).RequestAsync("PATCH", "/character/c1/", new OWRequestOptions
            {
                Body = body, Write = true, ElementWrite = true, IfMatch = 7, IdempotencyKey = "k1",
            });

            var a = t.Last;
            Assert.AreEqual("PATCH", a.Method);
            Assert.AreEqual(Base + "/character/c1/", a.Url);
            Assert.AreEqual(@"{""id"":""c1"",""name"":null,""created_at"":""2026-01-01"",""x_tool"":1}", a.Body, "as given, nothing cleaned");
            Assert.AreEqual(given, body.ToString(Newtonsoft.Json.Formatting.None), "the caller's body is not changed");
            CollectionAssert.AreEqual(
                new[] { "API-Key", "API-Pin", "Content-Type", "Idempotency-Key", "If-Match", "X-OW-Agent", "X-OW-Client" },
                HeaderNames(a));
            Assert.AreEqual("1234", a.Headers["API-Pin"]);
            Assert.AreEqual("\"7\"", a.Headers["If-Match"]);
            Assert.AreEqual("k1", a.Headers["Idempotency-Key"]);
            Assert.AreEqual("1", a.Headers["X-OW-Agent"]);
            Assert.AreEqual("atlas", a.Headers["X-OW-Client"]);

            await Make(t).RequestAsync("POST", "/character/c1/files", new OWRequestOptions { Body = new JObject(), Write = true });
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "Content-Type", "X-OW-Agent" }, HeaderNames(t.Last), "write alone");

            await Make(t).RequestAsync("POST", "/character/c1/proposals/p1/accept", new OWRequestOptions { ElementWrite = true });
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin", "X-OW-Client" }, HeaderNames(t.Last), "elementWrite alone");

            await Make(t).RequestAsync("DELETE", "/character/c1/hidden");
            CollectionAssert.AreEqual(new[] { "API-Key", "API-Pin" }, HeaderNames(t.Last), "neither: no write header");

            var body2 = new JArray(1, 2);
            await Make(t).RequestAsync("PUT", "/me/lenses", new OWRequestOptions { Body = body2 });
            Assert.AreEqual("[1,2]", t.Last.Body, "any JSON value, an array included");
        }

        // The account token's rules, kept: an /account route with its Bearer alone; any other route refused unless it is a
        // world read (WorldRead, world= from World) or a route keel declares the account token on (AccountToken);
        // AccountToken.World without World refused; Auth false with no credential. Kills: any of the client's
        // account-token rules skipped by the escape hatch.
        [Test]
        public async Task Request_OnAnAccountToken_KeepsTheClientsRules()
        {
            var t = new FakeTransport();
            await Make(t, "ow_a_acct").RequestAsync("GET", "/account/tokens");
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(t.Last));
            Assert.AreEqual("Bearer ow_a_acct", t.Last.Headers["Authorization"]);

            var e = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, "ow_a_acct").RequestAsync("GET", "/world/"));
            StringAssert.Contains("needs a world key", e.Message);
            Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, "ow_a_acct")
                .RequestAsync("GET", "/world/", new OWRequestOptions { WorldRead = true }), "a world read needs World");
            Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, "ow_a_acct")
                .RequestAsync("PUT", "/me/lenses", new OWRequestOptions { AccountToken = OWAccountTokenRoute.World }), "World needed");
            Assert.AreEqual(1, t.Calls.Count, "nothing sent by the three refusals");

            await Make(t, "ow_a_acct", world: "w9").RequestAsync("GET", "/character/", new OWRequestOptions { WorldRead = true, Query = "limit=5" });
            Assert.AreEqual(Base + "/character/?limit=5&world=w9", t.Last.Url);

            await Make(t, "ow_a_acct", world: "w9").RequestAsync("PUT", "/me/lenses", new OWRequestOptions
            {
                Body = new JObject { ["lenses"] = null }, AccountToken = OWAccountTokenRoute.World,
            });
            Assert.AreEqual(Base + "/me/lenses?world=w9", t.Last.Url);

            await Make(t, "ow_a_acct", world: "w9").RequestAsync("GET", "/world/w1/lenses", new OWRequestOptions { AccountToken = OWAccountTokenRoute.Path });
            Assert.AreEqual(Base + "/world/w1/lenses", t.Last.Url, "the path names the world: no world=");

            await Make(t, "ow_a_acct", world: "w9").RequestAsync("GET", "/health/", new OWRequestOptions { Auth = false });
            CollectionAssert.IsEmpty(t.Last.Headers.Keys, "Auth false: no credential, no world=");
            Assert.AreEqual(Base + "/health/", t.Last.Url);
        }

        // A keyless client's rules, kept: a world read with World set, with no credential; anything else refused.
        [Test]
        public async Task Request_OnAKeylessClient_ReadsTheWorldItNames_AndIsRefusedAnythingElse()
        {
            var t = new FakeTransport();
            var keyless = new OWClient(new OWClientConfig { World = "w1", Transport = t, BaseUrl = Base });
            await keyless.RequestAsync("GET", "/world/", new OWRequestOptions { WorldRead = true });
            Assert.AreEqual(Base + "/world/?world=w1", t.Last.Url);
            CollectionAssert.IsEmpty(t.Last.Headers.Keys);

            var e = Assert.ThrowsAsync<InvalidOperationException>(() => keyless.RequestAsync("GET", "/me/"));
            StringAssert.Contains("holds none", e.Message);
            Assert.ThrowsAsync<InvalidOperationException>(() => keyless.RequestAsync("POST", "/character/",
                new OWRequestOptions { Body = new JObject(), WorldRead = true }), "a world read is a GET");
            Assert.AreEqual(1, t.Calls.Count);
        }

        // Kills: a 202 on a proposable write handed back as if it were the element, or a 202 thrown when not asked; a 204
        // or an empty body thrown on; keel's refusal not shaped.
        [Test]
        public async Task Request_Answers_A202ProposalWhenProposable_NullForNoBody_AndKeelsRefusalAsAnError()
        {
            var proposal = @"{""proposal"":{""id"":""p1"",""state"":""open""}}";
            var t = new FakeTransport { Status = 202, ResponseBody = proposal };
            var e = Assert.ThrowsAsync<OWProposedError>(() => Make(t).RequestAsync("PATCH", "/character/c1/",
                new OWRequestOptions { Body = new JObject { ["name"] = "x" }, Proposable = true }));
            Assert.AreEqual("p1", e.Proposal.Id);

            var answered = await Make(t).RequestAsync("PATCH", "/character/c1/", new OWRequestOptions { Body = new JObject() });
            Assert.AreEqual("p1", (string)answered["proposal"]["id"], "not proposable: the 202's body as served");

            t = new FakeTransport { Status = 204, ResponseBody = "" };
            Assert.IsNull(await Make(t).RequestAsync("DELETE", "/character/c1/"));
            t = new FakeTransport { Status = 200, ResponseBody = "" };
            Assert.IsNull(await Make(t).RequestAsync("POST", "/x"));

            t = new FakeTransport { Status = 404, ResponseBody = @"{""error"":{""type"":""not_found"",""code"":""not_found"",""message"":""No.""}}" };
            var no = Assert.ThrowsAsync<OWApiError>(() => Make(t).RequestAsync("GET", "/character/c9/"));
            Assert.IsTrue(no.IsNotFound);
        }

        // Kills: a method keel serves none of, a path that is no path of the base URL (relative, empty, carrying its own
        // query or a fragment), a body on a GET (fetch refuses one, so the TypeScript SDK never sends it) or a negative
        // If-Match reaching the transport.
        [Test]
        public void Request_AMethodOrPathItCannotSend_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var c = Make(t);
            foreach (var method in new[] { null, "", "get", "HEAD", "OPTIONS" })
            {
                var e = Assert.Throws<ArgumentException>(() => c.RequestAsync(method, "/world/"), method ?? "null");
                StringAssert.Contains("Nothing was sent", e.Message);
            }

            foreach (var path in new[] { null, "", "world/", "/world/?a=1", "/world/#x", "https://evil.example/x" })
                Assert.Throws<ArgumentException>(() => c.RequestAsync("GET", path), path ?? "null");

            Assert.Throws<ArgumentException>(() => c.RequestAsync("GET", "/world/", new OWRequestOptions { Body = new JObject() }));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.RequestAsync("DELETE", "/character/c1/", new OWRequestOptions { IfMatch = -1 }));
            Assert.IsEmpty(t.Calls);
        }
    }
}
