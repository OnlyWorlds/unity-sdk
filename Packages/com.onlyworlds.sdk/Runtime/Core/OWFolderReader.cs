using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>What a folder read found, including what it could not use.</summary>
    /// <remarks>
    /// A reader that reports only its successes is not reportable. Skipped files are part of the
    /// result, not an aside -- a world that silently loses one element to a malformed file looks
    /// identical to a world that never had it.
    /// </remarks>
    public class OWFolderReadResult
    {
        /// <summary>World metadata as it was on disk.</summary>
        public JObject World;

        /// <summary>Elements found under the normative <c>elements/&lt;type&gt;/</c> layout.</summary>
        public List<OWFolderElement> Elements = new List<OWFolderElement>();

        /// <summary>
        /// Elements found under the dated <c>spatial/{map,pin,zone,marker}/</c> layout.
        /// </summary>
        /// <remarks>
        /// Kept separate on purpose. A reader that covers only <c>elements/</c> conforms; this list
        /// lets a caller decide whether to accept the legacy path rather than having that choice
        /// made silently on its behalf.
        /// </remarks>
        public List<OWFolderElement> LegacyElements = new List<OWFolderElement>();

        /// <summary>Files that were not usable, and why. Never an error on its own.</summary>
        public List<OWSkippedFile> Skipped = new List<OWSkippedFile>();

        public string WorldId => World?["id"]?.ToString();
        public string WorldName => World?["name"]?.ToString();

        /// <summary>
        /// The onlyworlds.com world a linked folder is: <c>api.world_id</c> first, then <c>id</c> (older folders can
        /// differ). Null with neither. <see cref="WorldId"/> stays the folder's own id, which keys the cache.
        /// </summary>
        public string ServerWorldId
        {
            get
            {
                var apiWorld = (World?["api"] as JObject)?["world_id"];
                if (apiWorld != null && apiWorld.Type == JTokenType.String && apiWorld.ToString().Length > 0)
                    return apiWorld.ToString();
                return WorldId;
            }
        }

        /// <summary>
        /// The world's current time: <c>time_current</c>, else the wire's spelling <c>time_range_current</c>. Both are
        /// accepted forever and <c>time_current</c> wins (spec §5, as the Python package reads it). Null when neither is
        /// present; an explicit JSON null comes back as a null token, never as 0.
        /// </summary>
        public JToken TimeCurrent =>
            World == null ? null
            : World.TryGetValue("time_current", out var t) ? t
            : World.TryGetValue("time_range_current", out var w) ? w
            : null;

        /// <summary>True when the world declares an api block -- the sync switch.</summary>
        /// <remarks>Absent means local-only, and no sync machinery may attach.</remarks>
        public bool IsSyncable => World?["api"] != null && World["api"].Type != JTokenType.Null;

        /// <summary>
        /// True when the folder declares itself a frozen snapshot.
        /// </summary>
        /// <remarks>
        /// Advisory in the spec, and a label rather than a lock. Never let this be the thing
        /// protecting the bytes: not writing unless asked is a property of the reader, and it has
        /// to hold for the folders that carry no marker at all -- which is nearly all of them.
        /// </remarks>
        public bool DeclaresReadOnly =>
            World?["writable"] != null && World["writable"].Type == JTokenType.Boolean
                                       && !World["writable"].Value<bool>();

        public bool IsSnapshot => World?["snapshot_of"] != null;

        /// <summary>
        /// Ids of the known-of stubs among <see cref="Elements"/> and <see cref="LegacyElements"/> (§4.4,
        /// <c>known_of: true</c>), sorted ordinally: elements this folder knows of but does not hold whole.
        /// </summary>
        /// <remarks>
        /// A stub's absent fields are unknown, not empty: never read them as values or as a deletion, and never
        /// push one. The bodies are as on disk; the reader fills nothing in.
        /// </remarks>
        public List<string> KnownOf
        {
            get
            {
                var ids = Elements.Concat(LegacyElements).Where(e => e.KnownOf).Select(e => e.Id).ToList();
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }
    }

    /// <summary>One element as it sits on disk.</summary>
    public struct OWFolderElement
    {
        /// <summary>The element's id, from the body. Identity lives in the body, not the filename.</summary>
        public string Id;

        /// <summary>
        /// Type, taken from the body when it declares one, else inferred from the directory.
        /// </summary>
        public string Type;

        /// <summary>True when the type came from the directory rather than the body.</summary>
        public bool TypeInferred;

        /// <summary>The element body, verbatim.</summary>
        public JObject Body;

        /// <summary>Absolute path this element was read from.</summary>
        public string Path;

        /// <summary>
        /// True for a known-of stub (<c>known_of: true</c>, §4.4): known of, never whole. Its absent fields are
        /// unknown, not empty, and it is never pushed.
        /// </summary>
        public bool KnownOf => OWPayload.IsKnownOf(Body);
    }

    /// <summary>A file the reader could not use.</summary>
    public struct OWSkippedFile
    {
        public string Path;
        public string Reason;
    }

    /// <summary>
    /// Reads an OnlyWorlds world folder from disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the OW Folder Format spec's reading rules through the version <see cref="OWFolderExport.FormatVersion"/>
    /// declares. Since v0.3.8 those that reach a reader: the world's provenance keys (<c>source</c>, <c>exported_as</c>,
    /// <c>producer</c>, <c>origin</c>: v0.3.9, v0.3.14, v0.3.16) kept as written, a known-of stub read as one (v0.3.10),
    /// <c>records/</c> left to the producers that know it (v0.3.12); the archive is <see cref="OWWorldArchive"/>'s
    /// (v0.3.13); a producer's <c>input</c> word it does not know never refused (v0.3.17, v0.3.18, which only grew the list). The
    /// rest (v0.3.11, v0.3.15) bind writers. The rules that shape this code, all of which are easy to
    /// violate by accident:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Opening is not editing.</b> This class only reads. It creates no directories, no
    /// dot-folders, no marker files, and never rewrites a file it opened -- including to
    /// "normalise" a key spelling. Migration is a deliberate act, never a consequence of reading.
    /// A tool that tidies on open produces a dirty repository authored by nobody.</item>
    /// <item><b>Only <c>id</c> is required</b> on an element, and only <c>id</c> + <c>name</c> on
    /// the world. A file without an id is not an element: skip it, leave it alone, report it.</item>
    /// <item><b>One bad file must never take out the world.</b> A truncated file is skipped and
    /// every other element still serves.</item>
    /// <item><b>Never synthesize.</b> No invented timestamps, no placeholder names, no
    /// backfilled types. An empty-string name is a legal name and a different claim from unnamed;
    /// an explicit null means UNSET and is never 0 or "".</item>
    /// <item><b>A missing type directory means no elements of that type</b>, never an error.</item>
    /// <item><b>A tool's litter is never an element</b> (§4): a dot-prefixed folder under <c>elements/</c> (<c>.atlas/</c>)
    /// is never read, and a dot-prefixed file in a type directory (an editor's swap file, a sync client's <c>._</c>
    /// twin) is skipped and reported, whatever it holds.</item>
    /// <item><b>Both spellings are accepted</b>: <c>map</c>/<c>map_id</c>, <c>zone</c>/<c>zone_id</c> (Atlas writes the
    /// <c>_id</c> forms on pins and markers), <c>time_current</c>/<c>time_range_current</c>. The bodies here stay as
    /// written; <see cref="WithSchemaLinkNames"/> is the schema's view, and it is what the cache is given.</item>
    /// </list>
    /// </remarks>
    public static class OWFolderReader
    {
        /// <summary>Types the dated <c>spatial/</c> layout ever held.</summary>
        internal static readonly string[] LegacySpatialTypes = { "map", "pin", "zone", "marker" };

        /// <summary>A dot-prefixed name: a tool's private state or litter, never the world (§4).</summary>
        private static bool IsDotPrefixed(string name) => name.StartsWith(".", StringComparison.Ordinal);

        /// <summary>
        /// Reads a world folder. Never writes, never throws for a malformed element.
        /// </summary>
        /// <param name="folderPath">Directory containing <c>world.json</c>.</param>
        /// <param name="includeLegacySpatial">
        /// Also read the dated <c>spatial/</c> layout into
        /// <see cref="OWFolderReadResult.LegacyElements"/>. A reader that leaves this off is
        /// conforming.
        /// </param>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        /// <exception cref="OWFolderFormatException">
        /// There is no readable <c>world.json</c>, or it has no id (a non-empty string). That is the one failure that
        /// makes the whole folder unreadable -- everything else degrades to a skipped file.
        /// </exception>
        public static OWFolderReadResult Read(string folderPath, bool includeLegacySpatial = true)
        {
            if (string.IsNullOrEmpty(folderPath)) throw new ArgumentNullException(nameof(folderPath));

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"No world folder at '{folderPath}'.");
            }

            var result = new OWFolderReadResult { World = ReadWorldJson(folderPath) };

            // Every file is listed first, in the order the serial reader visited them, so the result's
            // order and its skipped list are the same as they always were.
            var files = new List<(string path, string type, bool legacy)>();
            var elementsRoot = Path.Combine(folderPath, "elements");
            if (Directory.Exists(elementsRoot))
            {
                foreach (var typeDir in Directory.GetDirectories(elementsRoot))
                {
                    var type = Path.GetFileName(typeDir);
                    if (IsDotPrefixed(type)) continue;
                    foreach (var file in Directory.GetFiles(typeDir, "*.json")) files.Add((file, type, false));
                }
            }

            if (includeLegacySpatial)
            {
                var spatialRoot = Path.Combine(folderPath, "spatial");
                if (Directory.Exists(spatialRoot))
                {
                    foreach (var type in LegacySpatialTypes)
                    {
                        var dir = Path.Combine(spatialRoot, type);
                        if (!Directory.Exists(dir)) continue;

                        foreach (var file in Directory.GetFiles(dir, "*.json")) files.Add((file, type, true));
                    }
                }
            }

            var texts = ReadAllTexts(files);
            Assemble(result, files, texts);
            return result;
        }

        /// <summary>
        /// Reads a world held in memory: each file's path inside the world and its text. The folder read without the
        /// folder, for a world that arrives as a zip, a download or a test.
        /// </summary>
        /// <param name="entries">
        /// Path (relative to the world, <c>/</c> or <c>\</c>) to text. A null text is a file that could not be read,
        /// and is skipped as the folder form skips one. Paths the folder form would not read are ignored.
        /// </param>
        /// <param name="includeLegacySpatial">As <see cref="Read(string, bool)"/>.</param>
        /// <param name="source">Names the world in error messages (an archive's path, say). Never stored.</param>
        /// <returns>
        /// The same result <see cref="Read(string, bool)"/> gives for a folder holding these files, with each
        /// element's and skipped file's <c>Path</c> the entry's path, forward slashes.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The same rules as the folder form, by the same code: one <c>world.json</c> at the root, files directly in
        /// <c>elements/&lt;type&gt;/</c> and (when asked) <c>spatial/{map,pin,zone,marker}/</c> ending
        /// <c>.json</c>, every element parsed and every spelling kept as the folder form keeps it, and an id in both
        /// layouts served from <c>elements/</c>. Names match as written (case-sensitive), as on a case-sensitive disk.
        /// </para>
        /// <para>
        /// Order: type folders and files in ordinal order, <c>spatial/</c> after <c>elements/</c> in the folder
        /// form's type order. A folder's order is its file system's, so the two agree wherever that is ordinal for
        /// the names in hand (lowercase slugs on NTFS, as written by every OnlyWorlds tool).
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">Two entries with the same path.</exception>
        /// <exception cref="OWFolderFormatException">
        /// No <c>world.json</c>, it is not JSON, or it has no id (a non-empty string).
        /// </exception>
        public static OWFolderReadResult ReadEntries(
            IEnumerable<KeyValuePair<string, string>> entries, bool includeLegacySpatial = true, string source = null)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var where = string.IsNullOrEmpty(source) ? "(entries)" : source;

            var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var path = (entry.Key ?? string.Empty).Replace('\\', '/');
                if (byPath.ContainsKey(path)) throw new ArgumentException($"Two entries named '{path}' in {where}.", nameof(entries));
                byPath.Add(path, entry.Value);
            }

            if (!byPath.TryGetValue("world.json", out var worldText))
                throw new OWFolderFormatException($"No world.json in '{where}'.");

            var result = new OWFolderReadResult { World = ParseWorldJson(worldText, where) };

            var elements = new List<(string path, string type)>();
            var legacy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in byPath.Keys)
            {
                var parts = path.Split('/');
                if (parts.Length != 3 || parts[1].Length == 0 || !parts[2].EndsWith(".json", StringComparison.Ordinal)) continue;

                if (parts[0] == "elements")
                {
                    if (!IsDotPrefixed(parts[1])) elements.Add((path, parts[1]));
                }
                else if (includeLegacySpatial && parts[0] == "spatial" && Array.IndexOf(LegacySpatialTypes, parts[1]) >= 0)
                {
                    if (!legacy.TryGetValue(parts[1], out var list)) legacy[parts[1]] = list = new List<string>();
                    list.Add(path);
                }
            }

            // The folder form visits elements/ type by type, then spatial/ in LegacySpatialTypes' order.
            elements.Sort((a, b) =>
            {
                var byType = string.CompareOrdinal(a.type, b.type);
                return byType != 0 ? byType : string.CompareOrdinal(a.path, b.path);
            });

            var files = elements.Select(e => (e.path, e.type, false)).ToList();
            foreach (var type in LegacySpatialTypes)
            {
                if (!legacy.TryGetValue(type, out var list)) continue;
                list.Sort(StringComparer.Ordinal);
                files.AddRange(list.Select(p => (p, type, true)));
            }

            Assemble(result, files, files.Select(f => byPath[f.Item1]).ToArray());
            return result;
        }

        /// <summary>
        /// Parses every listed file into the result, then serves an id found in both layouts from <c>elements/</c>.
        /// Shared by the folder and the in-memory form, so the two cannot drift apart.
        /// </summary>
        private static void Assemble(
            OWFolderReadResult result, List<(string path, string type, bool legacy)> files, IReadOnlyList<string> texts)
        {
            for (var i = 0; i < files.Count; i++)
            {
                var (path, type, legacy) = files[i];
                ReadElement(path, texts[i], type, legacy ? result.LegacyElements : result.Elements, result.Skipped);
            }

            // §4 (v0.3.8, one id, one file): an id in both layouts is served from elements/, and the spatial/ copy is
            // logged, never deleted (it is not ours to tidy).
            if (result.LegacyElements.Count > 0)
            {
                var current = new HashSet<string>(result.Elements.Select(e => e.Id), StringComparer.Ordinal);
                foreach (var e in result.LegacyElements)
                {
                    if (current.Contains(e.Id))
                        result.Skipped.Add(new OWSkippedFile { Path = e.Path, Reason = "same id as a file in elements/, which is served" });
                }
                result.LegacyElements.RemoveAll(e => current.Contains(e.Id));
            }
        }

        /// <summary>The disk spellings a folder may use for a schema link, and the schema's name for each.</summary>
        private static readonly (string disk, string schema)[] LinkSpellings = { ("map_id", "map"), ("zone_id", "zone") };

        /// <summary>
        /// A body with its links under the schema's names: <c>map_id</c> read as <c>map</c>, <c>zone_id</c> as
        /// <c>zone</c>, each in its own place, when the schema's name is not already there (it wins when both are).
        /// Returns <paramref name="body"/> itself when nothing differs, else a copy: the body read from disk is
        /// never changed, so opening is still not editing.
        /// </summary>
        public static JObject WithSchemaLinkNames(JObject body)
        {
            if (body == null) return null;

            JObject copy = null;
            foreach (var (disk, schema) in LinkSpellings)
            {
                if (!body.ContainsKey(disk) || body.ContainsKey(schema)) continue;

                copy ??= (JObject)body.DeepClone();
                var old = copy.Property(disk);
                old.AddBeforeSelf(new JProperty(schema, old.Value));
                old.Remove();
            }

            return copy ?? body;
        }

        /// <summary>Most readers at once. Past 8 the disk, not the threads, sets the pace.</summary>
        private const int ReadThreads = 8;

        /// <summary>
        /// Each file's text, by index; null where the read failed. The reads run on up to
        /// <see cref="ReadThreads"/> threads; parsing stays on the caller's thread.
        /// </summary>
        /// <remarks>
        /// Reading is half of a big world's open: 18,716 files one at a time took 1.3-1.5 s (Windrose,
        /// 2026-09-30). Parsing is not parallelised on purpose: under Unity's Mono GC a parallel parse
        /// measured 2-6x slower than one thread, because every parser allocates into the same heap.
        /// A WebGL player has no threads, so it reads one at a time.
        /// </remarks>
        private static string[] ReadAllTexts(List<(string path, string type, bool legacy)> files)
        {
            var texts = new string[files.Count];
#if UNITY_WEBGL && !UNITY_EDITOR
            for (var i = 0; i < files.Count; i++) texts[i] = TryReadText(files[i].path);
#else
            System.Threading.Tasks.Parallel.For(0, files.Count,
                new System.Threading.Tasks.ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Math.Min(ReadThreads, Environment.ProcessorCount)),
                },
                i => texts[i] = TryReadText(files[i].path));
