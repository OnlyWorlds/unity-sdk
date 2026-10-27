using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The message profile's shared edge cases: inputs the official fixture leaves open (what a Note is to every
    /// reader, the <c>created_at</c> order and its readable rule, an edit, a Character's line), answers written by
    /// hand. The file is the TypeScript SDK's <c>test/fixtures/message-edges.json</c> from release 4.88.0 (commit
    /// d8c9514), the same bytes the Python package holds, vendored in <c>Tests/Editor/Fixtures/message/edges.json</c>.
    /// Each case names its reader; the readers run here as they run in the TypeScript SDK's replay
    /// (<c>test/message.test.mjs</c>). When the file moves, re-vendor it and update the hash and the count below.
    /// </summary>
    public class OWMessageEdgeTests
    {
        /// <summary>The edge file's sha256: <c>test/fixtures/message-edges.json</c> in the TypeScript SDK 4.88.0 (d8c9514).</summary>
        private const string EdgesSha256 = "83a1d2d5c447bb4b95b13133f81300d458c2b30aa24e3396750f235f44ef94b8";

        private const int EdgeCount = 26;

        private static JArray _edges;

        private static string EdgesPath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "message", "edges.json");
            }
        }

        private static JArray Edges
        {
            get
            {
                if (_edges != null) return _edges;
                var path = EdgesPath;
                if (path == null || !File.Exists(path)) return null;
                // Dates stay text, as the readers receive them off the wire (see OWJson.ParseObject).
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    return _edges = JArray.Load(reader);
                }
            }
        }

        /// <summary>One test per edge case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var edges = Edges;
            if (edges == null)
            {
                yield return new TestCaseData(string.Empty).SetName("MessageEdge_FileNotFound");
                yield break;
            }

            foreach (var edge in edges.Cast<JObject>())
            {
                var name = (string)edge["name"];
                yield return new TestCaseData(name).SetName("MessageEdge_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheEdgeFile_IsTheTypeScriptSdksBytes_AndHoldsItsCases()
        {
            var path = EdgesPath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored edge file was not found: " + path);
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                Assert.AreEqual(EdgesSha256, hash,
                    "the vendored edge file is not the TypeScript SDK 4.88.0's: re-vendor it (a checkout's line endings change the bytes)");
            }

            Assert.AreEqual(EdgeCount, Edges.Count);
        }

        [TestCaseSource(nameof(Cases))]
        public void TheEdgeCase_ReadsAsTheFileWritesIt(string name)
        {
            var edge = Edges.Cast<JObject>().FirstOrDefault(e => (string)e["name"] == name);
            Assert.IsNotNull(edge, "no edge case named " + name);
            var got = Answer(edge);
            Assert.IsTrue(JToken.DeepEquals(edge["want"], got),
                name + ": gave " + got.ToString(Formatting.None) + ", want " + edge["want"].ToString(Formatting.None));
        }

        /// <summary>One case's answer, from the reader the case names, as the TypeScript SDK's replay gives it.</summary>
        private static JToken Answer(JObject e)
        {
            var reader = (string)e["reader"];
            switch (reader)
            {
                case "recipients":
                    return new JArray(OWMessage.RecipientsOf((JObject)e["message"]));
                case "addressed":
                    return new JValue(OWMessage.IsAddressedTo((JObject)e["message"], (string)e["character"]));
                case "everyone":
                    return new JValue(OWMessage.IsToEveryone((JObject)e["message"], (bool)e["keyless"]));
                case "unseen":
                {
                    var unseen = OWMessage.HasUnseenRecipients((JObject)e["message"], (bool)e["keyless"]);
                    return unseen.HasValue ? new JValue(unseen.Value) : JValue.CreateNull();
                }
                case "unread":
                    return Ids(OWMessage.UnreadFor((JArray)e["messages"], (string)e["reader_id"], null, Members(e)));
                case "thread":
                    return Ids(OWMessage.ThreadOf((JArray)e["messages"], (string)e["root"]));
                case "edited":
                {
                    var edited = OWMessage.EditedBy((JObject)e["message"], Members(e));
                    return new JObject
                    {
                        ["edited"] = edited.Edited,
                        ["by"] = edited.By == null ? JValue.CreateNull() : new JValue(edited.By),
                    };
                }
                case "note":
                {
                    var line = OWMessage.NoteOf((JArray)e["notes"], (string)e["character"], Members(e));
                    return line == null ? JValue.CreateNull() : new JValue(line);
                }
                default:
                    throw new InvalidOperationException("an edge of unknown reader " + reader);
            }
        }

        /// <summary>The case's members list; null when the case has none or says null (an unreadable list).</summary>
        private static IEnumerable<JToken> Members(JObject e) => e["members"] as JArray;

        private static JArray Ids(IEnumerable<JObject> list) => new JArray(list.Select(m => (string)m["id"]));
    }
}
