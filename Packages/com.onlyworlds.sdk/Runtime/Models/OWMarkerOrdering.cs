// Hand-written. NOT generated, and deliberately not in Generated/.
//
// This is a RULING, not a model: nothing in the schema YAML expresses it, so the emitter
// cannot produce it and a regeneration must not be able to erase it. It lived inside the
// hand-written OWMarker.cs until the emitter landed on 2026-09-04; the model half of that
// file is now generated, and this half moved here rather than being lost with it.

namespace OnlyWorlds.Sdk
{
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
