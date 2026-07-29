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
        public const int DistSerial = 6;

        /// <summary>Tag form of the pin. Mutable -- never trust it alone.</summary>
        public const string Tag = "v0.30.0-dist.6";

        /// <summary>
        /// sha256 of the pinned MANIFEST.json itself. The immutable half of the pin.
        /// </summary>
        public const string ManifestSha256 =
            "574c1a5440257c945601e81fdeaf0120e16fcf7cfa3af4dda798747678ad1dda";

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
    }
}
