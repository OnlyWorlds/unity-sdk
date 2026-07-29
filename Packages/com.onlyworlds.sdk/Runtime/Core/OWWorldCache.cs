using System;
using System.Collections.Generic;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// One world, cached as a Unity asset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Unity-native half of the SDK: a world that lives in the project, survives domain
    /// reloads, works offline, and is inspectable. Games ship against this rather than against the
    /// network.
    /// </para>
    /// <para>
    /// Identity is <see cref="OWWorldKey"/> -- (source, worldId) -- never the world id alone. See
    /// that type for why: a world-id-keyed registry silently opened the wrong snapshot in a sibling
    /// tool, with no error and a correct-looking UI.
    /// </para>
    /// <para>
    /// Elements are stored as raw JSON rather than as typed objects. Three reasons: Unity cannot
    /// serialize a polymorphic <c>List&lt;OWElement&gt;</c> without <c>[SerializeReference]</c> and
    /// its overhead; extension fields survive by construction because nothing is ever re-emitted
    /// through a model that might not know a field; and the cache stays valid across a regeneration
    /// of the models, which matters when the models are generated and the schema moves.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "OWWorldCache", menuName = "OnlyWorlds/World Cache")]
    public class OWWorldCache : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private OWWorldKey _key;
        [SerializeField] private string _worldName;

        /// <summary>The change_seq this cache was last synced to.</summary>
        [SerializeField] private long _cursor;

        [SerializeField] private string _syncedAt;

        /// <summary>
        /// Advisory: this world must not be written back to.
        /// </summary>
        /// <remarks>
        /// Set from a folder snapshot's <c>writable: false</c>. Consumers MUST honour it (spec
        /// v0.3). A snapshot that cannot say what it is will eventually be written to by a tool
        /// that did not know.
        /// </remarks>
        [SerializeField] private bool _readOnly;

        [SerializeField] private List<OWCachedElement> _elements = new List<OWCachedElement>();

        /// <summary>id -> index into <see cref="_elements"/>. Rebuilt on deserialize.</summary>
        private Dictionary<string, int> _byId;

        /// <summary>type slug -> indices. Rebuilt on deserialize.</summary>
        private Dictionary<string, List<int>> _byType;

        public OWWorldKey Key => _key;
        public string WorldName => _worldName;
        public long Cursor => _cursor;
        public string SyncedAt => _syncedAt;
        public bool ReadOnly => _readOnly;
        public int Count => _elements.Count;

        public void Initialize(OWWorldKey key, string worldName, bool readOnly = false)
        {
            _key = key;
            _worldName = worldName;
            _readOnly = readOnly;
        }

        public void SetCursor(long cursor, string syncedAtIso)
        {
            _cursor = cursor;
            _syncedAt = syncedAtIso;
        }

        // -- Queries ----------------------------------------------------------

        /// <summary>
        /// Looks up one element by id. Dictionary lookup, never a linear scan.
        /// </summary>
        /// <remarks>
        /// Link navigation is id resolution, and a world of 1,669 elements resolved by scanning
        /// would be O(n) per hop. The index is what makes the cache a resolver rather than a list.
        /// </remarks>
        public T Get<T>(string id) where T : OWElement
        {
            EnsureIndex();
            if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out var i)) return null;
            return Deserialize<T>(_elements[i]);
        }

        /// <summary>True when the id is present, without paying for deserialization.</summary>
        public bool Contains(string id)
        {
            EnsureIndex();
            return !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);
        }

        /// <summary>Every element of a type.</summary>
        public List<T> All<T>(string typeSlug) where T : OWElement
        {
            EnsureIndex();
            var results = new List<T>();
            if (!_byType.TryGetValue(typeSlug, out var indices)) return results;

            foreach (var i in indices)
            {
                var element = Deserialize<T>(_elements[i]);
                if (element != null) results.Add(element);
            }

            return results;
        }

        /// <summary>
        /// Raw JSON bodies for a type, without deserializing into models.
        /// </summary>
        /// <remarks>
        /// For consumers that want the wire shape rather than a typed model -- the browser's detail
        /// panel, which renders whatever fields exist including ones no model knows about. Typed
        /// access would silently hide exactly the extension fields worth seeing.
        /// </remarks>
        public List<string> AllRaw(string typeSlug)
        {
            EnsureIndex();
            var results = new List<string>();
            if (!_byType.TryGetValue(typeSlug, out var indices)) return results;

            foreach (var i in indices)
            {
                var raw = _elements[i].RawJson;
                if (!string.IsNullOrEmpty(raw)) results.Add(raw);
            }

            return results;
        }

        /// <summary>
        /// Resolves a link array to elements, skipping ids the cache does not hold.
        /// </summary>
        /// <remarks>
        /// A dangling link is normal, not exceptional: a partial sync or a sparse fieldset leaves
        /// real ids unresolvable. Callers that need to know use <see cref="Contains"/>.
        /// </remarks>
        public List<T> Resolve<T>(IEnumerable<string> ids) where T : OWElement
        {
            var results = new List<T>();
            if (ids == null) return results;

            foreach (var id in ids)
            {
                var element = Get<T>(id);
                if (element != null) results.Add(element);
            }

            return results;
        }

        // -- Mutation ---------------------------------------------------------

        /// <summary>Adds or replaces an element from its raw JSON.</summary>
        public void Upsert(string id, string typeSlug, string rawJson)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("An element needs an id.", nameof(id));

            EnsureIndex();

            if (_byId.TryGetValue(id, out var existing))
            {
                var previousType = _elements[existing].TypeSlug;
                _elements[existing] = new OWCachedElement(id, typeSlug, rawJson);

                // A type change has to move the element between type buckets, or the index keeps
                // listing it under the old type forever: All(newType) misses it, All(oldType)
                // returns it, and deserializing it as the old model then fails or yields the wrong
                // object. The SDK's own callers never change a type (it is fixed on the wire), but
                // Upsert is public -- and a folder reader hits this on the first file whose body
                // type contradicts its directory, which is an explicit case in the conformance
                // fixture. Dropping the index is cheaper than patching it and cannot go stale.
                if (!string.Equals(previousType, typeSlug, StringComparison.Ordinal))
                {
                    _byId = null;
                    _byType = null;
                }

                return;
            }

            _elements.Add(new OWCachedElement(id, typeSlug, rawJson));
            var index = _elements.Count - 1;
            _byId[id] = index;

            if (!_byType.TryGetValue(typeSlug, out var list))
            {
                list = new List<int>();
                _byType[typeSlug] = list;
            }

            list.Add(index);
        }

        public bool Remove(string id)
        {
            EnsureIndex();
            if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out var i)) return false;

            _elements.RemoveAt(i);

            // Indices after the removed slot all shift, so the index is rebuilt rather than
            // patched. Deletes are rare; a subtly wrong index would not be.
            _byId = null;
            _byType = null;
            return true;
        }

        public void Clear()
        {
            _elements.Clear();
            _byId = null;
            _byType = null;
            _cursor = 0;
        }

        // -- Index ------------------------------------------------------------

        private void EnsureIndex()
        {
            if (_byId != null && _byType != null) return;

            _byId = new Dictionary<string, int>(_elements.Count);
            _byType = new Dictionary<string, List<int>>();

            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];
                if (string.IsNullOrEmpty(element.Id)) continue;

                _byId[element.Id] = i;

                var slug = element.TypeSlug ?? string.Empty;
                if (!_byType.TryGetValue(slug, out var list))
                {
                    list = new List<int>();
                    _byType[slug] = list;
                }

                list.Add(i);
            }
        }

        private static T Deserialize<T>(OWCachedElement cached) where T : OWElement
        {
            if (string.IsNullOrEmpty(cached.RawJson)) return null;

            try
            {
                return OWJson.Deserialize<T>(cached.RawJson);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OnlyWorlds] Could not deserialize cached element {cached.Id}: {e.Message}");
                return null;
            }
        }

        public void OnBeforeSerialize() { }

        /// <summary>Drops the index so it rebuilds against whatever was just loaded.</summary>
        public void OnAfterDeserialize()
        {
            _byId = null;
            _byType = null;
        }
    }

    /// <summary>One element as stored: identity, type, and the untouched wire body.</summary>
    [Serializable]
    public struct OWCachedElement
    {
        [SerializeField] private string _id;
        [SerializeField] private string _typeSlug;
        [SerializeField] private string _rawJson;

        public string Id => _id;
        public string TypeSlug => _typeSlug;
        public string RawJson => _rawJson;

        public OWCachedElement(string id, string typeSlug, string rawJson)
        {
            _id = id;
            _typeSlug = typeSlug;
            _rawJson = rawJson;
        }
    }
}
