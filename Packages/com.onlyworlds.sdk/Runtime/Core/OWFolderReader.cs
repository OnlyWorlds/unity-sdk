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
    /// Implements the OW Folder Format spec v0.3.7's reading rules. The rules that shape this code, all of which
    /// are easy to violate by accident:
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
    /// <item><b>Both spellings are accepted</b>: <c>map</c>/<c>map_id</c>, <c>zone</c>/<c>zone_id</c> (Atlas writes the
    /// <c>_id</c> forms on pins and markers), <c>time_current</c>/<c>time_range_current</c>. The bodies here stay as
    /// written; <see cref="WithSchemaLinkNames"/> is the schema's view, and it is what the cache is given.</item>
    /// </list>
    /// </remarks>
    public static class OWFolderReader
    {
        /// <summary>Types the dated <c>spatial/</c> layout ever held.</summary>
        private static readonly string[] LegacySpatialTypes = { "map", "pin", "zone", "marker" };

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
        /// There is no readable <c>world.json</c>, or it has no id. That is the one failure that
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
            for (var i = 0; i < files.Count; i++)
            {
                var (path, type, legacy) = files[i];
                ReadElement(path, texts[i], type, legacy ? result.LegacyElements : result.Elements, result.Skipped);
            }

            return result;
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

            JObject world;
            try
            {
                world = OWFolderWriter.Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException($"world.json in '{folderPath}' is not valid JSON.", e);
            }

            var id = world["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
            {
                throw new OWFolderFormatException(
                    $"world.json in '{folderPath}' has no id. Id and name are the only required "
                    + "keys, and a world without an id cannot be keyed or compared.");
            }

            return world;
        }

        private static void ReadElement(
            string file, string text, string typeFromDirectory,
            List<OWFolderElement> into, List<OWSkippedFile> skipped)
        {
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

            cache.Initialize(
                OWWorldKey.FromFolder(read.WorldId, folderPath),
                read.WorldName,
                readOnly: read.DeclaresReadOnly);

            cache.Clear();

            foreach (var element in read.Elements)
            {
                cache.Upsert(element.Id, element.Type,
                    OWFolderReader.WithSchemaLinkNames(element.Body).ToString(Newtonsoft.Json.Formatting.None));
            }

            // A legacy spatial/ file never replaces an id elements/ already gave: that is the older copy of a migrated file.
            var loaded = new HashSet<string>(read.Elements.Select(e => e.Id), StringComparer.Ordinal);
            foreach (var element in read.LegacyElements)
            {
                if (loaded.Contains(element.Id)) continue;
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
    }
}
