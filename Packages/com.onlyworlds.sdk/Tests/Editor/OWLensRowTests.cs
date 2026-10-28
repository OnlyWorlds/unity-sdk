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
    /// A lens's rows are the bodies as the reader is served them (lens.md, "Resolution"; keel's
    /// <c>elements/serialize.py</c> and <c>elements/visibility.py</c> <c>redact</c>), replayed over the row-trim cases the
    /// TypeScript SDK (<c>test/fixtures/row-trim-cases.json</c>) and the Python package
    /// (<c>tests/fixtures/row-trim-cases.json</c>) share byte for byte, vendored here in <c>Tests/Editor/Fixtures/lens/</c>
    /// from the Python package (<c>github.com/OnlyWorlds/onlyworlds-python</c>, commit <c>0fadf58</c>). A reader that is
    /// not the owner gets, in every link field, only the ids it is served as that field's target type; a Pin's pair
    /// emptied (<c>""</c>, null) when its target is unseen or not of its <c>element_type</c>; a lens's notes keyed to an
    /// element it cannot see left out. <c>x_served_trimmed</c> names only a Message's <c>characters</c> and
    /// <c>narrator</c>, and a lens's anchor fields and <c>notes</c> (for a note on an anchor left out), below co-builder
    /// and never on the public rung. The owner's rows are its bodies untouched. When the packages move the file,
    /// re-vendor it and update the hash below.
    /// </summary>
    /// <remarks>
    /// A case is <c>name</c>, <c>world</c> (its <c>elements</c> as the owner holds them, its <c>now</c>), <c>lens</c> (a
    /// body, or the id of a lens in the world), <c>reader</c> (<c>rung</c>; a guest's <c>whole</c> and <c>known_of</c>;
    /// <c>as</c> for the owner reading as another reader) and <c>want_rows</c>, the rows keel serves that reader, compared
    /// as JSON (key order aside).
    /// </remarks>
    public class OWLensRowTests
    {
        /// <summary>The file's sha256, as the TypeScript SDK's and the Python package's suites hold it.</summary>
        private const string Sha256 = "a03998723cc2c717831785374e84fd6ac7e503f98d92a3218be5ec733c0168da";

        private static JArray _cases;

        private static string CasesPath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "lens", "row-trim-cases.json");
            }
        }

        private static JArray CaseList
        {
            get
            {
                if (_cases != null) return _cases;
                var path = CasesPath;
                if (path == null || !File.Exists(path)) return null;
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    return _cases = JArray.Load(reader);
                }
            }
        }

        /// <summary>One case per file case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var cases = CaseList;
            if (cases == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Rows_CasesNotFound");
                yield break;
            }

            foreach (var c in cases.Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Rows_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheCases_AreThePackagesBytes()
        {
            var path = CasesPath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored row-trim cases were not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(Sha256, hash,
                "the vendored cases are not the packages' file: re-vendor it (a checkout's line endings change the bytes)");
            Assert.AreEqual(20, CaseList.Count, "the file holds 20 cases");
        }

        [TestCaseSource(nameof(Cases))]
        public void ARow_IsTheBodyKeelServesThatReader(string name)
        {
            Assert.IsNotNull(CaseList, "the vendored row-trim cases were not found");
            var c = CaseList.Cast<JObject>().Single(x => (string)x["name"] == name);
            var fixtureWorld = (JObject)c["world"];
            var world = fixtureWorld["elements"].Cast<JObject>().Select(e => (JObject)e.DeepClone()).ToList();
            var options = new OWLensOptions
            {
                Now = fixtureWorld["now"] == null || fixtureWorld["now"].Type == JTokenType.Null ? null : (int?)fixtureWorld["now"],
                HiddenIds = Strings(fixtureWorld["hidden"]),
            };
            var reader = ReaderOf((JObject)c["reader"]);
            var lens = c["lens"];
            var got = lens.Type == JTokenType.String
                ? OWLens.Resolve(world, (string)lens, reader, options)
                : OWLens.Resolve(world, (JObject)lens, reader, options);

            Assert.IsTrue(got.IsReadable, $"{name}: refused {got.RefusedKey}");
            var rows = new JArray(got.Rows.Select(x => x.DeepClone()));
            Assert.IsTrue(JToken.DeepEquals(c["want_rows"], rows),
                $"{name}: rows {rows.ToString(Formatting.None)}, want {c["want_rows"].ToString(Formatting.None)}");
        }

        /// <summary>A case's reader: its rung, a guest's lists, its character; <c>as</c> is the owner reading as another.</summary>
        private static OWLensReader ReaderOf(JObject r)
        {
            if (r["as"] is JObject other) return ReaderOf(other).WithOwnerCounts();
            var character = r["character"]?.Type == JTokenType.String ? (string)r["character"] : null;
            switch ((string)r["rung"])
            {
                case "owner": return OWLensReader.Owner(character);
                case "contributor": return OWLensReader.Contributor(character);
                case "public": return OWLensReader.Public();
                case "guest": return OWLensReader.Guest(Strings(r["whole"]), Strings(r["known_of"]), character);
                default: throw new ArgumentException("a case reader of unknown rung: " + r["rung"]);
            }
        }

        private static List<string> Strings(JToken token) =>
            token is JArray array ? array.Select(t => (string)t).ToList() : new List<string>();
    }
}
