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
    /// The <c>title</c> element type.
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
    public class OWTitle : OWElement
    {
        // -- Mandate -----------------------------------------------------

        /// <summary>Rights or powers granted by the title</summary>
        [JsonProperty("authority")]
        [SerializeField] private string _authority;

        /// <summary>Conditions or qualifications for receiving or holding the title</summary>
        [JsonProperty("eligibility")]
        [SerializeField] private string _eligibility;

        /// <summary>Date on which the title was granted, defined in world TIME units</summary>
        [JsonProperty("grant_date")]
        [SerializeField] private SerializableNullable<int> _grantDate;

        /// <summary>Date on which the title ended or was revoked, defined in world TIME units</summary>
        [JsonProperty("revoke_date")]
        [SerializeField] private SerializableNullable<int> _revokeDate;

        /// <summary>Institution that formally created or granted the title</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("issuer")]
        [SerializeField] private string _issuer;

        /// <summary>Institution in which the title functions or holds relevance</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("body")]
        [SerializeField] private string _body;

        /// <summary>Another title that has authority over this one</summary>
        /// <remarks>Link: UUID of title. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("superior_title")]
        [SerializeField] private string _superiorTitle;

        /// <summary>Characters who currently hold or represent the title</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("holders")]
        [SerializeField] private List<string> _holders = new List<string>();

        /// <summary>Objects that symbolize or authorize the title</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("symbols")]
        [SerializeField] private List<string> _symbols = new List<string>();

        // -- World -------------------------------------------------------

        /// <summary>Current state or general condition of the title</summary>
        [JsonProperty("status")]
        [SerializeField] private string _status;

        /// <summary>Background information on the title's origin, evolution, or significance</summary>
        [JsonProperty("history")]
        [SerializeField] private string _history;

        /// <summary>Characters otherwise relevant to the title</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("characters")]
        [SerializeField] private List<string> _characters = new List<string>();

        /// <summary>Institutions relevant to the title</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("institutions")]
        [SerializeField] private List<string> _institutions = new List<string>();

        /// <summary>Families relevant to the title</summary>
        /// <remarks>Link: UUIDs of family. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("families")]
        [SerializeField] private List<string> _families = new List<string>();

        /// <summary>Zones relevant to the title</summary>
        /// <remarks>Link: UUIDs of zone. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("zones")]
        [SerializeField] private List<string> _zones = new List<string>();

        /// <summary>Locations relevant to the title</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("locations")]
        [SerializeField] private List<string> _locations = new List<string>();

        /// <summary>Objects otherwise relevant to the title</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("objects")]
        [SerializeField] private List<string> _objects = new List<string>();

        /// <summary>Constructs relevant to the title</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("constructs")]
        [SerializeField] private List<string> _constructs = new List<string>();

        /// <summary>Laws relevant to the title</summary>
        /// <remarks>Link: UUIDs of law. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("laws")]
        [SerializeField] private List<string> _laws = new List<string>();

        /// <summary>Collectives relevant to the title</summary>
        /// <remarks>Link: UUIDs of collective. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("collectives")]
        [SerializeField] private List<string> _collectives = new List<string>();

        /// <summary>Creatures relevant to the title</summary>
        /// <remarks>Link: UUIDs of creature. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("creatures")]
        [SerializeField] private List<string> _creatures = new List<string>();

        /// <summary>Phenomena relevant to the title</summary>
        /// <remarks>Link: UUIDs of phenomenon. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("phenomena")]
        [SerializeField] private List<string> _phenomena = new List<string>();

        /// <summary>Species relevant to the title</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("species")]
        [SerializeField] private List<string> _species = new List<string>();

        /// <summary>Languages relevant to the title</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("languages")]
        [SerializeField] private List<string> _languages = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Authority { get => _authority; set => _authority = value; }
        public string Eligibility { get => _eligibility; set => _eligibility = value; }
        public SerializableNullable<int> GrantDate { get => _grantDate; set => _grantDate = value; }
        public SerializableNullable<int> RevokeDate { get => _revokeDate; set => _revokeDate = value; }
        public string Issuer { get => _issuer; set => _issuer = value; }
        public string Body { get => _body; set => _body = value; }
        public string SuperiorTitle { get => _superiorTitle; set => _superiorTitle = value; }
        public List<string> Holders => _holders;
        public List<string> Symbols => _symbols;

        public string Status { get => _status; set => _status = value; }
        public string History { get => _history; set => _history = value; }
        public List<string> Characters => _characters;
        public List<string> Institutions => _institutions;
        public List<string> Families => _families;
        public List<string> Zones => _zones;
        public List<string> Locations => _locations;
        public List<string> Objects => _objects;
        public List<string> Constructs => _constructs;
        public List<string> Laws => _laws;
        public List<string> Collectives => _collectives;
        public List<string> Creatures => _creatures;
        public List<string> Phenomena => _phenomena;
        public List<string> Species => _species;
        public List<string> Languages => _languages;
    }
}
