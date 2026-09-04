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
    /// Represents a graphical marker on a Map, such as a line (road, river) or polygon (zone, region)
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
    public class OWMarker : OWElement
    {
        // -- Details -----------------------------------------------------

        /// <summary>Map this marker is placed on</summary>
        /// <remarks>Link: UUID of map. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("map")]
        [SerializeField] private string _map;

        /// <summary>Zone that is defined by this marker</summary>
        /// <remarks>Link: UUID of zone. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("zone")]
        [SerializeField] private string _zone;

        /// <summary>x coordinate, from bottom left of the map</summary>
        [JsonProperty("x")]
        [SerializeField] private SerializableNullable<int> _x;

        /// <summary>y coordinate, from bottom left of the map</summary>
        [JsonProperty("y")]
        [SerializeField] private SerializableNullable<int> _y;

        /// <summary>z coordinate, in case of depth</summary>
        [JsonProperty("z")]
        [SerializeField] private SerializableNullable<int> _z;

        /// <summary>Sequence position when markers define a polygon or line (0 = first point)</summary>
        [JsonProperty("order")]
        [SerializeField] private SerializableNullable<int> _order;

        // -- Accessors --------------------------------------------------------

        public string Map { get => _map; set => _map = value; }
        public string Zone { get => _zone; set => _zone = value; }
        public SerializableNullable<int> X { get => _x; set => _x = value; }
        public SerializableNullable<int> Y { get => _y; set => _y = value; }
        public SerializableNullable<int> Z { get => _z; set => _z = value; }
        public SerializableNullable<int> Order { get => _order; set => _order = value; }
    }
}
