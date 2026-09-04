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
    /// The <c>ability</c> element type.
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
    public class OWAbility : OWElement
    {
        // -- Mechanics ---------------------------------------------------

        /// <summary>Method or conditions under which the ability is activated</summary>
        [JsonProperty("activation")]
        [SerializeField] private string _activation;

        /// <summary>Length of time the ability remains active or its effects persist, measured in TIME units</summary>
        [JsonProperty("duration")]
        [SerializeField] private SerializableNullable<int> _duration;

        /// <summary>Relative measure of the ability's inherent potency or force, used for scaling or comparison purposes</summary>
        [JsonProperty("potency")]
        [SerializeField] private SerializableNullable<int> _potency;

        /// <summary>Effective reach or distance at which the ability can be used, measured in DISTANCE units</summary>
        [JsonProperty("range")]
        [SerializeField] private SerializableNullable<int> _range;

        /// <summary>Phenomena that result from the ability's use, such as environmental changes or sensory effects</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("effects")]
        [SerializeField] private List<string> _effects = new List<string>();

        /// <summary>Describes specific difficulties or constraints that make the ability hard to master or use effectively</summary>
        [JsonProperty("challenges")]
        [SerializeField] private string _challenges;

        /// <summary>Traits that naturally enhance or improve performance with this ability</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("talents")]
        [SerializeField] private List<string> _talents = new List<string>();

        /// <summary>Constructs that must be satisfied for the ability to be used, such as rituals, permissions, or required roles</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("requisites")]
        [SerializeField] private List<string> _requisites = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>How widely the ability is known or practiced, and potential clues to its origins and cultural diffusion</summary>
        [JsonProperty("prevalence")]
        [SerializeField] private string _prevalence;

        /// <summary>A construct that expresses the conceptual, social, or institutional system this ability operates within</summary>
        /// <remarks>Link: UUID of construct. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("tradition")]
        [SerializeField] private string _tradition;

        /// <summary>The phenomenon that serves as the enabling force or condition that allows this ability to function</summary>
        /// <remarks>Link: UUID of phenomenon. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("source")]
        [SerializeField] private string _source;

        /// <summary>Location where the ability is most strongly rooted, developed, or traditionally practiced</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("locus")]
        [SerializeField] private string _locus;

        /// <summary>Objects or tools required to activate, channel, or perform the ability</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("instruments")]
        [SerializeField] private List<string> _instruments = new List<string>();

        /// <summary>Magic frameworks or structures that the ability associates with</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("systems")]
        [SerializeField] private List<string> _systems = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Activation { get => _activation; set => _activation = value; }
        public SerializableNullable<int> Duration { get => _duration; set => _duration = value; }
        public SerializableNullable<int> Potency { get => _potency; set => _potency = value; }
        public SerializableNullable<int> Range { get => _range; set => _range = value; }
        public List<string> Effects => _effects;
        public string Challenges { get => _challenges; set => _challenges = value; }
        public List<string> Talents => _talents;
        public List<string> Requisites => _requisites;

        public string Prevalence { get => _prevalence; set => _prevalence = value; }
        public string Tradition { get => _tradition; set => _tradition = value; }
        public string Source { get => _source; set => _source = value; }
        public string Locus { get => _locus; set => _locus = value; }
        public List<string> Instruments => _instruments;
        public List<string> Systems => _systems;
    }
}
