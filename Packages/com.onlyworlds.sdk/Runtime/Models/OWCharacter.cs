using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A character. The schema's heaviest type, and the worst case for nullable integers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN PROVING MODEL -- expected to be replaced wholesale by the emitter.
    /// </para>
    /// <para>
    /// Chosen as a proving model because it carries 17 of the schema's 70 nullable integers -- more
    /// than any other type. Every one of them is a place where collapsing null to 0 would assert
    /// something the author never said. A character with <c>CHA</c> unset is not a character with
    /// CHA 0, and a level-unknown character is not a level-0 character.
    /// </para>
    /// <para>
    /// Field order follows the schema's own document order, which IS display order: Constitution,
    /// Origins, World, Personality, Social, TTRPG. The emitter will carry these groups as a
    /// generated static table; here they are comments, because the viewer does not exist yet.
    /// </para>
    /// <para>
    /// Links are bare UUIDs -- v2 wire truth. No <c>_ids</c> suffix (that is dead v1 dialect), and
    /// no link-object stubs: the cache is the resolver.
    /// </para>
    /// </remarks>
    // OptIn must be repeated on every subclass -- Newtonsoft does not inherit [JsonObject],
    // so a model without it silently serializes every public property alongside the
    // attributed fields, duplicating each one on the wire. The emitter must emit this line.
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    public class OWCharacter : OWElement
    {
        // -- Constitution -----------------------------------------------------

        [JsonProperty("physicality")]
        [SerializeField] private string _physicality;

        [JsonProperty("mentality")]
        [SerializeField] private string _mentality;

        [JsonProperty("height")]
        [SerializeField] private SerializableNullable<int> _height;

        [JsonProperty("weight")]
        [SerializeField] private SerializableNullable<int> _weight;

        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        [JsonProperty("traits")]
        [SerializeField] private List<string> _traits = new List<string>();

        [JsonProperty("abilities")]
        [SerializeField] private List<string> _abilities = new List<string>();

        // -- Origins ----------------------------------------------------------

        [JsonProperty("background")]
        [SerializeField] private string _background;

        [JsonProperty("motivations")]
        [SerializeField] private string _motivations;

        [JsonProperty("birth_date")]
        [SerializeField] private SerializableNullable<int> _birthDate;

        [JsonProperty("birthplace")]
        [SerializeField] private string _birthplace;

        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        // -- World ------------------------------------------------------------

        [JsonProperty("reputation")]
        [SerializeField] private string _reputation;

        [JsonProperty("location")]
        [SerializeField] private string _location;

        [JsonProperty("objects")]
        [SerializeField] private List<string> _objects = new List<string>();

        [JsonProperty("institutions")]
        [SerializeField] private List<string> _institutions = new List<string>();

        // -- Personality ------------------------------------------------------

        [JsonProperty("charisma")]
        [SerializeField] private SerializableNullable<int> _charisma;

        [JsonProperty("coercion")]
        [SerializeField] private SerializableNullable<int> _coercion;

        [JsonProperty("competence")]
        [SerializeField] private SerializableNullable<int> _competence;

        [JsonProperty("compassion")]
        [SerializeField] private SerializableNullable<int> _compassion;

        [JsonProperty("creativity")]
        [SerializeField] private SerializableNullable<int> _creativity;

        [JsonProperty("courage")]
        [SerializeField] private SerializableNullable<int> _courage;

        // -- Social -----------------------------------------------------------

        [JsonProperty("family")]
        [SerializeField] private List<string> _family = new List<string>();

        [JsonProperty("friends")]
        [SerializeField] private List<string> _friends = new List<string>();

        [JsonProperty("rivals")]
        [SerializeField] private List<string> _rivals = new List<string>();

        // -- TTRPG ------------------------------------------------------------

        [JsonProperty("level")]
        [SerializeField] private SerializableNullable<int> _level;

        [JsonProperty("hit_points")]
        [SerializeField] private SerializableNullable<int> _hitPoints;

        [JsonProperty("STR")]
        [SerializeField] private SerializableNullable<int> _str;

        [JsonProperty("DEX")]
        [SerializeField] private SerializableNullable<int> _dex;

        [JsonProperty("CON")]
        [SerializeField] private SerializableNullable<int> _con;

        [JsonProperty("INT")]
        [SerializeField] private SerializableNullable<int> _int;

        [JsonProperty("WIS")]
        [SerializeField] private SerializableNullable<int> _wis;

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
