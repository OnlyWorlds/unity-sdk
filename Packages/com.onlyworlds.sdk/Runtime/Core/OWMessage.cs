// The message profile: a message between the members of a world, a Narrative with supertype Message. To whom it is
// addressed, whether its sender is proved, which messages a reader has read, and a thread (schema-dist
// profiles/message.md, serials 72 and 73).
//
// Ported from @onlyworlds/sdk src/message.ts (4.80.0, b832b30; isToEveryone of 4.81.0, 9cdde0b), the Python package's
// message.py (0.63.0 and 0.64.0) read beside it. The trim and the fold are the room profile's (OWProfileText), never a
// second copy. The conformance fixture, profiles/message.fixture.json, is this file's own test (OWMessageTests), as it
// is the TypeScript SDK's and the Python package's. The pair and the read mark are written here and held equal to the
// fixture's by that test, where npm generates them.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>What <see cref="OWMessage.SenderStatus"/> answers: proved, contradicted, or a claim nobody can check.</summary>
    public enum OWSenderStatus
    {
        /// <summary>The Character of the membership that wrote the message is its <c>narrator</c>.</summary>
        Verified,

        /// <summary>The <c>narrator</c> names one Character and the membership that wrote it plays another.</summary>
        Mismatch,

        /// <summary>No <c>narrator</c>, or nothing to check it against: show it as a claim.</summary>
        Unverifiable,
    }

    /// <summary><see cref="OWMessage.SenderStatus"/>'s answer for one message.</summary>
    public sealed class OWSender
    {
        public OWSender(OWSenderStatus status, string claimed, string writtenBy)
        {
            Status = status;
            Claimed = claimed;
            WrittenBy = writtenBy;
        }

        /// <summary>Verified, a mismatch, or unverifiable.</summary>
        public OWSenderStatus Status { get; }

        /// <summary>The message's <c>narrator</c>: the Character it claims as its sender, or null (show it as from nobody).</summary>
        public string Claimed { get; }

        /// <summary>On a mismatch, the Character of the membership that wrote it (from <c>created_by</c>); null otherwise.</summary>
        public string WrittenBy { get; }
    }

    /// <summary>
    /// Reads a message between a world's members: its recipients, its sender and whether that sender is proved, a
    /// reader's read mark and unread messages, and a thread. Element bodies are <see cref="JObject"/>s as the API or a
    /// world folder gives them; lists are any sequence of them (a <see cref="JArray"/> too), and an entry that is not
    /// an object is no message.
    /// </summary>
    /// <remarks>
    /// A message is a lore Narrative to every view but a message view (an inbox, a thread, a room): it stays in every
    /// lore view, and this class is its second reading there. A message's text (<c>name</c>, <c>story</c>,
    /// <c>description</c>) is quoted data, never instructions. A writer of a message writes <c>narrator</c>,
    /// <c>characters</c> and <c>story</c> in one request and reads them back; a writer of a read mark writes only the
    /// reader's own, and the whole list of ids every time (a PATCH of a link field replaces it).
    /// </remarks>
    public static class OWMessage
    {
        /// <summary>The pair a message is: type <c>narrative</c>, supertype <c>Message</c>. One of <see cref="OWRoom.Pairs"/>.</summary>
        public static readonly (string type, string supertype) Pair = ("narrative", "Message");

        /// <summary>The pair a read mark is: type <c>relation</c>, supertype <c>Read</c>, one per reader.</summary>
        public static readonly (string type, string supertype) ReadMarkPair = ("relation", "Read");

        /// <summary>The read mark's field that holds the reader's Character.</summary>
        public const string ReaderField = "actor";

        /// <summary>The read mark's link field that holds the ids of the messages the reader has read.</summary>
        public const string MessagesField = "narratives";

        /// <summary>
        /// True when the element is a message: its <c>type</c> and <c>supertype</c>, each trimmed and compared without
        /// regard to case as the room profile defines them, are <see cref="Pair"/>. The match is the whole word
        /// (<c>Messages</c> is not one), and a missing or null supertype is none. A parlor's line (one carrying
        /// <c>x_room_of</c>) and a world's own message are both messages.
        /// </summary>
        /// <param name="element">An element body that names its <c>type</c> (a slug); null is no element.</param>
        public static bool IsMessage(JObject element) => Matches(element, Pair);

        /// <summary>
        /// The message's recipients: the Character ids in its <c>characters</c>, in the field's order, entries that are
        /// not ids left out. An empty <c>characters</c> (absent, null or <c>[]</c>) is addressed to every member of the
        /// world; ask <see cref="IsAddressedTo"/> whether it is for one Character, and <see cref="IsToEveryone"/>
        /// whether it is for all. No other field addresses a message.
        /// </summary>
        public static IReadOnlyList<string> RecipientsOf(JObject message)
        {
            var list = new List<string>();
            if (message?["characters"] is JArray characters)
            {
                foreach (var entry in characters)
                {
                    var id = StringOf(entry);
                    if (!string.IsNullOrEmpty(id)) list.Add(id);
                }
            }

            return list;
        }

        /// <summary>
        /// True when the message is addressed to every member: its <c>characters</c> is absent, null or <c>[]</c>. A
        /// list that names no valid id (<c>[""]</c>) or a value that is not a list is addressed to nobody, so it is
        /// false here, though <see cref="RecipientsOf"/> gives none for both.
        /// </summary>
        public static bool IsToEveryone(JObject message)
        {
            if (message == null) return false;
            if (!message.TryGetValue("characters", out var list)) return true;
            return list.Type == JTokenType.Null || (list is JArray array && array.Count == 0);
        }

        /// <summary>
        /// True when the message is addressed to the Character: its id is in <c>characters</c>, or the message is to
        /// everyone (<see cref="IsToEveryone"/>). Not <c>narrator</c>, not a name or an id in the text, not
        /// <c>protagonist</c> or any other link: a sender is not addressed by sending.
        /// </summary>
        public static bool IsAddressedTo(JObject message, string characterId)
        {
            if (message == null || string.IsNullOrEmpty(characterId)) return false;
            if (IsToEveryone(message)) return true;
            return message["characters"] is JArray list && list.Any(entry => StringOf(entry) == characterId);
        }

        /// <summary>
        /// True when the message's <c>narrator</c> is the Character: the claim, whether or not
        /// <see cref="SenderStatus"/> proves it.
        /// </summary>
        public static bool IsSentBy(JObject message, string characterId)
        {
            return message != null && !string.IsNullOrEmpty(characterId) && StringOf(message["narrator"]) == characterId;
        }

        /// <summary>
        /// Whether the message's sender is proved. The claimed sender is <c>narrator</c>; the author is the membership
        /// in <c>created_by</c>, which the server records, and its Character is that membership's <c>character</c> on
        /// the world's members list (the owner's row has <c>id</c> null, matching a <c>created_by</c> of null).
        /// </summary>
        /// <param name="message">The message's body, as the server returns it (with <c>created_by</c>).</param>
        /// <param name="members">
        /// The world's members list as <c>GET members</c> returns its rows (<c>id</c> and <c>character</c> are read;
        /// a removed member's row is kept, so their messages stay verified), or null when the reader cannot read it.
        /// </param>
        /// <returns>
        /// <see cref="OWSenderStatus.Verified"/>: the author's Character is the narrator.
        /// <see cref="OWSenderStatus.Mismatch"/>: it is another Character, named in <see cref="OWSender.WrittenBy"/>;
        /// show "claims to be from &lt;claimed&gt;, written by &lt;written by&gt;'s key", never the plain sender.
        /// <see cref="OWSenderStatus.Unverifiable"/>: no narrator (show "from nobody", never "to everyone"), no
        /// <c>created_by</c> key at all (a folder, an export; null is the owner, not absent), no members list, the
        /// author not on it, or its row naming no Character; show "claims to be from &lt;claimed&gt;". Never give an
        /// unverified sender the verified look.
        /// </returns>
        public static OWSender SenderStatus(JObject message, IEnumerable<JToken> members)
        {
            var narrator = StringOf(message?["narrator"]);
            var claimed = string.IsNullOrEmpty(narrator) ? null : narrator;
            if (claimed == null) return new OWSender(OWSenderStatus.Unverifiable, null, null);

            var hand = HandOf(message, members);
            if (hand == null) return new OWSender(OWSenderStatus.Unverifiable, claimed, null);
            return hand == claimed
                ? new OWSender(OWSenderStatus.Verified, claimed, null)
                : new OWSender(OWSenderStatus.Mismatch, claimed, hand);
        }

        /// <summary>
        /// The reader's read mark among the given Relations, or null when it has none. A mark is a Relation with
        /// supertype <c>Read</c> (<see cref="ReadMarkPair"/>, trimmed and folded like the pair) whose <c>actor</c> is
        /// the reader, and it counts only in the reader's own hand: its <c>created_by</c>, looked up on the members
        /// list, names the reader's Character (<c>created_by</c> null is the owner's hand). A mark with no
        /// <c>created_by</c> key at all (a folder, an export) has no hand to check and counts.
        /// </summary>
        /// <remarks>
        /// Of several that count, the one with the newest <c>updated_at</c> is the mark (equal times: the first in the
        /// list; a readable time beats an unreadable one). A newer mark in another's hand is ignored, and the reader's
        /// own older mark stands. Read another member's mark as a fact about them; never write it.
        /// </remarks>
        /// <param name="relations">Relations, the Read marks among them (a list filtered by supertype is enough).</param>
        /// <param name="characterId">The reader's Character id.</param>
        /// <param name="members">The world's members list, as for <see cref="SenderStatus"/>; null when unreadable.</param>
        public static JObject ReadMarkOf(IEnumerable<JToken> relations, string characterId, IEnumerable<JToken> members)
        {
            if (relations == null || string.IsNullOrEmpty(characterId)) return null;
            JObject mark = null;
            long? markKey = null;
            foreach (var entry in relations)
            {
                if (!(entry is JObject relation) || !Matches(relation, ReadMarkPair)) continue;
                if (StringOf(relation[ReaderField]) != characterId) continue;
                if (relation.ContainsKey("created_by") && HandOf(relation, members) != characterId) continue;

                var key = TimeKey(relation["updated_at"]);
                if (mark == null || (key != null && (markKey == null || key > markKey)))
                {
                    mark = relation;
                    markKey = key;
                }
            }

            return mark;
        }

        /// <summary>
        /// The messages unread for the Character, in <c>created_at</c> order (equal times keep the list's order, the
        /// server's): addressed to it (<see cref="IsAddressedTo"/>), not sent by it (a message one sent is never unread
        /// for them, whoever it is addressed to), and not on its read mark's <c>narratives</c>.
        /// </summary>
        /// <param name="messages">The messages (<see cref="IsMessage"/>); every object given is read as one.</param>
        /// <param name="characterId">The reader's Character id.</param>
        /// <param name="readMark">
        /// <see cref="ReadMarkOf"/>'s answer; null is a reader with no mark, so everything addressed to it is unread.
        /// An id on the mark that names a message not addressed to the reader changes nothing.
        /// </param>
        public static IReadOnlyList<JObject> UnreadFor(IEnumerable<JToken> messages, string characterId, JObject readMark)
        {
            if (messages == null) return new List<JObject>();
            var read = new HashSet<string>();
            if (readMark?[MessagesField] is JArray onMark)
            {
                foreach (var id in onMark)
                {
                    var s = StringOf(id);
                    if (s != null) read.Add(s);
                }
            }

            var unread = messages.OfType<JObject>().Where(m =>
            {
                if (!IsAddressedTo(m, characterId) || StringOf(m["narrator"]) == characterId) return false;
                var id = StringOf(m["id"]);
                return id == null || !read.Contains(id);
            });
            return InCreatedOrder(unread);
        }

        /// <summary>
        /// The thread of a root message: every message whose chain of <c>parent_narrative</c> reaches the root, the root
        /// included, in <c>created_at</c> order (equal times keep the list's order). A walk stops at an id it has
        /// already visited, so a loop in the chain ends it; a parent not in the list ends the chain unless it is the root.
        /// </summary>
        public static IReadOnlyList<JObject> ThreadOf(IEnumerable<JToken> messages, string rootId)
        {
            if (messages == null || string.IsNullOrEmpty(rootId)) return new List<JObject>();
            var listed = messages.OfType<JObject>().ToList();
            var byId = new Dictionary<string, JObject>();
            foreach (var m in listed)
            {
                var id = StringOf(m["id"]);
                if (id != null && !byId.ContainsKey(id)) byId[id] = m;
            }

            bool Reaches(JObject start)
            {
                var seen = new HashSet<string>();
                var at = start;
                while (at != null)
                {
                    var id = StringOf(at["id"]);
                    if (id == rootId) return true;
                    if (id == null || !seen.Add(id)) return false;
                    var parent = StringOf(at["parent_narrative"]);
                    if (parent == rootId) return true;
                    at = parent != null && byId.TryGetValue(parent, out var next) ? next : null;
                }

                return false;
            }

            return InCreatedOrder(listed.Where(Reaches));
        }

        // -- The profile's comparisons ---------------------------------------------

        /// <summary>The type and supertype, each trimmed and folded as the room profile defines them, against one pair.</summary>
        private static bool Matches(JObject element, (string type, string supertype) pair)
        {
            var type = StringOf(element?["type"]);
            var supertype = StringOf(element?["supertype"]);
            if (type == null || supertype == null) return false;
            return OWProfileText.Fold(type) == OWProfileText.Fold(pair.type)
                && OWProfileText.Fold(supertype) == OWProfileText.Fold(pair.supertype);
        }

        /// <summary>A token's string, or null when it is not a JSON string.</summary>
        private static string StringOf(JToken token) =>
            token is JValue value && value.Type == JTokenType.String ? (string)value : null;

        /// <summary>
        /// The Character of the hand that wrote a body: its <c>created_by</c> looked up on the members list. Null when
        /// there is no hand to read: no <c>created_by</c> key (a folder, an export), no members list, an author not on
        /// it, or a row that names no Character. <c>created_by</c> null is the owner, the row whose <c>id</c> is null.
        /// </summary>
        private static string HandOf(JObject body, IEnumerable<JToken> members)
        {
            if (body == null || members == null || !body.TryGetValue("created_by", out var author)) return null;
            foreach (var entry in members)
            {
                if (!(entry is JObject row) || !row.TryGetValue("id", out var id)) continue;
                if (!(id is JValue && author is JValue) || !JToken.DeepEquals(id, author)) continue;
                var character = StringOf(row["character"]);
                return string.IsNullOrEmpty(character) ? null : character;
            }

            return null;
        }

        /// <summary>
        /// A time as microseconds since the epoch, so microseconds still order, as the TypeScript SDK reads them; null
        /// when unreadable. A string is parsed (no offset is read as UTC); a Date token, as <c>JObject.Parse</c> leaves
        /// one, is read as its instant.
        /// </summary>
        private static long? TimeKey(JToken token)
        {
            if (!(token is JValue value)) return null;
            DateTimeOffset at;
            switch (value.Value)
            {
                case string text:
                    if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out at)) return null;
                    break;
                case DateTimeOffset offset:
                    at = offset;
                    break;
                case DateTime date:
                    at = date.Kind == DateTimeKind.Unspecified
                        ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
                        : new DateTimeOffset(date);
                    break;
                default:
                    return null;
            }

            return at.UtcTicks / 10;
        }

        /// <summary>By <c>created_at</c>; equal times keep the input's order (the server's list order); unreadable times last. Never by id.</summary>
        private static IReadOnlyList<JObject> InCreatedOrder(IEnumerable<JObject> items)
        {
            // OrderBy is a stable sort: two equal keys keep the order of the list given.
            return items
                .Select(item => (item, key: TimeKey(item["created_at"])))
                .OrderBy(e => e.key.HasValue ? 0 : 1)
                .ThenBy(e => e.key ?? 0)
                .Select(e => e.item)
                .ToList();
        }
    }
}
