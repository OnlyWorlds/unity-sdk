using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's draught this client gained in its fourth tranche (<see cref="OWClient.DraughtAsync"/>): one
    /// Character's context in one read, GET /character/{id}/context. What it sends, with and without its policy, that the
    /// packet comes back as served, and what is refused before anything is sent.
    /// </summary>
    public class OWDraughtRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        // An agent client with its own name and a PIN: a read must leave the PIN and both write headers out.
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "1234")
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = Base,
                Agent = true,
                Client = "atlas",
            });

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // A compact packet: the frame, one section, a null kept (style), and a time that must stay the string it was.
        private const string Packet =
            @"{""world"":{""id"":""w1"",""name"":""Ostvik"",""description"":null},""calendar"":null,""as_of"":300,"
            + @"""now"":""2026-10-31T05:00:00.123456+00:00"",""mode"":""voice"",""reader"":""owner"","
            + @"""policy"":{""components"":[""self"",""events""]},""style"":null,"
            + @"""events"":[{""via"":[""characters""],""her_links"":2,""grade"":""whole"",""dated"":true,"
            + @"""element"":{""id"":""e1"",""type"":""event"",""name"":""The flood""}}]}";

        // The TypeScript SDK's draught(characterId) (4.47.0): GET /character/{id}/context, no query when no policy is
        // given, a read: the key without its PIN and no write header. The answer is the packet as served. Kills: the
        // wrong verb or path, the id unescaped, a query or a body sent, the PIN or a write header sent, or the packet
        // rewritten (a time re-serialized in the local zone, a null dropped, an absent section filled in).
        [Test]
        public async Task Draught_NoPolicy_GetsTheContext_NoQuery_TheKeyAlone_AndAnswersThePacketAsServed()
        {
            var t = new FakeTransport { ResponseBody = Packet };
            var packet = await Make(t).DraughtAsync("c1");

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/character/c1/context", a.Url, "the id escaped, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(a), "a read: no PIN, no write header");

            Assert.IsTrue(JToken.DeepEquals(OWJson.ParseObject(Packet), packet), "the packet as served");
            Assert.AreEqual(JTokenType.String, packet["now"].Type);
            Assert.AreEqual("2026-10-31T05:00:00.123456+00:00", (string)packet["now"]);
            Assert.AreEqual(JTokenType.Null, packet["style"].Type, "a null is an answer");
            Assert.IsNull(packet["knowledge"], "an absent section stays absent");
        }

        // The TypeScript SDK's draught(characterId, policy) (4.47.0; compact from 4.69.0, lens from 4.92.0): each key in
        // its order (components, mode, as_of, undated, since, budget, depth, compact, lens), under keel's name, the
        // components joined by commas, each value escaped. Kills: a key misnamed (asOf, as-of), the order changed, the
        // components sent once each, a value unescaped, compact false or depth 0 dropped as if unset.
        [Test]
        public async Task Draught_EveryPolicyKey_IsSentInTheTypeScriptSDKsOrder_UnderKeelsNames()
        {
            var t = new FakeTransport();
            await Make(t).DraughtAsync("c1", new OWDraughtPolicy
            {
                Components = new[] { "self", "events", "messages" },
                Mode = "act",
                AsOf = -300,
                Undated = "exclude",
                Since = "4:0196 x",
                Budget = 20,
                Depth = 0,
                Compact = false,
                Lens = "l/1",
            });

            Assert.AreEqual(Base + "/character/c1/context?components=self%2Cevents%2Cmessages&mode=act&as_of=-300"
                + "&undated=exclude&since=4%3A0196%20x&budget=20&depth=0&compact=false&lens=l%2F1", t.Last.Url);

            await Make(t).DraughtAsync("c1", new OWDraughtPolicy { Compact = true });
            Assert.AreEqual(Base + "/character/c1/context?compact=true", t.Last.Url);

            await Make(t).DraughtAsync("c1", new OWDraughtPolicy { Components = new string[0], Mode = "", Since = "", Lens = "" });
            Assert.AreEqual(Base + "/character/c1/context", t.Last.Url, "null or empty sends nothing");
        }

        // Kills: a draught sent with no Character (a path keel 404s), or a component that would corrupt the joined list.
        [Test]
        public void Draught_NoCharacter_OrAComponentThatIsEmptyOrHoldsAComma_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var c = Make(t);
            Assert.Throws<ArgumentException>(() => c.DraughtAsync(null));
            Assert.Throws<ArgumentException>(() => c.DraughtAsync(""));
            foreach (var bad in new[] { "", null, "self,events" })
            {
                var e = Assert.Throws<ArgumentException>(
                    () => c.DraughtAsync("c1", new OWDraughtPolicy { Components = new[] { "role", bad } }), bad ?? "null");
                StringAssert.Contains("Nothing was sent", e.Message);
            }

            Assert.IsEmpty(t.Calls);
        }

        // A read: a legacy key sends its PIN on every request (v1 parity), a read key none.
        [Test]
        public async Task Draught_ALegacyKeySendsItsPin_AReadKeyNone()
        {
            var t = new FakeTransport();
            await Make(t, "0123456789").DraughtAsync("c1");
            Assert.AreEqual("1234", t.Last.Headers["API-Pin"]);

            await Make(t, "ow_r_test", null).DraughtAsync("c1");
            CollectionAssert.AreEqual(new[] { "API-Key" }, HeaderNames(t.Last));
        }
    }
}
