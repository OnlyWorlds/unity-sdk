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
        public const string CanonicalVersion = "00.33.00";

        /// <summary>Distribution serial within that canonical version.</summary>
        public const int DistSerial = 97;

        /// <summary>Tag form of the pin. Mutable -- never trust it alone.</summary>
        public const string Tag = "v0.33.0-dist.97";

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
        /// <para>
        /// <b>15 -> 18 (2026-10-09)</b>: canonical <c>00.31.00</c>, the world units. The three new fields
        /// (<c>length_unit</c>, <c>mass_unit</c>, <c>distance_unit</c>) are on the World, which this SDK reads as
        /// a <c>JObject</c>, so they arrive with no model change. The element models changed only in
        /// prose: the eight unit descriptions now name the World field they mean. <c>presentation.json</c>
        /// is byte-identical again, so <see cref="PresentationSha256"/> did not move.
        /// </para>
        /// <para>
        /// <b>18 -> 19 (2026-10-10)</b>: canonical unchanged. The sidecar moved again: <c>zone</c>
        /// <c>architecture</c> -> <c>pentagon</c>, <c>marker</c> <c>location_on</c> -> <c>polyline</c>, so the
        /// World Browser's two icons were re-rendered. The dist grew to 69 files (<c>schema.json</c>, the
        /// decoded schema for engines; <c>example/hyperion</c>, a public example world folder), and
        /// <c>schema_walk.py</c> and <c>rulings.yaml</c> were rewritten in public words with a new
        /// <c>decode_schema</c>. The generated models changed only in their serial and date stamps, and
        /// every ruling id this SDK cites still exists.
        /// </para>
        /// <para>
        /// <b>19 -> 20 (2026-10-11)</b>: canonical <c>00.32.00</c>, the World definition trued (<c>created_at</c>,
        /// <c>updated_at</c>, <c>public_read</c> in; <c>api_key</c>, <c>user</c>, <c>version</c> out). The walk gained
        /// one scalar kind, <c>scalar_bool</c>, whose only field is <c>world.public_read</c>: the World is read as a
        /// <c>JObject</c> here, so nothing is generated from it. <c>presentation.json</c> is byte-identical.
        /// </para>
        /// <para>
        /// <b>20 -> 24 (2026-10-16)</b>: canonical unchanged. One schema line moved: the element <c>id</c>'s
        /// description now says a UUID, new ids minted as uuidv7 and earlier uuidv4 ids valid (serial 23). The
        /// dist gained <c>walk/lint_folder.py</c>, the folder check (serials 21, 22, 24; 24 knows
        /// <c>changed_by</c> and <c>changed_via</c> as server-set keys). <c>schema_walk.py</c> and
        /// <c>rulings.yaml</c> are byte-identical, and so is <c>presentation.json</c>; the generated models
        /// changed only in their serial and date stamps.
        /// </para>
        /// <para>
        /// <b>24 -> 33 (2026-10-20)</b>: canonical <c>00.32.02</c>, two descriptions sharpened (an Event's
        /// <c>characters</c> are those who know of it: present, aware or involved; a Narrative's dates are when it is
        /// told). The dist gained <c>profiles/</c>, the first extension profiles (<c>identity.md</c>,
        /// <c>calendar.md</c>) and the calendar's conformance fixture (<c>calendar.fixture.json</c>, vendored for
        /// <c>OWCalendarFixtureTests</c>). <c>presentation.json</c> moved for the first time since 15: each type gained
        /// a <c>public</c> list, the fields the platform shows on a known-of element. <c>schema_walk.py</c> and
        /// <c>rulings.yaml</c> are byte-identical.
        /// </para>
        /// <para>
        /// <b>33 -> 35 (2026-10-20)</b>: canonical unchanged. <c>example/world-export.json</c>, the portal's download,
        /// carries the provenance lines (<c>world.source</c>, <c>world.exported_as</c>), vendored for
        /// <c>OWEnvelopeReaderTests</c>; <c>walk/lint_folder.py</c> reads known-of stubs and never reads
        /// <c>records/</c> (34). <c>presentation.json</c>, <c>schema_walk.py</c> and <c>rulings.yaml</c> are
        /// byte-identical; the generated models changed only in their serial.
        /// </para>
        /// <para>
        /// <b>35 -> 39 (2026-10-22)</b>: canonical unchanged, and no decoded file moved: the schema YAMLs,
        /// <c>schema.json</c>, <c>schema_walk.py</c>, <c>rulings.yaml</c>, <c>presentation.json</c> and the examples are
        /// byte-identical. The dist gained two extension profiles, <c>packet.md</c> (36) and <c>review.md</c> (37); the
        /// folder lint flags an <c>api</c> block in a git-tracked <c>world.json</c> (38); and <c>fixtures/owworld/</c>,
        /// the <c>.owworld</c> conformance set of folder spec 4.7 (39), vendored for
        /// <c>OWWorldArchiveConformanceTests</c>. The generated models changed only in their serial and date stamps.
        /// </para>
        /// <para>
        /// <b>39 -> 40 (2026-10-22)</b>: canonical unchanged, no decoded file moved. The conformance set gained
        /// <c>fail-OW-14-crc</c> (an entry whose bytes fail its CRC refuses the archive, cause <c>not_openable</c>; folder
        /// spec 4.7's size bullet), with its <c>verdicts.json</c> entry and generator lines: 79 zips. The generated models
        /// changed only in their serial stamp.
        /// </para>
        /// <para>
        /// <b>40 -> 42 (2026-10-22)</b>: canonical unchanged, no decoded file moved. The conformance set gained
        /// <c>fail-OW-14-stored-short</c> (a stored entry shorter than its declared size, <c>not_openable</c>; serial 41)
        /// and <c>fail-OW-6-marker-bom</c> (a <c>mimetype</c> marker with a byte order mark before the media type,
        /// <c>marker</c>; serial 42), with their <c>verdicts.json</c> entries and generator lines: 81 zips. The reader
        /// already refused both for the cause the set names. The generated models changed only in their serial stamp.
        /// </para>
        /// <para>
        /// <b>42 -> 43 (2026-10-22)</b>: canonical <c>00.33.00</c>, a MINOR step. Zone gains <c>shape</c>, a string in
        /// Scope (polygon or line, lowercase; empty, absent or any other value means not stated; Titus's word,
        /// 2026-10-22). <c>OWZone.Shape</c> is generated from it. The example export moved only its
        /// <c>schema_version</c>; the conformance set did not move. <c>presentation.json</c> is byte-identical.
        /// </para>
        /// <para>
        /// <b>43 -> 44 (2026-10-22)</b>: canonical unchanged, no decoded file moved. The dist gained the room profile
        /// (<c>profiles/room.md</c>, <c>room.fixture.json</c>: which elements are a room's lines and work items), which
        /// this SDK does not read yet. The generated models changed only in their serial stamp.
        /// </para>
        /// <para>
        /// <b>44 -> 48 (2026-10-22)</b>: canonical unchanged, no schema file moved. The room profile gained its mark,
        /// <c>x_room_of</c> (45: the pair and the mark together make a room element), and defines "trimmed and without
        /// regard to case" once for every profile (47). Element bodies carry <c>changed_client</c> beside
        /// <c>changed_by</c> and <c>changed_via</c> (46): the example export and the folder lint's body keys
        /// say so, and the export is re-vendored for <c>OWEnvelopeReaderTests</c>. Two profiles joined: <c>checks</c>
        /// (47, <c>x_checks_rules</c> on a work world's Charter), which this SDK does not read, and <c>names</c> (48,
        /// <c>x_names</c> on the World, a world's own words for the types), read by <see cref="OWNames"/>, its fixture
        /// vendored for <c>OWNamesTests</c>. <c>presentation.json</c>, <c>schema_walk.py</c> and
        /// <c>rulings.yaml</c> are byte-identical; the generated models changed only in their serial stamp.
        /// </para>
        /// <para>
        /// <b>48 -> 49 (2026-10-22)</b>: canonical unchanged, no schema file moved. The conformance set gains the private
        /// case under OW-8 (folder spec v0.3.15, §4.5 rule 11): four writer fixtures on a second source, the base world
        /// plus a producer marked private, and <c>verdicts.json</c>'s <c>sources</c>; none of the 81 zips moved (85 now).
        /// The set is re-vendored for <c>OWWorldArchiveConformanceTests</c>, whose <c>Pack</c> case now runs. The
        /// generated models changed only in their serial stamp.
        /// </para>
        /// <para>
        /// <b>49 -> 51 (2026-10-23)</b>: canonical unchanged, no schema file moved. Only <c>example/world-export.json</c>
        /// moved: its world block is now the World whole, every key GET /world serves (50), and carries <c>origin</c> (the
        /// world's first import: <c>source</c> and <c>producer</c> as the importing tool gave them) and a top-level
        /// <c>x_names</c> map, the World's extension store (51; folder spec v0.3.16). The export is re-vendored for
        /// <c>OWEnvelopeReaderTests</c>. The generated models changed only in their serial stamp.
        /// </para>
        /// <para>
        /// <b>51 -> 52 (2026-10-23)</b>: canonical unchanged, no schema file moved. Only <c>profiles/review.md</c> moved: the
        /// reader rule. A reader that hands a world on leaves elements whose <c>x_review_proposed</c> is an
        /// object out unless asked, counts them and labels a kept one "proposed, not yet reviewed"; a reader that shows
        /// a world to its people shows the mark. The SDK parses no profile file; the generated models changed only in
        /// their serial stamp.
        /// </para>
        /// <para>
        /// <b>52 -> 55 (2026-10-24)</b>: canonical unchanged, no schema file moved. The folder lint takes <c>hidden</c> as a
        /// folder key, true or false (53; folder format §4.8, which <see cref="OWPayload.IsHidden"/> already reads). The
        /// review profile's rule 4 says what a reader does with a malformed mark (53): a reader that hands a world on treats
        /// it as a doubt, a reader that shows a world to its people shows the element as settled, the queue reads an object
        /// only; <see cref="OWPayload.IsProposedOrInDoubt"/> and <see cref="OWPayload.IsProposed"/> already read it so. The
        /// checks profile's list case and reader rules 6 to 8 (54), and the spine profile (55, <c>profiles/spine.md</c>,
        /// <c>spine.fixture.json</c>: a world that maps a software ecosystem), which this SDK does not read.
        /// <c>presentation.json</c>, <c>schema_walk.py</c> and <c>rulings.yaml</c> are byte-identical; the generated models
        /// changed only in their serial and date stamps.
        /// </para>
        /// <para>
        /// <b>55 -> 58 (2026-10-24)</b>: canonical unchanged, no schema file moved. The <c>.owworld</c> set grows to 96
        /// cases: Unicode 15.1's <c>UnicodeData</c> and <c>CompositionExclusions</c> beside the case folds, so a reader builds
        /// OW-18's NFC from them (57), eight NFC pairs (57), and three cases of 58: a deflate stream that ends before its
        /// declared compressed size, one that never ends, and an unassigned pair two NFCs tell apart. The spine profile
        /// gains its seat and projection sentences; the dist carries a LICENSE and SECURITY of the publisher's.
        /// <c>presentation.json</c> is byte-identical. <b>59</b> is text only: the set's rules, the profile pages and the
        /// folder lint say when a rule was ruled, not where; the zips are byte for byte, <c>verdicts.json</c> moved.
        /// <b>60</b> moves only the spine profile's page (eight readings and the <c>x_spine_retires</c> key), which this SDK
        /// does not read.
        /// </para>
        /// </remarks>
        public const string ManifestSha256 =
            "a236f055875a97fe97f6ee5a501bb9342d7a6b81192ba701cc185822c627f705";

        /// <summary>
        /// sha256 of <c>presentation.json</c> as the pinned MANIFEST lists it.
        /// </summary>
        /// <remarks>
        /// The vendored copy at <c>Runtime/Resources/ow-presentation.json</c> must hash to exactly
        /// this. Verified by <c>OWSchemaPinTests</c> on every run, so the sidecar cannot rot
        /// silently the way an unguarded vendored copy always eventually does.
        /// </remarks>
        public const string PresentationSha256 =
            "af84662c29628b6e90b923c9541f68f5924563adce397cebbadfdbf0f06e5f18";

        /// <summary>When these constants were last verified against the published dist.</summary>
        public const string PinnedOn = "2026-10-30";

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
        /// world. Already handled: it is one of the six stripped fields.
        /// </para>
        /// </remarks>
        public const string RulingsPath = "walk/rulings.yaml";
    }
}
