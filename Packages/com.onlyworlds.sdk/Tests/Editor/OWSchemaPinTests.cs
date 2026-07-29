using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The drift guard on the vendored schema sidecar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vendored copy with no guard rots silently -- it keeps looking authoritative while the
    /// source it was copied from moves on, and nothing anywhere says so. Three separate instances
    /// of that failure landed across the ecosystem in one week: a walker duplicated ten times and a
    /// year stale, a MANIFEST that skipped the one file consumers fetch to verify the others, and a
    /// platform test suite that had not RUN for four days behind a lint failure. The general form,
    /// as the Keeper filed it: anything that restates a verified fact is an unverified artifact
    /// until separately checked, and the more official it looks the further it drifts.
    /// </para>
    /// <para>
    /// This is the <c>schema:check</c> half of the two-check split -- it asserts the vendored copy
    /// still matches the pinned distribution. The <c>codegen:check</c> half (generated C# matches
    /// the schema it was emitted from) belongs with the emitter and does not exist yet.
    /// </para>
    /// <para>
    /// <b>If one of these fails, the fix is to re-vendor and re-pin deliberately</b> -- fetch the
    /// dist, verify the MANIFEST against its own recorded hash, copy the file, update
    /// <see cref="OWSchemaPin"/>. Editing an expected hash until the test goes green converts a
    /// guard into a rubber stamp.
    /// </para>
    /// </remarks>
    public class OWSchemaPinTests
    {
        private const string VendoredRelativePath =
            "Packages/com.onlyworlds.sdk/Runtime/Resources/ow-presentation.json";

        private static string Sha256OfFile(string path)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(File.ReadAllBytes(path));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string VendoredPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, VendoredRelativePath);

        [Test]
        public void VendoredSidecar_IsOnDisk_WhereThePinSaysItIs()
        {
            Assert.IsTrue(File.Exists(VendoredPath),
                $"The vendored sidecar is missing from {VendoredRelativePath}. Every other "
                + "assertion here is vacuous without it -- a guard that cannot find its subject "
                + "must fail loudly, not pass quietly.");
        }

        [Test]
        public void VendoredSidecar_MatchesThePinnedHash()
        {
            var actual = Sha256OfFile(VendoredPath);

            Assert.AreEqual(OWSchemaPin.PresentationSha256, actual,
                $"The vendored presentation sidecar no longer matches the hash pinned in "
                + $"OWSchemaPin ({OWSchemaPin.Tag}, MANIFEST {OWSchemaPin.ManifestSha256}). Either "
                + "the file was edited by hand -- it is generated, so it never should be -- or a "
                + "re-vendor landed without updating the pin. Do not adjust the expected value to "
                + "make this pass; re-vendor from the dist and re-pin.");
        }

        [Test]
        public void PinIsInternallyCoherent()
        {
            // A pin that half-describes itself is worse than none: it looks deliberate.
            StringAssert.Contains(OWSchemaPin.DistSerial.ToString(), OWSchemaPin.Tag,
                "The tag must name the same dist serial the pin records.");

            Assert.AreEqual(64, OWSchemaPin.ManifestSha256.Length, "A sha256 is 64 hex characters.");
            Assert.AreEqual(64, OWSchemaPin.PresentationSha256.Length);
            StringAssert.StartsWith("https://github.com/OnlyWorlds/", OWSchemaPin.Repository);
        }

        [Test]
        public void LoadedResource_IsTheSameFileTheGuardHashes()
        {
            // The guard hashes bytes on disk; the runtime reads through Resources.Load, which can
            // normalise text. If those two ever diverge the guard would be protecting a file the
            // SDK does not actually use -- a check pointed at the wrong subject, which is its own
            // failure mode. Compare on content, not on bytes, so line-ending policy stays free.
            var asset = Resources.Load<TextAsset>("ow-presentation");
            Assert.IsNotNull(asset, "The sidecar must be loadable from Resources at runtime.");

            var onDisk = File.ReadAllText(VendoredPath).Replace("\r\n", "\n").TrimEnd();
            var loaded = asset.text.Replace("\r\n", "\n").TrimEnd();

            Assert.AreEqual(onDisk, loaded,
                "The Resources copy and the hashed file must be the same document.");
        }
    }
}
