using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A world folder as one file: the spec's world zip, named <c>.owworld</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder format names its one-file form: "one world per zip, as one top-level folder; readers also accept
    /// <c>world.json</c> at the root". An <c>.owworld</c> is exactly that zip, renamed, so Unity can give it an
    /// importer of its own without claiming every <c>.zip</c> in a project. One file is one declared input: Unity
    /// re-imports it when it changes, and nothing is ever written into a folder another tool owns (a world folder
    /// dropped into <c>Assets/</c> would get a <c>.meta</c> beside every file, making Unity a second writer of it).
    /// </para>
    /// <para>
    /// Everything here is a plain static over files, so it is testable without the Editor; the importer and the menu
    /// are thin shells over it. It lives in the runtime assembly because the zip API is part of .NET Standard 2.1 and
    /// needs no new reference, so a desktop player can open an <c>.owworld</c> the same way.
    /// </para>
    /// <para>
    /// <b>The rule: what an archive holds.</b> Exactly <c>world.json</c>, <c>elements/</c>, <c>spatial/</c>,
    /// <c>media/</c> and <c>records/</c>, the world data the folder format names (<c>spatial/</c> being its dated
    /// layout, which most real worlds still use; <c>records/</c> its non-element world data, folder spec §4.5). An
    /// allowlist, because a world folder in a repository carries more than the world: a README, a
    /// <c>.gitignore</c>, a tool's dot-directory. Inside the content folders every dot-folder and dot-file (a tool's
    /// private state) and every Unity <c>.meta</c> is left out too. <c>records/</c> travels whole and byte for byte
    /// (§4.5 rules 1 and 6: a tool that does not know a producer never drops its files), dot-files included; only
    /// Unity's own <c>.meta</c> beside a file is left out there. The importer reads none of it: records are never
    /// elements (rule 4).
    /// </para>
    /// <para>
    /// <b>The rule: no key travels.</b> <c>world.json</c>'s <c>api</c> block is dropped unless the caller asks to keep
    /// it. That block holds an API key, and an archive is made to be shared: committed with a project, sent, shipped
    /// in a build. The spec's rule for a copy to share is this one: it "may drop the <c>api</c> block, which holds a
    /// key, and nothing else". Nothing else in <c>world.json</c> changes.
    /// </para>
    /// <para>
    /// <b>Known gap, media.</b> <see cref="Pack"/> carries <c>media/</c> (it is world content), but
    /// <see cref="Read"/> leaves it in the archive: the cache holds elements only, so an imported world has no media
    /// yet. The archive keeps it for the day the cache, or a sibling asset, does.
    /// </para>
    /// </remarks>
    public static class OWWorldArchive
    {
        /// <summary>The file extension, without its dot.</summary>
        public const string Extension = "owworld";

        /// <summary>
        /// The time stamped on every entry: the zip format's own epoch, 1980-01-01 00:00.
        /// </summary>
        /// <remarks>
        /// A file's mtime is not part of a world. Stamping it would make the same world pack to different bytes on
        /// every checkout (git sets mtimes to the checkout time), which is noise in version control and a needless
        /// re-import. The offset is irrelevant: a zip stores the clock reading, and this one reads the same anywhere.
        /// </remarks>
        public static readonly DateTimeOffset EntryTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private const string WorldFile = "world.json";

        /// <summary>The folders an archive holds beside <c>world.json</c> (the class's first rule).</summary>
        internal static readonly string[] ContentFolders = { "elements", "spatial", "media" };

        /// <summary>Non-element world data (folder spec §4.5): packed whole, never read by the importer.</summary>
        internal const string RecordsFolder = "records";

        // -- Pack ---------------------------------------------------------------

        /// <summary>
        /// Packs a world folder into an <c>.owworld</c>. Writes only the archive; never touches the folder.
        /// </summary>
        /// <param name="worldFolder">Directory containing <c>world.json</c>.</param>
        /// <param name="archivePath">The archive to write. Replaced if it exists. Must not be inside the folder.</param>
        /// <param name="keepApiBlock">
        /// Keep <c>world.json</c>'s <c>api</c> block. Off by default: that block holds an API key, and an archive is
        /// a copy made to be shared -- committed, sent, shipped. The spec's rule for a copy to share is exactly this:
        /// it "may drop the <c>api</c> block, which holds a key, and nothing else". A <c>world.json</c> with no
        /// <c>api</c> block goes in byte for byte; one that loses it is re-emitted in the format's own bytes.
        /// </param>
        /// <returns>The entry names written, in order.</returns>
        /// <remarks>
        /// <para>
        /// Inside the archive the world sits in one top-level folder named after <paramref name="worldFolder"/>, so
        /// extracting it by hand gives back a recognisable folder. Every dot-folder and dot-file is left out (a
        /// tool's private state: <c>.atlas/</c>, <c>.git/</c>, <c>.ow/</c>), and so is every Unity <c>.meta</c>
        /// (present when the folder sits under <c>Assets/</c>, and never the world's); <c>records/</c> goes in whole
        /// (see the class's first rule).
        /// </para>
        /// <para>
        /// Deterministic: entries in ordinal path order, each stamped <see cref="EntryTime"/>, so the same folder
        /// packs to the same bytes whatever the file system's listing order or the files' mtimes. Same bytes across
        /// runtimes is not claimed: the deflate stream is the runtime's zlib.
        /// </para>
        /// </remarks>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        /// <exception cref="OWFolderFormatException">No readable <c>world.json</c>, or it has no id.</exception>
        /// <exception cref="ArgumentException"><paramref name="archivePath"/> is inside the folder.</exception>
        public static List<string> Pack(string worldFolder, string archivePath, bool keepApiBlock = false)
        {
            if (string.IsNullOrEmpty(worldFolder)) throw new ArgumentNullException(nameof(worldFolder));
            if (string.IsNullOrEmpty(archivePath)) throw new ArgumentNullException(nameof(archivePath));

            var root = FullDirectory(worldFolder);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"No world folder at '{worldFolder}'.");

            var archive = Path.GetFullPath(archivePath);
            if (IsUnder(archive, root))
            {
                throw new ArgumentException(
                    $"The archive '{archivePath}' would be written inside the world folder it packs. Pack writes "
                    + "only the archive, never into the folder.", nameof(archivePath));
            }

            var worldBytes = WorldJsonBytes(root, keepApiBlock);
            var plan = PackPlan(root);

            var top = Path.GetFileName(root);
            if (string.IsNullOrEmpty(top)) top = "world";

            var dir = Path.GetDirectoryName(archive);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var written = new List<string>(plan.Count);
            using (var stream = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var (relative, source) in plan)
                {
                    var name = top + "/" + relative;
                    var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    entry.LastWriteTime = EntryTime;

                    var bytes = relative == WorldFile ? worldBytes : File.ReadAllBytes(source);
                    using (var output = entry.Open()) output.Write(bytes, 0, bytes.Length);

                    written.Add(name);
                }
            }

            return written;
        }

        /// <summary>
        /// What <see cref="Pack"/> would write: each file's path inside the world (forward slashes) and on disk, in
        /// ordinal path order. Reads directory listings only.
        /// </summary>
        internal static List<(string relative, string source)> PackPlan(string worldFolder)
        {
            var root = FullDirectory(worldFolder);
            var plan = new List<(string relative, string source)> { (WorldFile, Path.Combine(root, WorldFile)) };

            foreach (var folder in ContentFolders)
            {
                var dir = Path.Combine(root, folder);
                if (Directory.Exists(dir)) Collect(dir, folder, plan);
            }

            var records = Path.Combine(root, RecordsFolder);
            if (Directory.Exists(records)) CollectRecords(records, RecordsFolder, plan);

            // Ordinal, never the file system's order: NTFS lists case-insensitively, ext4 in hash order, and the
            // archive's bytes must not depend on which one packed it.
            plan.Sort((a, b) => string.CompareOrdinal(a.relative, b.relative));
            return plan;
        }

        private static void Collect(string dir, string relative, List<(string relative, string source)> into)
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (IsToolLitter(name) || name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                into.Add((relative + "/" + name, file));
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (IsToolLitter(name)) continue;
                Collect(sub, relative + "/" + name, into);
            }
        }

        /// <summary>
        /// <c>records/</c> whole: a producer's files are its own, dot-files included (§4.5 rule 6). Only Unity's
        /// <c>.meta</c> for a file or folder beside it is left out, since Unity writes it, not a producer.
        /// </summary>
        private static void CollectRecords(string dir, string relative, List<(string relative, string source)> into)
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (IsUnityMeta(dir, name)) continue;
                into.Add((relative + "/" + name, file));
            }

            foreach (var sub in Directory.GetDirectories(dir))
                CollectRecords(sub, relative + "/" + Path.GetFileName(sub), into);
        }

        private static bool IsUnityMeta(string dir, string name)
        {
            if (!name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
            var owner = Path.Combine(dir, name.Substring(0, name.Length - 5));
            return File.Exists(owner) || Directory.Exists(owner);
        }

        /// <summary>A dot-prefixed name: a tool's private state or litter, never the world (spec, Rules for Tools).</summary>
        private static bool IsToolLitter(string name) => name.StartsWith(".", StringComparison.Ordinal);

        private static byte[] WorldJsonBytes(string root, bool keepApiBlock)
        {
            var path = Path.Combine(root, WorldFile);
            if (!File.Exists(path)) throw new OWFolderFormatException($"No world.json in '{root}'.");

            var bytes = File.ReadAllBytes(path);
            JObject world;
            try
            {
                world = OWFolderWriter.Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException($"world.json in '{root}' is not valid JSON.", e);
            }

            if (string.IsNullOrEmpty(world["id"]?.ToString()))
                throw new OWFolderFormatException($"world.json in '{root}' has no id, so it cannot be packed as a world.");

            if (keepApiBlock || world.Property("api") == null) return bytes;

            world.Remove("api");
            return new UTF8Encoding(false).GetBytes(OWFolderWriter.Serialize(world));
        }

        /// <summary>
        /// A default file name for a folder's archive: <c>&lt;world name slug&gt;.owworld</c>, else the id's tail.
        /// </summary>
        /// <exception cref="OWFolderFormatException">No readable <c>world.json</c>, or it has no id.</exception>
        public static string DefaultArchiveName(string worldFolder)
        {
            var root = FullDirectory(worldFolder);
            var path = Path.Combine(root, WorldFile);
            if (!File.Exists(path)) throw new OWFolderFormatException($"No world.json in '{root}'.");

            JObject world;
            try { world = OWFolderWriter.ReadJsonFile(path); }
            catch (Exception e) { throw new OWFolderFormatException($"world.json in '{root}' is not valid JSON.", e); }

            var id = world["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) throw new OWFolderFormatException($"world.json in '{root}' has no id.");

            var slug = OWFolderWriter.Slugify(world["name"]?.ToString() ?? string.Empty);
            return (string.IsNullOrEmpty(slug) ? OWFolderWriter.IdTail(id) : slug) + "." + Extension;
        }

        // -- Read ---------------------------------------------------------------

        /// <summary>
        /// Where the world sits in an archive: <c>""</c> for <c>world.json</c> at the root, else <c>"name/"</c>, the
        /// one top-level folder holding it.
        /// </summary>
        /// <param name="entryNames">Every entry name in the archive.</param>
        /// <exception cref="OWFolderFormatException">
        /// No world, or more than one. Never a guess: a zip with two worlds in it opened as one of them is the wrong
        /// world with a correct-looking UI, the failure the cache's key exists to prevent.
        /// </exception>
        /// <remarks>
        /// Only <c>world.json</c> directly at the root or directly in a top-level folder counts; a top-level
        /// dot-folder is a tool's, never a world. Anything else beside the world (a README) is ignored.
        /// </remarks>
        public static string FindWorldRoot(IEnumerable<string> entryNames)
        {
            if (entryNames == null) throw new ArgumentNullException(nameof(entryNames));

            var roots = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var raw in entryNames)
            {
                var name = NormalizeEntry(raw);
                if (name == WorldFile)
                {
                    roots.Add(string.Empty);
                    continue;
                }

                var parts = name.Split('/');
                // records/ is a world's own data, never a second world (a producer may keep a world.json there).
                if (parts.Length == 2 && parts[1] == WorldFile && parts[0].Length > 0 && !IsToolLitter(parts[0])
                    && parts[0] != RecordsFolder)
                    roots.Add(parts[0] + "/");
            }

            if (roots.Count == 1) return roots.Min;

            if (roots.Count == 0)
            {
                throw new OWFolderFormatException(
                    "No world.json in the archive. An .owworld holds one world: world.json at its root, or one "
                    + "top-level folder holding world.json.");
            }

            var named = string.Join(", ", roots.Select(r => r.Length == 0 ? "(root)" : r.TrimEnd('/')));
            throw new OWFolderFormatException(
                $"The archive holds {roots.Count} worlds ({named}). One world per archive: pack each on its own.");
        }

        /// <summary>
        /// Reads an <c>.owworld</c> in memory: unzips the world's <c>world.json</c>, <c>elements/</c> and
        /// <c>spatial/</c> entries and reads them with <see cref="OWFolderReader.ReadEntries"/>, by the folder read's
        /// own rules and code. Writes nothing anywhere.
        /// </summary>
        /// <param name="archivePath">The archive on disk.</param>
        /// <param name="includeLegacySpatial">As <see cref="OWFolderReader.Read(string, bool)"/>: also read <c>spatial/</c>.</param>
        /// <returns>
        /// The folder read's result, with each element's and skipped file's <c>Path</c> its path inside the world
        /// (<c>elements/character/x.json</c>, forward slashes).
        /// </returns>
        /// <remarks>
        /// <para>
        /// In memory because extracting to disk was nearly the whole cost: Tuvsh Ul's 3,832 entries took 5.5-8 s
        /// through a temp folder, against 0.33 s to unzip in memory (harness, 2026-10-20). <c>media/</c> stays in
        /// the zip: nothing reads it yet.
        /// </para>
        /// <para>
        /// Every entry name is checked before anything is read. One that is absolute (<c>/x</c>, <c>C:/x</c>) or
        /// climbs out of the archive (<c>../x</c>) refuses the whole archive; so does one written under the world's
        /// folder that climbs out of it (<c>w/../x</c>), and a second entry of one name in the world. An
        /// archive someone sent is untrusted input, and an importer runs on it unasked: nothing is written, but a
        /// hostile name must not become an element's path.
        /// </para>
        /// </remarks>
        /// <exception cref="FileNotFoundException">The archive does not exist.</exception>
        /// <exception cref="OWFolderFormatException">
        /// Not a zip; an absolute, climbing or repeated entry name; no world or more than one in it; or the world's
        /// own <c>world.json</c> is unreadable.
        /// </exception>
        public static OWFolderReadResult Read(string archivePath, bool includeLegacySpatial = true)
        {
            if (string.IsNullOrEmpty(archivePath)) throw new ArgumentNullException(nameof(archivePath));
            if (!File.Exists(archivePath)) throw new FileNotFoundException($"No archive at '{archivePath}'.", archivePath);

            var stream = File.OpenRead(archivePath);
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            }
            catch (InvalidDataException e)
            {
                stream.Dispose();
                throw new OWFolderFormatException($"'{archivePath}' is not a zip archive.", e);
            }

            using (zip)
            {
                // Every name is checked against the archive first, before the world is even found.
                var named = new List<(ZipArchiveEntry entry, string written, string resolved)>(zip.Entries.Count);
                foreach (var entry in zip.Entries)
                {
                    var written = NormalizeEntry(entry.FullName);
                    if (written.EndsWith("/", StringComparison.Ordinal)) continue; // a directory

                    var resolved = IsAbsolute(written) ? null : Resolve(written);
                    if (resolved == null) throw Hostile(entry.FullName, "is absolute or climbs out of the archive");
                    if (resolved.Length == 0) continue;

                    named.Add((entry, written, resolved));
                }

                var root = FindWorldRoot(named.Select(n => n.resolved));

                // Then against the world: an entry written under the world's folder must resolve inside it.
                var entries = new List<KeyValuePair<string, string>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (entry, written, _) in named)
                {
                    if (!written.StartsWith(root, StringComparison.Ordinal)) continue;

                    var relative = Resolve(written.Substring(root.Length));
                    if (relative == null) throw Hostile(entry.FullName, "climbs out of its world");
                    if (!seen.Add(relative)) throw Hostile(entry.FullName, $"is a second entry named '{relative}'");
                    if (!IsRead(relative, includeLegacySpatial)) continue;

                    entries.Add(new KeyValuePair<string, string>(relative, TryReadText(entry)));
                }

                return OWFolderReader.ReadEntries(entries, includeLegacySpatial, archivePath);
            }
        }

        /// <summary>True for the paths the folder reader looks at: world.json, elements/, spatial/.</summary>
        private static bool IsRead(string relative, bool includeLegacySpatial)
            => relative == WorldFile
               || relative.StartsWith("elements/", StringComparison.Ordinal)
               || (includeLegacySpatial && relative.StartsWith("spatial/", StringComparison.Ordinal));

        /// <summary>An entry's text as <c>File.ReadAllText</c> reads a file (UTF-8, BOM honoured); null when unreadable.</summary>
        /// <remarks>Null is what the folder form gets from a file it cannot read: the element is skipped, not fatal.</remarks>
        private static string TryReadText(ZipArchiveEntry entry)
        {
            try
            {
                using (var reader = new StreamReader(entry.Open())) return reader.ReadToEnd();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static OWFolderFormatException Hostile(string entryName, string why)
            => new OWFolderFormatException($"The archive's entry '{entryName}' {why}; refusing the archive.");

        /// <summary>A rooted name (<c>/x</c>, <c>//server/x</c> once slashes are normalized) or a drive (<c>C:x</c>).</summary>
        private static bool IsAbsolute(string name)
            => name.StartsWith("/", StringComparison.Ordinal) || (name.Length > 1 && name[1] == ':');

        /// <summary>
        /// A <c>/</c>-separated relative path with its <c>.</c>, <c>..</c> and empty segments resolved; null when a
        /// <c>..</c> climbs above where the path starts.
        /// </summary>
        private static string Resolve(string name)
        {
            var kept = new List<string>();
            foreach (var segment in name.Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..")
                {
                    if (kept.Count == 0) return null;
                    kept.RemoveAt(kept.Count - 1);
                    continue;
                }

                kept.Add(segment);
            }

            return string.Join("/", kept);
        }

        // -- Load ---------------------------------------------------------------

        /// <summary>Prefix of an archive key's origin: the asset's GUID follows it.</summary>
        public const string GuidOriginPrefix = "guid:";

        /// <summary>
        /// The cache key for an archive in a project: <c>(Folder, worldId, "guid:&lt;asset guid&gt;")</c>. Pure: the
        /// importer looks the GUID up, this only shapes and checks it.
        /// </summary>
        /// <param name="worldId">The world's own id (<c>world.json</c>'s <c>id</c>).</param>
        /// <param name="assetGuid">Unity's GUID for the archive asset: 32 hex digits, any case (kept lowercase).</param>
        /// <remarks>
        /// A folder world is keyed by its folder's absolute path, which is right for a folder opened in place and
        /// wrong for an asset: serialized into the cache, it would commit one machine's <c>C:\Users\...</c> into the
        /// project, and a teammate's import would key the same world differently. The asset's GUID lives in the
        /// committed <c>.meta</c>, so it is the same on every clone, it survives a move or a rename (a path would go
        /// stale until a re-import), and it still tells two archives of one world apart, which is what the key's
        /// origin is for.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Not 32 hex digits, or all zeros (Unity's empty GUID). A path is refused here, so none can reach the key.
        /// </exception>
        public static OWWorldKey KeyFor(string worldId, string assetGuid)
            => OWWorldKey.FromFolder(worldId, GuidOrigin(assetGuid));

        /// <summary>
        /// Reads an archive and fills a cache with it, read-only, keyed by <see cref="KeyFor"/>.
        /// </summary>
        /// <param name="cache">Cache to fill. Its contents are replaced.</param>
        /// <param name="archivePath">The archive on disk (any path; it is read, never stored).</param>
        /// <param name="assetGuid">The archive asset's GUID: the key's origin.</param>
        /// <param name="includeLegacySpatial">Also load the dated <c>spatial/</c> layout. On by default.</param>
        /// <remarks>
        /// Always read-only, whatever the world declares: an archive is a capture, with nothing to write back to.
        /// The importer's whole body is this call.
        /// </remarks>
        public static OWFolderReadResult LoadInto(
            OWWorldCache cache, string archivePath, string assetGuid, bool includeLegacySpatial = true)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            var origin = GuidOrigin(assetGuid);

            var read = Read(archivePath, includeLegacySpatial);
            return OWFolderLoader.LoadInto(cache, read, OWWorldKey.FromFolder(read.WorldId, origin), readOnly: true);
        }

        private static string GuidOrigin(string assetGuid)
        {
            if (assetGuid == null || assetGuid.Length != 32 || !assetGuid.All(Uri.IsHexDigit) || assetGuid.All(c => c == '0'))
            {
                throw new ArgumentException(
                    $"'{assetGuid}' is not an asset GUID (32 hex digits). An archive's key carries its asset's GUID, "
                    + "never a path.", nameof(assetGuid));
            }

            return GuidOriginPrefix + assetGuid.ToLowerInvariant();
        }

        // -- Paths --------------------------------------------------------------

        private static string NormalizeEntry(string name) => (name ?? string.Empty).Replace('\\', '/');

        private static string FullDirectory(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="root"/> (a full path).</summary>
        /// <remarks>Case-insensitive, so it refuses rather than misses on a case-insensitive file system.</remarks>
        private static bool IsUnder(string path, string root)
            => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
