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
    /// The <c>species</c> element type.
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
    public class OWSpecies : OWElement
    {
        // -- Biology -----------------------------------------------------

        /// <summary>Typical physical or form features of the species</summary>
        [JsonProperty("appearance")]
        [SerializeField] private string _appearance;

        /// <summary>Average or typical life expectancy of an individual, defined in world TIME units</summary>
        [JsonProperty("life_span")]
        [SerializeField] private SerializableNullable<int> _lifeSpan;

        /// <summary>Average or typical adult weight, defined in world MASS units</summary>
        [JsonProperty("weight")]
        [SerializeField] private SerializableNullable<int> _weight;

        /// <summary>Other species consumed as food sources</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("nourishment")]
        [SerializeField] private List<string> _nourishment = new List<string>();

        /// <summary>Reproductive method(s) of the species</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("reproduction")]
        [SerializeField] private List<string> _reproduction = new List<string>();

        /// <summary>Special physiological or evolutionary abilities</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("adaptations")]
        [SerializeField] private List<string> _adaptations = new List<string>();

        // -- Psychology --------------------------------------------------

        /// <summary>Innate behavioral drives and survival tendencies</summary>
        [JsonProperty("instincts")]
        [SerializeField] private string _instincts;

        /// <summary>Typical patterns of social behavior</summary>
        [JsonProperty("sociality")]
        [SerializeField] private string _sociality;

        /// <summary>Overall behavioral disposition</summary>
        [JsonProperty("temperament")]
        [SerializeField] private string _temperament;

        /// <summary>Typical methods and approaches of interaction</summary>
        [JsonProperty("communication")]
        [SerializeField] private string _communication;

        /// <summary>General aggressiveness level, on relative scale of 0 to 100</summary>
        [JsonProperty("aggression")]
        [SerializeField] private SerializableNullable<int> _aggression;

        /// <summary>Behavioral patterns associated with the species</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>The species' ecological or cultural function in the world</summary>
        [JsonProperty("role")]
        [SerializeField] private string _role;

        /// <summary>Species that the species is considered a subspecies of</summary>
        /// <remarks>Link: UUID of species. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_species")]
        [SerializeField] private string _parentSpecies;

        /// <summary>Locations associated with the species or its habitat</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("locations")]
        [SerializeField] private List<string> _locations = new List<string>();

        /// <summary>Zones associated with the species or its habitat</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("zones")]
        [SerializeField] private List<string> _zones = new List<string>();

        /// <summary>Phenomena associated with the species or its behavior</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("affinities")]
        [SerializeField] private List<string> _affinities = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Appearance { get => _appearance; set => _appearance = value; }
        public SerializableNullable<int> LifeSpan { get => _lifeSpan; set => _lifeSpan = value; }
        public SerializableNullable<int> Weight { get => _weight; set => _weight = value; }
        public List<string> Nourishment => _nourishment;
        public List<string> Reproduction => _reproduction;
        public List<string> Adaptations => _adaptations;

        public string Instincts { get => _instincts; set => _instincts = value; }
        public string Sociality { get => _sociality; set => _sociality = value; }
        public string Temperament { get => _temperament; set => _temperament = value; }
        public string Communication { get => _communication; set => _communication = value; }
        public SerializableNullable<int> Aggression { get => _aggression; set => _aggression = value; }
        public List<string> Traits => _traits;

        public string Role { get => _role; set => _role = value; }
        public string ParentSpecies { get => _parentSpecies; set => _parentSpecies = value; }
        public List<string> Locations => _locations;
        public List<string> Zones => _zones;
        public List<string> Affinities => _affinities;
    }
}
