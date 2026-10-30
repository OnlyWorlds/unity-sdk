using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The lens profile's shared edge cases: inputs the conformance fixture leaves open (a known-of stub's links, the
    /// window's edge, the world's <c>time_current</c> or <c>time_range_current</c> read as its now, no window without a
    /// now, a <c>to</c> after <c>as_of</c>, the fence on anchors, the members list in the mail rule, a message not
    /// repeated among the kept, the thirty days' edge, ties by id by code point, an umbrella's stub child, a stub message
    /// in the mail part, the notes a reader may count as ignored), answers written by hand. The file is the TypeScript
    /// SDK's <c>test/fixtures/lens-edges.json</c> from release 4.98.0 (commit e9622d2), the same bytes the Python package
    /// (onlyworlds 0.80.0) holds, vendored in
    /// <c>Tests/Editor/Fixtures/lens/edges.json</c>. Each case names its reader and its world; the readers run here as
    /// they run in the TypeScript SDK's replay (<c>test/lens.test.mjs</c>), which reads the world's <c>now</c> (else its
    /// <c>time_current</c>, else <c>time_range_current</c>) and its members off the world itself. When the file moves,
    /// re-vendor it and update the hash and the count below.
    /// </summary>
    public class OWLensEdgeTests
    {
        /// <summary>The edge file's sha256: <c>test/fixtures/lens-edges.json</c> in the TypeScript SDK 4.98.0 (e9622d2).</summary>
        private const string EdgesSha256 = "4cb440cde85da877e2b5ba7129dd34be90a1e0bb109eefb56f0cae040a19b080";

        private const int EdgeCount = 15;

        private static JArray _edges;

        private static string EdgesPath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "lens", "edges.json");
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
                yield return new TestCaseData(string.Empty).SetName("LensEdge_FileNotFound");
                yield break;
            }

            foreach (var edge in edges.Cast<JObject>())
            {
                var name = (string)edge["name"];
                yield return new TestCaseData(name).SetName("LensEdge_" + name.Replace('-', '_'));
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
                    "the vendored edge file is not the TypeScript SDK 4.98.0's: re-vendor it (a checkout's line endings change the bytes)");
            }

            Assert.AreEqual(EdgeCount, Edges.Count);
        }

        [TestCaseSource(nameof(Cases))]
        public void TheEdgeCase_ReadsAsTheFileWritesIt(string name)
        {
            var edge = Edges.Cast<JObject>().FirstOrDefault(e => (string)e["name"] == name);
            Assert.IsNotNull(edge, "no edge case named " + name);
            var want = (JObject)edge["want"];
            var got = Answer(edge);
            // Only the keys the case writes in `want` are compared, as the TypeScript replay compares them; a `mail`
            // part compares only the sub-keys the case writes.
            foreach (var key in want.Properties())
            {
                var gotValue = got[key.Name];
                if (key.Name == "mail" && key.Value is JObject wantMail && gotValue is JObject gotMail)
                {
                    foreach (var m in wantMail.Properties())
                        Assert.IsTrue(JToken.DeepEquals(m.Value, gotMail[m.Name]),
                            name + ": mail." + m.Name + ": gave " + (gotMail[m.Name]?.ToString(Formatting.None) ?? "null")
                            + ", want " + m.Value.ToString(Formatting.None));
                    continue;
                }

                Assert.IsTrue(JToken.DeepEquals(key.Value, gotValue),
                    name + "." + key.Name + ": gave " + (gotValue?.ToString(Formatting.None) ?? "null")
                    + ", want " + key.Value.ToString(Formatting.None));
            }
        }

        /// <summary>One case's answer, as the TypeScript SDK's replay gives it: resolve the lens over the case's world.</summary>
        private static JObject Answer(JObject e)
        {
            var world = ((JArray)e["world"]["elements"]).Cast<JToken>().ToList();
            // A case names its lens as a body, or by id (a lens the reader reads in the world, as the umbrella cases do).
            var reader = ReaderOf((JObject)e["reader"]);
            var result = e["lens"] is JObject body
                ? OWLens.Resolve(world, body, reader, OptionsOf(e))
                : OWLens.Resolve(world, (string)e["lens"], reader, OptionsOf(e));
            return Json(result);
        }

        /// <summary>A reader as the file writes it: its rung, a guest's lists, its character.</summary>
        private static OWLensReader ReaderOf(JObject r)
        {
            var character = r["character"]?.Type == JTokenType.String ? (string)r["character"] : null;
            switch ((string)r["rung"])
            {
                case "owner": return OWLensReader.Owner(character);
                case "contributor": return OWLensReader.Contributor(character);
                case "public": return OWLensReader.Public();
                case "guest": return OWLensReader.Guest(Strings(r["whole"]), Strings(r["known_of"]), character);
                case "served": return OWLensReader.Served(character);
                default: throw new ArgumentException("an edge reader of unknown rung: " + r["rung"]);
            }
        }

        /// <summary>
        /// The reading's moment and facts, read off the world as the TypeScript replay reads them: the case's
        /// <c>as_of</c>, the world's <c>now</c> (else its <c>time_current</c>) as the fence's edge, the case's
        /// <c>clock</c>, the world's members list.
        /// </summary>
        internal static OWLensOptions OptionsOf(JObject e)
        {
            var world = (JObject)e["world"];
            return new OWLensOptions
            {
                AsOf = Whole(e["as_of"]),
                Now = Whole(world["now"]) ?? Whole(world["time_current"]) ?? Whole(world["time_range_current"]),
                Clock = e["clock"]?.Type == JTokenType.String
                    ? DateTimeOffset.Parse((string)e["clock"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                    : (DateTimeOffset?)null,
                Members = world["members"] as JArray,
            };
        }

        /// <summary>A result in the file's words: kept, cut (a reason with no count absent), rows, notes_ignored, mail.</summary>
        internal static JObject Json(OWLensResult r)
        {
            var o = new JObject
            {
                ["kept"] = new JArray(r.Kept),
                ["cut"] = Counted(r.Cut),
                ["rows"] = new JArray(r.Rows.Select(x => x.DeepClone())),
                ["notes_ignored"] = r.NotesIgnored,
            };
            if (r.Mail != null)
            {
                o["mail"] = new JObject
                {
                    ["unread"] = new JArray(r.Mail.Unread),
                    ["read"] = new JArray(r.Mail.Read),
                    ["rows"] = new JArray(r.Mail.Rows.Select(x => x.DeepClone())),
                    ["cut"] = Counted(r.Mail.Cut),
                    ["totals"] = new JObject { ["unread"] = r.Mail.TotalUnread, ["read"] = r.Mail.TotalRead },
                };
            }

            return o;
        }

        private static JObject Counted(IReadOnlyDictionary<string, int> cut)
        {
            var o = new JObject();
            foreach (var pair in cut.Where(p => p.Value > 0)) o[pair.Key] = pair.Value;
            return o;
        }

        /// <summary>A JSON integer as an int; null for a missing key, a null, or a value that is not a whole number.</summary>
        private static int? Whole(JToken token) =>
            token is JValue v && v.Type == JTokenType.Integer ? (int?)(long)v : null;

        private static List<string> Strings(JToken token) =>
            token is JArray array ? array.Select(t => (string)t).ToList() : new List<string>();
    }
}
