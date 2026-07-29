namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// What published schema distribution this package's vendored files came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pin is <b>(tag, manifest hash)</b>, never the tag alone. A tag is mutable: moving one
    /// regenerates a self-consistent MANIFEST, so the manifest cannot detect its own tag having
    /// moved. Recording the manifest's own sha256 here -- outside the artifact it describes -- is
    /// what makes the pin falsifiable. This is the <c>go.sum</c> pattern, and the CVE class it
    /// exists to prevent has had tens of thousands of victims.
    /// </para>
    /// <para>
    /// Verify the fetched MANIFEST against <see cref="ManifestSha256"/> BEFORE trusting anything
    /// listed inside it, then verify each vendored file against its entry. Trust flows in one
    /// direction: pin -> manifest -> file. A file that matches its manifest entry proves nothing
    /// if the manifest itself was never checked.
    /// </para>
    /// <para>
    /// Re-vendoring is a deliberate act: replace the file, update these constants, and let the
    /// tests tell you whether the two agree. If a test fails after a re-vendor, the answer is
    /// never to edit the expected hash until it passes.
    /// </para>
    /// </remarks>
    public static class OWSchemaPin
    {
        /// <summary>The published distribution these files came from.</summary>
        public const string Repository = "https://github.com/OnlyWorlds/schema-dist";

        /// <summary>Canonical schema version, as the dist's own VERSION file reports it.</summary>
        public const string CanonicalVersion = "00.30.01";

        /// <summary>Distribution serial within that canonical version.</summary>
        public const int DistSerial = 11;

        /// <summary>Tag form of the pin. Mutable -- never trust it alone.</summary>
        public const string Tag = "v0.30.1-dist.11";

        /// <summary>
        /// sha256 of the pinned MANIFEST.json itself. The immutable half of the pin.
        /// </summary>
        /// <remarks>
        /// Re-pinned 6 -> 11 across one morning (2026-07-29). Serials 7-9 changed only
        /// <c>rulings.yaml</c>; 11 is the first canonical bump (<c>00.30.00</c> -> <c>00.30.01</c>).
        /// <c>presentation.json</c> is <b>byte-identical across every one of them</b>. That is
        /// exactly what this two-level pin exists to express: the vendored FILE was never stale,
        /// only the distribution around it moved. A pin that could not tell those apart would have
        /// cried wolf four times before lunch; this one stayed green and only the manifest hash
        /// needed updating.
        /// </remarks>
        public const string ManifestSha256 =
            "11ab23d54cb158a4be51fdcf624dc6bf9040afde112649299e86c5498fb0f494";

        /// <summary>
        /// sha256 of <c>presentation.json</c> as the pinned MANIFEST lists it.
        /// </summary>
        /// <remarks>
        /// The vendored copy at <c>Runtime/Resources/ow-presentation.json</c> must hash to exactly
        /// this. Verified by <c>OWSchemaPinTests</c> on every run, so the sidecar cannot rot
        /// silently the way an unguarded vendored copy always eventually does.
        /// </remarks>
        public const string PresentationSha256 =
            "7d6aefb5442393d7a9da0e136a5538d55a0c3dcda2b138d83b975d7643d5e15c";

        /// <summary>When these constants were last verified against the published dist.</summary>
        public const string PinnedOn = "2026-07-29";

        /// <summary>
        /// ⚑ Rulings that bind THIS SDK and any emitter generating into it. Read the row, not the key.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Bounds: the <c>maximum: 0</c> trap is GONE from the standard as of canonical
        /// <c>00.30.01</c> / dist.11</b> -- the 26 sentinel fields no longer carry the key at all,
        /// and absence means unbounded, which is what it always meant. (It briefly existed as a
        /// live hazard: an emitter honoring the key naively would have written
        /// <c>[Range(0, 0)]</c> onto every date, weight, height, count and duration in the
        /// standard. Ruled advisory in dist.8, removed at source in dist.11.)
        /// </para>
        /// <para>
        /// <b>What replaces it, and it is subtler.</b> When bounds do surface, the same six names
        /// appear on two types as <i>different quantities</i>: <c>character.charisma</c> is an
        /// unsigned SCORE (0-100), <c>trait.charisma</c> is a signed MODIFIER (-100 to 100),
        /// because a trait is a modifier with an <c>anti_trait</c> sibling and negative is the
        /// point. <b>Never share validation between them.</b>
        /// </para>
        /// <para>
        /// The walk surfaces no bounds today regardless -- that waits on an opt-in
        /// <c>include_constraints</c> flag, and the ordering (canonical, then walk, then emitters)
        /// is deliberate. <c>schema_walk.py</c>'s silence here is <b>load-bearing, not a gap</b>.
        /// </para>
        /// <para>
        /// <b><c>change_seq</c> is not unique</b> -- a bulk import stamps every element it creates
        /// with one seq. Never diff, dedupe or key on it. (<see cref="OWSync"/> uses it only as a
        /// watermark maximum, which is safe.)
        /// </para>
        /// <para>
        /// <b><c>""</c> IS the wire's unset for strings</b> -- keel stores them <c>blank=True</c>,
        /// never nullable, so there is no third state. Never round-trip a distinction the wire
        /// cannot carry.
        /// </para>
        /// <para>
        /// <b><c>world</c> is rejected in POST/PATCH bodies</b> -- the API key header determines the
        /// world. Already handled: it is one of the five stripped fields.
        /// </para>
        /// </remarks>
        public const string RulingsPath = "walk/rulings.yaml";
    }
}
