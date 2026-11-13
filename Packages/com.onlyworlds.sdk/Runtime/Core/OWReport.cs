// The report profile's rule 9, threads (schema-dist profiles/report.md, serial 120): the reports a reader is given,
// grouped by their parent_narrative as written. A report is a Narrative with supertype Report and the room's mark; a
// board entry is one with supertype Board and the mark; both are told by the room profile's reader (OWRoom: the trim,
// the fold and the mark by presence), never a second copy. Inside a group the order is the message profile's
// created_at order (OWMessage.InCreatedOrder and its one instant reader, TimeKey).
//
// Written from the page and its fixture, profiles/report.fixture.json, which is this file's own test (OWReportTests),
// as it is the TypeScript SDK's (reportThreadsOf, 4.159.0) and the Python package's (report_threads_of, 0.120.0). The
// corners the fixture holds no case for are read as both packages read them, and pinned by tests of this SDK's own.

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
