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
    /// The <c>creature</c> element type.
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
    public class OWCreature : OWElement
    {
        // -- Biology -----------------------------------------------------

        /// <summary>Visual description of the creature</summary>
        [JsonProperty("appearance")]
        [SerializeField] private string _appearance;

        /// <summary>Approximate or exact weight of the creature, using world MASS units</summary>
        [JsonProperty("weight")]
        [SerializeField] private SerializableNullable<int> _weight;

        /// <summary>Approximate height of the creature, using the world's defined LENGTH units</summary>
        [JsonProperty("height")]
        [SerializeField] private SerializableNullable<int> _height;

        /// <summary>Species this creature belongs to</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        // -- Behavior ----------------------------------------------------

        /// <summary>Typical behaviors, instincts, or recurring actions the creature tends to display</summary>
        [JsonProperty("habits")]
        [SerializeField] private string _habits;

        /// <summary>The emotional tone or attitude the creature conveys through posture, expression, or aggression</summary>
        [JsonProperty("demeanor")]
        [SerializeField] private string _demeanor;

        /// <summary>Traits that influence the creature's behavior, capabilities, or appearance</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        /// <summary>Innate or learned abilities the creature can perform or activate</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        /// <summary>Languages the creature can understand, speak, or otherwise use to communicate</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Current situation or classification of the creature</summary>
        [JsonProperty("status")]
        [SerializeField] private string _status;

        /// <summary>The time of the creature's birth, recorded in the world's defined TIME unit</summary>
        [JsonProperty("birth_date")]
        [SerializeField] private SerializableNullable<int> _birthDate;

        /// <summary>Specific location where the creature is currently found or most associated with</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("location")]
        [SerializeField] private string _location;

        /// <summary>Larger area or region commonly inhabited or currently claimed by the creature</summary>
        /// <remarks>Link: UUID of zone. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("zone")]
        [SerializeField] private string _zone;

        // -- TTRPG -------------------------------------------------------

        /// <summary>Difficulty or threat level of the creature in a gameplay context</summary>
        [JsonProperty("challenge_rating")]
        [SerializeField] private SerializableNullable<int> _challengeRating;

        /// <summary>Total health or durability value in combat</summary>
        [JsonProperty("hit_points")]
        [SerializeField] private SerializableNullable<int> _hitPoints;

        /// <summary>Defense rating against physical attacks or effects</summary>
        [JsonProperty("armor_class")]
        [SerializeField] private SerializableNullable<int> _armorClass;

        /// <summary>Typical movement speed, measured in the world's DISTANCE unit per round</summary>
        [JsonProperty("speed")]
        [SerializeField] private SerializableNullable<int> _speed;

        /// <summary>Combat or tactical abilities the creature can perform or use</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("actions")]
        [SerializeField] private List<string> _actions = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Appearance { get => _appearance; set => _appearance = value; }
        public SerializableNullable<int> Weight { get => _weight; set => _weight = value; }
        public SerializableNullable<int> Height { get => _height; set => _height = value; }
        public List<string> Species => _species;

        public string Habits { get => _habits; set => _habits = value; }
        public string Demeanor { get => _demeanor; set => _demeanor = value; }
        public List<string> Traits => _traits;
        public List<string> Abilities => _abilities;
        public List<string> Languages => _languages;

        public string Status { get => _status; set => _status = value; }
        public SerializableNullable<int> BirthDate { get => _birthDate; set => _birthDate = value; }
        public string Location { get => _location; set => _location = value; }
        public string Zone { get => _zone; set => _zone = value; }

        public SerializableNullable<int> ChallengeRating { get => _challengeRating; set => _challengeRating = value; }
        public SerializableNullable<int> HitPoints { get => _hitPoints; set => _hitPoints = value; }
        public SerializableNullable<int> ArmorClass { get => _armorClass; set => _armorClass = value; }
        public SerializableNullable<int> Speed { get => _speed; set => _speed = value; }
        public List<string> Actions => _actions;
    }
}
