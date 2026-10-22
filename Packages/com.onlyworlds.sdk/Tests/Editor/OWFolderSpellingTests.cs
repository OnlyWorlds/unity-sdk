using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The spec's "accept both spellings" (reading rules): <c>map</c>/<c>map_id</c>, <c>zone</c>/<c>zone_id</c>,
    /// <c>time_current</c>/<c>time_range_current</c>, and a linked folder's server world from <c>api.world_id</c>; and
    /// what a reader never takes for an element, a tool's dot-prefixed file or folder (§4).
    /// </summary>
    /// <remarks>
    /// Atlas writes <c>map_id</c> and <c>zone_id</c> on pins and markers (the Python package's push.py says so), and
    /// before this reading a pin loaded into the cache from an Atlas folder had no map. The files themselves are never
    /// touched: the bytes on disk are compared before and after.
    /// </remarks>
    public class OWFolderSpellingTests
    {
        private const string MapId = "0195d1a0-0000-7000-8000-000000000001";
        private const string ZoneId = "0195d1a0-0000-7000-8000-000000000002";
        private const string PinId = "0195d1a0-0000-7000-8000-000000000003";
        private const string MarkerId = "0195d1a0-0000-7000-8000-000000000004";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ow-spelling-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string json)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private void AtlasSpelledWorld(string worldJson)
        {
            Write("world.json", worldJson);
            Write("elements/map/m.json", $"{{\"id\": \"{MapId}\", \"name\": \"The map\"}}");
            Write("elements/zone/z.json", $"{{\"id\": \"{ZoneId}\", \"name\": \"The road\"}}");
            Write("elements/pin/p.json", $"{{\"id\": \"{PinId}\", \"name\": \"A town\", \"map_id\": \"{MapId}\", \"x\": 3, \"y\": 4}}");
            Write("elements/marker/k.json",
                $"{{\"id\": \"{MarkerId}\", \"name\": \"Road 0\", \"map_id\": \"{MapId}\", \"zone_id\": \"{ZoneId}\", \"order\": 0}}");
        }

        [Test]
        public void An_Atlas_spelled_pin_and_marker_reach_the_cache_with_their_links()
        {
            AtlasSpelledWorld("{\"id\": \"w1\", \"name\": \"W\"}");
            var pinBefore = File.ReadAllBytes(Path.Combine(_root, "elements/pin/p.json"));

            var cache = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();
            try
            {
                OWFolderLoader.LoadInto(cache, _root);

                Assert.AreEqual(MapId, cache.Get<OWPin>(PinId).Map, "a pin's map_id is its map");
                Assert.AreEqual(MapId, cache.Get<OWMarker>(MarkerId).Map, "a marker's map_id is its map");
                Assert.AreEqual(ZoneId, cache.Get<OWMarker>(MarkerId).Zone, "a marker's zone_id is its zone");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cache);
            }

            CollectionAssert.AreEqual(pinBefore, File.ReadAllBytes(Path.Combine(_root, "elements/pin/p.json")),
                "opening is not editing: the file keeps its spelling");
        }

        [Test]
        public void A_legacy_spatial_file_never_replaces_an_id_elements_gave()
        {
            AtlasSpelledWorld("{\"id\": \"w1\", \"name\": \"W\"}");
            Write("spatial/zone/z-old.json", $"{{\"id\": \"{ZoneId}\", \"name\": \"The old road\"}}");
            Write("spatial/pin/only-here.json", $"{{\"id\": \"0195d1a0-0000-7000-8000-0000000000ff\", \"map_id\": \"{MapId}\"}}");

            var cache = UnityEngine.ScriptableObject.CreateInstance<OWWorldCache>();
            try
            {
                OWFolderLoader.LoadInto(cache, _root);
                Assert.AreEqual("The road", cache.Get<OWZone>(ZoneId).Name, "elements/ is the copy kept");
                Assert.AreEqual(MapId, cache.Get<OWPin>("0195d1a0-0000-7000-8000-0000000000ff").Map,
                    "a pin only under spatial/ still loads, its map_id read");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cache);
            }
        }

        [Test]
        public void The_reader_serves_elements_and_logs_the_spatial_duplicate()
        {
            AtlasSpelledWorld("{\"id\": \"w1\", \"name\": \"W\"}");
            Write("spatial/zone/z-old.json", $"{{\"id\": \"{ZoneId}\", \"name\": \"The old road\"}}");

            var read = OWFolderReader.Read(_root);

            Assert.IsFalse(read.LegacyElements.Exists(e => e.Id == ZoneId), "the spatial/ copy is not served");
            Assert.IsTrue(read.Skipped.Exists(s => s.Path.EndsWith("z-old.json") && s.Reason.Contains("elements/")),
                "and it is logged");
            Assert.IsTrue(File.Exists(Path.Combine(_root, "spatial/zone/z-old.json")), "and never deleted");
        }

        [Test]
        public void A_writer_writes_a_spatial_element_where_it_lives_and_a_new_one_to_elements()
        {
            AtlasSpelledWorld("{\"id\": \"w1\", \"name\": \"W\"}");
            var pinFile = Path.Combine(_root, "spatial/pin/p.json");
            Directory.CreateDirectory(Path.GetDirectoryName(pinFile));
            File.Move(Path.Combine(_root, "elements/pin/p.json"), pinFile);

            var written = OWFolderWriter.WriteElement(_root, JObject.Parse(
                $"{{\"id\": \"{PinId}\", \"type\": \"pin\", \"name\": \"A town\", \"map_id\": \"{MapId}\", \"x\": 5, \"y\": 6}}"));

            StringAssert.Contains(Path.Combine("spatial", "pin"), written, "an edit stays in the layout it was read from");
            Assert.IsEmpty(Directory.Exists(Path.Combine(_root, "elements/pin"))
                ? Directory.GetFiles(Path.Combine(_root, "elements/pin")) : new string[0], "no second copy in elements/");

            var fresh = OWFolderWriter.WriteElement(_root, JObject.Parse(
                "{\"id\": \"0195d1a0-0000-7000-8000-0000000000ee\", \"type\": \"pin\", \"name\": \"New\"}"));
            StringAssert.Contains(Path.Combine("elements", "pin"), fresh, "a new element goes to elements/");
        }

        [Test]
        public void The_read_result_keeps_the_body_as_written()
        {
            AtlasSpelledWorld("{\"id\": \"w1\", \"name\": \"W\"}");
            var read = OWFolderReader.Read(_root);
            foreach (var e in read.Elements)
            {
                if (e.Id != PinId) continue;
                Assert.IsTrue(e.Body.ContainsKey("map_id"));
                Assert.IsFalse(e.Body.ContainsKey("map"));
                return;
            }
            Assert.Fail("the pin was not read");
        }

        [Test]
        public void The_schema_name_takes_the_old_ones_place_and_wins_when_both_are_there()
        {
            var body = JObject.Parse("{\"id\": \"p\", \"map_id\": \"a\", \"x\": 1}");
            var view = OWFolderReader.WithSchemaLinkNames(body);
            CollectionAssert.AreEqual(new[] { "id", "map", "x" }, new System.Collections.Generic.List<string>(
                System.Linq.Enumerable.Select(view.Properties(), p => p.Name)), "in its own place");
            Assert.AreEqual("a", view["map"].ToString());
            Assert.IsTrue(body.ContainsKey("map_id"), "the original is never changed");

            var both = JObject.Parse("{\"id\": \"p\", \"map\": \"new\", \"map_id\": \"old\"}");
            Assert.AreSame(both, OWFolderReader.WithSchemaLinkNames(both), "the schema's name wins, nothing to change");

            var plain = JObject.Parse("{\"id\": \"p\", \"map\": \"a\"}");
            Assert.AreSame(plain, OWFolderReader.WithSchemaLinkNames(plain));
            Assert.IsNull(OWFolderReader.WithSchemaLinkNames(null));
        }

        [Test]
        public void The_world_time_reads_either_spelling_and_time_current_wins()
        {
            AtlasSpelledWorld("{\"id\": \"w1\", \"name\": \"W\", \"time_range_current\": 1850}");
            Assert.AreEqual(1850, (int)OWFolderReader.Read(_root).TimeCurrent);

            Write("world.json", "{\"id\": \"w1\", \"name\": \"W\", \"time_current\": 1849, \"time_range_current\": 1850}");
            Assert.AreEqual(1849, (int)OWFolderReader.Read(_root).TimeCurrent);

            Write("world.json", "{\"id\": \"w1\", \"name\": \"W\", \"time_current\": null}");
            var t = OWFolderReader.Read(_root).TimeCurrent;
            Assert.IsNotNull(t, "an explicit null is a null token, not absent");
            Assert.AreEqual(JTokenType.Null, t.Type);

            Write("world.json", "{\"id\": \"w1\", \"name\": \"W\"}");
            Assert.IsNull(OWFolderReader.Read(_root).TimeCurrent);
        }

        [Test]
        public void A_linked_folders_server_world_is_api_world_id_then_id()
        {
            AtlasSpelledWorld("{\"id\": \"local\", \"name\": \"W\", \"api\": {\"world_id\": \"server\"}}");
            var read = OWFolderReader.Read(_root);
            Assert.AreEqual("server", read.ServerWorldId);
            Assert.AreEqual("local", read.WorldId, "the folder's own id still keys the cache");

            Write("world.json", "{\"id\": \"local\", \"name\": \"W\", \"api\": null}");
            Assert.AreEqual("local", OWFolderReader.Read(_root).ServerWorldId);

            Write("world.json", "{\"id\": \"local\", \"name\": \"W\", \"api\": {\"world_id\": \"\"}}");
            Assert.AreEqual("local", OWFolderReader.Read(_root).ServerWorldId);
        }

        // -- A tool's litter (§4: dot-prefixed files and folders are never elements) ---------------------

        private const string LitterId = "11111111-2222-4333-8444-5555555555c1";

        /// <summary>
        /// The Python package's vector (<c>tests/test_reader.py</c>, 0.5.1): a dot-prefixed file in a type directory
        /// is a tool's litter, not an element, even with a valid body and the id of a real file; skipped and listed.
        /// </summary>
        [Test]
        public void A_dot_prefixed_file_in_a_type_directory_is_skipped_and_listed()
        {
            Write("world.json", "{\"id\": \"w\", \"name\": \"W\"}");
            Write("elements/character/.tmp-a.json", $"{{\"id\": \"{LitterId}\", \"name\": \"litter\"}}");
            Write("elements/character/real--5555c1.json", $"{{\"id\": \"{LitterId}\", \"name\": \"real\"}}");

            var read = OWFolderReader.Read(_root);

            Assert.AreEqual(1, read.Elements.Count, "only the real file is an element");
            Assert.AreEqual("real", (string)read.Elements[0].Body["name"]);
            CollectionAssert.AreEqual(new[] { ".tmp-a.json" }, read.Skipped.ConvertAll(s => Path.GetFileName(s.Path)));
        }

        /// <summary>A dot-prefixed folder under <c>elements/</c> is a tool's (<c>.atlas/</c>): never read, never listed.</summary>
        [Test]
        public void A_dot_prefixed_folder_under_elements_is_never_read()
        {
            Write("world.json", "{\"id\": \"w\", \"name\": \"W\"}");
            Write("elements/.atlas/state.json", $"{{\"id\": \"{LitterId}\", \"type\": \"character\", \"name\": \"state\"}}");

            var read = OWFolderReader.Read(_root);

            Assert.AreEqual(0, read.Elements.Count);
            Assert.AreEqual(0, read.Skipped.Count, "a tool's folder is not the world's, so nothing in it is reported");
        }

        /// <summary>The in-memory form (an archive's read) by the same rules.</summary>
        [Test]
        public void The_in_memory_read_skips_the_same_litter()
        {
            var read = OWFolderReader.ReadEntries(new System.Collections.Generic.Dictionary<string, string>
            {
                ["world.json"] = "{\"id\": \"w\", \"name\": \"W\"}",
                ["elements/character/.tmp-a.json"] = $"{{\"id\": \"{LitterId}\", \"name\": \"litter\"}}",
                ["elements/character/real--5555c1.json"] = $"{{\"id\": \"{LitterId}\", \"name\": \"real\"}}",
                ["elements/.atlas/state.json"] = $"{{\"id\": \"{LitterId}\", \"type\": \"character\", \"name\": \"state\"}}",
            });

            Assert.AreEqual(1, read.Elements.Count, "only the real file is an element");
            Assert.AreEqual("real", (string)read.Elements[0].Body["name"]);
            CollectionAssert.AreEqual(new[] { "elements/character/.tmp-a.json" }, read.Skipped.ConvertAll(s => s.Path));
        }
    }
}
