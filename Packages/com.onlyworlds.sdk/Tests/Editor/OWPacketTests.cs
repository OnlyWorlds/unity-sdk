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
    /// The packet profile's conformance fixture (schema-dist <c>profiles/packet.fixture.json</c>, vendored byte for byte
    /// in <c>Tests/Editor/Fixtures/packet/</c> from dist.145; the page's first fixture at serial 136, two more state and
    /// asker cases at serial 138), replayed case by case through <see cref="OWPacket.WorkOf"/> over the fixture's world
    /// and source world, and each item through the one-item readers (<see cref="OWPacket.WorkStateOf"/>,
    /// <see cref="OWPacket.IsOpenWork"/>, <see cref="OWPacket.AskerOf"/>). The tests beside the fixture are the TypeScript
    /// SDK's (test/packet.test.mjs, 4.181.0), case for case. When the dist moves the fixture, re-vendor it and update the
    /// hash below.
    /// </summary>
    /// <remarks>
    /// The fixture's want, in this package's spelling: states -> <see cref="OWWork.States"/>, shown -> <see cref="OWWork.Shown"/>,
    /// open_work -> <see cref="OWWork.OpenWork"/>, elements -> <see cref="OWWork.Elements"/> (each entry's id), askers ->
    /// <see cref="OWWork.Askers"/>, lints -> <see cref="OWWork.Lints"/>, waiting_on -> <see cref="OWWork.WaitingOn"/>.
    /// Maps compare as maps, every list of work items and the lints as sets, and elements in order (the fixture's Order
    /// note).
    /// </remarks>
    public class OWPacketTests
    {
        /// <summary>The fixture's sha256 as dist.145's MANIFEST.json records it.</summary>
        private const string DistSha256 = "78f2688dda61ff240ca0c4aaaa3b5ea3ff02bd5e5f745d58a334ceab73791c8c";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "packet", "packet.fixture.json");
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

        private static OWWorkWorld WorldOf(JToken world) =>
            world == null || world.Type == JTokenType.Null
                ? null
                : new OWWorkWorld((string)world["id"], world["characters"].Values<string>(), world["others"].Values<string>());

        private static IEnumerable<TestCaseData> CasesOf(string list, string prefix)
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName(prefix + "_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture[list].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName(prefix + "_" + name.Replace('-', '_'));
            }
        }

        /// <summary>One case per <c>state_cases</c> case, named after it.</summary>
        public static IEnumerable<TestCaseData> StateCases() => CasesOf("state_cases", "PacketState");

        /// <summary>One case per <c>asker_cases</c> case, named after it.</summary>
        public static IEnumerable<TestCaseData> AskerCases() => CasesOf("asker_cases", "PacketAsker");

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored packet fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.145's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        /// <summary>The case lists are read whole: a case added later must not pass unseen.</summary>
        [Test]
        public void TheFixture_IsThePacketProfiles_WithItsTwoLists()
        {
            Assert.IsNotNull(Fixture, "the vendored packet fixture was not found");
            Assert.AreEqual("packet", (string)Fixture["profile"]);
            Assert.AreEqual(11, ((JArray)Fixture["state_cases"]).Count, "serial 138's fixture holds 11 state cases");
            Assert.AreEqual(14, ((JArray)Fixture["asker_cases"]).Count, "serial 138's fixture holds 14 asker cases");
            var names = Fixture["state_cases"].Concat(Fixture["asker_cases"]).Select(c => (string)c["name"]).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "a case is found by its name, so each name is once");
        }

        [Test]
        public void ThePair_IsARoomPair_AndTheStatesAreTheFour()
        {
            CollectionAssert.Contains(OWRoom.Pairs.ToList(), OWPacket.Pair, "a work item is a room pair");
            CollectionAssert.AreEqual(new[] { "open", "taken", "done", "standing" }, OWPacket.States.ToList());
        }

        [TestCaseSource(nameof(StateCases))]
        public void EveryStateCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored packet fixture was not found");
            var c = Fixture["state_cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var items = ((JArray)c["items"]).ToList();
            var got = OWPacket.WorkOf(items, WorldOf(Fixture["world"]), WorldOf(Fixture["source_world"]));
            var want = (JObject)c["want"];

            AssertMap(want["states"], got.States, name + ": states");
            CollectionAssert.AreEquivalent(want["shown"].Values<string>().ToList(), got.Shown.ToList(), name + ": shown");
            AssertMapOfSets(want["open_work"], got.OpenWork, name + ": openWork");
            if (want["elements"] is JObject elements)
            {
                foreach (var p in elements.Properties())
                {
                    CollectionAssert.AreEqual(p.Value.Values<string>().ToList(), got.Elements[p.Name].Select(e => e.Id).ToList(),
                        $"{name}: elements of {p.Name}, in order");
                }
            }

            // The one-item readers answer as the list reader does. `states` keys only the work items, so an element given
            // that is no work item has no key there and its one-item state is null (serial 138's case).
            var states = (JObject)want["states"];
            foreach (var item in items)
            {
                var id = (string)item["id"];
                var state = states.ContainsKey(id) ? (string)states[id] : null;
                Assert.AreEqual(state, OWPacket.WorkStateOf(item), $"{name}: WorkStateOf {id}");
                Assert.AreEqual(state == "open" || state == "taken", OWPacket.IsOpenWork(item), $"{name}: IsOpenWork {id}");
            }
        }

        [TestCaseSource(nameof(AskerCases))]
        public void EveryAskerCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored packet fixture was not found");
            var c = Fixture["asker_cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var items = ((JArray)c["items"]).ToList();
            var world = WorldOf(Fixture["world"]);
            var got = OWPacket.WorkOf(items, world, WorldOf(Fixture["source_world"]));
            var want = (JObject)c["want"];

            AssertMap(want["askers"], got.Askers, name + ": askers");
            CollectionAssert.AreEquivalent(want["lints"].Select(l => l.ToString(Formatting.None)).ToList(),
                got.Lints.Select(l => l.ToJson().ToString(Formatting.None)).ToList(), name + ": lints");
            AssertMapOfSets(want["waiting_on"], got.WaitingOn, name + ": waitingOn");
            foreach (var item in items)
            {
                Assert.AreEqual((string)want["askers"][(string)item["id"]], OWPacket.AskerOf(item, world),
                    $"{name}: AskerOf {item["id"]}");
            }
        }

        private static void AssertMap(JToken want, IReadOnlyDictionary<string, string> got, string message)
        {
            var w = ((JObject)want).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
            CollectionAssert.AreEquivalent(w.Keys.ToList(), got.Keys.ToList(), message + " (keys)");
            foreach (var kv in w) Assert.AreEqual(kv.Value, got[kv.Key], $"{message} of {kv.Key}");
        }

        private static void AssertMapOfSets(JToken want, IReadOnlyDictionary<string, IReadOnlyList<string>> got, string message)
        {
            var w = (JObject)want;
            CollectionAssert.AreEquivalent(w.Properties().Select(p => p.Name).ToList(), got.Keys.ToList(), message + " (keys)");
            foreach (var p in w.Properties())
                CollectionAssert.AreEquivalent(p.Value.Values<string>().ToList(), got[p.Name].ToList(), $"{message} of {p.Name}");
        }

        // -- Beside the fixture (the TypeScript SDK's own tests: the page's rules imply them) ---------------------

        private static readonly OWWorkWorld World = new OWWorkWorld("w", new[] { "c1", "C2" }, new[] { "loc" });

        private int _n;

        /// <summary>A work item as npm's tests build one: open, one element, from a folder; <paramref name="fields"/> set over it.</summary>
        private JObject Item(params (string key, JToken value)[] fields)
        {
            var item = new JObject
            {
                ["id"] = "i" + ++_n,
                ["type"] = "relation",
                ["supertype"] = "Work item",
                ["subtype"] = "open",
                ["name"] = "Work",
                ["x_packet_elements"] = new JArray(new JObject { ["type"] = "location", ["id"] = "loc", ["reason"] = "Why." }),
                ["x_packet_source"] = new JObject { ["server"] = null, ["world"] = "w" },
            };
            foreach (var (key, value) in fields) item[key] = value ?? JValue.CreateNull();
            return item;
        }

        private static JObject Entry(string type, string id, string reason) =>
            new JObject { ["type"] = type, ["id"] = id, ["reason"] = reason };

        [Test]
        public void WorkStateOf_TrimsWhiteSpaceAndFolds_TheWholeWord()
        {
            const string nel = "\u0085"; // NEXT LINE, White_Space
            const string ideo = "　";
            Assert.AreEqual("standing", OWPacket.WorkStateOf(Item(("subtype", " Standing "))));
            Assert.AreEqual("taken", OWPacket.WorkStateOf(Item(("subtype", nel + "taken" + nel))));
            Assert.AreEqual("done", OWPacket.WorkStateOf(Item(("subtype", ideo + "DONE\t"))));
            Assert.AreEqual("open", OWPacket.WorkStateOf(Item(("subtype", "OPEN"))));
            Assert.IsNull(OWPacket.WorkStateOf(Item(("subtype", "op en"))));
            Assert.IsNull(OWPacket.WorkStateOf(Item(("subtype", "opened"))));
            Assert.IsNull(OWPacket.WorkStateOf(Item(("subtype", "​open"))), "a zero width space is no White_Space");
        }

        [Test]
        public void WorkStateOf_ASubtypeThatIsNoString_IsNoState()
        {
            foreach (var bad in new JToken[] { 7, true, new JArray("open"), new JObject { ["open"] = true } })
                Assert.IsNull(OWPacket.WorkStateOf(Item(("subtype", bad))), bad.ToString(Formatting.None));
        }

        [Test]
        public void WorkStateOf_OnlyAWorkItemHasAState_ThePairTrimmedAndFolded_TheRoomMarkNotNeeded()
        {
            Assert.AreEqual("open", OWPacket.WorkStateOf(Item(("type", " Relation"), ("supertype", "WORK ITEM "))));
            Assert.AreEqual("open", OWPacket.WorkStateOf(Item(("x_room_of", "w"))));
            Assert.IsNull(OWPacket.WorkStateOf(new JObject { ["type"] = "character", ["supertype"] = "Work item", ["subtype"] = "open" }));
            Assert.IsNull(OWPacket.WorkStateOf(Item(("supertype", "Work items"))));
            Assert.IsNull(OWPacket.WorkStateOf(Item(("supertype", null))));
            Assert.IsFalse(OWPacket.IsOpenWork(new JObject { ["type"] = "relation", ["supertype"] = "Board", ["subtype"] = "open" }));
            foreach (var bad in new JToken[] { null, JValue.CreateNull(), JValue.CreateUndefined(), "open", 7, new JArray(Item()) })
            {
                var label = bad == null ? "null" : bad.ToString(Formatting.None);
                Assert.IsNull(OWPacket.WorkStateOf(bad), label);
                Assert.IsFalse(OWPacket.IsOpenWork(bad), label);
            }

            // A packet before it lands carries no id, and its state reads the same.
            var bare = Item(("subtype", "taken"));
            bare.Remove("id");
            Assert.IsTrue(OWPacket.IsOpenWork(bare));
        }

        [Test]
        public void IsOpenWork_OpenAndTakenOnly_DoneStandingAndNoStateNever()
        {
            var got = new[] { "open", " Taken", "done", "standing", "blocked", "" }
                .Select(s => OWPacket.IsOpenWork(Item(("subtype", s)))).ToList();
            CollectionAssert.AreEqual(new[] { true, true, false, false, false, false }, got);
        }

        [Test]
        public void AskerOf_TheKeyAsWritten_NeverTheMakerOrActor_BlankEmptyAndNoStringNameNoOne()
        {
            Assert.AreEqual("c1", OWPacket.AskerOf(Item(("x_packet_from", "c1"), ("actor", "C2"), ("created_by", "m"))));
            Assert.IsNull(OWPacket.AskerOf(Item(("actor", "c1"), ("created_by", "c1"), ("x_packet_producer", new JObject { ["tool"] = "c1" }))));
            Assert.AreEqual(" c1", OWPacket.AskerOf(Item(("x_packet_from", " c1"))), "unchecked without a world: as written, never trimmed");
            foreach (var bad in new JToken[] { "", "   ", "\t", null, 7, new JArray("c1"), new JObject { ["id"] = "c1" } })
                Assert.IsNull(OWPacket.AskerOf(Item(("x_packet_from", bad))), bad == null ? "null" : bad.ToString(Formatting.None));
        }

        [Test]
        public void AskerOf_WithAWorld_OnlyACharacterOfItWrittenExactlySo()
        {
            Assert.AreEqual("c1", OWPacket.AskerOf(Item(("x_packet_from", "c1")), World));
            Assert.IsNull(OWPacket.AskerOf(Item(("x_packet_from", "C1")), World), "an id in upper case names nothing");
            Assert.AreEqual("C2", OWPacket.AskerOf(Item(("x_packet_from", "C2")), World), "a Character whose own id is in upper case");
            Assert.IsNull(OWPacket.AskerOf(Item(("x_packet_from", "c2")), World));
            Assert.IsNull(OWPacket.AskerOf(Item(("x_packet_from", "c1 ")), World));
            Assert.IsNull(OWPacket.AskerOf(Item(("x_packet_from", "loc")), World));
            Assert.IsNull(OWPacket.AskerOf(new JObject { ["type"] = "character", ["x_packet_from"] = "c1" }, World));
        }

        [Test]
        public void WorkOf_AnEmptyOrBlankKeyIsALint_AnUpperCaseSpellingOfAHeldIdNamesNothingAndIsNoLint()
        {
            var items = new[]
            {
                Item(("x_packet_from", "")), Item(("x_packet_from", "   ")), Item(("x_packet_from", "C1")),
                Item(("x_packet_from", "LOC")), Item(("x_packet_from", "loc")),
            };
            var got = OWPacket.WorkOf(items, World);
            CollectionAssert.AreEqual(new string[] { null, null, null, null, null }, got.Askers.Values.ToList());
            CollectionAssert.AreEqual(new[] { items[0], items[1], items[4] }.Select(i => "asker " + i["id"]).ToList(),
                got.Lints.Select(l => l.Kind + " " + l.Item).ToList());
        }

        [Test]
        public void WorkOf_AKeyNamingAnotherWorkItemTheReaderIsGiven_IsNoCharacter_SoALint()
        {
            var a = Item();
            var b = Item(("x_packet_from", a["id"]));
            var got = OWPacket.WorkOf(new[] { a, b }, World);
            CollectionAssert.AreEqual(new[] { "asker " + b["id"] }, got.Lints.Select(l => l.Kind + " " + l.Item).ToList());
        }

        [Test]
        public void WorkOf_OpenWorkNamesEachItemOncePerElement_AStandingItemCountsOnNone()
        {
            var twice = Item(("x_packet_elements", new JArray(Entry("location", "loc", "a"), Entry("location", "loc", "b"))));
            var standing = Item(("subtype", "Standing"), ("x_packet_elements", new JArray(Entry("object", "obj", "kept"))));
            var got = OWPacket.WorkOf(new[] { twice, standing }, World);
            CollectionAssert.AreEqual(new[] { "loc", "obj" }, got.OpenWork.Keys.ToList());
            CollectionAssert.AreEqual(new[] { (string)twice["id"] }, got.OpenWork["loc"].ToList());
            CollectionAssert.IsEmpty(got.OpenWork["obj"]);
            // elements keeps the packet as written, both entries.
            CollectionAssert.AreEqual(new[] { "a", "b" }, got.Elements[(string)twice["id"]].Select(e => e.Reason).ToList());
            Assert.AreEqual("{\"type\":\"object\",\"id\":\"obj\",\"reason\":\"kept\"}",
                string.Join(",", got.Elements[(string)standing["id"]].Select(e => e.ToJson().ToString(Formatting.None))));
        }

        [Test]
        public void WorkOf_AnEntryWithoutItsThreeKeys_IsIgnoredNeverRepaired()
        {
            var it = Item(("x_packet_elements", new JArray(
                new JObject { ["type"] = "location", ["id"] = "a" }, new JObject { ["id"] = "b", ["reason"] = "r" },
                new JObject { ["type"] = "location", ["id"] = 7, ["reason"] = "r" }, Entry("location", "  ", "r"),
                "c", JValue.CreateNull(), Entry("location", "d", "kept"))));
            var got = OWPacket.WorkOf(new[] { it }, World);
            Assert.AreEqual("{\"type\":\"location\",\"id\":\"d\",\"reason\":\"kept\"}",
                string.Join(",", got.Elements[(string)it["id"]].Select(e => e.ToJson().ToString(Formatting.None))));
            CollectionAssert.AreEqual(new[] { "d" }, got.OpenWork.Keys.ToList());
            CollectionAssert.AreEqual(new[] { (string)it["id"] }, got.OpenWork["d"].ToList());
            CollectionAssert.IsEmpty(OWPacket.WorkOf(new[] { Item(("x_packet_elements", "loc")) }, World).OpenWork);
        }

        [Test]
        public void WorkOf_WhatIsNoWorkItemOrHasNoId_IsInNoPart_OfTwoWithOneIdTheFirstIsRead()
        {
            var first = Item(("id", "same"), ("subtype", "open"));
            var got = OWPacket.WorkOf(new JToken[]
            {
                new JObject { ["type"] = "character", ["id"] = "x", ["subtype"] = "open" }, Item(("id", "")),
                Item(("id", JValue.CreateUndefined())), first, Item(("id", "same"), ("subtype", "done")), null, "i",
            }, World);
            CollectionAssert.AreEqual(new[] { "same" }, got.Shown.ToList());
            CollectionAssert.AreEqual(new[] { "same" }, got.States.Keys.ToList());
            Assert.AreEqual("open", got.States["same"]);
        }

        [Test]
        public void WorkOf_EveryCharacterOfTheWorldHasItsWaitingList_AndAnIdSuchAsProtoIsAKeyLikeAnyOther()
        {
            var world = new OWWorkWorld(null, new[] { "__proto__", "c1" }, Array.Empty<string>());
            var it = Item(("id", "__proto__"), ("x_packet_from", "__proto__"),
                ("x_packet_elements", new JArray(Entry("location", "__proto__", "r"))));
            var got = OWPacket.WorkOf(new[] { it }, world);
            CollectionAssert.AreEqual(new[] { "__proto__", "c1" }, got.WaitingOn.Keys.ToList());
            CollectionAssert.AreEqual(new[] { "__proto__" }, got.WaitingOn["__proto__"].ToList());
            CollectionAssert.AreEqual(new[] { "__proto__" }, got.States.Keys.ToList());
            CollectionAssert.AreEqual(new[] { "__proto__" }, got.OpenWork.Keys.ToList());
        }

        [Test]
        public void WorkOf_RefusesNoItemsAndNoWorld()
        {
            // npm's other two refusals, a source world and an askerOf world that are no object, cannot be written in C#.
            var e = Assert.Throws<ArgumentNullException>(() => OWPacket.WorkOf(null, World));
            Assert.AreEqual("items", e.ParamName);
            StringAssert.StartsWith("WorkOf:", e.Message);
            e = Assert.Throws<ArgumentNullException>(() => OWPacket.WorkOf(Array.Empty<JToken>(), null));
            Assert.AreEqual("world", e.ParamName);
            StringAssert.StartsWith("WorkOf:", e.Message);
        }

        [Test]
        public void WorkOf_IsPure_TheItemsAreNotMoved_AndTheAnswerIsTheCallersOwn()
        {
            Assert.IsNotNull(Fixture, "the vendored packet fixture was not found");
            // Its own copy of the fixture: the shared one has been read by every case before this test, so a reader that
            // wrote into the bodies would have written there already and the copy below would match itself.
            var fresh = OWJson.ParseObject(File.ReadAllText(FixturePath));
            var items = ((JArray)fresh["state_cases"][3]["items"]).ToList();
            var before = string.Join("\n", items.Select(i => i.ToString(Formatting.None)));
            var got = OWPacket.WorkOf(items, WorldOf(Fixture["world"]));
            Assert.AreEqual(before, string.Join("\n", items.Select(i => i.ToString(Formatting.None))));
            // The answer shares nothing with the bodies: a body changed after the read leaves the answer as it was.
            var copy = (JObject)items[0].DeepClone();
            var mine = OWPacket.WorkOf(new[] { copy }, WorldOf(Fixture["world"]));
            copy["x_packet_elements"][0]["id"] = "planted";
            Assert.AreEqual(got.Elements[(string)items[0]["id"]][0].Id, mine.Elements[(string)copy["id"]][0].Id);
        }
    }
}
