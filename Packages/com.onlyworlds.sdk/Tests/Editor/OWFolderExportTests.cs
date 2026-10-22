using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Answers by route: <c>/world/</c> and <c>/&lt;type&gt;/</c>, each a queue of pages, any other type an empty one.
    /// </summary>
    internal class RouteTransport : IOWTransport
    {
        public readonly List<OWRequest> Calls = new List<OWRequest>();
        public string World = "{}";
        public readonly Dictionary<string, Queue<string>> Pages = new Dictionary<string, Queue<string>>();

        /// <summary>A route that answers with this status and body instead.</summary>
        public readonly Dictionary<string, (int Status, string Body)> Refusals = new Dictionary<string, (int, string)>();

        public RouteTransport Type(string type, params string[] pages)
        {
            Pages[type] = new Queue<string>(pages);
            return this;
        }

        public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            var path = new Uri(request.Url).AbsolutePath;
            var route = path.Substring(path.IndexOf("/v2/", StringComparison.Ordinal) + 4).Trim('/');

            if (Refusals.TryGetValue(route, out var refusal))
            {
                return Task.FromResult(new OWResponse { Status = refusal.Status, Body = refusal.Body, Headers = new Dictionary<string, string>() });
            }

            var body = route == "world"
                ? World
                : Pages.TryGetValue(route, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : "{\"data\":[],\"has_more\":false,\"next_cursor\":null}";
            return Task.FromResult(new OWResponse { Status = 200, Body = body, Headers = new Dictionary<string, string>() });
        }
    }

    /// <summary>
    /// The export, against the onlyworlds Python package it ports: the bytes and names below are what the
    /// package's <c>write_folder</c> wrote for the same wire bodies (run 2026-10-16, onlyworlds folder.py
    /// v0.3.7; the stamp moved to v0.3.8 with the package's 0.5.0, its <c>world_for_disk</c> re-run 2026-10-17 on the
    /// three world cases below: only <c>format_version</c> changed; v0.3.9 with the package's 0.7.0, the three cases and
    /// Testmoor's <c>world.json</c> with <c>source</c> re-run 2026-10-19 through its <c>world_for_disk</c> and <c>write_folder</c>;
    /// v0.3.12 with the package's 0.10.4, the three cases re-run 2026-10-21: only <c>format_version</c> changed; v0.3.13
    /// with the package's 0.15.0, the three cases re-run 2026-10-22: only <c>format_version</c> changed; v0.3.14 with the
    /// package's 0.16.0, the four cases re-run 2026-10-22: only <c>format_version</c> changed, and Testmoor's
    /// <c>world.json</c> gains <c>producer</c> where the package's <c>export_world</c> puts it, this package's tool and
    /// version in it), not
    /// what this SDK happens to write. Its known-of stubs (v0.3.10) are <see cref="OWKnownOfTests"/>.
    /// </summary>
    public class OWFolderExportTests
    {
        private const string Key = "ow_w_s3cr3tkeyvalue";
        private const string Pin = "4242";

        private string _parent;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _parent = Path.Combine(Path.GetTempPath(), "ow-folder-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_parent);
            _folder = Path.Combine(_parent, "world");
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null && Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
        }

        private static OWClient Client(IOWTransport transport) => new OWClient(new OWClientConfig
        {
            ApiKey = Key,
            ApiPin = Pin,
            Transport = transport,
            BaseUrl = "https://example.test/api/v2",
        });

        private static string Page(params string[] bodies) => Page(false, null, bodies);

        private static string Page(bool hasMore, string next, params string[] bodies)
            => "{\"data\":[" + string.Join(",", bodies) + "],\"has_more\":" + (hasMore ? "true" : "false")
               + ",\"next_cursor\":" + (next == null ? "null" : "\"" + next + "\"") + "}";

        private static string Text(string path) => File.ReadAllText(path, new UTF8Encoding(false));

        private static string[] Keys(JObject o) => o.Properties().Select(p => p.Name).ToArray();

        private static OWFolderExportOptions NoMedia => new OWFolderExportOptions { IncludeMedia = false };

        /// <summary>Everything left in the parent: the folder and any temp beside it.</summary>
        private string[] Leftovers() => Directory.GetFileSystemEntries(_parent).Select(Path.GetFileName).ToArray();

        // -- WorldForDisk: the package's world_for_disk, case for case -------------------------------

        [Test]
        public void WorldForDisk_RenamesTheNowInPlace_AndStampsTheVersionLast()
        {
            var wire = JObject.Parse("{\"id\":\"w\",\"name\":\"n\",\"time_range_current\":3,\"x\":1}");

            var disk = OWFolderExport.WorldForDisk(wire);

            // package: {'id': 'w', 'name': 'n', 'time_current': 3, 'x': 1, 'format_version': '0.3.14'}
            CollectionAssert.AreEqual(new[] { "id", "name", "time_current", "x", "format_version" }, Keys(disk));
            Assert.AreEqual(3, (int)disk["time_current"]);
            Assert.AreEqual("0.3.14", (string)disk["format_version"]);
        }

        [Test]
        public void WorldForDisk_BothSpellingsPresent_KeepsBoth()
        {
            var wire = JObject.Parse("{\"id\":\"w\",\"name\":\"\",\"time_range_current\":3,\"time_current\":4}");

            var disk = OWFolderExport.WorldForDisk(wire);

            // package: {'id': 'w', 'name': '', 'time_range_current': 3, 'time_current': 4, 'format_version': '0.3.14'}
            CollectionAssert.AreEqual(new[] { "id", "name", "time_range_current", "time_current", "format_version" }, Keys(disk));
            Assert.AreEqual(3, (int)disk["time_range_current"]);
            Assert.AreEqual(4, (int)disk["time_current"]);
        }

        [Test]
        public void WorldForDisk_KeepsACarriedFormatVersion_EvenNull()
        {
            var disk = OWFolderExport.WorldForDisk(JObject.Parse("{\"id\":\"w\",\"name\":\"n\",\"format_version\":null}"));

            // package: {'id': 'w', 'name': 'n', 'format_version': None}
            Assert.AreEqual(JTokenType.Null, disk["format_version"].Type);
        }

        [Test]
        public void WorldForDisk_SnapshotCountsWithoutSnapshotOf_IsDropped()
        {
            var disk = OWFolderExport.WorldForDisk(
                JObject.Parse("{\"id\":\"w\",\"name\":\"n\",\"snapshot_counts\":{\"a\":1}}"),
                new Dictionary<string, int> { { "character", 2 } });

            Assert.IsFalse(disk.ContainsKey("snapshot_counts"));
        }

        [Test]
        public void WorldForDisk_ASnapshotsCounts_AreRecountedInPlace_ZerosLeftOut()
        {
            var disk = OWFolderExport.WorldForDisk(
                JObject.Parse("{\"id\":\"w\",\"name\":\"n\",\"snapshot_of\":\"o\",\"snapshot_counts\":{\"a\":9}}"),
                new Dictionary<string, int> { { "character", 2 }, { "map", 0 } });

            // package: {..., 'snapshot_of': 'o', 'snapshot_counts': {'character': 2}, 'format_version': '0.3.14'}
            CollectionAssert.AreEqual(new[] { "id", "name", "snapshot_of", "snapshot_counts", "format_version" }, Keys(disk));
            Assert.AreEqual("{\"character\":2}", disk["snapshot_counts"].ToString(Newtonsoft.Json.Formatting.None));
        }

        [Test]
        public void WorldForDisk_ASnapshotsCounts_WithNoCountsGiven_AreDropped()
        {
            var disk = OWFolderExport.WorldForDisk(
                JObject.Parse("{\"id\":\"w\",\"name\":\"n\",\"snapshot_of\":\"o\",\"snapshot_counts\":{\"a\":9}}"));

            Assert.IsFalse(disk.ContainsKey("snapshot_counts"));
        }

        [Test]
        public void WorldForDisk_LeavesItsInputAlone()
        {
            var wire = JObject.Parse("{\"id\":\"w\",\"name\":\"n\",\"time_range_current\":3,\"x\":{\"deep\":[1]}}");
            var before = wire.ToString();

            var disk = OWFolderExport.WorldForDisk(wire);
            ((JArray)disk["x"]["deep"]).Add(2);

            Assert.AreEqual(before, wire.ToString());
        }

        [TestCase("{\"name\":\"n\"}")]
        [TestCase("{\"id\":\"\",\"name\":\"n\"}")]
        [TestCase("{\"id\":7,\"name\":\"n\"}")]
        [TestCase("{\"id\":\"w\"}")]
        [TestCase("{\"id\":\"w\",\"name\":null}")]
        public void WorldForDisk_RefusesAWorldWithoutItsTwoRequiredKeys(string wire)
        {
            Assert.Throws<OWFolderFormatException>(() => OWFolderExport.WorldForDisk(JObject.Parse(wire)));
        }

        // -- The folder, in the package's bytes ------------------------------------------------------

        private const string WireWorld =
            "{\"id\":\"0695db52-0000-7000-8000-000000000001\",\"name\":\"Testmoor\",\"description\":\"A moor, Jos\\u00e9's.\","
            + "\"time_range_min\":0,\"time_range_current\":453,\"length_unit\":\"\",\"public_read\":true,\"owner_character\":null,"
            + "\"created_at\":\"2026-01-07T01:21:41.262478+00:00\",\"updated_at\":\"2026-10-11T11:53:28.489198+00:00\"}";

        private const string WireCharacter =
            "{\"id\":\"01a1206b-0000-7000-8000-00000000c001\",\"type\":\"character\",\"name\":\"Ann \\u00c1\",\"level\":2.50,"
            + "\"weight\":1.0,\"big\":1e21,\"tiny\":0.000001,\"tags\":[],\"x_tool\":{\"b\":1,\"a\":[]},\"location\":null,"
            + "\"created_at\":\"2026-01-07T01:21:41.262478+00:00\"}";

        private const string WireMap =
            "{\"id\":\"069620b8-0000-7000-8000-00000000a001\",\"type\":\"map\",\"name\":\"The Moor\","
            + "\"image_url\":\"https://media.example.test/w/maps/the%20moor.webp?v=2\"}";

        private RouteTransport Testmoor() => new RouteTransport { World = WireWorld }
            .Type("character", Page(WireCharacter))
            .Type("map", Page(WireMap));

        [Test]
        public async Task Export_WritesThePackagesBytes()
        {
            var options = new OWFolderExportOptions
            {
                IncludeMedia = false, Clock = () => new DateTimeOffset(2026, 10, 19, 21, 0, 0, 5, TimeSpan.Zero),
            };
            await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options);

            Assert.AreEqual(
                "{\n  \"id\": \"0695db52-0000-7000-8000-000000000001\",\n  \"name\": \"Testmoor\",\n"
                + "  \"description\": \"A moor, Jos\u00e9's.\",\n  \"time_range_min\": 0,\n  \"time_current\": 453,\n"
                + "  \"length_unit\": \"\",\n  \"public_read\": true,\n  \"owner_character\": null,\n"
                + "  \"created_at\": \"2026-01-07T01:21:41.262478+00:00\",\n"
                + "  \"updated_at\": \"2026-10-11T11:53:28.489198+00:00\",\n"
                + "  \"source\": {\n    \"server\": \"https://example.test\",\n"
                + "    \"world\": \"0695db52-0000-7000-8000-000000000001\",\n    \"at\": \"2026-10-19T21:00:00.005Z\"\n  },\n"
                + "  \"producer\": {\n    \"tool\": \"com.onlyworlds.sdk\",\n    \"version\": \"" + OWFolderExport.ProducerVersion + "\",\n"
                + "    \"input\": \"server\"\n  },\n"
                + "  \"format_version\": \"0.3.14\"\n}\n",
                Text(Path.Combine(_folder, "world.json")));

            Assert.AreEqual(
                "{\n  \"id\": \"01a1206b-0000-7000-8000-00000000c001\",\n  \"type\": \"character\",\n"
                + "  \"name\": \"Ann \u00c1\",\n  \"level\": 2.5,\n  \"weight\": 1,\n  \"big\": 1e+21,\n  \"tiny\": 0.000001,\n"
                + "  \"tags\": [],\n  \"x_tool\": {\n    \"b\": 1,\n    \"a\": []\n  },\n  \"location\": null,\n"
                + "  \"created_at\": \"2026-01-07T01:21:41.262478+00:00\"\n}\n",
                Text(Path.Combine(_folder, "elements", "character", "ann-a--0000c001.json")));

            Assert.AreEqual(
                "{\n  \"id\": \"069620b8-0000-7000-8000-00000000a001\",\n  \"type\": \"map\",\n  \"name\": \"The Moor\",\n"
                + "  \"image_url\": \"https://media.example.test/w/maps/the%20moor.webp?v=2\"\n}\n",
                Text(Path.Combine(_folder, "elements", "map", "the-moor--0000a001.json")));

            // No BOM, and nothing else in the tree: two type directories, no media, no temp beside it.
            Assert.AreEqual((byte)'{', File.ReadAllBytes(Path.Combine(_folder, "world.json"))[0]);
            CollectionAssert.AreEquivalent(new[] { "elements", "world.json" }, Directory.GetFileSystemEntries(_folder).Select(Path.GetFileName));
            CollectionAssert.AreEquivalent(new[] { "character", "map" }, Directory.GetDirectories(Path.Combine(_folder, "elements")).Select(Path.GetFileName));
            CollectionAssert.AreEqual(new[] { "world" }, Leftovers());
        }

        [Test]
        public async Task Export_ReportsWhatItWrote()
        {
            var report = await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, NoMedia);

            Assert.AreEqual(Path.GetFullPath(_folder), report.Folder);
            Assert.AreEqual("0695db52-0000-7000-8000-000000000001", report.WorldId);
            Assert.AreEqual("Testmoor", report.WorldName);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { { "character", 1 }, { "map", 1 } }, report.Counts);
            Assert.AreEqual(2, report.Total);
        }

        [Test]
        public async Task Export_ReadsAll22Types_WithGetsOnly()
        {
            var transport = Testmoor();
            await OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia);

            Assert.IsTrue(transport.Calls.All(c => c.Method == "GET"));
            var routes = transport.Calls.Select(c => new Uri(c.Url).AbsolutePath).ToList();
            Assert.AreEqual("/api/v2/world/", routes[0]);
            Assert.AreEqual("/api/v2/me/", routes[1], "which slice the key reads, for exported_as");
            CollectionAssert.AreEqual(OWElementTypes.Slugs.Select(t => "/api/v2/" + t + "/"), routes.Skip(2));
        }

        [Test]
        public async Task Export_CollisionRule_IdAscending_CaseFolded_WhateverTheOrder()
        {
            // Arrival order is the reverse of id order, so a writer that settles by arrival names them wrong.
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }.Type("character", Page(
                "{\"id\":\"44444444-0000-7000-8000-000000000002\",\"name\":\"\u682a\u5f0f\"}",
                "{\"id\":\"33333333-0000-7000-8000-000000000001\"}",
                "{\"id\":\"22222222-0000-7000-8000-00000000ABCD\",\"name\":\"ann\"}",
                "{\"id\":\"11111111-0000-7000-8000-00000000abcd\",\"name\":\"Ann\"}",
                "{\"id\":\"00000000-0000-7000-8000-00000000abcd\",\"name\":\"Ann\"}"));

            await OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia);

            // package resolve_filenames, same five:
            var expected = new Dictionary<string, string>
            {
                { "00000000-0000-7000-8000-00000000abcd", "ann--0000abcd.json" },
                { "11111111-0000-7000-8000-00000000abcd", "ann--11111111-0000-7000-8000-00000000abcd.json" },
                { "22222222-0000-7000-8000-00000000ABCD", "ann--22222222-0000-7000-8000-00000000ABCD.json" },
                { "33333333-0000-7000-8000-000000000001", "33333333-0000-7000-8000-000000000001.json" },
                { "44444444-0000-7000-8000-000000000002", "44444444-0000-7000-8000-000000000002.json" },
            };
            var directory = Path.Combine(_folder, "elements", "character");
            var actual = Directory.GetFiles(directory).ToDictionary(
                f => (string)OWFolderWriter.ReadJsonFile(f)["id"], Path.GetFileName);
            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public async Task Export_AnIdServedTwice_KeepsItsLastBody()
        {
            // A page walk that met a write serves the element again on a later page, newer.
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }.Type("character",
                Page(true, "c2", "{\"id\":\"a-1\",\"name\":\"Old\"}", "{\"id\":\"b-2\",\"name\":\"Bea\"}"),
                Page("{\"id\":\"a-1\",\"name\":\"New\"}"));

            var report = await OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia);

            var directory = Path.Combine(_folder, "elements", "character");
            CollectionAssert.AreEquivalent(new[] { "new--a-1.json", "bea--b-2.json" }, Directory.GetFiles(directory).Select(Path.GetFileName));
            Assert.AreEqual(2, report.Counts["character"]);
        }

        [Test]
        public void Export_OneIdUnderTwoTypes_IsRefused_AndLeavesNothing()
        {
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }
                .Type("character", Page("{\"id\":\"same\",\"name\":\"A\"}"))
                .Type("location", Page("{\"id\":\"same\",\"name\":\"B\"}"));

            Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia));
            CollectionAssert.IsEmpty(Leftovers());
        }

        [Test]
        public void Export_AnElementWithoutAnId_IsRefused()
        {
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }
                .Type("character", Page("{\"name\":\"Nobody\"}"));

            Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia));
            CollectionAssert.IsEmpty(Leftovers());
        }

        // -- A folder's birth ------------------------------------------------------------------------

        [Test]
        public void Export_RefusesAFolderThatHoldsAnything_AndTouchesIt_Not()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "notes.txt"), "mine");
            var transport = Testmoor();

            Assert.ThrowsAsync<IOException>(() => OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia));

            CollectionAssert.IsEmpty(transport.Calls, "Refused before the first read.");
            CollectionAssert.AreEqual(new[] { "notes.txt" }, Directory.GetFileSystemEntries(_folder).Select(Path.GetFileName));
            CollectionAssert.AreEqual(new[] { "world" }, Leftovers());
        }

        [Test]
        public void Export_RefusesAPathThatIsAFile()
        {
            File.WriteAllText(_folder, "a file");

            Assert.ThrowsAsync<IOException>(() => OWFolderExport.ExportAsync(Client(Testmoor()), _folder, NoMedia));
            Assert.AreEqual("a file", File.ReadAllText(_folder));
        }

        [Test]
        public async Task Export_IntoAnEmptyDirectory_Works()
        {
            Directory.CreateDirectory(_folder);

            await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, NoMedia);

            Assert.IsTrue(File.Exists(Path.Combine(_folder, "world.json")));
            CollectionAssert.AreEqual(new[] { "world" }, Leftovers());
        }

        [Test]
        public async Task Export_IntoAFolderWhoseParentIsMissing_MakesTheParent()
        {
            var deep = Path.Combine(_parent, "a", "b", "world");

            await OWFolderExport.ExportAsync(Client(Testmoor()), deep, NoMedia);

            Assert.IsTrue(File.Exists(Path.Combine(deep, "world.json")));
            CollectionAssert.AreEqual(new[] { "world" }, Directory.GetFileSystemEntries(Path.Combine(_parent, "a", "b")).Select(Path.GetFileName));
        }

        /// <summary>A folder 170 characters long: the elements fit under Windows' 259, a Map's sidecar (about 100 below it) does not.</summary>
        private string DeepFolder()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("The path limit is Windows'.");
            return Path.Combine(_parent, new string('d', 170 - _parent.Length - 1));
        }

        [Test]
        public void Export_TooDeepForItsFiles_IsRefusedBeforeAnyWrite()
        {
            var deep = DeepFolder();
            var fetched = 0;
            var options = new OWFolderExportOptions { FetchMedia = (url, ct) => { fetched++; return Task.FromResult(new byte[] { 1 }); } };

            var error = Assert.ThrowsAsync<PathTooLongException>(() => OWFolderExport.ExportAsync(Client(Testmoor()), deep, options));

            StringAssert.Contains("characters shorter", error.Message);
            Assert.AreEqual(0, fetched);
            CollectionAssert.IsEmpty(Leftovers());
        }

        [Test]
        public async Task Export_TheDepthCheck_CountsOnlyWhatWillBeWritten()
        {
            var deep = DeepFolder();

            var report = await OWFolderExport.ExportAsync(Client(Testmoor()), deep, NoMedia);

            Assert.AreEqual(2, report.Total);
        }

        [Test]
        public void Export_AWorldThatCannotBeWritten_RemovesTheHalfBuiltFolder()
        {
            // The elements land in the temp before world.json is refused (as in the package), so this
            // is the case that proves the temp goes.
            var transport = Testmoor();
            transport.World = "{\"name\":\"no id\"}";

            Assert.ThrowsAsync<OWFolderFormatException>(() => OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia));
            CollectionAssert.IsEmpty(Leftovers());
        }

        // -- Errors and secrets ----------------------------------------------------------------------

        [Test]
        public void Export_ARefusedRead_ThrowsTheClientsOWApiError_WithNoKeyOrPin()
        {
            var transport = Testmoor();
            transport.Refusals["location"] = (401,
                "{\"error\":{\"type\":\"authentication_error\",\"code\":\"invalid_credentials\",\"message\":\"Invalid API key or PIN.\"}}");

            var error = Assert.ThrowsAsync<OWApiError>(() => OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia));

            Assert.IsTrue(error.IsAuthError);
            StringAssert.DoesNotContain(Key, error.ToString());
            StringAssert.DoesNotContain(Pin, error.ToString());
            CollectionAssert.IsEmpty(Leftovers());
        }

        [Test]
        public async Task Export_NoFileCarriesTheKeyOrThePin()
        {
            var options = new OWFolderExportOptions { FetchMedia = (url, ct) => Task.FromResult(new byte[] { 1, 2, 3 }) };

            await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options);

            var files = Directory.GetFiles(_parent, "*", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(5), "world, two elements, an image and its sidecar");
            foreach (var file in files)
            {
                var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                StringAssert.DoesNotContain(Key, text, file);
                StringAssert.DoesNotContain(Pin, text, file);
            }
        }

        [Test]
        public void Export_CancelledMidMedia_ThrowsAndRemovesTheTemp()
        {
            var cts = new CancellationTokenSource();
            var options = new OWFolderExportOptions
            {
                FetchMedia = (url, ct) =>
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(new byte[0]);
                },
            };

            Assert.CatchAsync<OperationCanceledException>(() =>
                OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options, ct: cts.Token));
            CollectionAssert.IsEmpty(Leftovers());
        }

        [Test]
        public void Export_CancelledBeforeTheWalk_ReadsNothingMore()
        {
            var transport = Testmoor();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(() =>
                OWFolderExport.ExportAsync(Client(transport), _folder, NoMedia, ct: cts.Token));
            Assert.That(transport.Calls.Count, Is.LessThanOrEqualTo(1));
            CollectionAssert.IsEmpty(Leftovers());
        }

        // -- Media -----------------------------------------------------------------------------------

        [Test]
        public async Task Export_KeepsAMapsImage_AsServed_WithAtlasesSidecar()
        {
            var image = new byte[] { 0x52, 0x49, 0x46, 0x46, 9, 8, 7 };
            var fetched = new List<string>();
            var options = new OWFolderExportOptions
            {
                FetchMedia = (url, ct) =>
                {
                    fetched.Add(url);
                    return Task.FromResult(image);
                },
            };

            var report = await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options);

            CollectionAssert.AreEqual(new[] { "https://media.example.test/w/maps/the%20moor.webp?v=2" }, fetched);

            // The filename as served: the path's last segment, unescaped by nobody, query cut.
            var directory = Path.Combine(_folder, "media", "069620b8-0000-7000-8000-00000000a001", "base-image");
            var imagePath = Path.Combine(directory, "the%20moor.webp");
            CollectionAssert.AreEqual(image, File.ReadAllBytes(imagePath));

            var sidecars = Directory.GetFiles(directory, "*.meta.json");
            Assert.AreEqual(1, sidecars.Length);
            var sidecar = OWFolderWriter.Parse(Text(sidecars[0]));
            CollectionAssert.AreEqual(new[] { "id", "element_id", "folder", "filename", "mime", "size_bytes", "added_at" }, Keys(sidecar));
            Assert.AreEqual(Path.GetFileName(sidecars[0]), (string)sidecar["id"] + ".meta.json");
            Assert.IsTrue(Guid.TryParse((string)sidecar["id"], out _));
            Assert.AreEqual("069620b8-0000-7000-8000-00000000a001", (string)sidecar["element_id"]);
            Assert.AreEqual("base-image", (string)sidecar["folder"]);
            Assert.AreEqual("the%20moor.webp", (string)sidecar["filename"]);
            Assert.AreEqual("image/webp", (string)sidecar["mime"]);
            Assert.AreEqual(image.Length, (int)sidecar["size_bytes"]);
            StringAssert.IsMatch(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$", (string)sidecar["added_at"]);

            // Only the bytes as served: no PNG beside the WEBP (the caller's step), and the Map is untouched.
            CollectionAssert.AreEquivalent(new[] { imagePath, sidecars[0] }, Directory.GetFiles(directory));
            CollectionAssert.AreEquivalent(new[] { imagePath, sidecars[0] }, report.Media);
            CollectionAssert.IsEmpty(report.MediaProblems);
            StringAssert.DoesNotContain("image_media_id", Text(Path.Combine(_folder, "elements", "map", "the-moor--0000a001.json")));
        }

        [Test]
        public async Task Export_AnImageThatWillNotFetch_IsAProblemLine_NotAFailure()
        {
            var options = new OWFolderExportOptions
            {
                FetchMedia = (url, ct) => Task.FromException<byte[]>(new OWTransportError("GET " + url + " failed: HTTP 403")),
            };

            var report = await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options);

            CollectionAssert.AreEqual(
                new[] { "The Moor: GET https://media.example.test/w/maps/the%20moor.webp?v=2 failed: HTTP 403" },
                report.MediaProblems);
            CollectionAssert.IsEmpty(report.Media);
            Assert.IsFalse(Directory.Exists(Path.Combine(_folder, "media")));
            Assert.AreEqual(2, report.Total);
        }

        [Test]
        public async Task Export_OneBadImage_LeavesTheOthers()
        {
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }.Type("map", Page(
                "{\"id\":\"m-1\",\"name\":\"Good\",\"image_url\":\"https://h.test/good.png\"}",
                "{\"id\":\"m-2\",\"image_url\":\"https://h.test/bad.png\"}"));
            var options = new OWFolderExportOptions
            {
                FetchMedia = (url, ct) => url.EndsWith("bad.png")
                    ? Task.FromException<byte[]>(new InvalidOperationException("nope"))
                    : Task.FromResult(new byte[] { 1 }),
            };

            var report = await OWFolderExport.ExportAsync(Client(transport), _folder, options);

            // A Map with no name is named by its id.
            CollectionAssert.AreEqual(new[] { "m-2: nope" }, report.MediaProblems);
            Assert.IsTrue(File.Exists(Path.Combine(_folder, "media", "m-1", "base-image", "good.png")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_folder, "media", "m-2")));
        }

        [Test]
        public async Task Export_ImageUrlsThatAreNotWebAddresses_AreLeftAlone()
        {
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }.Type("map", Page(
                "{\"id\":\"m-1\",\"name\":\"Empty\",\"image_url\":\"\"}",
                "{\"id\":\"m-2\",\"name\":\"Null\",\"image_url\":null}",
                "{\"id\":\"m-3\",\"name\":\"Local\",\"image_url\":\"maps/local.png\"}",
                "{\"id\":\"m-4\",\"name\":\"None\"}"));
            var calls = 0;
            var options = new OWFolderExportOptions { FetchMedia = (url, ct) => { calls++; return Task.FromResult(new byte[0]); } };

            var report = await OWFolderExport.ExportAsync(Client(transport), _folder, options);

            Assert.AreEqual(0, calls);
            CollectionAssert.IsEmpty(report.MediaProblems);
            Assert.IsFalse(Directory.Exists(Path.Combine(_folder, "media")));
        }

        [TestCase("https://h.test/", "m-1.img")]
        [TestCase("https://h.test", "m-1.img")]
        [TestCase("https://h.test/a/b.JPG#frag", "b.JPG")]
        [TestCase("https://h.test/a/..", "m-1.img")]
        [TestCase("https://h.test/a/x%3Ay.png", "x%3Ay.png")]
        public async Task Export_TheImagesFilename_IsTheLastPathSegment_OrTheMapsId(string url, string filename)
        {
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }
                .Type("map", Page("{\"id\":\"m-1\",\"name\":\"M\",\"image_url\":\"" + url + "\"}"));
            var options = new OWFolderExportOptions { FetchMedia = (u, ct) => Task.FromResult(new byte[] { 1 }) };

            await OWFolderExport.ExportAsync(Client(transport), _folder, options);

            var directory = Path.Combine(_folder, "media", "m-1", "base-image");
            Assert.IsTrue(File.Exists(Path.Combine(directory, filename)), string.Join(", ", Directory.GetFiles(directory)));
            var sidecar = OWFolderWriter.Parse(Text(Directory.GetFiles(directory, "*.meta.json").Single()));
            Assert.AreEqual(filename.EndsWith(".JPG") ? "image/jpeg" : filename.EndsWith(".png") ? "image/png" : "application/octet-stream",
                (string)sidecar["mime"]);
        }

        [Test]
        public async Task Export_AMapIdThatCannotNameAFolder_IsAProblem_NeverAPathOutOfMedia()
        {
            var transport = new RouteTransport { World = "{\"id\":\"w\",\"name\":\"n\"}" }
                .Type("map", Page("{\"id\":\"..\",\"name\":\"Up\",\"image_url\":\"https://h.test/up.png\"}"));
            var calls = 0;
            var options = new OWFolderExportOptions { FetchMedia = (u, ct) => { calls++; return Task.FromResult(new byte[] { 1 }); } };

            var report = await OWFolderExport.ExportAsync(Client(transport), _folder, options);

            Assert.AreEqual(0, calls);
            CollectionAssert.AreEqual(new[] { "Up: its id cannot name a folder" }, report.MediaProblems);
            Assert.IsFalse(File.Exists(Path.Combine(_folder, "up.png")));
        }

        [Test]
        public async Task Export_WithoutMedia_NeverFetches()
        {
            var calls = 0;
            var options = new OWFolderExportOptions
            {
                IncludeMedia = false,
                FetchMedia = (url, ct) => { calls++; return Task.FromResult(new byte[] { 1 }); },
            };

            await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options);

            Assert.AreEqual(0, calls);
            Assert.IsFalse(Directory.Exists(Path.Combine(_folder, "media")));
        }

        // -- Progress --------------------------------------------------------------------------------

        private sealed class Recorder : IProgress<OWFolderExportProgress>
        {
            public readonly List<OWFolderExportProgress> Steps = new List<OWFolderExportProgress>();
            public void Report(OWFolderExportProgress value) => Steps.Add(value);
        }

        [Test]
        public async Task Export_ReportsEachStage()
        {
            var recorder = new Recorder();
            var options = new OWFolderExportOptions { FetchMedia = (url, ct) => Task.FromResult(new byte[] { 1 }) };

            await OWFolderExport.ExportAsync(Client(Testmoor()), _folder, options, recorder);

            var steps = recorder.Steps.Select(s => $"{s.Stage}:{s.Type}:{s.Done}/{s.Total}").ToList();
            var expected = new List<string> { "world::0/1", "world::1/1" };
            for (var i = 0; i < OWElementTypes.Slugs.Length; i++) expected.Add($"elements:{OWElementTypes.Slugs[i]}:{i}/22");
            expected.AddRange(new[] { "elements::22/22", "media::0/1", "media::1/1" });
            CollectionAssert.AreEqual(expected, steps);
        }

        // -- Live: Moppetopia against the package's own export ---------------------------------------

        /// <summary>A plain HttpClient transport, for a run outside Unity's loop.</summary>
        private sealed class HttpTransport : IOWTransport
        {
            private static readonly HttpClient Http = new HttpClient();

            public async Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
            {
                var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
                foreach (var pair in request.Headers) message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                using (var response = await Http.SendAsync(message, ct))
                {
                    return new OWResponse
                    {
                        Status = (int)response.StatusCode,
                        Body = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()),
                        Headers = new Dictionary<string, string>(),
                    };
                }
            }
        }

        /// <summary>
        /// Exports Moppetopia (its public demo key, read-only) and diffs it against the package's export
        /// of it. Explicit: it needs the network and the folder <c>OW_MOPPETOPIA_REFERENCE</c> names
        /// (the Armature's <c>projects/moppetopia-world</c>); <c>OW_EXPORT_OUT</c> keeps the export.
        /// </summary>
        [Test, Explicit("Live: reads onlyworlds.com and media.onlyworlds.com.")]
        public async Task Live_Moppetopia_MatchesThePackagesExport()
        {
            var reference = Environment.GetEnvironmentVariable("OW_MOPPETOPIA_REFERENCE");
            if (string.IsNullOrEmpty(reference) || !Directory.Exists(reference)) Assert.Ignore("OW_MOPPETOPIA_REFERENCE is not a folder.");

            var outRoot = Environment.GetEnvironmentVariable("OW_EXPORT_OUT");
            var folder = string.IsNullOrEmpty(outRoot) ? _folder : Path.Combine(outRoot, "moppetopia-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            var client = new OWClient(new OWClientConfig { ApiKey = "0000000001", Transport = new HttpTransport() });
            var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "onlyworlds-unity-sdk");
            var options = new OWFolderExportOptions { FetchMedia = (url, ct) => http.GetByteArrayAsync(url) };

            var report = await OWFolderExport.ExportAsync(client, folder, options);

            var ours = Directory.GetFiles(Path.Combine(folder, "elements"), "*.json", SearchOption.AllDirectories)
                .Select(f => f.Substring(folder.Length + 1)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var theirs = Directory.GetFiles(Path.Combine(reference, "elements"), "*.json", SearchOption.AllDirectories)
                .Select(f => f.Substring(reference.Length + 1)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var differ = ours.Intersect(theirs).Where(f =>
                !File.ReadAllBytes(Path.Combine(folder, f)).SequenceEqual(File.ReadAllBytes(Path.Combine(reference, f)))).ToList();
            var worldSame = File.ReadAllBytes(Path.Combine(folder, "world.json"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(reference, "world.json")));

            TestContext.WriteLine($"folder {folder}");
            TestContext.WriteLine($"elements: ours {ours.Count}, theirs {theirs.Count}, only ours {ours.Except(theirs).Count()}, "
                                  + $"only theirs {theirs.Except(ours).Count()}, differ {differ.Count}; world.json same: {worldSame}");
            foreach (var line in report.MediaProblems) TestContext.WriteLine("media problem: " + line);
            foreach (var path in report.Media.Where(p => !p.EndsWith(".meta.json")))
            {
                var twin = Path.Combine(reference, path.Substring(folder.Length + 1));
                TestContext.WriteLine($"media {path.Substring(folder.Length + 1)}: same bytes as theirs: "
                                      + (File.Exists(twin) && File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(twin))));
            }

            CollectionAssert.AreEqual(theirs, ours);
            CollectionAssert.IsEmpty(differ);
            Assert.IsTrue(worldSame, "world.json differs from the package's");
            CollectionAssert.IsEmpty(report.MediaProblems);
        }
    }
}
