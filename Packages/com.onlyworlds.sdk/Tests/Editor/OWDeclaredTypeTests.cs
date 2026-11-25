using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// A declared type (folder spec §4.1, ruled 2026-11-22): a string exactly equal to one of the 22 type slugs as the
    /// standard spells them; anything else in <c>type</c> (an unknown word, another case such as <c>"Location"</c>, a
    /// number, null) declares nothing, and the directory fills it. A file whose body declares one of the 22 and sits in
    /// another type's directory is read as the body says.
    /// </summary>
    /// <remarks>
    /// The cases (<c>Fixtures/declared-type/declared-type.cases.json</c>) are the same bytes as the Python package's
    /// <c>tests/fixtures/declared-type.cases.json</c> and the TypeScript SDK's <c>test/fixtures/declared-type.cases.json</c>;
    /// all three suites hold the file's sha256. Each case is one file, its JSON as written, and the type a conforming
    /// reader serves. The folder on disk and the same files in memory are read through one reader; the portal's one-file
    /// export takes the same rule for the bodies in each type's list.
    /// </remarks>
    public class OWDeclaredTypeTests
    {
        /// <summary>The cases file's sha256, as the Python and TypeScript suites hold it.</summary>
        private const string CasesSha256 = "89968d85deaa4d5974a20a4d73aab80c7e8ec374ee4236b0809e00c405831b96";

        private const int CaseCount = 21;

        private const string World = "{\"id\": \"w\", \"name\": \"W\"}";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ow-declared-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                Assert.IsNotNull(root, "the package's own path is how the cases are found");
                var path = Path.Combine(root, "Tests", "Editor", "Fixtures", "declared-type", "declared-type.cases.json");
                Assert.IsTrue(File.Exists(path), path);
                return path;
            }
        }

        private static List<JObject> Cases()
        {
            var cases = OWJson.ParseObject(File.ReadAllText(FixturePath))["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(CaseCount, cases.Count, "the shared cases' count");
            return cases;
        }

        private static string Text(JObject c, string key) => (string)c[key];

        /// <summary>The one element a case's folder serves, from either layout; fails when there is not exactly one.</summary>
        private static OWFolderElement Only(OWFolderReadResult read, string name)
        {
            var all = read.Elements.Concat(read.LegacyElements).Where(e => e.Id == "x1").ToList();
            Assert.AreEqual(1, all.Count, $"{name}: one element served; skipped: "
                                          + string.Join(", ", read.Skipped.Select(s => s.Path + " (" + s.Reason + ")")));
            return all[0];
        }

        [Test]
        public void TheCasesFile_IsTheSharedBytes()
        {
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(FixturePath))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(CasesSha256, hash,
                "the vendored cases are not the packages' bytes: re-vendor them (a checkout's line endings change the bytes)");
        }

        [Test]
        public void EveryCase_TheFolderOnDisk_ServesTheTypeTheCaseWants()
        {
            var off = new List<string>();
            var number = 0;
            foreach (var c in Cases())
            {
                var folder = Path.Combine(_root, "case-" + number++.ToString("00"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "world.json"), World);
                var file = Path.Combine(folder, Text(c, "path").Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, Text(c, "text"));

                var got = Only(OWFolderReader.Read(folder), Text(c, "name")).Type;
                if (got != Text(c, "want")) off.Add($"{Text(c, "name")}: served '{got}', want '{Text(c, "want")}'");
            }

            Assert.IsEmpty(off, string.Join("; ", off));
        }

        [Test]
        public void EveryCase_TheSameFilesInMemory_ServeTheTypeTheCaseWants()
        {
            var off = new List<string>();
            foreach (var c in Cases())
            {
                var entries = new[]
                {
                    new KeyValuePair<string, string>("world.json", World),
                    new KeyValuePair<string, string>(Text(c, "path"), Text(c, "text")),
                };
                var got = Only(OWFolderReader.ReadEntries(entries), Text(c, "name")).Type;
                if (got != Text(c, "want")) off.Add($"{Text(c, "name")}: served '{got}', want '{Text(c, "want")}'");
            }

            Assert.IsEmpty(off, string.Join("; ", off));
        }

        [Test]
        public void TheFlag_SaysWhereTheTypeCameFrom_AndTheBodyStaysAsWritten()
        {
            var served = new Dictionary<string, OWFolderElement>();
            foreach (var c in Cases())
            {
                var entries = new[]
                {
                    new KeyValuePair<string, string>("world.json", World),
                    new KeyValuePair<string, string>(Text(c, "path"), Text(c, "text")),
                };
                served[Text(c, "name")] = Only(OWFolderReader.ReadEntries(entries), Text(c, "name"));
            }

            Assert.IsFalse(served["another type's slug"].TypeInferred);
            Assert.IsFalse(served["the directory's own slug"].TypeInferred);
            Assert.IsTrue(served["no type key"].TypeInferred);
            Assert.IsTrue(served["another type, capital first"].TypeInferred);
            Assert.IsTrue(served["a number"].TypeInferred);
            Assert.IsTrue(served["a list holding a slug"].TypeInferred);
            Assert.AreEqual("Location", (string)served["another type, capital first"].Body["type"]);
            Assert.AreEqual(JTokenType.Integer, served["a number"].Body["type"].Type);
            Assert.AreEqual("map", served["the old layout, another slug"].Type);
            Assert.IsFalse(served["the old layout, another slug"].TypeInferred);
            Assert.IsTrue(served["the old layout, a capital"].TypeInferred);
        }

        /// <summary>
        /// The portal's one-file export holds each type's bodies in a list under the type's slug, so a case under
        /// <c>elements/&lt;type&gt;/</c> is that body in that list. The three cases under <c>spatial/</c> (the folder's dated
        /// layout) have no place in an export and are not asked here; the other 18 are.
        /// </summary>
        [Test]
        public void TheExport_EveryCaseUnderElements_ServesTheTypeTheCaseWants()
        {
            var off = new List<string>();
            var asked = 0;
            foreach (var c in Cases())
            {
                var path = Text(c, "path").Split('/');
                if (path[0] != "elements") continue;
                asked++;
                var json = "{\"format\":\"onlyworlds-world-export\",\"world\":" + World + ",\"elements\":{\"" + path[1] + "\":["
                           + Text(c, "text") + "]}}";
                var read = OWEnvelopeReader.Parse(json);
                Assert.AreEqual(1, read.Elements.Count, Text(c, "name") + ": one element served");
                var got = read.Elements[0].Type;
                if (got != Text(c, "want")) off.Add($"{Text(c, "name")}: served '{got}', want '{Text(c, "want")}'");
                if (got != Text(c, "want")) continue;
                var declared = read.Elements[0].Body["type"];
                var declares = declared != null && declared.Type == JTokenType.String && (string)declared == got;
                Assert.AreEqual(!declares, read.Elements[0].TypeInferred,
                    Text(c, "name") + ": inferred exactly when the body declares no slug");
            }

            Assert.AreEqual(18, asked, "the cases under elements/");
            Assert.IsEmpty(off, string.Join("; ", off));
        }
    }
}
