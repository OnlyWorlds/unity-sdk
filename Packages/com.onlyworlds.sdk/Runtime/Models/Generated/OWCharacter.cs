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
    /// The <c>character</c> element type.
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
    public class OWCharacter : OWElement
    {
        // -- Constitution ------------------------------------------------

        /// <summary>The character's visible physical features and body attributes</summary>
        [JsonProperty("physicality")]
        [SerializeField] private string _physicality;

        /// <summary>The character's mindset, emotional tone, and style of thinking</summary>
        [JsonProperty("mentality")]
        [SerializeField] private string _mentality;

        /// <summary>The character's approximate or exact height, using world LENGTH units</summary>
        [JsonProperty("height")]
        [SerializeField] private SerializableNullable<int> _height;

        /// <summary>The character's approximate or exact weight, using world MASS units</summary>
        [JsonProperty("weight")]
        [SerializeField] private SerializableNullable<int> _weight;

        /// <summary>Species the character might belong to</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        /// <summary>Traits for notable behavioral, physical, or systemic characteristics</summary>
        /// <remarks>Link: UUIDs of trait. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        /// <summary>Abilities the character might perform, control, or invoke</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        // -- Origins -----------------------------------------------------

        /// <summary>History, upbringing, or formative experiences of the character</summary>
        [JsonProperty("background")]
        [SerializeField] private string _background;

        /// <summary>Core desires, goals, or values that drive the character's choices and behavior</summary>
        [JsonProperty("motivations")]
        [SerializeField] private string _motivations;

        /// <summary>Moment of birth, expressed in the world's TIME units</summary>
        [JsonProperty("birth_date")]
        [SerializeField] private SerializableNullable<int> _birthDate;

        /// <summary>Location where the character was born</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("birthplace")]
        [SerializeField] private string _birthplace;

        /// <summary>Languages the character can understand, speak, or use for communication</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Brief summary of the character's current condition, role, or predicament</summary>
        [JsonProperty("reputation")]
        [SerializeField] private string _reputation;

        /// <summary>The character's present physical location</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("location")]
        [SerializeField] private string _location;

        /// <summary>Key objects owned by or symbolically linked to the character</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("objects")]
        [SerializeField] private List<string> _objects = new List<string>();

        /// <summary>Institutions the character is affiliated with</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("institutions")]
        [SerializeField] private List<string> _institutions = new List<string>();

        // -- Personality -------------------------------------------------

        /// <summary>Ability to attract, inspire, and influence others</summary>
        [JsonProperty("charisma")]
        [SerializeField] private SerializableNullable<int> _charisma;

        /// <summary>Capacity to dominate, intimidate, or apply force to shape outcomes</summary>
        [JsonProperty("coercion")]
        [SerializeField] private SerializableNullable<int> _coercion;

        /// <summary>Skill in planning, understanding, and managing complex systems or situations</summary>
        [JsonProperty("competence")]
        [SerializeField] private SerializableNullable<int> _competence;

        /// <summary>Willingness to empathize with and care for others</summary>
        [JsonProperty("compassion")]
        [SerializeField] private SerializableNullable<int> _compassion;

        /// <summary>Ability to generate novel ideas, perspectives, or solutions</summary>
        [JsonProperty("creativity")]
        [SerializeField] private SerializableNullable<int> _creativity;

        /// <summary>Readiness to face danger, risk, or adversity</summary>
        [JsonProperty("courage")]
        [SerializeField] private SerializableNullable<int> _courage;

        // -- Social ------------------------------------------------------

        /// <summary>Families the character belongs to by blood or adoption</summary>
        /// <remarks>Link: UUIDs of family. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("family")]
        [SerializeField] private List<string> _family = new List<string>();

        /// <summary>Characters the character considers close allies or companions</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("friends")]
        [SerializeField] private List<string> _friends = new List<string>();

        /// <summary>Characters the character is in active opposition or competition with</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("rivals")]
        [SerializeField] private List<string> _rivals = new List<string>();

        // -- TTRPG -------------------------------------------------------

        /// <summary>Progression rank of the character in a game system</summary>
        [JsonProperty("level")]
        [SerializeField] private SerializableNullable<int> _level;

        /// <summary>Total health available to the character</summary>
        [JsonProperty("hit_points")]
        [SerializeField] private SerializableNullable<int> _hitPoints;

        /// <summary>Physical force and carrying capacity</summary>
        [JsonProperty("STR")]
        [SerializeField] private SerializableNullable<int> _str;

        /// <summary>Agility, coordination, and reflexes</summary>
        [JsonProperty("DEX")]
        [SerializeField] private SerializableNullable<int> _dex;

        /// <summary>Endurance and resistance to strain</summary>
        [JsonProperty("CON")]
        [SerializeField] private SerializableNullable<int> _con;

        /// <summary>Reasoning, memory, and learning</summary>
        [JsonProperty("INT")]
        [SerializeField] private SerializableNullable<int> _int;

        /// <summary>Intuition, awareness, and judgment</summary>
        [JsonProperty("WIS")]
        [SerializeField] private SerializableNullable<int> _wis;

        /// <summary>Persuasiveness and personal magnetism</summary>
        [JsonProperty("CHA")]
        [SerializeField] private SerializableNullable<int> _cha;

        // -- Accessors --------------------------------------------------------

        public string Physicality { get => _physicality; set => _physicality = value; }
        public string Mentality { get => _mentality; set => _mentality = value; }
        public SerializableNullable<int> Height { get => _height; set => _height = value; }
        public SerializableNullable<int> Weight { get => _weight; set => _weight = value; }
        public List<string> Species => _species;
        public List<string> Traits => _traits;
        public List<string> Abilities => _abilities;

        public string Background { get => _background; set => _background = value; }
        public string Motivations { get => _motivations; set => _motivations = value; }
        public SerializableNullable<int> BirthDate { get => _birthDate; set => _birthDate = value; }
        public string Birthplace { get => _birthplace; set => _birthplace = value; }
        public List<string> Languages => _languages;

        public string Reputation { get => _reputation; set => _reputation = value; }
        public string Location { get => _location; set => _location = value; }
        public List<string> Objects => _objects;
        public List<string> Institutions => _institutions;

        public SerializableNullable<int> Charisma { get => _charisma; set => _charisma = value; }
        public SerializableNullable<int> Coercion { get => _coercion; set => _coercion = value; }
        public SerializableNullable<int> Competence { get => _competence; set => _competence = value; }
        public SerializableNullable<int> Compassion { get => _compassion; set => _compassion = value; }
        public SerializableNullable<int> Creativity { get => _creativity; set => _creativity = value; }
        public SerializableNullable<int> Courage { get => _courage; set => _courage = value; }

        public List<string> Family => _family;
        public List<string> Friends => _friends;
        public List<string> Rivals => _rivals;

        public SerializableNullable<int> Level { get => _level; set => _level = value; }
        public SerializableNullable<int> HitPoints { get => _hitPoints; set => _hitPoints = value; }
        public SerializableNullable<int> Str { get => _str; set => _str = value; }
        public SerializableNullable<int> Dex { get => _dex; set => _dex = value; }
        public SerializableNullable<int> Con { get => _con; set => _con = value; }
        public SerializableNullable<int> Int { get => _int; set => _int = value; }
        public SerializableNullable<int> Wis { get => _wis; set => _wis = value; }
        public SerializableNullable<int> Cha { get => _cha; set => _cha = value; }
    }
}
