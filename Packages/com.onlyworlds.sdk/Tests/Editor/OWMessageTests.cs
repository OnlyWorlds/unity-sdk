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
    /// byte in <c>Tests/Editor/Fixtures/message/</c> from dist.89), read case by case through <see cref="OWMessage"/>'s
    /// readers, and its pair and read mark held equal to <see cref="OWMessage.Pair"/> and the read mark's constants. The
    /// TypeScript SDK and the Python package run the same cases; the tests beside the fixture are the TypeScript SDK's
    /// (test/message.test.mjs, 4.81.0) and the Python package's (tests/test_message.py, 0.64.0). When the dist moves
    /// the fixture, re-vendor it and update the hash below.
    /// </summary>
    public class OWMessageTests
    {
        /// <summary>The fixture's sha256 as dist.89's MANIFEST.json records it.</summary>
        private const string DistSha256 = "7d7ea66e4c987fe39f208dba9e80b6ede85d6c65097727226a9253a0058ca206";

        /// <summary>The fixture's cases by kind, as dist.89 holds them (87 in all).</summary>
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>
        {
            ["pair"] = 10, ["addressed"] = 11, ["unseen"] = 3, ["everyone"] = 3, ["sent_by"] = 3, ["sender"] = 14,
            ["unread"] = 17, ["mark"] = 10, ["thread"] = 4, ["edited"] = 4, ["note"] = 8,
        };

        /// <summary>The keys of a case that hold its answer, by kind.</summary>
        private static readonly Dictionary<string, string[]> ExpectedKeys = new Dictionary<string, string[]>
        {
            ["pair"] = new[] { "is_message" },
            ["addressed"] = new[] { "addressed" },
            ["unseen"] = new[] { "addressed", "unseen_recipients" },
            ["everyone"] = new[] { "to_everyone", "unseen_recipients" },
            ["sent_by"] = new[] { "sent_by" },
            ["sender"] = new[] { "status", "claimed", "written_by" },
            ["mark"] = new[] { "mark" },
            ["unread"] = new[] { "unread" },
            ["thread"] = new[] { "thread" },
            ["edited"] = new[] { "edited", "edited_by" },
            ["note"] = new[] { "line" },
        };

        private const string Ada = "019a3b10-0000-7000-8000-0000000000a1";
        private const string Mara = "019a3b10-0000-7000-8000-0000000000a2";
        private const string Wren = "019a3b10-0000-7000-8000-0000000000a3";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
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
                "the vendored fixture is not dist.89's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
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

            var note = Fixture["note_pair"];
            Assert.AreEqual(((string)note["type"], (string)note["supertype"]), OWMessage.NotePair);
        }

        [Test]
        public void TheFixture_HoldsDist89sCases()
        {
            Assert.IsNotNull(Fixture, "the vendored message fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(87, cases.Count, "dist.89's fixture holds 87 cases");
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
                case "unseen":
                    // A case without keyless has a credential.
                    return new JObject
                    {
                        ["addressed"] = OWMessage.IsAddressedTo((JObject)c["message"], (string)c["character"]),
                        ["unseen_recipients"] = Maybe(OWMessage.HasUnseenRecipients((JObject)c["message"])),
                    };
                case "everyone":
                {
                    var keyless = (bool)c["keyless"];
                    return new JObject
                    {
                        ["to_everyone"] = OWMessage.IsToEveryone((JObject)c["message"], keyless),
                        ["unseen_recipients"] = Maybe(OWMessage.HasUnseenRecipients((JObject)c["message"], keyless)),
                    };
                }
                case "edited":
                {
                    var e = OWMessage.EditedBy((JObject)c["message"], members);
                    return new JObject { ["edited"] = e.Edited, ["edited_by"] = Str(e.By) };
                }
                case "note":
                {
                    var line = OWMessage.NoteOf((JArray)c["notes"], (string)c["character"], members);
                    return new JObject { ["line"] = Str(line) };
                }
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

        /// <summary>A nullable answer as the fixture writes it: null as JSON null.</summary>
        private static JToken Maybe(bool? value) => value.HasValue ? new JValue(value.Value) : JValue.CreateNull();

        /// <summary>A string answer as the fixture writes it: null as JSON null, not a string-typed null.</summary>
        private static JToken Str(string value) => value == null ? JValue.CreateNull() : new JValue(value);

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
        public void AReadMark_IsTheReadPair_TrimmedAndFoldedAsTheRoomProfile()
        {
            Assert.IsTrue(OWMessage.IsReadMark(J("{'type': 'relation', 'supertype': 'Read', 'actor': 'a', 'narratives': []}")));
            Assert.IsTrue(OWMessage.IsReadMark(new JObject { ["type"] = "Relation", ["supertype"] = " READ\t" }));
            // A whole-word match on both: a reading list is not a mark, a message is not one, and a mark needs both keys.
            Assert.IsFalse(OWMessage.IsReadMark(new JObject { ["type"] = "relation", ["supertype"] = "Reads" }));
            Assert.IsFalse(OWMessage.IsReadMark(new JObject { ["type"] = "narrative", ["supertype"] = "Read" }));
            Assert.IsFalse(OWMessage.IsReadMark(new JObject { ["type"] = "relation", ["supertype"] = null }));
            Assert.IsFalse(OWMessage.IsReadMark(new JObject { ["supertype"] = "Read" }));
            Assert.IsFalse(OWMessage.IsReadMark(J("{'type': 'narrative', 'supertype': 'Message'}")));
            Assert.IsFalse(OWMessage.IsReadMark(null));
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
        public void TheTrimmedMark_IsAListThatNamesTheField_AndNothingElse()
        {
            // Only a list naming the field is the mark: a string, a list naming another field, or a folded spelling is not.
            foreach (var mark in new JToken[] { "characters", new JArray("narrator"), new JArray(" Characters"), new JArray(), JValue.CreateNull() })
            {
                var m = To(new JArray());
                m[OWMessage.ServedTrimmedKey] = mark;
                Assert.AreEqual(false, OWMessage.HasUnseenRecipients(m), mark.ToString());
                Assert.IsTrue(OWMessage.IsToEveryone(m), mark.ToString());
            }

            var trimmed = J("{'characters': null, 'x_served_trimmed': ['narrator', 'characters']}");
            Assert.AreEqual(true, OWMessage.HasUnseenRecipients(trimmed));
            Assert.IsFalse(OWMessage.IsToEveryone(trimmed));
            Assert.IsFalse(OWMessage.IsAddressedTo(trimmed, Ada), "an empty trimmed list is to someone this reader cannot see");
            Assert.AreEqual(false, OWMessage.HasUnseenRecipients(null));
            Assert.AreEqual(false, OWMessage.HasUnseenRecipients(null, keyless: true));
            Assert.IsNull(OWMessage.HasUnseenRecipients(trimmed, keyless: true), "keyless: unknown");
            Assert.IsFalse(OWMessage.IsToEveryone(new JObject(), keyless: true));
        }

        [Test]
        public void AnUnseenSender_IsAMissingNarratorBesideTheMark_DecidedBeforeTheHand()
        {
            var members = (JArray)Fixture["members"];
            foreach (var narrator in new[] { "", "'narrator': null, ", "'narrator': '', " })
            {
                var m = J("{" + narrator + "'created_by': null, 'x_served_trimmed': ['narrator']}");
                var s = OWMessage.SenderStatus(m, members);
                Assert.AreEqual(OWSenderStatus.Unseen, s.Status, m.ToString());
                Assert.IsNull(s.Claimed);
                Assert.IsNull(s.WrittenBy);
            }

            // A narrator the reader can see beside the mark is read as ever: the mark names a field that lost an id.
            var kept = J("{'narrator': '" + Ada + "', 'created_by': null, 'x_served_trimmed': ['narrator']}");
            Assert.AreEqual(OWSenderStatus.Verified, OWMessage.SenderStatus(kept, members).Status);
            var other = J("{'narrator': null, 'created_by': null, 'x_served_trimmed': ['characters']}");
            Assert.AreEqual(OWSenderStatus.Unverifiable, OWMessage.SenderStatus(other, members).Status);
        }

        [Test]
        public void EditedBy_ReadsTheLastHand_AndNeverChangesTheSender()
        {
            var members = (JArray)Fixture["members"];
            Assert.IsFalse(OWMessage.EditedBy(null, members).Edited);
            // A last hand with no first hand beside it is never equal: an edit, by the Character it plays.
            var e = OWMessage.EditedBy(J("{'narrator': '" + Mara + "', 'changed_by': null}"), members);
            Assert.AreEqual((true, Ada), (e.Edited, e.By));
            // No members list: an edit by another hand, named by nobody.
            var blind = OWMessage.EditedBy(J("{'created_by': null, 'changed_by': '019a3b10-0000-7000-8000-0000000000b2'}"), null);
            Assert.AreEqual((true, (string)null), (blind.Edited, blind.By));
            // Owner made it, owner changed it: no edit.
            Assert.IsFalse(OWMessage.EditedBy(J("{'created_by': null, 'changed_by': null}"), members).Edited);

            var edited = J("{'narrator': '" + Mara + "', 'created_by': '019a3b10-0000-7000-8000-0000000000b2', 'changed_by': null}");
            Assert.AreEqual(OWSenderStatus.Verified, OWMessage.SenderStatus(edited, members).Status);
        }

        [Test]
        public void ANote_IsNoMessage_InNoInboxOrThread()
        {
            var note = J("{'id': 'n', 'type': 'narrative', 'supertype': ' note ', 'narrator': '" + Ada + "', " +
                         "'characters': ['" + Mara + "'], 'parent_narrative': 'r', 'created_at': '2026-10-26T09:00:00Z'}");
            var root = J("{'id': 'r', 'type': 'narrative', 'supertype': 'Message', 'characters': [], 'created_at': '2026-10-26T08:00:00Z'}");
            Assert.IsTrue(OWMessage.IsNote(note));
            Assert.IsFalse(OWMessage.IsMessage(note));
            Assert.IsFalse(OWMessage.IsNote(root));
            Assert.IsFalse(OWMessage.IsNote(new JObject { ["type"] = "narrative", ["supertype"] = "Notes" }));
            CollectionAssert.IsEmpty(OWMessage.RecipientsOf(note), "a Note has no recipients, whatever its characters holds");
            Assert.IsFalse(OWMessage.IsAddressedTo(note, Mara));
            Assert.IsFalse(OWMessage.IsToEveryone(note));
            CollectionAssert.AreEqual(new[] { "r" }, IdList(OWMessage.ThreadOf(new[] { root, note }, "r")));
            CollectionAssert.IsEmpty(OWMessage.ThreadOf(new[] { root, note }, "n"), "a Note is not even its own thread's root");
            CollectionAssert.AreEqual(new[] { "r" }, IdList(OWMessage.UnreadFor(new[] { note, root }, Mara, null, null)));
        }

        [Test]
        public void NoteOf_TakesTheNewestReadableTime_OrTheLastInTheList()
        {
            JObject Note(string id, string createdAt) =>
                J("{'id': '" + id + "', 'type': 'narrative', 'supertype': 'Note', 'narrator': '" + Ada + "'" +
                  (createdAt == null ? "" : ", 'created_at': '" + createdAt + "'") + "}");

            var early = Note("early", "2026-10-26T09:00:00Z");
            var late = Note("late", "2026-10-26T10:00:00Z");
            var odd = Note("odd", "09:61");
            var none = Note("none", null);
            Assert.AreEqual("late", OWMessage.NoteOf(new[] { late, odd, early, none }, Ada, null),
                "an unreadable time is never the newest");
            Assert.AreEqual("none", OWMessage.NoteOf(new[] { odd, none }, Ada, null), "none readable: the last in the list");
            Assert.IsNull(OWMessage.NoteOf(new[] { late }, Mara, null));
            Assert.IsNull(OWMessage.NoteOf(new[] { late }, "", null));
            Assert.IsNull(OWMessage.NoteOf(null, Ada, null));
            // On the wire with no members list, nobody's hand is proved: no line.
            var wire = Note("wire", "2026-10-26T09:00:00Z");
            wire["created_by"] = JValue.CreateNull();
            Assert.IsNull(OWMessage.NoteOf(new[] { wire }, Ada, null));
            Assert.AreEqual("wire", OWMessage.NoteOf(new[] { wire }, Ada, (JArray)Fixture["members"]));
        }

        [Test]
        public void NoteOf_PassesByAProposedNote_AndTakesOneWhoseMarkIsEmpty()
        {
            JObject Note(string id, JToken mark)
            {
                var n = J("{'id': '" + id + "', 'type': 'narrative', 'supertype': 'Note', 'narrator': '" + Ada + "'}");
                n[OWPayload.ReviewProposed] = mark;
                return n;
            }

            Assert.IsNull(OWMessage.NoteOf(new[] { Note("p", new JObject { ["by"] = "seat" }) }, Ada, null), "a proposed Note is no line");
            // Serial 87: an empty mark is settled (the review profile's rule 1, by presence).
            foreach (var empty in new JToken[] { new JObject(), new JArray(), "  " })
                Assert.AreEqual("e", OWMessage.NoteOf(new[] { Note("e", empty) }, Ada, null), empty.ToString(Newtonsoft.Json.Formatting.None) + " is no mark");
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

        [Test]
        public void ADateToken_AndAString_OrderOnOneClock()
        {
            // A body read with dates as tokens and one read as text, in one list: one instant scale for both.
            var token = JObject.Parse("{\"id\": \"t\", \"characters\": [], \"created_at\": \"2026-10-26T09:00:00Z\"}");
            var text = J("{'id': 's', 'characters': [], 'created_at': '2026-10-26T10:00:00Z'}");
            Assert.AreEqual(JTokenType.Date, token["created_at"].Type, "the parse leaves a Date token");
            Assert.AreEqual(JTokenType.String, text["created_at"].Type, "the house parse leaves a string");
            CollectionAssert.AreEqual(new[] { "t", "s" }, IdList(OWMessage.UnreadFor(new[] { text, token }, Ada, null, null)));
        }
    }
}
