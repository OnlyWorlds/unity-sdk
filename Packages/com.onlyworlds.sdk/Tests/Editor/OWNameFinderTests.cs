// OWNameFinder: a world's element names found in prose. The shared cases (Fixtures/name-finder/name-finder.cases.json,
// the same bytes as the TypeScript SDK's test/fixtures/name-finder.cases.json and the Python package's copy, its
// sha256 pinned here), their timed recipes, then what the cases cannot hold: C# string indexes, the object answered,
// purity and the refusals. Ported from @onlyworlds/sdk 4.194.0, test/name-finder.test.mjs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    public class OWNameFinderTests
    {
        /// <summary>The cases file's sha256, as both packages' suites hold it.</summary>
        private const string CasesSha256 = "4c4d7d3b4480e4c20bab799972faf90c542b5b570c1b43f7882291444c31007a";

        private const int CaseCount = 47;

        private const int TimedCount = 7;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "name-finder", "name-finder.cases.json");
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

        /// <summary>One test per shared case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("NameFinder_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) && ch < 0x80 ? ch : '_').ToArray());
                yield return new TestCaseData(name).SetName("NameFinder_" + safe);
            }
        }

        /// <summary>An element of a case is found by its <c>names</c> list when it has one, else by its <c>name</c>.</summary>
        private static IEnumerable<object> NamesOf(JToken e) => e["names"] is JArray a ? a.Cast<object>() : new object[] { e["name"] };

        /// <summary>How many code points the units from <paramref name="from"/> to <paramref name="to"/> hold: a pair is one, a lone surrogate one.</summary>
        private static int Points(string text, int from, int to)
        {
            var n = 0;
            for (var k = from; k < to; k++)
            {
                if (char.IsHighSurrogate(text[k]) && k + 1 < to && char.IsLowSurrogate(text[k + 1])) k++;
                n++;
            }

            return n;
        }

        /// <summary>One case's answer in the fixture's own shape: each found name's place and length in code points, and its element's id.</summary>
        private static JArray Answer(JObject c)
        {
            var nameCase = (string)c["case"] == "folded" ? OWNameCase.Folded : (string)c["case"] == "exact" ? OWNameCase.Exact : (OWNameCase)0;
            var text = (string)c["text"];
            var got = new JArray();
            foreach (var f in OWNameFinder.For((JArray)c["elements"], nameCase, NamesOf).Find(text))
                got.Add(new JObject { ["at"] = Points(text, 0, f.Start), ["length"] = Points(text, f.Start, f.End), ["id"] = f.Element["id"] });
            return got;
        }

        [Test]
        public void TheCasesFile_IsTheSharedBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored name finder cases were not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(CasesSha256, hash,
                "the vendored cases are not the packages' bytes: re-vendor them (a checkout's line endings change the bytes)");
            Assert.AreEqual(1, (int)Fixture["version"]);
        }

        [Test]
        public void EveryCase_IsAnswered_AndNoneIsDropped()
        {
            Assert.IsNotNull(Fixture, "the vendored name finder cases were not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(CaseCount, cases.Count, $"the cases file holds {CaseCount} cases");
            Assert.AreEqual(CaseCount, cases.Select(c => (string)c["name"]).Distinct().Count(), "every case's name is its own");
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                var got = Answer(c);
                if (!JToken.DeepEquals(c["want"], got))
                    wrong.Add($"{c["name"]}\n    want {c["want"].ToString(Newtonsoft.Json.Formatting.None)}\n    got  {got.ToString(Newtonsoft.Json.Formatting.None)}");
            }

            Assert.IsEmpty(wrong, $"{wrong.Count} case(s) disagree:\n  " + string.Join("\n  ", wrong));
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_GivesTheCasesAnswer(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored name finder cases were not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var got = Answer(c);
            Assert.IsTrue(JToken.DeepEquals(c["want"], got),
                $"{name}: gave {got.ToString(Newtonsoft.Json.Formatting.None)}, the cases say {c["want"].ToString(Newtonsoft.Json.Formatting.None)}");
        }

        [Test]
        public void TheCases_HoldAnswersOfBothKinds_SomeNameFound_AndNone()
        {
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.IsTrue(cases.Any(c => ((JArray)c["want"]).Count > 0) && cases.Any(c => ((JArray)c["want"]).Count == 0));
            foreach (var fold in new[] { "exact", "folded" })
                Assert.IsTrue(cases.Any(c => (string)c["case"] == fold && ((JArray)c["want"]).Count > 0), fold);
        }

        /// <summary>A timed recipe built as both packages' tests build it: <c>{i}</c> is the count from 0.</summary>
        private static (List<JToken> elements, string text) Build(JObject recipe)
        {
            string Pattern(string p, int i) => p.Replace("{i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var elements = new List<JToken>();
            var count = (int)recipe["elements"]["count"];
            for (var i = 0; i < count; i++)
                elements.Add(new JObject { ["id"] = "e" + i, ["name"] = Pattern((string)recipe["elements"]["pattern"], i) });
            var text = new StringBuilder();
            var t = (JObject)recipe["text"];
            if (t["repeat"] != null)
            {
                var repeat = (string)t["repeat"];
                for (var i = 0; i < (int)t["times"]; i++) text.Append(repeat);
            }
            else
            {
                for (var i = 0; i < (int)t["times"]; i++) text.Append(Pattern((string)t["cycle"], i % (int)t["count"]));
            }

            return (elements, text.ToString());
        }

        /// <summary>
        /// The cases' <c>underMs</c> is the TypeScript SDK's line, where its slowest recipe takes about a tenth of a
        /// second. On Mono the slowest took a third of a second when this was written (the address prefixes, whose cost
        /// is <see cref="OWAddress.FindLinks"/>'s), so the shared line would sit only six times above it on a quiet
        /// machine; the line here is five times <c>underMs</c>, the factor the Python package's test takes for the same
        /// reason: far above a loaded machine's run, and far below what work that grows with the square of the text
        /// would take at these sizes.
        /// </summary>
        private const int MonoFactor = 5;

        [Test]
        public void TheTimedRecipes_EachAnswersItsCount_WellWithinTheBound()
        {
            var timed = Fixture["timed"].Cast<JObject>().ToList();
            Assert.AreEqual(TimedCount, timed.Count);
            var underMs = (int)Fixture["underMs"] * MonoFactor;
            var slow = new List<string>();
            foreach (var recipe in timed)
            {
                var (elements, text) = Build(recipe);
                var find = OWNameFinder.For(elements, OWNameCase.Exact);
                var watch = Stopwatch.StartNew();
                var found = find.Find(text);
                watch.Stop();
                TestContext.WriteLine($"{recipe["name"]}: {found.Count} found in {watch.ElapsedMilliseconds} ms (text {text.Length} units)");
                Assert.AreEqual((int)recipe["count"], found.Count, (string)recipe["name"]);
                if (watch.ElapsedMilliseconds >= underMs) slow.Add($"{recipe["name"]}: {watch.ElapsedMilliseconds} ms");
            }

            Assert.IsEmpty(slow, $"over {underMs} ms: " + string.Join("; ", slow));
        }

        [Test]
        public void StartAndEnd_AreCSharpStringIndexes_AndTheSubstringIsTheNameAsWritten()
        {
            var dragon = char.ConvertFromUtf32(0x1f409);
            var text = $"{dragon} Kira met {dragon}{dragon} KIRA.";
            var find = OWNameFinder.For(new JToken[] { new JObject { ["id"] = "k", ["name"] = "Kira" } }, OWNameCase.Folded);
            var got = find.Find(text).Select(f => (f.Start, f.End, text.Substring(f.Start, f.End - f.Start))).ToList();
            CollectionAssert.AreEqual(new[] { (3, 7, "Kira"), (17, 21, "KIRA") }, got);
        }

        private static (int, int)[] Spans(string name, OWNameCase nameCase, string text)
            => OWNameFinder.For(new JToken[] { new JObject { ["id"] = "k", ["name"] = name } }, nameCase).Find(text).Select(f => (f.Start, f.End)).ToArray();

        /// <summary>
        /// Inputs the shared cases are silent on, each answered as the TypeScript SDK 4.194.0 answers it (run there, its
        /// answers written here in its own UTF-16 offsets; the Python package answers the same in code points).
        /// </summary>
        [Test]
        public void WhereTheCasesAreSilent_TheAnswerIsTheTypeScriptSdks()
        {
            var dragon = char.ConvertFromUtf32(0x1f409);
            const string highAlone = "\ud83d";
            const string acute = "́";
            // A lone surrogate is no word character, and a name that holds one matches it only where it stands alone.
            CollectionAssert.AreEqual(new[] { (1, 5) }, Spans("Kira", OWNameCase.Exact, highAlone + "Kira"));
            CollectionAssert.AreEqual(new[] { (5, 8) }, Spans("Ki" + highAlone, OWNameCase.Exact, $"Ki{dragon} Ki{highAlone}."));
            // A name of punctuation alone is whole where the code point before it is no word character.
            CollectionAssert.AreEqual(new[] { (11, 14), (29, 32) }, Spans("???", OWNameCase.Exact, "Who??? and ??? and a???b and ????"));
            // A name that starts with a combining mark: whole after a space, inside a word after a letter.
            CollectionAssert.AreEqual(new[] { (1, 5) }, Spans(acute + "Ana", OWNameCase.Exact, $" {acute}Ana e{acute}Ana"));
            // Brackets at the text's end are prose; a link's target ends at its first closing parenthesis.
            CollectionAssert.AreEqual(new[] { (1, 5) }, Spans("Kira", OWNameCase.Exact, "[Kira]"));
            CollectionAssert.AreEqual(new[] { (1, 5) }, Spans("Kira", OWNameCase.Exact, "[Kira]("));
            CollectionAssert.AreEqual(new[] { (10, 14), (16, 20) }, Spans("Kira", OWNameCase.Exact, "[a](b(c)) Kira) Kira"));
            CollectionAssert.AreEqual(new[] { (0, 4), (9, 13), (18, 22) }, Spans("Kira", OWNameCase.Exact, "Kira[](x)Kira[x]()Kira"));
            // A letter number (Nl) is no word character.
            CollectionAssert.AreEqual(new[] { (0, 4), (10, 14) }, Spans("Kira", OWNameCase.Exact, "KiraⅫ and Kira"));
            // Folded: a name that ends in a letter above the Basic Multilingual Plane, the text in the other case.
            var deseret = char.ConvertFromUtf32(0x10400);
            CollectionAssert.AreEqual(new[] { (0, 4), (5, 9) },
                Spans("Ab" + deseret, OWNameCase.Folded, "AB" + char.ConvertFromUtf32(0x10428) + " ab" + deseret));
        }

        [Test]
        public void Element_IsTheObjectPassedIn_AndTheDefaultNameIsItsName()
        {
            var kira = new JObject { ["id"] = "k", ["name"] = "Kira", ["type"] = "character", ["description"] = "A watchman." };
            var f = OWNameFinder.For(new JToken[] { kira }, OWNameCase.Exact).Find("Kira.").Single();
            Assert.AreSame(kira, f.Element);
            Assert.AreSame(kira, OWNameFinder.For(new HashSet<JToken> { kira }, OWNameCase.Exact).Find("Kira.")[0].Element, "any enumerable of elements");
        }

        [Test]
        public void TypedElements_AreFoundByTheirName_AndAnsweredAsPassedIn()
        {
            var kira = new OWElement { Id = "k", Name = "Kira" };
            var unnamed = new OWElement { Id = "u" };
            var f = OWNameFinder.For(new[] { kira, unnamed }, OWNameCase.Folded).Find("KIRA.").Single();
            Assert.AreSame(kira, f.Element);
            var byAlias = OWNameFinder.For(new[] { kira }, OWNameCase.Exact, e => new object[] { e.Name, "The Watch" });
            Assert.AreEqual(new[] { "k", "k" }, byAlias.Find("The Watch and Kira").Select(x => x.Element.Id).ToArray());
        }

        [Test]
        public void ElementsOfTheCallersOwnKind_TakeAnIdAndTheirNames()
        {
            var rows = new[] { ("k", "Kira"), ("g", "Iron Gate"), ("k", "Kira") };
            var find = OWNameFinder.For(rows, OWNameCase.Exact, r => r.Item1, r => new object[] { r.Item2 });
            Assert.AreEqual(new[] { "g", "k" }, find.Find("At the Iron Gate, Kira.").Select(x => x.Element.Item1).ToArray(), "one id listed twice is one element");
        }

        [Test]
        public void ANameIsAStringOrAJsonString_AndAnyOtherValueIsPassedOver()
        {
            var e = new JObject { ["id"] = "k", ["name"] = "Kira" };
            var find = OWNameFinder.For(new JToken[] { e }, OWNameCase.Exact,
                x => new object[] { "Varro", new JValue("Wren"), new JValue(7), JValue.CreateNull(), null, 42, new JArray("Mira") });
            Assert.AreEqual(new[] { 0, 10 }, find.Find("Varro and Wren and 7 and Mira").Select(f => f.Start).ToArray());
        }

        [Test]
        public void NothingGivenIsChanged_AndOneFinderAnswersTheSameForTheSameText()
        {
            var elements = new List<JToken>
            {
                new JObject { ["id"] = "k", ["name"] = "  Kira  ", ["aliases"] = new JArray("The Watch") },
                new JObject { ["id"] = "g", ["name"] = "Iron Gate" },
            };
            var before = new JArray(elements).ToString(Newtonsoft.Json.Formatting.None);
            var find = OWNameFinder.For(elements, OWNameCase.Folded,
                x => new object[] { x["name"] }.Concat(x["aliases"] is JArray a ? a.Cast<object>() : Enumerable.Empty<object>()));
            const string text = "the watch saw Kira at the IRON GATE";
            var first = find.Find(text);
            Assert.AreEqual(before, new JArray(elements).ToString(Newtonsoft.Json.Formatting.None));
            Assert.AreEqual(new[] { "k", "k", "g" }, first.Select(f => (string)f.Element["id"]).ToArray());
            ((List<OWNameFound<JToken>>)first).Clear();
            Assert.AreEqual(3, find.Find(text).Count, "an answer is the caller's own list");
            elements.Add(new JObject { ["id"] = "x", ["name"] = "saw" });
            Assert.AreEqual(3, find.Find(text).Count, "the names were read once, when the finder was made");
        }

        [Test]
        public void Refusals_EachByName_BeforeAnythingIsRead()
        {
            var els = new JToken[] { new JObject { ["id"] = "k", ["name"] = "Kira" } };
            var noCase = Assert.Throws<ArgumentException>(() => OWNameFinder.For(els, default(OWNameCase)));
            StringAssert.Contains("nameCase is OWNameCase.Exact or OWNameCase.Folded, and must be given; got 0", noCase.Message);
            Assert.Throws<ArgumentException>(() => OWNameFinder.For(els, (OWNameCase)3));
            Assert.Throws<ArgumentNullException>(() => OWNameFinder.For((IEnumerable<JToken>)null, OWNameCase.Exact));
            foreach (var bad in new JToken[] { new JObject { ["name"] = "Kira" }, new JObject { ["id"] = 7, ["name"] = "Kira" },
                         new JObject { ["id"] = "", ["name"] = "Kira" }, null, new JValue("Kira"), new JArray() })
            {
                var e = Assert.Throws<ArgumentException>(() => OWNameFinder.For(new[] { bad }, OWNameCase.Exact), bad?.ToString() ?? "null");
                StringAssert.Contains("every element is an object with a string id", e.Message);
            }

            Assert.Throws<ArgumentException>(() => OWNameFinder.For(new OWElement[] { null }, OWNameCase.Exact));
            Assert.Throws<ArgumentException>(() => OWNameFinder.For(new[] { new OWElement { Name = "Kira" } }, OWNameCase.Exact));
            Assert.Throws<ArgumentNullException>(() => OWNameFinder.For(new[] { "k" }, OWNameCase.Exact, null, s => new object[] { s }));
            Assert.Throws<ArgumentNullException>(() => OWNameFinder.For(new[] { "k" }, OWNameCase.Exact, s => s, null));
            Assert.Throws<ArgumentException>(() => OWNameFinder.For(els, OWNameCase.Exact, x => null));
            var text = Assert.Throws<ArgumentNullException>(() => OWNameFinder.For(els, OWNameCase.Exact).Find(null));
            StringAssert.Contains("text must be a string", text.Message);
        }
    }
}
