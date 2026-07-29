using System;
using Newtonsoft.Json;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A graphical marker on a map -- a point in a line or polygon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN PROVING MODEL -- expected to be replaced wholesale by the emitter.
    /// </para>
    /// <para>
    /// Chosen as a proving model because it is the schema's <c>required:</c> case, and because
    /// <c>order</c> carries a CONVENTION the model cannot express on its own (see
    /// <see cref="Order"/>).
    /// </para>
    /// </remarks>
    // OptIn must be repeated on every subclass -- Newtonsoft does not inherit [JsonObject],
    // so a model without it silently serializes every public property alongside the
    // attributed fields, duplicating each one on the wire. The emitter must emit this line.
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    public class OWMarker : OWElement
    {
        [JsonProperty("map")]
        [SerializeField] private string _map;

        [JsonProperty("zone")]
        [SerializeField] private string _zone;

        [JsonProperty("x")]
        [SerializeField] private SerializableNullable<int> _x;

        [JsonProperty("y")]
        [SerializeField] private SerializableNullable<int> _y;

        [JsonProperty("z")]
        [SerializeField] private SerializableNullable<int> _z;

        [JsonProperty("order")]
        [SerializeField] private SerializableNullable<int> _order;

        public string Map { get => _map; set => _map = value; }
        public string Zone { get => _zone; set => _zone = value; }

        public SerializableNullable<int> X { get => _x; set => _x = value; }
        public SerializableNullable<int> Y { get => _y; set => _y = value; }
        public SerializableNullable<int> Z { get => _z; set => _z = value; }

        /// <summary>
        /// Sequence position when markers define a polygon or line (0 = first point).
        /// </summary>
        /// <remarks>
        /// RULED 2026-07-28: <c>order</c> is OPTIONAL. It exists for INSERTING between existing
        /// markers, not for declaring a total ordering that must always be present. A viewer that
        /// assumes order is populated will scramble any polygon authored without it.
        /// <para>
        /// Absent <c>order</c>, sorting by <c>created_at</c> is <b>ADVISORY only</b> (demoted
        /// 2026-07-28) -- and with both absent, marker order is UNDEFINED. See
        /// <see cref="OWMarkerOrdering"/> for why it cannot be normative.
        /// </para>
        /// </remarks>
        public SerializableNullable<int> Order { get => _order; set => _order = value; }
    }

    /// <summary>
    /// The marker sort convention, in one place so no consumer re-derives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a RULING the schema cannot carry -- exactly the class of knowledge that belongs in
    /// the shared walk's ruling table rather than being rediscovered per client. Published as
    /// <c>nullable-by-default</c> in <c>walk/rulings.yaml</c>; encoded here so the viewer and any
    /// sim consumer agree by construction.
    /// </para>
    /// <para>
    /// ⚑ <b>The <c>created_at</c> fallback is ADVISORY, not a consumer MUST</b> -- demoted
    /// 2026-07-28 after Atlas reported sorting in file-scan order. It cannot be normative because
    /// <c>created_at</c> is itself optional on element bodies and a writer must not synthesize one,
    /// so a legal hand-authored folder can hold markers with neither key. With both absent, marker
    /// order is <b>UNDEFINED</b> and any stable order conforms; this implementation keeps input
    /// order, which is as good a stable choice as any.
    /// </para>
    /// <para>
    /// <b>Sorting harder does not recover a vertex sequence the file never recorded.</b> A
    /// <c>created_at</c> sort is equally capable of drawing a plausible-looking wrong polygon. The
    /// real fix is upstream: a writer emitting a zone SHOULD write <c>order</c>.
    /// </para>
    /// </remarks>
    public static class OWMarkerOrdering
    {
        /// <summary>
        /// Orders markers for rendering: explicit <c>order</c> first, then creation time where
        /// present, then a stable no-op. See the remarks -- the second and third tiers are
        /// advisory, and a caller that needs a guaranteed polygon needs <c>order</c> on the wire.
        /// </summary>
        public static int Compare(OWMarker a, OWMarker b)
        {
            if (a == null || b == null) return 0;

            var aHas = a.Order.HasValue;
            var bHas = b.Order.HasValue;

            // Both ordered: by order. This is the insertion case order exists to serve.
            if (aHas && bHas) return a.Order.Value.CompareTo(b.Order.Value);

            // One ordered: it was deliberately placed, so it leads.
            if (aHas) return -1;
            if (bHas) return 1;

            // Neither ordered: creation time is the ruled fallback.
            return string.CompareOrdinal(a.CreatedAt ?? string.Empty, b.CreatedAt ?? string.Empty);
        }
    }
}
