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
    /// The <c>law</c> element type.
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
    public class OWLaw : OWElement
    {
        // -- Code --------------------------------------------------------

        /// <summary>The formal wording, expression, or decree of the law</summary>
        [JsonProperty("declaration")]
        [SerializeField] private string _declaration;

        /// <summary>The intent, motivation, or justification for the law's creation</summary>
        [JsonProperty("purpose")]
        [SerializeField] private string _purpose;

        /// <summary>Date the law was formally established, in world TIME units</summary>
        [JsonProperty("date")]
        [SerializeField] private SerializableNullable<int> _date;

        /// <summary>A law that this law derives from, modifies, or enhances</summary>
        /// <remarks>Link: UUID of law. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_law")]
        [SerializeField] private string _parentLaw;

        /// <summary>Consequences intended to beapplied when the law is contravened</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("penalties")]
        [SerializeField] private List<string> _penalties = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>The institution that created or issued the law</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("author")]
        [SerializeField] private string _author;

        /// <summary>Locations where the law is supported or enforced</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("locations")]
        [SerializeField] private List<string> _locations = new List<string>();

        /// <summary>Zones where the law is supported or enforced</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("zones")]
        [SerializeField] private List<string> _zones = new List<string>();

        /// <summary>Things that the law explicitly or effectively forbids</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("prohibitions")]
        [SerializeField] private List<string> _prohibitions = new List<string>();

        /// <summary>Titles responsible for interpreting or ruling on the law's application and jurisdiction</summary>
        /// <remarks>Link: UUIDs of title. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("adjudicators")]
        [SerializeField] private List<string> _adjudicators = new List<string>();

        /// <summary>Titles responsible for enforcing or imposing the law</summary>
        /// <remarks>Link: UUIDs of title. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("enforcers")]
        [SerializeField] private List<string> _enforcers = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Declaration { get => _declaration; set => _declaration = value; }
        public string Purpose { get => _purpose; set => _purpose = value; }
        public SerializableNullable<int> Date { get => _date; set => _date = value; }
        public string ParentLaw { get => _parentLaw; set => _parentLaw = value; }
        public List<string> Penalties => _penalties;

        public string Author { get => _author; set => _author = value; }
        public List<string> Locations => _locations;
        public List<string> Zones => _zones;
        public List<string> Prohibitions => _prohibitions;
        public List<string> Adjudicators => _adjudicators;
        public List<string> Enforcers => _enforcers;
    }
}
