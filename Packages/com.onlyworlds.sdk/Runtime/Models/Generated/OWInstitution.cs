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
    /// The <c>institution</c> element type.
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
    public class OWInstitution : OWElement
    {
        // -- Foundation --------------------------------------------------

        /// <summary>Core belief, mission, or purpose that drives the institution</summary>
        [JsonProperty("doctrine")]
        [SerializeField] private string _doctrine;

        /// <summary>Date when the institution was established, in the world's TIME format</summary>
        [JsonProperty("founding_date")]
        [SerializeField] private SerializableNullable<int> _foundingDate;

        /// <summary>Institution that governs, embodies, or originated this one</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_institution")]
        [SerializeField] private string _parentInstitution;

        // -- Claims ------------------------------------------------------

        /// <summary>Areas the institution controls or claims authority over</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("zones")]
        [SerializeField] private List<string> _zones = new List<string>();

        /// <summary>Significant objects owned or tied to the institution's operations, holdings, or identity</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("objects")]
        [SerializeField] private List<string> _objects = new List<string>();

        /// <summary>Creatures under the institution's protection, use, or symbolic control</summary>
        /// <remarks>Link: UUIDs of creature. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("creatures")]
        [SerializeField] private List<string> _creatures = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Current political, cultural, or functional standing of the institution in the world</summary>
        [JsonProperty("status")]
        [SerializeField] private string _status;

        /// <summary>Institutions this one actively cooperates or aligns with</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("allies")]
        [SerializeField] private List<string> _allies = new List<string>();

        /// <summary>Institutions this one opposes, competes with, or is in conflict with</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("adversaries")]
        [SerializeField] private List<string> _adversaries = new List<string>();

        /// <summary>Conceptual, procedural, or structural systems created or maintained by the institution</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("constructs")]
        [SerializeField] private List<string> _constructs = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Doctrine { get => _doctrine; set => _doctrine = value; }
        public SerializableNullable<int> FoundingDate { get => _foundingDate; set => _foundingDate = value; }
        public string ParentInstitution { get => _parentInstitution; set => _parentInstitution = value; }

        public List<string> Zones => _zones;
        public List<string> Objects => _objects;
        public List<string> Creatures => _creatures;

        public string Status { get => _status; set => _status = value; }
        public List<string> Allies => _allies;
        public List<string> Adversaries => _adversaries;
        public List<string> Constructs => _constructs;
    }
}
