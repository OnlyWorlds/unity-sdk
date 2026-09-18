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
    /// The <c>location</c> element type.
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
    public class OWLocation : OWElement
    {
        // -- Setting -----------------------------------------------------

        /// <summary>Visual and environmental aspects of the location</summary>
        [JsonProperty("form")]
        [SerializeField] private string _form;

        /// <summary>Main use, role, or purpose of the location within the world</summary>
        [JsonProperty("function")]
        [SerializeField] private string _function;

        /// <summary>Date on which the location was founded, established, or designated</summary>
        [JsonProperty("founding_date")]
        [SerializeField] private SerializableNullable<int> _foundingDate;

        /// <summary>Wider location that this location is part of</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("parent_location")]
        [SerializeField] private string _parentLocation;

        /// <summary>Distinct collective groups or communities residing within the location</summary>
        /// <remarks>Link: UUIDs of collective. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("populations")]
        [SerializeField] private List<string> _populations = new List<string>();

        // -- Politics ----------------------------------------------------

        /// <summary>Political structure, stability, and dynamics of the location</summary>
        [JsonProperty("political_climate")]
        [SerializeField] private string _politicalClimate;

        /// <summary>Institution that has the highest degree of political control over the location</summary>
        /// <remarks>Link: UUID of institution. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("primary_power")]
        [SerializeField] private string _primaryPower;

        /// <summary>Governing figure assigned by the location's primary power</summary>
        /// <remarks>Link: UUID of title. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("governing_title")]
        [SerializeField] private string _governingTitle;

        /// <summary>Institutions with significant political control</summary>
        /// <remarks>Link: UUIDs of institution. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("secondary_powers")]
        [SerializeField] private List<string> _secondaryPowers = new List<string>();

        /// <summary>Zone of interest that is associated with the location</summary>
        /// <remarks>Link: UUID of zone. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("zone")]
        [SerializeField] private string _zone;

        /// <summary>Locations with active, traditional, or historical rivalries</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("rival")]
        [SerializeField] private string _rival;

        /// <summary>Locations with active, cooperative, or historical ties</summary>
        /// <remarks>Link: UUID of location. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("partner")]
        [SerializeField] private string _partner;

        // -- World -------------------------------------------------------

        /// <summary>Cultural practices, habits, or festivals</summary>
        [JsonProperty("customs")]
        [SerializeField] private string _customs;

        /// <summary>Individual(s) who founded or named the location</summary>
        /// <remarks>Link: UUIDs of character. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("founders")]
        [SerializeField] private List<string> _founders = new List<string>();

        /// <summary>Significant religious constructs practiced or recognized at the location</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("cults")]
        [SerializeField] private List<string> _cults = new List<string>();

        /// <summary>Organisms or other species locally consumed or celebrated as specialty foods</summary>
        /// <remarks>Link: UUIDs of species. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("delicacies")]
        [SerializeField] private List<string> _delicacies = new List<string>();

        // -- Production --------------------------------------------------

        /// <summary>Techniques or strategies used to gather natural resources</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("extraction_methods")]
        [SerializeField] private List<string> _extractionMethods = new List<string>();

        /// <summary>Products and materials that are gathered or obtained</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("extraction_goods")]
        [SerializeField] private List<string> _extractionGoods = new List<string>();

        /// <summary>Techniques or workflows used to refine or manufacture goods</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("industry_methods")]
        [SerializeField] private List<string> _industryMethods = new List<string>();

        /// <summary>Products and materials that are refined or manufactured</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("industry_goods")]
        [SerializeField] private List<string> _industryGoods = new List<string>();

        // -- Commerce ----------------------------------------------------

        /// <summary>Roads, ports, and other physical systems that enable the movement of goods and people</summary>
        [JsonProperty("infrastructure")]
        [SerializeField] private string _infrastructure;

        /// <summary>Locations that receive extracted goods through trade, interchange, or seizure</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("extraction_markets")]
        [SerializeField] private List<string> _extractionMarkets = new List<string>();

        /// <summary>Locations that receive industrial goods through trade, interchange, or seizure</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("industry_markets")]
        [SerializeField] private List<string> _industryMarkets = new List<string>();

        /// <summary>Trade media recognized or circulated at the location</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("currencies")]
        [SerializeField] private List<string> _currencies = new List<string>();

        // -- Construction ------------------------------------------------

        /// <summary>Look, form, and materials used in the built environment and location design</summary>
        [JsonProperty("architecture")]
        [SerializeField] private string _architecture;

        /// <summary>Notable structural objects at the location</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("buildings")]
        [SerializeField] private List<string> _buildings = new List<string>();

        /// <summary>Techniques or systems used to construct structures at the location</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("building_methods")]
        [SerializeField] private List<string> _buildingMethods = new List<string>();

        // -- Defense -----------------------------------------------------

        /// <summary>Qualities of natural, constructed, and implemented defenses at the location</summary>
        [JsonProperty("defensibility")]
        [SerializeField] private string _defensibility;

        /// <summary>Height or elevation of the location relative to surrounding terrain, defined in world DISTANCE units</summary>
        [JsonProperty("elevation")]
        [SerializeField] private SerializableNullable<int> _elevation;

        /// <summary>Military units or forces responsible for defending the location</summary>
        /// <remarks>Link: UUIDs of construct. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("fighters")]
        [SerializeField] private List<string> _fighters = new List<string>();

        /// <summary>Objects or installations for defending the location</summary>
        /// <remarks>Link: UUIDs of object. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("defensive_objects")]
        [SerializeField] private List<string> _defensiveObjects = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Form { get => _form; set => _form = value; }
        public string Function { get => _function; set => _function = value; }
        public SerializableNullable<int> FoundingDate { get => _foundingDate; set => _foundingDate = value; }
        public string ParentLocation { get => _parentLocation; set => _parentLocation = value; }
        public List<string> Populations => _populations;

        public string PoliticalClimate { get => _politicalClimate; set => _politicalClimate = value; }
        public string PrimaryPower { get => _primaryPower; set => _primaryPower = value; }
        public string GoverningTitle { get => _governingTitle; set => _governingTitle = value; }
        public List<string> SecondaryPowers => _secondaryPowers;
        public string Zone { get => _zone; set => _zone = value; }
        public string Rival { get => _rival; set => _rival = value; }
        public string Partner { get => _partner; set => _partner = value; }

        public string Customs { get => _customs; set => _customs = value; }
        public List<string> Founders => _founders;
        public List<string> Cults => _cults;
        public List<string> Delicacies => _delicacies;

        public List<string> ExtractionMethods => _extractionMethods;
        public List<string> ExtractionGoods => _extractionGoods;
        public List<string> IndustryMethods => _industryMethods;
        public List<string> IndustryGoods => _industryGoods;

        public string Infrastructure { get => _infrastructure; set => _infrastructure = value; }
        public List<string> ExtractionMarkets => _extractionMarkets;
        public List<string> IndustryMarkets => _industryMarkets;
        public List<string> Currencies => _currencies;

        public string Architecture { get => _architecture; set => _architecture = value; }
        public List<string> Buildings => _buildings;
        public List<string> BuildingMethods => _buildingMethods;

        public string Defensibility { get => _defensibility; set => _defensibility = value; }
        public SerializableNullable<int> Elevation { get => _elevation; set => _elevation = value; }
        public List<string> Fighters => _fighters;
        public List<string> DefensiveObjects => _defensiveObjects;
    }
}
