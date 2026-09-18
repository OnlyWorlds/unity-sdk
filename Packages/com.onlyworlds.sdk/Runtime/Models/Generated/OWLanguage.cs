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
    /// The <c>language</c> element type.
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
    public class OWLanguage : OWElement
    {
        // -- Structure ---------------------------------------------------

        /// <summary>The language's sound systems, including phonemes, tone, and pronunciation rules</summary>
        [JsonProperty("phonology")]
        [SerializeField] private string _phonology;

        /// <summary>Rules governing syntax, morphology, and sentence structure</summary>
        [JsonProperty("grammar")]
        [SerializeField] private string _grammar;

        /// <summary>Vocabulary principles or full word lists used in the language</summary>
        [JsonProperty("lexicon")]
        [SerializeField] private string _lexicon;

        /// <summary>Script or notation system used to represent the language in written form</summary>
        [JsonProperty("writing")]
        [SerializeField] private string _writing;

        /// <summary>Linguistic group or typological category the language belongs to</summary>
        /// <remarks>Link: UUID of construct. Bare id -- resolve through the cache.</remarks>
        [JsonProperty("classification")]
        [SerializeField] private string _classification;

        // -- World -------------------------------------------------------

        /// <summary>Current vitality, reputation, or dominance of the language</summary>
        [JsonProperty("status")]
        [SerializeField] private string _status;

        /// <summary>Geographical areas where the language is used or spoken</summary>
        /// <remarks>Link: UUIDs of location. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("spread")]
        [SerializeField] private List<string> _spread = new List<string>();

        /// <summary>Variants or dialect languages derived from the language</summary>
        /// <remarks>Link: UUIDs of language. Bare ids -- resolve through the cache.</remarks>
        [JsonProperty("dialects")]
        [SerializeField] private List<string> _dialects = new List<string>();

        // -- Accessors --------------------------------------------------------

        public string Phonology { get => _phonology; set => _phonology = value; }
        public string Grammar { get => _grammar; set => _grammar = value; }
        public string Lexicon { get => _lexicon; set => _lexicon = value; }
        public string Writing { get => _writing; set => _writing = value; }
        public string Classification { get => _classification; set => _classification = value; }

        public string Status { get => _status; set => _status = value; }
        public List<string> Spread => _spread;
        public List<string> Dialects => _dialects;
    }
}
