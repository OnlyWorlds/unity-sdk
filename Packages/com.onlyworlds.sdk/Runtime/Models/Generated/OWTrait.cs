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
    /// The <c>trait</c> element type.
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
    public class OWTrait : OWElement
    {
        // -- Qualitative -------------------------------------------------

        /// <summary>Relating to social relationships, reputation, or interaction dynamics</summary>
        [JsonProperty("social_effects")]
        [SerializeField] private string _socialEffects;

        /// <summary>Relating to physical changes, limitations, or enhancements</summary>
        [JsonProperty("physical_effects")]
        [SerializeField] private string _physicalEffects;

        /// <summary>Relating to practical or learned performance or aptitude</summary>
        [JsonProperty("functional_effects")]
        [SerializeField] private string _functionalEffects;

        /// <summary>Relating to temperament, mental state, or personality expression</summary>
        [JsonProperty("personality_effects")]
        [SerializeField] private string _personalityEffects;

        /// <summary>Relating to visible aspects and patterns of behavior</summary>
        [JsonProperty("behaviour_effects")]
        [SerializeField] private string _behaviourEffects;

        // -- Quantitative ------------------------------------------------

        /// <summary>Affecting a character's charisma score</summary>
        [JsonProperty("charisma")]
        [SerializeField] private SerializableNullable<int> _charisma;

        /// <summary>Affecting a character's coercion score</summary>
        [JsonProperty("coercion")]
        [SerializeField] private SerializableNullable<int> _coercion;

        /// <summary>Affecting a character's competence score</summary>
        [JsonProperty("competence")]
        [SerializeField] private SerializableNullable<int> _competence;

        /// <summary>Affecting a character's compassion score</summary>
        [JsonProperty("compassion")]
        [SerializeField] private SerializableNullable<int> _compassion;

        /// <summary>Affecting a character's creativity score</summary>
        [JsonProperty("creativity")]
        [SerializeField] private SerializableNullable<int> _creativity;

        /// <summary>Affecting a character's courage score</summary>
        [JsonProperty("courage")]
        [SerializeField] private SerializableNullable<int> _courage;

        // -- World -------------------------------------------------------

        /// <summary>Describes the trait's societal, symbolic, or systemic presence</summary>
        [JsonProperty("significance")]
        [SerializeField] private string _significance;

        /// <summary>Opposing trait that contradicts or nullifies the trait</summary>
        /// <remarks>Link: UUID of trait. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("anti_trait")]
        [SerializeField] private string _antiTrait;

        /// <summary>Abilities strengthened or enabled by the trait</summary>
        /// <remarks>Link: UUIDs of ability. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("empowered_abilities")]
        [SerializeField] private List<string> _empoweredAbilities = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string SocialEffects { get => _socialEffects; set => _socialEffects = value; }
        public string PhysicalEffects { get => _physicalEffects; set => _physicalEffects = value; }
        public string FunctionalEffects { get => _functionalEffects; set => _functionalEffects = value; }
        public string PersonalityEffects { get => _personalityEffects; set => _personalityEffects = value; }
        public string BehaviourEffects { get => _behaviourEffects; set => _behaviourEffects = value; }

        public SerializableNullable<int> Charisma { get => _charisma; set => _charisma = value; }
        public SerializableNullable<int> Coercion { get => _coercion; set => _coercion = value; }
        public SerializableNullable<int> Competence { get => _competence; set => _competence = value; }
        public SerializableNullable<int> Compassion { get => _compassion; set => _compassion = value; }
        public SerializableNullable<int> Creativity { get => _creativity; set => _creativity = value; }
        public SerializableNullable<int> Courage { get => _courage; set => _courage = value; }

        public string Significance { get => _significance; set => _significance = value; }
        public string AntiTrait { get => _antiTrait; set => _antiTrait = value; }
        public List<string> EmpoweredAbilities => _empoweredAbilities;
    }
}
