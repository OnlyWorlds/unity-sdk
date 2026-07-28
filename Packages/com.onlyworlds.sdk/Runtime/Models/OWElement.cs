using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Fields every OnlyWorlds element carries, plus the extension passthrough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN, TEMPORARILY. Skeld's emitter ruling is that the base type is EMITTED, not
    /// hand-written -- "a hand-written base is a hand-edited generated file waiting to happen." This
    /// file exists to prove the shape compiles and round-trips, and is expected to be deleted
    /// wholesale when the C# emitter lands. Do not grow it.
    /// </para>
    /// <para>
    /// Only <c>name</c> is required (Captain's ruling, 2026-07-28). Everything else is optional and
    /// nullable on the wire.
    /// </para>
    /// </remarks>
    [Serializable]
    public class OWElement
    {
        // -- Identity ---------------------------------------------------------

        [JsonProperty("id")]
        [SerializeField] private string _id;

        [JsonProperty("name")]
        [SerializeField] private string _name;

        [JsonProperty("description")]
        [SerializeField] private string _description;

        [JsonProperty("supertype")]
        [SerializeField] private string _supertype;

        [JsonProperty("subtype")]
        [SerializeField] private string _subtype;

        [JsonProperty("image_url")]
        [SerializeField] private string _imageUrl;

        public string Id { get => _id; set => _id = value; }
        public string Name { get => _name; set => _name = value; }
        public string Description { get => _description; set => _description = value; }
        public string Supertype { get => _supertype; set => _supertype = value; }
        public string Subtype { get => _subtype; set => _subtype = value; }
        public string ImageUrl { get => _imageUrl; set => _imageUrl = value; }

        // -- Server-managed. Read-only: never send these back. ----------------
        // See OWPayload.ReadOnlyFields -- world alone returns 422, because world identity comes
        // from the API key, not the payload.

        [JsonProperty("world")]
        [SerializeField] private string _world;

        [JsonProperty("type")]
        [SerializeField] private string _type;

        [JsonProperty("created_at")]
        [SerializeField] private string _createdAt;

        [JsonProperty("updated_at")]
        [SerializeField] private string _updatedAt;

        [JsonProperty("change_seq")]
        [SerializeField] private long _changeSeq;

        public string World => _world;
        public string Type => _type;
        public string CreatedAt => _createdAt;
        public string UpdatedAt => _updatedAt;
        public long ChangeSeq => _changeSeq;

        // -- Extensions -------------------------------------------------------

        /// <summary>
        /// Every field the model does not know about, preserved verbatim.
        /// </summary>
        /// <remarks>
        /// <para>
        /// AUTOMATIC AND NON-OPTIONAL. Other tools write their own state into <c>x_&lt;tool&gt;_*</c>
        /// fields on shared elements. A client that deserializes, drops what it does not model, and
        /// writes back has silently destroyed another tool's data -- and the March build did exactly
        /// that via <c>MissingMemberHandling.Ignore</c> with nowhere for unknown fields to go.
        /// </para>
        /// <para>
        /// <see cref="JsonExtensionDataAttribute"/> captures on read and re-emits on write, so the
        /// merge-back is automatic rather than something a caller must remember. Unity cannot
        /// serialize this dictionary, hence <see cref="_extensionsForUnity"/> below.
        /// </para>
        /// </remarks>
        [JsonExtensionData]
        private IDictionary<string, Newtonsoft.Json.Linq.JToken> _extensions
            = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();

        /// <summary>
        /// Unity-serializable mirror of the extensions bag, as a flat key/raw-JSON list.
        /// </summary>
        /// <remarks>
        /// Unity serializes neither <c>Dictionary</c> nor <c>JToken</c>, so an element cached as a
        /// ScriptableObject would lose its extensions across a domain reload -- reintroducing the
        /// exact data loss the bag prevents, one layer down. <see cref="OnBeforeUnitySerialize"/>
        /// and <see cref="OnAfterUnityDeserialize"/> keep the two in step.
        /// </remarks>
        [SerializeField] private List<OWExtensionField> _extensionsForUnity = new List<OWExtensionField>();

        public IReadOnlyList<OWExtensionField> Extensions => _extensionsForUnity;

        public void OnBeforeUnitySerialize()
        {
            _extensionsForUnity.Clear();
            if (_extensions == null) return;

            foreach (var pair in _extensions)
            {
                _extensionsForUnity.Add(new OWExtensionField(pair.Key, pair.Value?.ToString(Formatting.None)));
            }
        }

        public void OnAfterUnityDeserialize()
        {
            _extensions = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
            if (_extensionsForUnity == null) return;

            foreach (var field in _extensionsForUnity)
            {
                if (string.IsNullOrEmpty(field.Key)) continue;
                _extensions[field.Key] = Newtonsoft.Json.Linq.JToken.Parse(field.RawJson ?? "null");
            }
        }
    }

    /// <summary>One unmodeled field, kept as its raw JSON so nothing is lost in translation.</summary>
    [Serializable]
    public struct OWExtensionField
    {
        [SerializeField] private string _key;
        [SerializeField] private string _rawJson;

        public string Key => _key;
        public string RawJson => _rawJson;

        public OWExtensionField(string key, string rawJson)
        {
            _key = key;
            _rawJson = rawJson;
        }
    }
}
