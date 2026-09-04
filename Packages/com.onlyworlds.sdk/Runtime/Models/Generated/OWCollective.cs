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
    /// The <c>collective</c> element type.
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
    public class OWCollective : OWElement
    {
        // -- Formation ---------------------------------------------------

        /// <summary>Internal structure or demographic makeup of the collective</summary>
        [JsonProperty("composition")]
        [SerializeField] private string _composition;

        /// <summary>Number of members in the collective (approximate or exact)</summary>
        [JsonProperty("count")]
        [SerializeField] private SerializableNullable<int> _count;

        /// <summary>Date the collective was formed, using world TIME units</summary>
        [JsonProperty("formation_date")]
        [SerializeField] private SerializableNullable<int> _formationDate;

        /// <summary>Institution that manages or directs the collective</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("operator")]
        [SerializeField] private string _operator;

        /// <summary>Tools or gear in possession of and/or regularly used by the collective</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("equipment")]
        [SerializeField] private List<string> _equipment = new List<string>();

        // -- Dynamics ----------------------------------------------------

        /// <summary>Primary behaviors or actions the collective engages in</summary>
        [JsonProperty("activity")]
        [SerializeField] private string _activity;

        /// <summary>Emotional control or volatility expressed by the collective</summary>
        [JsonProperty("disposition")]
        [SerializeField] private string _disposition;

        /// <summary>Current condition or operational status of the collective</summary>
        [JsonProperty("state")]
        [SerializeField] private string _state;

        /// <summary>Abilities commonly shared among members of the collective, or abilities of that collective as a whole</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        /// <summary>Cultural expressions, rituals, or symbols that unify or distinguish the collective</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("symbolism")]
        [SerializeField] private List<string> _symbolism = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Species that compose or participate in the collective</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        /// <summary>Characters who are members of the collective</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("characters")]
        [SerializeField] private List<string> _characters = new List<string>();

        /// <summary>Creatures associated with or included in the collective</summary>
        /// <remarks>Link: UUIDs of creature. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("creatures")]
        [SerializeField] private List<string> _creatures = new List<string>();

        /// <summary>Phenomena that influence or characterize the collective</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("phenomena")]
        [SerializeField] private List<string> _phenomena = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Composition { get => _composition; set => _composition = value; }
        public SerializableNullable<int> Count { get => _count; set => _count = value; }
        public SerializableNullable<int> FormationDate { get => _formationDate; set => _formationDate = value; }
        public string Operator { get => _operator; set => _operator = value; }
        public List<string> Equipment => _equipment;

        public string Activity { get => _activity; set => _activity = value; }
        public string Disposition { get => _disposition; set => _disposition = value; }
        public string State { get => _state; set => _state = value; }
        public List<string> Abilities => _abilities;
        public List<string> Symbolism => _symbolism;

        public List<string> Species => _species;
        public List<string> Characters => _characters;
        public List<string> Creatures => _creatures;
        public List<string> Phenomena => _phenomena;
    }
}
