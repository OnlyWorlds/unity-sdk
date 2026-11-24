// OWNameFinder: a world's element names found in prose. The shared cases (Fixtures/name-finder/name-finder.cases.json,
// version 2, the same bytes as the TypeScript SDK's test/fixtures/name-finder.cases.json and the Python package's copy,
// its sha256 pinned here): what Find answers and what Shared lists, then the timed recipes, then what the cases cannot
// hold: C# string indexes, the objects answered, purity, the refusals, and the one fact about folding the matcher
// stands on. Ported from @onlyworlds/sdk 4.194.0 and 4.198.0, test/name-finder.test.mjs.

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
        private const string CasesSha256 = "8e91e79981830511055976011f9fc6d4f4b62fb0183c29bad61c2d05fd4b13e4";

        private const int CaseCount = 62;

        private const int TimedCount = 10;

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
            Assert.AreEqual(2, (int)Fixture["version"]);
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

        /// <summary>One case's <see cref="OWNameFinder{T}.Shared"/> in the fixture's own shape: each name as compared, and the ids of the elements that hold it.</summary>
        private static JArray SharedOf(JObject c)
        {
            var nameCase = (string)c["case"] == "folded" ? OWNameCase.Folded : (string)c["case"] == "exact" ? OWNameCase.Exact : (OWNameCase)0;
            var got = new JArray();
            foreach (var s in OWNameFinder.For((JArray)c["elements"], nameCase, NamesOf).Shared)
                got.Add(new JObject { ["name"] = s.Name, ["ids"] = new JArray(s.Elements.Select(e => e["id"])) });
            return got;
        }

        [Test]
        public void EveryCase_ListsTheNamesElementsShare_AsTheCasesDo()
        {
            Assert.IsNotNull(Fixture, "the vendored name finder cases were not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                var got = SharedOf(c);
                if (!JToken.DeepEquals(c["shared"], got))
                    wrong.Add($"{c["name"]}\n    want {c["shared"].ToString(Newtonsoft.Json.Formatting.None)}\n    got  {got.ToString(Newtonsoft.Json.Formatting.None)}");
            }

            Assert.IsEmpty(wrong, $"{wrong.Count} case(s) disagree:\n  " + string.Join("\n  ", wrong));
            Assert.IsTrue(cases.Count(c => ((JArray)c["shared"]).Count > 0) >= 10 && cases.Any(c => ((JArray)c["shared"]).Count > 1));
            foreach (var fold in new[] { "exact", "folded" })
                Assert.IsTrue(cases.Any(c => (string)c["case"] == fold && ((JArray)c["shared"]).Count > 0), fold);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_GivesTheCasesAnswer(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored name finder cases were not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var got = Answer(c);
            Assert.IsTrue(JToken.DeepEquals(c["want"], got),
                $"{name}: gave {got.ToString(Newtonsoft.Json.Formatting.None)}, the cases say {c["want"].ToString(Newtonsoft.Json.Formatting.None)}");
            var shared = SharedOf(c);
            Assert.IsTrue(JToken.DeepEquals(c["shared"], shared),
                $"{name}: lists {shared.ToString(Newtonsoft.Json.Formatting.None)} as shared, the cases say {c["shared"].ToString(Newtonsoft.Json.Formatting.None)}");
        }

        [Test]
        public void TheCases_HoldAnswersOfBothKinds_SomeNameFound_AndNone()
        {
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.IsTrue(cases.Any(c => ((JArray)c["want"]).Count > 0) && cases.Any(c => ((JArray)c["want"]).Count == 0));
            foreach (var fold in new[] { "exact", "folded" })
                Assert.IsTrue(cases.Any(c => (string)c["case"] == fold && ((JArray)c["want"]).Count > 0), fold);
        }

        /// <summary>
        /// A timed recipe built as both packages' tests build it: <c>{i}</c> is the count from 0; a long name is its
        /// <c>repeat</c>, <c>times</c> over, and then its <c>then</c>.
        /// </summary>
        private static (List<JToken> elements, string text) Build(JObject recipe)
        {
            string Pattern(string p, int i) => p.Replace("{i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var elements = new List<JToken>();
            var count = (int)recipe["elements"]["count"];
            var long_ = recipe["elements"]["long"] as JObject;
            string longName = null;
            if (long_ != null)
            {
                var name = new StringBuilder();
                for (var i = 0; i < (int)long_["times"]; i++) name.Append((string)long_["repeat"]);
                longName = name.Append((string)long_["then"]).ToString();
            }

            for (var i = 0; i < count; i++)
                elements.Add(new JObject { ["id"] = "e" + i, ["name"] = longName ?? Pattern((string)recipe["elements"]["pattern"], i) });
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
        /// second. On Mono the slowest takes a fifth to a third of a second (the address prefixes, whose cost is
        /// <see cref="OWAddress.FindLinks"/>'s; every other recipe is under a tenth), so the shared line would sit only
        /// six times above it on a quiet machine; the line here is five times <c>underMs</c>, the factor the Python
        /// package's test takes for the same reason: far above a loaded machine's run, and far below what the old bound
        /// (the text's length times the longest name's) cost on the three recipes with a name of thousands of
        /// characters, 25 and 50 seconds on Mono for two of them.
        /// </summary>
        private const int MonoFactor = 5;

        [Test]
        public void TheTimedRecipes_EachAnswersItsCount_WellWithinTheBound()
        {
            var timed = Fixture["timed"].Cast<JObject>().ToList();
            Assert.AreEqual(TimedCount, timed.Count);
            Assert.IsTrue(timed.Count(r => r["elements"]["long"] is JObject l && (int)l["times"] >= 2500) >= 3,
                "three recipes with a name of thousands of characters");
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
        /// Inputs the shared cases are silent on, each answered as the TypeScript SDK 4.194.0 and 4.198.0 answer it (run
        /// there, its answers written here in its own UTF-16 offsets; the Python package answers the same in code points).
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
        public void Shared_IsTheObjectsPassedIn_InTheOrderGiven_ReadOnly_AndReadOnce()
        {
            var a = new JObject { ["id"] = "a", ["name"] = "Ember" };
            var b = new JObject { ["id"] = "b", ["name"] = "EMBER " };
            var k = new JObject { ["id"] = "k", ["name"] = "Kira" };
            var elements = new List<JToken> { k, b, a };
            var find = OWNameFinder.For(elements, OWNameCase.Folded);
            Assert.AreEqual(1, find.Shared.Count);
            Assert.AreEqual("ember", find.Shared[0].Name);
            Assert.AreSame(b, find.Shared[0].Elements[0]);
            Assert.AreSame(a, find.Shared[0].Elements[1]);
            Assert.IsTrue(((ICollection<OWNameShared<JToken>>)find.Shared).IsReadOnly, "the list is read-only");
            Assert.IsTrue(((ICollection<JToken>)find.Shared[0].Elements).IsReadOnly, "an entry's elements are read-only");
            Assert.Throws<NotSupportedException>(() => ((IList<JToken>)find.Shared[0].Elements)[0] = k);
            Assert.AreSame(find.Shared, find.Shared, "one list, made when the finder was");
            elements.Add(new JObject { ["id"] = "x", ["name"] = "Kira" });
            Assert.AreEqual(1, find.Shared.Count, "the names were read once, when the finder was made");
            Assert.AreEqual(new[] { "Kira" }, OWNameFinder.For(elements, OWNameCase.Exact).Shared.Select(s => s.Name).ToArray());
            Assert.IsEmpty(OWNameFinder.For(new JToken[0], OWNameCase.Exact).Shared);
            Assert.AreEqual(new[] { "k" }, find.Find("Ember, ember, Kira").Select(f => (string)f.Element["id"]).ToArray(), "a listed name is never answered");
        }

        /// <summary>
        /// The matcher lays a mark before every character of a name that is not a word character, read from the name as
        /// folded, and before every such character of the text, read as written. The two agree only while no character
        /// changes kind when it is folded. The TypeScript SDK holds that on every code point of its runtime; here the
        /// fold is Unicode 15.1's and the categories Mono's (Unicode 11.0), so a capital assigned since 11.0 whose fold
        /// is an older letter (U+A7C4 to U+A7C6, Unicode 12.0) is no word character here and its fold is one. What is
        /// held: every code point that changes kind when folded is one this runtime does not know, the only place the
        /// word edge promises nothing (<see cref="BesideACapitalThisRuntimeDoesNotKnow_TheRuntimesReadStands"/>).
        /// </summary>
        [Test]
        public void Folding_MovesACharacterIntoOrOutOfAWord_OnlyWhereThisRuntimeDoesNotKnowIt()
        {
            var folds = 0;
            var moved = new List<string>();
            var known = new List<string>();
            for (var cp = 0; cp <= 0x10ffff; cp++)
            {
                if (cp >= 0xd800 && cp <= 0xdfff) continue;
                var f = OWCaseFold.Fold(cp);
                if (f == cp) continue;
                folds++;
                if (OWNameFinder.IsWord(cp) == OWNameFinder.IsWord(f)) continue;
                moved.Add($"U+{cp:X4}");
                if (Category(cp) != System.Globalization.UnicodeCategory.OtherNotAssigned
                    && Category(f) != System.Globalization.UnicodeCategory.OtherNotAssigned)
                    known.Add($"U+{cp:X4}");
            }

            TestContext.WriteLine($"{folds} code points fold; {moved.Count} change kind in this runtime: {string.Join(", ", moved)}");
            Assert.Greater(folds, 1400, $"{folds} code points fold");
            CollectionAssert.IsEmpty(known, "a code point this runtime knows changes kind when folded");
        }

        private static System.Globalization.UnicodeCategory Category(int cp)
            => cp < 0x10000
                ? System.Globalization.CharUnicodeInfo.GetUnicodeCategory((char)cp)
                : System.Globalization.CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);

        /// <summary>
        /// Beside or inside a name, a character this runtime does not know (here U+A7C4, Unicode 12.0, a capital whose
        /// fold U+A794 Mono knows as a letter) reads as no word character, as the word edge's ruling has it: a code
        /// point assigned after Unicode 11.0 is promised nothing. The TypeScript SDK on a current Node answers
        /// <c>[(0, 4), (5, 9)]</c> for the first, the same as here for the second, and nothing for the third.
        /// </summary>
        [Test]
        public void BesideACapitalThisRuntimeDoesNotKnow_TheRuntimesReadStands()
        {
            const string capital = "Ꞔ";
            const string small = "ꞔ";
            // Folded: the capital in the text lays a word edge the folded name does not hold.
            CollectionAssert.AreEqual(new[] { (5, 9) }, Spans("a" + capital + "bc", OWNameCase.Folded, $"a{capital}bc a{small}bc"));
            // Exact: name and text read the capital alike.
            CollectionAssert.AreEqual(new[] { (0, 4) }, Spans("a" + capital + "bc", OWNameCase.Exact, $"a{capital}bc a{small}bc"));
            // At a name's edge the capital is no word character, in either case (as before this matcher).
            foreach (var nameCase in new[] { OWNameCase.Exact, OWNameCase.Folded })
                CollectionAssert.AreEqual(new[] { (1, 4), (10, 13) }, Spans("abc", nameCase, $"{capital}abc {small}abc abc{capital}"), nameCase.ToString());
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
            Assert.IsEmpty(find.Shared, "one id listed twice shares nothing");
            var two = OWNameFinder.For(rows.Append(("j", "Iron Gate")), OWNameCase.Exact, r => r.Item1, r => new object[] { r.Item2 });
            Assert.AreEqual(new[] { ("g", "Iron Gate"), ("j", "Iron Gate") }, two.Shared.Single().Elements.ToArray(), "the tuples passed in, in the order given");
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
