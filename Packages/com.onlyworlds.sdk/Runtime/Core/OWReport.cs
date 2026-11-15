// The report profile's rule 9, threads (schema-dist profiles/report.md, serial 120): the reports a reader is given,
// grouped by their parent_narrative as written. A report is a Narrative with supertype Report and the room's mark; a
// board entry is one with supertype Board and the mark; both are told by the room profile's reader (OWRoom: the trim,
// the fold and the mark by presence), never a second copy. Inside a group the order is the message profile's
// created_at order (OWMessage.InCreatedOrder and its one instant reader, TimeKey).
//
// Written from the page and its fixture, profiles/report.fixture.json, which is this file's own test (OWReportTests),
// as it is the TypeScript SDK's (reportThreadsOf, 4.159.0) and the Python package's (report_threads_of, 0.120.0). The
// corners serial 120's fixture held no case for were read as both packages read them and pinned by tests of this SDK's
// own; serial 121 ruled each the same way on the page, and its fixture holds a case for each.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>What a report's <c>parent_narrative</c> names, as <see cref="OWReport.ThreadsOf"/> reads it.</summary>
    public static class OWReportParent
    {
        /// <summary>No parent: the field absent, null, not a string, or empty once trimmed of White_Space.</summary>
        public const string None = "none";

        /// <summary>A report the reader holds (the pair and the mark): the report is in that report's thread.</summary>
        public const string Report = "report";

        /// <summary>A board entry the reader holds (the pair <c>narrative</c> + <c>Board</c> and the mark): the report is one of the talks under it.</summary>
        public const string Board = "board";

        /// <summary>An id whose element the reader does not hold (a folder or an export without it, an id that dangles).</summary>
        public const string Missing = "missing";

        /// <summary>An element that is neither a report nor a board entry (a message, lore, a Report or Board Narrative without the mark).</summary>
        public const string Other = "other";

        /// <summary>The report's own id: read as naming no parent, with a <see cref="OWReportLintKind.Self"/> lint.</summary>
        public const string Self = "self";
    }

    /// <summary>The two lints of the report profile's rule 9.</summary>
    public static class OWReportLintKind
    {
        /// <summary>The report's parent is a report whose own parent is a report the reader holds: shown under the parent it names, never repaired.</summary>
        public const string Chain = "chain";

        /// <summary>The report names itself.</summary>
        public const string Self = "self";
    }

    /// <summary>One group: a report's thread, or the talks under a board entry.</summary>
    public sealed class OWReportGroup
    {
        internal OWReportGroup(string parent, string kind, IReadOnlyList<string> reports)
        {
            Parent = parent;
            Kind = kind;
            Reports = reports;
        }

        /// <summary>The parent's id, as the reports name it.</summary>
        public string Parent { get; }

        /// <summary><see cref="OWReportParent.Report"/> (a thread) or <see cref="OWReportParent.Board"/> (a board entry's talks).</summary>
        public string Kind { get; }

        /// <summary>
        /// The ids of the reports that name the parent, by <c>created_at</c> read as an instant (equal times keep the
        /// list's order; one that cannot be read after every one that can, in the list's order). The parent itself is
        /// not among them: a reader shows it first.
        /// </summary>
        public IReadOnlyList<string> Reports { get; }

        /// <summary><c>{parent, kind, reports}</c>, as the fixture writes a group.</summary>
        public JObject ToJson() => new JObject
        {
            ["parent"] = Parent,
            ["kind"] = Kind,
            ["reports"] = new JArray(Reports.Cast<object>().ToArray()),
        };
    }

    /// <summary>One lint: its kind and the report it is on.</summary>
    public sealed class OWReportLint
    {
        internal OWReportLint(string kind, string report)
        {
            Kind = kind;
            Report = report;
        }

        /// <summary><see cref="OWReportLintKind.Chain"/> or <see cref="OWReportLintKind.Self"/>.</summary>
        public string Kind { get; }

        /// <summary>The report's id.</summary>
        public string Report { get; }

        /// <summary><c>{kind, report}</c>, as the fixture writes a lint.</summary>
        public JObject ToJson() => new JObject { ["kind"] = Kind, ["report"] = Report };
    }

    /// <summary><see cref="OWReport.ThreadsOf"/>'s answer.</summary>
    public sealed class OWReportThreads
    {
        private readonly IReadOnlyList<KeyValuePair<string, string>> _parents;

        internal OWReportThreads(IReadOnlyList<KeyValuePair<string, string>> parents, IReadOnlyList<OWReportGroup> groups,
            IReadOnlyList<OWReportLint> lints)
        {
            _parents = parents;
            Parents = parents.ToDictionary(p => p.Key, p => p.Value);
            Groups = groups;
            Lints = lints;
        }

        /// <summary>For each report given, by its id, what its parent is (an <see cref="OWReportParent"/> word).</summary>
        public IReadOnlyDictionary<string, string> Parents { get; }

        /// <summary>The report ids of <see cref="Parents"/>, in the order the reports were given.</summary>
        public IEnumerable<string> Reports => _parents.Select(p => p.Key);

        /// <summary>
        /// One per parent that is a report or a board entry and that some report names, in the order its parent is
        /// first named in the list given. The page rules no order between groups: compare them as a set.
        /// </summary>
        public IReadOnlyList<OWReportGroup> Groups { get; }

        /// <summary>In the order of their reports. The page rules no order: compare them as a set.</summary>
        public IReadOnlyList<OWReportLint> Lints { get; }

        /// <summary><c>{parents, groups, lints}</c>, as the fixture writes a case's <c>want</c>; <c>parents</c> in the order given.</summary>
        public JObject ToJson()
        {
            var parents = new JObject();
            foreach (var p in _parents) parents[p.Key] = p.Value;
            return new JObject
            {
                ["parents"] = parents,
                ["groups"] = new JArray(Groups.Select(g => (object)g.ToJson()).ToArray()),
                ["lints"] = new JArray(Lints.Select(l => (object)l.ToJson()).ToArray()),
            };
        }
    }

    /// <summary>
    /// One line of a report's <c>consequences</c>, as <see cref="OWReport.ReadLine"/> reads it (the report profile's
    /// rule 10): the line, the words a reader shows, and the element the line names, if any.
    /// </summary>
    public sealed class OWReportLine
    {
        internal OWReportLine(string line, string words, string address, OWAddressReading reading, int? start, int? end)
        {
            Line = line;
            Words = words;
            Address = address;
            Reading = reading;
            Start = start;
            End = end;
        }

        /// <summary>The line as read.</summary>
        public string Line { get; }

        /// <summary>
        /// The words a reader shows: the line with its address taken out where nothing the writer wrote is lost, else
        /// the whole line.
        /// </summary>
        public string Words { get; }

        /// <summary>
        /// The element the line names, written <c>ow://&lt;type&gt;/&lt;id&gt;</c> with the id in lowercase, or null when
        /// no address in the line names an element of this world (which is no fault).
        /// </summary>
        public string Address { get; }

        /// <summary>What <see cref="OWAddress.ParseLink"/> reads in the address as written, or null.</summary>
        public OWAddressReading Reading { get; }

        /// <summary>
        /// Where the address as written starts in <see cref="Line"/>, a C# string index (UTF-16 code units), as
        /// <see cref="OWAddress.FindLinks"/> counts; null with no address.
        /// </summary>
        public int? Start { get; }

        /// <summary>The index just past the address as written; null with no address.</summary>
        public int? End { get; }
    }

    /// <summary>
    /// Reads the report profile's threads: which earlier report a report continues, and which board entry a closed
    /// talk's report hangs on. Element bodies are <see cref="JObject"/>s as the API or a world folder gives them.
    /// </summary>
    public static class OWReport
    {
        /// <summary>The pair a report is: type <c>narrative</c>, supertype <c>Report</c>. One of <see cref="OWRoom.Pairs"/>.</summary>
        public static readonly (string type, string supertype) Pair = ("narrative", "Report");

        /// <summary>The pair a board entry is: type <c>narrative</c>, supertype <c>Board</c>. One of <see cref="OWRoom.Pairs"/>.</summary>
        public static readonly (string type, string supertype) BoardPair = ("narrative", "Board");

        /// <summary>
        /// True when the element is a report: its <c>type</c> and <c>supertype</c>, trimmed and folded, are
        /// <see cref="Pair"/>, and it carries the room's mark <c>x_room_of</c>, read by presence
        /// (<see cref="OWRoom.IsRoomElement"/>). Without the mark it is lore (a scout's report in a novel).
        /// </summary>
        public static bool IsReport(JObject element) => OWRoom.IsRoomElementOf(element, Pair);

        /// <summary>True when the element is a board entry: <see cref="BoardPair"/> and the room's mark, read as <see cref="IsReport"/> reads them.</summary>
        public static bool IsBoardEntry(JObject element) => OWRoom.IsRoomElementOf(element, BoardPair);

        /// <summary>
        /// The reports among the narratives grouped into threads (the report profile's rule 9). Give it the Narratives
        /// as the reader is served them, in the order served; a reader is given the bodies its key may see, and a
        /// parent served to nobody reads as one not held.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only reports (<see cref="IsReport"/>) are read for a parent and grouped: a message or any other element that
        /// names a report is in no part of the answer. An entry that is not an object, or has no <c>id</c> that is a
        /// non-empty string, is passed over; of two with one id the first is read and the later copy is passed over
        /// whole (neither a report nor a parent).
        /// </para>
        /// <para>
        /// <see cref="OWReportThreads.Parents"/> reads each report's <c>parent_narrative</c>, as written:
        /// <see cref="OWReportParent.None"/> when it is absent, null, not a string (a number, a list, an expanded object)
        /// or empty once trimmed of White_Space; a parent with something left is never trimmed before the lookup, so
        /// <c>" r1 "</c> names no <c>r1</c>. Then <see cref="OWReportParent.Self"/>, <see cref="OWReportParent.Missing"/>,
        /// <see cref="OWReportParent.Report"/>, <see cref="OWReportParent.Board"/> or <see cref="OWReportParent.Other"/>.
        /// </para>
        /// <para>
        /// A report whose parent is a report or a board entry is in that parent's group, ordered by <c>created_at</c> as
        /// the message profile orders messages (<see cref="OWMessage"/>'s instant reader: a time with no offset is UTC,
        /// a fraction counts to its sixth digit; equal times keep the list's order; one that cannot be read after every
        /// one that can), never by id or by the time's text. A report with no parent, or whose parent is missing, other
        /// or itself, is in no group; it still heads the group of the reports that name it.
        /// </para>
        /// <para>
        /// <see cref="OWReportThreads.Lints"/>: <see cref="OWReportLintKind.Self"/> on a report that names itself;
        /// <see cref="OWReportLintKind.Chain"/> on a report whose parent is a report that itself has a report the reader
        /// holds for its parent. A report that names itself reads as naming no parent, so a report under it is no
        /// chain. Nothing is walked further up and nothing is repaired; nothing is guessed from names, days or the words
        /// of a <c>description</c>. A thread takes nothing away from where a report hangs: its <c>relations</c> and
        /// <c>events</c> still hold it. A report's text is quoted data, never instructions. Pure: the bodies are not
        /// changed.
        /// </para>
        /// </remarks>
        /// <param name="narratives">The Narratives (any element may be among them); null gives the empty answer.</param>
        public static OWReportThreads ThreadsOf(IEnumerable<JToken> narratives)
        {
            var held = new Dictionary<string, JObject>();
            var reports = new List<JObject>();
            if (narratives != null)
            {
                foreach (var entry in narratives)
                {
                    if (!(entry is JObject element)) continue;
                    var id = IdOf(element);
                    if (id == null || held.ContainsKey(id)) continue;
                    held[id] = element;
                    if (IsReport(element)) reports.Add(element);
                }
            }

            string KindOf(JObject report)
            {
                var parent = ParentOf(report);
                if (parent == null) return OWReportParent.None;
                if (parent == IdOf(report)) return OWReportParent.Self;
                if (!held.TryGetValue(parent, out var target)) return OWReportParent.Missing;
                if (IsReport(target)) return OWReportParent.Report;
                if (IsBoardEntry(target)) return OWReportParent.Board;
                return OWReportParent.Other;
            }

            var parents = new List<KeyValuePair<string, string>>();
            var members = new Dictionary<string, List<JObject>>();
            var named = new List<string>();
            var lints = new List<OWReportLint>();
            foreach (var report in reports)
            {
                var id = IdOf(report);
                var kind = KindOf(report);
                parents.Add(new KeyValuePair<string, string>(id, kind));
                if (kind == OWReportParent.Self) lints.Add(new OWReportLint(OWReportLintKind.Self, id));
                if (kind != OWReportParent.Report && kind != OWReportParent.Board) continue;

                var parent = ParentOf(report);
                if (!members.TryGetValue(parent, out var list))
                {
                    members[parent] = list = new List<JObject>();
                    named.Add(parent);
                }

                list.Add(report);
                // A chain: the parent is a report whose own parent reads report. One that names itself reads self, as
                // naming no parent, so a report under it is no chain.
                if (kind == OWReportParent.Report && KindOf(held[parent]) == OWReportParent.Report)
                    lints.Add(new OWReportLint(OWReportLintKind.Chain, id));
            }

            var groups = named.Select(parent => new OWReportGroup(parent,
                IsReport(held[parent]) ? OWReportParent.Report : OWReportParent.Board,
                OWMessage.InCreatedOrder(members[parent]).Select(IdOf).ToList())).ToList();
            return new OWReportThreads(parents, groups, lints);
        }

        /// <summary>
        /// One line of a report's <c>consequences</c>, read as the report profile reads it (rule 10, parts 2 and 3):
        /// the element it names, and the words a reader shows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The line's address is the last address in it, found as the address profile finds one in prose
        /// (<see cref="OWAddress.FindLinks"/>), that names an element of this world: an <c>ow://</c> or site address,
        /// canonical or not, whose world is <paramref name="world"/> or is left out, with a type word and an id. An
        /// address of another world, one whose world is a slug, a world's own address or a lens with no element is
        /// prose and stays in the words; a line with only those has no address, which is no fault. Whether the element
        /// still exists is the caller's to read: the line is read from its text alone.
        /// </para>
        /// <para>
        /// The address leaves the shown words only when nothing the writer wrote is lost: it is canonical, carries no
        /// moment and no lens, and stands apart (before it the line's start, White_Space or one colon; after it the
        /// line's end, White_Space or a closing mark, <c>. , ; : ! ) ] } ' " ’ ” »</c>), alone or inside a matched
        /// pair (<c>()</c>, <c>[]</c>, <c>""</c>, <c>''</c>, <c>“”</c>, <c>‘’</c>, <c>«»</c>). Then it goes with the
        /// White_Space on both sides and one colon before it, and the two halves join with one space, or none when the
        /// second is empty or begins with a closing mark. In every other shape, and when nothing stands before the
        /// address, the whole line is shown as written, still naming its element.
        /// </para>
        /// <para>
        /// The line is read as given, untrimmed: <see cref="ReadLines"/> splits and trims a whole text. The TypeScript
        /// SDK's <c>readReportLine</c> and the Python package's twin; the report profile's lines fixture is the test.
        /// </para>
        /// <code>
        /// var got = OWReport.ReadLine("Renamed the Consul's Steinway: ow://object/01a1d4ef-0000-7000-8000-000000000084.");
        /// // got.Words == "Renamed the Consul's Steinway."
        /// // got.Address == "ow://object/01a1d4ef-0000-7000-8000-000000000084"
        /// </code>
        /// </remarks>
        /// <param name="line">One line, as it stands.</param>
        /// <param name="world">
        /// This world's id, lowercase as the world holds it, compared exactly with the address's world (whose hex is read
        /// lowercase). Null: only an address with no world names an element here.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="line"/> is null.</exception>
        public static OWReportLine ReadLine(string line, string world = null)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            // Part 2: the last address that names an element of this world, from the text alone.
            OWAddressFound here = null;
            foreach (var f in OWAddress.FindLinks(line))
            {
                var r = f.Reading;
                if (r.Type == null || r.Id == null) continue;
                if (r.World == null || (!r.WorldIsSlug && world != null && string.Equals(r.World, world, StringComparison.Ordinal)))
                    here = f;
            }

            if (here == null) return new OWReportLine(line, line, null, null, null, null);

            var reading = here.Reading;
            int start = here.Start, end = here.End;
            var whole = new OWReportLine(line, line, "ow://" + reading.Type + "/" + reading.Id, reading, start, end);

            // Part 3: it leaves the words only when nothing the writer wrote is lost.
            if (!reading.Canonical || reading.Moment != null || reading.Lens != null) return whole;
            int from = start, to = end;
            if (start > 0 && CloserOf(line[start - 1]) is char closer)
            {
                // A matched pair goes with it (3a); an opener not closed right after keeps the whole line.
                if (end >= line.Length || line[end] != closer) return whole;
                from = start - 1;
                to = end + 1;
            }

            var apartBefore = from == 0 || OWProfileText.IsWhiteSpace(line[from - 1]) || line[from - 1] == ':';
            var apartAfter = to == line.Length || OWProfileText.IsWhiteSpace(line[to]) || IsClosingMark(line[to]);
            if (!apartBefore || !apartAfter) return whole;

            // The White_Space on both sides goes, and one colon before it with the White_Space before that colon (3c).
            var left = TrimEndWhiteSpace(line.Substring(0, from));
            if (left.Length > 0 && left[left.Length - 1] == ':') left = TrimEndWhiteSpace(left.Substring(0, left.Length - 1));
            var right = TrimStartWhiteSpace(line.Substring(to));

            // Nothing stands before the address (3b): the whole line stays, so a line is never shown empty.
            if (left.Length == 0) return whole;
            var words = right.Length == 0 ? left : IsClosingMark(right[0]) ? left + right : left + " " + right;
            return new OWReportLine(line, words, whole.Address, reading, start, end);
        }

        /// <summary>
        /// Every line of a report's <c>consequences</c>, each read by <see cref="ReadLine"/> (the report profile's rule
        /// 10, part 1). The text is split at every carriage return and every line feed, so a CRLF is one break and
        /// nothing else breaks a line (U+000B, U+000C, U+2028, U+2029 and U+0085 stay inside it); each piece is trimmed of
        /// Unicode White_Space at both ends, and a piece with nothing left is no line. A byte order mark is no
        /// White_Space: a line of one alone is a line.
        /// </summary>
        /// <param name="text">The whole value of <c>consequences</c>.</param>
        /// <param name="world">This world's id, as <see cref="ReadLine"/> takes it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public static IReadOnlyList<OWReportLine> ReadLines(string text, string world = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var lines = new List<OWReportLine>();
            foreach (var piece in text.Split('\r', '\n'))
            {
                var line = OWProfileText.TrimWhiteSpace(piece);
                if (line.Length > 0) lines.Add(ReadLine(line, world));
            }

            return lines;
        }

        /// <summary>The closing marks, character by character: a question mark is none (after an address it begins a query).</summary>
        private static bool IsClosingMark(char c)
        {
            switch (c)
            {
                case '.': case ',': case ';': case ':': case '!': case ')': case ']': case '}':
                case '\'': case '"': case '\u2019': case '\u201D': case '\u00BB':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The closer of a pair an address may stand in, by its opener; null for any other character (a brace makes no pair).</summary>
        private static char? CloserOf(char opener)
        {
            switch (opener)
            {
                case '(': return ')';
                case '[': return ']';
                case '"': return '"';
                case '\'': return '\'';
                case '\u201C': return '\u201D';
                case '\u2018': return '\u2019';
                case '\u00AB': return '\u00BB';
                default: return null;
            }
        }

        private static string TrimEndWhiteSpace(string text)
        {
            var end = text.Length;
            while (end > 0 && OWProfileText.IsWhiteSpace(text[end - 1])) end--;
            return text.Substring(0, end);
        }

        private static string TrimStartWhiteSpace(string text)
        {
            var start = 0;
            while (start < text.Length && OWProfileText.IsWhiteSpace(text[start])) start++;
            return text.Substring(start);
        }

        /// <summary>The element's id: a non-empty string, else null (no element to this reader).</summary>
        private static string IdOf(JObject element) =>
            element["id"] is JValue v && v.Type == JTokenType.String && v.Value is string id && id.Length > 0 ? id : null;

        /// <summary>
        /// <c>parent_narrative</c> as written, or null for no parent: absent, null, not a string, or nothing left once
        /// trimmed of White_Space (the room profile's trim). A parent with content is never trimmed.
        /// </summary>
        private static string ParentOf(JObject element)
        {
            if (!(element["parent_narrative"] is JValue v) || v.Type != JTokenType.String || !(v.Value is string parent))
                return null;
            return OWProfileText.TrimWhiteSpace(parent).Length == 0 ? null : parent;
        }
    }
}