#endif
            return texts;
        }

        private static string TryReadText(string path)
        {
            try { return File.ReadAllText(path); }
            catch (Exception) { return null; }
        }

        private static JObject ReadWorldJson(string folderPath)
        {
            var path = Path.Combine(folderPath, "world.json");

            if (!File.Exists(path))
            {
                throw new OWFolderFormatException($"No world.json in '{folderPath}'.");
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException($"world.json in '{folderPath}' is not valid JSON.", e);
            }

            return ParseWorldJson(text, folderPath);
        }

        /// <summary>world.json's text to its object, refusing one that is not JSON or has no id.</summary>
        /// <param name="where">The folder (or source) named in the message.</param>
        private static JObject ParseWorldJson(string text, string where)
        {
            JObject world;
            try
            {
                if (text == null) throw new IOException("world.json could not be read.");
                world = OWFolderWriter.Parse(text);
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException($"world.json in '{where}' is not valid JSON.", e);
            }

            if (WorldIdOf(world) == null)
            {
                throw new OWFolderFormatException(
                    $"world.json in '{where}' has no id (a non-empty string). Id and name are the only required "
                    + "keys, and a world without an id cannot be keyed or compared.");
            }

            return world;
        }

        /// <summary>
        /// world.json's id when it is one: a string that is not blank. Null for no id, a blank one, or one of any
        /// other JSON type.
        /// </summary>
        /// <remarks>
        /// The spec requires "<c>id</c> (non-empty string)" (folder spec §4.1), and the TypeScript reader refuses
        /// the rest (<c>world-file.ts</c>'s <c>parseWorld</c>: a string, not blank once trimmed). Read as text,
        /// <c>7</c> would key the world as <c>"7"</c> and <c>"  "</c> as a blank, and neither is an id another tool
        /// keys the same world by. The folder read, the archive read and the archive's pack all ask this one question.
        /// </remarks>
        internal static string WorldIdOf(JObject world)
        {
            var id = world?["id"];
            if (id == null || id.Type != JTokenType.String) return null;
            var text = (string)id;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static void ReadElement(
            string file, string text, string typeFromDirectory,
            List<OWFolderElement> into, List<OWSkippedFile> skipped)
        {
            if (IsDotPrefixed(Path.GetFileName(file)))
            {
                // §4: a tool's litter, never an element, even with a valid body and a real element's id.
                skipped.Add(new OWSkippedFile { Path = file, Reason = "a dot-prefixed file: a tool's litter, not an element" });
                return;
            }

            JObject body;
            try
            {
                if (text == null) throw new IOException($"'{file}' could not be read.");
                body = OWFolderWriter.Parse(text);
            }
            catch (Exception)
            {
                // Unreadable, truncated, empty, or not JSON at all. Skip it, LEAVE IT ON DISK, and
                // keep serving everything else -- one malformed file taking out the whole world is
                // a real failure mode, not a hypothetical.
                skipped.Add(new OWSkippedFile { Path = file, Reason = "unparseable JSON" });
                return;
            }

            var id = body["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
            {
                // Identity lives in the body. A file without an id is not an element -- and it
                // is emphatically not ours to rename, backfill or delete.
                skipped.Add(new OWSkippedFile { Path = file, Reason = "no id in body" });
                return;
            }

            // The body wins over the directory when it declares a type: a file may legitimately
            // sit in the wrong folder, and the declaration is the stronger claim. Fill from the
            // directory only when the body is silent, and never write the inference back.
            var declared = body["type"]?.ToString();
            var inferred = string.IsNullOrEmpty(declared);

            into.Add(new OWFolderElement
            {
                Id = id,
                Type = inferred ? typeFromDirectory : declared,
                TypeInferred = inferred,
                Body = body,
                Path = file,
            });
        }
    }

    /// <summary>Loads a folder world into a cache.</summary>
    public static class OWFolderLoader
    {
        /// <summary>
        /// Reads a world folder and fills a cache with it.
        /// </summary>
        /// <param name="cache">Cache to fill. Its contents are replaced.</param>
        /// <param name="folderPath">Directory containing <c>world.json</c>.</param>
        /// <param name="includeLegacySpatial">Also load the dated <c>spatial/</c> layout.</param>
        /// <returns>The read result, including everything that was skipped.</returns>
        /// <remarks>
        /// <para>
        /// The cache is keyed <c>(Folder, worldId, folderPath)</c>, which is the whole reason the
        /// key carries a source: two snapshots of the same world -- chapter 1 and chapter 12 --
        /// share a world id, and a world-id-keyed store opens the wrong one with a correct-looking
        /// UI and no warning. Demonstrated live in Atlas before either of us had built on it.
        /// </para>
        /// <para>
        /// A folder that declares <c>writable: false</c> produces a read-only cache, so a later
        /// sync refuses rather than overwriting a capture. The refusal is the belt; the flag is
        /// only the label on it.
        /// </para>
        /// </remarks>
        public static OWFolderReadResult LoadInto(
            OWWorldCache cache, string folderPath, bool includeLegacySpatial = true)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            var read = OWFolderReader.Read(folderPath, includeLegacySpatial);

            return LoadInto(cache, read, OWWorldKey.FromFolder(read.WorldId, folderPath));
        }

        /// <summary>
        /// Fills a cache from a read that has already been done, so a caller that needed the read
        /// for something else (an archive, a report) does not pay for it twice.
        /// </summary>
        /// <param name="cache">Cache to fill. Its contents are replaced.</param>
        /// <param name="read">A folder read's result. Its legacy elements are loaded when it holds any.</param>
        /// <param name="key">
        /// The cache's identity. The caller chooses it because the read does not know where it came
        /// from: <see cref="LoadInto(OWWorldCache, string, bool)"/> passes
        /// <c>OWWorldKey.FromFolder(worldId, folderPath)</c>; an imported archive passes a key whose
        /// origin is its asset's GUID (<c>OWWorldArchive.KeyFor</c>), never a machine's absolute path.
        /// </param>
        /// <param name="readOnly">
        /// Null: the folder's own <c>writable: false</c> decides, as the path form does. True: read-only
        /// whatever the folder says (a capture, such as an archive or a download).
        /// </param>
        /// <returns><paramref name="read"/>, so the call reads like the path form.</returns>
        public static OWFolderReadResult LoadInto(
            OWWorldCache cache, OWFolderReadResult read, OWWorldKey key, bool? readOnly = null)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (read == null) throw new ArgumentNullException(nameof(read));

            cache.Initialize(
                key,
                read.WorldName,
                readOnly: readOnly ?? read.DeclaresReadOnly);

            cache.Clear();

            foreach (var element in read.Elements)
            {
                cache.Upsert(element.Id, element.Type,
                    OWFolderReader.WithSchemaLinkNames(element.Body).ToString(Newtonsoft.Json.Formatting.None));
            }

            // The reader already dropped (and logged) a spatial/ copy of an id elements/ holds (§4, v0.3.8).
            foreach (var element in read.LegacyElements)
            {
                cache.Upsert(element.Id, element.Type,
                    OWFolderReader.WithSchemaLinkNames(element.Body).ToString(Newtonsoft.Json.Formatting.None));
            }

            return read;
        }
    }

    /// <summary>The folder itself is unreadable -- not merely one element within it.</summary>
    public class OWFolderFormatException : Exception
    {
        public OWFolderFormatException(string message) : base(message) { }
        public OWFolderFormatException(string message, Exception inner) : base(message, inner) { }

        /// <summary>An archive's refusal, with its cause (one of <see cref="OWWorldArchiveReason"/>).</summary>
        internal OWFolderFormatException(string reason, string message, Exception inner = null) : base(message, inner)
        {
            Reason = reason;
        }

        /// <summary>
        /// Why an <c>.owworld</c> was refused, by the cause's name in folder spec §4.7's shared list (the values of
        /// <see cref="OWWorldArchiveReason"/>, such as <c>"unsafe_path"</c> or <c>"collision"</c>); null for a
        /// folder's refusal. <see cref="OWWorldArchive.Read"/> and <c>LoadInto</c> always set it.
        /// </summary>
        public string Reason { get; }
    }
}
