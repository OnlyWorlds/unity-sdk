using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Writes an OnlyWorlds world folder to disk, in the bytes the format specifies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion to <see cref="OWFolderReader"/>, implementing the writer half of the OW Folder
    /// Format spec at the version <see cref="OWFolderExport.FormatVersion"/> declares (an export's own lines, §4.2, are
    /// <see cref="OWFolderExport"/>'s). The reader's law is <i>opening is not editing</i>; the writer's is
    /// narrower and harder: <b>write only what you were given, in bytes another implementation
    /// would have produced.</b>
    /// </para>
    /// <para>
    /// The requirement underneath is DIFFABILITY. A world folder lives in version control, and two
    /// writers that agree on layout but not on bytes produce diffs full of noise -- which defeats
    /// the per-chapter snapshot the format exists for. So the serialization is pinned rather than
    /// defaulted, at six points where a platform default would silently differ:
    /// </para>
    /// <list type="bullet">
    /// <item><b>LF, never CRLF.</b> <see cref="JsonTextWriter"/> takes its newline from the
    /// underlying <see cref="TextWriter"/>, which defaults to <see cref="Environment.NewLine"/> --
    /// CRLF on Windows, where this SDK is mostly authored. Every file this class writes would
    /// otherwise differ from every file the reference implementation wrote, on every line.</item>
    /// <item><b>UTF-8 with no BOM.</b> <c>new UTF8Encoding(false)</c>, because
    /// <see cref="Encoding.UTF8"/> emits one, and three bytes of preamble make a file that diff
    /// calls wholly changed and some parsers reject.</item>
    /// <item><b>Two-space indent, one trailing newline</b> -- matching the reference
    /// implementation's <c>JSON.stringify(value, null, 2)</c>, which no serializer produces by
    /// accident.</item>
    /// <item><b>Key order as received.</b> Keys are never sorted, never reordered to match the
    /// schema. Sorting would stabilize the diff and break something worse: it would reorder foreign
    /// extension values and destroy the byte-fidelity the format requires.</item>
    /// <item><b>Numbers as <c>JSON.stringify</c> writes them</b> (spec §5, ruled 2026-09-28): an
    /// integral float is <c>1</c>, never <c>1.0</c>. The byte-fidelity binds the value, not a
    /// producer's spelling of it, and Newtonsoft's spelling (<c>1.0</c>, <c>1E+21</c>) is not the
    /// reference implementation's.</item>
    /// <item><b>Strings as <c>JSON.stringify</c> escapes them.</b> U+2028, U+2029 and U+0085 are
    /// written raw, where Newtonsoft escapes them; a lone surrogate is escaped, where Newtonsoft's
    /// UTF-8 step would replace it.</item>
    /// </list>
    /// <para>
    /// <b>Identity is the id in the body, never the filename.</b> A body whose name changed lands
    /// at a new path, and the old file is removed -- otherwise a rename silently forks one element
    /// into two, and the reader, keyed on id, serves whichever it happens to see last.
    /// </para>
    /// <para>
    /// <b>Two elements, one short name</b> (§3.2, MUST): the later claimant takes the full-id form,
    /// and a batch (<see cref="Write"/>) is written lowest id first, so its collisions settle
    /// id-ascending, as every conforming writer settles them.
    /// </para>
    /// <para>
    /// <b>Nothing is synthesized.</b> No invented timestamps, no backfilled type, no placeholder
    /// name. A writer must not invent data it does not have, and must not drop data it does: the
    /// body goes through whole, extension fields and all, at every depth.
    /// </para>
    /// </remarks>
    public static class OWFolderWriter
    {
        /// <summary>Slug length cap, in characters.</summary>
        /// <remarks>
        /// 40, matching the reference implementation. The spec once said "~60" and was corrected to
        /// the shipped value rather than the reverse: where a spec and a shipped implementation
        /// disagree on an arbitrary constant, the implementation wins, because two writers
        /// producing byte-different filenames for one element make every diff meaningless.
        /// </remarks>
        public const int SlugMaxLength = 40;

        /// <summary>How many trailing characters of the id go in a filename.</summary>
        /// <remarks>
        /// The TRAILING characters, deliberately. UUIDv7 fronts are timestamps and look alike, so a
        /// leading fragment would collide constantly for elements minted in the same second.
        /// </remarks>
        public const int IdTailLength = 8;

        /// <summary>Separator between the slug and the id fragment.</summary>
        public const string SlugIdSeparator = "--";

        // -- Element writes ---------------------------------------------------

        /// <summary>
        /// Writes one element into <c>elements/&lt;type&gt;/</c>, creating what it needs to.
        /// </summary>
        /// <param name="folder">World folder root -- the directory holding <c>world.json</c>.</param>
        /// <param name="body">
        /// The element, verbatim. Written through unchanged: this method never adds, removes,
        /// reorders or normalises a key, at any depth.
        /// </param>
        /// <returns>Absolute path of the file written.</returns>
        /// <exception cref="OWFolderFormatException">
        /// The body has no usable id, or declares no type, or declares a type that is not one of the 22 type slugs as
        /// written (<c>"Character"</c>, <c>"widget"</c>, <c>7</c>, <c>"../x"</c>: the message names it). Identity and location are the two things
        /// a writer cannot invent, so both are refusals rather than guesses. Or a file of this id is not
        /// well-formed UTF-8 and the body is not the one the reader read there: the reader decoded it with
        /// U+FFFD, and the write would put that over the person's bytes (the message names the file). Or both
        /// names this element may take are files the writer cannot read as an element (below). Or the folder holds
        /// <c>world.json</c>, <c>elements/</c> or <c>spatial/</c> in another case and not as written, which its reader
        /// refuses (<see cref="OWWorldArchiveReason.NameCase"/>; folder spec §4.8): nothing is written. Or
        /// <c>elements/</c> holds this element's type folder in another case and not as written
        /// (<c>elements/Character/</c>), where a disk that folds case would put the file and no reader would read it: the
        /// same reason, the folder named, nothing written.
        /// </exception>
        /// <remarks>
        /// <para>
        /// A body read from a file that is not UTF-8 and not edited is not written: the file stays as it is,
        /// byte for byte, and its path is returned.
        /// </para>
        /// <para>
        /// A writer never writes over a file it could not read (folder spec §4.8): a short name held by a file that is no
        /// element the reader serves (cut short, not JSON, not one JSON value, a number it cannot hold, no id) sends
        /// this element to the full-id form, as another id's file does, and that file keeps its bytes. When the
        /// full-id form is such a file too, nothing is written and the write is refused, naming it.
        /// </para>
        /// <para>
        /// If the id already exists in that directory under a different filename -- a rename -- the
        /// old file is removed, so identity stays with the id and never forks.
        /// </para>
        /// <para>
        /// A short name another id already holds in that directory, compared case-insensitively
        /// (NTFS and APFS resolve the two spellings to one file), sends this element to the full-id
        /// form. The holder is never renamed: one write changes one element's file, as Atlas's own
        /// writes do. To settle a collision among elements arriving together by the spec's
        /// id-ascending rule, write them with <see cref="Write"/>.
        /// </para>
        /// </remarks>
        public static string WriteElement(string folder, JObject body)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (body == null) throw new ArgumentNullException(nameof(body));
            RefuseFixedNameInAnotherCase(folder);

            var (id, type) = Validate(body);
            var directory = DirectoryFor(folder, id, type);
            var undecodable = UndecodableFiles(folder, type);
            RefuseEditOverUndecodable(undecodable, id, body);

            var name = NameOf(body);
            var filename = ResolveFilename(directory, id, name);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, filename);

            // A rename moves this element to a new path. §4's writer note (clarified in v0.3.19): the new body goes
            // into the file it holds first, so an interrupted rename leaves one body under two names at worst, never
            // two different bodies for the reader's first-by-name to choose between.
            var others = OtherFilesFor(directory, id, filename);

            // A file that is not UTF-8 and holds this body unedited is left as it is: rewriting it would put U+FFFD
            // (what the reader decoded) over the bytes the person wrote. (An edited one was refused above.)
            if (others.Count == 0 && undecodable.TryGetValue(id, out var held) && held.Count == 1
                && string.Equals(held[0], path, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            foreach (var other in others) WriteJsonFile(other, body);

            WriteJsonFile(path, body);

            // The old file still holds the same id, and leaving it there forks one element into two files. Removed
            // AFTER the new file lands, so an interrupted write leaves a duplicate rather than nothing.
            foreach (var other in others) File.Delete(other);

            return path;
        }

        /// <summary>
        /// Writes several elements: <see cref="WriteElement"/> per body, in id order, so that a
        /// collision among them settles as the spec's writer collision rule says.
        /// </summary>
        /// <param name="folder">World folder root.</param>
        /// <param name="bodies">Element bodies, each carrying its own id and type.</param>
        /// <returns>Absolute paths written, in the order the bodies arrived.</returns>
        /// <exception cref="OWFolderFormatException">
        /// A body has no usable id or no type, or a type that is not one of the 22 slugs as written, or one would be written over a file of its id that is not
        /// UTF-8 when it is not the body read there (see <see cref="WriteElement"/>), or the folder holds a fixed
        /// name in another case (<see cref="OWWorldArchiveReason.NameCase"/>). Every body is checked before the first is
        /// written, so a refused batch writes nothing.
        /// </exception>
        /// <remarks>
        /// <para>
        /// §3.2 (MUST, v0.3.3): two elements whose short names land on one path are settled
        /// id-ascending by Unicode code point (<see cref="OWCodePointComparer"/>) -- the lowest id keeps the
        /// short form, every other takes the full id -- so
        /// every writer and every re-run picks the same names, whatever order the elements came in.
        /// Written lowest id first, each element meets only lower ids already in place, and that is
        /// the rule. Into an empty folder this names files exactly as the onlyworlds package's
        /// <c>write_folder</c> does (<c>resolve_filenames</c>).
        /// </para>
        /// <para>
        /// An element already in the folder keeps its name: a lower id arriving later takes the
        /// full-id form rather than displacing it (see <see cref="WriteElement"/>). The package never
        /// meets that case, because it writes only a folder's birth.
        /// </para>
        /// <para>
        /// A body repeated in the batch keeps its LAST copy: the order is stable, so the later one is
        /// written later.
        /// </para>
        /// </remarks>
        public static List<string> Write(string folder, IEnumerable<JObject> bodies)
        {
            if (bodies == null) throw new ArgumentNullException(nameof(bodies));

            var list = new List<JObject>(bodies);
            foreach (var body in list)
            {
                if (body == null) throw new ArgumentNullException(nameof(bodies), "A body in the batch is null.");
            }

            RefuseFixedNameInAnotherCase(folder);

            var undecodableByType = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
            foreach (var body in list)
            {
                var (id, type) = Validate(body);
                var directory = DirectoryFor(folder, id, type);
                if (!undecodableByType.TryGetValue(type, out var undecodable))
                    undecodableByType[type] = undecodable = UndecodableFiles(folder, type);
                RefuseEditOverUndecodable(undecodable, id, body);

                // A body whose every name is a file the writer could not read is refused here, before any is written.
                // The batch's own writes add only files it reads, so the answer cannot change while it runs.
                ResolveFilename(directory, id, NameOf(body));
            }

            var ids = list.Select(body => IdOf(body)).ToList();
            var order = Enumerable.Range(0, list.Count).ToList();
            order.Sort((x, y) =>
            {
                var c = OWCodePointComparer.Instance.Compare(ids[x], ids[y]);
                return c != 0 ? c : x.CompareTo(y);
            });

            var written = new string[list.Count];
            foreach (var i in order) written[i] = WriteElement(folder, list[i]);
            return written.ToList();
        }

        /// <summary>
        /// Refuses to write into a folder its own reader refuses: one holding <c>world.json</c>, <c>elements/</c> or
        /// <c>spatial/</c> in another case and not as written (folder spec §4.8, ruled 2026-11-25). On a disk that folds
        /// case the write would land inside <c>Elements/</c>, which no reader reads. A folder not there yet holds nothing.
        /// </summary>
        /// <exception cref="OWFolderFormatException">Its reason <see cref="OWWorldArchiveReason.NameCase"/>; nothing written.</exception>
        private static void RefuseFixedNameInAnotherCase(string folder)
        {
            if (Directory.Exists(folder)) OWFixedNames.RefuseFolder(folder);
        }

        /// <summary>The body's id and type, or the refusal a writer gives without them.</summary>
        private static (string Id, string Type) Validate(JObject body)
        {
            var id = IdOf(body);
            if (string.IsNullOrEmpty(id))
            {
                throw new OWFolderFormatException(
                    "Cannot write an element with no id. Identity lives in the body, and a writer "
                    + "must not mint one -- an id, once minted, must be persisted and must never be "
                    + "re-derived.");
            }

            var token = body["type"];
            if (token == null || token.Type == JTokenType.Null
                || (token.Type == JTokenType.String && ((string)token).Length == 0))
            {
                throw new OWFolderFormatException(
                    $"Element '{id}' declares no type, and a writer has no directory to put it in. "
                    + "A reader may infer a type from the directory; a writer cannot run that "
                    + "inference backwards.");
            }

            // The type names the folder, so it is one of the 22 as written, or nothing is written: another case, an
            // unknown word, a number or a path ("../x") would make a folder no reader reads, or one outside elements/.
            var type = OWFolderReader.DeclaredType(body);
            if (type == null)
            {
                var given = token.Type == JTokenType.String ? $"'{(string)token}'"
                    : token.Type == JTokenType.Integer || token.Type == JTokenType.Float ? "a number"
                    : token.Type == JTokenType.Boolean ? ((bool)token ? "true" : "false")
                    : token.Type == JTokenType.Array ? "a list"
                    : token.Type == JTokenType.Object ? "an object"
                    : token.Type.ToString();
                throw new OWFolderFormatException(
                    $"Element '{id}' declares the type {given}, which is not one of the 22 type slugs as the standard "
                    + $"spells them ({string.Join(", ", OWElementTypes.Slugs)}): a reader reads no other type folder, "
                    + "so nothing was written.");
            }

            return (id, type);
        }

        /// <summary>
        /// Removes an element from the folder, wherever in it that element sits.
        /// </summary>
        /// <param name="folder">World folder root.</param>
        /// <param name="id">Element id. The filename is not consulted for identity.</param>
        /// <returns>True if a file was removed.</returns>
        /// <exception cref="OWFolderFormatException">
        /// The folder holds <c>world.json</c>, <c>elements/</c> or <c>spatial/</c> in another case and not as written
        /// (<see cref="OWWorldArchiveReason.NameCase"/>): nothing is removed.
        /// </exception>
        /// <remarks>
        /// Every type directory is searched, because an element's type may have changed since it
        /// was written, and a delete that only looks where it expects the file leaves an orphan
        /// behind -- which the reader then serves as a live element. Only what the reader serves from is
        /// searched: a type folder named as written, and a file whose extension is <c>.json</c> in lower case.
        /// </remarks>
        public static bool DeleteElement(string folder, string id)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            RefuseFixedNameInAnotherCase(folder);

            var elementsRoot = Path.Combine(folder, "elements");
            if (!Directory.Exists(elementsRoot)) return false;

            var removed = false;
            foreach (var typeDir in Directory.GetDirectories(elementsRoot))
            {
                // Only a type folder as written, where the reader serves from (ruled 2026-11-23): a file in elements/Character/,
                // elements/widget/ or a tool's .atlas/ is not the element's, so it is not removed.
                if (Array.IndexOf(OWElementTypes.Slugs, Path.GetFileName(typeDir)) < 0) continue;
                foreach (var file in FilesHoldingId(typeDir, id))
                {
                    File.Delete(file);
                    removed = true;
                }
            }

            return removed;
        }

        // -- The world file ---------------------------------------------------

        /// <summary>
        /// Writes <c>world.json</c> at the folder root.
        /// </summary>
        /// <param name="folder">World folder root. Created if absent.</param>
        /// <param name="world">
        /// World metadata, verbatim. Carried through whole -- a writer must not drop keys the
        /// source had, including the full <c>time_*</c> block, <c>description</c> and
        /// <c>image_url</c>.
        /// </param>
        /// <returns>Absolute path of <c>world.json</c>.</returns>
        /// <exception cref="OWFolderFormatException">
        /// The world's id is no non-empty string; or the folder holds <c>world.json</c>, <c>elements/</c> or
        /// <c>spatial/</c> in another case and not as written (<see cref="OWWorldArchiveReason.NameCase"/>): a
        /// <c>World.JSON</c> keeps its bytes.
        /// </exception>
        /// <remarks>
        /// On disk the world's now-line is <c>time_current</c>, the canonical schema's spelling --
        /// not the wire's <c>time_range_current</c>, which is a server-side rename the standard
        /// never adopted. This method writes what it is given: a caller holding a wire body is the
        /// one that must map the key, and doing it here would rewrite a spelling the file chose.
        /// </remarks>
        public static string WriteWorld(string folder, JObject world)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (world == null) throw new ArgumentNullException(nameof(world));

            // The folder read's own question (§4.1: a non-empty string), so this never writes a world.json the reader
            // refuses. A draft world (§3.4, v0.3.22) is no exception: its id is the tool's own until a host gives one.
            if (OWFolderReader.WorldIdOf(world) == null)
            {
                throw new OWFolderFormatException(
                    "Cannot write a world.json with no id (a non-empty string). Id and name are the only required "
                    + "keys, and a world without an id cannot be keyed or compared; a draft world's id is the "
                    + "tool's own until a host gives it one.");
            }

            RefuseFixedNameInAnotherCase(folder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "world.json");
            WriteJsonFile(path, world);
            return path;
        }

        // -- Filenames --------------------------------------------------------

        /// <summary>
        /// The canonical filename for an element: <c>&lt;slug&gt;--&lt;id-tail&gt;.json</c>.
        /// </summary>
        /// <param name="id">Element id.</param>
        /// <param name="name">Element name, which may be null, empty or unsluggable.</param>
        /// <returns>The filename, including extension.</returns>
        /// <remarks>
        /// A name that produces no slug -- empty, or entirely non-ASCII -- falls back to
        /// <c>&lt;full-id&gt;.json</c>. The tail is always present otherwise, so a filename changes
        /// only when the user renames the element: no silent reshuffling when a same-name twin
        /// appears, and a quiet history in version control.
        /// </remarks>
        public static string ElementFilename(string id, string name)
        {
            var slug = Slugify(name);
            return string.IsNullOrEmpty(slug)
                ? id + ".json"
                : slug + SlugIdSeparator + IdTail(id) + ".json";
        }

        /// <summary>
        /// The collision fallback: <c>&lt;slug&gt;--&lt;full-id&gt;.json</c>.
        /// </summary>
        /// <param name="id">Element id.</param>
        /// <param name="name">Element name.</param>
        /// <returns>The filename, including extension.</returns>
        /// <remarks>
        /// Used only when the short form is already held by a different id in the same directory.
        /// Two same-named elements sharing an 8-character tail is about a one-in-four-billion event
        /// per pair, and its consequence is an element silently destroyed by the write of its twin
        /// -- exactly the loss the format exists to prevent, so it is checked rather than assumed
        /// away.
        /// </remarks>
        public static string ElementFilenameFullId(string id, string name)
        {
            var slug = Slugify(name);
            return string.IsNullOrEmpty(slug)
                ? id + ".json"
                : slug + SlugIdSeparator + id + ".json";
        }

        /// <summary>
        /// Lowercase ASCII kebab of a name, capped at <see cref="SlugMaxLength"/>.
        /// </summary>
        /// <param name="name">Any name. Null, empty and unsluggable all give an empty string.</param>
        /// <returns>The slug, or an empty string when the name yields none.</returns>
        /// <remarks>
        /// <para>
        /// The folder format's rule (section 3.2): the name in NFKD; U+0300 to U+036F dropped and no other mark;
        /// lowercased; every run outside a-z and 0-9 one hyphen, none at either end; cut at 40 and the hyphens the cut
        /// leaves dropped. An accented e becomes a plain one, a ligature its letters, a full-width or mathematical
        /// letter its plain one, and a mark outside U+0300 to U+036F is a break, as the TypeScript and Python
        /// packages give.
        /// </para>
        /// <para>
        /// The decompositions are the SDK's own table (<c>OWSlugData</c>, from Unicode's UnicodeData.txt), never the
        /// runtime's: Mono's <c>string.Normalize</c> leaves a ligature and the mathematical letters whole.
        /// </para>
        /// </remarks>
        public static string Slugify(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var builder = new StringBuilder(name.Length);
            var pendingSeparator = false;

            for (var i = 0; i < name.Length; i++)
            {
                int cp = name[i];
                if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                {
                    cp = char.ConvertToUtf32(name[i], name[i + 1]);
                    i++;
                }

                var at = Array.BinarySearch(OWSlugData.CodePoints, cp);
                string part;
                if (at >= 0) part = OWSlugData.Parts[at];
                else if (cp >= 0x300 && cp <= 0x36F) part = string.Empty;
                else if (cp >= 'A' && cp <= 'Z') part = ((char)(cp + 32)).ToString();
                else if ((cp >= 'a' && cp <= 'z') || (cp >= '0' && cp <= '9')) part = ((char)cp).ToString();
                else part = "-";

                foreach (var c in part)
                {
                    if (c == '-')
                    {
                        pendingSeparator = true;
                        continue;
                    }
                    // Runs of separators collapse to a single hyphen, and a leading run emits
                    // nothing at all -- so a slug can never contain "--" and never starts with one.
                    if (pendingSeparator && builder.Length > 0) builder.Append('-');
                    pendingSeparator = false;
                    builder.Append(c);
                }
            }

            var slug = builder.ToString();
            if (slug.Length > SlugMaxLength)
            {
                slug = slug.Substring(0, SlugMaxLength).TrimEnd('-');
            }

            return slug;
        }

        /// <summary>Trailing fragment of an id, safe to place after the separator.</summary>
        /// <param name="id">Element id.</param>
        /// <returns>The fragment, which is the whole id when the id is short enough.</returns>
        /// <remarks>
        /// Canonical uuids never trip the guard here -- their last hyphen sits 13 characters from
        /// the end -- but fixture ids and hand-written ones can, and a fragment containing
        /// <c>--</c> or starting with a hyphen makes the separator unreadable.
        /// </remarks>
        public static string IdTail(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (id.Length <= IdTailLength) return id;

            var raw = id.Substring(id.Length - IdTailLength);
            if (raw.IndexOf(SlugIdSeparator, StringComparison.Ordinal) < 0
                && !raw.StartsWith("-", StringComparison.Ordinal))
            {
                return raw;
            }

            var compact = id.Replace("-", string.Empty);
            return compact.Length <= IdTailLength
                ? compact
                : compact.Substring(compact.Length - IdTailLength);
        }

        /// <summary>
        /// The directory an element is written into: <c>elements/&lt;type&gt;/</c>, or the legacy <c>spatial/&lt;type&gt;/</c>
        /// when the element already lives there and not in <c>elements/</c>. Each folder is found by its listed name, as
        /// written, never by the file system's lookup, which folds case on NTFS and APFS: <c>spatial/Pin/</c> is no pin
        /// folder, and a write never lands in <c>elements/Character/</c> (below).
        /// </summary>
        /// <exception cref="OWFolderFormatException">
        /// The element would go to <c>elements/&lt;type&gt;/</c> and <c>elements/</c> holds that type folder in another
        /// case and not as written (<c>elements/Character/</c>): on a disk that folds case the file would land there,
        /// where no reader reads it (a type folder is read as written, folder spec §4.8, ruled 2026-11-23). The reason
        /// is <see cref="OWWorldArchiveReason.NameCase"/>, the message names the folder; nothing is written.
        /// </exception>
        private static string DirectoryFor(string folder, string id, string type)
        {
            var directory = Path.Combine(folder, "elements", type);
            // §4 (v0.3.8, one id, one file): an element older Atlas kept under spatial/<type>/ is written there, in
            // its own file, so the folder never holds one id in both layouts. A new element goes to elements/.
            var legacy = Path.Combine(folder, "spatial", type);
            if (Array.IndexOf(OWFolderReader.LegacySpatialTypes, type) >= 0
                && HoldsFolderAsWritten(Path.Combine(folder, "spatial"), type)
                && FilesHoldingId(legacy, id).Count > 0
                && (!Directory.Exists(directory) || FilesHoldingId(directory, id).Count == 0))
            {
                return legacy;
            }

            RefuseTypeFolderInAnotherCase(Path.Combine(folder, "elements"), id, type);
            return directory;
        }

        /// <summary>Whether <paramref name="parent"/> lists a folder named exactly <paramref name="name"/>.</summary>
        private static bool HoldsFolderAsWritten(string parent, string name)
            => Directory.Exists(parent)
               && Directory.GetDirectories(parent).Any(d => string.Equals(Path.GetFileName(d), name, StringComparison.Ordinal));

        /// <summary>
        /// Refuses a write into <c>elements/&lt;type&gt;/</c> when <c>elements/</c> lists the type folder only in another
        /// case (by the archive's fold, as the fixed names are compared). One that lists both (a disk that keeps case)
        /// writes into the folder as written.
        /// </summary>
        private static void RefuseTypeFolderInAnotherCase(string elementsRoot, string id, string type)
        {
            if (!Directory.Exists(elementsRoot)) return;
            var names = Directory.GetDirectories(elementsRoot).Select(Path.GetFileName).ToList();
            if (OWFixedNames.Fault(names, new[] { type }) == null) return;

            var key = OWWorldArchive.Fold(type);
            var others = names.Where(n => OWWorldArchive.Fold(n) == key).ToList();
            others.Sort(OWCodePointComparer.Instance);
            throw new OWFolderFormatException(OWWorldArchiveReason.NameCase,
                $"'{Path.Combine(elementsRoot, others[0])}' is the type folder '{type}' in another case, and there is no "
                + $"'{type}' as written: a reader reads a type folder as written (folder spec §4.8), so element '{id}' "
                + $"written into it would be read by no one. Rename the folder '{type}'; nothing was written.");
        }

        /// <summary>The body's name when it is a string, the one a filename is made from; otherwise null.</summary>
        private static string NameOf(JObject body)
            => body["name"] != null && body["name"].Type == JTokenType.String ? body["name"].ToString() : null;

        /// <summary>
        /// The short form, unless that path is held by a file that is not this element's: a different id (the writer
        /// collision rule, §3.2) or a file the writer could not read as an element (§4.8: a writer never writes over a
        /// file it could not read). Then the next name, the full-id form.
        /// </summary>
        /// <exception cref="OWFolderFormatException">
        /// The full-id form is a file the writer could not read as well (or the only form, for a name with no slug):
        /// there is no name left that writes over nothing, so nothing is written. The message names the file.
        /// </exception>
        private static string ResolveFilename(string directory, string id, string name)
        {
            var candidate = ElementFilename(id, name);
            var candidatePath = FileNamed(directory, candidate);
            if (candidatePath == null) return candidate;

            // The path is taken. If it is taken by THIS element, that is not a collision -- it is
            // the ordinary case of rewriting a file in place.
            var occupant = IdInFile(candidatePath);
            if (string.Equals(occupant, id, StringComparison.Ordinal)) return candidate;

            // Another id, or a file no reader serves as an element (cut short, not JSON, not one value, no id): the
            // person's bytes stay, and this element takes the next name.
            var full = ElementFilenameFullId(id, name);
            var fullPath = FileNamed(directory, full);
            if (fullPath != null && IdInFile(fullPath) == null)
            {
                throw new OWFolderFormatException(
                    $"'{fullPath}' holds no element the writer can read"
                    + (fullPath == candidatePath ? "" : $", nor does '{candidatePath}'")
                    + $", and element '{id}' has no other name to take: a writer never writes over a file it could not "
                    + "read (folder spec §4.8). Mend or move that file first; nothing was written.");
            }

            return full;
        }

        /// <summary>
        /// The file of this name in the directory, looked for case-insensitively on every filesystem (on NTFS and APFS
        /// the two spellings are one file, and a folder written on Linux is opened on both); null when there is none.
        /// </summary>
        private static string FileNamed(string directory, string filename)
        {
            if (!Directory.Exists(directory)) return null;
            return Directory.GetFiles(directory, "*.json")
                .FirstOrDefault(file => string.Equals(Path.GetFileName(file), filename, StringComparison.OrdinalIgnoreCase));
        }

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// The element files of one type (in <c>elements/&lt;type&gt;/</c> and the legacy <c>spatial/&lt;type&gt;/</c>)
        /// whose bytes are not well-formed UTF-8, by the id the reader reads in them. The reader decodes such a file
        /// leniently, so the body it hands out holds U+FFFD where the file holds the person's bytes.
        /// </summary>
        private static Dictionary<string, List<string>> UndecodableFiles(string folder, string type)
        {
            var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var directory in new[] { Path.Combine(folder, "elements", type), Path.Combine(folder, "spatial", type) })
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.GetFiles(directory, "*.json"))
                {
                    try { StrictUtf8.GetString(File.ReadAllBytes(file)); continue; }
                    catch (DecoderFallbackException) { }
                    catch (Exception) { continue; }  // unreadable here: not this check's to judge

                    var id = IdInFile(file);
                    if (id == null) continue;
                    if (!found.TryGetValue(id, out var files)) found[id] = files = new List<string>();
                    files.Add(file);
                }
            }

            return found;
        }

        /// <summary>
        /// Refuses, by the file's name, to write a body over a file of its id that is not UTF-8 when the body is not
        /// the one the reader read there: the write would replace the person's bytes with U+FFFD. The body read and
        /// not edited passes (the caller leaves that file as it is).
        /// </summary>
        private static void RefuseEditOverUndecodable(Dictionary<string, List<string>> undecodable, string id, JObject body)
        {
            if (!undecodable.TryGetValue(id, out var files)) return;
            foreach (var file in files)
            {
                JObject read;
                try { read = Parse(File.ReadAllText(file)); }
                catch (Exception) { read = null; }
                if (read != null && JToken.DeepEquals(read, body)) continue;
                throw new OWFolderFormatException(
                    $"'{file}' is not well-formed UTF-8, so element '{id}' as read holds U+FFFD where the file holds "
                    + "other bytes, and writing it would replace them. Re-save that file as UTF-8 first; nothing was "
                    + "written.");
            }
        }

        /// <summary>Every file in this directory whose body carries the given id.</summary>
        private static List<string> FilesHoldingId(string directory, string id)
        {
            var found = new List<string>();
            if (!Directory.Exists(directory)) return found;

            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                if (string.Equals(IdInFile(file), id, StringComparison.Ordinal)) found.Add(file);
            }

            return found;
        }

        /// <summary>
        /// The files in this directory that hold <paramref name="id"/> and are not
        /// <paramref name="keep"/>.
        /// </summary>
        private static List<string> OtherFilesFor(string directory, string id, string keep)
        {
            var others = new List<string>();
            foreach (var file in FilesHoldingId(directory, id))
            {
                // Case-insensitive, because NTFS and APFS resolve two spellings to one entry: on
                // those filesystems, "the other" file would be the one about to be written.
                if (string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase)) continue;
                others.Add(file);
            }

            return others;
        }

        /// <summary>
        /// The id the folder reader would serve this file as, or null when it would serve none: not one JSON object (cut
        /// short, not JSON, content after the value, a number the reader cannot hold, not an object) or no id.
        /// </summary>
        /// <remarks>
        /// An unreadable neighbour is never an error here. A malformed file is not ours to delete or to write over
        /// (<see cref="ResolveFilename"/> gives the element the next name), and it must not stop the element we were
        /// actually asked to write. Read as the reader reads it (<see cref="Parse"/>, dates kept as text), so an id that
        /// looks like a date is compared as written.
        /// </remarks>
        private static string IdInFile(string path)
        {
            // The reader skips a name spelled otherwise (X.JSON) whatever it holds (ruled 2026-11-23), so it holds no
            // element here either: never written into (on NTFS x.json opens it), never removed as a rename's old file.
            if (!Path.GetFileName(path).EndsWith(".json", StringComparison.Ordinal)) return null;
            try
            {
                var id = Parse(File.ReadAllText(path))["id"]?.ToString();
                return string.IsNullOrEmpty(id) ? null : id;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string IdOf(JObject body)
        {
            var id = body["id"];
            return id == null || id.Type == JTokenType.Null ? null : id.ToString();
        }

        // -- The bytes --------------------------------------------------------

        /// <summary>
        /// Parses JSON without letting the parser touch the values it does not need to.
        /// </summary>
        /// <param name="text">JSON text.</param>
        /// <returns>The object, with every scalar exactly as written.</returns>
        /// <remarks>
        /// <para>
        /// <b>Use this, not <c>JObject.Parse</c>, for anything that will be written back.</b>
        /// Newtonsoft defaults <see cref="DateParseHandling"/> to <c>DateTime</c>, so a plain parse
        /// does not return the string in the file -- it recognises an ISO-8601 timestamp, converts
        /// it to a <see cref="DateTime"/> in the machine's LOCAL timezone, and re-serializes it in
        /// that timezone on the way out.
        /// </para>
        /// <para>
        /// Measured, not theorised: round-tripping the Sikelia world through
        /// <c>JObject.Parse</c> rewrote <c>"2026-09-04T20:36:14.605251+00:00"</c> as
        /// <c>"2026-09-04T22:36:14.605251+02:00"</c> in every one of 8 fixture files. The instant
        /// is the same and the JSON stays valid, which is exactly why it is dangerous: it is a
        /// whole-repository diff that no test comparing parsed values can see, and its content
        /// depends on the timezone of whichever machine happened to run the write.
        /// </para>
        /// <para>
        /// The same parse also normalises the FORMAT of a timestamp, so a world folder written from
        /// one developer's laptop would differ from the same folder written on the build machine.
        /// A format whose bytes depend on the writer's locale is not a format.
        /// </para>
        /// </remarks>
        public static JObject Parse(string text) => OWJson.ParseObject(text);

        /// <summary>
        /// Reads a JSON file without coercing its values. The file-shaped
        /// <see cref="Parse(string)"/>.
        /// </summary>
        /// <param name="path">Path to a JSON file.</param>
        /// <returns>The object, with every scalar exactly as written.</returns>
        public static JObject ReadJsonFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            return Parse(File.ReadAllText(path));
        }

        /// <summary>
        /// Serializes a token to the format's exact bytes: LF, two-space indent, one trailing
        /// newline, keys in the order they arrived, numbers spelled as <c>JSON.stringify</c>
        /// spells them.
        /// </summary>
        /// <param name="token">Any JSON token. Written through without inspection.</param>
        /// <returns>The file's text, ending in a single newline.</returns>
        /// <remarks>
        /// Public because it is the piece worth testing directly and worth reusing: a caller
        /// writing a sidecar beside a world folder should produce the same bytes the folder does,
        /// and reimplementing this is how two files in one repository come to disagree.
        /// </remarks>
        public static string Serialize(JToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));

            var buffer = new StringWriter(CultureInfo.InvariantCulture)
            {
                // Not a default anywhere. A TextWriter takes Environment.NewLine, so on Windows
                // every indented line would end CRLF and no file would match one the reference
                // implementation wrote.
                NewLine = "\n",
            };

            using (var writer = new JsTextWriter(buffer)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
            })
            {
                token.WriteTo(writer);
            }

            return buffer.ToString() + "\n";
        }

        /// <summary>Writes a token to a file in the format's exact bytes.</summary>
        private static void WriteJsonFile(string path, JToken token)
        {
            // UTF8Encoding(false), never Encoding.UTF8 -- the latter writes a three-byte BOM, and
            // the format says no BOM.
            File.WriteAllText(path, Serialize(token), new UTF8Encoding(false));
        }

        /// <summary>
        /// A <see cref="JsonTextWriter"/> that spells every non-integer number the way ECMAScript's
        /// <c>Number::toString</c> does, and escapes strings the way <c>JSON.stringify</c> does, and
        /// changes nothing else.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Strings: Newtonsoft's default escaping also writes U+0085, U+2028 and U+2029 as
        /// <c>\u0085</c>, <c>\u2028</c> and <c>\u2029</c>, and its UTF-8 step turns a lone surrogate
        /// into U+FFFD. <c>JSON.stringify</c> (and the Python package's writer) writes the first three
        /// raw and a lone surrogate as its escape (measured 2026-10-16 against the package on one body
        /// holding all of them). So every string, value and key, goes through <see cref="Quote"/>
        /// instead.
        /// </para>
        /// <para>
        /// Newtonsoft appends <c>.0</c> to an integral double and switches to <c>E+</c> notation at
        /// its own thresholds; the reference implementation writes <c>1</c>, <c>100000000000000000000</c>
        /// and <c>1e+21</c>. Same values, different bytes, so every float in a folder became a diff.
        /// Integers never reach these overloads: a token parsed from a file keeps an integer literal
        /// as an integer (<see cref="OWJson.ParseObject"/>), so it is written back as it was read.
        /// </para>
        /// <para>
        /// The digits are the SHORTEST that parse back to the same double, and the closest of those,
        /// as ECMAScript requires. Mono's <c>"R"</c> format is not that (it writes <c>1/3</c> with 17
        /// digits where 16 round-trip), and its <c>"E"</c> formats and its parser are not exact at
        /// extreme exponents either, so the digits are found in integer arithmetic
        /// (<see cref="OWJsNumber"/>, shared with the wire's writer, <see cref="OWJson"/>).
        /// </para>
        /// </remarks>
        private sealed class JsTextWriter : OWJsNumberWriter
        {
            public JsTextWriter(TextWriter writer) : base(writer, keepPoint: false) { }

            public override void WriteValue(string value)
            {
                if (value == null) WriteNull();
                else WriteRawValue(Quote(value));
            }

            public override void WritePropertyName(string name) => WritePropertyName(name, true);

            // Unescaped from here on: the base writes the quotes and the colon around what it is given.
            public override void WritePropertyName(string name, bool escape)
                => base.WritePropertyName(escape ? Escape(name) : name, false);

            private static string Quote(string value) => "\"" + Escape(value) + "\"";

            /// <summary>
            /// <c>JSON.stringify</c>'s string escaping (ES2019, well-formed): the quote, the backslash
            /// and the controls below U+0020 (in their short forms where they have one), and a lone
            /// surrogate as <c>\udXXX</c>. Everything else is written as it is.
            /// </summary>
            private static string Escape(string value)
            {
                StringBuilder builder = null;
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    string escape = null;
                    switch (c)
                    {
                        case '"': escape = "\\\""; break;
                        case '\\': escape = "\\\\"; break;
                        case '\b': escape = "\\b"; break;
                        case '\f': escape = "\\f"; break;
                        case '\n': escape = "\\n"; break;
                        case '\r': escape = "\\r"; break;
                        case '\t': escape = "\\t"; break;
                        default:
                            var pairedHigh = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]);
                            var pairedLow = char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(value[i - 1]);
                            if (c < ' ' || (char.IsSurrogate(c) && !pairedHigh && !pairedLow))
                            {
                                escape = "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
                            }

                            break;
                    }

                    if (escape == null)
                    {
                        builder?.Append(c);
                        continue;
                    }

                    if (builder == null) builder = new StringBuilder(value, 0, i, value.Length + 8);
                    builder.Append(escape);
                }

                return builder == null ? value : builder.ToString();
            }

            // A NaN or an infinity is refused rather than written in Newtonsoft's way (see Refuse); every
            // finite double and float is spelled by the shared writer (OWJsNumber), as the wire spells it.
            protected override void WriteNonFinite(double value) => Refuse(value);

            protected override void WriteNonFinite(float value) => Refuse(value);

            // JSON.stringify would see this value only after parsing it to a double, so it goes the
            // same way here. A decimal beyond double precision loses digits exactly as it would there.
            public override void WriteValue(decimal value) => WriteValue((double)value);

            public override void WriteValue(decimal? value)
            {
                if (value.HasValue) WriteValue(value.Value);
                else WriteNull();
            }

            /// <summary>Refuses NaN and the infinities.</summary>
            /// <remarks>
            /// <c>JSON.stringify</c> silently writes <c>null</c> for these, which is a value change;
            /// Newtonsoft writes a quoted <c>"NaN"</c>, which is another. A writer that must not
            /// invent data refuses instead (the Python folder module does the same).
            /// </remarks>
            private static void Refuse(double value)
            {
                throw new OWFolderFormatException(
                    $"Cannot write the number {value.ToString(CultureInfo.InvariantCulture)}: JSON has "
                    + "no form for it, and writing null or a string in its place would change the value.");
            }
        }
    }
}
