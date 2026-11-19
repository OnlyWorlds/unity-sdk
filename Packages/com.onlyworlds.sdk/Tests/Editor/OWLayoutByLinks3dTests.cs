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
    /// The <c>by-links-3d</c> layout on the pinned schema distribution's fixture,
    /// <c>profiles/layout-by-links-3d.fixture.json</c> (the default layouts page, <c>profiles/layout.md</c>, serial 137),
    /// and its directions table, <c>profiles/layout-by-links-3d.directions.json</c>, both vendored byte for byte in
    /// <c>Tests/Editor/Fixtures/layout/</c>. Read through <see cref="OWJson"/>, which reads every double exactly. The
    /// fixture's <c>unplaced</c> suite is read whole by
    /// <see cref="OWLayout.UnplacedByLinks3d(JObject, System.Collections.IEnumerable)"/>, the ids compared exactly and in
    /// order.
    /// </summary>
    public class OWLayoutByLinks3dTests
    {
        /// <summary><c>profiles/layout-by-links-3d.fixture.json</c> in schema-dist <c>v0.34.1-dist.139</c>.</summary>
        private const string FixtureSha256 = "acd6da7252493485cafc24356a84c53656d078872a28c695c0f7554009b6fb2a";

        /// <summary><c>profiles/layout-by-links-3d.directions.json</c> in schema-dist <c>v0.34.1-dist.139</c>, the fixture's own <c>table_sha256</c>.</summary>
        private const string DirectionsSha256 = "37dc2bbb4a1769e4ddafc2d0a27c9889229c3ef483e8f7cd6c9ba66a0d1afb87";

        private static JObject _fixture;

        private static string Vendored(string file)
        {
            var root = TestPackage.Root();
            return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "layout", file);
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = Vendored("layout-by-links-3d.fixture.json");
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2")));
        }

        [Test]
        public void TheFixtureAndTable_AreThePinnedDistsBytes()
        {
            var fixture = Vendored("layout-by-links-3d.fixture.json");
            var table = Vendored("layout-by-links-3d.directions.json");
            Assert.That(fixture != null && File.Exists(fixture) && File.Exists(table), Is.True, "the fixture and the table are vendored");
            Assert.That(Sha256(fixture), Is.EqualTo(FixtureSha256));
            Assert.That(Sha256(table), Is.EqualTo(DirectionsSha256));
            Assert.That((string)Fixture["table_sha256"], Is.EqualTo(DirectionsSha256));
            Assert.That((string)Fixture["method"], Is.EqualTo("by-links-3d"));
            Assert.That(Fixture["cases"].Count(), Is.EqualTo(116));
            Assert.That(Fixture["unplaced"].Count(), Is.EqualTo(9));
        }

        /// <summary>One test per <c>unplaced</c> case, named after it.</summary>
        public static IEnumerable<TestCaseData> UnplacedCases()
        {
            var fx = Fixture;
            if (fx == null)
            {
                yield return new TestCaseData(string.Empty).SetName("UnplacedByLinks3d_FixtureNotFound");
                yield break;
            }

            foreach (var c in fx["unplaced"].Cast<JObject>())
                yield return new TestCaseData((string)c["name"]).SetName("UnplacedByLinks3d_" + ((string)c["name"]).Replace('-', '_'));
        }

        [TestCaseSource(nameof(UnplacedCases))]
        public void UnplacedByLinks3d_GivesTheFixturesIds_InIdOrder(string name)
        {
            Assert.That(Fixture, Is.Not.Null, "the fixture is vendored");
            var c = (JObject)Fixture["unplaced"].First(x => (string)x["name"] == name);
            var got = OWLayout.UnplacedByLinks3d((JObject)c["graph"].DeepClone(), (JArray)c["placed"].DeepClone());
            Assert.That(got, Is.EqualTo(c["want"].Select(t => (string)t).ToArray()));
        }

        [Test]
        public void UnplacedByLinks3d_RefusesTwoNodesWithOneId()
        {
            var g = OWJson.ParseObject("{\"nodes\":[{\"id\":\"a\",\"type\":\"character\"},{\"id\":\"a\",\"type\":\"location\"}],\"edges\":[]}");
            Assert.Throws<ArgumentException>(() => OWLayout.UnplacedByLinks3d(g, new string[0]));
        }
    }
}
