using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The server's lens routes on the client: <see cref="OWClient.SetMyLensesAsync"/> (PUT /me/lenses) and
    /// <see cref="OWClient.WorldLensesAsync"/> (GET /world/{world_id}/lenses), and the <c>lenses</c> GET /me now serves.
    /// The TypeScript SDK's vectors (4.92.0), ported: what each call sends, what it answers, and what is refused before
    /// anything is sent.
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

        // Kills: a null sent as a list (or as no list at all) instead of refused. The TypeScript SDK refuses anything but
        // a list; here the parameter's type takes only a list, so null is the one thing left to refuse.
        [Test]
        public void SetMyLenses_NullIsRefused_NothingSent()
        {
            var t = new FakeTransport { ResponseBody = MeBody };
            var e = Assert.ThrowsAsync<ArgumentNullException>(() => Make(t).SetMyLensesAsync(null));
            StringAssert.Contains("list of lens ids", e.Message);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.IsEmpty(t.Calls);
        }

        // Kills: an account token, with World or without, or a keyless client, reaching the transport.
        [Test]
        public void SetMyLenses_AnAccountTokenOrNoKey_IsRefused_NothingSent()
        {
            foreach (var (key, world) in new[] { ("ow_a_test", (string)null), ("ow_a_test", "w1"), (null, "w1") })
            {
                var t = new FakeTransport { ResponseBody = MeBody };
                var e = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, key, null, world).SetMyLensesAsync(new[] { "l1" }),
                    $"{key ?? "no key"}, World {world ?? "null"}");
                StringAssert.Contains(key == null ? "needs a key" : "needs a world key", e.Message);
                StringAssert.Contains("Nothing was sent", e.Message);
                Assert.IsEmpty(t.Calls, $"{key ?? "no key"}, World {world ?? "null"}: nothing sent");
            }
        }

        // -- WorldLensesAsync -------------------------------------------------------------------------------

        // Kills: the world id unescaped, resolve or read_as missing or misspelt, resolve=false dropped, an empty query
        // sent as resolve=false, a body on the read, or the PIN, X-OW-Agent or X-OW-Client on it.
        [Test]
        public async Task WorldLenses_SendsGetWorldLenses_ResolveAndReadAs_TheKeyAlone_AndAnswersTheBodyWhole()
        {
            var t = new FakeTransport { ResponseBody = LensesBody };
            var c = Make(t);

            var answer = await c.WorldLensesAsync("w 1", resolve: true, readAs: "guest");
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(LensesBody), answer), "the answer as served: " + answer);

            await c.WorldLensesAsync("w1");
            await c.WorldLensesAsync("w1", resolve: false);

            Assert.AreEqual(3, t.Calls.Count);
            var a = t.Calls[0];
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual("/api/v2/world/w%201/lenses", new Uri(a.Url).AbsolutePath);
            Assert.AreEqual("resolve=true&read_as=guest", Query(a));
            Assert.IsNull(a.Body);
            Assert.AreEqual(new[] { "API-Key" }, a.Headers.Keys.ToArray(), "a read: the key alone, no PIN, no write header");

            Assert.AreEqual("", Query(t.Calls[1]), "no option, no query");
            Assert.AreEqual("resolve=false", Query(t.Calls[2]), "false is sent as false");
        }

        // Kills: the world id or read_as put in raw, so a character that means something in a URL reaches keel as
        // another path or another parameter.
        [Test]
        public async Task WorldLenses_TheWorldIdAndReadAs_AreEscaped()
        {
            var t = new FakeTransport { ResponseBody = LensesBody };
            await Make(t).WorldLensesAsync("w?1", resolve: true, readAs: "co-builder & co");
            Assert.AreEqual("/api/v2/world/w%3F1/lenses", new Uri(t.Last.Url).AbsolutePath);
            Assert.AreEqual("resolve=true&read_as=co-builder%20%26%20co", Query(t.Last));

            // An empty readAs is left out, as the TypeScript SDK's query builder leaves out an empty string.
            await Make(t).WorldLensesAsync("w1", resolve: true, readAs: "");
            Assert.AreEqual("resolve=true", Query(t.Last));
        }

        // Kills: an account token (World set or not), or a keyless client, reaching the transport: the route is a world
        // key's, not one of the world reads World opens.
        [Test]
        public void WorldLenses_AnAccountTokenOrNoKey_IsRefused_NothingSent()
        {
            foreach (var (key, world) in new[] { ("ow_a_test", (string)null), ("ow_a_test", "w1"), (null, "w1") })
            {
                var t = new FakeTransport { ResponseBody = LensesBody };
                var e = Assert.ThrowsAsync<InvalidOperationException>(() => Make(t, key, null, world).WorldLensesAsync("w1", resolve: true),
                    $"{key ?? "no key"}, World {world ?? "null"}");
                StringAssert.Contains(key == null ? "needs a key" : "needs a world key", e.Message);
                StringAssert.Contains("Nothing was sent", e.Message);
                Assert.IsEmpty(t.Calls, $"{key ?? "no key"}, World {world ?? "null"}: nothing sent");
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
