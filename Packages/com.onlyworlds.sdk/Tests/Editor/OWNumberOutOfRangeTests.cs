using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// A number the reader cannot hold (an integer of more than 380 characters, a number past the largest double) is
    /// one refusal with one name, <c>number_out_of_range</c>, at every door: a folder skips that file and serves the
    /// rest, a world.json or an export file is refused by that name, a response throws it with the path inside the body.
    /// The TypeScript SDK's folder reader (4.195.0) and the Python package's (0.143.0) skip such a file by the same word.
    /// </summary>
    public class OWNumberOutOfRangeTests
    {
        private const string WorldId = "0195c3a1-0000-7000-8000-00000000aaaa";
        private const string IdA = "0195c3a1-7b2e-7c4d-9e5f-00000000000a";
        private const string IdB = "0195c3a1-7b2e-7c4d-9e5f-00000000000b";
        private const string IdC = "0195c3a1-7b2e-7c4d-9e5f-00000000000c";

        /// <summary>Far past the line on both sides of it: an integer of 4,301 digits, and a float past the largest double.</summary>
        private static readonly string[] Unheld = { new string('7', 4301), "1e400", "-1e400" };

        /// <summary>The longest integer the reader holds: read exactly, written back with the same bytes.</summary>
        private static readonly string Held = new string('7', 380);

        private string _root;
        private readonly List<OWWorldCache> _caches = new List<OWWorldCache>();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ow-number-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var cache in _caches) UnityEngine.Object.DestroyImmediate(cache);
            _caches.Clear();
            if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private OWWorldCache NewCache()
        {
            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            cache.Initialize(OWWorldKey.FromApi("w-1"), "Num");
            _caches.Add(cache);
            return cache;
        }

        // -- Fixtures ---------------------------------------------------------------------------------------------

        private static string FileText(string id, string name, string number = null)
            => "{\n  \"id\": \"" + id + "\",\n  \"type\": \"character\",\n  \"name\": \"" + name + "\""
               + (number == null ? "" : ",\n  \"x_n\": " + number) + "\n}\n";

        private static string Wire(string id, string name, string number = null)
            => "{\"id\":\"" + id + "\",\"type\":\"character\",\"name\":\"" + name + "\""
               + (number == null ? "" : ",\"x_n\":" + number) + "}";

        private static string Page(string number)
            => "{\"data\":[" + Wire(IdA, "A") + "," + Wire(IdB, "B", number) + "," + Wire(IdC, "C")
               + "],\"has_more\":false,\"next_cursor\":null}";

        /// <summary>A world folder of three characters, the middle one holding <paramref name="number"/>.</summary>
        private string Folder(string tag, string number, string worldNumber = null)
        {
            var dir = Path.Combine(_root, tag);
            var characters = Path.Combine(dir, "elements", "character");
            Directory.CreateDirectory(characters);
            File.WriteAllText(Path.Combine(dir, "world.json"),
                "{\n  \"id\": \"" + WorldId + "\",\n  \"name\": \"Num\"" + (worldNumber == null ? "" : ",\n  \"x_n\": " + worldNumber) + "\n}\n",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "a--0000000a.json"), FileText(IdA, "A"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "b--0000000b.json"), FileText(IdB, "B", number), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "c--0000000c.json"), FileText(IdC, "C"), new UTF8Encoding(false));
            return dir;
        }

        private static string[] Ids(OWFolderReadResult read) => read.Elements.Select(e => e.Id).ToArray();

        private static void AssertRefusal(Exception e, string path)
        {
            Assert.IsInstanceOf<OWNumberOutOfRangeException>(e, e?.ToString());
            var refusal = (OWNumberOutOfRangeException)e;
            Assert.AreEqual(OWJson.NumberOutOfRange, refusal.Reason);
            Assert.AreEqual(path, refusal.Path);
            StringAssert.Contains(OWJson.NumberOutOfRange, refusal.Message);
            Assert.IsInstanceOf<JsonReaderException>(refusal, "a caller that catches a JsonException today still catches it");
        }

        /// <summary>A transport answering by route: the tip, a page of characters, one element; anything else an empty page.</summary>
        private sealed class Routes : IOWTransport
        {
            public string Head = "{\"cursor\":7,\"head\":7,\"changes\":[],\"has_more\":false,\"epoch\":null}";
            public string Changes = "{\"cursor\":\"7:x\",\"head\":7,\"changes\":[],\"has_more\":false,\"epoch\":null}";
            public string World = "{\"id\":\"" + WorldId + "\",\"name\":\"Num\"}";
            public string Characters = "{\"data\":[],\"has_more\":false,\"next_cursor\":null}";
            public string One = "{}";

            /// <summary>When set, a request for this route never gets an answer.</summary>
            public string Unreachable;

            public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
            {
                var uri = new Uri(request.Url);
                var path = uri.AbsolutePath.TrimEnd('/');
                if (Unreachable != null && path.EndsWith(Unreachable, StringComparison.Ordinal))
                    throw new IOException("the connection dropped mid-walk");

                string body;
                if (path.EndsWith("/changes", StringComparison.Ordinal)) body = uri.Query.Contains("head=true") ? Head : Changes;
                else if (path.EndsWith("/world", StringComparison.Ordinal)) body = World;
                else if (path.EndsWith("/character", StringComparison.Ordinal)) body = Characters;
                else if (path.Contains("/character/")) body = One;
                else body = "{\"data\":[],\"has_more\":false,\"next_cursor\":null}";
                return Task.FromResult(new OWResponse { Status = 200, Body = body, Headers = new Dictionary<string, string>() });
            }
        }

        private static OWClient Client(IOWTransport transport) => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "pin-for-tests",
            Transport = transport,
            BaseUrl = "https://example.test/api/v2",
        });

        // -- The boundary -----------------------------------------------------------------------------------------

        [Test]
        public void EveryReader_RefusesTheNumberByName_WithItsPath()
        {
            foreach (var number in Unheld)
            {
                var text = "{\"a\":1,\"x_n\":" + number + "}";
                AssertRefusal(Assert.Catch(() => OWJson.ParseObject(text), number), "x_n");
                AssertRefusal(Assert.Catch(() => OWJson.Deserialize<JObject>(text), number), "x_n");
                AssertRefusal(Assert.Catch(() => OWJson.ParseToken(text), number), "x_n");
                AssertRefusal(Assert.Catch(() => OWJson.Deserialize<OWCharacter>(Wire(IdB, "B", number)), number), "x_n");
            }
        }

        [Test]
        public void TheLine_StaysWhereTheReaderDrawsIt()
        {
            // 380 characters read; 381 are refused, the sign counted among them.
            Assert.AreEqual(JTokenType.Integer, OWJson.ParseObject("{\"n\":" + Held + "}")["n"].Type);
            AssertRefusal(Assert.Catch(() => OWJson.ParseObject("{\"n\":" + new string('7', 381) + "}")), "n");
            AssertRefusal(Assert.Catch(() => OWJson.ParseObject("{\"n\":-" + new string('7', 380) + "}")), "n");
            Assert.AreEqual(JTokenType.Integer, OWJson.ParseObject("{\"n\":-" + new string('7', 379) + "}")["n"].Type);

            // The largest double reads; the first number past it is refused.
            Assert.AreEqual(double.MaxValue, (double)OWJson.ParseObject("{\"n\":1.7976931348623157e308}")["n"]);
            AssertRefusal(Assert.Catch(() => OWJson.ParseObject("{\"n\":1.8e308}")), "n");
        }

        [Test]
        public void OtherFaults_AreNotThisRefusal()
        {
            var cut = Assert.Catch<JsonReaderException>(() => OWJson.ParseObject("{\"n\":7"));
            Assert.IsNotInstanceOf<OWNumberOutOfRangeException>(cut);
            var word = Assert.Catch<JsonReaderException>(() => OWJson.ParseObject("{\"n\":nope}"));
            Assert.IsNotInstanceOf<OWNumberOutOfRangeException>(word);
        }

        [Test]
        public void AHeldNumber_ReadsExactly_AndWritesBackItsBytes()
        {
            var dir = Folder("held", Held);
            var read = OWFolderReader.Read(dir);
            CollectionAssert.AreEqual(new[] { IdA, IdB, IdC }, Ids(read));
            Assert.AreEqual(Held, read.Elements[1].Body["x_n"].ToString());

            var file = Path.Combine(dir, "elements", "character", "b--0000000b.json");
            var before = File.ReadAllText(file);
            Assert.AreEqual(before, OWFolderWriter.Serialize(OWFolderWriter.Parse(before)));
            OWFolderWriter.WriteElement(dir, read.Elements[1].Body);
            Assert.AreEqual(before, File.ReadAllText(file));
        }

        // -- The folder and the archive ---------------------------------------------------------------------------

        [Test]
        public void Folder_SkipsTheFile_ByName_AndServesTheRest()
        {
            for (var i = 0; i < Unheld.Length; i++)
            {
                var read = OWFolderReader.Read(Folder("f" + i, Unheld[i]));
                CollectionAssert.AreEqual(new[] { IdA, IdC }, Ids(read), Unheld[i]);
                Assert.AreEqual(1, read.Skipped.Count);
                Assert.AreEqual("b--0000000b.json", Path.GetFileName(read.Skipped[0].Path));
                StringAssert.StartsWith(OWJson.NumberOutOfRange, read.Skipped[0].Reason);
                StringAssert.DoesNotContain("unparseable", read.Skipped[0].Reason);
            }
        }

        [Test]
        public void Folder_IntoACache_HoldsTheRest()
        {
            var cache = NewCache();
            var read = OWFolderLoader.LoadInto(cache, Folder("load", Unheld[0]));
            Assert.AreEqual(2, cache.Count);
            Assert.IsTrue(cache.Contains(IdA) && cache.Contains(IdC));
            StringAssert.StartsWith(OWJson.NumberOutOfRange, read.Skipped.Single().Reason);
        }

        [Test]
        public void WorldJson_HoldingIt_RefusesTheWorld_ByName()
        {
            foreach (var number in Unheld)
            {
                var e = Assert.Throws<OWFolderFormatException>(() => OWFolderReader.Read(Folder("w" + number.Length + number[0], null, number)));
                StringAssert.Contains(OWJson.NumberOutOfRange, e.Message);
                StringAssert.DoesNotContain("not valid JSON", e.Message);
                AssertRefusal(e.InnerException, "x_n");
            }
        }

        [Test]
        public void Archive_SkipsTheFile_ByName_AndServesTheRest()
        {
            for (var i = 0; i < Unheld.Length; i++)
            {
                var archive = Path.Combine(_root, "a" + i + ".owworld");
                OWWorldArchive.Pack(Folder("a" + i, Unheld[i]), archive);
                var read = OWWorldArchive.Read(archive);
                CollectionAssert.AreEqual(new[] { IdA, IdC }, Ids(read), Unheld[i]);
                Assert.AreEqual("elements/character/b--0000000b.json", read.Skipped.Single().Path);
                StringAssert.StartsWith(OWJson.NumberOutOfRange, read.Skipped.Single().Reason);
            }
        }

        [Test]
        public void ExportFile_HoldingIt_IsRefused_ByName()
        {
            foreach (var number in Unheld)
            {
                var envelope = "{\"format\":\"onlyworlds-world-export\",\"schema_version\":\"00.32.00\",\"exported_at\":\"2026-11-23T00:00:00Z\","
                               + "\"world\":{\"id\":\"" + WorldId + "\",\"name\":\"Num\"},\"elements\":{\"character\":["
                               + Wire(IdA, "A") + "," + Wire(IdB, "B", number) + "," + Wire(IdC, "C") + "]}}";
                var e = Assert.Throws<OWFolderFormatException>(() => OWEnvelopeReader.Parse(envelope, "export.json"));
                StringAssert.Contains(OWJson.NumberOutOfRange, e.Message);
                AssertRefusal(e.InnerException, "elements.character[1].x_n");
            }
        }

        // -- The cache --------------------------------------------------------------------------------------------

        [Test]
        public void Cache_ARawBodyItCannotRead_IsNoAnswerOfFalse()
        {
            foreach (var number in Unheld)
            {
                var cache = NewCache();
                cache.Upsert(IdA, "character", Wire(IdA, "A"));
                cache.Upsert(IdB, "character", Wire(IdB, "B", number).Replace("}", ",\"known_of\":true}"));
                cache.Upsert(IdC, "character", Wire(IdC, "C"));

                AssertRefusal(Assert.Catch(() => cache.IsKnownOf(IdB), number), "x_n");
                CollectionAssert.AreEqual(new[] { "A", "C" }, cache.All<OWCharacter>("character").Select(c => c.Name).ToArray());
            }
        }

        // -- The client -------------------------------------------------------------------------------------------

        [Test]
        public void Client_AResponseHoldingIt_ThrowsTheRefusal_WithThePathInTheBody()
        {
            foreach (var number in Unheld)
            {
                var routes = new Routes { Characters = Page(number), One = Wire(IdB, "B", number) };
                var list = Assert.CatchAsync(() => Client(routes).ListAllAsync<JObject>("character"));
                AssertRefusal(list, "data[1].x_n");
                StringAssert.Contains("GET /character/", list.Message);

                AssertRefusal(Assert.CatchAsync(() => Client(routes).ListAsync<OWCharacter>("character")), "data[1].x_n");
                AssertRefusal(Assert.CatchAsync(() => Client(routes).GetAsync<JObject>("character", IdB)), "x_n");
                AssertRefusal(Assert.CatchAsync(() => Client(routes).RequestAsync("GET", "/character/")), "data[1].x_n");
            }
        }

        // -- Sync -------------------------------------------------------------------------------------------------

        private OWWorldCache HeldAtFive()
        {
            var cache = NewCache();
            cache.Upsert(IdA, "character", Wire(IdA, "A0"));
            cache.Upsert(IdC, "character", Wire(IdC, "C0"));
            cache.SetCursor(5, "5", null, "2026-11-23T00:00:00Z");
            return cache;
        }

        private static void AssertHeldAtFive(OWWorldCache cache)
        {
            CollectionAssert.AreEqual(new[] { "A0", "C0" }, cache.All<OWCharacter>("character").Select(c => c.Name).ToArray());
            Assert.AreEqual(2, cache.Count);
            Assert.AreEqual(5, cache.Cursor);
            Assert.AreEqual("5", cache.CursorToken);
            Assert.AreEqual("2026-11-23T00:00:00Z", cache.SyncedAt);
        }

        [Test]
        public void Baseline_AWalkThatThrows_LeavesTheCacheAsItWas()
        {
            // Not a number: a transport fault mid-walk, after the tip was read and before the walk ended.
            var cache = HeldAtFive();
            Assert.ThrowsAsync<OWTransportError>(() => OWSync.BaselineAsync(Client(new Routes { Unreachable = "/character" }), cache));
            AssertHeldAtFive(cache);
        }

        [Test]
        public void Baseline_APageHoldingIt_ThrowsTheRefusal_AndLeavesTheCacheAsItWas()
        {
            var cache = HeldAtFive();
            AssertRefusal(Assert.CatchAsync(() => OWSync.BaselineAsync(Client(new Routes { Characters = Page(Unheld[0]) }), cache)), "data[1].x_n");
            AssertHeldAtFive(cache);
        }

        [Test]
        public void Incremental_APageHoldingIt_ThrowsTheRefusal_AndChangesNothing()
        {
            foreach (var number in Unheld)
            {
                var cache = HeldAtFive();
                string Up(string id, int seq, string el) =>
                    "{\"op\":\"upsert\",\"type\":\"character\",\"id\":\"" + id + "\",\"change_seq\":" + seq
                    + ",\"updated_at\":\"2026-11-23T00:00:00Z\",\"element\":" + el + "}";
                var changes = "{\"cursor\":\"7:x\",\"head\":7,\"changes\":[" + Up(IdA, 6, Wire(IdA, "A1")) + ","
                              + Up(IdB, 6, Wire(IdB, "B1", number)) + "," + Up(IdC, 7, Wire(IdC, "C1")) + "],\"has_more\":false,\"epoch\":null}";
                AssertRefusal(Assert.CatchAsync(() => OWSync.IncrementalAsync(Client(new Routes { Changes = changes }), cache)), "changes[1].element.x_n");
                AssertHeldAtFive(cache);
            }
        }

        // -- The export -------------------------------------------------------------------------------------------

        [Test]
        public void Export_APageHoldingIt_ThrowsTheRefusal_AndWritesNothing()
        {
            var target = Path.Combine(_root, "export", "world");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            AssertRefusal(Assert.CatchAsync(() => OWFolderExport.ExportAsync(Client(new Routes { Characters = Page(Unheld[1]) }), target,
                new OWFolderExportOptions { IncludeMedia = false })), "data[1].x_n");
            CollectionAssert.IsEmpty(Directory.GetFileSystemEntries(Path.GetDirectoryName(target)));
        }
    }
}
