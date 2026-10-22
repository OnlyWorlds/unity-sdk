using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The <c>.owworld</c> conformance set (folder spec §4.7; schema-dist <c>fixtures/owworld/</c>, tag
    /// <c>v0.33.0-dist.49</c>): every zip in it read by <see cref="OWWorldArchive.Read"/>, which must accept or refuse
    /// it, for the cause the set names, as its <c>verdicts.json</c> says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set is generated, never hand-made: the dist's <c>generate.py</c> writes every zip and <c>verdicts.json</c>
    /// beside them. Each fixture carries one cause, names its test (OW-1..OW-22) and its rule. This class reads the
    /// <c>reader</c> verdict only: the <c>writer</c> one is checked by a reference checker against the files a writer
    /// made, not by a reader. The TypeScript SDK and the reference checker run the same set to the same verdicts.
    /// </para>
    /// <para>
    /// The zips and <c>verdicts.json</c> are vendored byte for byte in <c>Tests/Editor/Fixtures/owworld~/</c>: the
    /// trailing <c>~</c> keeps Unity from importing them, so the package's own <c>.owworld</c> importer never runs on
    /// a hostile file (a zip bomb, a symlink entry) and no <c>.meta</c> is written beside them. The vendored
    /// <c>verdicts.json</c> is pinned to the dist's MANIFEST by <see cref="VerdictsSha256"/>, and every zip to the
    /// sha256 <c>verdicts.json</c> lists for it. The environment variable <c>OW_OWWORLD_FIXTURES</c>, a folder holding
    /// a <c>verdicts.json</c>, runs another copy of the set instead (a newer draft); that copy is checked against its
    /// own <c>verdicts.json</c>, not the pin. When the dist moves the set, re-vendor both and update the pin.
    /// </para>
    /// <para>
    /// A refusal is an <see cref="OWFolderFormatException"/> whose <c>Reason</c> must be the fixture's cause; any other
    /// exception fails the case, its type named in the message, since this reader names a cause on every refusal.
    /// A fixture with <c>strips</c> (OW-10's <c>api</c>) must also come back without that
    /// key in <c>World</c>. Read with the default budget: the set's in-set fixtures read the same at it and at the floor.
    /// </para>
    /// </remarks>
    public class OWWorldArchiveConformanceTests
    {
        private const string FolderName = "owworld~";

        /// <summary><c>fixtures/owworld/verdicts.json</c>'s sha256 as dist.49's MANIFEST.json records it.</summary>
        private const string VerdictsSha256 = "629846ddf30b8e40e7b0f2bda0b8167b8bfd5a08fb81eaa8f696023b5c15edd5";

        private static string _dir;
        private static bool _vendored;
        private static JObject _verdicts;

        /// <summary>The fixture folder, or null when there is none to read.</summary>
        private static string FixtureDir
        {
            get
            {
                if (_dir != null) return _dir.Length == 0 ? null : _dir;
                _dir = string.Empty;

                var env = Environment.GetEnvironmentVariable("OW_OWWORLD_FIXTURES");
                if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "verdicts.json"))) return _dir = env;

                var root = PackageRoot();
                var own = root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", FolderName);
                if (own != null && File.Exists(Path.Combine(own, "verdicts.json")))
                {
                    _dir = own;
                    _vendored = true;
                }

                return _dir.Length == 0 ? null : _dir;
            }
        }

        /// <summary>
        /// The package's own resolved path; outside the Editor (no package manager to ask), the working directory's
        /// <c>Packages/com.onlyworlds.sdk</c>, as <c>OWWireContractTests</c> finds its fixture.
        /// </summary>
        private static string PackageRoot()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(OWWorldArchiveConformanceTests).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }

        private static JObject Verdicts
        {
            get
            {
                if (_verdicts != null) return _verdicts;
                var dir = FixtureDir;
                return dir == null ? null : _verdicts = JObject.Parse(File.ReadAllText(Path.Combine(dir, "verdicts.json")));
            }
        }

        private static string Sha256Of(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>One case per generated fixture, named after its file.</summary>
        public static IEnumerable<TestCaseData> Fixtures()
        {
            var verdicts = Verdicts;
            if (verdicts == null)
            {
                yield return new TestCaseData(string.Empty).SetName("OWWorldArchiveConformance_FixturesNotFound");
                yield break;
            }

            foreach (var fixture in verdicts["fixtures"].Cast<JObject>())
            {
                if ((string)fixture["status"] == "not_generated") continue;
                var file = (string)fixture["file"];
                yield return new TestCaseData(file).SetName("Conformance_" + file.Replace('.', '_').Replace('-', '_'));
            }
        }

        [Test]
        public void TheSet_IsWhatItsVerdictsList()
        {
            var verdicts = Verdicts;
            if (verdicts == null)
            {
                Assert.Ignore("The .owworld fixture set was not found. Nothing ran -- treat it as unmeasured, not as passing.");
            }

            if (_vendored)
            {
                Assert.AreEqual(VerdictsSha256, Sha256Of(Path.Combine(FixtureDir, "verdicts.json")),
                    "the vendored verdicts.json is not dist.49's: re-vendor the set from schema-dist byte for byte");
            }
            else
            {
                TestContext.WriteLine($"OW_OWWORLD_FIXTURES: the set at {FixtureDir}, checked against its own verdicts.json");
            }

            var listed = verdicts["fixtures"].Cast<JObject>().Where(f => (string)f["status"] != "not_generated").ToList();
            Assert.Greater(listed.Count, 0, "a set with no fixtures checks nothing");
            foreach (var fixture in listed)
            {
                var path = Path.Combine(FixtureDir, (string)fixture["file"]);
                Assert.IsTrue(File.Exists(path), $"{fixture["file"]} is listed but not in the set");
                Assert.AreEqual((long)fixture["bytes"], new FileInfo(path).Length,
                    $"{fixture["file"]} is not the generated file (regenerate the set, or re-copy it byte for byte)");
                Assert.AreEqual((string)fixture["sha256"], Sha256Of(path),
                    $"{fixture["file"]} is not the generated file (regenerate the set, or re-copy it byte for byte)");
            }
        }

        /// <summary>
        /// The writer's half of the private case (folder spec §4.5 rule 11, §4.7 OW-8): the asked side's world, packed
        /// again by <see cref="OWWorldArchive.Pack(string, string, bool, bool)"/>, gives each side's files byte for
        /// byte, the marker aside.
        /// </summary>
        [TestCase(false, "pass-OW-8-private-left-out.owworld")]
        [TestCase(true, "pass-OW-8-private-asked.owworld")]
        public void Pack_OnThePrivateCase_GivesEachSideOfTheSet(bool includePrivateRecords, string side)
        {
            const string source = "pass-OW-8-private-asked.owworld";
            if (Verdicts == null || !File.Exists(Path.Combine(FixtureDir, source)))
            {
                Assert.Ignore("This set has no private case (it came in dist.49). Nothing ran.");
            }

            var temp = Path.Combine(Path.GetTempPath(), "ow-conformance-private-" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(Path.Combine(FixtureDir, source), temp);
                var packed = Path.Combine(temp, "packed.owworld");
                OWWorldArchive.Pack(Path.Combine(temp, "abbey"), packed, false, includePrivateRecords);

                var want = FilesOf(Path.Combine(FixtureDir, side));
                var got = FilesOf(packed);
                CollectionAssert.AreEqual(want.Keys.ToList(), got.Keys.ToList(), $"Pack's entries are not {side}'s");
                foreach (var name in want.Keys)
                {
                    CollectionAssert.AreEqual(want[name], got[name], $"{name} differs from {side}'s bytes");
                }
            }
            finally
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }
        }

        /// <summary>An archive's files by name, in its order, inflated; the marker left out (Pack always writes it).</summary>
        private static Dictionary<string, byte[]> FilesOf(string archive)
        {
            var files = new Dictionary<string, byte[]>();
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries.Where(e => e.FullName != "mimetype"))
            {
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                files[entry.FullName] = copy.ToArray();
            }

            return files;
        }

        [TestCaseSource(nameof(Fixtures))]
        public void ReadsAsTheSetSays(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                Assert.Ignore("The .owworld fixture set was not found. Nothing ran -- treat it as unmeasured, not as passing.");
            }

            var fixture = Verdicts["fixtures"].Cast<JObject>().First(f => (string)f["file"] == file);
            var expected = (string)fixture["reader"];

            var strips = (string)fixture["strips"];
            string got;
            string said;
            try
            {
                var read = OWWorldArchive.Read(Path.Combine(FixtureDir, file));
                got = "accept";
                said = $"world {read.WorldId}, {read.Elements.Count} elements, {read.Skipped.Count} skipped";
                if (!string.IsNullOrEmpty(strips) && read.World?[strips] != null)
                {
                    // Section 4.7 (OW-10): a reader strips the key and never returns it; one that returns it fails.
                    got = "returned " + strips;
                    said += $"; world.json came back with its '{strips}' block";
                }
            }
            catch (OWFolderFormatException e)
            {
                got = "refuse";
                said = $"[{e.Reason}] {e.Message}";
                // The cause: a refusal for another cause than the fixture's fails the case.
                var wanted = (string)fixture["reason"];
                if (expected == "refuse" && e.Reason != wanted) got = $"refuse for {e.Reason ?? "no cause"}, not {wanted}";
            }
            catch (Exception e)
            {
                // Every refusal from Read names its cause, so a crash is a failure even where the set says refuse.
                got = $"crashed with {e.GetType().Name}";
                said = $"{e.GetType().Name} (not an OWFolderFormatException): {e.Message}";
            }

            TestContext.WriteLine($"{file} | {fixture["test"]} | expected {expected} | got {got} | {said}");

            var open = fixture["open"] == null ? string.Empty : $" OPEN, the verdict follows the lean: {fixture["lean"]}";
            Assert.AreEqual(expected, got,
                $"{file} ({fixture["test"]}): {fixture["rule"]}{open}\nUnity: {said}");
        }
    }
}
