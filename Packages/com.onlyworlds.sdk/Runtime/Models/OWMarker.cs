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
    [Serializable]
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
        /// RULED 2026-07-28: <c>order</c> is OPTIONAL, and absent order means "sort by
        /// <c>created_at</c>". It exists for INSERTING between existing markers, not for declaring
        /// a total ordering that must always be present. A viewer that assumes order is populated
        /// will scramble any polygon authored without it. See <see cref="OWMarkerOrdering"/>.
        /// </remarks>
        public SerializableNullable<int> Order { get => _order; set => _order = value; }
    }

    /// <summary>
    /// The marker sort convention, in one place so no consumer re-derives it.
    /// </summary>
    /// <remarks>
    /// This is a RULING the schema cannot carry -- exactly the class of knowledge that belongs in
    /// the shared walk's ruling table rather than being rediscovered per client. Encoded here so
    /// the viewer and any sim consumer agree by construction.
    /// </remarks>
    public static class OWMarkerOrdering
    {
        /// <summary>
        /// Orders markers for rendering: explicit <c>order</c> first, then creation time.
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
