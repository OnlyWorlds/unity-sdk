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
        public const int DistSerial = 15;

        /// <summary>Tag form of the pin. Mutable -- never trust it alone.</summary>
        public const string Tag = "v0.30.1-dist.15";

        /// <summary>
        /// sha256 of the pinned MANIFEST.json itself. The immutable half of the pin.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Re-pinned 6 -> 11 across one morning (2026-07-29). Serials 7-9 changed only
        /// <c>rulings.yaml</c>; 11 is the first canonical bump (<c>00.30.00</c> -> <c>00.30.01</c>).
        /// <c>presentation.json</c> was <b>byte-identical across every one of them</b>. That is
        /// exactly what this two-level pin exists to express: the vendored FILE was never stale,
        /// only the distribution around it moved. A pin that could not tell those apart would have
        /// cried wolf four times before lunch; that re-pin stayed green and only the manifest hash
        /// needed updating.
        /// </para>
        /// <para>
        /// <b>11 -> 15 (2026-09-18) is the first re-pin where the sidecar DID move</b>, so the
        /// other half of the pin earned its keep for the first time: two icon slugs changed
        /// (<c>institution</c> <c>business</c> -> <c>account_balance</c>, <c>marker</c>
        /// <c>place</c> -> <c>location_on</c>, both onto the Material Symbols SVG set), and
        /// <see cref="PresentationSha256"/> moved with them. The schema itself gained one
        /// <c>minimum: 0</c> (<c>ability.potency</c>), six description corrections and two YAML
        /// de-indents that lifted a <c>description</c> out of an <c>items:</c> block, where the
        /// walk could not see it. <c>schema_walk.py</c> is byte-identical 11 -> 15: the decoder
        /// did not move, so nothing was re-interpreted.
        /// </para>
        /// </remarks>
        public const string ManifestSha256 =
            "9472b3d4a40546df68df0e7fc42a762a7e775d269f508066df140932c503f8e3";

        /// <summary>
        /// sha256 of <c>presentation.json</c> as the pinned MANIFEST lists it.
        /// </summary>
        /// <remarks>
        /// The vendored copy at <c>Runtime/Resources/ow-presentation.json</c> must hash to exactly
        /// this. Verified by <c>OWSchemaPinTests</c> on every run, so the sidecar cannot rot
        /// silently the way an unguarded vendored copy always eventually does.
        /// </remarks>
        public const string PresentationSha256 =
            "648d9f01b53ea0ceaeb6e04dd17a9fc3ae4d706e8925cb0a89c685d86281762a";

        /// <summary>When these constants were last verified against the published dist.</summary>
        public const string PinnedOn = "2026-09-18";

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
        /// <b>The walk surfaces no bounds, and as of dist.15 that is PERMANENT, not pending.</b>
        /// An earlier draft of the ruling row left the door open ("only then may the walk carry
        /// it"); serial 15 closes it on Kael's argument, which reversed his own accepted proposal.
        /// The wire does not enforce -- a charisma of 9999 stores against a maximum of 100 -- so a
        /// walk carrying <c>maximum</c> would ship a constraint the platform does not keep, and
        /// any consumer reading a bound is tempted to enforce it. If a docs consumer ever needs
        /// the numbers they belong in the presentation tier as a display hint, never in the
        /// decoder. <c>schema_walk.py</c>'s silence here is <b>load-bearing, not a gap</b>, and
        /// this SDK must never read <c>minimum:</c>/<c>maximum:</c> out of the YAML to fill it.
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
