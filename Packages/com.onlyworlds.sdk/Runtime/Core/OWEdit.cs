using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A typed edit session over one element: read it, change it, send only what changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PATCH is destructive on the fields it receives</b>, so "send only what changed" is not an
    /// optimisation -- it is the difference between updating a field and silently overwriting every
    /// other field with whatever your local copy happened to hold. An object fetched an hour ago
    /// and PATCHed wholesale will happily undo an hour of somebody else's work.
    /// </para>
    /// <para>
    /// <b>Why a baseline diff rather than per-field dirty flags.</b> The 22 models are generated
    /// from the schema. A design needing a <c>_dirtyHeight</c> beside every <c>_height</c> would
    /// push tracking into the emitter and multiply it by 376 fields, and every hand-written model
    /// would be one forgotten flag away from silent data loss. Diffing a before-snapshot against
    /// the current state needs nothing from the model at all: it works for types that do not exist
    /// yet, which is the whole point.
    /// </para>
    /// <para>
    /// The diff runs on the SERIALIZED form, so it inherits the SDK's semantics for free -- unset
    /// stays distinct from zero, extension fields ride along untouched, and a field cleared to
    /// <c>null</c> is a real change that sends explicit null rather than being mistaken for
    /// "absent, don't send".
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var edit = OWEdit.Begin(character);
    /// character.Name = "Quillon";
    /// character.Level = 3;
    /// await edit.CommitAsync(client, "character");   // PATCHes name and level. Nothing else.
    /// </code>
    /// </example>
    public class OWEdit<T> where T : OWElement
    {
        private readonly T _element;
        private readonly JObject _baseline;

        internal OWEdit(T element)
        {
            _element = element ?? throw new ArgumentNullException(nameof(element));
            _baseline = Snapshot(element);
        }

        /// <summary>The element being edited.</summary>
        public T Element => _element;

        /// <summary>Its id, as captured when the session began.</summary>
        public string Id => _baseline["id"]?.ToString();

        private static JObject Snapshot(T element)
            => JObject.Parse(OWJson.Serialize(element));

        /// <summary>
        /// The fields that changed since <see cref="OWEdit.Begin{T}"/>, as a PATCH body.
        /// </summary>
        /// <remarks>
        /// Empty when nothing changed -- and an empty PATCH is a request worth not sending at all.
        /// Server-owned fields are stripped by the client on the way out regardless; they are
        /// excluded here too so the diff reports what a caller can actually act on.
        /// </remarks>
        public JObject BuildPatch()
        {
            var current = Snapshot(_element);
            var patch = new JObject();

            foreach (var property in current.Properties())
            {
                if (OWPayload.IsReadOnly(property.Name)) continue;

                var before = _baseline[property.Name];

                // JToken.DeepEquals treats an explicit null and an absent key as different, which
                // is exactly the distinction this SDK exists to preserve: clearing a field is a
                // change and must be sent as null, not dropped as "nothing to say".
                if (before != null && JToken.DeepEquals(before, property.Value)) continue;

                patch[property.Name] = property.Value;
            }

            // A key present in the baseline but gone from the current form has been removed from
            // the model itself. That is a model-shape change rather than a value edit, and
            // inventing a null for it would send a clear the caller never asked for.
            return patch;
        }

        /// <summary>True when at least one writable field differs from the baseline.</summary>
        public bool HasChanges => BuildPatch().Count > 0;

        /// <summary>
        /// PATCHes only the changed fields, then re-baselines so the session can continue.
        /// </summary>
        /// <returns>The server's updated element, or the unchanged element if nothing differed.</returns>
        /// <remarks>
        /// Sends nothing when there is nothing to send. A no-op PATCH still bumps
        /// <c>updated_at</c> server-side, which turns an accidental save into a false edit in
        /// everyone else's change feed.
        /// </remarks>
        public async Task<T> CommitAsync(OWClient client, string type, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrEmpty(type)) throw new ArgumentException("A type is required.", nameof(type));

            var id = Id;
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException(
                    "This element has no id, so there is nothing to PATCH. Create it first.");
            }

            var patch = BuildPatch();
            if (patch.Count == 0) return _element;

            var updated = await client.PatchAsync<T>(type, id, patch, ct).ConfigureAwait(false);

            Rebase();
            return updated;
        }

        /// <summary>
        /// Treats the current state as the new baseline, discarding pending changes.
        /// </summary>
        public void Rebase()
        {
            var current = Snapshot(_element);
            _baseline.RemoveAll();
            foreach (var property in current.Properties()) _baseline[property.Name] = property.Value;
        }

        /// <summary>Names of the fields that would be sent. Useful for logging and confirmations.</summary>
        public IEnumerable<string> ChangedFields()
        {
            foreach (var property in BuildPatch().Properties()) yield return property.Name;
        }
    }

    /// <summary>Entry point for typed edits.</summary>
    public static class OWEdit
    {
        /// <summary>
        /// Starts tracking changes to an element. Take this immediately after reading it.
        /// </summary>
        /// <remarks>
        /// The baseline is captured at this moment, so anything mutated BEFORE the call is
        /// invisible to the diff and will not be sent.
        /// </remarks>
        public static OWEdit<T> Begin<T>(T element) where T : OWElement => new OWEdit<T>(element);
    }
}
