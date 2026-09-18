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
    /// The <c>object</c> element type.
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
    public class OWObject : OWElement
    {
        // -- Form --------------------------------------------------------

        /// <summary>Appearance, design, or visual presentation of the object</summary>
        [JsonProperty("aesthetics")]
        [SerializeField] private string _aesthetics;

        /// <summary>Approximate or exact mass of the object, defined by world MASS units</summary>
        [JsonProperty("weight")]
        [SerializeField] private SerializableNullable<int> _weight;

        /// <summary>The number of identical units in this object entry</summary>
        [JsonProperty("amount")]
        [SerializeField] private SerializableNullable<int> _amount;

        /// <summary>Larger object that this one is part of or contained within</summary>
        /// <remarks>Link: UUID of object. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_object")]
        [SerializeField] private string _parentObject;

        /// <summary>The physical matter that constitutes the object</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("materials")]
        [SerializeField] private List<string> _materials = new List<string>();

        /// <summary>Mechanisms relating to the object's design or operation</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("technology")]
        [SerializeField] private List<string> _technology = new List<string>();

        // -- Function ----------------------------------------------------

        /// <summary>Intended purpose or primary use of the object</summary>
        [JsonProperty("utility")]
        [SerializeField] private string _utility;

        /// <summary>Phenomena potentially triggered or emitted on object use</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("effects")]
        [SerializeField] private List<string> _effects = new List<string>();

        /// <summary>Abilities that the object grants or enables</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        /// <summary>What might be used or depleted on object use</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("consumes")]
        [SerializeField] private List<string> _consumes = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Background or history of the object</summary>
        [JsonProperty("origins")]
        [SerializeField] private string _origins;

        /// <summary>Physical place where the object is currently located or stored</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("location")]
        [SerializeField] private string _location;

        /// <summary>Required to read, understand, or activate the object</summary>
        /// <remarks>Link: UUID of language. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("language")]
        [SerializeField] private string _language;

        /// <summary>Traits that resonate with or enhance the object's use, function, or effects</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("affinities")]
        [SerializeField] private List<string> _affinities = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Aesthetics { get => _aesthetics; set => _aesthetics = value; }
        public SerializableNullable<int> Weight { get => _weight; set => _weight = value; }
        public SerializableNullable<int> Amount { get => _amount; set => _amount = value; }
        public string ParentObject { get => _parentObject; set => _parentObject = value; }
        public List<string> Materials => _materials;
        public List<string> Technology => _technology;

        public string Utility { get => _utility; set => _utility = value; }
        public List<string> Effects => _effects;
        public List<string> Abilities => _abilities;
        public List<string> Consumes => _consumes;

        public string Origins { get => _origins; set => _origins = value; }
        public string Location { get => _location; set => _location = value; }
        public string Language { get => _language; set => _language = value; }
        public List<string> Affinities => _affinities;
    }
}
