using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The server's lens routes on the client: <see cref="OWClient.SetMyLensesAsync"/> (PUT /me/lenses) and
    /// <see cref="OWClient.WorldLensesAsync"/> (GET /world/{world_id}/lenses), on a world key and on an account token,
    /// <see cref="OWClient.SetMemberLensesAsync"/> (PATCH /account/worlds/{world_id}/members/{member_id}), and the
    /// <c>lenses</c> GET /me now serves. The TypeScript SDK's vectors (4.92.0 and 4.94.0), ported: what each call sends,
    /// what it answers, and what is refused before anything is sent.
    /// </summary>
    public class OWLensRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        private const string MeBody =
            @"{""world"":{""id"":""w"",""name"":""W""},""kind"":""person"",""role"":""contributor"",""membership"":""m1"",""character"":null,""lenses"":[""l2"",""l1""]}";

        private const string LensesBody =
            @"{""data"":[{""id"":""l1"",""name"":""Harbour watch"",""element"":{""id"":""l1"",""type"":""construct"",""name"":""Harbour watch""},""template"":false,""retired"":false,""x_lens"":null,""titles"":[],""chosen"":true}],""read_as"":""guest""}";

        // An agent client with its own name: the headers it would send on an element write are the ones these routes leave out.
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

        private static string Query(OWRequest sent) => new Uri(sent.Url).Query.TrimStart('?');

        // -- SetMyLensesAsync -------------------------------------------------------------------------------

        // Kills: the ids reordered or wrapped, the wrong verb or path, the PIN left off, X-OW-Agent or X-OW-Client sent
        // (keel's document declares neither on PUT /me/lenses), or an answer other than GET /me's body as served.
        [Test]
        public async Task SetMyLenses_SendsPutMeLenses_TheIdsInOrder_WithThePin_NoAgentOrClientHeader_AndAnswersTheMeBody()
        {
            var t = new FakeTransport { ResponseBody = MeBody };
            var c = Make(t);

            var me = await c.SetMyLensesAsync(new[] { "l2", "l1" });
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(MeBody), me), "the /me body as served: " + me);
            Assert.AreEqual(new[] { "l2", "l1" }, me["lenses"].Values<string>().ToArray());

            await c.SetMyLensesAsync(new string[0]);

            Assert.AreEqual(2, t.Calls.Count);
            var a = t.Calls[0];
            Assert.AreEqual("PUT", a.Method);
            Assert.AreEqual("/api/v2/me/lenses", new Uri(a.Url).AbsolutePath);
            Assert.AreEqual("", Query(a));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""lenses"":[""l2"",""l1""]}"), JObject.Parse(a.Body)), a.Body);
            Assert.AreEqual("ow_w_test", a.Headers["API-Key"]);
            Assert.AreEqual("1234", a.Headers["API-Pin"]);
            Assert.AreEqual("application/json", a.Headers["Content-Type"]);
            Assert.IsFalse(a.Headers.ContainsKey("X-OW-Agent"), "keel's document declares no X-OW-Agent here");
            Assert.IsFalse(a.Headers.ContainsKey("X-OW-Client"), "keel's document declares no X-OW-Client here");

            // An empty list is a choice: sent as one, never left out.
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""lenses"":[]}"), JObject.Parse(t.Calls[1].Body)), t.Calls[1].Body);
        }

        // The TypeScript SDK 4.99.0's vector: null puts the holder's list back to never chosen, sent as { lenses: null },
        // which keel tells apart from an empty list (a choice). Kills: a null refused (as before 0.27.0), sent as [] or
        // left out of the body, on a world key or on an account token.
        [Test]
        public async Task SetMyLenses_Null_IsSentAsNeverChosen_OnAWorldKeyAndAnAccountToken()
        {
            var t = new FakeTransport { ResponseBody = MeBody };
            await Make(t).SetMyLensesAsync(null);
            await Make(t, "ow_a_test", null, "w1").SetMyLensesAsync(null);

            Assert.AreEqual(2, t.Calls.Count);
            foreach (var a in t.Calls)
            {
                Assert.AreEqual("PUT", a.Method);
                Assert.AreEqual("/api/v2/me/lenses", new Uri(a.Url).AbsolutePath);
                Assert.AreEqual(@"{""lenses"":null}", a.Body, "null, not [] and not left out");
            }
        }

        // The TypeScript SDK 4.94.0's vector: an account token sets its own list in the world World names, sending
        // world=<id> and its Bearer, no API-Key, no PIN (even one configured), no X-OW-Agent or X-OW-Client. Kills: the
        // account token refused as a world key's call, world= left off or taken from anywhere but World, or a PIN sent.
        [Test]
        public async Task SetMyLenses_OnAnAccountToken_SendsTheBearerAndWorld_NoKeyNoPin()
        {
            var t = new FakeTransport { ResponseBody = MeBody };
            var c = Make(t, "ow_a_test", "1234", "w 1");

            var me = await c.SetMyLensesAsync(new[] { "l1" });
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(MeBody), me), "the /me body as served: " + me);

            Assert.AreEqual(1, t.Calls.Count);
            var a = t.Calls[0];
            Assert.AreEqual("PUT", a.Method);
            Assert.AreEqual("/api/v2/me/lenses", new Uri(a.Url).AbsolutePath);
            Assert.AreEqual("world=w%201", Query(a));
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""lenses"":[""l1""]}"), JObject.Parse(a.Body)), a.Body);
            CollectionAssert.AreEquivalent(new[] { "Authorization", "Content-Type" }, a.Headers.Keys.ToArray());
            Assert.AreEqual("Bearer ow_a_test", a.Headers["Authorization"]);
        }

        // Kills: an account token without World sending (keel would have no world to set the list in), or a keyless client,
        // World set or not, reaching the transport: a write needs a credential.
        [Test]
        public void SetMyLenses_AnAccountTokenWithoutWorld_OrNoKey_IsRefused_NothingSent()
        {
            var t = new FakeTransport { ResponseBody = MeBody };
            var e = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, "ow_a_test", null).SetMyLensesAsync(new[] { "l1" }));
            StringAssert.Contains("names the world it sends to with World: set it", e.Message);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.IsEmpty(t.Calls, "an account token without World: nothing sent");

            var keyless = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, null, null, "w1").SetMyLensesAsync(new[] { "l1" }));
            StringAssert.Contains("needs a key", keyless.Message);
            Assert.IsEmpty(t.Calls, "a keyless client: nothing sent");
        }

        // -- WorldLensesAsync -------------------------------------------------------------------------------

        // Kills: the world id unescaped, resolve or read_as missing or misspelt, resolve=false dropped, an empty query
        // sent as resolve=false, a body on the read, or the PIN, X-OW-Agent or X-OW-Client on it.
        [Test]
        public async Task WorldLenses_SendsGetWorldLenses_ResolveAndReadAs_TheKeyAlone_AndAnswersTheBodyWhole()
        {
            var t = new FakeTransport { ResponseBody = LensesBody };
            var c = Make(t);

            var answer = await c.WorldLensesAsync("w1", resolve: true, readAs: "guest");
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(LensesBody), answer), "the answer as served: " + answer);

            await c.WorldLensesAsync("w1");
            await c.WorldLensesAsync("w1", resolve: false);

            Assert.AreEqual(3, t.Calls.Count);
            var a = t.Calls[0];
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual("/api/v2/world/w1/lenses", new Uri(a.Url).AbsolutePath);
            Assert.AreEqual("resolve=true&read_as=guest", Query(a));
            Assert.IsNull(a.Body);
            Assert.AreEqual(new[] { "API-Key" }, a.Headers.Keys.ToArray(), "a read: the key alone, no PIN, no write header");

            Assert.AreEqual("", Query(t.Calls[1]), "no option, no query");
            Assert.AreEqual("resolve=false", Query(t.Calls[2]), "false is sent as false");
        }

        // Kills: read_as put in raw, so a character that means something in a URL reaches keel as another parameter. A
        // world id that is not one segment is refused before sending (OWPathSegmentTests); a dot inside goes as given.
        [Test]
        public async Task WorldLenses_TheWorldIdAndReadAs_AreEscaped()
        {
            var t = new FakeTransport { ResponseBody = LensesBody };
            await Make(t).WorldLensesAsync("w.1", resolve: true, readAs: "co-builder & co");
            Assert.AreEqual("/api/v2/world/w.1/lenses", new Uri(t.Last.Url).AbsolutePath);
            Assert.AreEqual("resolve=true&read_as=co-builder%20%26%20co", Query(t.Last));

            // An empty readAs is left out, as the TypeScript SDK's query builder leaves out an empty string.
            await Make(t).WorldLensesAsync("w1", resolve: true, readAs: "");
            Assert.AreEqual("resolve=true", Query(t.Last));
        }

        // The TypeScript SDK 4.94.0's vector: an account token reads the world its path names, the Bearer alone, and no
        // world= with or without the client's World (the path names the world). Kills: the account token refused, or
        // World's id added beside the path's.
        [Test]
        public async Task WorldLenses_OnAnAccountToken_SendsTheBearerAlone_TheWorldInThePath_NoWorldQuery()
        {
            foreach (var world in new[] { null, "other" })
            {
                var t = new FakeTransport { ResponseBody = LensesBody };
                var answer = await Make(t, "ow_a_test", null, world).WorldLensesAsync("w1", resolve: true);
                Assert.IsTrue(JToken.DeepEquals(JObject.Parse(LensesBody), answer), "the answer as served: " + answer);

                var a = t.Calls.Single();
                Assert.AreEqual("GET", a.Method);
                Assert.AreEqual("/api/v2/world/w1/lenses", new Uri(a.Url).AbsolutePath);
                Assert.AreEqual("resolve=true", Query(a), $"World {world ?? "null"}: no world=");
                Assert.AreEqual(new[] { "Authorization" }, a.Headers.Keys.ToArray(), "the Bearer alone");
                Assert.AreEqual("Bearer ow_a_test", a.Headers["Authorization"]);
            }
        }

        // Kills: a keyless client, World set or not, reaching the transport: the lens list is never a public read.
        [Test]
        public void WorldLenses_NoKey_IsRefused_NothingSent()
        {
            var t = new FakeTransport { ResponseBody = LensesBody };
            var e = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, null, null, "w1").WorldLensesAsync("w1", resolve: true));
            StringAssert.Contains("needs a key", e.Message);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.IsEmpty(t.Calls);
        }

        // -- SetMemberLensesAsync ---------------------------------------------------------------------------

        // The TypeScript SDK 4.94.0's vector: PATCH /account/worlds/{world_id}/members/{member_id} with { lenses } alone,
        // the Bearer and nothing else, both ids escaped, 'owner' as a member id, an empty list sent as one, the answer as
        // served. Kills: a role, Character or sponsor key sent beside lenses, an id put in raw, the wrong verb or path, a
        // world=, an API-Key, PIN, X-OW-Agent or X-OW-Client header, or an answer reshaped.
        [Test]
        public async Task SetMemberLenses_SendsPatchAccountMember_LensesAlone_TheBearer_AndAnswersTheRowAsServed()
        {
            const string row = @"{""id"":""m1"",""role"":""guest"",""kind"":""agent"",""lenses"":[""l2"",""l1""],""x_served_trimmed"":[""lenses""]}";
            var t = new FakeTransport { ResponseBody = row };
            var c = Make(t, "ow_a_test", "1234", "w9");

            var answer = await c.SetMemberLensesAsync("w1", "m1", new[] { "l2", "l1" });
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(row), answer), "the row as served: " + answer);
            await c.SetMemberLensesAsync("w1", "owner", new string[0]);

            Assert.AreEqual(2, t.Calls.Count);
            var a = t.Calls[0];
            Assert.AreEqual("PATCH", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w1/members/m1", a.Url, "both ids escaped, no query");
            Assert.AreEqual(@"{""lenses"":[""l2"",""l1""]}", a.Body, "exactly { lenses }");
            CollectionAssert.AreEquivalent(new[] { "Authorization", "Content-Type" }, a.Headers.Keys.ToArray(),
                "the Bearer, no key, PIN, agent or client header");
            Assert.AreEqual("Bearer ow_a_test", a.Headers["Authorization"]);

            var b = t.Calls[1];
            Assert.AreEqual(Base + "/account/worlds/w1/members/owner", b.Url);
            Assert.AreEqual(@"{""lenses"":[]}", b.Body, "an empty list is a choice, sent as one");
        }

        // The TypeScript SDK 4.99.0's vector: null puts the row back to never chosen, { lenses: null } and nothing else.
        // Kills: a null refused (as before 0.27.0), sent as [] or left out of the body.
        [Test]
        public async Task SetMemberLenses_Null_IsSentAsNeverChosen()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""m1"",""role"":""guest"",""kind"":""agent"",""lenses"":[]}" };
            await Make(t, "ow_a_test", null).SetMemberLensesAsync("w1", "m1", null);

            var a = t.Calls.Single();
            Assert.AreEqual("PATCH", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w1/members/m1", a.Url);
            Assert.AreEqual(@"{""lenses"":null}", a.Body, "null, not [] and not left out");
        }

        // Kills: a world key, a read key, a legacy key or a keyless client reaching the transport.
        [Test]
        public void SetMemberLenses_AnyKeyButAnAccountToken_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            foreach (var key in new[] { "ow_w_test", "ow_r_test", "0123456789", null })
            {
                var refused = Assert.ThrowsAsync<InvalidOperationException>(
                    () => Make(t, key, null, key == null ? "w1" : null).SetMemberLensesAsync("w1", "m1", new[] { "l1" }), key ?? "no key");
                StringAssert.Contains("SetMemberLensesAsync needs an account token (ow_a_)", refused.Message, key ?? "no key");
                StringAssert.Contains("Nothing was sent", refused.Message, key ?? "no key");
                Assert.IsEmpty(t.Calls, $"{key ?? "no key"}: nothing sent");
            }
        }

        // The TypeScript SDK fences its options object (an unknown key, a resolve that is not a boolean, each a TypeError
        // before sending). Here the parameters are the fence: resolve is a bool? and readAs a string, and there is no
        // other option to misspell. Kills: a loosened parameter that would let anything else through.
        [Test]
        public void WorldLenses_TheOptionsAreTyped_ResolveABoolAndReadAsAString()
        {
            var m = typeof(OWClient).GetMethod(nameof(OWClient.WorldLensesAsync));
            Assert.IsNotNull(m);
            var p = m.GetParameters();
            Assert.AreEqual(new[] { "worldId", "resolve", "readAs", "ct" }, p.Select(x => x.Name).ToArray());
            Assert.AreEqual(typeof(bool?), p[1].ParameterType);
            Assert.AreEqual(typeof(string), p[2].ParameterType);
        }

        // -- The lenses GET /me serves ----------------------------------------------------------------------

        // GET /me answers a JObject, as served: lenses arrives in it, in the holder's order, and an empty list stays an
        // empty list (a choice), which OWLens.LensesFor then prefers over a Character's own list. Kills: a reader that
        // drops the key, reorders it, or reads an empty choice as no choice.
        [Test]
        public async Task GetMe_CarriesLenses_AsServed_AndAnEmptyChoiceStaysAChoice()
        {
            var t = new FakeTransport { ResponseBody = MeBody };
            var me = await Make(t).GetMeAsync();
            Assert.AreEqual(new[] { "l2", "l1" }, me["lenses"].Values<string>().ToArray());

            t.ResponseBody = @"{""kind"":""person"",""lenses"":[]}";
            var none = await Make(t).GetMeAsync();
            Assert.AreEqual(JTokenType.Array, none["lenses"].Type);
            Assert.IsEmpty(OWLens.LensesFor(none["lenses"].Values<string>(), new[] { "l9" }), "an empty choice wins");
        }
    }
}
