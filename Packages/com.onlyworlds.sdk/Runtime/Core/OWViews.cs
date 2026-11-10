// How an element is shown by its kind: the distribution's views.json sidecar (schema-dist serial 113 on; version 2 from
// serial 114), baked at codegen into OWViews.Table.cs by codegen/generate_views.py. Presentation like the colour groups
// (OWPresentation), not part of the standard, and nothing in it says who may see or do what: that is the server's, by
// what it serves.
//
// Ported from @onlyworlds/sdk src/views.ts (4.151.0, dd1f3dc), viewOf, viewIn and VIEWS, with its codegen's checks of
// the file (codegen/generate_types.py load_views). The file's own fixture, views.fixture.json, is this file's test
// (OWViewsTests), as it is the TypeScript SDK's. Version 2 states what version 1 left open (a tie on one rung, a row's
// own banner marks, the x_served_trimmed banner) and adds the order a page draws its fields in (OWView.Order).

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>A mark views.json shows at the head of every kind's page, and the page or document that defines it.</summary>
    public sealed class OWViewBanner
    {
        internal OWViewBanner(string mark, string definedBy)
        {
            Mark = mark;
            DefinedBy = definedBy;
        }

        /// <summary>The key an element carries (<c>hidden</c>, <c>x_review_proposed</c>).</summary>
        public string Mark { get; }

        /// <summary>The page or document that defines how the mark is read (<c>defined_by</c>).</summary>
        public string DefinedBy { get; }
    }

    /// <summary>
    /// What a row matches: a type alone, a type with a supertype, or a profile's pair (<c>type/Supertype</c>), which
    /// matches only with that profile's mark, by that profile's own reader.
    /// </summary>
    public sealed class OWViewKind
    {
        private OWViewKind(string type, string supertype, string profile, string pair)
        {
            Type = type;
            Supertype = supertype;
            Profile = profile;
            Pair = pair;
        }

        internal static OWViewKind OfType(string type, string supertype) => new OWViewKind(type, supertype, null, null);

        internal static OWViewKind OfProfile(string profile, string pair) =>
            new OWViewKind(pair.Substring(0, pair.IndexOf('/')), pair.Substring(pair.IndexOf('/') + 1), profile, pair);

        /// <summary>The type slug the row matches (for a profile's pair, the pair's type).</summary>
        public string Type { get; }

        /// <summary>The supertype the row matches; null for a row of a type alone.</summary>
        public string Supertype { get; }

        /// <summary>The profile whose reader matches the pair (<c>room</c>, <c>charter</c>); null for a type row.</summary>
        public string Profile { get; }

        /// <summary>The profile's pair as the file writes it (<c>narrative/Report</c>); null for a type row.</summary>
        public string Pair { get; }
    }

    /// <summary>A field a row shows folded, under its label.</summary>
    public sealed class OWViewFold
    {
        internal OWViewFold(string field, string label)
        {
            Field = field;
            Label = label;
        }

        /// <summary>The field.</summary>
        public string Field { get; }

        /// <summary>The words the fold is shown under.</summary>
        public string Label { get; }
    }

    /// <summary>The layout a row is drawn in: one of <see cref="OWViews.Layouts"/>, and the link a conversation follows.</summary>
    public sealed class OWViewLayout
    {
        internal OWViewLayout(string name, string by)
        {
            Name = name;
            By = by;
        }

        /// <summary>The layout's name (<c>document</c>, <c>conversation</c>).</summary>
        public string Name { get; }

        /// <summary>The link a conversation follows (<c>parent_narrative</c>); null when the layout names none.</summary>
        public string By { get; }
    }

    /// <summary>A row of views.json: how elements of one kind are shown.</summary>
    public sealed class OWViewRow
    {
        internal OWViewRow(string id, OWViewKind kind, string from, string[] sentence, string[] lead, OWViewFold[] fold,
            OWViewLayout layout, string[] banner)
        {
            Id = id;
            Kind = kind;
            From = from;
            Sentence = System.Array.AsReadOnly(sentence);
            Lead = System.Array.AsReadOnly(lead);
            Fold = System.Array.AsReadOnly(fold);
            Layout = layout;
            Banner = System.Array.AsReadOnly(banner);
        }

        /// <summary>The row's own name, distinct in the table (<c>report</c>, <c>room-line</c>).</summary>
        public string Id { get; }

        /// <summary>What the row matches.</summary>
        public OWViewKind Kind { get; }

        /// <summary>The page that defines the kind.</summary>
        public string From { get; }

        /// <summary>The fields that give the one sentence, the first that holds something winning; empty when the file names none.</summary>
        public IReadOnlyList<string> Sentence { get; }

        /// <summary>The fields shown open first, in order; empty when the file names none.</summary>
        public IReadOnlyList<string> Lead { get; }

        /// <summary>The fields shown folded, each under its label; empty when the file names none.</summary>
        public IReadOnlyList<OWViewFold> Fold { get; }

        /// <summary>The layout.</summary>
        public OWViewLayout Layout { get; }

        /// <summary>Marks this kind's page shows at its head after the table's own <see cref="OWViews.Banners"/>; empty when none.</summary>
        public IReadOnlyList<string> Banner { get; }
    }

    /// <summary><see cref="OWViews.ViewOf"/>'s answer for one element.</summary>
    public sealed class OWView
    {
        internal OWView(OWViewRow row, string sentence, IReadOnlyList<string> banners, IReadOnlyList<string> order)
        {
            Row = row;
            Sentence = sentence;
            Banners = banners;
            Order = order;
        }

        /// <summary>The row that shows it, the table's own; null when no row matches and the element draws as its type.</summary>
        public OWViewRow Row { get; }

        /// <summary>The one sentence for a card, a list row or a hover; null when there is no row or no named field holds anything.</summary>
        public string Sentence { get; }

        /// <summary>The marks to show at the head of its page, in order: the table's banners it carries, then the row's own.</summary>
        public IReadOnlyList<string> Banners { get; }

        /// <summary>
        /// The fields its page draws, in the order drawn (the file's <c>document</c> layout): the row's
        /// <see cref="OWViewRow.Lead"/>, then the rest of the type's section fields in the schema's own order, then each
        /// <see cref="OWViewRow.Fold"/>; each field once, and only a field that holds something. With no row, the type's
        /// section fields. A conversation draws each line's own fields this way. Empty for anything that is not an
        /// element.
        /// </summary>
        public IReadOnlyList<string> Order { get; }
    }

    /// <summary>How an element is shown by its kind, from the distribution's views.json as baked at this release.</summary>
    public static partial class OWViews
    {
        /// <summary>
        /// How an element is shown: the row of <see cref="Rows"/> that matches it, its one sentence, the marks to show
        /// at the head of its page, and the fields its page draws in order. Pass the element as the reader is served
        /// it: the banners read what was served (a known-of stub, the hidden mark, a trimmed list).
        /// </summary>
        /// <param name="element">An element body; anything that is not a JSON object has no row, no sentence, no banners and no fields drawn.</param>
        /// <remarks>
        /// <para>
        /// A row matches by its kind, its type and supertype trimmed and folded as the room profile defines once: a
        /// profile's pair only with that profile's mark, by the profile's own reader (<see cref="OWRoom.IsRoomElement"/>
        /// for room, <see cref="OWRoom.IsCharterElement"/> for charter); a type with a supertype; a type alone. The most
        /// specific row wins, in that order. Where two rows match on one rung (a table at fault: the codegen refuses
        /// one), the first in the table wins and the page is still drawn.
        /// </para>
        /// <para>
        /// The sentence is the first field the row names in <see cref="OWViewRow.Sentence"/> whose value is a string
        /// holding something after trimming White_Space; that value trimmed, cut at its first line break (U+000A or
        /// U+000D only: U+2028 is no cut), and trimmed again.
        /// </para>
        /// <para>
        /// The banners, in <see cref="Banners"/>' order: <c>known_of</c> and <c>hidden</c> only when <c>true</c>; the
        /// two review marks only when the value is an object with at least one key, whatever its keys (the review
        /// profile's rules 1, 4 and 5); <c>x_served_trimmed</c> only when it names a field (a list holding a string with
        /// something left after trimming White_Space). Then the row's own <see cref="OWViewRow.Banner"/> marks the
        /// element carries, each read as the table's are when it is one of them, otherwise by presence as the room
        /// profile reads its mark (present, not null, a string only when something is left after trimming).
        /// </para>
        /// <para>
        /// The order (the file's <c>document</c> layout): the row's lead, then the type's section fields in the schema's
        /// own order (<c>schema/&lt;type&gt;.yaml</c>, its sections and their fields as listed, as the walk reads them
        /// and the codegen bakes them) that the row puts in neither lead nor fold, then each fold; a field named in both lead and fold is drawn in the
        /// lead. A field that holds nothing (absent, null, a string with nothing left after trimming, an empty list, an
        /// object with no keys) is not drawn. The base fields (name, description, supertype, subtype, image_url) are the
        /// page's head, drawn here only where the row names one in lead or fold.
        /// </para>
        /// </remarks>
        public static OWView ViewOf(JToken element) => ViewOf(element, Banners, Rows);

        internal static OWView ViewOf(JToken element, IReadOnlyList<OWViewBanner> banners, IReadOnlyList<OWViewRow> rows)
        {
            if (!(element is JObject el)) return new OWView(null, null, new List<string>().AsReadOnly(), new List<string>().AsReadOnly());

            OWViewRow row = null;
            var best = 0;
            foreach (var r in rows)
            {
                var s = Score(r, el);
                if (s > best)
                {
                    best = s;
                    row = r;
                }
            }

            var shown = banners.Select(b => b.Mark).Where(m => Carries(el, m)).ToList();
            if (row == null) return new OWView(null, null, shown.AsReadOnly(), DrawOrder(null, el));
            foreach (var m in row.Banner)
            {
                if (Carries(el, m)) shown.Add(m);
            }

            return new OWView(row, SentenceOf(el, row), shown.AsReadOnly(), DrawOrder(row, el));
        }

        /// <summary>The fields drawn, in order: lead, then the type's section fields in neither lead nor fold, then fold; each once.</summary>
        private static IReadOnlyList<string> DrawOrder(OWViewRow row, JObject el)
        {
            var type = StringOf(el["type"]);
            var sections = type != null && SectionFields.TryGetValue(OWProfileText.Fold(type), out var names)
                ? names
                : (IReadOnlyList<string>)System.Array.Empty<string>();
            var lead = row?.Lead ?? (IReadOnlyList<string>)System.Array.Empty<string>();
            var folds = row == null ? new List<string>() : row.Fold.Select(f => f.Field).ToList();
            var drawn = new List<string>();

            void Draw(string field)
            {
                if (!drawn.Contains(field) && Holds(el[field])) drawn.Add(field);
            }

            foreach (var f in lead) Draw(f);
            foreach (var f in sections)
            {
                if (!lead.Contains(f) && !folds.Contains(f)) Draw(f);
            }

            foreach (var f in folds) Draw(f);
            return drawn.AsReadOnly();
        }

        /// <summary>A field holds nothing when absent, null, a string with nothing after trimming, an empty list, or an object with no keys.</summary>
        private static bool Holds(JToken v)
        {
            if (v == null) return false;
            switch (v.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return false;
                case JTokenType.String:
                    return OWProfileText.TrimWhiteSpace((string)v).Length > 0;
                case JTokenType.Array:
                case JTokenType.Object:
                    return v.HasValues;
                default:
                    return true;
            }
        }

        /// <summary>How specifically a row matches: 3 a profile's pair with its mark, 2 a type with a supertype, 1 a type alone, 0 not at all.</summary>
        private static int Score(OWViewRow row, JObject el)
        {
            var type = StringOf(el["type"]);
            if (type == null) return 0;
            var supertype = StringOf(el["supertype"]);
            var kind = row.Kind;
            if (OWProfileText.Fold(type) != OWProfileText.Fold(kind.Type)) return 0;
            if (kind.Profile != null)
            {
                if (supertype == null || OWProfileText.Fold(supertype) != OWProfileText.Fold(kind.Supertype)) return 0;
                switch (kind.Profile)
                {
                    case "room": return OWRoom.IsRoomElement(el) ? 3 : 0;
                    case "charter": return OWRoom.IsCharterElement(el) ? 3 : 0;
                    default: return 0;
                }
            }

            if (kind.Supertype == null) return 1;
            return supertype != null && OWProfileText.Fold(supertype) == OWProfileText.Fold(kind.Supertype) ? 2 : 0;
        }

        private static bool Carries(JObject el, string mark)
        {
            var v = el[mark];
            switch (mark)
            {
                case "hidden":
                case "known_of":
                    return v != null && v.Type == JTokenType.Boolean && (bool)v;
                case "x_served_trimmed":
                    // it names a field: a string with something left after trimming (a list of blanks names none)
                    return v is JArray list && list.Any(x => x.Type == JTokenType.String && OWProfileText.TrimWhiteSpace((string)x).Length > 0);
                case "x_review_proposed":
                case "x_review_checked":
                    return v is JObject o && o.Count > 0;
                default:
                    return OWRoom.HasMark(el, mark);
            }
        }

        private static string SentenceOf(JObject el, OWViewRow row)
        {
            foreach (var field in row.Sentence)
            {
                var v = StringOf(el[field]);
                if (v == null) continue;
                var text = OWProfileText.TrimWhiteSpace(v);
                if (text.Length == 0) continue;
                var cut = text.IndexOfAny(new[] { '\n', '\r' });
                return OWProfileText.TrimWhiteSpace(cut < 0 ? text : text.Substring(0, cut));
            }

            return null;
        }

        private static string StringOf(JToken token) =>
            token != null && token.Type == JTokenType.String ? (string)token : null;
    }
}
