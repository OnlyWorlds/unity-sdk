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
    /// The message profile's conformance fixture (schema-dist <c>profiles/message.fixture.json</c>, vendored byte for
    /// byte in <c>Tests/Editor/Fixtures/message/</c> from dist.75), read case by case through <see cref="OWMessage"/>'s
    /// readers, and its pair and read mark held equal to <see cref="OWMessage.Pair"/> and the read mark's constants. The
    /// TypeScript SDK and the Python package run the same cases; the tests beside the fixture are the TypeScript SDK's
    /// (test/message.test.mjs, 4.81.0) and the Python package's (tests/test_message.py, 0.64.0). When the dist moves
    /// the fixture, re-vendor it and update the hash below.
    /// </summary>
    public class OWMessageTests
    {
        /// <summary>The fixture's sha256 as dist.75's MANIFEST.json records it.</summary>
        private const string DistSha256 = "b662f4fc370ab0f1fb2a9dfc48a141fb59febf761103061d0147f4c96edd0541";

        /// <summary>The fixture's cases by kind, as dist.75 holds them (55 in all).</summary>
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>
        {
            ["pair"] = 9, ["addressed"] = 9, ["sent_by"] = 3, ["sender"] = 10, ["unread"] = 12, ["mark"] = 9, ["thread"] = 3,
        };

        /// <summary>The keys of a case that hold its answer, by kind.</summary>
        private static readonly Dictionary<string, string[]> ExpectedKeys = new Dictionary<string, string[]>
        {
            ["pair"] = new[] { "is_message" },
            ["addressed"] = new[] { "addressed" },
            ["sent_by"] = new[] { "sent_by" },
            ["sender"] = new[] { "status", "claimed", "written_by" },
            ["mark"] = new[] { "mark" },
            ["unread"] = new[] { "unread" },
            ["thread"] = new[] { "thread" },
        };

        private const string Ada = "019a3b10-0000-7000-8000-0000000000a1";
        private const string Mara = "019a3b10-0000-7000-8000-0000000000a2";
        private const string Wren = "019a3b10-0000-7000-8000-0000000000a3";

        private static JObject _fixture;

        /// <summary>
        /// The package's own resolved path; outside the Editor (no package manager to ask), the working directory's
        /// <c>Packages/com.onlyworlds.sdk</c>, as <c>OWRoomTests</c> finds its fixture.
        /// </summary>
        private static string PackageRoot()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWMessageTests).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }

        private static string FixturePath
        {
            get
            {
                var root = PackageRoot();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "message", "message.fixture.json");
            }
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = FixturePath;
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        /// <summary>One case per fixture case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Message_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Message_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored message fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.75's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void ThePair_IsTheFixtures_AndOneOfTheRoomPairs_AndTheReadMarkIsTheFixtures()
        {
            Assert.IsNotNull(Fixture, "the vendored message fixture was not found");
            Assert.AreEqual("message", (string)Fixture["profile"]);
            Assert.AreEqual(((string)Fixture["pair"]["type"], (string)Fixture["pair"]["supertype"]), OWMessage.Pair);
            Assert.IsTrue(OWRoom.Pairs.Contains(OWMessage.Pair), "the profile calls the message pair a line in the room");

            var mark = Fixture["read_mark"];
            Assert.AreEqual(((string)mark["type"], (string)mark["supertype"]), OWMessage.ReadMarkPair);
            Assert.AreEqual((string)mark["reader_field"], OWMessage.ReaderField);
            Assert.AreEqual((string)mark["messages_field"], OWMessage.MessagesField);
        }

        [Test]
        public void TheFixture_HoldsDist75sCases()
        {
            Assert.IsNotNull(Fixture, "the vendored message fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(55, cases.Count, "dist.75's fixture holds 55 cases");
            var counts = cases.GroupBy(c => (string)c["kind"]).ToDictionary(g => g.Key, g => g.Count());
            CollectionAssert.AreEquivalent(Counts, counts);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored message fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var kind = (string)c["kind"];
            Assert.IsTrue(ExpectedKeys.ContainsKey(kind), $"case {name}: a case of unknown kind {kind}");

            var want = new JObject();
            foreach (var key in ExpectedKeys[kind])
            {
                if (c.TryGetValue(key, out var value)) want[key] = value.DeepClone();
            }

            var got = Answer(c);
            Assert.IsTrue(JToken.DeepEquals(want, got),
                $"case {kind}/{name}: gave {got.ToString(Newtonsoft.Json.Formatting.None)}, the fixture says {want.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        /// <summary>One case's answer as the fixture writes it, from the package's readers.</summary>
        private static JObject Answer(JObject c)
        {
            var members = c.TryGetValue("members", out var own) ? own as JArray : Fixture["members"] as JArray;
            switch ((string)c["kind"])
            {
                case "pair":
                    return new JObject { ["is_message"] = OWMessage.IsMessage((JObject)c["element"]) };
                case "addressed":
                    return new JObject { ["addressed"] = OWMessage.IsAddressedTo((JObject)c["message"], (string)c["character"]) };
                case "sent_by":
                    // Sent by X is the claim: narrator, which SenderStatus hands back as Claimed.
                    return new JObject { ["sent_by"] = OWMessage.IsSentBy((JObject)c["message"], (string)c["character"]) };
                case "sender":
                {
                    var s = OWMessage.SenderStatus((JObject)c["message"], members);
                    // A null string converts to a string-typed JValue, which a JSON null does not equal: write null as null.
                    var got = new JObject
                    {
                        ["status"] = Word(s.Status),
                        ["claimed"] = s.Claimed == null ? JValue.CreateNull() : new JValue(s.Claimed),
                    };
                    if (s.WrittenBy != null) got["written_by"] = s.WrittenBy;
                    return got;
                }
                case "mark":
                {
                    var mark = OWMessage.ReadMarkOf((JArray)c["relations"], (string)c["reader"], members);
                    return new JObject { ["mark"] = mark == null ? JValue.CreateNull() : mark["id"].DeepClone() };
                }
                case "unread":
                    return new JObject
                    {
                        ["unread"] = Ids(OWMessage.UnreadFor((JArray)c["messages"], (string)c["reader"], c["read_mark"] as JObject, members)),
                    };
                case "thread":
                    return new JObject { ["thread"] = Ids(OWMessage.ThreadOf((JArray)c["messages"], (string)c["root"])) };
                default:
                    throw new InvalidOperationException("a case of unknown kind " + c["kind"]);
            }
        }

        private static string Word(OWSenderStatus status) => status.ToString().ToLowerInvariant();

        private static JArray Ids(IEnumerable<JObject> list) => new JArray(list.Select(m => m["id"].DeepClone()));

        private static List<string> IdList(IEnumerable<JObject> list) => list.Select(m => (string)m["id"]).ToList();

        // -- Beside the fixture (the TypeScript SDK's and the Python package's own tests) ------

        /// <summary>A body from JSON written with single quotes, read without coercing a value.</summary>
        private static JObject J(string json) => OWJson.ParseObject(json.Replace('\'', '"'));

        /// <summary>A body whose <c>characters</c> is the value given.</summary>
        private static JObject To(JToken characters) => new JObject { ["characters"] = characters };

        [Test]
        public void RecipientsOf_GivesTheIdsAsWritten_AndEmptyIsEveryone()
        {
            CollectionAssert.AreEqual(new[] { Ada }, OWMessage.RecipientsOf(To(new JArray(Ada))));
            CollectionAssert.IsEmpty(OWMessage.RecipientsOf(To(JValue.CreateNull())));
            CollectionAssert.IsEmpty(OWMessage.RecipientsOf(new JObject()));
            CollectionAssert.IsEmpty(OWMessage.RecipientsOf(null));
            Assert.IsTrue(OWMessage.IsAddressedTo(To(new JArray()), Ada));
        }

        [Test]
        public void Recipients_KeepTheirOrder_AndAMalformedListIsNotEveryone()
        {
            CollectionAssert.AreEqual(new[] { Mara, Ada }, OWMessage.RecipientsOf(To(new JArray(Mara, Ada))));
            CollectionAssert.IsEmpty(OWMessage.RecipientsOf(new JObject()));
            CollectionAssert.IsEmpty(OWMessage.RecipientsOf(To(Ada)));

            var malformed = new JToken[] { Ada, new JArray(""), new JArray(5), new JObject { ["0"] = Ada } };
            foreach (var characters in malformed)
                Assert.IsFalse(OWMessage.IsAddressedTo(To(characters), Ada), characters.ToString());
            foreach (var characters in new JToken[] { new JArray(), JValue.CreateNull() })
                Assert.IsTrue(OWMessage.IsAddressedTo(To(characters), Ada), characters.ToString());
            Assert.IsTrue(OWMessage.IsAddressedTo(new JObject(), Ada));
            Assert.IsFalse(OWMessage.IsAddressedTo(To(new JArray()), ""));

            Assert.IsTrue(OWMessage.IsSentBy(J("{'narrator': '" + Ada + "'}"), Ada));
            Assert.IsFalse(OWMessage.IsSentBy(J("{'narrator': '" + Ada + "'}"), Mara));
            Assert.IsFalse(OWMessage.IsSentBy(new JObject(), ""));

            // A list of no valid id is addressed to nobody: not everyone, though RecipientsOf gives none for it.
            foreach (var characters in new JToken[] { Ada, new JArray(""), new JArray(5), new JArray(Ada) })
                Assert.IsFalse(OWMessage.IsToEveryone(To(characters)), characters.ToString());
            foreach (var m in new[] { new JObject(), To(JValue.CreateNull()), To(new JArray()) })
                Assert.IsTrue(OWMessage.IsToEveryone(m), m.ToString());
            Assert.IsFalse(OWMessage.IsToEveryone(null));
        }

        [Test]
        public void TheReaders_OrderByMicroseconds_AndALoopInAChainEndsTheWalk()
        {
            var msg = J("{'id': 'm', 'type': 'narrative', 'name': 'Hello', 'supertype': ' MESSAGE ', 'narrator': 'a', " +
                        "'characters': [], 'created_at': '2026-10-26T09:40:00.000100Z'}");
            var later = (JObject)msg.DeepClone();
            later["id"] = "n";
            later["created_at"] = "2026-10-26T09:40:00.000050Z";
            Assert.IsTrue(OWMessage.IsMessage(msg));
            // Microseconds order too: 50 before 100, though both are the same millisecond.
            CollectionAssert.AreEqual(new[] { "n", "m" }, IdList(OWMessage.UnreadFor(new[] { msg, later }, "b", null, null)));

            JObject Link(string id, string parent)
            {
                var m = (JObject)msg.DeepClone();
                m["id"] = id;
                m["parent_narrative"] = parent;
                return m;
            }

            var loop = new[] { Link("x", "y"), Link("y", "x") };
            CollectionAssert.IsEmpty(OWMessage.ThreadOf(loop, "r"), "a loop in the chain ends the walk");
            CollectionAssert.AreEqual(new[] { "x", "y" }, IdList(OWMessage.ThreadOf(loop, "x")), "a loop through the root reaches it");
        }

        [Test]
        public void AThreadWhoseParentsLoop_Ends()
        {
            var loop = new[]
            {
                J("{'id': 'a', 'parent_narrative': 'b', 'created_at': '2026-10-26T09:00:00Z'}"),
                J("{'id': 'b', 'parent_narrative': 'a', 'created_at': '2026-10-26T09:01:00Z'}"),
                J("{'id': 'c', 'parent_narrative': 'a', 'created_at': '2026-10-26T09:02:00Z'}"),
            };
            CollectionAssert.IsEmpty(OWMessage.ThreadOf(loop, "r"));
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, IdList(OWMessage.ThreadOf(loop, "a")));
        }

        [Test]
        public void ThePair_IsTrimmedAndFoldedAsTheRoomProfile()
        {
            var longS = char.ConvertFromUtf32(0x17F); // simple-folds to "s"
            Assert.IsTrue(OWMessage.IsMessage(new JObject { ["type"] = "NARRATIVE", ["supertype"] = "　Me" + longS + longS + "age\t" }));
            // U+001C is not White_Space, and a byte order mark stays: neither is trimmed.
            Assert.IsFalse(OWMessage.IsMessage(new JObject { ["type"] = "narrative", ["supertype"] = "\u001CMessage" }));
            Assert.IsFalse(OWMessage.IsMessage(new JObject { ["type"] = "narrative", ["supertype"] = "﻿Message" }));
            Assert.IsFalse(OWMessage.IsMessage(new JObject { ["supertype"] = "Message" }));
            Assert.IsFalse(OWMessage.IsMessage(null));
        }

        [Test]
        public void TheReadMarkSupertypeIsFolded_AndAMarkWithNoMembersListHasNoHand()
        {
            var members = (JArray)Fixture["members"];
            var mark = J("{'id': 'm1', 'type': 'Relation', 'supertype': ' read ', 'actor': '" + Ada + "', 'narratives': [], 'created_by': null}");
            Assert.AreSame(mark, OWMessage.ReadMarkOf(new[] { mark }, Ada, members));
            Assert.IsNull(OWMessage.ReadMarkOf(new[] { mark }, Ada, null), "created_by is there, and nothing can check it");
            var reads = (JObject)mark.DeepClone();
            reads["supertype"] = "Reads";
            Assert.IsNull(OWMessage.ReadMarkOf(new[] { reads }, Ada, members));
        }

        [Test]
        public void TheNewestMarkWins_WhereverItStandsInTheList()
        {
            // The fixture lists the newer mark first; a reader that took the first it met would pass it. Here the newest
            // stands last, a readable time beats an unreadable one, and equal times keep the first in the list.
            JObject Mark(string id, string updatedAt) =>
                J("{'id': '" + id + "', 'type': 'relation', 'supertype': 'Read', 'actor': '" + Ada + "', 'narratives': []" +
                  (updatedAt == null ? "" : ", 'updated_at': '" + updatedAt + "'") + "}");

            var older = Mark("old", "2026-10-26T08:00:00Z");
            var newer = Mark("new", "2026-10-26T11:00:00Z");
            var undated = Mark("undated", null);
            var twin = Mark("twin", "2026-10-26T11:00:00Z");
            Assert.AreEqual("new", (string)OWMessage.ReadMarkOf(new[] { older, newer }, Ada, null)["id"]);
            Assert.AreEqual("new", (string)OWMessage.ReadMarkOf(new[] { undated, older, newer }, Ada, null)["id"]);
            Assert.AreEqual("old", (string)OWMessage.ReadMarkOf(new[] { older, undated }, Ada, null)["id"]);
            Assert.AreEqual("new", (string)OWMessage.ReadMarkOf(new[] { newer, twin }, Ada, null)["id"]);
        }

        [Test]
        public void AMarkCountsOnlyWhenItsLastWriterIsItsAuthor()
        {
            var members = (JArray)Fixture["members"];
            JObject Mark(string hands) =>
                J("{'id': 'm', 'type': 'relation', 'supertype': 'Read', 'actor': '" + Ada + "', 'narratives': []" + hands + "}");

            // The owner (null) plays Ada in the fixture's members list: made and last changed by the same hand.
            Assert.IsNotNull(OWMessage.ReadMarkOf(new[] { Mark(", 'created_by': null, 'changed_by': null") }, Ada, members));
            // A last writer with no author beside it has nothing to equal: ignored, though a folder's mark with neither counts.
            Assert.IsNull(OWMessage.ReadMarkOf(new[] { Mark(", 'changed_by': null") }, Ada, members));
            Assert.IsNotNull(OWMessage.ReadMarkOf(new[] { Mark("") }, Ada, members));
            // Changed since by another membership: ignored.
            Assert.IsNull(OWMessage.ReadMarkOf(new[] { Mark(", 'created_by': null, 'changed_by': 7") }, Ada, members));
        }

        [Test]
        public void OnTheWire_AReaderWithNoMembersList_ProvesNoneOfItsOwnMessages()
        {
            var wire = J("{'id': 'w', 'narrator': '" + Ada + "', 'characters': [], 'created_by': null, 'created_at': '2026-10-26T09:00:00Z'}");
            var folder = J("{'id': 'f', 'narrator': '" + Ada + "', 'characters': [], 'created_at': '2026-10-26T09:01:00Z'}");
            CollectionAssert.AreEqual(new[] { "w" }, IdList(OWMessage.UnreadFor(new[] { wire, folder }, Ada, null, null)),
                "the wire's message is a claim nobody can check; the folder's narrator is trusted");
            CollectionAssert.IsEmpty(OWMessage.UnreadFor(new[] { wire, folder }, Ada, null, (JArray)Fixture["members"]));
        }

        [Test]
        public void SenderStatus_CarriesTheClaim_AndTheHandThatWroteIt()
        {
            var got = OWMessage.SenderStatus(J("{'narrator': '" + Wren + "', 'created_by': null}"), (JArray)Fixture["members"]);
            Assert.AreEqual(OWSenderStatus.Mismatch, got.Status);
            Assert.AreEqual(Wren, got.Claimed);
            Assert.AreEqual(Ada, got.WrittenBy);
        }

        [Test]
        public void TheReaders_TakeNullAndNonObjectEntries_WithoutThrowing()
        {
            Assert.IsFalse(OWMessage.IsAddressedTo(null, Ada));
            Assert.IsFalse(OWMessage.IsSentBy(null, Ada));
            var none = OWMessage.SenderStatus(null, null);
            Assert.AreEqual(OWSenderStatus.Unverifiable, none.Status);
            Assert.IsNull(none.Claimed);
            Assert.IsNull(OWMessage.ReadMarkOf(null, Ada, null));
            CollectionAssert.IsEmpty(OWMessage.UnreadFor(null, Ada, null, null));
            CollectionAssert.IsEmpty(OWMessage.ThreadOf(null, "r"));

            // A list of bodies as a JArray holds whatever the JSON held; entries that are not objects are no message.
            var list = new JArray(5, JValue.CreateNull(), J("{'id': 'm', 'characters': [], 'created_at': '2026-10-26T09:00:00Z'}"));
            CollectionAssert.AreEqual(new[] { "m" }, IdList(OWMessage.UnreadFor(list, Ada, null, null)));
            CollectionAssert.AreEqual(new[] { "m" }, IdList(OWMessage.ThreadOf(list, "m")));
        }

        [Test]
        public void ABodyParsedWithDates_OrdersAsItsText()
        {
            // JObject.Parse reads an ISO time as a Date token, not a string; the order is the same instant's.
            var early = JObject.Parse("{\"id\": \"e\", \"characters\": [], \"created_at\": \"2026-10-26T09:00:00Z\"}");
            var late = JObject.Parse("{\"id\": \"l\", \"characters\": [], \"created_at\": \"2026-10-26T10:00:00+00:00\"}");
            CollectionAssert.AreEqual(new[] { "e", "l" }, IdList(OWMessage.UnreadFor(new[] { late, early }, Ada, null, null)));
            // An unreadable time sorts after every readable one, and two unreadable keep the list's order.
            var odd = J("{'id': 'x', 'characters': [], 'created_at': 'soon'}");
            var none = J("{'id': 'y', 'characters': []}");
            CollectionAssert.AreEqual(new[] { "e", "l", "x", "y" }, IdList(OWMessage.UnreadFor(new[] { odd, late, none, early }, Ada, null, null)));
        }
    }
}
