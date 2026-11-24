// OWMapSpace.PinsKnownTo: which of a map's Pins one reader knows. The shared cases
// (Fixtures/pins-known-to/pins-known-to.cases.json, the same bytes as the TypeScript SDK's
// test/fixtures/pins-known-to.cases.json and the Python package's copy, its sha256 pinned here): the whole answer of
// each case, key order included, and each refusal; then what the cases cannot hold: the inputs left as they were, two
// calls answering two objects, and the answer beside OWMapSpace.Of's. Ported from @onlyworlds/sdk 4.200.0,
// test/pins-known-to.test.mjs (its frozen-input test has no C# counterpart: a JToken cannot be frozen).

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
    public class OWPinsKnownToTests
    {
        /// <summary>The cases file's sha256, as both packages' suites hold it.</summary>
        private const string CasesSha256 = "80f66dba8aa1a009770bdcffbf457ec84b78f7e4270a2fb9cf8614a4b76e39f6";

        private const int CaseCount = 31;

        private const int RefusalCount = 8;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "pins-known-to", "pins-known-to.cases.json");
            }
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = FixturePath;
                return path == null || !File.Exists(path) ? null : _fixture = Parse();
            }
        }

        /// <summary>The cases file parsed afresh from its bytes.</summary>
        private static JObject Parse() => OWJson.ParseObject(File.ReadAllText(FixturePath));

        /// <summary>One test per shared case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("PinsKnownTo_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) && ch < 0x80 ? ch : '_').ToArray());
                yield return new TestCaseData(name).SetName("PinsKnownTo_" + safe);
            }
        }

        /// <summary>A case's list as the reader takes it: a JSON array is the list itself.</summary>
        private static IEnumerable<JToken> List(JToken t) => (JArray)t;

        /// <summary>A case's answer as JSON text, which holds the key order and every value: two answers are the same exactly when their texts are.</summary>
        private static string Answer(JObject c) => OWMapSpace.PinsKnownTo(List(c["pins"]), List(c["served"])).ToJson().ToString(Formatting.None);

        [Test]
        public void TheCasesFile_IsTheSharedBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored known Pins cases were not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(CasesSha256, hash,
                "the vendored cases are not the packages' bytes: re-vendor them (a checkout's line endings change the bytes)");
            Assert.AreEqual(1, (int)Fixture["version"]);
            Assert.AreEqual(CaseCount, Fixture["cases"].Count());
            Assert.AreEqual(RefusalCount, Fixture["refusals"].Count());
        }

        [Test]
        public void EveryCase_TheWholeAnswer_KeyOrderIncluded()
        {
            Assert.IsNotNull(Fixture, "the vendored known Pins cases were not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(cases.Count, cases.Select(c => (string)c["name"]).Distinct().Count(), "every case's name is its own");
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                var got = Answer(c);
                var want = c["want"].ToString(Formatting.None);
                if (got != want) wrong.Add($"{c["name"]}\n    want {want}\n    got  {got}");
            }

            Assert.IsEmpty(wrong, $"{wrong.Count} case(s) disagree:\n  " + string.Join("\n  ", wrong));
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_GivesTheCasesAnswer(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored known Pins cases were not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            Assert.AreEqual(c["want"].ToString(Formatting.None), Answer(c), name);
        }

        /// <summary>
        /// Each shared refusal: an <see cref="ArgumentException"/> (an <see cref="ArgumentNullException"/> for a null)
        /// that names the argument the case refuses, and not the other. A JSON <c>null</c> is passed as C#'s null; an
        /// object, a string or a number is passed as its <see cref="JToken"/>, which is enumerable in C# and still no list.
        /// </summary>
        [Test]
        public void EveryRefusal_AnArgumentException_ThatNamesTheArgument_AndNoOther()
        {
            Assert.IsNotNull(Fixture, "the vendored known Pins cases were not found");
            IEnumerable<JToken> Given(JToken t) => t.Type == JTokenType.Null ? null : t;
            var wrong = new List<string>();
            foreach (var r in Fixture["refusals"].Cast<JObject>())
            {
                var refuses = (string)r["refuses"];
                var other = refuses == "pins" ? "served" : "pins";
                Exception thrown = null;
                try
                {
                    OWMapSpace.PinsKnownTo(Given(r["pins"]), Given(r["served"]));
                }
                catch (Exception e)
                {
                    thrown = e;
                }

                if (!(thrown is ArgumentException a)) wrong.Add($"{r["name"]}: {(thrown == null ? "answered" : "threw " + thrown.GetType().Name)}");
                else if (a.ParamName != refuses || !a.Message.Contains(refuses + " is a list") || a.Message.Contains(other + " is a list"))
                    wrong.Add($"{r["name"]}: \"{a.Message}\" ({a.ParamName}) does not name {refuses} alone");
            }

            Assert.IsEmpty(wrong, $"{wrong.Count} refusal(s) disagree:\n  " + string.Join("\n  ", wrong));
        }

        [Test]
        public void TheInputs_AreLeftAsTheyWere()
        {
            // Each case is parsed afresh from the file's bytes: a copy of the shared object would carry a write too.
            var before = Parse()["cases"].Cast<JObject>().ToList();
            var run = Parse()["cases"].Cast<JObject>().ToList();
            foreach (var c in run) OWMapSpace.PinsKnownTo(List(c["pins"]), List(c["served"]));
            for (var i = 0; i < run.Count; i++)
            {
                Assert.AreEqual(before[i]["pins"].ToString(Formatting.None), run[i]["pins"].ToString(Formatting.None), $"{run[i]["name"]}: pins");
                Assert.AreEqual(before[i]["served"].ToString(Formatting.None), run[i]["served"].ToString(Formatting.None), $"{run[i]["name"]}: served");
            }
        }

        [Test]
        public void TwoCalls_AnswerTwoObjects()
        {
            var pins = new JArray(new JObject { ["type"] = "pin", ["id"] = "p1", ["map"] = "m1", ["element_type"] = "location", ["element_id"] = "l1" });
            var served = new JArray(new JObject { ["type"] = "location", ["id"] = "l1" });
            var a = OWMapSpace.PinsKnownTo(pins, served);
            var b = OWMapSpace.PinsKnownTo(pins, served);
            Assert.AreNotSame(a, b);
            Assert.AreNotSame(a.Known, b.Known);
            Assert.AreNotSame(a.Known[0], b.Known[0]);
            Assert.AreNotSame(a.Out, b.Out);
            Assert.AreEqual(OWPinKnownHow.Whole, a.Known[0].How);
            Assert.IsTrue(((ICollection<OWPinKnown>)a.Known).IsReadOnly, "the list is read-only");
        }

        [Test]
        public void BesideMapSpace_ADrawingKeepsThePinsBothName()
        {
            var world = new JArray(
                new JObject { ["type"] = "map", ["id"] = "m1", ["width"] = 100, ["height"] = 100 },
                new JObject { ["type"] = "pin", ["id"] = "p1", ["map"] = "m1", ["x"] = 1, ["y"] = 2, ["element_type"] = "location", ["element_id"] = "l1" },
                new JObject { ["type"] = "pin", ["id"] = "p2", ["map"] = "m1", ["x"] = 3, ["y"] = 4, ["element_type"] = "location", ["element_id"] = "l2" },
                new JObject { ["type"] = "pin", ["id"] = "p3", ["map"] = "m1", ["element_type"] = "location", ["element_id"] = "l1" }, // known, with no point
                new JObject { ["type"] = "pin", ["id"] = "p4", ["map"] = "gone", ["x"] = 5, ["y"] = 6, ["element_type"] = "location", ["element_id"] = "l1" }, // known, on no Map
                new JObject { ["type"] = "location", ["id"] = "l1" },
                new JObject { ["type"] = "location", ["id"] = "l2" });
            var served = new JArray(new JObject { ["type"] = "location", ["id"] = "l1", ["known_of"] = true });
            var answer = OWMapSpace.PinsKnownTo(world, served);
            Assert.AreEqual(new[] { "p1", "p3", "p4" }, answer.Known.Select(k => k.Pin).ToArray());
            Assert.AreEqual(1, answer.Unknown);
            var how = answer.Known.ToDictionary(k => k.Pin, k => k.How);
            var drawn = OWMapSpace.Of(world).Pins.Where(p => how.ContainsKey(p.Pin)).Select(p => (p.Pin, p.Point.X, p.Point.Y, how[p.Pin])).ToArray();
            Assert.AreEqual(new[] { ("p1", 1L, 2L, OWPinKnownHow.KnownOf) }, drawn);
        }
    }
}
