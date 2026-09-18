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
    /// The <c>family</c> element type.
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
    public class OWFamily : OWElement
    {
        // -- Identity ----------------------------------------------------

        /// <summary>The core values or shared ethos that the family embodies</summary>
        [JsonProperty("spirit")]
        [SerializeField] private string _spirit;

        /// <summary>Background or origin story of the family</summary>
        [JsonProperty("history")]
        [SerializeField] private string _history;

        /// <summary>Cultural practices, symbols, or customs overseen by the family</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traditions")]
        [SerializeField] private List<string> _traditions = new List<string>();

        /// <summary>Traits possibly found among members of the family</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        /// <summary>Abilities or special qualities possibly present in the family</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        /// <summary>Languages spoken by, or associated with the family</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        /// <summary>Notable forebears or historic characters in the family's lineage</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("ancestors")]
        [SerializeField] private List<string> _ancestors = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Current social, political, or general standing of the family</summary>
        [JsonProperty("reputation")]
        [SerializeField] private string _reputation;

        /// <summary>Key locations owned, governed, or symbolically tied to the family</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("estates")]
        [SerializeField] private List<string> _estates = new List<string>();

        /// <summary>Institutions administered or managed by the family</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("governs")]
        [SerializeField] private List<string> _governs = new List<string>();

        /// <summary>Important objects or artifacts handed down by the family</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("heirlooms")]
        [SerializeField] private List<string> _heirlooms = new List<string>();

        /// <summary>Creatures owned, bonded to, or representing the family</summary>
        /// <remarks>Link: UUIDs of creature. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("creatures")]
        [SerializeField] private List<string> _creatures = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Spirit { get => _spirit; set => _spirit = value; }
        public string History { get => _history; set => _history = value; }
        public List<string> Traditions => _traditions;
        public List<string> Traits => _traits;
        public List<string> Abilities => _abilities;
        public List<string> Languages => _languages;
        public List<string> Ancestors => _ancestors;

        public string Reputation { get => _reputation; set => _reputation = value; }
        public List<string> Estates => _estates;
        public List<string> Governs => _governs;
        public List<string> Heirlooms => _heirlooms;
        public List<string> Creatures => _creatures;
    }
}
