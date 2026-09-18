// GENERATED from the OnlyWorlds schema distribution -- DO NOT EDIT.
// Regenerate: python codegen/generate_models.py   (drift guard: codegen/check_drift.py)
//
// canonical: 00.30.01
// serial: 15
// published: 2026-09-18

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The <c>narrative</c> element type.
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
    public class OWNarrative : OWElement
    {
        // -- Context -----------------------------------------------------

        /// <summary>Content of the narrative, as told or remembered</summary>
        [JsonProperty("story")]
        [SerializeField] private string _story;

        /// <summary>Outcomes or legacy of the narrative</summary>
        [JsonProperty("consequences")]
        [SerializeField] private string _consequences;

        /// <summary>Date when the narrative begins, measured in world TIME units</summary>
        [JsonProperty("start_date")]
        [SerializeField] private SerializableNullable<int> _startDate;

        /// <summary>Date when the narrative ends, measured in world TIME units</summary>
        [JsonProperty("end_date")]
        [SerializeField] private SerializableNullable<int> _endDate;

        /// <summary>Position of this narrative within a parent narrative's sequence</summary>
        [JsonProperty("order")]
        [SerializeField] private SerializableNullable<int> _order;

        /// <summary>Larger narrative that this narrative takes place in</summary>
        /// <remarks>Link: UUID of narrative. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_narrative")]
        [SerializeField] private string _parentNarrative;

        /// <summary>Primary character of the narrative</summary>
        /// <remarks>Link: UUID of character. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("protagonist")]
        [SerializeField] private string _protagonist;

        /// <summary>Opposing character of the narrative</summary>
        /// <remarks>Link: UUID of character. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("antagonist")]
        [SerializeField] private string _antagonist;

        /// <summary>Character credited with telling or recording the narrative</summary>
        /// <remarks>Link: UUID of character. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("narrator")]
        [SerializeField] private string _narrator;

        /// <summary>Institution that preserves or curates the narrative</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("conservator")]
        [SerializeField] private string _conservator;

        // -- Involves ----------------------------------------------------

        /// <summary>Events relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of event. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("events")]
        [SerializeField] private List<string> _events = new List<string>();

        /// <summary>Characters relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("characters")]
        [SerializeField] private List<string> _characters = new List<string>();

        /// <summary>Objects relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("objects")]
        [SerializeField] private List<string> _objects = new List<string>();

        /// <summary>Locations relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("locations")]
        [SerializeField] private List<string> _locations = new List<string>();

        /// <summary>Species relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        /// <summary>Creatures relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of creature. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("creatures")]
        [SerializeField] private List<string> _creatures = new List<string>();

        /// <summary>Institutions relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("institutions")]
        [SerializeField] private List<string> _institutions = new List<string>();

        /// <summary>Traits relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        /// <summary>Groups relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of collective. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("collectives")]
        [SerializeField] private List<string> _collectives = new List<string>();

        /// <summary>Zones relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("zones")]
        [SerializeField] private List<string> _zones = new List<string>();

        /// <summary>Abilities relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        /// <summary>Phenomena relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("phenomena")]
        [SerializeField] private List<string> _phenomena = new List<string>();

        /// <summary>Languages relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        /// <summary>Families relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of family. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("families")]
        [SerializeField] private List<string> _families = new List<string>();

        /// <summary>Relationships relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of relation. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("relations")]
        [SerializeField] private List<string> _relations = new List<string>();

        /// <summary>Titles relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of title. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("titles")]
        [SerializeField] private List<string> _titles = new List<string>();

        /// <summary>Constructs relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("constructs")]
        [SerializeField] private List<string> _constructs = new List<string>();

        /// <summary>Laws relevant to the narrative</summary>
        /// <remarks>Link: UUIDs of law. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("laws")]
        [SerializeField] private List<string> _laws = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Story { get => _story; set => _story = value; }
        public string Consequences { get => _consequences; set => _consequences = value; }
        public SerializableNullable<int> StartDate { get => _startDate; set => _startDate = value; }
        public SerializableNullable<int> EndDate { get => _endDate; set => _endDate = value; }
        public SerializableNullable<int> Order { get => _order; set => _order = value; }
        public string ParentNarrative { get => _parentNarrative; set => _parentNarrative = value; }
        public string Protagonist { get => _protagonist; set => _protagonist = value; }
        public string Antagonist { get => _antagonist; set => _antagonist = value; }
        public string Narrator { get => _narrator; set => _narrator = value; }
        public string Conservator { get => _conservator; set => _conservator = value; }

        public List<string> Events => _events;
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
        public List<string> Laws => _laws;
    }
}
