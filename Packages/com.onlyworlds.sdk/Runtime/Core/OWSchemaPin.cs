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
        public const string CanonicalVersion = "00.30.00";

        /// <summary>Distribution serial within that canonical version.</summary>
        public const int DistSerial = 9;

        /// <summary>Tag form of the pin. Mutable -- never trust it alone.</summary>
        public const string Tag = "v0.30.0-dist.9";

        /// <summary>
        /// sha256 of the pinned MANIFEST.json itself. The immutable half of the pin.
        /// </summary>
        /// <remarks>
        /// Re-pinned 6 -> 9 on 2026-07-29. Serials 7, 8 and 9 changed only <c>rulings.yaml</c> and
        /// this manifest -- no schema file, no walk change, and <c>presentation.json</c> is
        /// byte-identical across all four. That is the case this two-level pin exists to express:
        /// the vendored FILE was never stale, only the distribution around it moved, and a pin that
        /// could not tell those apart would have cried wolf three times in one morning.
        /// </remarks>
        public const string ManifestSha256 =
            "c9ef41514641c40021bf4282e57612a25bc0d84ac616784a8d2b2a0c1ccfd050";

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
        /// <b><c>maximum:</c> IS ADVISORY -- never generate range validation from it.</b>
        /// (<c>maximum-is-advisory-and-zero-means-unbounded</c>, dist.8.) 41 integer fields carry a
        /// <c>maximum:</c> and they are two populations under one key: 15 are real 0-100 attribute
        /// scales, and <b>26 use <c>maximum: 0</c> as a sentinel meaning "no maximum"</b> -- every
        /// date, weight, height, count, elevation, duration and <c>life_span</c>. An emitter that
        /// honors the key naively writes <c>[Range(0, 0)]</c> onto every date and weight in the
        /// standard, silently rejecting real data. Checked twice: keel has zero
        /// <c>MaxValueValidator</c>, and <c>charisma: 9999</c> against a <c>maximum: 100</c> field
        /// POSTed 201 and stored 9999.
        /// </para>
        /// <para>
        /// <c>schema_walk.py</c> dropping <c>maximum</c> is <b>load-bearing, not a gap</b>. Do not
        /// "fix" it.
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
