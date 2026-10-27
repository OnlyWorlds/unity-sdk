// The message profile: a message between the members of a world, a Narrative with supertype Message. To whom it is
// addressed, whether its sender is proved, who edited it, which messages a reader has read, a thread, and the Note
// beside it, which is not a message (schema-dist profiles/message.md, serials 72, 73, 75 and 76).
//
// Ported from @onlyworlds/sdk src/message.ts (4.80.0, b832b30; isToEveryone of 4.81.0, 9cdde0b), the Python package's
// message.py (0.63.0 and 0.64.0) read beside it. Serial 76's readings (the served-trimmed mark, keyless, the unseen
// sender, editedBy, isNote and noteOf) are written from the profile and its fixture. The trim and the fold are the room
// profile's (OWProfileText), never a second copy. The conformance fixture, profiles/message.fixture.json, is this file's
// own test (OWMessageTests), as it is the TypeScript SDK's and the Python package's. The pairs and the read mark are
// written here and held equal to the fixture's by that test, where npm generates them.

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

        /// <summary>
        /// The server left out a sender this reader may not see: <c>narrator</c> is null and the body's
        /// <c>x_served_trimmed</c> names <c>narrator</c>. Show "from someone you cannot see", never "from nobody" and
        /// never a name.
        /// </summary>
        Unseen,
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

        /// <summary>Verified, a mismatch, unverifiable, or unseen.</summary>
        public OWSenderStatus Status { get; }

        /// <summary>
        /// The message's <c>narrator</c>: the Character it claims as its sender, or null (show it as from nobody, or as
        /// from someone you cannot see when <see cref="Status"/> is <see cref="OWSenderStatus.Unseen"/>).
        /// </summary>
        public string Claimed { get; }

        /// <summary>On a mismatch, the Character of the membership that wrote it (from <c>created_by</c>); null otherwise.</summary>
        public string WrittenBy { get; }
    }

    /// <summary><see cref="OWMessage.EditedBy"/>'s answer for one message: whether a later hand changed it, and whose.</summary>
    public sealed class OWEditedBy
    {
        public OWEditedBy(bool edited, string by)
        {
            Edited = edited;
            By = by;
        }

        /// <summary>True when the message carries <c>changed_by</c> and it is not its <c>created_by</c>.</summary>
        public bool Edited { get; }

        /// <summary>
        /// The Character of the membership in <c>changed_by</c>, on the world's members list; null when that membership
        /// plays no Character, is off the list, or there is no list (show "edited by another hand"), and null whenever
        /// <see cref="Edited"/> is false.
        /// </summary>
        public string By { get; }
    }

    /// <summary>
    /// Reads a message between a world's members: its recipients (and whether the server left some out for this
    /// reader), its sender and whether that sender is proved, who edited it, a reader's read mark and unread messages,
    /// a thread, and a Character's Note, which is not a message. Element bodies are <see cref="JObject"/>s as the API or
    /// a world folder gives them; lists are any sequence of them (a <see cref="JArray"/> too), and an entry that is not
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
        /// The pair a Note is: type <c>narrative</c>, supertype <c>Note</c>. A member's own line in the world, never a
        /// message: addressed to nobody, never unread, never in a thread.
        /// </summary>
        public static readonly (string type, string supertype) NotePair = ("narrative", "Note");

        /// <summary>
        /// The key the server marks a body with when it left out ids the reader's key may not see: a list of the field
        /// names that lost one (<c>characters</c>, <c>narrator</c> or both). The server sets it and a reader only reads
        /// it; a folder keeps it as it was served, and a write never sends it (<see cref="OWPayload.ReadOnlyFields"/>).
        /// </summary>
        public const string ServedTrimmedKey = "x_served_trimmed";

        /// <summary>
        /// True when the element is a message: its <c>type</c> and <c>supertype</c>, each trimmed and compared without
        /// regard to case as the room profile defines them, are <see cref="Pair"/>. The match is the whole word
        /// (<c>Messages</c> is not one), and a missing or null supertype is none. A parlor's line (one carrying
        /// <c>x_room_of</c>) and a world's own message are both messages.
        /// </summary>
        /// <param name="element">An element body that names its <c>type</c> (a slug); null is no element.</param>
        public static bool IsMessage(JObject element) => Matches(element, Pair);

        /// <summary>
        /// True when the element is a read mark: its <c>type</c> and <c>supertype</c>, trimmed and folded as
        /// <see cref="IsMessage"/> reads them, are <see cref="ReadMarkPair"/>, whoever's mark it is. A mark is a reader's
        /// bookkeeping, not part of the world's story: a reader that weighs elements by their links (a graph's hubs) can
        /// leave it out by this.
        /// </summary>
        /// <param name="element">An element body that names its <c>type</c> (a slug); null is no element.</param>
        public static bool IsReadMark(JObject element) => Matches(element, ReadMarkPair);

        /// <summary>
        /// True when the element is a Note: its <c>type</c> and <c>supertype</c>, trimmed and folded as
        /// <see cref="IsMessage"/> reads them, are <see cref="NotePair"/>. A Note is a member's own line, never a
        /// message: the addressing, unread and thread readers here pass it by, whatever its <c>characters</c> holds.
        /// <see cref="NoteOf"/> finds a Character's line.
        /// </summary>
        /// <param name="element">An element body that names its <c>type</c> (a slug); null is no element.</param>
        public static bool IsNote(JObject element) => Matches(element, NotePair);

        /// <summary>
        /// The message's recipients: the Character ids in its <c>characters</c>, in the field's order, entries that are
        /// not ids left out. An empty <c>characters</c> (absent, null or <c>[]</c>) is addressed to every member of the
        /// world unless the server trimmed the list for this reader (<see cref="HasUnseenRecipients"/>); ask
        /// <see cref="IsAddressedTo"/> whether it is for one Character, and <see cref="IsToEveryone"/> whether it is for
        /// all. No other field addresses a message. A Note has none.
        /// </summary>
        public static IReadOnlyList<string> RecipientsOf(JObject message)
        {
            var list = new List<string>();
            if (IsNote(message)) return list;
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
        /// True when the message is addressed to every member: its <c>characters</c> is absent, null or <c>[]</c>, the
        /// server did not trim that list for this reader (<see cref="HasUnseenRecipients"/>), and the body came with a
        /// credential. A list that names no valid id (<c>[""]</c>) or a value that is not a list is addressed to nobody,
        /// so it is false here, though <see cref="RecipientsOf"/> gives none for both. An empty list with the trimmed
        /// mark reads "to someone you cannot see". A Note is never to everyone.
        /// </summary>
        /// <param name="message">The message's body.</param>
        /// <param name="keyless">
        /// True for a body read with no credential (the keyless public reader). The server leaves the ids that reader may
        /// not see out of it and never marks it, so an empty list is "to everyone, or to someone you cannot see": false
        /// here, always.
        /// </param>
        public static bool IsToEveryone(JObject message, bool keyless = false)
        {
            if (message == null || keyless || IsNote(message) || Trimmed(message, "characters")) return false;
            if (!message.TryGetValue("characters", out var list)) return true;
            return list.Type == JTokenType.Null || (list is JArray array && array.Count == 0);
        }

        /// <summary>
        /// Whether someone this reader cannot see is a recipient: true when the body's <c>x_served_trimmed</c> names
        /// <c>characters</c> (the server left their ids out), whatever is left in the list. It never says who or how
        /// many. False for a body without the mark, for null, and for a Note (it has no recipients, so nobody is unseen,
        /// keyless or not).
        /// </summary>
        /// <param name="message">The message's body.</param>
        /// <param name="keyless">
        /// True for a body read with no credential: the server never marks it, so the answer is unknown, null.
        /// </param>
        public static bool? HasUnseenRecipients(JObject message, bool keyless = false)
        {
            if (message == null || IsNote(message)) return false;
            if (keyless) return null;
            return Trimmed(message, "characters");
        }

        /// <summary>
        /// True when the message is addressed to the Character: its id is in <c>characters</c>, or the message is to
        /// everyone (<see cref="IsToEveryone"/>, which an empty list the server trimmed is not: it is addressed to no
        /// Character this reader is given). Not <c>narrator</c>, not a name or an id in the text, not
        /// <c>protagonist</c> or any other link: a sender is not addressed by sending. A Note is addressed to nobody.
        /// </summary>
        public static bool IsAddressedTo(JObject message, string characterId)
        {
            if (message == null || string.IsNullOrEmpty(characterId) || IsNote(message)) return false;
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
        /// author not on it, or its row naming no Character; show "claims to be from &lt;claimed&gt;".
        /// <see cref="OWSenderStatus.Unseen"/>: no narrator, and the body's <c>x_served_trimmed</c> names
        /// <c>narrator</c>: the server left out a sender this reader may not see; show "from someone you cannot see".
        /// Decided before the hand is checked. A body read with no credential is never marked, so its missing narrator
        /// stays unverifiable. Never give an unverified sender the verified look. An edit never changes this answer
        /// (<see cref="EditedBy"/>).
        /// </returns>
        public static OWSender SenderStatus(JObject message, IEnumerable<JToken> members)
        {
            var narrator = StringOf(message?["narrator"]);
            var claimed = string.IsNullOrEmpty(narrator) ? null : narrator;
            if (claimed == null && Trimmed(message, "narrator")) return new OWSender(OWSenderStatus.Unseen, null, null);
            if (claimed == null) return new OWSender(OWSenderStatus.Unverifiable, null, null);

            var hand = HandOf(message, members);
            if (hand == null) return new OWSender(OWSenderStatus.Unverifiable, claimed, null);
            return hand == claimed
                ? new OWSender(OWSenderStatus.Verified, claimed, null)
                : new OWSender(OWSenderStatus.Mismatch, claimed, hand);
        }

        /// <summary>
        /// Whether a later hand changed the message, and whose. The proof of sending is the first write,
        /// <c>created_by</c>; the last hand is <c>changed_by</c> (null for the owner, as <c>created_by</c> is). It is an
        /// edit when the message carries <c>changed_by</c> and it is not its <c>created_by</c> (an absent
        /// <c>created_by</c> is never equal). Show "edited by &lt;by&gt;" beside the sender's answer, or "edited by
        /// another hand" when <see cref="OWEditedBy.By"/> is null. An edit never changes <see cref="SenderStatus"/>'s
        /// answer. A message with no <c>changed_by</c> (a folder, an export) says nothing of edits.
        /// </summary>
        /// <param name="message">The message's body, as the server returns it.</param>
        /// <param name="members">The world's members list, as for <see cref="SenderStatus"/>; null when unreadable.</param>
        public static OWEditedBy EditedBy(JObject message, IEnumerable<JToken> members)
        {
            if (message == null || !message.TryGetValue("changed_by", out var editor)) return new OWEditedBy(false, null);
            if (message.TryGetValue("created_by", out var author) && SameHand(editor, author)) return new OWEditedBy(false, null);
            return new OWEditedBy(true, CharacterOf(editor, members));
        }

        /// <summary>
        /// The reader's read mark among the given Relations, or null when it has none. A mark is a Relation with
        /// supertype <c>Read</c> (<see cref="ReadMarkPair"/>, trimmed and folded like the pair) whose <c>actor</c> is
        /// the reader, and it counts only in the reader's own hand: its <c>created_by</c>, looked up on the members
        /// list, names the reader's Character (<c>created_by</c> null is the owner's hand), and its last writer is its
        /// author: a mark that carries <c>changed_by</c> counts only beside an equal <c>created_by</c>, so one another
        /// hand has changed since, the owner's included, is ignored. A mark with neither key (a folder, an export) has
        /// no hand to check and counts.
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
                if (relation.TryGetValue("changed_by", out var lastHand)
                    && !(relation.TryGetValue("created_by", out var author) && SameHand(lastHand, author))) continue;

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
        /// server's; a time that cannot be read comes after every one that can, in the list's order): addressed to it
        /// (<see cref="IsAddressedTo"/>), not its own, and not on its read mark's <c>narratives</c>. Its own is a message
        /// it sent, never unread for it whoever it is addressed to: where the message carries no <c>created_by</c> (a
        /// folder, an export) its <c>narrator</c> is trusted; on the wire only a verified sender counts
        /// (<see cref="SenderStatus"/>), so a claim never makes a message read for the Character it names, and a
        /// mismatch or an author nobody can check stays unread. A Note is never unread.
        /// </summary>
        /// <param name="messages">The messages (<see cref="IsMessage"/>); every object given but a Note is read as one.</param>
        /// <param name="characterId">The reader's Character id.</param>
        /// <param name="readMark">
        /// <see cref="ReadMarkOf"/>'s answer; null is a reader with no mark, so everything addressed to it is unread.
        /// An id on the mark that names a message not addressed to the reader changes nothing.
        /// </param>
        /// <param name="members">
        /// The world's members list, as for <see cref="SenderStatus"/>; null when unreadable, and then none of the
        /// reader's own messages on the wire is proved.
        /// </param>
        public static IReadOnlyList<JObject> UnreadFor(IEnumerable<JToken> messages, string characterId, JObject readMark,
            IEnumerable<JToken> members)
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
                if (!IsAddressedTo(m, characterId) || IsOwn(m, characterId, members)) return false;
                var id = StringOf(m["id"]);
                return id == null || !read.Contains(id);
            });
            return InCreatedOrder(unread);
        }

        /// <summary>
        /// The thread of a root message: every message whose chain of <c>parent_narrative</c> reaches the root, the root
        /// included, in <c>created_at</c> order (equal times keep the list's order). A walk stops at an id it has
        /// already visited, so a loop in the chain ends it; a parent not in the list ends the chain unless it is the root,
        /// and so does a <c>parent_narrative</c> that is not a string id (a number, null, an object). A Note is in no
        /// thread: it is never listed, though a chain walks through its <c>parent_narrative</c> like any other (a reply
        /// to a Note that answers the root is in the thread).
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

            return InCreatedOrder(listed.Where(m => !IsNote(m) && Reaches(m)));
        }

        /// <summary>
        /// The Character's line: the id of its newest Note in its own hand, or null when it has none. A Note
        /// (<see cref="IsNote"/>) with a string id, whose <c>narrator</c> is the Character, neither hidden (<c>hidden</c>
        /// is the boolean true; any other value is not hidden here) nor proposed (<see cref="OWPayload.IsProposed"/>:
        /// <c>x_review_proposed</c> an object), and in its author's hand: where it carries no
        /// <c>created_by</c> (a folder, an export) its <c>narrator</c> is trusted; on the wire only a verified sender
        /// counts (<see cref="SenderStatus"/>), so a mismatch or an unverifiable Note is nobody's line. The newest is the
        /// last in <c>created_at</c> order whose time can be read, or the last in the list when none can. A message is
        /// never the line.
        /// </summary>
        /// <param name="narratives">Narratives, the Notes among them; anything else given is passed by.</param>
        /// <param name="characterId">The Character id; an empty id has no line.</param>
        /// <param name="members">The world's members list, as for <see cref="SenderStatus"/>; null when unreadable.</param>
        public static string NoteOf(IEnumerable<JToken> narratives, string characterId, IEnumerable<JToken> members)
        {
            if (narratives == null || string.IsNullOrEmpty(characterId)) return null;
            var own = narratives.OfType<JObject>()
                .Where(n => IsNote(n) && !string.IsNullOrEmpty(StringOf(n["id"])) && IsSentBy(n, characterId)
                            && !IsHiddenTrue(n) && !OWPayload.IsProposed(n) && IsOwn(n, characterId, members))
                .ToList();
            if (own.Count == 0) return null;
            var dated = own.Where(n => TimeKey(n["created_at"]) != null).ToList();
            return StringOf((dated.Count > 0 ? InCreatedOrder(dated).Last() : own.Last())["id"]);
        }

        // -- The profile's comparisons ---------------------------------------------

        /// <summary>True when the body's <c>x_served_trimmed</c> is a list that names the field.</summary>
        private static bool Trimmed(JObject body, string field) =>
            body?[ServedTrimmedKey] is JArray fields && fields.Any(entry => StringOf(entry) == field);

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
            if (body == null || !body.TryGetValue("created_by", out var author)) return null;
            return CharacterOf(author, members);
        }

        /// <summary>
        /// The Character a membership plays: its row on the members list, by <c>id</c> (null the owner's row). Null when
        /// there is no list, the membership is not on it, or its row names no Character.
        /// </summary>
        private static string CharacterOf(JToken membership, IEnumerable<JToken> members)
        {
            if (members == null || !(membership is JValue)) return null;
            foreach (var entry in members)
            {
                if (!(entry is JObject row) || !row.TryGetValue("id", out var id)) continue;
                if (!SameHand(id, membership)) continue;
                var character = StringOf(row["character"]);
                return string.IsNullOrEmpty(character) ? null : character;
            }

            return null;
        }

        /// <summary>
        /// True when the message is the reader's own for unread: where it carries no <c>created_by</c> (a folder, an
        /// export) its <c>narrator</c> is trusted; where it does, only a verified sender counts.
        /// </summary>
        private static bool IsOwn(JObject message, string characterId, IEnumerable<JToken> members)
        {
            if (!message.ContainsKey("created_by")) return IsSentBy(message, characterId);
            var sender = SenderStatus(message, members);
            return sender.Status == OWSenderStatus.Verified && sender.Claimed == characterId;
        }

        /// <summary>Two membership ids for the same hand: equal JSON values, null the owner's.</summary>
        private static bool SameHand(JToken a, JToken b) => a is JValue && b is JValue && JToken.DeepEquals(a, b);

        /// <summary>
        /// A time as microseconds since the epoch, so microseconds still order, as the TypeScript SDK reads them; null
        /// when unreadable. A string is read by the profile's rule (<see cref="InstantOf"/>); a Date token, as
        /// <c>JObject.Parse</c> leaves one, is read as its instant.
        /// </summary>
        private static long? TimeKey(JToken token)
        {
            if (!(token is JValue value)) return null;
            DateTimeOffset at;
            switch (value.Value)
            {
                case string text:
                    return InstantOf(text);
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

        /// <summary>
        /// An RFC 3339 date-time as microseconds since the epoch, or null when unreadable: <c>YYYY-MM-DDTHH:MM:SS</c>, an
        /// optional fraction of a second after <c>.</c> (microseconds kept, the rest dropped), then <c>Z</c> or
        /// <c>+HH:MM</c> / <c>-HH:MM</c> (<c>T</c> and <c>Z</c> in either case), or no zone, read as UTC. The date must
        /// exist (Gregorian, leap years), hours 00-23, minutes and seconds 00-59, an offset 00-23 hours and 00-59 minutes.
        /// ASCII digits only, the whole string, nothing trimmed: the TypeScript SDK's rule, so every package orders alike.
        /// </summary>
        private static long? InstantOf(string text)
        {
            const string D = "[0-9]";
            var m = System.Text.RegularExpressions.Regex.Match(text,
                "^(" + D + "{4})-(" + D + "{2})-(" + D + "{2})[Tt](" + D + "{2}):(" + D + "{2}):(" + D + "{2})"
                + "(?:\\.(" + D + "+))?(?:[Zz]|([+-])(" + D + "{2}):(" + D + "{2}))?\\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!m.Success) return null;
            int Part(int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
            int year = Part(1), month = Part(2), day = Part(3), hour = Part(4), minute = Part(5), second = Part(6);
            var leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
            var monthLength = month == 2 ? (leap ? 29 : 28) : (month == 4 || month == 6 || month == 9 || month == 11 ? 30 : 31);
            if (month < 1 || month > 12 || day < 1 || day > monthLength || hour > 23 || minute > 59 || second > 59) return null;
            long offset = 0;
            if (m.Groups[8].Success)
            {
                int offsetHours = Part(9), offsetMinutes = Part(10);
                if (offsetHours > 23 || offsetMinutes > 59) return null;
                offset = (offsetHours * 3600L + offsetMinutes * 60L) * (m.Groups[8].Value == "-" ? -1 : 1);
            }

            long micros = 0;
            if (m.Groups[7].Success) micros = long.Parse((m.Groups[7].Value + "000000").Substring(0, 6), CultureInfo.InvariantCulture);
            var days = DaysFromCivil(year, month, day);
            return (days * 86400L + hour * 3600L + minute * 60L + second - offset) * 1000000L + micros;
        }

        /// <summary>Days from 1970-01-01 to a proleptic Gregorian date (exact integer arithmetic, years 0000-9999).</summary>
        private static long DaysFromCivil(int year, int month, int day)
        {
            long y = month <= 2 ? year - 1 : year;
            long era = (y >= 0 ? y : y - 399) / 400;
            long yoe = y - era * 400;
            long doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
            long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        /// <summary>True when <c>hidden</c> is the boolean true; the Note reader counts no other value as hidden.</summary>
        private static bool IsHiddenTrue(JObject body) =>
            body?["hidden"] is JValue mark && mark.Type == JTokenType.Boolean && (bool)mark;

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
