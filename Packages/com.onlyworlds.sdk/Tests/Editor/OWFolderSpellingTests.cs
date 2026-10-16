using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The spec's "accept both spellings" (reading rules): <c>map</c>/<c>map_id</c>, <c>zone</c>/<c>zone_id</c>,
    /// <c>time_current</c>/<c>time_range_current</c>, and a linked folder's server world from <c>api.world_id</c>.
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
    }
}
