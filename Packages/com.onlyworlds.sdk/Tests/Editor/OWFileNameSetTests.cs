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
    /// A new element's slug (folder spec §3.2, "How the slug is made"): NFKD; U+0300 to U+036F dropped and no other
    /// mark; lowercased; every run outside a-z and 0-9 one hyphen; hyphens at both ends dropped; cut at 40 and the new
    /// end's hyphens dropped. A name that leaves no slug gives the full id.
    /// </summary>
    /// <remarks>
    /// The cases (<c>Fixtures/file-names/file-names.fixture.json</c>) are the shared file-name set, held by their
    /// sha256: 38 names, each slug the one the TypeScript package's folder writer and the Python package's
    /// <c>slugify</c> both give.
    /// </remarks>
    public class OWFileNameSetTests
    {
        private const string FixtureSha256 = "0da1846641e116b2ff39269261779854e794db117388bd912e5aff68a243a91d";

        private const int CaseCount = 38;

        private const string Id = "019a9300-0000-7000-8000-0000000000c9";

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                Assert.IsNotNull(root, "the package's own path is how the cases are found");
                var path = Path.Combine(root, "Tests", "Editor", "Fixtures", "file-names", "file-names.fixture.json");
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

        [Test]
        public void TheFixture_IsTheSharedBytes()
        {
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(FixturePath))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(FixtureSha256, hash,
                "the vendored set is not the shared bytes: re-vendor it (a checkout's line endings change the bytes)");
        }

        [Test]
        public void EveryCase_TheSlugAndTheFileName_AreTheSentences()
        {
            var off = new List<string>();
            foreach (var c in Cases())
            {
                var name = (string)c["element_name"];
                var slug = (string)c["slug"];
                var got = OWFolderWriter.Slugify(name);
                if (got != slug) off.Add($"{c["name"]}: slug \"{got}\", the set's \"{slug}\"");
                var file = OWFolderWriter.ElementFilename(Id, name);
                var want = slug.Length == 0 ? Id + ".json" : slug + "--000000c9.json";
                if (file != want) off.Add($"{c["name"]}: file \"{file}\", the set's \"{want}\"");
            }
            Assert.IsEmpty(off, string.Join("\n", off));
        }
    }
}
