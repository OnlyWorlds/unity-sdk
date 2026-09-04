// GENERATED from the OnlyWorlds schema distribution -- DO NOT EDIT.
// Regenerate: python codegen/generate_models.py   (drift guard: codegen/check_drift.py)
//
// canonical: 00.30.01
// serial: 11
// published: 2026-07-29

using System;
using System.Collections.Generic;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Every element type in the standard, and the C# class that models it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GENERATED, so the membership list cannot drift from the models beside it. This is the
    /// resolver <see cref="OWPin"/>'s generic link needs: the wire hands over an
    /// <c>element_type</c> slug and an <c>element_id</c>, and the slug has to become a type
    /// before anything can be deserialized into it.
    /// </para>
    /// <para>
    /// Both directions are exposed because both are needed and neither is derivable from the
    /// other at a glance: <see cref="TypeFor"/> for reading the wire,
    /// <see cref="SlugFor"/> for writing to it.
    /// </para>
    /// </remarks>
    public static class OWElementTypes
    {
        /// <summary>Every element type slug, in the standard's own order.</summary>
        public static readonly string[] Slugs =
        {
            "ability", "collective", "character", "construct", "creature", "event", "family",
            "institution", "language", "law", "location", "map", "marker", "narrative", "object",
            "phenomenon", "pin", "relation", "species", "title", "trait", "zone",
        };

        private static readonly Dictionary<string, Type> _bySlug =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "ability", typeof(OWAbility) },
            { "collective", typeof(OWCollective) },
            { "character", typeof(OWCharacter) },
            { "construct", typeof(OWConstruct) },
            { "creature", typeof(OWCreature) },
            { "event", typeof(OWEvent) },
            { "family", typeof(OWFamily) },
            { "institution", typeof(OWInstitution) },
            { "language", typeof(OWLanguage) },
            { "law", typeof(OWLaw) },
            { "location", typeof(OWLocation) },
            { "map", typeof(OWMap) },
            { "marker", typeof(OWMarker) },
            { "narrative", typeof(OWNarrative) },
            { "object", typeof(OWObject) },
            { "phenomenon", typeof(OWPhenomenon) },
            { "pin", typeof(OWPin) },
            { "relation", typeof(OWRelation) },
            { "species", typeof(OWSpecies) },
            { "title", typeof(OWTitle) },
            { "trait", typeof(OWTrait) },
            { "zone", typeof(OWZone) },
        };

        private static readonly Dictionary<Type, string> _bySlugReverse = BuildReverse();

        private static Dictionary<Type, string> BuildReverse()
        {
            var map = new Dictionary<Type, string>();
            foreach (var pair in _bySlug) map[pair.Value] = pair.Key;
            return map;
        }

        /// <summary>
        /// The model type for a slug, or <c>null</c> when the slug is not in the standard.
        /// </summary>
        /// <remarks>
        /// Returns null rather than throwing: an unknown slug means a world written by a
        /// NEWER standard than this package was generated against, which a reader should
        /// report and skip, not crash on. Matching is case-insensitive because the wire's
        /// casing is not something a caller should have to know.
        /// </remarks>
        public static Type TypeFor(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return null;
            return _bySlug.TryGetValue(slug, out var type) ? type : null;
        }

        /// <summary>The slug for a model type, or <c>null</c> if it is not a generated model.</summary>
        public static string SlugFor(Type type)
        {
            if (type == null) return null;
            return _bySlugReverse.TryGetValue(type, out var slug) ? slug : null;
        }

        /// <summary>The slug for a model type.</summary>
        public static string SlugFor<T>() where T : OWElement => SlugFor(typeof(T));

        /// <summary>True when the slug names a type in the standard.</summary>
        public static bool IsKnown(string slug) => TypeFor(slug) != null;

        /// <summary>
        /// The JSON field names each generated model carries, per slug -- NOT including the
        /// shared base fields.
        /// </summary>
        /// <remarks>
        /// Generated alongside the models so a test can ask what a model knows without
        /// reflecting over private fields, and so a folder reader can report an unknown key
        /// rather than swallowing it. <c>pin</c> carries <c>element_type</c> and
        /// <c>element_id</c> here, which is what the wire carries -- there is no field named
        /// <c>element</c>.
        /// </remarks>
        public static readonly IReadOnlyDictionary<string, string[]> FieldNames =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "ability", new[] { "activation", "duration", "potency", "range", "effects", "challenges", "talents", "requisites", "prevalence", "tradition", "source", "locus", "instruments", "systems" } },
            { "collective", new[] { "composition", "count", "formation_date", "operator", "equipment", "activity", "disposition", "state", "abilities", "symbolism", "species", "characters", "creatures", "phenomena" } },
            { "character", new[] { "physicality", "mentality", "height", "weight", "species", "traits", "abilities", "background", "motivations", "birth_date", "birthplace", "languages", "reputation", "location", "objects", "institutions", "charisma", "coercion", "competence", "compassion", "creativity", "courage", "family", "friends", "rivals", "level", "hit_points", "STR", "DEX", "CON", "INT", "WIS", "CHA" } },
            { "construct", new[] { "rationale", "history", "status", "reach", "start_date", "end_date", "founder", "custodian", "characters", "objects", "locations", "species", "creatures", "institutions", "traits", "collectives", "zones", "abilities", "phenomena", "languages", "families", "relations", "titles", "constructs", "events", "narratives" } },
            { "creature", new[] { "appearance", "weight", "height", "species", "habits", "demeanor", "traits", "abilities", "languages", "status", "birth_date", "location", "zone", "challenge_rating", "hit_points", "armor_class", "speed", "actions" } },
            { "event", new[] { "history", "challenges", "consequences", "start_date", "end_date", "triggers", "characters", "objects", "locations", "species", "creatures", "institutions", "traits", "collectives", "zones", "abilities", "phenomena", "languages", "families", "relations", "titles", "constructs" } },
            { "family", new[] { "spirit", "history", "traditions", "traits", "abilities", "languages", "ancestors", "reputation", "estates", "governs", "heirlooms", "creatures" } },
            { "institution", new[] { "doctrine", "founding_date", "parent_institution", "zones", "objects", "creatures", "status", "allies", "adversaries", "constructs" } },
            { "language", new[] { "phonology", "grammar", "lexicon", "writing", "classification", "status", "spread", "dialects" } },
            { "law", new[] { "declaration", "purpose", "date", "parent_law", "penalties", "author", "locations", "zones", "prohibitions", "adjudicators", "enforcers" } },
            { "location", new[] { "form", "function", "founding_date", "parent_location", "populations", "political_climate", "primary_power", "governing_title", "secondary_powers", "zone", "rival", "partner", "customs", "founders", "cults", "delicacies", "extraction_methods", "extraction_goods", "industry_methods", "industry_goods", "infrastructure", "extraction_markets", "industry_markets", "currencies", "architecture", "buildings", "building_methods", "defensibility", "elevation", "fighters", "defensive_objects" } },
            { "map", new[] { "background_color", "hierarchy", "width", "height", "depth", "parent_map", "location" } },
            { "marker", new[] { "map", "zone", "x", "y", "z", "order" } },
            { "narrative", new[] { "story", "consequences", "start_date", "end_date", "order", "parent_narrative", "protagonist", "antagonist", "narrator", "conservator", "events", "characters", "objects", "locations", "species", "creatures", "institutions", "traits", "collectives", "zones", "abilities", "phenomena", "languages", "families", "relations", "titles", "constructs", "laws" } },
            { "object", new[] { "aesthetics", "weight", "amount", "parent_object", "materials", "technology", "utility", "effects", "abilities", "consumes", "origins", "location", "language", "affinities" } },
            { "phenomenon", new[] { "expression", "effects", "duration", "catalysts", "empowerments", "mythology", "system", "triggers", "wielders", "environments" } },
            { "pin", new[] { "map", "element_type", "element_id", "x", "y", "z" } },
            { "relation", new[] { "background", "start_date", "end_date", "intensity", "actor", "events", "characters", "objects", "locations", "species", "creatures", "institutions", "traits", "collectives", "zones", "abilities", "phenomena", "languages", "families", "titles", "constructs", "narratives" } },
            { "species", new[] { "appearance", "life_span", "weight", "nourishment", "reproduction", "adaptations", "instincts", "sociality", "temperament", "communication", "aggression", "traits", "role", "parent_species", "locations", "zones", "affinities" } },
            { "title", new[] { "authority", "eligibility", "grant_date", "revoke_date", "issuer", "body", "superior_title", "holders", "symbols", "status", "history", "characters", "institutions", "families", "zones", "locations", "objects", "constructs", "laws", "collectives", "creatures", "phenomena", "species", "languages" } },
            { "trait", new[] { "social_effects", "physical_effects", "functional_effects", "personality_effects", "behaviour_effects", "charisma", "coercion", "competence", "compassion", "creativity", "courage", "significance", "anti_trait", "empowered_abilities" } },
            { "zone", new[] { "role", "start_date", "end_date", "phenomena", "linked_zones", "context", "populations", "titles", "principles" } },
        };

        /// <summary>Field names on <see cref="OWElement"/>, shared by every type.</summary>
        /// <remarks>
        /// A CONSTANT in the emitter rather than a walk result: these are
        /// <c>base_properties.yaml</c>'s fields (lowercased to their wire spelling) plus the
        /// four server-managed fields that live in no element YAML at all. Change it in
        /// <c>codegen/generate_models.py</c> beside <see cref="OWElement"/>, never here.
        /// It sits next to the generated per-type lists so a caller can ask "does this
        /// model know this key?" in one place.
        /// </remarks>
        public static readonly string[] BaseFieldNames =
        {
            "id", "name", "description", "supertype", "subtype", "image_url",
            "world", "type", "created_at", "updated_at", "change_seq",
        };
    }
}
