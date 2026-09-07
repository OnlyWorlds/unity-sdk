using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// Format spec v0.3.6. The reader's law is <i>opening is not editing</i>; the writer's is
    /// narrower and harder: <b>write only what you were given, in bytes another implementation
    /// would have produced.</b>
    /// </para>
    /// <para>
    /// The requirement underneath is DIFFABILITY. A world folder lives in version control, and two
    /// writers that agree on layout but not on bytes produce diffs full of noise -- which defeats
    /// the per-chapter snapshot the format exists for. So the serialization is pinned rather than
    /// defaulted, at four points where a platform default would silently differ:
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
    /// </list>
    /// <para>
    /// <b>Identity is the id in the body, never the filename.</b> A body whose name changed lands
    /// at a new path, and the old file is removed -- otherwise a rename silently forks one element
    /// into two, and the reader, keyed on id, serves whichever it happens to see last.
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
        /// The body has no usable id, or declares no type. Identity and location are the two things
        /// a writer cannot invent, so both are refusals rather than guesses.
        /// </exception>
        /// <remarks>
        /// If the id already exists in that directory under a different filename -- a rename -- the
        /// old file is removed, so identity stays with the id and never forks.
        /// </remarks>
        public static string WriteElement(string folder, JObject body)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (body == null) throw new ArgumentNullException(nameof(body));

            var id = IdOf(body);
            if (string.IsNullOrEmpty(id))
            {
                throw new OWFolderFormatException(
                    "Cannot write an element with no id. Identity lives in the body, and a writer "
                    + "must not mint one -- an id, once minted, must be persisted and must never be "
                    + "re-derived.");
            }

            var type = body["type"]?.ToString();
            if (string.IsNullOrEmpty(type))
            {
                throw new OWFolderFormatException(
                    $"Element '{id}' declares no type, and a writer has no directory to put it in. "
                    + "A reader may infer a type from the directory; a writer cannot run that "
                    + "inference backwards.");
            }

            var directory = Path.Combine(folder, "elements", type);
            Directory.CreateDirectory(directory);

            var name = body["name"] != null && body["name"].Type == JTokenType.String
                ? body["name"].ToString()
                : null;

            var filename = ResolveFilename(directory, id, name);
            var path = Path.Combine(directory, filename);

            WriteJsonFile(path, body);

            // A rename moved this element to a new path. Its old file still holds the same id, and
            // leaving it there forks one element into two -- the reader would ingest both and serve
            // whichever it saw last. Removed AFTER the new file lands, so an interrupted write
            // leaves a duplicate rather than nothing.
            RemoveOtherFilesFor(directory, id, filename);

            return path;
        }

        /// <summary>
        /// Writes several elements. Equivalent to <see cref="WriteElement"/> per body.
        /// </summary>
        /// <param name="folder">World folder root.</param>
        /// <param name="bodies">Element bodies, each carrying its own id and type.</param>
        /// <returns>Absolute paths written, in the order the bodies arrived.</returns>
        public static List<string> Write(string folder, IEnumerable<JObject> bodies)
        {
            if (bodies == null) throw new ArgumentNullException(nameof(bodies));

            var written = new List<string>();
            foreach (var body in bodies)
            {
                written.Add(WriteElement(folder, body));
            }

            return written;
        }

        /// <summary>
        /// Removes an element from the folder, wherever in it that element sits.
        /// </summary>
        /// <param name="folder">World folder root.</param>
        /// <param name="id">Element id. The filename is not consulted for identity.</param>
        /// <returns>True if a file was removed.</returns>
        /// <remarks>
        /// Every type directory is searched, because an element's type may have changed since it
        /// was written, and a delete that only looks where it expects the file leaves an orphan
        /// behind -- which the reader then serves as a live element.
        /// </remarks>
        public static bool DeleteElement(string folder, string id)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            var elementsRoot = Path.Combine(folder, "elements");
            if (!Directory.Exists(elementsRoot)) return false;

            var removed = false;
            foreach (var typeDir in Directory.GetDirectories(elementsRoot))
            {
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
        /// <exception cref="OWFolderFormatException">The world has no id.</exception>
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

            if (string.IsNullOrEmpty(world["id"]?.ToString()))
            {
                throw new OWFolderFormatException(
                    "Cannot write a world.json with no id. Id and name are the only required keys, "
                    + "and a world without an id cannot be keyed or compared.");
            }

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
        /// Diacritics are decomposed and their combining marks dropped before the alphanumeric
        /// pass, so an accented e becomes a plain one. Doing it in the other order leaves a
        /// trailing hyphen instead, because the mark is then just another non-alphanumeric
        /// character.
        /// </para>
        /// <para>
        /// The cap is applied last and the result re-trimmed, so a name cut mid-word never leaves a
        /// trailing hyphen -- which would make the <c>--</c> separator ambiguous.
        /// </para>
        /// </remarks>
        public static string Slugify(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var decomposed = name.Normalize(NormalizationForm.FormKD);

            var builder = new StringBuilder(decomposed.Length);
            var pendingSeparator = false;

            foreach (var c in decomposed)
            {
                // Combining marks are dropped, not separated: the mark belongs to the letter before
                // it, and treating it as a break splits one word into two.
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

                var lower = char.ToLowerInvariant(c);
                var isAlphanumeric = (lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9');

                if (isAlphanumeric)
                {
                    // Runs of separators collapse to a single hyphen, and a leading run emits
                    // nothing at all -- so a slug can never contain "--" and never starts with one.
                    if (pendingSeparator && builder.Length > 0) builder.Append('-');
                    pendingSeparator = false;
                    builder.Append(lower);
                }
                else
                {
                    pendingSeparator = true;
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
        /// The short form, unless a different id in this directory already holds that path.
        /// </summary>
        private static string ResolveFilename(string directory, string id, string name)
        {
            var candidate = ElementFilename(id, name);
            var candidatePath = Path.Combine(directory, candidate);

            if (!File.Exists(candidatePath)) return candidate;

            // The path is taken. If it is taken by THIS element, that is not a collision -- it is
            // the ordinary case of rewriting a file in place.
            var occupant = IdInFile(candidatePath);
            if (occupant == null || string.Equals(occupant, id, StringComparison.Ordinal)) return candidate;

            return ElementFilenameFullId(id, name);
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
        /// Deletes files in this directory that hold <paramref name="id"/> and are not
        /// <paramref name="keep"/>.
        /// </summary>
        private static void RemoveOtherFilesFor(string directory, string id, string keep)
        {
            foreach (var file in FilesHoldingId(directory, id))
            {
                // Case-insensitive, because NTFS and APFS resolve two spellings to one entry: on
                // those filesystems, deleting "the other" file would delete the one just written.
                if (string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(file);
            }
        }

        /// <summary>The id inside a file, or null if it has none or cannot be read.</summary>
        /// <remarks>
        /// An unreadable neighbour is never an error here. A malformed file is not ours to delete,
        /// and it must not stop the element we were actually asked to write.
        /// </remarks>
        private static string IdInFile(string path)
        {
            try
            {
                return JObject.Parse(File.ReadAllText(path))["id"]?.ToString();
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
        /// newline, keys in the order they arrived.
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

            using (var writer = new JsonTextWriter(buffer)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',

                // Default already, pinned anyway: the reference implementation emits raw UTF-8 for
                // non-ASCII, and EscapeNonAscii here would make every accented name a byte-level
                // disagreement that no reader could see.
                StringEscapeHandling = StringEscapeHandling.Default,
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
    }
}
