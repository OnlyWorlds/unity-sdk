// The lens profile: a saved way of reading a world, a Construct with supertype Lens whose links are its anchors and
// whose x_lens recipe says what to read (depth, types, window, from, to, undated, show, budget, mail, notes), and the
// one resolution every reader agrees on (schema-dist profiles/lens.md, serial 78, ruled 2026-10-26).
//
// Written from the profile and its conformance fixture, profiles/lens.fixture.json, which is this file's own test
// (OWLensTests); keel's reference reader replays the same cases. The packages' reader is resolveLens(world, lens,
// reader) (the Python package's resolve_lens): here OWLens.Resolve. It leans on what the SDK already reads: the room
// profile's pairs and trim (OWRoom, OWProfileText), the hidden and proposed marks (OWPayload), the message profile's
// read mark, unread rule and verified sender (OWMessage), the generated models' link fields in the schema's order, and
// the schema's type order (OWElementTypes.Slugs).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Why a lens left an element out: the profile's closed list. Each cut element is counted once, under the first
    /// reason that applies: the fence (<see cref="AfterAsOf"/>, <see cref="Undated"/>), then <see cref="Types"/>, then
    /// <see cref="Window"/>, then <see cref="Budget"/>.
    /// </summary>
    public static class OWLensCut
    {
        /// <summary>Past the limit, in the budget's order.</summary>
        public const string Budget = "budget";

        /// <summary>Reached, of a type (or a room pair) the lens does not keep.</summary>
        public const string Types = "types";

        /// <summary>Dated before the window, or outside <c>from</c> and <c>to</c>.</summary>
        public const string Window = "window";

        /// <summary>Of a dated type with no date, under <c>undated: exclude</c>.</summary>
        public const string Undated = "undated";

        /// <summary>Dated after the reading's <c>as_of</c>, by the element's own date.</summary>
        public const string AfterAsOf = "after_as_of";

        /// <summary>An anchor the reader cannot see. The owner's count only.</summary>
        public const string AnchorNotVisible = "anchor_not_visible";

        /// <summary>A non-anchor a hop would reach that the reader's view leaves out. The owner's count only.</summary>
        public const string NotVisible = "not_visible";

        /// <summary>What a hop past depth 1 would reach: depth 2 is legal, and a reader serves 0 and 1.</summary>
        public const string HopsNotServed = "hops_not_served";

        /// <summary>Every reason, in the profile's order.</summary>
        public static readonly string[] All =
        {
            Budget, Types, Window, Undated, AfterAsOf, AnchorNotVisible, NotVisible, HopsNotServed,
        };

        /// <summary>
        /// The two counts only the owner sees (the owner reading as itself, or as another reader). Any other reader's
        /// result never carries them, not even as zero: a count would tell a guest that something hidden exists.
        /// </summary>
        public static readonly string[] OwnerOnly = { AnchorNotVisible, NotVisible };
    }

    /// <summary>What a reader may read of a world, the rung its view is built from.</summary>
    public enum OWLensRung
    {
        /// <summary>The whole world but what is proposed.</summary>
        Owner,

        /// <summary>The world less what is hidden (<see cref="OWPayload.IsHidden"/>) and what is proposed.</summary>
        Contributor,

        /// <summary>Exactly the ids it is given whole, and the ids it knows of as stubs; never what is proposed.</summary>
        Guest,

        /// <summary>The world less what is hidden, what is proposed, and every room element (<see cref="OWRoom"/>).</summary>
        Public,

        /// <summary>
        /// The bodies as a key was served them (the API's reply to that key, a folder pulled with it): every body given
        /// is whole but a known-of stub (<see cref="OWPayload.IsKnownOf"/>), which is a stub, and what is proposed,
        /// which is in no view. Nothing is hidden on top of what the server already left out.
        /// </summary>
        Served,
    }

    /// <summary>Who reads a lens: the view it reads through, its Character (for the mail rule), and whose counts it sees.</summary>
    public sealed class OWLensReader
    {
        private OWLensReader(OWLensRung rung, string character, IEnumerable<string> whole, IEnumerable<string> knownOf,
            bool ownerCounts)
        {
            Rung = rung;
            Character = string.IsNullOrEmpty(character) ? null : character;
            Whole = whole == null ? null : new HashSet<string>(whole.Where(i => i != null));
            KnownOf = knownOf == null ? null : new HashSet<string>(knownOf.Where(i => i != null));
            OwnerCounts = ownerCounts;
        }

        /// <summary>The rung the view is built from.</summary>
        public OWLensRung Rung { get; }

        /// <summary>The reader's Character, whose mail the mail rule reads; null for a reader with none (no mail).</summary>
        public string Character { get; }

        /// <summary>A guest's ids it is given whole; null for every other rung.</summary>
        public IReadOnlyCollection<string> Whole { get; }

        /// <summary>A guest's ids it knows of as stubs; null for every other rung.</summary>
        public IReadOnlyCollection<string> KnownOf { get; }

        /// <summary>
        /// True when the result carries the owner's two counts (<see cref="OWLensCut.OwnerOnly"/>): the owner, or the
        /// owner reading as another reader (<see cref="WithOwnerCounts"/>).
        /// </summary>
        public bool OwnerCounts { get; }

        /// <summary>The world's owner: the whole world but what is proposed, and its own counts.</summary>
        public static OWLensReader Owner(string character = null) =>
            new OWLensReader(OWLensRung.Owner, character, null, null, true);

        /// <summary>A contributor: the world less the hidden and the proposed.</summary>
        public static OWLensReader Contributor(string character = null) =>
            new OWLensReader(OWLensRung.Contributor, character, null, null, false);

        /// <summary>A guest: exactly the ids it is served whole, and those it knows of as stubs.</summary>
        public static OWLensReader Guest(IEnumerable<string> whole, IEnumerable<string> knownOf = null, string character = null) =>
            new OWLensReader(OWLensRung.Guest, character, whole ?? Array.Empty<string>(), knownOf ?? Array.Empty<string>(), false);

        /// <summary>The public reader (no key): the world less the hidden, the proposed and the room elements.</summary>
        public static OWLensReader Public() => new OWLensReader(OWLensRung.Public, null, null, null, false);

        /// <summary>A key's own served bodies, read as they are (<see cref="OWLensRung.Served"/>).</summary>
        public static OWLensReader Served(string character = null) =>
            new OWLensReader(OWLensRung.Served, character, null, null, false);

        /// <summary>The owner reading as this reader: this reader's view, and the owner's two counts beside it.</summary>
        public OWLensReader WithOwnerCounts() => new OWLensReader(Rung, Character, Whole, KnownOf, true);
    }

    /// <summary>The reading's moment and the world's facts a lens reads beside its elements.</summary>
    public sealed class OWLensOptions
    {
        /// <summary>
        /// The reading's date, a whole number in the world's <c>time_basic_unit</c>: the fence's upper edge, and the
        /// point <c>window</c> reaches back from. Null: <see cref="Now"/>.
        /// </summary>
        public int? AsOf { get; set; }

        /// <summary>
        /// The world's now (its <c>time_current</c>), read when <see cref="AsOf"/> is null. With neither, the fence has
        /// no upper edge and <c>window</c> cuts nothing; <c>from</c> and <c>to</c> still apply.
        /// </summary>
        public int? Now { get; set; }

        /// <summary>The reading's time, for the mail rule's thirty days (mail is dated by the clock). Null: the current time.</summary>
        public DateTimeOffset? Clock { get; set; }

        /// <summary>The world's members list, as <see cref="OWMessage.SenderStatus"/> reads it; null when unreadable.</summary>
        public IEnumerable<JToken> Members { get; set; }

        /// <summary>
        /// The ids the world's hidden mark keeps, beside each element's own <c>hidden</c>: a world-level list, as the
        /// TypeScript SDK and the Python package read a world's <c>hidden</c> (the lens profile's fixture worlds carry
        /// one). Null for none.
        /// </summary>
        public IEnumerable<string> HiddenIds { get; set; }
    }

    /// <summary>The mail rule's part of a reading: the reader's own messages, read through the message profile.</summary>
    public sealed class OWLensMail
    {
        internal OWLensMail(IReadOnlyList<string> unread, IReadOnlyList<string> read, IReadOnlyList<JObject> rows,
            IReadOnlyDictionary<string, int> cut, int totalUnread, int totalRead)
        {
            Unread = unread;
            Read = read;
            Rows = rows;
            Cut = cut;
            TotalUnread = totalUnread;
            TotalRead = totalRead;
        }

        /// <summary>The ids kept that are unread for the reader (<see cref="OWMessage.UnreadFor"/>), in <c>created_at</c> order.</summary>
        public IReadOnlyList<string> Unread { get; }

        /// <summary>The ids kept that the reader has read, in <c>created_at</c> order.</summary>
        public IReadOnlyList<string> Read { get; }

        /// <summary>
        /// Every kept message in <c>created_at</c> order: in full when unread (or under <c>mail: all</c>), else as a title
        /// (<c>id</c>, <c>name</c>, <c>narrator</c>, <c>created_at</c>). Bodies as this reader is served them.
        /// </summary>
        public IReadOnlyList<JObject> Rows { get; }

        /// <summary>What the mail part left out, by reason: <c>window</c> (older than thirty days), <c>budget</c> (the oldest first).</summary>
        public IReadOnlyDictionary<string, int> Cut { get; }

        /// <summary>The unread messages addressed to the reader that it may see, before the window and the budget.</summary>
        public int TotalUnread { get; }

        /// <summary>The read messages addressed to the reader that it may see, before the window and the budget.</summary>
        public int TotalRead { get; }
    }

    /// <summary><see cref="OWLens.Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/>'s answer.</summary>
    public sealed class OWLensResult
    {
        internal OWLensResult(IReadOnlyList<string> kept, IReadOnlyList<JObject> rows, IReadOnlyDictionary<string, int> cut,
            int notesIgnored, OWLensMail mail, string refusedKey)
        {
            Kept = kept;
            Rows = rows;
            Cut = cut;
            NotesIgnored = notesIgnored;
            Mail = mail;
            RefusedKey = refusedKey;
        }

        /// <summary>The kept elements' ids, in the budget's order: anchors first in the lens's link order, then what the hops reached.</summary>
        public IReadOnlyList<string> Kept { get; }

        /// <summary>
        /// The kept elements as <c>show</c> makes them, one per id in <see cref="Kept"/>: <c>full</c> (the body as this
        /// reader is served it, links to what it cannot see left out), <c>first_line</c> (<c>id</c>, <c>type</c>,
        /// <c>name</c>, <c>description</c> up to its first line break) or <c>name</c> (<c>id</c>, <c>type</c>,
        /// <c>name</c>); a kept anchor with a note carries it as <c>note</c>.
        /// </summary>
        public IReadOnlyList<JObject> Rows { get; }

        /// <summary>
        /// The cut, counted by reason (<see cref="OWLensCut"/>); a reason with no count is absent. The reader's counts,
        /// never the world's: "2 more" is two the reader could open.
        /// </summary>
        public IReadOnlyDictionary<string, int> Cut { get; }

        /// <summary>How many of <c>notes</c>' ids are not anchors of the lens (ignored, and counted).</summary>
        public int NotesIgnored { get; }

        /// <summary>The mail part when the lens names a mail rule; null under <c>mail: none</c>.</summary>
        public OWLensMail Mail { get; }

        /// <summary>
        /// The key that makes the lens unreadable (<see cref="OWLens.CheckKeys"/>), with nothing resolved; null when the
        /// lens is read.
        /// </summary>
        public string RefusedKey { get; }

        /// <summary>True when the lens's <c>x_lens</c> is legal and it was read.</summary>
        public bool IsReadable => RefusedKey == null;
    }

    /// <summary>
    /// Reads a lens: whether an element is one (<see cref="IsLens"/>) or a template (<see cref="IsTemplate"/>), whether its
    /// recipe is legal (<see cref="CheckKeys"/>), what it keeps for a reader
    /// (<see cref="Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/>), and which lenses a seat reads
    /// (<see cref="LensesFor"/>). Element bodies are <see cref="JObject"/>s as the API or a world folder gives them; a world
    /// is any sequence of them (a <see cref="JArray"/> too), and an entry that is not an object with a string id and one of
    /// the standard's types is passed by.
    /// </summary>
    /// <remarks>
    /// A lens only narrows: it is read inside what its reader may already see, never past it. The reader is pure: it
    /// never writes, and never changes a body it is given. A tool that writes a lens writes <c>x_lens</c> whole and keeps
    /// it byte for byte, and never writes <c>x_served_trimmed</c>.
    /// </remarks>
    public static class OWLens
    {
        /// <summary>The pair a lens is: type <c>construct</c>, supertype <c>Lens</c>, each trimmed and folded as the room profile defines them.</summary>
        public static readonly (string type, string supertype) Pair = ("construct", "Lens");

        /// <summary>The subtype a template carries: a lens with no anchors that resolves to nothing until copied and pointed.</summary>
        public const string TemplateSubtype = "Template";

        /// <summary>The extension field that holds the recipe, a JSON object; every key optional, <c>null</c> absent.</summary>
        public const string Field = "x_lens";

        /// <summary>A folder Character's list of the lens ids it reads (a folder world has no membership).</summary>
        public const string UsesField = "x_lens_uses";

        /// <summary>
        /// The anchor fields: the Construct's Involves links in the schema's order, less <c>titles</c> (on a lens, the
        /// Titles it is the default for). <c>founder</c> and <c>custodian</c> are never anchors.
        /// </summary>
        public static readonly IReadOnlyList<string> AnchorFields = new[]
        {
            "characters", "objects", "locations", "species", "creatures", "institutions", "traits", "collectives", "zones",
            "abilities", "phenomena", "languages", "families", "relations", "constructs", "events", "narratives",
        };

        /// <summary>
        /// The date table, a type slug to its date field. The ten types not in it are timeless and never fenced. The
        /// fence, <c>undated</c>, <c>window</c>, <c>from</c> and <c>to</c> all read it, and so does <see cref="DateOf"/>.
        /// Read-only, also through a cast: a caller cannot change it under the fence.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DateFields =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                ["character"] = "birth_date", ["creature"] = "birth_date", ["institution"] = "founding_date",
                ["location"] = "founding_date", ["collective"] = "formation_date", ["law"] = "date", ["title"] = "grant_date",
                ["construct"] = "start_date", ["event"] = "start_date", ["narrative"] = "start_date",
                ["relation"] = "start_date", ["zone"] = "start_date",
            });

        /// <summary>
        /// An element's date as the lens profile reads it: the whole number in its type's date field
        /// (<see cref="DateFields"/>), in the world's own time unit. Null for a timeless type, a body with no known
        /// <c>type</c> (trimmed and folded, as the reader reads it), a date field that is absent, null or not a whole
        /// number, and anything that is not an element body.
        /// </summary>
        /// <remarks>
        /// It reads the body it is given: a known-of stub serves no date, so it reads null, whatever the element's own
        /// date is. A timeline or a horizon that dates elements can read this instead of copying the table, and dates them
        /// exactly as the reader's fence does. Mirrors the TypeScript SDK's <c>dateOf</c> (4.96.0) and the Python
        /// package's <c>date_of</c> (0.79.0).
        /// </remarks>
        public static long? DateOf(JToken element)
        {
            if (!(element is JObject body)) return null;
            var type = StringOf(body["type"]);
            if (type == null || !DateFields.TryGetValue(OWProfileText.Fold(type), out var field)) return null;
            return Whole(body[field], out var date) ? date : (long?)null;
        }

        /// <summary><c>depth</c>'s default: the anchors and what links to or from them.</summary>
        public const int DefaultDepth = 1;

        /// <summary><c>budget</c>'s default, in elements.</summary>
        public const int DefaultBudget = 50;

        /// <summary><c>undated</c>'s default: an undated element under the fence is kept.</summary>
        public const string DefaultUndated = "include";

        /// <summary><c>show</c>'s default: the body as the reader is served it.</summary>
        public const string DefaultShow = "full";

        /// <summary><c>mail</c>'s default: no mail part.</summary>
        public const string DefaultMail = "none";

        /// <summary>The mail rule's window, in days on <c>created_at</c> before the reading's time.</summary>
        public const int MailDays = 30;

        /// <summary>The deepest hop a reader serves; <c>depth</c> 2 is legal and its second hop is counted, never served.</summary>
        private const int ServedDepth = 1;

        private static readonly string[] UndatedValues = { "include", "exclude" };
        private static readonly string[] ShowValues = { "full", "first_line", "name" };
        private static readonly string[] MailValues = { "unread_full_read_titles", "all", "none" };

        /// <summary>The keys a known-of stub keeps (the API's stub, less the type's public fields).</summary>
        private static readonly string[] StubKeys =
        {
            "type", "id", "name", "description", "supertype", "subtype", "image_url", "created_at", "updated_at", "change_seq",
        };

        /// <summary>
        /// True when the element is a lens: its <c>type</c> and <c>supertype</c>, each trimmed and folded as the room
        /// profile defines them, are <see cref="Pair"/>. The match is the whole word (<c>Lenses</c> is not one).
        /// </summary>
        public static bool IsLens(JObject element)
        {
            var type = StringOf(element?["type"]);
            var supertype = StringOf(element?["supertype"]);
            return type != null && supertype != null && OWProfileText.Fold(type) == OWProfileText.Fold(Pair.type)
                   && OWProfileText.Fold(supertype) == OWProfileText.Fold(Pair.supertype);
        }

        /// <summary>
        /// True when the element is a template: a lens (<see cref="IsLens"/>) whose <c>subtype</c>, trimmed and folded, is
        /// <see cref="TemplateSubtype"/>. A template resolves to nothing, anchors or not. Another Construct with that
        /// subtype is no template.
        /// </summary>
        public static bool IsTemplate(JObject element)
        {
            var subtype = StringOf(element?["subtype"]);
            return IsLens(element) && subtype != null
                   && OWProfileText.Fold(subtype) == OWProfileText.Fold(TemplateSubtype);
        }

        /// <summary>
        /// Null when <c>x_lens</c> is legal, else the first key refused, in the profile's table order (<c>depth</c>,
        /// <c>types</c>, <c>window</c>, <c>from</c>, <c>to</c>, <c>undated</c>, <c>show</c>, <c>budget</c>, <c>mail</c>,
        /// <c>notes</c>), or <c>x_lens</c> itself when the value is not an object. Absent and <c>null</c> are legal, a
        /// key whose value is <c>null</c> is absent, and a key the profile does not name is ignored.
        /// </summary>
        /// <remarks>
        /// <c>depth</c> 0, 1 or 2; <c>types</c> a list of type slugs or room pairs (<c>narrative/message</c>), each word
        /// trimmed and folded; <c>window</c> a whole number from 0, never beside <c>from</c> or <c>to</c>; <c>from</c> a
        /// whole number; <c>to</c> a whole number after <c>from</c>, never alone; <c>undated</c> <c>include</c> or
        /// <c>exclude</c>; <c>show</c> <c>full</c>, <c>first_line</c> or <c>name</c>; <c>budget</c> 1 to 1000;
        /// <c>mail</c> <c>unread_full_read_titles</c>, <c>all</c> or <c>none</c>; <c>notes</c> an object of strings. A
        /// whole number is a JSON integer (a boolean or a float is not); one beyond the 64-bit range is refused.
        /// </remarks>
        public static string CheckKeys(JToken xLens)
        {
            if (xLens == null || xLens.Type == JTokenType.Null) return null;
            if (!(xLens is JObject x)) return Field;

            bool Has(string key, out JToken value) => x.TryGetValue(key, out value) && value.Type != JTokenType.Null;

            if (Has("depth", out var depth) && !(Whole(depth, out var d) && d >= 0 && d <= 2)) return "depth";
            if (Has("types", out var types) && !(types is JArray list && list.All(IsTypeWord))) return "types";
            var hasWindow = Has("window", out var window);
            if (hasWindow && !(Whole(window, out var w) && w >= 0)) return "window";
            var hasFrom = Has("from", out var from);
            var hasTo = Has("to", out var to);
            if (hasWindow && (hasFrom || hasTo)) return "window";
            long f = 0;
            if (hasFrom && !Whole(from, out f)) return "from";
            if (hasTo && !(Whole(to, out var t) && hasFrom && f < t)) return "to";
            if (Has("undated", out var undated) && !IsOneOf(undated, UndatedValues)) return "undated";
            if (Has("show", out var show) && !IsOneOf(show, ShowValues)) return "show";
            if (Has("budget", out var budget) && !(Whole(budget, out var b) && b >= 1 && b <= 1000)) return "budget";
            if (Has("mail", out var mail) && !IsOneOf(mail, MailValues)) return "mail";
            if (Has("notes", out var notes)
                && !(notes is JObject n && n.Properties().All(p => p.Value.Type == JTokenType.String)))
                return "notes";
            return null;
        }

        /// <summary>
        /// The lenses a seat reads: its membership's choice when it has made one (an empty choice is a choice), else its
        /// folder Character's <c>x_lens_uses</c>, else none. The membership wins, so the list is never kept in two places.
        /// </summary>
        /// <param name="membership">The membership's chosen lens ids; null when it has made no choice (or there is none, a folder).</param>
        /// <param name="characterUses">The Character's <see cref="UsesField"/>; null when absent.</param>
        public static IReadOnlyList<string> LensesFor(IEnumerable<string> membership, IEnumerable<string> characterUses)
        {
            if (membership != null) return membership.ToList();
            return characterUses?.ToList() ?? new List<string>();
        }

        /// <summary>
        /// What a lens keeps for a reader: the profile's <c>resolveLens(world, lens, reader)</c>. One pure function over a
        /// world snapshot: it returns the kept elements in order, the cut counted by reason, and, when the lens names a
        /// mail rule, the mail part. It never writes, and never changes a body it is given.
        /// </summary>
        /// <param name="world">
        /// The world's elements as its holder has them: the whole world (a folder, the owner's pull) for every rung but
        /// <see cref="OWLensRung.Served"/>, whose bodies are already a key's served view. The reader's view is built from
        /// them first (visibility before resolution): a hidden or proposed element is not there to hop through.
        /// </param>
        /// <param name="lens">The lens's body.</param>
        /// <param name="reader">Who reads (<see cref="OWLensReader"/>).</param>
        /// <param name="options">The reading's date and time and the world's members list; null for none.</param>
        /// <remarks>
        /// <para>
        /// The rules, in short. A hop reaches both ways, through what this reader is served. A Relation a hop lands on is
        /// an edge: in the same hop it reaches every element its own links name (never what links to it, never on through
        /// a second Relation), whether or not <c>types</c> keeps it. A hop never crosses a room element; one a hop
        /// reaches is kept only when <c>types</c> names its pair. A known-of stub is kept as served and nothing hops out
        /// of it. An umbrella enters only its own <c>constructs</c> lenses, each once, the union in link order; a lens a
        /// hop reaches is kept and never entered; the lens being read is never in its own result. The order is anchors
        /// first in the lens's link order, then by type in the schema's order, then newest first (<c>updated_at</c>), then
        /// by id; the budget keeps the first and counts the rest.
        /// </para>
        /// <para>
        /// The fence is the reading's <c>as_of</c>, on each element's own date (a stub's missing date is never read as
        /// undated); <c>undated</c>, the fence and the date table apply to anchors too, <c>types</c> and the window only
        /// to what a hop or a filter reaches. A date is a whole number in the world's own unit and is compared as it is,
        /// never filled to a finer one. A lens with no anchors is a filter over the whole visible world (types only;
        /// what is not selected is not counted) unless it is a template; one whose anchors were all left out for this
        /// reader (or whose <c>x_served_trimmed</c> names an anchor field) is anchored and resolves to what is left.
        /// </para>
        /// </remarks>
        public static OWLensResult Resolve(IEnumerable<JToken> world, JObject lens, OWLensReader reader,
            OWLensOptions options = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var view = new View(world, reader, options?.HiddenIds);
            return Finish(new Reading(view, options), lens, out _);
        }

        /// <summary>
        /// <see cref="Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/> for a lens named by its id in
        /// the world. A lens the reader is not served whole resolves to nothing.
        /// </summary>
        public static OWLensResult Resolve(IEnumerable<JToken> world, string lensId, OWLensReader reader,
            OWLensOptions options = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var view = new View(world, reader, options?.HiddenIds);
            var lens = lensId != null && view.Whole.Contains(lensId) ? view.Els[lensId] : null;
            return Finish(new Reading(view, options), lens, out _);
        }

        /// <summary>The bodies a reader is served, in the world's order: stubs as stubs, links to what it cannot see left out.</summary>
        internal static List<JObject> ServedView(IEnumerable<JToken> world, OWLensReader reader)
        {
            var view = new View(world, reader);
            return view.Order.Where(view.Sees).Select(view.Served).ToList();
        }

        /// <summary>A type's link fields in the schema's order, each a single link or a list, from the generated models.</summary>
        internal static IReadOnlyList<(string field, bool many)> LinkFieldsOf(string type)
        {
            return type != null && LinkTable.TryGetValue(type, out var fields) ? fields : Array.Empty<(string, bool)>();
        }

        // -- Resolution ----------------------------------------------------------------

        /// <summary>
        /// A lens read over one reading's world: the result <see cref="Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/>
        /// answers, and <paramref name="cutById"/>, the reason each element it left out was cut, by id, before the counts
        /// drop the owner's two (the graph reader counts a link by its target's reason). An element that is not a lens
        /// resolves to nothing, its <c>x_lens</c> unread.
        /// </summary>
        internal static OWLensResult Finish(Reading reading, JObject lens, out IReadOnlyDictionary<string, string> cutById)
        {
            var empty = new OWLensResult(new List<string>(), new List<JObject>(), new Dictionary<string, int>(), 0, null, null);
            cutById = new Dictionary<string, string>();
            if (lens == null || !IsLens(lens)) return empty;
            var bad = CheckKeys(lens[Field]);
            if (bad != null)
                return new OWLensResult(new List<string>(), new List<JObject>(), new Dictionary<string, int>(), 0, null, bad);

            var part = Read(reading, lens, new HashSet<string>(), true);
            var view = reading.View;
            var counts = new Dictionary<string, int>();
            foreach (var reason in part.Cut.Values)
            {
                if (!view.OwnerCounts && Array.IndexOf(OWLensCut.OwnerOnly, reason) >= 0) continue;
                counts[reason] = counts.TryGetValue(reason, out var n) ? n + 1 : 1;
            }

            var rows = part.Kept.Select(i => Row(view, i, part.Recipe)).ToList();
            cutById = part.Cut;
            return new OWLensResult(part.Kept, rows, counts, part.NotesIgnored, part.Mail, null);
        }

        /// <summary>
        /// One lens's reading, the umbrella's children through it: kept ids, each cut id's first reason, the lenses
        /// visited. <paramref name="top"/> is the lens being read (not a child entered): every lens entered is then
        /// left out of what it keeps and of its cut, since the lens read and every lens entered are never in their own
        /// result.
        /// </summary>
        private static Part Read(Reading reading, JObject lens, HashSet<string> visitedBefore, bool top)
        {
            var view = reading.View;
            var visited = new HashSet<string>(visitedBefore);
            var lensId = StringOf(lens["id"]);
            if (lensId != null) visited.Add(lensId);
            var part = new Part { Visited = visited, Recipe = Recipe.Of(lens) };
            if (CheckKeys(lens[Field]) != null || IsTemplate(lens)) return part;

            var r = part.Recipe;
            var cut = part.Cut;
            Anchors(view, lens, out var anchors, out var children, out var unseen);
            if (view.OwnerCounts)
            {
                foreach (var i in unseen)
                {
                    if (!visited.Contains(i)) SetDefault(cut, i, OWLensCut.AnchorNotVisible);
                }
            }

            var kept = new List<string>();
            var notVisible = new HashSet<string>();
            if (!NamesAnchors(lens))
            {
                // A filter over the whole visible world: what its types do not select is not counted.
                var pool = view.Order.Where(i => view.Sees(i) && !visited.Contains(i) && Admitted(view, i, r));
                foreach (var i in Ordered(view, pool))
                {
                    var reason = Fence(view, i, r, reading.AsOf) ?? Window(view, i, r, reading.AsOf);
                    if (reason != null) SetDefault(cut, i, reason);
                    else kept.Add(i);
                }
            }
            else
            {
                // An anchor that is the lens itself, or a lens already entered, is never in its own result.
                var read = anchors.Where(a => !visited.Contains(a)).ToList();
                foreach (var a in read)
                {
                    var reason = Fence(view, a, r, reading.AsOf);
                    if (reason != null) SetDefault(cut, a, reason);
                    else kept.Add(a);
                }

                var known = new HashSet<string>(anchors);
                known.UnionWith(visited);
                known.UnionWith(unseen);
                known.UnionWith(children);
                var frontier = read.Where(i => HopsOut(view, i, r, reading.AsOf)).ToList();
                var reached = new List<string>();
                for (var depth = 1; depth <= r.Depth; depth++)
                {
                    var ring = Hop(view, frontier, known, depth <= ServedDepth ? notVisible : null)
                        .Where(view.Sees).ToList();
                    if (depth > ServedDepth)
                    {
                        foreach (var i in ring)
                        {
                            if (!view.IsRoom(i) || Admitted(view, i, r)) SetDefault(cut, i, OWLensCut.HopsNotServed);
                        }
                    }
                    else
                    {
                        reached.AddRange(ring);
                    }

                    frontier = ring.Where(i => HopsOut(view, i, r, reading.AsOf)).ToList();
                }

                foreach (var i in Ordered(view, reached))
                {
                    var reason = Fence(view, i, r, reading.AsOf)
                                 ?? (Admitted(view, i, r) ? null : OWLensCut.Types)
                                 ?? Window(view, i, r, reading.AsOf);
                    if (reason != null) SetDefault(cut, i, reason);
                    else kept.Add(i);
                }

                foreach (var i in notVisible)
                {
                    if (!anchors.Contains(i) && !visited.Contains(i)) SetDefault(cut, i, OWLensCut.NotVisible);
                }
            }

            // The umbrella: each child once, by its own recipe, the union in link order.
            var keptSet = new HashSet<string>(kept);
            foreach (var child in children)
            {
                if (visited.Contains(child)) continue;
                var sub = Read(reading, view.Els[child], visited, false);
                visited.UnionWith(sub.Visited);
                foreach (var i in sub.Kept)
                {
                    if (keptSet.Add(i)) kept.Add(i);
                }

                foreach (var pair in sub.Cut) SetDefault(cut, pair.Key, pair.Value);
            }

            if (top)
            {
                kept = kept.Where(i => !visited.Contains(i)).ToList();
                foreach (var i in visited) cut.Remove(i);
            }

            if (r.Mail != "none")
            {
                part.Mail = MailOf(reading, r, out var inMail);
                kept = kept.Where(i => !inMail.Contains(i)).ToList();
            }

            foreach (var i in kept.Skip(r.Budget)) cut[i] = OWLensCut.Budget;
            if (kept.Count > r.Budget) kept = kept.Take(r.Budget).ToList();
            foreach (var i in kept) cut.Remove(i);

            part.Kept = kept;
            part.NotesIgnored = r.Notes.Keys.Count(i => !anchors.Contains(i));
            return part;
        }

        /// <summary>
        /// One hop from the frontier, both ways, through what this reader is served; a Relation reached is an edge to
        /// its own links in the same hop. The new ids in the order met. <paramref name="notVisible"/>, when given, gathers
        /// the owner's count: the links this reader's view stops.
        /// </summary>
        private static List<string> Hop(View view, IEnumerable<string> frontier, HashSet<string> known, HashSet<string> notVisible)
        {
            var met = new List<string>();

            void Meet(string i)
            {
                if (known.Add(i)) met.Add(i);
            }

            foreach (var x in frontier)
            {
                foreach (var y in view.LinksOf(x))
                {
                    if (view.Sees(y)) Meet(y);
                }

                foreach (var i in view.LinkedFromWhole(x))
                {
                    if (i != x) Meet(i);
                }

                if (notVisible != null && view.OwnerCounts)
                {
                    // A link to an id in no element is a dangling link, not a visibility fact: never counted.
                    foreach (var n in view.LinksOf(x).Concat(view.LinkedFromAll(x)))
                    {
                        if (!view.Sees(n) && view.Els.ContainsKey(n)) notVisible.Add(n);
                    }
                }
            }

            foreach (var y in met.ToList())
            {
                if (view.TypeOf(y) != "relation" || !view.Whole.Contains(y) || view.IsRoom(y)) continue;
                foreach (var z in view.LinksOf(y))
                {
                    if (view.Sees(z)) Meet(z);
                    else if (notVisible != null && view.OwnerCounts && view.Els.ContainsKey(z)) notVisible.Add(z);
                }
            }

            return met;
        }

        /// <summary>
        /// The lens's anchors in its link order (the anchor fields in order, each field's array in order, each id once):
        /// those the reader sees, the child lenses it is served whole (an umbrella's), and those it cannot see.
        /// </summary>
        private static void Anchors(View view, JObject lens, out List<string> anchors, out List<string> children,
            out List<string> unseen)
        {
            anchors = new List<string>();
            children = new List<string>();
            unseen = new List<string>();
            var seen = new HashSet<string>();
            foreach (var field in AnchorFields)
            {
                if (!(lens[field] is JArray list)) continue;
                foreach (var entry in list)
                {
                    var i = StringOf(entry);
                    if (string.IsNullOrEmpty(i) || !seen.Add(i)) continue;
                    // An anchor in no element is a dangling link: skipped, and never counted as not visible.
                    if (!view.Els.TryGetValue(i, out var element)) continue;
                    if (field == "constructs" && IsLens(element))
                        (view.Whole.Contains(i) ? children : unseen).Add(i);
                    else if (view.Sees(i)) anchors.Add(i);
                    else unseen.Add(i);
                }
            }
        }

        /// <summary>
        /// True when the lens names an anchor, whoever reads it: an anchor field with an entry, or the server's
        /// <c>x_served_trimmed</c> naming one (its ids left out for this reader). Such a lens is never read as a filter.
        /// </summary>
        private static bool NamesAnchors(JObject lens)
        {
            if (AnchorFields.Any(f => lens[f] is JArray list && list.Count > 0)) return true;
            return lens[OWMessage.ServedTrimmedKey] is JArray trimmed
                   && trimmed.Any(entry => StringOf(entry) is string f && AnchorFields.Contains(f));
        }

        /// <summary>True when a hop goes on out of the element: served whole, not a room element, not after the fence.</summary>
        private static bool HopsOut(View view, string i, Recipe r, long? asOf) =>
            view.Whole.Contains(i) && !view.IsRoom(i) && Fence(view, i, r, asOf) != OWLensCut.AfterAsOf;

        /// <summary>
        /// True when the lens's <c>types</c> keeps the element: a room element only when its pair is named; any other when
        /// <c>types</c> is empty or names its type.
        /// </summary>
        private static bool Admitted(View view, string i, Recipe r)
        {
            var e = view.Els[i];
            if (view.IsRoom(i))
                return r.Types.Contains(OWProfileText.Fold(StringOf(e["type"])) + "/" + OWProfileText.Fold(StringOf(e["supertype"])));
            return r.Types.Count == 0 || r.Types.Contains(view.TypeOf(i));
        }

        /// <summary>The fence on the element's own date (never a stub's missing one): <c>after_as_of</c>, or <c>undated</c> under <c>exclude</c>.</summary>
        private static string Fence(View view, string i, Recipe r, long? asOf)
        {
            var kind = DateOf(view, i, out var date);
            if (kind == DateKind.Undated) return r.Undated == "exclude" ? OWLensCut.Undated : null;
            if (kind == DateKind.Dated && asOf.HasValue && date > asOf.Value) return OWLensCut.AfterAsOf;
            return null;
        }

        /// <summary>The window and the range, on a dated element: before <c>as_of</c> minus <c>window</c>, before <c>from</c>, at or after <c>to</c>.</summary>
        private static string Window(View view, string i, Recipe r, long? asOf)
        {
            if (DateOf(view, i, out var date) != DateKind.Dated) return null;
            if (r.Window.HasValue && asOf.HasValue && date < asOf.Value - r.Window.Value) return OWLensCut.Window;
            if (r.From.HasValue && date < r.From.Value) return OWLensCut.Window;
            if (r.To.HasValue && date >= r.To.Value) return OWLensCut.Window;
            return null;
        }

        private enum DateKind
        {
            Timeless,
            Undated,
            Dated,
        }

        /// <summary>The element's own date by the date table: a whole number in the world's unit, as it is.</summary>
        private static DateKind DateOf(View view, string i, out long date)
        {
            date = 0;
            var type = view.TypeOf(i);
            if (type == null || !DateFields.TryGetValue(type, out var field)) return DateKind.Timeless;
            return Whole(view.Els[i][field], out date) ? DateKind.Dated : DateKind.Undated;
        }

        /// <summary>By type in the schema's order, then newest first (<c>updated_at</c>), then by id by code point.</summary>
        private static IEnumerable<string> Ordered(View view, IEnumerable<string> ids)
        {
            return ids
                .OrderBy(i => view.TypeRank(i))
                .ThenByDescending(i => OWMessage.TimeKey(view.Els[i]["updated_at"]) ?? long.MinValue)
                .ThenBy(i => i, CodePointOrder)
                .ToList();
        }

        private static readonly char[] LineBreaks = { '\r', '\n' };

        /// <summary>A kept element as <c>show</c> makes it, from the body this reader is served, with its note.</summary>
        private static JObject Row(View view, string i, Recipe r)
        {
            var body = view.Served(i);
            JObject row;
            if (r.Show == "name" || r.Show == "first_line")
            {
                row = new JObject
                {
                    ["id"] = Copy(body["id"]),
                    ["type"] = Copy(body["type"]),
                    ["name"] = Copy(body["name"]),
                };
                // Up to the first line break, CR or LF; a body with no string description has none in its row.
                if (r.Show == "first_line" && StringOf(body["description"]) is string description)
                {
                    var lineBreak = description.IndexOfAny(LineBreaks);
                    row["description"] = lineBreak < 0 ? description : description.Substring(0, lineBreak);
                }
            }
            else
            {
                row = body;
            }

            if (r.Notes.TryGetValue(i, out var note)) row["note"] = note;
            return row;
        }

        /// <summary>
        /// The mail rule, through the message profile: the messages addressed to the reader's Character and not its own,
        /// unread by its own read mark, the sender verified; thirty days on <c>created_at</c> before the reading's time,
        /// the newest kept up to the budget; unread in full and read as a title, or every one in full under <c>all</c>.
        /// </summary>
        private static OWLensMail MailOf(Reading reading, Recipe r, out HashSet<string> ids)
        {
            var view = reading.View;
            var me = view.Character;
            var members = reading.Members;
            var messages = view.WholeSorted.Where(i => OWMessage.IsMessage(view.Els[i])).Select(view.Served).ToList();
            var relations = view.WholeSorted.Where(i => view.TypeOf(i) == "relation").Select(view.Served).ToList();
            var unread = new HashSet<string>();
            var own = new HashSet<string>();
            var mine = new List<JObject>();
            if (me != null)
            {
                var mark = OWMessage.ReadMarkOf(relations, me, members);
                foreach (var m in OWMessage.UnreadFor(messages, me, mark, members)) unread.Add(StringOf(m["id"]));
                foreach (var m in messages)
                {
                    if (OWMessage.IsSentBy(m, me) && !unread.Contains(StringOf(m["id"]))) own.Add(StringOf(m["id"]));
                }

                mine = messages.Where(m => OWMessage.IsAddressedTo(m, me) && !own.Contains(StringOf(m["id"]))).ToList();
            }

            var totalUnread = mine.Count(m => unread.Contains(StringOf(m["id"])));
            var totalRead = mine.Count - totalUnread;
            var since = reading.Clock.UtcTicks / 10 - (long)MailDays * 24 * 60 * 60 * 1000 * 1000;
            var cut = new Dictionary<string, int>();
            var recent = new List<JObject>();
            foreach (var m in OWMessage.InCreatedOrder(mine))
            {
                var at = OWMessage.TimeKey(m["created_at"]);
                if (at == null || at < since) cut[OWLensCut.Window] = (cut.TryGetValue(OWLensCut.Window, out var n) ? n : 0) + 1;
                else recent.Add(m);
            }

            if (recent.Count > r.Budget)
            {
                cut[OWLensCut.Budget] = recent.Count - r.Budget;
                recent = recent.Skip(recent.Count - r.Budget).ToList();
            }

            var rows = recent.Select(m => r.Mail == "all" || unread.Contains(StringOf(m["id"]))
                    ? m
                    : new JObject
                    {
                        ["id"] = Copy(m["id"]),
                        ["name"] = Copy(m["name"]),
                        ["narrator"] = Copy(m["narrator"]),
                        ["created_at"] = Copy(m["created_at"]),
                    })
                .ToList();
            ids = new HashSet<string>(recent.Select(m => StringOf(m["id"])));
            return new OWLensMail(
                recent.Select(m => StringOf(m["id"])).Where(unread.Contains).ToList(),
                recent.Select(m => StringOf(m["id"])).Where(i => !unread.Contains(i)).ToList(),
                rows, cut, totalUnread, totalRead);
        }

        // -- The pieces ------------------------------------------------------------------

        /// <summary>
        /// What one reader is served of a world: whole bodies, known-of stubs, the links between them. An entry whose
        /// type, trimmed and folded, is not one of the standard's is not in the world (the TypeScript SDK's and the
        /// Python package's readers pass it by too).
        /// </summary>
        internal sealed class View
        {
            public readonly Dictionary<string, JObject> Els = new Dictionary<string, JObject>();
            public readonly List<string> Order = new List<string>();
            public readonly HashSet<string> Whole = new HashSet<string>();
            public readonly HashSet<string> KnownOf = new HashSet<string>();

            /// <summary>The ids written as proposed (a mark or a doubt): in no reader's view, the owner's included.</summary>
            public readonly HashSet<string> Proposed = new HashSet<string>();

            public readonly List<string> WholeSorted;
            public readonly bool OwnerCounts;
            public readonly string Character;

            /// <summary>The rung the view is built from: the reader's own, or on the owner reading as another, that reader's.</summary>
            public readonly OWLensRung Rung;

            private readonly Dictionary<string, List<string>> _links = new Dictionary<string, List<string>>();
            private readonly Dictionary<string, List<string>> _fromWhole = new Dictionary<string, List<string>>();
            private readonly Dictionary<string, List<string>> _fromAll = new Dictionary<string, List<string>>();
            private readonly Dictionary<string, bool> _room = new Dictionary<string, bool>();

            /// <summary>
            /// The ids under the hidden mark: <paramref name="hiddenIds"/> (the world's own list) and each element whose
            /// own <c>hidden</c> marks it (<see cref="OWPayload.IsHidden"/>).
            /// </summary>
            public readonly HashSet<string> Hidden = new HashSet<string>();

            public View(IEnumerable<JToken> world, OWLensReader reader, IEnumerable<string> hiddenIds = null)
            {
                if (hiddenIds != null) Hidden.UnionWith(hiddenIds.Where(i => i != null));
                OwnerCounts = reader.OwnerCounts;
                Character = reader.Character;
                Rung = reader.Rung;
                foreach (var entry in world ?? Enumerable.Empty<JToken>())
                {
                    if (!(entry is JObject e)) continue;
                    var id = StringOf(e["id"]);
                    if (string.IsNullOrEmpty(id) || Els.ContainsKey(id)) continue;
                    var type = StringOf(e["type"]);
                    if (type == null || Array.IndexOf(OWElementTypes.Slugs, OWProfileText.Fold(type)) < 0) continue;
                    Els[id] = e;
                    Order.Add(id);
                    if (OWPayload.IsHidden(e)) Hidden.Add(id);
                }

                foreach (var id in Order)
                {
                    var e = Els[id];
                    // The review profile's rule for a reader that hands a world on: a mark or a doubt, any value but null.
                    if (OWPayload.IsProposedOrInDoubt(e))
                    {
                        Proposed.Add(id);
                        continue;
                    }

                    switch (reader.Rung)
                    {
                        case OWLensRung.Owner:
                            Whole.Add(id);
                            break;
                        case OWLensRung.Contributor:
                            if (!Hidden.Contains(id)) Whole.Add(id);
                            break;
                        case OWLensRung.Public:
                            if (!Hidden.Contains(id) && !IsRoom(id)) Whole.Add(id);
                            break;
                        case OWLensRung.Guest:
                            if (reader.Whole.Contains(id)) Whole.Add(id);
                            else if (reader.KnownOf.Contains(id)) KnownOf.Add(id);
                            break;
                        case OWLensRung.Served:
                            if (OWPayload.IsKnownOf(e)) KnownOf.Add(id);
                            else Whole.Add(id);
                            break;
                    }
                }

                WholeSorted = Whole.OrderBy(i => i, StringComparer.Ordinal).ToList();
                foreach (var id in Order)
                {
                    foreach (var y in LinksOf(id).Distinct())
                    {
                        Add(_fromAll, y, id);
                    }
                }

                foreach (var id in WholeSorted)
                {
                    foreach (var y in LinksOf(id).Distinct())
                    {
                        Add(_fromWhole, y, id);
                    }
                }
            }

            public bool Sees(string i) => Whole.Contains(i) || KnownOf.Contains(i);

            /// <summary>The type slug, folded; null for a body with none.</summary>
            public string TypeOf(string i)
            {
                var type = StringOf(Els[i]["type"]);
                return type == null ? null : OWProfileText.Fold(type);
            }

            /// <summary>The type's place in the schema's order; a type outside the standard after every one in it.</summary>
            public int TypeRank(string i)
            {
                var rank = Array.IndexOf(OWElementTypes.Slugs, TypeOf(i));
                return rank < 0 ? OWElementTypes.Slugs.Length : rank;
            }

            public bool IsRoom(string i)
            {
                if (!_room.TryGetValue(i, out var room)) _room[i] = room = OWRoom.IsRoomElement(Els[i]);
                return room;
            }

            /// <summary>Every id the element's own body links to, in the schema's field order (each field's list in order).</summary>
            public List<string> LinksOf(string i)
            {
                if (_links.TryGetValue(i, out var cached)) return cached;
                var list = new List<string>();
                var body = Els[i];
                foreach (var (field, many) in LinkFieldsOf(TypeOf(i)))
                {
                    var value = body[field];
                    if (many)
                    {
                        if (!(value is JArray array)) continue;
                        foreach (var entry in array)
                        {
                            var id = StringOf(entry);
                            if (!string.IsNullOrEmpty(id)) list.Add(id);
                        }
                    }
                    else
                    {
                        var id = StringOf(value);
                        if (!string.IsNullOrEmpty(id)) list.Add(id);
                    }
                }

                _links[i] = list;
                return list;
            }

            /// <summary>The ids served whole whose bodies link to the element, by id.</summary>
            public IReadOnlyList<string> LinkedFromWhole(string i) =>
                _fromWhole.TryGetValue(i, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

            /// <summary>The ids in the whole world (the owner's view) whose bodies link to the element.</summary>
            public IReadOnlyList<string> LinkedFromAll(string i) =>
                _fromAll.TryGetValue(i, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

            /// <summary>
            /// The body this reader is served, a copy: a stub's keys and <c>known_of: true</c>, or the whole body with
            /// every link to an element it cannot see left out (a single link null, a list without the id).
            /// </summary>
            public JObject Served(string i)
            {
                var e = Els[i];
                if (KnownOf.Contains(i))
                {
                    var stub = new JObject();
                    foreach (var p in e.Properties())
                    {
                        if (Array.IndexOf(StubKeys, p.Name) >= 0) stub[p.Name] = p.Value.DeepClone();
                    }

                    stub[OWPayload.KnownOf] = true;
                    return stub;
                }

                var body = (JObject)e.DeepClone();
                var trimmed = new List<string>();
                foreach (var (field, many) in LinkFieldsOf(TypeOf(i)))
                {
                    if (!body.TryGetValue(field, out var value)) continue;
                    if (many)
                    {
                        var kept = new JArray();
                        var cut = false;
                        if (value is JArray array)
                        {
                            foreach (var entry in array)
                            {
                                var id = StringOf(entry);
                                if (id != null && Sees(id)) kept.Add(entry.DeepClone());
                                else if (id != null) cut = true;
                            }
                        }

                        body[field] = kept;
                        if (cut) trimmed.Add(field);
                    }
                    else
                    {
                        var id = StringOf(value);
                        if (string.IsNullOrEmpty(id) || !Sees(id))
                        {
                            body[field] = JValue.CreateNull();
                            if (id != null) trimmed.Add(field);
                        }
                    }
                }

                // A served message or lens names the link fields its reader's view trimmed, as the server marks them
                // (OWMessage.ServedTrimmedKey), so a reader of the body knows a list was cut, not empty.
                if (trimmed.Count > 0 && (OWMessage.IsMessage(body) || IsLens(body)))
                {
                    var mark = body[OWMessage.ServedTrimmedKey] is JArray old ? (JArray)old.DeepClone() : new JArray();
                    foreach (var field in trimmed)
                    {
                        if (!mark.Any(entry => StringOf(entry) == field)) mark.Add(field);
                    }

                    body[OWMessage.ServedTrimmedKey] = mark;
                }

                return body;
            }

            private static void Add(Dictionary<string, List<string>> index, string key, string value)
            {
                if (!index.TryGetValue(key, out var list)) index[key] = list = new List<string>();
                list.Add(value);
            }
        }

        /// <summary>One reading's moment and the world's facts, shared by an umbrella and its children.</summary>
        internal sealed class Reading
        {
            public Reading(View view, OWLensOptions options)
            {
                View = view;
                AsOf = options?.AsOf ?? options?.Now;
                Clock = options?.Clock ?? DateTimeOffset.UtcNow;
                Members = options?.Members;
            }

            public View View { get; }
            public long? AsOf { get; }
            public DateTimeOffset Clock { get; }
            public IEnumerable<JToken> Members { get; }
        }

        /// <summary>One lens's reading before the counts and rows: kept ids, each cut id's first reason, the lenses visited.</summary>
        private sealed class Part
        {
            public List<string> Kept = new List<string>();
            public readonly Dictionary<string, string> Cut = new Dictionary<string, string>();
            public HashSet<string> Visited;
            public Recipe Recipe;
            public OWLensMail Mail;
            public int NotesIgnored;
        }

        /// <summary>A legal <c>x_lens</c> read with its defaults.</summary>
        private sealed class Recipe
        {
            public int Depth = DefaultDepth;
            public int Budget = DefaultBudget;
            public string Undated = DefaultUndated;
            public string Show = DefaultShow;
            public string Mail = DefaultMail;
            public readonly List<string> Types = new List<string>();
            public long? Window;
            public long? From;
            public long? To;
            public readonly Dictionary<string, string> Notes = new Dictionary<string, string>();

            public static Recipe Of(JObject lens)
            {
                var r = new Recipe();
                if (!(lens?[Field] is JObject x)) return r;
                if (Whole(x["depth"], out var depth)) r.Depth = (int)depth;
                if (Whole(x["budget"], out var budget)) r.Budget = (int)budget;
                r.Undated = StringOf(x["undated"]) ?? r.Undated;
                r.Show = StringOf(x["show"]) ?? r.Show;
                r.Mail = StringOf(x["mail"]) ?? r.Mail;
                if (x["types"] is JArray types)
                {
                    foreach (var word in types) r.Types.Add(OWProfileText.Fold(StringOf(word) ?? ""));
                }

                if (Whole(x["window"], out var window)) r.Window = window;
                if (Whole(x["from"], out var from)) r.From = from;
                if (Whole(x["to"], out var to)) r.To = to;
                if (x["notes"] is JObject notes)
                {
                    foreach (var p in notes.Properties())
                    {
                        if (StringOf(p.Value) is string sentence) r.Notes[p.Name] = sentence;
                    }
                }

                return r;
            }
        }

        // -- The generated models' links -------------------------------------------------

        /// <summary>
        /// Each type's link fields, from the generated models: the schema's field order (<see cref="OWElementTypes.FieldNames"/>),
        /// a list field a link list, a string field that is not a scalar string (<see cref="OWElementTypes.IsStringField"/>)
        /// a single link. The pin's <c>element_type</c> is the generic link's type word, not an id.
        /// </summary>
        private static readonly Dictionary<string, (string field, bool many)[]> LinkTable = BuildLinkTable();

        private static Dictionary<string, (string field, bool many)[]> BuildLinkTable()
        {
            var table = new Dictionary<string, (string, bool)[]>();
            foreach (var slug in OWElementTypes.Slugs)
            {
                var model = OWElementTypes.TypeFor(slug);
                var kinds = new Dictionary<string, Type>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var member in model.GetFields(flags))
                {
                    var name = member.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
                    if (name != null) kinds[name] = member.FieldType;
                }

                var fields = new List<(string, bool)>();
                foreach (var name in OWElementTypes.FieldNames[slug])
                {
                    if (name == "element_type" || !kinds.TryGetValue(name, out var kind)) continue;
                    if (typeof(IEnumerable<string>).IsAssignableFrom(kind) && kind != typeof(string)) fields.Add((name, true));
                    else if (kind == typeof(string) && !OWElementTypes.IsStringField(slug, name)) fields.Add((name, false));
                }

                table[slug] = fields.ToArray();
            }

            return table;
        }

        // -- Small readers ---------------------------------------------------------------

        /// <summary>
        /// A JSON number with no fraction as a long (<c>7</c> and <c>7.0</c> alike, as the TypeScript SDK and the Python
        /// package read it); a boolean, a string, a fraction, NaN or a number past 64 bits is not a whole number.
        /// </summary>
        private static bool Whole(JToken token, out long value)
        {
            value = 0;
            if (token is JValue f && f.Type == JTokenType.Float)
            {
                var d = f.Value is decimal m ? (double)m : Convert.ToDouble(f.Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d) return false;
                if (d < -9223372036854775808.0 || d >= 9223372036854775808.0) return false;
                value = (long)d;
                return true;
            }

            if (!(token is JValue v) || v.Type != JTokenType.Integer) return false;
            switch (v.Value)
            {
                case long l:
                    value = l;
                    return true;
                case int i:
                    value = i;
                    return true;
                case short s:
                    value = s;
                    return true;
                case byte b:
                    value = b;
                    return true;
                case sbyte sb:
                    value = sb;
                    return true;
                case ushort us:
                    value = us;
                    return true;
                case uint ui:
                    value = ui;
                    return true;
                case ulong ul when ul <= long.MaxValue:
                    value = (long)ul;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsOneOf(JToken token, string[] values) => StringOf(token) is string s && Array.IndexOf(values, s) >= 0;

        /// <summary>A type slug or a room pair (<c>narrative/message</c>), trimmed and folded.</summary>
        private static bool IsTypeWord(JToken token)
        {
            var word = StringOf(token);
            if (word == null) return false;
            var folded = OWProfileText.Fold(word);
            if (Array.IndexOf(OWElementTypes.Slugs, folded) >= 0) return true;
            return OWRoom.Pairs.Any(p => OWProfileText.Fold(p.type) + "/" + OWProfileText.Fold(p.supertype) == folded);
        }

        private static void SetDefault(Dictionary<string, string> cut, string id, string reason)
        {
            if (!cut.ContainsKey(id)) cut[id] = reason;
        }

        private static JToken Copy(JToken token) => token?.DeepClone() ?? JValue.CreateNull();

        /// <summary>A token's string, or null when it is not a JSON string.</summary>
        private static string StringOf(JToken token) =>
            token is JValue value && value.Type == JTokenType.String ? (string)value : null;

        /// <summary>
        /// Orders ids by Unicode code point, the tie the profile's reference readers break (the TypeScript SDK's and
        /// the Python package's): <see cref="OWCodePointComparer"/>, the order the folder format's id tie-breaks share.
        /// </summary>
        private static readonly IComparer<string> CodePointOrder = OWCodePointComparer.Instance;
    }
}
