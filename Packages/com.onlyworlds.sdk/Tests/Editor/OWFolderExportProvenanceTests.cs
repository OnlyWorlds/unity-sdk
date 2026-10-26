using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The provenance lines an API export writes into <c>world.json</c> (folder format §4.2): <c>source</c> always,
    /// <c>exported_as</c> when <c>GET /me</c> says which slice the key reads (v0.3.9), <c>producer</c> always (v0.3.14).
    /// The package's <c>tests/test_export.py</c>, case for case, so the two exporters of one format agree.
    /// </summary>
    public class OWFolderExportProvenanceTests
    {
        private const string World = "{\"id\":\"w-1\",\"name\":\"Moor\"}";
        private static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 19, 21, 30, 0, 250, TimeSpan.Zero);

        private string _parent;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _parent = Path.Combine(Path.GetTempPath(), "ow-provenance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_parent);
            _folder = Path.Combine(_parent, "world");
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null && Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }

        private static OWFolderExportOptions Options => new OWFolderExportOptions { IncludeMedia = false, Clock = () => At };

        /// <summary>A world whose <c>/me</c> answers <paramref name="me"/> (null: the route's empty default).</summary>
        private static RouteTransport Moor(string me = null)
        {
            var t = new RouteTransport { World = World };
            if (me != null) t.Type("me", me);
            return t;
        }

        private async Task<JObject> Export(RouteTransport transport)
        {
            await OWFolderExport.ExportAsync(new OWClient(new OWClientConfig
            {
                ApiKey = "ow_r_test", Transport = transport, BaseUrl = "https://www.onlyworlds.com/api/v2",
            }), _folder, Options);
            // The SDK's own parse: JObject.Parse would turn source.at into a DateTime and lose its shape.
            return OWFolderWriter.Parse(File.ReadAllText(Path.Combine(_folder, "world.json")));
        }

        // -- source ------------------------------------------------------------------------------------

        [Test]
        public async Task WorldJson_SaysWhereItCameFrom_AfterTheWiresKeys()
        {
            var world = await Export(Moor());

            Assert.AreEqual("https://www.onlyworlds.com", (string)world["source"]["server"], "the API's origin, not its path");
            Assert.AreEqual("w-1", (string)world["source"]["world"]);
            Assert.AreEqual("2026-10-19T21:30:00.250Z", (string)world["source"]["at"], "toISOString's shape: UTC, milliseconds, Z");
            CollectionAssert.AreEqual(new[] { "id", "name", "source", "producer", "format_version" }, world.Properties().Select(p => p.Name));
        }

        [Test]
        public async Task TheClockIsReadBeforeTheWorld()
        {
            var read = 0;
            var transport = Moor();
            var options = new OWFolderExportOptions
            {
                IncludeMedia = false, Clock = () => { read = transport.Calls.Count; return At; },
            };
            await OWFolderExport.ExportAsync(new OWClient(new OWClientConfig { ApiKey = "ow_r_test", Transport = transport }), _folder, options);

            Assert.AreEqual(0, read, "source.at is when the export began");
        }

        // -- exported_as: the spec owner's table ---------------------------------------------------------

        [Test]
        public async Task AnOwnerKey_ExportsTheOwnersSlice()
        {
            var world = await Export(Moor("{\"kind\":\"owner\",\"role\":\"owner\",\"character\":\"c-own\"}"));

            Assert.AreEqual("{\"kind\":\"owner\",\"role\":\"owner\",\"character\":null}",
                world["exported_as"].ToString(Newtonsoft.Json.Formatting.None), "the whole world, even with an owner_character");
            CollectionAssert.AreEqual(new[] { "id", "name", "source", "exported_as", "producer", "format_version" },
                world.Properties().Select(p => p.Name));
        }

        [TestCase("contributor")]
        [TestCase("co_builder")]
        public async Task AMembersKey_ExportsTheRolesSlice_NoCharacter(string role)
        {
            var world = await Export(Moor("{\"kind\":\"person\",\"role\":\"" + role + "\",\"character\":\"c-9\"}"));

            Assert.AreEqual("{\"kind\":\"member\",\"role\":\"" + role + "\",\"character\":null}",
                world["exported_as"].ToString(Newtonsoft.Json.Formatting.None));
        }

        [Test]
        public async Task AGuestsKey_ExportsItsCharactersSlice_TheNameReadFromTheCharacter()
        {
            var transport = Moor("{\"kind\":\"player\",\"role\":\"guest\",\"character\":\"c-pip\"}")
                .Type("character/c-pip", "{\"id\":\"c-pip\",\"type\":\"character\",\"name\":\"Pip\"}");
            var world = await Export(transport);

            Assert.AreEqual("{\"kind\":\"character\",\"role\":\"guest\",\"character\":{\"id\":\"c-pip\",\"name\":\"Pip\"}}",
                world["exported_as"].ToString(Newtonsoft.Json.Formatting.None));
        }

        [Test]
        public async Task NoMe_WritesNoExportedAs_AndTheExportStands()
        {
            var transport = Moor();
            transport.Refusals["me"] = (404, "{\"error\":{\"code\":\"not_found\",\"message\":\"no\"}}");
            var world = await Export(transport);

            Assert.IsFalse(world.ContainsKey("exported_as"));
            Assert.IsTrue(world.ContainsKey("source"));
        }

        [Test]
        public async Task AGuestWhoseCharacterCannotBeRead_WritesNoExportedAs()
        {
            var transport = Moor("{\"kind\":\"person\",\"role\":\"guest\",\"character\":\"c-x\"}");
            transport.Refusals["character/c-x"] = (403, "{\"error\":{\"code\":\"forbidden\",\"message\":\"no\"}}");
            var world = await Export(transport);

            Assert.IsFalse(world.ContainsKey("exported_as"));
        }

        // -- producer (v0.3.14): who made the folder, from what ------------------------------------------

        [Test]
        public async Task AFreshExport_WritesTheProducerLine_InTheSpecsKeyOrder()
        {
            var world = await Export(Moor("{\"kind\":\"owner\",\"role\":\"owner\",\"character\":null}"));

            Assert.AreEqual("{\"tool\":\"com.onlyworlds.sdk\",\"version\":\"" + OWFolderExport.ProducerVersion + "\",\"input\":\"server\"}",
                world["producer"].ToString(Newtonsoft.Json.Formatting.None));
            Assert.IsTrue(JToken.DeepEquals(OWFolderExport.ExportProducer, world["producer"]));
        }

        [Test]
        public async Task NoMe_StillWritesTheProducerLine()
        {
            // The slice can be unknown; who made the folder never is.
            var transport = Moor();
            transport.Refusals["me"] = (404, "{\"error\":{\"code\":\"not_found\",\"message\":\"no\"}}");
            var world = await Export(transport);

            Assert.IsTrue(JToken.DeepEquals(OWFolderExport.ExportProducer, world["producer"]));
        }

        [Test]
        public async Task AnExportIsABirth_SoItsLinesAreItsOwn_WhereTheBodyHadThem()
        {
            // A GET /world body carrying another folder's lines (keel keeps them at a folder upload): they name the
            // maker of THAT folder, so this folder's own are written, as for source. The package's vector.
            var transport = new RouteTransport
            {
                World = "{\"id\":\"w-1\",\"name\":\"Moor\","
                        + "\"source\":{\"server\":null,\"world\":\"w0\",\"at\":\"2026-10-01T00:00:00.000Z\"},"
                        + "\"producer\":{\"tool\":\"azgaar-converter\",\"version\":\"0.4.0\",\"input\":\"azgaar-map\"}}",
            };
            var world = await Export(transport);

            Assert.IsTrue(JToken.DeepEquals(OWFolderExport.ExportProducer, world["producer"]));
            Assert.AreEqual("w-1", (string)world["source"]["world"]);
            CollectionAssert.AreEqual(new[] { "id", "name", "source", "producer", "format_version" },
                world.Properties().Select(p => p.Name), "replaced where they stood, as the package's dict assignment does");
        }

        // -- origin (§4.2, v0.3.16) ----------------------------------------------------------------------

        [Test]
        public async Task AnExport_CarriesTheServersOrigin_BesideItsOwnLines()
        {
            // The world's first import, as the server keeps it, beside this export's own fresh lines. The package's vector.
            const string first = "{\"source\":null,\"producer\":{\"tool\":\"lantern\",\"version\":\"0.9.0\",\"input\":\"bare\"}}";
            var world = await Export(new RouteTransport { World = "{\"id\":\"w-1\",\"name\":\"W\",\"public_read\":false,\"origin\":" + first + "}" });

            Assert.AreEqual(first, world["origin"].ToString(Newtonsoft.Json.Formatting.None), "as served");
            Assert.AreEqual("w-1", (string)world["source"]["world"], "this export's own line, fresh");
            Assert.IsTrue(JToken.DeepEquals(OWFolderExport.ExportProducer, world["producer"]));
            CollectionAssert.AreEqual(new[] { "id", "name", "public_read", "origin", "source", "producer", "format_version" },
                world.Properties().Select(p => p.Name));
        }

        [Test]
        public async Task AnExport_OfAWorldServingNoOrigin_WritesNone()
        {
            // None served, none written: the export's own source and producer are never turned into one.
            var world = await Export(Moor("{\"kind\":\"owner\",\"role\":\"owner\",\"character\":null}"));

            Assert.IsFalse(world.ContainsKey("origin"));
        }

        [Test]
        public void TheProducersVersion_IsThePackagesVersion()
        {
            var package = PackageJson();
            if (package == null) Assert.Ignore("package.json was not found: the version was not compared.");

            var json = JObject.Parse(File.ReadAllText(package));
            Assert.AreEqual((string)json["version"], OWFolderExport.ProducerVersion,
                "OWFolderExport.ProducerVersion must move with package.json's version: a release changes both");
            Assert.AreEqual((string)json["name"], OWFolderExport.ProducerTool, "the tool is the name the package publishes under");
        }

        /// <summary>The package's own <c>package.json</c>: through the package manager, else the working directory's package.</summary>
        private static string PackageJson()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWFolderExportProvenanceTests).Assembly);
                if (info != null) return Path.Combine(info.resolvedPath, "package.json");
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk", "package.json");
            return File.Exists(local) ? local : null;
        }

        [Test]
        public void TheMapping_NamesOnlyWhatItKnows()
        {
            string Map(string me, string name = "Pip") =>
                OWFolderExport.ExportedAs(me == null ? null : JObject.Parse(me), _ => name)?.ToString(Newtonsoft.Json.Formatting.None);

            Assert.IsNull(Map(null), "no /me");
            Assert.IsNull(Map("{}"), "an answer with no kind or role");
            Assert.IsNull(Map("{\"kind\":\"person\",\"role\":\"viewer\"}"), "a role the table does not name");
            Assert.IsNull(Map("{\"kind\":\"player\",\"role\":\"guest\",\"character\":null}"), "a guest with no Character");
            Assert.IsNull(Map("{\"kind\":\"player\",\"role\":\"guest\",\"character\":\"c1\"}", name: null), "a name that cannot be read");
            Assert.AreEqual("{\"kind\":\"character\",\"role\":\"guest\",\"character\":{\"id\":\"c1\",\"name\":\"\"}}",
                Map("{\"kind\":\"player\",\"role\":\"guest\",\"character\":\"c1\"}", name: ""), "an empty name is a name served");
            Assert.AreEqual("{\"kind\":\"owner\",\"role\":\"owner\",\"character\":null}",
                Map("{\"kind\":\"owner\",\"role\":\"guest\",\"character\":\"c1\"}"), "kind owner decides first");
        }
    }
}
