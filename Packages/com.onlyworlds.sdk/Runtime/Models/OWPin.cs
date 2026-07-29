using System;
using Newtonsoft.Json;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A pin placed on a map. The schema's only generic-link case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN PROVING MODEL -- expected to be replaced wholesale by the emitter.
    /// </para>
    /// <para>
    /// Chosen as one of three proving models because <c>element</c> is a <c>generic-link</c>, the
    /// only one in all 22 types: it points at ANY element type, carried on the wire as a
    /// (<c>element_type</c>, <c>element_id</c>) pair rather than a bare UUID. Every other link is a
    /// plain UUID with a known target type.
    /// </para>
    /// <para>
    /// Note what is NOT here: no hard-fail on the schema's <c>required: [map, element, x, y]</c>.
    /// Wire-probed 2026-07-28 -- the v2 API does not enforce it (a mapless pin POSTs 201 with
    /// map/x/y null), and Captain ruled those lists get dropped from canonical because mapless pins
    /// are a legitimate app pattern. Only <c>name</c> is required.
    /// </para>
    /// </remarks>
    // OptIn must be repeated on every subclass -- Newtonsoft does not inherit [JsonObject],
    // so a model without it silently serializes every public property alongside the
    // attributed fields, duplicating each one on the wire. The emitter must emit this line.
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    public class OWPin : OWElement
    {
        [JsonProperty("map")]
        [SerializeField] private string _map;

        [JsonProperty("element_type")]
        [SerializeField] private string _elementType;

        [JsonProperty("element_id")]
        [SerializeField] private string _elementId;

        [JsonProperty("x")]
        [SerializeField] private SerializableNullable<int> _x;

        [JsonProperty("y")]
        [SerializeField] private SerializableNullable<int> _y;

        [JsonProperty("z")]
        [SerializeField] private SerializableNullable<int> _z;

        /// <summary>UUID of the Map this pin sits on.</summary>
        public string Map { get => _map; set => _map = value; }

        /// <summary>Element type slug the pin points at, e.g. <c>character</c>.</summary>
        public string ElementType { get => _elementType; set => _elementType = value; }

        /// <summary>UUID of the element the pin points at.</summary>
        public string ElementId { get => _elementId; set => _elementId = value; }

        public SerializableNullable<int> X { get => _x; set => _x = value; }
        public SerializableNullable<int> Y { get => _y; set => _y = value; }
        public SerializableNullable<int> Z { get => _z; set => _z = value; }

        /// <summary>True when the generic link points somewhere resolvable.</summary>
        public bool HasTarget => !string.IsNullOrEmpty(_elementType) && !string.IsNullOrEmpty(_elementId);
    }
}
