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
    /// <remarks>
    /// <c>[JsonObject(OptIn)]</c> is load-bearing, not tidiness. Newtonsoft's default is OptOut,
    /// which serializes every public member -- so each field would go to the wire TWICE: once as
    /// the attributed <c>name</c> and again as the property <c>Name</c>. The server ignores the
    /// PascalCase copies, which is exactly why it went unnoticed: writes looked like they worked
    /// while sending a duplicate of every field. Found 2026-07-29 by diffing serialized output for
    /// the typed write path; no round-trip test could see it, because they all assert on the keys
    /// that SHOULD be present and none on the keys that should not.
    /// <para>
    /// The consequence with OptIn: <b>a member reaches the wire only if it carries
    /// <c>[JsonProperty]</c></b>. A generated model that forgets one silently drops that field.
    /// </para>
    /// </remarks>
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    public class OWElement : ISerializationCallbackReceiver
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
        /// and <see cref="OnAfterUnityDeserialize"/> keep the two in step, and Unity calls them
        /// through <see cref="ISerializationCallbackReceiver"/>.
        /// </remarks>
        /// <seealso cref="OnBeforeSerialize"/>
        [SerializeField] private List<OWExtensionField> _extensionsForUnity = new List<OWExtensionField>();

        /// <summary>
        /// Every unmodeled field on this element, as key + raw JSON.
        /// </summary>
        /// <remarks>
        /// Projected from the live bag rather than the serialized mirror. Reading the mirror
        /// directly would return whatever the last serialization pass left there -- empty on an
        /// element that has only ever been deserialized from JSON, which is the common case and
        /// which made this property silently lie before 2026-07-29.
        /// </remarks>
        public IReadOnlyList<OWExtensionField> Extensions
        {
            get
            {
                var projected = new List<OWExtensionField>(_extensions?.Count ?? 0);
                if (_extensions == null) return projected;

                foreach (var pair in _extensions)
                {
                    projected.Add(new OWExtensionField(pair.Key, pair.Value?.ToString(Formatting.None)));
                }

                return projected;
            }
        }

        /// <summary>
        /// Unity's serialization hook. Flattens the bag into the serializable mirror.
        /// </summary>
        /// <remarks>
        /// Unity calls this on the main thread immediately before writing the object. Subclasses
        /// that need their own hook must override <see cref="OnBeforeUnitySerialize"/> rather than
        /// re-implementing the interface, or the bag stops being carried.
        /// </remarks>
        void ISerializationCallbackReceiver.OnBeforeSerialize() => OnBeforeUnitySerialize();

        /// <summary>Unity's deserialization hook. Rebuilds the bag from the mirror.</summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize() => OnAfterUnityDeserialize();

        protected virtual void OnBeforeUnitySerialize()
        {
            _extensionsForUnity.Clear();
            if (_extensions == null) return;

            foreach (var pair in _extensions)
            {
                _extensionsForUnity.Add(new OWExtensionField(pair.Key, pair.Value?.ToString(Formatting.None)));
            }
        }

        protected virtual void OnAfterUnityDeserialize()
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
