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
    /// Only <c>name</c> is required (Titus's ruling, 2026-07-28). Everything else is optional and
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

        // Scalar strings start at "", never null (rulings.yaml string-empty-is-unset): "" IS the
        // wire's unset, keel has no null to store, and Unity's serializer turns null into "" after
        // a reload anyway -- so a fresh model and a reloaded one now say the same thing.

        [JsonProperty("name")]
        [SerializeField] private string _name = "";

        [JsonProperty("description")]
        [SerializeField] private string _description = "";

        [JsonProperty("supertype")]
        [SerializeField] private string _supertype = "";

        [JsonProperty("subtype")]
        [SerializeField] private string _subtype = "";

        [JsonProperty("image_url")]
        [SerializeField] private string _imageUrl = "";

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

        // Since 2026-09-28: the membership that created the element on a shared world, or null
        // (the owner, and all history before membership). Server-managed and not in the standard.
        [JsonProperty("created_by")]
        [SerializeField] private string _createdBy;

        // The membership whose write last changed the element (null for
        // the owner) and the door that write came through. Null for anything last changed before
        // writes recorded them. Server-managed: keel drops both from a write body.
        [JsonProperty("changed_by")]
        [SerializeField] private string _changedBy;

        [JsonProperty("changed_via")]
        [SerializeField] private string _changedVia;

        // From schema-dist serial 46: the name the client gave in its X-OW-Client header on the last
        // write. Server-managed: keel ignores it in a write body.
        [JsonProperty("changed_client")]
        [SerializeField] private string _changedClient;

        public string World => _world;
        public string Type => _type;
        public string CreatedAt => _createdAt;
        public string UpdatedAt => _updatedAt;
        public long ChangeSeq => _changeSeq;
        public string CreatedBy => _createdBy;
        public string ChangedBy => _changedBy;

        /// <summary>
        /// The door of the last write: one of <see cref="OWWriteDoor"/>'s values, or null.
        /// <see cref="OWWriteDoor.IsAgent"/> says whether an agent made it.
        /// </summary>
        public string ChangedVia => _changedVia;

        /// <summary>
        /// The name the client gave on the last write (lower case letters, digits and hyphens, such as <c>atlas</c>),
        /// or null: none was sent or recorded. The client's claim about itself, never proof; whether an agent made
        /// the write is <see cref="ChangedVia"/>'s.
        /// </summary>
        public string ChangedClient => _changedClient;

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
        /// True for a stub the reader only knows of (<c>known_of: true</c>, folder format §4.4): its unset
        /// fields are unknown, not empty, and it is never written back (<see cref="OWPayload.IsKnownOf"/>).
        /// </summary>
        /// <remarks>
        /// Read from the extensions bag, deliberately not a <c>[JsonProperty("known_of")] bool</c>: a modelled
        /// bool would serialize <c>known_of: false</c> into every write of a whole element, and keel refuses
        /// <c>known_of</c> at any value (422 <c>known_of_not_writable</c>). In the bag it goes back out only
        /// when it came in, where the client strips it or, when <c>true</c>, refuses the write.
        /// </remarks>
        public bool IsKnownOf
            => _extensions != null
               && _extensions.TryGetValue(OWPayload.KnownOf, out var marker)
               && marker != null && marker.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)marker;

        /// <summary>
        /// True for a hidden element (<c>hidden</c> anything but absent, <c>null</c> and <c>false</c>, folder format §4.8):
        /// shown to the world's holder with the word hidden on it, left out by a reader that hands the world on
        /// (<see cref="OWPayload.IsHidden"/>). Read from the extensions bag, so the key goes back out only as it came in.
        /// </summary>
        public bool IsHidden
            => _extensions != null
               && _extensions.TryGetValue(OWPayload.Hidden, out var mark)
               && OWPayload.IsHiddenMark(mark);

        /// <summary>
        /// True for a proposed element (<c>x_review_proposed</c> an object): not canon until a keeper settles
        /// it. The queue's and a viewer's rule (<see cref="OWPayload.IsProposed"/>); the mark stays in the extensions bag.
        /// </summary>
        public bool IsProposed
            => _extensions != null
               && _extensions.TryGetValue(OWPayload.ReviewProposed, out var mark)
               && OWPayload.IsAMark(mark);

        /// <summary>
        /// True for a proposed element or one whose mark is in doubt (<c>x_review_proposed</c> there and not null): the
        /// rule of a reader that hands a world on (<see cref="OWPayload.IsProposedOrInDoubt"/>).
        /// </summary>
        public bool IsProposedOrInDoubt
            => _extensions != null
               && _extensions.TryGetValue(OWPayload.ReviewProposed, out var mark)
               && OWPayload.IsMarkOrDoubt(mark);

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
