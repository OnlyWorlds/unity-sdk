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
    /// The <c>map</c> element type.
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
    public class OWMap : OWElement
    {
        // -- Details -----------------------------------------------------

        /// <summary>Color of the space around the map when zoomed out</summary>
        [JsonProperty("background_color")]
        [SerializeField] private string _backgroundColor;

        /// <summary>To associate or differentiate between maps with a common parent</summary>
        [JsonProperty("hierarchy")]
        [SerializeField] private SerializableNullable<int> _hierarchy;

        /// <summary>In pixels</summary>
        [JsonProperty("width")]
        [SerializeField] private SerializableNullable<int> _width;

        /// <summary>In pixels</summary>
        [JsonProperty("height")]
        [SerializeField] private SerializableNullable<int> _height;

        /// <summary>In pixels</summary>
        [JsonProperty("depth")]
        [SerializeField] private SerializableNullable<int> _depth;

        /// <summary>Map within which this map is contained</summary>
        /// <remarks>Link: UUID of map. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_map")]
        [SerializeField] private string _parentMap;

        /// <summary>Location element that this map represents</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("location")]
        [SerializeField] private string _location;

        // -- Accessors --------------------------------------------------------

        public string BackgroundColor { get => _backgroundColor; set => _backgroundColor = value; }
        public SerializableNullable<int> Hierarchy { get => _hierarchy; set => _hierarchy = value; }
        public SerializableNullable<int> Width { get => _width; set => _width = value; }
        public SerializableNullable<int> Height { get => _height; set => _height = value; }
        public SerializableNullable<int> Depth { get => _depth; set => _depth = value; }
        public string ParentMap { get => _parentMap; set => _parentMap = value; }
        public string Location { get => _location; set => _location = value; }
    }
}
