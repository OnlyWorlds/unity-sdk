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
    /// The <c>pin</c> element type.
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
    public class OWPin : OWElement
    {
        // -- Details -----------------------------------------------------

        /// <summary>Map that the pin is placed on</summary>
        /// <remarks>Link: UUID of map. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("map")]
        [SerializeField] private string _map;

        /// <summary>
        /// Element type slug the link points at, e.g. <c>character</c>.
        /// </summary>
        /// <remarks>
        /// The schema's ONLY <c>generic-link</c>: it points at any element type, carried on
        /// the wire as an (<c>element_type</c>, <c>element_id</c>) pair rather than a bare
        /// UUID. Resolve the slug through <see cref="OWElementTypes"/>.
        /// <para>Schema: Link to any Element (managed by ContentType + UUID)</para>
        /// </remarks>
        [JsonProperty("element_type")]
        [SerializeField] private string _elementType;

        /// <summary>UUID of the element the link points at.</summary>
        [JsonProperty("element_id")]
        [SerializeField] private string _elementId;

        /// <summary>x coordinate, from bottom left of the map</summary>
        [JsonProperty("x")]
        [SerializeField] private SerializableNullable<int> _x;

        /// <summary>y coordinate, from bottom left of the map</summary>
        [JsonProperty("y")]
        [SerializeField] private SerializableNullable<int> _y;

        /// <summary>z coordinate, in case of depth (optional)</summary>
        [JsonProperty("z")]
        [SerializeField] private SerializableNullable<int> _z;

        // -- Accessors --------------------------------------------------------

        public string Map { get => _map; set => _map = value; }
        public string ElementType { get => _elementType; set => _elementType = value; }
        public string ElementId { get => _elementId; set => _elementId = value; }

        /// <summary>True when the generic link points somewhere resolvable.</summary>
        public bool HasTarget => !string.IsNullOrEmpty(_elementType) && !string.IsNullOrEmpty(_elementId);

        public SerializableNullable<int> X { get => _x; set => _x = value; }
        public SerializableNullable<int> Y { get => _y; set => _y = value; }
        public SerializableNullable<int> Z { get => _z; set => _z = value; }
    }
}
