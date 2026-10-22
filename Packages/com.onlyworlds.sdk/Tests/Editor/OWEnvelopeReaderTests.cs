using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The portal's one-file export (keel's <c>world_export</c>, spec §8c). The fixture is keel's own: the emitter's
    /// output for one fixed world, pinned by keel's <c>tests/test_export_fixture.py</c> and published in schema-dist as
    /// <c>example/world-export.json</c> (serial 43, tag <c>v0.33.0-dist.43</c>), vendored byte for byte. When keel's
    /// emitter moves on purpose, the dist moves with it: re-vendor from the dist and update the hash below. The edge
    /// cases are inline.
    /// </summary>
    public class OWEnvelopeReaderTests
    {
        /// <summary>The fixture's sha256 as dist.43's MANIFEST.json records it.</summary>
        private const string DistSha256 = "50dc6c2ab8e2caeff77a26ae8ca10f4dbe5e11df3a6ae27a33fdc067e85314d2";

        private const string WorldId = "01999999-0000-7000-8000-000000000420";
        private const string Keep = "01999999-0000-7000-8000-000000000001";
        private const string Mill = "01999999-0000-7000-8000-000000000002";
        private const string Pip = "01999999-0000-7000-8000-000000000003";
        private const string Mara = "01999999-0000-7000-8000-000000000004";
        private const string Brave = "01999999-0000-7000-8000-000000000005";

        private static string FixturePath
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWEnvelopeReaderTests).Assembly);
                Assert.IsNotNull(package, "the package's own path is how the fixture is found");
                var path = Path.Combine(package.resolvedPath, "Tests", "Editor", "Fixtures", "envelope", "world-export.json");
                Assert.IsTrue(File.Exists(path), path);
                return path;
            }
        }

        private static string Envelope(string elements, string world = @"{""id"":""w1"",""name"":""W""}")
            => @"{""format"":""onlyworlds-world-export"",""world"":" + world + @",""elements"":" + elements + "}";

        // -- The file ---------------------------------------------------------

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            using var sha = SHA256.Create();
            var hash = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(FixturePath))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored export is not dist.43's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_ReadsWhole()
        {
            var read = OWEnvelopeReader.Read(FixturePath);

            Assert.AreEqual(WorldId, read.WorldId);
            Assert.AreEqual("Fixture World", read.WorldName);
            Assert.AreEqual("onlyworlds-world-export", read.Format);
            Assert.AreEqual("00.33.00", read.SchemaVersion);
            Assert.AreEqual("2026-10-19T12:30:00+00:00", read.ExportedAt);
            Assert.AreEqual(5, read.Elements.Count);
            Assert.IsEmpty(read.Skipped);
            Assert.IsEmpty(read.LegacyElements);

            var byType = read.Elements.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(2, byType["character"]);
            Assert.AreEqual(2, byType["location"]);
            Assert.AreEqual(1, byType["trait"]);
            Assert.IsTrue(read.Elements.All(e => !e.TypeInferred));

            // Keel's download says where it came from and as whom (keel PR 133, folder spec §4.2), and the world keeps both.
            Assert.AreEqual("https://www.onlyworlds.com", (string)read.World["source"]["server"]);
            Assert.AreEqual(WorldId, (string)read.World["source"]["world"]);
            Assert.AreEqual("owner", (string)read.World["exported_as"]["kind"]);
            Assert.AreEqual(JTokenType.Null, read.World["exported_as"]["character"].Type);
        }

        [Test]
        public void ABody_IsKeptVerbatim_ExtensionsAndTimestampsIncluded()
        {
            var mara = OWEnvelopeReader.Read(FixturePath).Elements.Single(e => e.Id == Mara);

            Assert.AreEqual("#8b0000", (string)mara.Body["atlas_color"]);
            // Not re-dated by the parse: the timestamp's bytes are the file's.
            Assert.AreEqual("2026-10-19T12:05:00+00:00", (string)mara.Body["created_at"]);
            StringAssert.EndsWith("#elements/character/1", mara.Path);
        }

        [Test]
        public void AnExportIsNoSyncableFolder()
        {
            var read = OWEnvelopeReader.Read(FixturePath);

            Assert.IsFalse(read.IsSyncable, "the file has no api block, and no sync may attach to it");
            Assert.AreEqual(WorldId, read.ServerWorldId);
        }

        [Test]
        public void LoadsIntoACache_ReadOnly_AndDecodes()
        {
            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            try
            {
                var read = OWEnvelopeLoader.LoadInto(cache, FixturePath);

                Assert.AreEqual(read.Elements.Count, cache.Count);
                Assert.IsTrue(cache.ReadOnly);
                Assert.AreEqual(OWSourceKind.Folder, cache.Key.Source);
                Assert.AreEqual(WorldId, cache.Key.WorldId);

                // A single link, a multi-link, and a link between two of one type.
                Assert.AreEqual(Mill, cache.Get<OWCharacter>(Pip).Location);
                CollectionAssert.AreEqual(new[] { Brave }, cache.Get<OWCharacter>(Pip).Traits);
                Assert.AreEqual(Keep, cache.Get<OWLocation>(Mill).ParentLocation);
                Assert.AreEqual(Keep, cache.Get<OWCharacter>(Mara).Location);
            }
            finally
            {
                Object.DestroyImmediate(cache);
            }
        }

        // -- What degrades, and what refuses -----------------------------------

        [Test]
        public void AnElementWithNoId_IsSkippedAndSaidSo()
        {
            var read = OWEnvelopeReader.Parse(Envelope(@"{""character"":[{""name"":""Nobody""},{""id"":""c1"",""name"":""A""}]}"), "x.json");

            Assert.AreEqual(1, read.Elements.Count);
            Assert.AreEqual(1, read.Skipped.Count);
            Assert.AreEqual("x.json#elements/character/0", read.Skipped[0].Path);
            Assert.AreEqual("no id in body", read.Skipped[0].Reason);
        }

        [Test]
        public void TheBodysTypeBeatsItsGroup_AndAMissingOneComesFromIt()
        {
            var read = OWEnvelopeReader.Parse(Envelope(
                @"{""character"":[{""id"":""a"",""type"":""creature""},{""id"":""b""}]}"));

            Assert.AreEqual("creature", read.Elements[0].Type);
            Assert.IsFalse(read.Elements[0].TypeInferred);
            Assert.AreEqual("character", read.Elements[1].Type);
            Assert.IsTrue(read.Elements[1].TypeInferred);
        }

        [Test]
        public void NotAListOrNotAnObject_IsSkipped_NotFatal()
        {
            var read = OWEnvelopeReader.Parse(Envelope(@"{""character"":{""id"":""a""},""event"":[7,{""id"":""e1""}]}"));

            Assert.AreEqual(1, read.Elements.Count);
            Assert.AreEqual(new[] { "not a list", "not an object" }, read.Skipped.Select(s => s.Reason).ToArray());
        }

        [Test]
        public void AnEmptyWorld_HasNoElementsAndNoSkips()
        {
            var read = OWEnvelopeReader.Parse(Envelope("{}"));

            Assert.IsEmpty(read.Elements);
            Assert.IsEmpty(read.Skipped);
        }

        [Test]
        public void UnknownKeys_AreIgnored()
        {
            var json = @"{""format"":""onlyworlds-world-export"",""future"":{""x"":1},
                ""world"":{""id"":""w1"",""name"":""W"",""calendar"":{}},""elements"":{}}";

            Assert.AreEqual("w1", OWEnvelopeReader.Parse(json).WorldId);
        }

        [TestCase(@"{""world"":{""id"":""w1""},""elements"":{}}", "missing")]
        [TestCase(@"{""format"":""onlyworlds-world"",""world"":{""id"":""w1""}}", "onlyworlds-world")]
        [TestCase(@"{""format"":1,""world"":{""id"":""w1""}}", "missing")]
        public void AnotherFormat_IsRefused(string json, string named)
        {
            var ex = Assert.Throws<OWFolderFormatException>(() => OWEnvelopeReader.Parse(json, "f.json"));
            StringAssert.Contains($"'{named}'", ex.Message);
        }

        [TestCase(@"{""format"":""onlyworlds-world-export"",""elements"":{}}")]
        [TestCase(@"{""format"":""onlyworlds-world-export"",""world"":{""name"":""W""}}")]
        [TestCase(@"{""format"":""onlyworlds-world-export"",""world"":[]}")]
        public void AWorldWithNoId_IsRefused(string json)
        {
            Assert.Throws<OWFolderFormatException>(() => OWEnvelopeReader.Parse(json));
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[1,2]")]
        public void NotAJsonObject_IsRefused(string text)
        {
            Assert.Throws<OWFolderFormatException>(() => OWEnvelopeReader.Parse(text));
        }

        [Test]
        public void NoFile_IsFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(() => OWEnvelopeReader.Read(Path.Combine(Path.GetTempPath(), "no-such-export.json")));
        }
    }
}
