// GENERATED from the OnlyWorlds schema distribution -- DO NOT EDIT.
// Regenerate: python codegen/generate_models.py   (drift guard: codegen/check_drift.py)
//
// canonical: 00.30.01
// serial: 11
// published: 2026-07-29

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The <c>construct</c> element type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <c>name</c> is required (rulings.yaml: <c>nullable-by-default</c>). Every scalar
    /// integer is a <see cref="SerializableNullable{T}"/> because unset and 0 are different
    /// claims, and no range validation is generated -- <c>maximum:</c> is advisory and the
    /// walk does not surface bounds at all.
    /// </para>
    /// <para>
    /// Field order is the schema's document order, which is also display order.
    /// </para>
    /// </remarks>
    // OptIn must be repeated on every subclass -- Newtonsoft does not inherit [JsonObject],
    // so a model without it silently serializes every public property alongside the
    // attributed fields, duplicating each one on the wire.
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    public class OWConstruct : OWElement
    {
        // -- Nature ------------------------------------------------------

        /// <summary>The internal reasoning, structure, or justification of how the construct functions or makes sense within the world</summary>
        [JsonProperty("rationale")]
        [SerializeField] private string _rationale;

        /// <summary>The historical development or ideation of the construct, and its place in wider historical contexts</summary>
        [JsonProperty("history")]
        [SerializeField] private string _history;

        /// <summary>The present condition or operational status of the construct</summary>
        [JsonProperty("status")]
        [SerializeField] private string _status;

        /// <summary>The geographic, cultural, or political extent of the construct's influence</summary>
        [JsonProperty("reach")]
        [SerializeField] private string _reach;

        /// <summary>The point in time when the construct began or was first established (uses world's TIME definition)</summary>
        [JsonProperty("start_date")]
        [SerializeField] private SerializableNullable<int> _startDate;

        /// <summary>The point in time when the construct ceased to function or lost its meaning</summary>
        [JsonProperty("end_date")]
        [SerializeField] private SerializableNullable<int> _endDate;

        /// <summary>Character who conceived or initiated the construct</summary>
        /// <remarks>Link: UUID of character. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("founder")]
        [SerializeField] private string _founder;

        /// <summary>Institution maintaining, enforcing, or exploiting the construct</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("custodian")]
        [SerializeField] private string _custodian;

        // -- Involves ----------------------------------------------------

        /// <summary>Characters relevant to the construct</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("characters")]
        [SerializeField] private List<string> _characters = new List<string>();

        /// <summary>Objects relevant to the construct</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("objects")]
        [SerializeField] private List<string> _objects = new List<string>();

        /// <summary>Locations relevant to the construct</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("locations")]
        [SerializeField] private List<string> _locations = new List<string>();

        /// <summary>Species relevant to the construct</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        /// <summary>Creatures relevant to the construct</summary>
        /// <remarks>Link: UUIDs of creature. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("creatures")]
        [SerializeField] private List<string> _creatures = new List<string>();

        /// <summary>Institutions relevant to the construct</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("institutions")]
        [SerializeField] private List<string> _institutions = new List<string>();

        /// <summary>Traits relevant to the construct</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        /// <summary>Collectives relevant to the construct</summary>
        /// <remarks>Link: UUIDs of collective. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("collectives")]
        [SerializeField] private List<string> _collectives = new List<string>();

        /// <summary>Zones relevant to the construct</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("zones")]
        [SerializeField] private List<string> _zones = new List<string>();

        /// <summary>Abilities relevant to the construct</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        /// <summary>Phenomena relevant to the construct</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("phenomena")]
        [SerializeField] private List<string> _phenomena = new List<string>();

        /// <summary>Languages relevant to the construct</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        /// <summary>Families relevant to the construct</summary>
        /// <remarks>Link: UUIDs of family. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("families")]
        [SerializeField] private List<string> _families = new List<string>();

        /// <remarks>Link: UUIDs of relation. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("relations")]
        [SerializeField] private List<string> _relations = new List<string>();

        /// <summary>Titles relevant to the construct</summary>
        /// <remarks>Link: UUIDs of title. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("titles")]
        [SerializeField] private List<string> _titles = new List<string>();

        /// <summary>Other constructs relevant to the construct</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("constructs")]
        [SerializeField] private List<string> _constructs = new List<string>();

        /// <summary>Events relevant to the construct</summary>
        /// <remarks>Link: UUIDs of event. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("events")]
        [SerializeField] private List<string> _events = new List<string>();

        /// <summary>Narratives relevant to the construct</summary>
        /// <remarks>Link: UUIDs of narrative. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("narratives")]
        [SerializeField] private List<string> _narratives = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Rationale { get => _rationale; set => _rationale = value; }
        public string History { get => _history; set => _history = value; }
        public string Status { get => _status; set => _status = value; }
        public string Reach { get => _reach; set => _reach = value; }
        public SerializableNullable<int> StartDate { get => _startDate; set => _startDate = value; }
        public SerializableNullable<int> EndDate { get => _endDate; set => _endDate = value; }
        public string Founder { get => _founder; set => _founder = value; }
        public string Custodian { get => _custodian; set => _custodian = value; }

        public List<string> Characters => _characters;
        public List<string> Objects => _objects;
        public List<string> Locations => _locations;
        public List<string> Species => _species;
        public List<string> Creatures => _creatures;
        public List<string> Institutions => _institutions;
        public List<string> Traits => _traits;
        public List<string> Collectives => _collectives;
        public List<string> Zones => _zones;
        public List<string> Abilities => _abilities;
        public List<string> Phenomena => _phenomena;
        public List<string> Languages => _languages;
        public List<string> Families => _families;
        public List<string> Relations => _relations;
        public List<string> Titles => _titles;
        public List<string> Constructs => _constructs;
        public List<string> Events => _events;
        public List<string> Narratives => _narratives;
    }
}
