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
    /// The <c>phenomenon</c> element type.
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
    public class OWPhenomenon : OWElement
    {
        // -- Mechanics ---------------------------------------------------

        /// <summary>How the phenomenon manifests or takes shape in the world</summary>
        [JsonProperty("expression")]
        [SerializeField] private string _expression;

        /// <summary>The primary outcomes or changes caused by the phenomenon</summary>
        [JsonProperty("effects")]
        [SerializeField] private string _effects;

        /// <summary>The amount of time the phenomenon lasts, measured in world TIME units</summary>
        [JsonProperty("duration")]
        [SerializeField] private SerializableNullable<int> _duration;

        /// <summary>Objects or materials that initiate or enhance the phenomenon</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("catalysts")]
        [SerializeField] private List<string> _catalysts = new List<string>();

        /// <summary>Abilities that initiate or enhance the phenomenon, or are initiated or enhanced by it</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("empowerments")]
        [SerializeField] private List<string> _empowerments = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Cultural, religious, or narrative meaning associated with the phenomenon</summary>
        [JsonProperty("mythology")]
        [SerializeField] private string _mythology;

        /// <summary>Broader phenomenon that this one is part of or linked to</summary>
        /// <remarks>Link: UUID of phenomenon. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("system")]
        [SerializeField] private string _system;

        /// <summary>Conceptual mechanisms or patterns that cause the phenomenon to activate</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("triggers")]
        [SerializeField] private List<string> _triggers = new List<string>();

        /// <summary>Characters capable of intentionally directing or controlling the phenomenon</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("wielders")]
        [SerializeField] private List<string> _wielders = new List<string>();

        /// <summary>Locations where the phenomenon occurs or is known to manifest</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("environments")]
        [SerializeField] private List<string> _environments = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Expression { get => _expression; set => _expression = value; }
        public string Effects { get => _effects; set => _effects = value; }
        public SerializableNullable<int> Duration { get => _duration; set => _duration = value; }
        public List<string> Catalysts => _catalysts;
        public List<string> Empowerments => _empowerments;

        public string Mythology { get => _mythology; set => _mythology = value; }
        public string System { get => _system; set => _system = value; }
        public List<string> Triggers => _triggers;
        public List<string> Wielders => _wielders;
        public List<string> Environments => _environments;
    }
}
