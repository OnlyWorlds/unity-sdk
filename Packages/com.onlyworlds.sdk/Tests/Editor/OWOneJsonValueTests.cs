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
    /// A JSON text is one value and nothing after it but whitespace (folder spec §4.8): <c>{"id": "c-9"} {"hidden": true}</c>
    /// is read by no door, since a reader that took the first object would serve as visible what its writer meant hidden.
    /// Whitespace is JSON's four characters (space, tab, LF, CR); a comment, a second value, a stray brace, U+00A0 or a
    /// NUL after the value is content. A folder or a <c>.owworld</c> skips such a file by name and serves the rest, a
    /// world.json or an export file is refused, a response throws naming the request, as for a number the reader cannot
    /// hold (<see cref="OWNumberOutOfRangeTests"/>).
    /// </summary>
    public class OWOneJsonValueTests
    {
        private const string WorldId = "0195c3a1-0000-7000-8000-00000000aaaa";
        private const string IdA = "0195c3a1-7b2e-7c4d-9e5f-00000000000a";
        private const string IdB = "0195c3a1-7b2e-7c4d-9e5f-00000000000b";
        private const string IdC = "0195c3a1-7b2e-7c4d-9e5f-00000000000c";

        /// <summary>What may follow a value and is not whitespace.</summary>
        private static readonly string[] Tails =
        {
            " {\"hidden\": true}", "{\"hidden\": true}", " x", "}", " /* a comment */", " // a comment", " ", "\0",
            "\n\n{}", "\f", "\v",
        };

        /// <summary>JSON's whitespace, and nothing else, after the value: read as before.</summary>
        private static readonly string[] Whitespace = { "", "\n", "\n\n", "\r\n", " \t\r\n ", "\r" };

        private const string Explains = "nothing after it but whitespace";

        private string _root;
        private readonly List<OWWorldCache> _caches = new List<OWWorldCache>();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ow-onevalue-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var cache in _caches) UnityEngine.Object.DestroyImmediate(cache);
            _caches.Clear();
            if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static string Wire(string id, string name)
            => "{\"id\":\"" + id + "\",\"type\":\"character\",\"name\":\"" + name + "\"}";

        private static string FileText(string id, string name)
            => "{\n  \"id\": \"" + id + "\",\n  \"type\": \"character\",\n  \"name\": \"" + name + "\"\n}\n";

        private static string Shown(string tail) => string.Concat(tail.Select(c => c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString()));

        /// <summary>A world folder of three characters, the middle one's file ending in <paramref name="tail"/>.</summary>
        private string Folder(string tag, string tail, string worldTail = "")
        {
            var dir = Path.Combine(_root, tag);
            var characters = Path.Combine(dir, "elements", "character");
            Directory.CreateDirectory(characters);
            File.WriteAllText(Path.Combine(dir, "world.json"),
                "{\n  \"id\": \"" + WorldId + "\",\n  \"name\": \"One\"\n}" + worldTail, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "a--0000000a.json"), FileText(IdA, "A"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "b--0000000b.json"), Wire(IdB, "B") + tail, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(characters, "c--0000000c.json"), FileText(IdC, "C"), new UTF8Encoding(false));
            return dir;
        }

        private static string[] Ids(OWFolderReadResult read) => read.Elements.Select(e => e.Id).ToArray();

        private static void AssertRefusal(Exception e, string tail)
        {
            Assert.IsInstanceOf<OWContentAfterValueException>(e, Shown(tail) + ": " + e);
            Assert.IsInstanceOf<JsonReaderException>(e, "a caller that catches a JsonException today still catches it");
            StringAssert.Contains(Explains, e.Message);
        }

        private sealed class Answers : IOWTransport
        {
            public string Body;

            public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
                => Task.FromResult(new OWResponse { Status = 200, Body = Body, Headers = new Dictionary<string, string>() });
        }

        private static OWClient Client(string body) => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "pin-for-tests",
            Transport = new Answers { Body = body },
            BaseUrl = "https://example.test/api/v2",
        });

        // -- The boundary -----------------------------------------------------------------------------------------

        [Test]
        public void EveryReader_RefusesContentAfterTheValue()
        {
            foreach (var tail in Tails)
            {
                var text = Wire(IdB, "B") + tail;
                AssertRefusal(Assert.Catch(() => OWJson.ParseObject(text), Shown(tail)), tail);
                AssertRefusal(Assert.Catch(() => OWJson.Deserialize<JObject>(text), Shown(tail)), tail);
                AssertRefusal(Assert.Catch(() => OWJson.Deserialize<OWCharacter>(text), Shown(tail)), tail);
                AssertRefusal(Assert.Catch(() => OWJson.ParseToken(text), Shown(tail)), tail);
                AssertRefusal(Assert.Catch(() => OWFolderWriter.Parse(text), Shown(tail)), tail);
            }

            // A value that is not an object, as the token reader and the serializer read one.
            AssertRefusal(Assert.Catch(() => OWJson.ParseToken("7 8")), " 8");
            AssertRefusal(Assert.Catch(() => OWJson.ParseToken("\"a\" \"b\"")), " \"b\"");
            AssertRefusal(Assert.Catch(() => OWJson.Deserialize<List<int>>("[1] [2]")), " [2]");
        }

        [Test]
        public void Whitespace_AfterTheValue_ReadsAsBefore()
        {
            foreach (var tail in Whitespace)
            {
                var text = Wire(IdB, "B") + tail;
                Assert.AreEqual(IdB, (string)OWJson.ParseObject(text)["id"], Shown(tail));
                Assert.AreEqual(IdB, (string)OWJson.Deserialize<JObject>(text)["id"], Shown(tail));
                Assert.AreEqual(IdB, OWJson.Deserialize<OWCharacter>(text).Id, Shown(tail));
                Assert.AreEqual(IdB, (string)OWJson.ParseToken(text)["id"], Shown(tail));
            }

            Assert.AreEqual(7, (int)OWJson.ParseToken("7\n"));
            Assert.AreEqual("a", (string)OWJson.ParseToken("\"a\" "));
            Assert.IsNull(OWJson.Deserialize<JObject>(""), "an empty body deserializes to nothing, as before");
            Assert.IsNull(OWJson.Deserialize<JObject>(" \n"), "a blank body deserializes to nothing, as before");
        }

        [Test]
        public void TheRefusal_SaysWhereTheContentStarts()
        {
            var e = Assert.Catch(() => OWJson.ParseObject("{\n  \"id\": \"x\"\n}\n\n  {\"hidden\": true}\n"));
            AssertRefusal(e, "second value");
            var refusal = (OWContentAfterValueException)e;
            Assert.AreEqual(5, refusal.LineNumber);
            Assert.AreEqual(3, refusal.LinePosition);
            StringAssert.Contains("line 5, position 3", refusal.Message);
        }

        [Test]
        public void OtherFaults_AreNotThisRefusal()
        {
            Assert.IsNotInstanceOf<OWContentAfterValueException>(Assert.Catch<JsonReaderException>(() => OWJson.ParseObject("{\"n\":7")));
            Assert.IsNotInstanceOf<OWContentAfterValueException>(Assert.Catch<JsonReaderException>(() => OWJson.ParseObject("{\"n\":nope}")));
            Assert.IsInstanceOf<OWNumberOutOfRangeException>(Assert.Catch(() => OWJson.ParseObject("{\"n\":1e400} x")),
                "the number comes first in the text, so its refusal is the one thrown");
        }

        // -- The folder and the archive ---------------------------------------------------------------------------

        [Test]
        public void Folder_SkipsTheFile_ByName_AndServesTheRest()
        {
            for (var i = 0; i < Tails.Length; i++)
            {
                var read = OWFolderReader.Read(Folder("f" + i, Tails[i]));
                CollectionAssert.AreEqual(new[] { IdA, IdC }, Ids(read), Shown(Tails[i]));
                Assert.AreEqual("b--0000000b.json", Path.GetFileName(read.Skipped.Single().Path));
                StringAssert.StartsWith("content after the JSON value", read.Skipped.Single().Reason);
                StringAssert.Contains(Explains, read.Skipped.Single().Reason);
            }

            foreach (var tail in Whitespace)
                CollectionAssert.AreEqual(new[] { IdA, IdB, IdC }, Ids(OWFolderReader.Read(Folder("ws" + Whitespace.ToList().IndexOf(tail), tail))), Shown(tail));
        }

        [Test]
        public void WorldJson_WithContentAfterIt_RefusesTheWorld_ByName()
        {
            foreach (var tail in new[] { " {\"hidden\": true}", " /* c */", " " })
            {
                var dir = Folder("w" + (int)tail[tail.Length - 1], "", tail);
                var e = Assert.Throws<OWFolderFormatException>(() => OWFolderReader.Read(dir), Shown(tail));
                StringAssert.Contains("content after the JSON value", e.Message);
                StringAssert.DoesNotContain("not valid JSON", e.Message);
                AssertRefusal(e.InnerException, tail);

                var pack = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Pack(dir, Path.Combine(_root, "p.owworld")), Shown(tail));
                StringAssert.Contains("content after the JSON value", pack.Message);
            }
        }

        [Test]
        public void Archive_SkipsTheFile_ByName_AndServesTheRest()
        {
            for (var i = 0; i < Tails.Length; i++)
            {
                var archive = Path.Combine(_root, "a" + i + ".owworld");
                OWWorldArchive.Pack(Folder("a" + i, Tails[i]), archive);
                var read = OWWorldArchive.Read(archive);
                CollectionAssert.AreEqual(new[] { IdA, IdC }, Ids(read), Shown(Tails[i]));
                Assert.AreEqual("elements/character/b--0000000b.json", read.Skipped.Single().Path);
                StringAssert.StartsWith("content after the JSON value", read.Skipped.Single().Reason);
            }
        }

        [Test]
        public void ExportFile_WithContentAfterIt_IsRefused_ByName()
        {
            foreach (var tail in new[] { " {\"hidden\": true}", " /* c */", "\0" })
            {
                var envelope = "{\"format\":\"onlyworlds-world-export\",\"schema_version\":\"00.32.00\",\"exported_at\":\"2026-11-23T00:00:00Z\","
                               + "\"world\":{\"id\":\"" + WorldId + "\",\"name\":\"One\"},\"elements\":{\"character\":["
                               + Wire(IdA, "A") + "]}}" + tail;
                var e = Assert.Throws<OWFolderFormatException>(() => OWEnvelopeReader.Parse(envelope, "export.json"), Shown(tail));
                StringAssert.Contains("export.json is refused, content after the JSON value", e.Message);
                AssertRefusal(e.InnerException, tail);
            }

            Assert.AreEqual(1, OWEnvelopeReader.Parse("{\"format\":\"onlyworlds-world-export\",\"world\":{\"id\":\"" + WorldId
                + "\",\"name\":\"One\"},\"elements\":{\"character\":[" + Wire(IdA, "A") + "]}}\n\n", "export.json").Elements.Count);
        }

        [Test]
        public void FolderWriter_ReadJsonFile_RefusesIt()
        {
            var dir = Folder("rj", " {\"hidden\": true}");
            AssertRefusal(Assert.Catch(() => OWFolderWriter.ReadJsonFile(Path.Combine(dir, "elements", "character", "b--0000000b.json"))), "second value");
        }

        // -- The cache --------------------------------------------------------------------------------------------

        private OWWorldCache CacheWithB()
        {
            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            cache.Initialize(OWWorldKey.FromApi("w-1"), "One");
            _caches.Add(cache);
            cache.Upsert(IdA, "character", Wire(IdA, "A"));
            cache.Upsert(IdB, "character", Wire(IdB, "B").Replace("}", ",\"known_of\":true}") + " {\"hidden\": true}");
            cache.Upsert(IdC, "character", Wire(IdC, "C"));
            return cache;
        }

        [Test]
        public void Cache_ARawBodyWithContentAfterIt_IsNoAnswerOfFalse()
        {
            var cache = CacheWithB();
            AssertRefusal(Assert.Catch(() => cache.IsKnownOf(IdB)), "second value");
        }

        [Test]
        public void Cache_ARawBodyWithContentAfterIt_IsPassedOver_AndSaysSo()
        {
            var cache = CacheWithB();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("Could not deserialize cached element .*content after the JSON value"));
            CollectionAssert.AreEqual(new[] { "A", "C" }, cache.All<OWCharacter>("character").Select(c => c.Name).ToArray());
        }

        // -- The client -------------------------------------------------------------------------------------------

        [Test]
        public void Client_AResponseWithContentAfterIt_ThrowsTheRefusal_NamingTheRequest()
        {
            foreach (var tail in new[] { " {\"hidden\": true}", " /* c */", " " })
            {
                var one = Assert.CatchAsync(() => Client(Wire(IdB, "B") + tail).GetAsync<JObject>("character", IdB));
                AssertRefusal(one, tail);
                StringAssert.Contains("GET /character/" + IdB, one.Message);

                var page = "{\"data\":[" + Wire(IdA, "A") + "],\"has_more\":false,\"next_cursor\":null}" + tail;
                AssertRefusal(Assert.CatchAsync(() => Client(page).ListAllAsync<JObject>("character")), tail);
                AssertRefusal(Assert.CatchAsync(() => Client(page).ListAsync<OWCharacter>("character")), tail);
                AssertRefusal(Assert.CatchAsync(() => Client(Wire(IdB, "B") + tail).RequestAsync("GET", "/character/" + IdB)), tail);
            }
        }
    }
}
