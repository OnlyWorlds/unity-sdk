using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The charter profile's conformance fixture (schema-dist <c>profiles/charter.fixture.json</c>, vendored byte for byte
    /// in <c>Tests/Editor/Fixtures/charter/</c> from dist.84), replayed case by case: the pair-and-mark test
    /// (<see cref="OWRoom.IsCharterElement"/>) on the <c>element</c> cases, and the packet
    /// (<see cref="OWCharter.CharterOf"/>, <see cref="OWCharter.DomainsOf"/>) on the <c>charter</c> cases, over the
    /// fixture's named worlds and the worlds a case carries. The profile's tables (the pairs, the mark, the rules field,
    /// the skill's limit, the hub fields) are held equal to the fixture's and to the generated models. keel's reader, the
    /// TypeScript SDK and the Python package answer the same cases. When the dist moves the fixture, re-vendor it and
    /// update the hash below.
    /// </summary>
    /// <remarks>
    /// A fixture world lists its elements as its owner is served them and, apart, the ids its hidden mark keeps; the world
    /// handed to the reader here carries that mark on each of them (<c>hidden: true</c>), as <see cref="OWLensTests"/>
    /// builds its worlds, and a world's <c>open</c> is the reader's <c>open</c> argument.
    /// </remarks>
    public class OWCharterTests
    {
        /// <summary>The fixture's sha256 as dist.84's MANIFEST.json records it.</summary>
        private const string DistSha256 = "566204bdaebab8d3ac15be7b6ea5ab8b83ed393436c21c3a7ad4d3b7314980c6";

        /// <summary>The fixture's cases by kind, as dist.84 holds them (23 in all).</summary>
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>
        {
            ["element"] = 5, ["charter"] = 18,
        };

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "charter", "charter.fixture.json");
            }
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = FixturePath;
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        /// <summary>One case per fixture case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Charter_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Charter_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored charter fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.84's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist84sCases()
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(23, cases.Count, "dist.84's fixture holds 23 cases");
            var counts = cases.GroupBy(c => (string)c["kind"]).ToDictionary(g => g.Key, g => g.Count());
            CollectionAssert.AreEquivalent(Counts, counts);
        }

        [Test]
        public void TheTables_AreTheFixtures_AndTheGeneratedModels()
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            var f = Fixture;
            Assert.AreEqual("charter", (string)f["profile"]);
            var pairs = f["pairs"].Cast<JObject>().Select(p => ((string)p["type"], (string)p["supertype"])).ToList();
            CollectionAssert.AreEqual(pairs, OWRoom.ReadingAidPairs.ToList());
            Assert.AreEqual((string)f["mark"]["field"], OWRoom.ReadingAidMarkField);
            Assert.AreEqual((string)f["rules_field"], OWCharter.RulesField);
            Assert.AreEqual((int)f["skill_max"], OWCharter.SkillMax);

            var hubs = f["hub_fields"].Cast<JObject>().Select(h => ((string)h["field"], (string)h["type"])).ToList();
            CollectionAssert.AreEqual(hubs, OWCharter.HubFields.ToList());
            CollectionAssert.AreEqual(OWLens.LinkFieldsOf("construct").Select(l => l.field).ToList(),
                OWCharter.HubFields.Select(h => h.field).ToList(), "the hub fields are the Construct's link fields in the schema's order");
            foreach (var (_, type) in OWCharter.HubFields) CollectionAssert.Contains(OWElementTypes.Slugs, type);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var kind = (string)c["kind"];
            switch (kind)
            {
                case "element":
                    Assert.AreEqual((bool)c["is_charter_element"], OWRoom.IsCharterElement((JObject)c["element"]),
                        $"case {name}: {c["element"].ToString(Formatting.None)}");
                    return;
                case "charter":
                {
                    var fixtureWorld = WorldNamed(c["world"]);
                    var reader = ReaderOf(fixtureWorld, (string)c["reader"]);
                    var open = fixtureWorld["open"]?.Type == JTokenType.Boolean && (bool)fixtureWorld["open"];
                    var want = (JObject)c["want"];

                    var got = OWCharter.CharterOf(WorldOf(fixtureWorld), reader, open).ToJson();
                    Assert.IsTrue(JToken.DeepEquals(want, got),
                        $"{name}:\n got  {got.ToString(Formatting.None)}\n want {want.ToString(Formatting.None)}");

                    var domains = new JArray(OWCharter.DomainsOf(WorldOf(fixtureWorld), reader, open).Select(d => d.ToJson()));
                    Assert.IsTrue(JToken.DeepEquals(want["domains"], domains), name + ": domainsOf is charterOf's domains");
                    return;
                }
                default:
                    Assert.Fail($"case {name}: a case of unknown kind {kind}");
                    return;
            }
        }

        // -- Beside the fixture ----------------------------------------------------------

        /// <summary>
        /// The served reader: the bodies a contributor's key is served, read as they are, give the contributor's packet
        /// for every world the fixture names with a contributor. The aid's mark rides on a whole body, so nothing is lost.
        /// </summary>
        [Test]
        public void TheServedReader_OnAContributorsBodies_ReadsAsTheContributor()
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            var checkedWorlds = 0;
            foreach (var p in ((JObject)Fixture["worlds"]).Properties())
            {
                var fixtureWorld = (JObject)p.Value;
                if (!(fixtureWorld["readers"]?["contributor"] is JObject)) continue;
                var world = WorldOf(fixtureWorld);
                var contributor = OWLensReader.Contributor();
                var served = OWLens.ServedView(world, contributor);
                var want = OWCharter.CharterOf(world, contributor).ToJson();
                var got = OWCharter.CharterOf(served, OWLensReader.Served()).ToJson();
                Assert.IsTrue(JToken.DeepEquals(want, got), $"{p.Name}: served {got.ToString(Formatting.None)}, contributor {want.ToString(Formatting.None)}");
                checkedWorlds++;
            }

            Assert.GreaterOrEqual(checkedWorlds, 3);
        }

        /// <summary>The world's own hidden list hides as each element's own mark does, and the reading changes no body it is given.</summary>
        [Test]
        public void TheHiddenList_HidesLikeTheMark_AndNoBodyIsChanged()
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            var harbour = (JObject)Fixture["worlds"]["harbour"];
            var bare = harbour["elements"].Cast<JObject>().Select(e => (JObject)e.DeepClone()).ToList();
            var before = new JArray(bare.Select(e => e.DeepClone()));
            var hidden = harbour["hidden"].Select(t => (string)t).ToList();

            var byList = OWCharter.CharterOf(bare, OWLensReader.Contributor(), hiddenIds: hidden).ToJson();
            var byMark = OWCharter.CharterOf(WorldOf(harbour), OWLensReader.Contributor()).ToJson();
            Assert.IsTrue(JToken.DeepEquals(byMark, byList), byList.ToString(Formatting.None));
            Assert.IsTrue(JToken.DeepEquals(before, new JArray(bare)), "a body was changed");
        }

        /// <summary>
        /// A Charter the reader holds only as a stub is still read whole, as keel reads its row: its name, its rules and its
        /// skill text are the Charter's own, since the Charter is every member rung's to read whole. No fixture case holds a
        /// stub Charter. Kills: rules read from the stub, which serves no extension field.
        /// </summary>
        [Test]
        public void AStubCharter_IsReadWhole()
        {
            const string world = "019a7f00-0000-7000-8000-00000000f1aa";
            var elements = new List<JObject>
            {
                JObject.Parse(@"{""id"":""019a7f00-0000-7000-8000-0000000002a0"",""type"":""law"",""name"":""Rules"",
                    ""supertype"":""Charter"",""x_charter_of"":""" + world + @""",""created_at"":""2026-10-01T12:00:00Z"",
                    ""description"":""Read ow://object/019a7f00-0000-7000-8000-0000000002a1."",""x_checks_rules"":[{""rule"":""r""}]}"),
                JObject.Parse(@"{""id"":""019a7f00-0000-7000-8000-0000000002a1"",""type"":""object"",""name"":""Skill""}"),
            };
            var guest = OWLensReader.Guest(new[] { "019a7f00-0000-7000-8000-0000000002a1" },
                new[] { "019a7f00-0000-7000-8000-0000000002a0" });
            var charter = OWCharter.CharterOf(elements, guest).Charter;
            Assert.IsNotNull(charter, "a stub of a marked Charter is still the Charter, by its owner's body");
            Assert.AreEqual("Rules", charter.Name);
            Assert.AreEqual(@"[{""rule"":""r""}]", charter.Rules.ToString(Formatting.None), "the Charter's own rules, not the stub's none");
            CollectionAssert.AreEqual(new[] { "019a7f00-0000-7000-8000-0000000002a1" }, charter.Skill);
        }

        /// <summary>
        /// A skill id's case never matters, as keel compares UUIDs: an Object stored with an uppercase id is named by the
        /// Charter's lowercase text, and the skill lists the id lowercase. Kills: the text's id matched against the id as
        /// stored.
        /// </summary>
        [Test]
        public void ASkillId_MatchesWhateverItsCase()
        {
            var elements = new List<JObject>
            {
                JObject.Parse(@"{""id"":""019a7f00-0000-7000-8000-0000000002b0"",""type"":""law"",""name"":""Rules"",
                    ""supertype"":""Charter"",""x_charter_of"":""w"",
                    ""description"":""Read ow://object/019a7f00-0000-7000-8000-0000000002bf and 019A7F00-0000-7000-8000-0000000002BE.""}"),
                JObject.Parse(@"{""id"":""019A7F00-0000-7000-8000-0000000002BF"",""type"":""object"",""name"":""Upper""}"),
                JObject.Parse(@"{""id"":""019a7f00-0000-7000-8000-0000000002be"",""type"":""object"",""name"":""Lower""}"),
            };
            CollectionAssert.AreEqual(new[] { "019a7f00-0000-7000-8000-0000000002bf", "019a7f00-0000-7000-8000-0000000002be" },
                OWCharter.CharterOf(elements, OWLensReader.Owner()).Charter.Skill);
        }

        /// <summary>
        /// keel's reading where the fixture is silent: a <c>x_checks_rules</c> that is not a list is served as stored
        /// (keel's <c>[] if rules is None else rules</c>), and only null or absent reads as the empty list.
        /// </summary>
        [Test]
        public void Rules_AreAsStored_NullOrAbsentTheEmptyList()
        {
            JObject Law(string rules) => JObject.Parse(@"{""id"":""a"",""type"":""law"",""name"":""R"",""supertype"":""Charter"",""x_charter_of"":""w"""
                                                       + (rules == null ? "" : @",""x_checks_rules"":" + rules) + "}");
            string Rules(string rules) =>
                OWCharter.CharterOf(new[] { Law(rules) }, OWLensReader.Owner()).Charter.Rules.ToString(Formatting.None);

            Assert.AreEqual("[]", Rules(null));
            Assert.AreEqual("[]", Rules("null"));
            Assert.AreEqual(@"{""rule"":""r""}", Rules(@"{""rule"":""r""}"));
        }

        /// <summary>
        /// A Guide the reader knows only as a stub names it and orders nothing, since a stub serves no <c>constructs</c>:
        /// the Domains by folded name and id alone. The TypeScript SDK 4.100.0's vector (<c>test/charter.test.mjs</c>).
        /// Kills: the order read from the owner's Guide body.
        /// </summary>
        [Test]
        public void AStubGuide_NamesIt_AndOrdersNothing()
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            string Id(string n) => "019a7f00-0000-7000-8000-000000000" + n;
            var guest = OWLensReader.Guest(new[] { Id("204"), Id("205"), Id("206") }, new[] { Id("203") });
            var got = OWCharter.CharterOf(WorldOf((JObject)Fixture["worlds"]["harbour"]), guest);
            Assert.AreEqual(Id("203"), got.Guide.Id);
            Assert.AreEqual("Start here", got.Guide.Name);
            CollectionAssert.AreEqual(new[] { "ash row", "the Archive", "The Docks" }, got.Domains.Select(d => d.Name).ToArray(),
                "not the Guide's Archive, Docks");
        }

        /// <summary>The owner reading as the public reads a closed world as the public does: nothing. The TypeScript SDK 4.100.0's vector.</summary>
        [Test]
        public void TheOwnerAsThePublic_ReadsAClosedWorldAsThePublicDoes()
        {
            Assert.IsNotNull(Fixture, "the vendored charter fixture was not found");
            var got = OWCharter.CharterOf(WorldOf((JObject)Fixture["worlds"]["harbour"]), OWLensReader.Public().WithOwnerCounts());
            Assert.AreEqual(@"{""charter"":null,""guide"":null,""domains"":[]}", got.ToJson().ToString(Formatting.None));
        }

        /// <summary>A null reader is refused before anything is read.</summary>
        [Test]
        public void ANullReader_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => OWCharter.CharterOf(new List<JObject>(), null));
        }

        // -- The replay's worlds and readers ------------------------------------------------

        /// <summary>A case's world: a name in the fixture's worlds, or a world of its own.</summary>
        private static JObject WorldNamed(JToken world) =>
            world.Type == JTokenType.String ? (JObject)Fixture["worlds"][(string)world] : (JObject)world;

        /// <summary>The fixture world's elements, each its hidden ids keep carrying the hidden mark.</summary>
        private static List<JObject> WorldOf(JObject fixtureWorld)
        {
            var hidden = new HashSet<string>((fixtureWorld["hidden"] as JArray ?? new JArray()).Select(t => (string)t));
            var list = new List<JObject>();
            foreach (var e in fixtureWorld["elements"].Cast<JObject>())
            {
                var copy = (JObject)e.DeepClone();
                if (hidden.Contains((string)copy["id"])) copy[OWPayload.Hidden] = true;
                list.Add(copy);
            }

            return list;
        }

        /// <summary>A fixture reader: its rung, its character, a guest's lists; <c>as</c> is the owner reading as another.</summary>
        private static OWLensReader ReaderOf(JObject fixtureWorld, string name)
        {
            var r = (JObject)fixtureWorld["readers"][name];
            if (r.TryGetValue("as", out var asName)) return ReaderOf(fixtureWorld, (string)asName).WithOwnerCounts();
            var character = r["character"]?.Type == JTokenType.String ? (string)r["character"] : null;
            switch ((string)r["rung"])
            {
                case "owner": return OWLensReader.Owner(character);
                case "contributor": return OWLensReader.Contributor(character);
                case "public": return OWLensReader.Public();
                case "guest": return OWLensReader.Guest(Strings(r["whole"]), Strings(r["known_of"]), character);
                default: throw new ArgumentException("a fixture reader of unknown rung: " + r["rung"]);
            }
        }

        private static List<string> Strings(JToken token) =>
            token is JArray list ? list.Select(t => (string)t).ToList() : new List<string>();
    }
}
