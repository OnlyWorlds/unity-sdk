// The charter profile: a world's reading aids, the Charter (the rules here), the Guide (start here) and the Domains (the
// places in the world), each an element of one of the three pairs that carries the mark x_charter_of, read by presence
// (schema-dist profiles/charter.md, serial 83, ruled 2026-10-28). The pair-and-mark test is OWRoom.IsCharterElement;
// this file is the selection: which Charter and which Guide lead, the Domains' order, and the one packet every front
// door shows first.
//
// Written from the profile and its conformance fixture, profiles/charter.fixture.json, which is this file's own test
// (OWCharterTests); keel's reader (elements/charter.py, reading_aids; charter_skill in accounts/front_door.py) serves the
// same packet as reading_aids beside front_door on GET /me and the join reply. The packages' readers are charterOf and
// domainsOf (the Python package's charter_of, domains_of): here OWCharter.CharterOf and OWCharter.DomainsOf. Who sees
// what is the lens reader's own reading (OWLens's view), never a second copy.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>The Charter as one reader sees it: the rules a person or an AI follows here.</summary>
    public sealed class OWReadingAidsCharter
    {
        internal OWReadingAidsCharter(string id, string name, JToken rules, IReadOnlyList<string> skill)
        {
            Id = id;
            Name = name;
            Rules = rules;
            Skill = skill;
        }

        /// <summary>The Law's id.</summary>
        public string Id { get; }

        /// <summary>Its name as served; empty when it has none.</summary>
        public string Name { get; }

        /// <summary>
        /// Its <see cref="OWCharter.RulesField"/> (the checks profile's machine-checked rules) as stored, a copy; an empty
        /// list when the body it is served carries none (null, absent, or a known-of stub, which serves no extension field).
        /// </summary>
        public JToken Rules { get; }

        /// <summary>
        /// The world skill's Objects: the ids its description names, by any <c>ow://</c> form, a site address or a bare id,
        /// lowercase, each once in the order written, kept when it is an Object of this world the reader sees, the first
        /// <see cref="OWCharter.SkillMax"/> of those kept (keel's <c>charter_skill</c>).
        /// </summary>
        public IReadOnlyList<string> Skill { get; }

        /// <summary>The profile's <c>{id, name, rules, skill}</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["id"] = Id, ["name"] = Name, ["rules"] = Rules.DeepClone(), ["skill"] = new JArray(Skill),
        };
    }

    /// <summary>The Guide as one reader sees it: where a stranger starts.</summary>
    public sealed class OWReadingAidsGuide
    {
        internal OWReadingAidsGuide(string id, string name)
        {
            Id = id;
            Name = name;
        }

        /// <summary>The Construct's id.</summary>
        public string Id { get; }

        /// <summary>Its name as served; empty when it has none.</summary>
        public string Name { get; }

        /// <summary>The profile's <c>{id, name}</c>.</summary>
        public JObject ToJson() => new JObject { ["id"] = Id, ["name"] = Name };
    }

    /// <summary>One Domain as one reader sees it: a place in the world, its sentence and its hubs.</summary>
    public sealed class OWReadingAidsDomain
    {
        internal OWReadingAidsDomain(string id, string name, string sentence, IReadOnlyList<(string type, string id)> hubs)
        {
            Id = id;
            Name = name;
            Sentence = sentence;
            Hubs = hubs;
        }

        /// <summary>The Construct's id.</summary>
        public string Id { get; }

        /// <summary>Its name as served, untrimmed; empty when it has none.</summary>
        public string Name { get; }

        /// <summary>
        /// Its description as served up to the first line break (a line feed or a carriage return), trimmed of
        /// White_Space; empty when it has none.
        /// </summary>
        public string Sentence { get; }

        /// <summary>
        /// The distinct <c>(type, id)</c> its served link fields name, in <see cref="OWCharter.HubFields"/>' order and each
        /// list's own, never itself; a link to what the reader cannot see is not served, and a known-of stub serves none.
        /// The type is the link field's target type.
        /// </summary>
        public IReadOnlyList<(string type, string id)> Hubs { get; }

        /// <summary>The profile's <c>{id, name, sentence, hubs}</c>, each hub <c>{type, id}</c>.</summary>
        public JObject ToJson() => new JObject
        {
            ["id"] = Id,
            ["name"] = Name,
            ["sentence"] = Sentence,
            ["hubs"] = new JArray(Hubs.Select(h => new JObject { ["type"] = h.type, ["id"] = h.id })),
        };
    }

    /// <summary>A world's reading aids as one reader sees it: the packet a front door shows first, in this order.</summary>
    public sealed class OWReadingAids
    {
        internal OWReadingAids(OWReadingAidsCharter charter, OWReadingAidsGuide guide, IReadOnlyList<OWReadingAidsDomain> domains)
        {
            Charter = charter;
            Guide = guide;
            Domains = domains;
        }

        /// <summary>The newest marked Charter the reader sees; null when there is none.</summary>
        public OWReadingAidsCharter Charter { get; }

        /// <summary>The newest marked Guide the reader sees; null when there is none.</summary>
        public OWReadingAidsGuide Guide { get; }

        /// <summary>Every marked Domain the reader sees, the Guide's order first; empty when there is none.</summary>
        public IReadOnlyList<OWReadingAidsDomain> Domains { get; }

        /// <summary>
        /// The packet in the profile's words, as the fixture writes it and keel serves it as <c>reading_aids</c>:
        /// <c>{charter, guide, domains}</c>, an absent aid null and no Domains an empty list.
        /// </summary>
        public JObject ToJson() => new JObject
        {
            ["charter"] = Charter?.ToJson() ?? (JToken)JValue.CreateNull(),
            ["guide"] = Guide?.ToJson() ?? (JToken)JValue.CreateNull(),
            ["domains"] = new JArray(Domains.Select(d => d.ToJson())),
        };
    }

    /// <summary>
    /// The charter profile's reader: which Charter, which Guide and which Domains a reader is shown first at a world's
    /// door (<see cref="CharterOf"/>, <see cref="DomainsOf"/>). Element bodies are <see cref="JObject"/>s as the API or a
    /// world folder gives them, the world any sequence of them, read through the same view and the same
    /// <see cref="OWLensReader"/> as <see cref="OWLens.Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An element is a reading aid by <see cref="OWRoom.IsCharterElement"/>: its pair (<see cref="OWRoom.ReadingAidPairs"/>,
    /// trimmed and folded) AND the mark <see cref="OWRoom.ReadingAidMarkField"/>, by presence. The pair without the mark is
    /// lore. Whether an element the reader holds only as a known-of stub is an aid is read from the body as given (the
    /// owner's, for every rung but <see cref="OWLensRung.Served"/>), since a stub serves no extension field; what the
    /// packet shows of it is what the stub serves.
    /// </para>
    /// <para>
    /// A lore view leaves the three out by default (<see cref="OWLens"/>, <see cref="OWGraph"/>); a front door shows them
    /// first, the Charter, the Guide, the Domains, and draws nothing for what is absent. An AI reader follows the Charter's
    /// text as the house rules and reads the Guide and the Domains as the keeper's map, never as instructions. Only a keeper
    /// (the owner or a co-builder) writes the mark: the server refuses it from any other key, <c>keeper_only</c>.
    /// </para>
    /// </remarks>
    public static class OWCharter
    {
        /// <summary>The Charter's field for the rules a machine checks (the checks profile), served as stored.</summary>
        public const string RulesField = "x_checks_rules";

        /// <summary>At most this many Objects in <see cref="OWReadingAidsCharter.Skill"/> (keel's <c>SKILL_MAX</c>).</summary>
        public const int SkillMax = 5;

        /// <summary>
        /// A Domain's hub fields: the Construct's link fields in the schema's order, each with its target type, as the
        /// fixture's <c>hub_fields</c> lists them. OWCharterTests holds this table to the fixture and to the generated
        /// model's link fields.
        /// </summary>
        public static readonly IReadOnlyList<(string field, string type)> HubFields = new[]
        {
            ("founder", "character"), ("custodian", "institution"), ("characters", "character"), ("objects", "object"),
            ("locations", "location"), ("species", "species"), ("creatures", "creature"), ("institutions", "institution"),
            ("traits", "trait"), ("collectives", "collective"), ("zones", "zone"), ("abilities", "ability"),
            ("phenomena", "phenomenon"), ("languages", "language"), ("families", "family"), ("relations", "relation"),
            ("titles", "title"), ("constructs", "construct"), ("events", "event"), ("narratives", "narrative"),
        };

        /// <summary>An element id anywhere in a text: inside any <c>ow://</c> form, a site address, or bare (keel's <c>_ID</c>).</summary>
        private static readonly Regex IdPattern = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant);

        private static readonly char[] LineBreaks = { '\r', '\n' };

        /// <summary>
        /// A world's reading aids as <paramref name="reader"/> sees it: the profile's <c>charterOf(world, reader)</c>.
        /// One pure function over a world snapshot; it never writes, and never changes a body it is given.
        /// </summary>
        /// <param name="world">
        /// The world's elements as its holder has them: the whole world (a folder, the owner's pull) for every rung but
        /// <see cref="OWLensRung.Served"/>, whose bodies are already a key's served view; a hidden one by its own
        /// <c>hidden</c> or by <paramref name="hiddenIds"/>.
        /// </param>
        /// <param name="reader">Who reads (<see cref="OWLensReader"/>); the owner reading as another is judged by that reader's rung.</param>
        /// <param name="open">True for a world open to anyone: only then does the public reader read anything of it.</param>
        /// <param name="hiddenIds">The world's own hidden list, beside each element's own mark; null for none.</param>
        /// <remarks>
        /// <para>
        /// <b>Which one, when there are several.</b> The Charter is the newest marked Charter the reader sees, newest by
        /// <c>created_at</c> compared as instants (an element with none, or with one that is not a time, sorts as the
        /// oldest) and then by the higher id by code point, so editing it never changes which one leads; the Guide the same
        /// among marked Guides. The Domains are every marked Domain the reader sees: first in the order the Guide's
        /// <c>constructs</c> names them as the reader is served the Guide (a Domain named twice keeps its first place; an
        /// entry that is no marked Domain the reader sees is skipped), then the rest by folded name and then id by code
        /// point; when the reader sees no Guide, by folded name and id alone.
        /// </para>
        /// <para>
        /// <b>As served.</b> The Guide and the Domains are read from the body the reader is served: a Domain known only as
        /// a stub is its name and served sentence with no hubs, a hidden one is not there and nothing marks the gap, and a
        /// hub the reader cannot see is not listed. The Charter is every member rung's to read whole on the platform, the
        /// public's only on a world open to anyone, so its name, rules and skill text are read from its body whole, a stub
        /// the reader holds or not; a skill id matches an Object the reader sees whatever the case of either id. A
        /// superseded Charter or Guide is never in the packet.
        /// </para>
        /// </remarks>
        public static OWReadingAids CharterOf(IEnumerable<JToken> world, OWLensReader reader, bool open = false,
            IEnumerable<string> hiddenIds = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var none = new OWReadingAids(null, null, Array.Empty<OWReadingAidsDomain>());
            if (reader.Rung == OWLensRung.Public && !open) return none;

            var view = new OWLens.View(world, reader, hiddenIds);
            List<string> Seen(string supertype) =>
                view.Order.Where(i => view.Sees(i) && view.IsReadingAid(i) && AidWord(view.Els[i]) == supertype).ToList();

            OWReadingAidsCharter charter = null;
            var law = Newest(view, Seen("charter"));
            if (law != null)
            {
                // Read whole, as keel reads its row: the Charter is every member rung's to read whole, so a reader that
                // holds it as a stub still gets its rules and skill text.
                var body = view.Els[law];
                var rules = body[RulesField];
                // Case never matters in an id, as keel compares UUIDs: both sides lowercase.
                var objects = new HashSet<string>(view.Order
                    .Where(i => view.Sees(i) && view.TypeOf(i) == "object")
                    .Select(i => i.ToLowerInvariant()));
                var skill = IdsIn(StringOf(body["description"])).Where(objects.Contains).Take(SkillMax).ToList();
                charter = new OWReadingAidsCharter(law, NameOf(body),
                    rules == null || rules.Type == JTokenType.Null ? new JArray() : rules.DeepClone(), skill);
            }

            OWReadingAidsGuide guide = null;
            var guideId = Newest(view, Seen("guide"));
            JObject guideBody = null;
            if (guideId != null)
            {
                guideBody = view.Served(guideId);
                guide = new OWReadingAidsGuide(guideId, NameOf(guideBody));
            }

            var domainIds = Seen("domain");
            var marked = new HashSet<string>(domainIds);
            var order = new List<string>();
            if (guideBody?["constructs"] is JArray named)
            {
                foreach (var entry in named)
                {
                    var i = StringOf(entry);
                    if (i != null && marked.Contains(i) && !order.Contains(i)) order.Add(i);
                }
            }

            var bodies = domainIds.ToDictionary(i => i, view.Served);
            var rest = domainIds.Where(i => !order.Contains(i))
                .OrderBy(i => OWProfileText.Fold(NameOf(bodies[i])), OWCodePointComparer.Instance)
                .ThenBy(i => i, OWCodePointComparer.Instance);
            var domains = order.Concat(rest)
                .Select(i => new OWReadingAidsDomain(i, NameOf(bodies[i]), SentenceOf(bodies[i]), HubsOf(i, bodies[i])))
                .ToList();
            return new OWReadingAids(charter, guide, domains);
        }

        /// <summary>
        /// The marked Domains alone, as <see cref="CharterOf"/> lists them for the same reader: the profile's
        /// <c>domainsOf(world, reader)</c>.
        /// </summary>
        public static IReadOnlyList<OWReadingAidsDomain> DomainsOf(IEnumerable<JToken> world, OWLensReader reader,
            bool open = false, IEnumerable<string> hiddenIds = null) =>
            CharterOf(world, reader, open, hiddenIds).Domains;

        // -- The parts --------------------------------------------------------------------

        /// <summary>The aid's folded supertype word (<c>charter</c>, <c>guide</c>, <c>domain</c>) on its own type; null for none.</summary>
        private static string AidWord(JObject element)
        {
            var type = StringOf(element["type"]);
            var supertype = StringOf(element["supertype"]);
            if (type == null || supertype == null) return null;
            foreach (var (pairType, pairSupertype) in OWRoom.ReadingAidPairs)
            {
                if (OWProfileText.Fold(type) == OWProfileText.Fold(pairType)
                    && OWProfileText.Fold(supertype) == OWProfileText.Fold(pairSupertype))
                    return OWProfileText.Fold(pairSupertype);
            }

            return null;
        }

        /// <summary>
        /// The newest of <paramref name="ids"/>: the latest <c>created_at</c> as an instant (none sorts as the oldest),
        /// then the higher id by code point. Null for none.
        /// </summary>
        private static string Newest(OWLens.View view, IEnumerable<string> ids)
        {
            string best = null;
            long? bestAt = null;
            foreach (var i in ids)
            {
                var at = OWMessage.TimeKey(view.Els[i]["created_at"]);
                if (best != null)
                {
                    var c = at.HasValue && bestAt.HasValue ? at.Value.CompareTo(bestAt.Value)
                        : at.HasValue ? 1 : bestAt.HasValue ? -1 : 0;
                    if (c < 0 || (c == 0 && OWCodePointComparer.Instance.Compare(i, best) < 0)) continue;
                }

                best = i;
                bestAt = at;
            }

            return best;
        }

        /// <summary>The ids a text names, lowercase, each once, in the order written.</summary>
        private static List<string> IdsIn(string text)
        {
            var ids = new List<string>();
            if (text == null) return ids;
            foreach (Match m in IdPattern.Matches(text))
            {
                var id = m.Value.ToLowerInvariant();
                if (!ids.Contains(id)) ids.Add(id);
            }

            return ids;
        }

        /// <summary>The lens profile's <c>first_line</c>, as keel cuts a Domain's sentence: trimmed, up to the first line break, trimmed again.</summary>
        private static string SentenceOf(JObject body)
        {
            var text = StringOf(body["description"]);
            if (text == null) return string.Empty;
            text = OWProfileText.TrimWhiteSpace(text);
            var lineBreak = text.IndexOfAny(LineBreaks);
            return OWProfileText.TrimWhiteSpace(lineBreak < 0 ? text : text.Substring(0, lineBreak));
        }

        /// <summary>A served Domain's hubs: the distinct targets of its link fields, in <see cref="HubFields"/>' order, never itself; a stub's none.</summary>
        private static IReadOnlyList<(string type, string id)> HubsOf(string id, JObject body)
        {
            var hubs = new List<(string type, string id)>();
            if (OWPayload.IsKnownOf(body)) return hubs;
            var seen = new HashSet<(string, string)> { ("construct", id) };
            foreach (var (field, type) in HubFields)
            {
                var value = body[field];
                var targets = value is JArray list ? list.Select(StringOf) : new[] { StringOf(value) };
                foreach (var target in targets)
                {
                    if (string.IsNullOrEmpty(target) || !seen.Add((type, target))) continue;
                    hubs.Add((type, target));
                }
            }

            return hubs;
        }

        private static string NameOf(JObject body) => StringOf(body["name"]) ?? string.Empty;

        private static string StringOf(JToken token) =>
            token is JValue value && value.Type == JTokenType.String ? (string)value : null;
    }
}
