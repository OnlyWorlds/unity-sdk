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
    /// The strand profile's conformance fixture (schema-dist <c>profiles/strand.fixture.json</c>, vendored byte for byte
    /// in <c>Tests/Editor/Fixtures/strand/</c> from dist.98), replayed case by case through <see cref="OWStrand.Of"/>
    /// over the fixture's named worlds and their readers. keel's reader, the TypeScript SDK and the Python package answer
    /// the same cases. When the dist moves the fixture, re-vendor it and update the hash below.
    /// </summary>
    /// <remarks>
    /// A fixture world is the World body (its <c>x_strand</c> as stored) beside its elements as the owner is served them
    /// and, apart, the ids its hidden mark keeps; the elements handed to the reader here carry that mark on each of them
    /// (<c>hidden: true</c>), as <see cref="OWCharterTests"/> builds its worlds.
    /// </remarks>
    public class OWStrandTests
    {
        /// <summary>The fixture's sha256 as dist.98's MANIFEST.json records it.</summary>
        private const string DistSha256 = "1cd184788c0ebfdde7fc5df36792e9a3c76d4ff54864ca6c7a43d666a90d3823";

        private const string Story = "019a7f00-0000-7000-8000-000000000301";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "strand", "strand.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("Strand_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Strand_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored strand fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.98's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist98sCases_AndItsMarkIsTheReaders()
        {
            Assert.IsNotNull(Fixture, "the vendored strand fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(17, cases.Count, "dist.98's fixture holds 17 cases");
            CollectionAssert.AreEqual(new[] { "strand" }, cases.Select(c => (string)c["kind"]).Distinct().ToArray());
            Assert.AreEqual("strand", (string)Fixture["profile"]);
            Assert.AreEqual((string)Fixture["mark"]["field"], OWStrand.MarkField);
            CollectionAssert.AreEqual(Fixture["mark"]["members"].Select(m => (string)m).ToList(), OWStrand.Members.ToList());
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored strand fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            Assert.AreEqual("strand", (string)c["kind"], $"case {name}: a case of unknown kind {c["kind"]}");
            var fixtureWorld = WorldNamed(c["world"]);
            var reader = ReaderOf(fixtureWorld, (string)c["reader"]);
            var open = fixtureWorld["open"]?.Type == JTokenType.Boolean && (bool)fixtureWorld["open"];
            var want = (JObject)c["want"];

            var got = OWStrand.Of(fixtureWorld, ElementsOf(fixtureWorld), reader, open).ToJson();
            Assert.IsTrue(JToken.DeepEquals(want, got),
                $"{name}:\n got  {got.ToString(Formatting.None)}\n want {want.ToString(Formatting.None)}");
        }

        // -- Beside the fixture ----------------------------------------------------------

        /// <summary>
        /// The public reader reads no strand of a world that is not open, and the owner's strand of one that is, as the
        /// charter reader reads the public rung. The TypeScript SDK's vector (<c>test/strand.test.mjs</c>).
        /// </summary>
        [Test]
        public void ThePublic_ReadsNothingOfAClosedWorld_AndTheOpenOnesStrand()
        {
            var harbour = World("harbour");
            var owner = OWStrand.Of(harbour, ElementsOf(harbour), OWLensReader.Owner()).ToJson();
            Assert.IsTrue(OWStrand.Of(harbour, ElementsOf(harbour), OWLensReader.Public()).IsEmpty);
            Assert.IsTrue(OWStrand.Of(harbour, ElementsOf(harbour), OWLensReader.Public().WithOwnerCounts()).IsEmpty,
                "the owner reading as the public reads a closed world as the public does");
            var open = OWStrand.Of(harbour, ElementsOf(harbour), OWLensReader.Public(), open: true).ToJson();
            Assert.IsTrue(JToken.DeepEquals(owner, open), open.ToString(Formatting.None));
        }

        /// <summary>
        /// A mark that is not an object of three strings is no mark: a member missing, null, a number, the mark a list or
        /// null. Kills: a mark read from whichever members are strings.
        /// </summary>
        [Test]
        public void AMarkThatIsNotThreeStrings_IsNoMark()
        {
            var harbour = World("harbour");
            var m = (JObject)harbour[OWStrand.MarkField];
            var bad = new List<JToken>
            {
                Without(m, "event"),
                With(m, "location", JValue.CreateNull()),
                With(m, "narrative", new JValue(7)),
                new JArray(m["narrative"], m["event"], m["location"]),
                JValue.CreateNull(),
                new JValue(Story),
            };
            foreach (var mark in bad)
            {
                var world = (JObject)harbour.DeepClone();
                world[OWStrand.MarkField] = mark;
                Assert.IsTrue(OWStrand.Of(world, ElementsOf(world), OWLensReader.Owner()).IsEmpty, mark.ToString(Formatting.None));
            }

            var none = (JObject)harbour.DeepClone();
            none.Remove(OWStrand.MarkField);
            Assert.IsTrue(OWStrand.Of(none, ElementsOf(none), OWLensReader.Owner()).IsEmpty, "no key");
            Assert.IsTrue(OWStrand.Of(null, ElementsOf(harbour), OWLensReader.Owner()).IsEmpty, "no World body");
        }

        /// <summary>
        /// The page's words on a member that is an empty string: a string, so the mark's shape holds, and an id that names
        /// no element in the view, so its slot is empty and the rest still read (keel's <c>mark_of</c>). The TypeScript SDK
        /// and the Python package read it as no mark; the difference is open with the profile's owner.
        /// </summary>
        [Test]
        public void AnEmptyStringMember_IsAnEmptySlot_NotNoMark()
        {
            var world = World("harbour");
            world[OWStrand.MarkField] = With((JObject)world[OWStrand.MarkField], "event", new JValue(""));
            var got = OWStrand.Of(world, ElementsOf(world), OWLensReader.Owner());
            Assert.AreEqual(Story, got.Narrative?.Id);
            Assert.IsNull(got.Event);
            Assert.IsNull(got.Location, "the place hangs off the event");
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000331", got.Character?.Id);
            Assert.AreEqual("019a7f00-0000-7000-8000-000000000322", got.Now?.Id);
            Assert.IsNull(got.Same);
        }

        /// <summary>
        /// The served reader: the bodies a contributor's key is served, read as they are, give the contributor's strand
        /// for every world the fixture names. The lens reader's one view under another rung, nothing of the strand's own.
        /// </summary>
        [Test]
        public void TheServedReader_OnAContributorsBodies_ReadsAsTheContributor()
        {
            Assert.IsNotNull(Fixture, "the vendored strand fixture was not found");
            var checkedWorlds = 0;
            foreach (var p in ((JObject)Fixture["worlds"]).Properties())
            {
                var fixtureWorld = (JObject)p.Value;
                var elements = ElementsOf(fixtureWorld);
                var contributor = OWLensReader.Contributor();
                var want = OWStrand.Of(fixtureWorld, elements, contributor).ToJson();
                var got = OWStrand.Of(fixtureWorld, OWLens.ServedView(elements, contributor), OWLensReader.Served()).ToJson();
                Assert.IsTrue(JToken.DeepEquals(want, got), $"{p.Name}: served {got.ToString(Formatting.None)}, contributor {want.ToString(Formatting.None)}");
                checkedWorlds++;
            }

            Assert.AreEqual(15, checkedWorlds);
        }

        /// <summary>The world's own hidden list hides as each element's own mark does, and the reading changes no body it is given.</summary>
        [Test]
        public void TheHiddenList_HidesLikeTheMark_AndNoBodyIsChanged()
        {
            var fixtureWorld = World("harbour-hidden-place");
            var bare = fixtureWorld["elements"].Cast<JObject>().Select(e => (JObject)e.DeepClone()).ToList();
            var before = new JArray(bare.Select(e => e.DeepClone()));
            var worldBefore = fixtureWorld.ToString(Formatting.None);
            var hidden = fixtureWorld["hidden"].Select(t => (string)t).ToList();
            var guest = ReaderOf(fixtureWorld, "guest");

            var byList = OWStrand.Of(fixtureWorld, bare, guest, hiddenIds: hidden).ToJson();
            var byMark = OWStrand.Of(fixtureWorld, ElementsOf(fixtureWorld), guest).ToJson();
            Assert.IsTrue(JToken.DeepEquals(byMark, byList), byList.ToString(Formatting.None));
            Assert.IsTrue(JToken.DeepEquals(before, new JArray(bare)), "a body was changed");
            Assert.AreEqual(worldBefore, fixtureWorld.ToString(Formatting.None), "the World body was changed");
        }

        /// <summary>A null reader is refused before anything is read.</summary>
        [Test]
        public void ANullReader_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => OWStrand.Of(World("harbour"), new List<JObject>(), null));
        }

        // -- The replay's worlds and readers ------------------------------------------------

        private static JObject World(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored strand fixture was not found");
            return (JObject)Fixture["worlds"][name].DeepClone();
        }

        /// <summary>A case's world: a name in the fixture's worlds, or a world of its own.</summary>
        private static JObject WorldNamed(JToken world) =>
            world.Type == JTokenType.String ? (JObject)Fixture["worlds"][(string)world] : (JObject)world;

        /// <summary>The fixture world's elements, each its hidden ids keep carrying the hidden mark.</summary>
        private static List<JObject> ElementsOf(JObject fixtureWorld)
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

        private static JObject With(JObject mark, string member, JToken value)
        {
            var copy = (JObject)mark.DeepClone();
            copy[member] = value;
            return copy;
        }

        private static JObject Without(JObject mark, string member)
        {
            var copy = (JObject)mark.DeepClone();
            copy.Remove(member);
            return copy;
        }

        private static List<string> Strings(JToken token) =>
            token is JArray list ? list.Select(t => (string)t).ToList() : new List<string>();
    }
}
