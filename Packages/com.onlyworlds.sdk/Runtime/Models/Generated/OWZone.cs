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
    /// The <c>zone</c> element type.
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
    public class OWZone : OWElement
    {
        // -- Scope -------------------------------------------------------

        /// <summary>The operational function or intent of the zone</summary>
        [JsonProperty("role")]
        [SerializeField] private string _role;

        /// <summary>Date when the zone becomes extant or relevant, defined in world TIME units</summary>
        [JsonProperty("start_date")]
        [SerializeField] private SerializableNullable<int> _startDate;

        /// <summary>Date when the zone ceases to be meaningful or enforced, defined in world TIME units</summary>
        [JsonProperty("end_date")]
        [SerializeField] private SerializableNullable<int> _endDate;

        /// <summary>Phenomena that affect, define, or occur within the zone</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("phenomena")]
        [SerializeField] private List<string> _phenomena = new List<string>();

        /// <summary>Other zones that are associated with the zone</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("linked_zones")]
        [SerializeField] private List<string> _linkedZones = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Historical and key knowledge about the zone</summary>
        [JsonProperty("context")]
        [SerializeField] private string _context;

        /// <summary>Distinct collective groups or communities residing within the zone</summary>
        /// <remarks>Link: UUIDs of collective. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("populations")]
        [SerializeField] private List<string> _populations = new List<string>();

        /// <summary>Titles assigned to represent, manage, or protect the zone</summary>
        /// <remarks>Link: UUIDs of title. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("titles")]
        [SerializeField] private List<string> _titles = new List<string>();

        /// <summary>Influential mechanics acted within, upon, or by the zone</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("principles")]
        [SerializeField] private List<string> _principles = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Role { get => _role; set => _role = value; }
        public SerializableNullable<int> StartDate { get => _startDate; set => _startDate = value; }
        public SerializableNullable<int> EndDate { get => _endDate; set => _endDate = value; }
        public List<string> Phenomena => _phenomena;
        public List<string> LinkedZones => _linkedZones;

        public string Context { get => _context; set => _context = value; }
        public List<string> Populations => _populations;
        public List<string> Titles => _titles;
        public List<string> Principles => _principles;
    }
}
