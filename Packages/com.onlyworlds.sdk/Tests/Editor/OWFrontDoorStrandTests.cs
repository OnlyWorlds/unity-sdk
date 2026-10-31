using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The strand on the front door (schema-dist <c>profiles/strand.md</c> rule 2, serial 100): <c>GET /me</c> and an
    /// agent's join reply serve <c>reading_aids.strand</c>, the six slots of the strand as that key reads the world, six
    /// null slots when the world has none or its story is out of the key's view. The bodies are keel's
    /// <c>READING_AIDS_SCHEMA</c> (<c>accounts/front_door.py</c>) field for field; the slots its tests serve
    /// (<c>tests/test_strand_front_door.py</c>).
    /// </summary>
    public class OWFrontDoorStrandTests
    {
        private const string Base = "https://example.test/api/v2";
        private const string Code = "ow_j_EoEUrAfQ9xumy-lbBJqXix3q1xl49-P4";

        private const string FullStrand = @"{""narrative"":{""id"":""019a7f00-0000-7000-8000-000000000301"",""name"":""The Night the Lighthouse Went Dark""},
            ""event"":{""id"":""019a7f00-0000-7000-8000-000000000311"",""name"":""The lamp fails""},
            ""location"":{""id"":""019a7f00-0000-7000-8000-000000000321"",""name"":""The Lighthouse""},
            ""character"":{""id"":""019a7f00-0000-7000-8000-000000000331"",""name"":""Ilse the Keeper""},
            ""now"":{""id"":""019a7f00-0000-7000-8000-000000000322"",""name"":""The Docks""},
            ""same"":false}";

        private const string NoStrand =
            @"{""narrative"":null,""event"":null,""location"":null,""character"":null,""now"":null,""same"":null}";

        private const string Aids = @"{""charter"":{""id"":""law-1"",""name"":""House rules"",""rules"":[],""skill"":[]},
            ""guide"":{""id"":""con-g"",""name"":""Start here""},""domains"":[],""strand"":";

        private static string Me(string strand) =>
            @"{""world"":{""id"":""w1"",""name"":""W""},""scope"":""read"",""kind"":""owner"",
                ""front_door"":{""sponsor"":null,""sponsor_character"":null,""my_character"":null,""charter"":""law-1"",""charter_skill"":[]},
                ""reading_aids"":" + Aids + strand + "}}";

        private static OWClient Make(FakeTransport t) => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "0000",
            Transport = t,
            BaseUrl = Base,
        });

        [Test]
        public async Task Me_ReadsTheServedStrand_EverySlot()
        {
            var t = new FakeTransport { ResponseBody = Me(FullStrand) };
            var me = await Make(t).GetMeAsync();
            var strand = OWReadingAids.Of(me).Strand;

            Assert.IsNotNull(strand, "a served strand is read");
            Assert.IsFalse(strand.IsEmpty);
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000301", strand.Narrative.Id);
            Assert.AreEqual("The Night the Lighthouse Went Dark", strand.Narrative.Name);
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000311", strand.Event.Id);
            Assert.AreEqual("The lamp fails", strand.Event.Name);
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000321", strand.Location.Id);
            Assert.AreEqual("The Lighthouse", strand.Location.Name);
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000331", strand.Character.Id);
            Assert.AreEqual("Ilse the Keeper", strand.Character.Name);
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000322", strand.Now.Id);
            Assert.AreEqual("The Docks", strand.Now.Name);
            Assert.AreEqual(false, strand.Same);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(FullStrand), strand.ToJson()), "the six slots as served");
            Assert.IsTrue(JToken.DeepEquals(me["reading_aids"], OWReadingAids.Of(me).ToJson()),
                "read back, the packet as served, the strand in it");
        }

        [Test]
        public void Me_SixNullSlots_ReadAsAnEmptyStrand_NotAsNone()
        {
            var aids = OWReadingAids.Of(JObject.Parse(Me(NoStrand)));

            Assert.IsNotNull(aids.Strand, "six null slots are a strand served empty, not an absent key");
            Assert.IsTrue(aids.Strand.IsEmpty);
            Assert.IsNull(aids.Strand.Narrative);
            Assert.IsNull(aids.Strand.Event);
            Assert.IsNull(aids.Strand.Location);
            Assert.IsNull(aids.Strand.Character);
            Assert.IsNull(aids.Strand.Now);
            Assert.IsNull(aids.Strand.Same);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(NoStrand), aids.ToJson()["strand"]), "six nulls written back");
        }

        [Test]
        public void Me_APacketWithoutTheKey_HasNoStrand_AndWritesNone()
        {
            // A keel from before serial 100 serves {charter, guide, domains}.
            var aids = OWReadingAids.Of(JObject.Parse(
                @"{""reading_aids"":{""charter"":null,""guide"":null,""domains"":[]}}"));

            Assert.IsNotNull(aids);
            Assert.IsNull(aids.Strand, "an absent key reads as absent");
            CollectionAssert.AreEqual(new[] { "charter", "guide", "domains" },
                aids.ToJson().Properties().Select(p => p.Name).ToArray());
        }

        [Test]
        public void Me_TheStrandComesFourth()
        {
            // The charter profile's rule 2: the Charter, the Guide, the Domains, then the strand.
            var aids = OWReadingAids.Of(JObject.Parse(Me(NoStrand)));
            CollectionAssert.AreEqual(new[] { "charter", "guide", "domains", "strand" },
                aids.ToJson().Properties().Select(p => p.Name).ToArray());
        }

        [Test]
        public void Me_AServedSlotIsReadAsServed()
        {
            // Then and now one place: `same` as served. A slot served with no name reads an empty string.
            var aids = OWReadingAids.Of(JObject.Parse(Me(
                @"{""narrative"":{""id"":""n-1"",""name"":""The Crossing""},""event"":null,
                    ""location"":{""id"":""loc-1"",""name"":null},""character"":{""id"":""c-1"",""name"":""The Judge""},
                    ""now"":{""id"":""loc-1"",""name"":null},""same"":true}")));

            Assert.AreEqual(true, aids.Strand.Same);
            Assert.IsNull(aids.Strand.Event);
            Assert.AreEqual("loc-1", aids.Strand.Location.Id);
            Assert.AreEqual("", aids.Strand.Location.Name);
            Assert.AreEqual("loc-1", aids.Strand.Now.Id);
        }

        [Test]
        public async Task AnAgentsJoinAnswer_CarriesTheStrand_AsItsMeDoes()
        {
            // keel's test: the hero hidden from the new seat, so the hero's two slots and `same` are null.
            const string partial = @"{""narrative"":{""id"":""n-1"",""name"":""The Gang""},""event"":{""id"":""e-1"",""name"":""The Crossing""},
                ""location"":{""id"":""loc-1"",""name"":""The River""},""character"":null,""now"":null,""same"":null}";
            var t = new FakeTransport
            {
                Status = 201,
                ResponseBody = @"{""kind"":""agent"",
                    ""world"":{""id"":""w1"",""name"":""W"",""description"":""""},
                    ""seat"":{""id"":""s1"",""role"":""contributor"",""agent_name"":""Moss""},
                    ""with_character"":false,""character"":null,""key"":""ow_w_seat"",""pin"":""ow_s_secret"",
                    ""base_url"":""https://example.test/api/v2"",""env"":""OW_KEY=ow_w_seat"",""next"":[],""members"":[],
                    ""front_door"":{""sponsor"":null,""sponsor_character"":null,""my_character"":null,""charter"":null,""charter_skill"":[]},
                    ""reading_aids"":{""charter"":null,""guide"":null,""domains"":[],""strand"":" + partial + "}}",
            };

            var strand = (await OWJoin.RedeemAsync(Code, t, agentName: "Moss", baseUrl: Base)).ReadingAids.Strand;

            Assert.IsNotNull(strand);
            Assert.AreEqual("The Gang", strand.Narrative.Name);
            Assert.AreEqual("e-1", strand.Event.Id);
            Assert.AreEqual("The River", strand.Location.Name);
            Assert.IsNull(strand.Character);
            Assert.IsNull(strand.Now);
            Assert.IsNull(strand.Same);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(partial), strand.ToJson()));
        }
    }
}
