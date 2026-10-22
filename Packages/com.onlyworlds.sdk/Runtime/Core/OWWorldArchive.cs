using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>The budget <see cref="OWWorldArchive.Read"/> holds an archive to, as the TypeScript reader's are.</summary>
    /// <remarks>
    /// An archive someone sent is untrusted input, and an importer opens it unasked: past either limit it is refused
    /// before anything is inflated. Raise them for one read of a world known to be larger.
    /// </remarks>
    public sealed class OWWorldArchiveReadOptions
    {
        /// <summary>
        /// The most bytes the archive's files may hold uncompressed, all files counted, by their declared sizes.
        /// <see cref="OWWorldArchive.DefaultMaxBytes"/> (1 GiB) by default.
        /// </summary>
        public long MaxBytes = OWWorldArchive.DefaultMaxBytes;

        /// <summary>
        /// The most files the archive may hold, directories not counted. <see cref="OWWorldArchive.DefaultMaxEntries"/>
        /// (200,000) by default.
        /// </summary>
        public int MaxEntries = OWWorldArchive.DefaultMaxEntries;
    }

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

        /// <summary>
        /// The most an archive's files may hold uncompressed, by default: 1 GiB. <see cref="Read"/> refuses past it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The TypeScript SDK's reader budget (<c>WORLD_FILE_MAX_BYTES</c> in <c>world-file.ts</c>, Atlas's), so one
        /// file opens or is refused the same in both. Sikelia today is 25 MB in 20,570 files.
        /// </para>
        /// <para>
        /// Not decided: what the spec's floor is, the size every reader must accept. Boss proposes 500,000 elements
        /// and 2 GiB, which needs zip64 (a classic zip holds at most 65,535 entries; zip64 in Unity's .NET Standard
        /// 2.1 profile is unmeasured). The floor is the Captain's to rule; these defaults follow the TypeScript
        /// reader's until then, and <see cref="OWWorldArchiveReadOptions"/> raises them for one read.
        /// </para>
        /// </remarks>
        public const long DefaultMaxBytes = 1L << 30;

        /// <summary>
        /// The most files an archive may hold, by default: 200,000, the TypeScript SDK's <c>WORLD_FILE_MAX_ENTRIES</c>
        /// (an open question, as <see cref="DefaultMaxBytes"/> says).
        /// </summary>
        public const int DefaultMaxEntries = 200_000;

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
        /// <exception cref="OWFolderFormatException">No readable <c>world.json</c>, or no id (a non-empty string) in it.</exception>
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
        /// <c>records/</c> whole: a producer's files are its own, dot-files and its own <c>.meta</c> files included
        /// (§4.5 rule 6). Only Unity's <c>.meta</c> for a file or folder beside it is left out, since Unity writes it,
        /// not a producer (<see cref="IsUnityMeta"/> tells the two apart).
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

        /// <summary>
        /// Unity's own <c>.meta</c>: named after a file or folder beside it, and opening as Unity opens every one it
        /// writes, <c>fileFormatVersion: N</c> then <c>guid: </c> and 32 lowercase hex digits.
        /// </summary>
        /// <remarks>
        /// The name alone is not enough: a producer may keep its own <c>x.meta</c> beside its own <c>x</c> in
        /// <c>records/</c>, and that file travels byte for byte (§4.5 rule 6a). The content tells the two apart
        /// exactly: Unity writes those two lines first in every <c>.meta</c>, for a file or a folder, and no producer
        /// opens a file with Unity's version line and a GUID by accident. Where the folder sits cannot tell them
        /// apart: a world under <c>Assets/</c> holds both kinds side by side, and Unity writes <c>.meta</c> files
        /// under <c>Packages/</c> too. Only the opening bytes are read; a line may end in CRLF (a git checkout's).
        /// </remarks>
        private static bool IsUnityMeta(string dir, string name)
        {
            if (!name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
            var owner = Path.Combine(dir, name.Substring(0, name.Length - 5));
            if (!File.Exists(owner) && !Directory.Exists(owner)) return false;

            var head = new byte[UnityMetaHeadBytes];
            var count = 0;
            using (var stream = File.OpenRead(Path.Combine(dir, name)))
            {
                int read;
                while (count < head.Length && (read = stream.Read(head, count, head.Length - count)) > 0) count += read;
            }

            return UnityMetaHead.IsMatch(Encoding.ASCII.GetString(head, 0, count));
        }

        /// <summary>The two lines that open every <c>.meta</c> Unity writes.</summary>
        private static readonly Regex UnityMetaHead =
            new Regex(@"\AfileFormatVersion: [0-9]+\r?\nguid: [0-9a-f]{32}\r?(\n|\z)", RegexOptions.CultureInvariant);

        /// <summary>Enough bytes for those two lines (about 60), and few enough to read a large file's start only.</summary>
        private const int UnityMetaHeadBytes = 128;

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

            if (OWFolderReader.WorldIdOf(world) == null)
            {
                throw new OWFolderFormatException(
                    $"world.json in '{root}' has no id (a non-empty string), so it cannot be packed as a world.");
            }

            if (keepApiBlock || world.Property("api") == null) return bytes;

            world.Remove("api");
            return new UTF8Encoding(false).GetBytes(OWFolderWriter.Serialize(world));
        }

        /// <summary>
        /// A default file name for a folder's archive: <c>&lt;world name slug&gt;.owworld</c>, else the id's tail.
        /// </summary>
        /// <exception cref="OWFolderFormatException">No readable <c>world.json</c>, or no id (a non-empty string) in it.</exception>
        public static string DefaultArchiveName(string worldFolder)
        {
            var root = FullDirectory(worldFolder);
            var path = Path.Combine(root, WorldFile);
            if (!File.Exists(path)) throw new OWFolderFormatException($"No world.json in '{root}'.");

            JObject world;
            try { world = OWFolderWriter.ReadJsonFile(path); }
            catch (Exception e) { throw new OWFolderFormatException($"world.json in '{root}' is not valid JSON.", e); }

            var id = OWFolderReader.WorldIdOf(world);
            if (id == null) throw new OWFolderFormatException($"world.json in '{root}' has no id (a non-empty string).");

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
        /// <param name="options">
        /// The budget the archive is held to. Null uses the defaults, <see cref="DefaultMaxBytes"/> and
        /// <see cref="DefaultMaxEntries"/>.
        /// </param>
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
        /// Every entry name is checked before anything is read, and must be a plain relative path, <c>/</c>-separated
        /// (folder spec §4.7, OW-16, strict): one that is absolute (<c>/x</c>, <c>C:/x</c>), holds a backslash, or has
        /// an empty, <c>.</c> or <c>..</c> segment refuses the whole archive, whatever it would resolve to
        /// (<c>w/a/../b.json</c> too), and so does a second entry of one name. An archive someone sent is untrusted
        /// input, and an importer runs on it unasked: nothing is written, but a hostile name must not become an
        /// element's path, and one name must not have two spellings.
        /// </para>
        /// <para>
        /// So is its size held to a budget before anything is inflated: more files than
        /// <see cref="OWWorldArchiveReadOptions.MaxEntries"/>, or more bytes declared than
        /// <see cref="OWWorldArchiveReadOptions.MaxBytes"/>, refuses the archive from its directory alone. A declared
        /// size can lie (a zip bomb is a small file that inflates to gigabytes), so each entry is inflated at most to
        /// the size it declares, and one that holds more refuses the archive there: what is held in memory never
        /// passes the budget. The TypeScript reader's rule, by its numbers.
        /// </para>
        /// <para>
        /// And every entry must be one that lands safely on any disk, anywhere in the archive, not only in the world:
        /// no two entries of one name, and no two names that are one once case and Unicode normalization (NFC) are
        /// ignored (one file on Windows and macOS); no path both a file and a folder; no symbolic link or special file
        /// (by the Unix mode in the external attributes); nothing encrypted, and only stored or deflated entries (an
        /// entry in another method is refused, never skipped); no Windows device name (<c>CON</c>, <c>nul.txt</c>,
        /// <c>COM1</c>...), trailing dot or space, <c>&lt;&gt;:"|?*</c> or control character in a name; and no
        /// non-ASCII name without the zip's UTF-8 flag. Inside the world, a second <c>world.json</c> anywhere but
        /// under <c>records/</c> is a second world.
        /// </para>
        /// </remarks>
        /// <exception cref="FileNotFoundException">The archive does not exist.</exception>
        /// <exception cref="OWFolderFormatException">
        /// Not a zip; an entry name that is not a plain relative path, or is repeated, colliding or unsafe; a path both a file and a folder,
        /// a link, an encrypted entry or one in a method other than stored or deflate; past the budget, or an entry
        /// inflating past its declared size; no world or more than one in it, a nested one included; or the world's
        /// own <c>world.json</c> is unreadable.
        /// </exception>
        public static OWFolderReadResult Read(
            string archivePath, bool includeLegacySpatial = true, OWWorldArchiveReadOptions options = null)
        {
            var budget = options ?? new OWWorldArchiveReadOptions();
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
                // What ZipArchiveEntry leaves out (a name's raw bytes and flags, the method, a Unix mode) is read from
                // the central directory by hand, one record per entry, in the same order. The zip reads its own first.
                var entryCount = zip.Entries.Count;
                var directory = ReadCentralDirectory(stream);
                if (directory == null || directory.Count != entryCount)
                {
                    throw new OWFolderFormatException(
                        $"'{archivePath}' has a central directory that cannot be walked entry by entry; refusing the archive.");
                }

                // Every name and the budget are checked against the archive first, before the world is even found
                // and before one byte is inflated: the central directory gives each entry's name and declared size.
                var named = new List<(ZipArchiveEntry entry, string path)>(zip.Entries.Count);
                var names = new HashSet<string>(StringComparer.Ordinal);
                var filePaths = new HashSet<string>(StringComparer.Ordinal);
                var folderPaths = new HashSet<string>(StringComparer.Ordinal);
                var folded = new Dictionary<string, string>(StringComparer.Ordinal);
                var files = 0;
                long declared = 0;
                for (var i = 0; i < zip.Entries.Count; i++)
                {
                    var entry = zip.Entries[i];
                    var written = entry.FullName ?? string.Empty;
                    var isFolder = written.EndsWith("/", StringComparison.Ordinal);
                    var path = isFolder ? written.Substring(0, written.Length - 1) : written;

                    var fault = PathFault(path);
                    if (fault != null) throw Hostile(entry.FullName, fault);

                    CheckEntry(directory[i], entry.FullName, path);
                    if (!names.Add(isFolder ? path + "/" : path))
                        throw Hostile(entry.FullName, $"is a second entry named '{path}'");
                    CheckCollision(path, entry.FullName, folded);
                    AddFolders(path, isFolder, folderPaths);
                    if (!isFolder) filePaths.Add(path);

                    if (isFolder) continue;

                    if (++files > budget.MaxEntries)
                    {
                        throw PastBudget(archivePath, $"holds more than {budget.MaxEntries} files",
                            nameof(budget.MaxEntries));
                    }

                    // Compared by what is left, never by adding: two declared sizes can overflow a long (and a zip64
                    // size past long's range reads negative).
                    if (entry.Length < 0 || entry.Length > budget.MaxBytes - declared)
                    {
                        throw PastBudget(archivePath, $"declares more than {budget.MaxBytes} bytes uncompressed",
                            nameof(budget.MaxBytes));
                    }

                    declared += entry.Length;
                    named.Add((entry, path));
                }

                // A path that is a file and also a folder of another path cannot both land on a disk.
                foreach (var folder in folderPaths)
                {
                    if (filePaths.Contains(folder))
                    {
                        throw new OWFolderFormatException(
                            $"The archive's path '{folder}' is both a file and a folder; refusing the archive.");
                    }
                }

                var root = FindWorldRoot(named.Select(n => n.path));

                // Then the world's own entries: every name is already a plain path, so its place is its name.
                var entries = new List<KeyValuePair<string, string>>();
                foreach (var (entry, path) in named)
                {
                    if (!path.StartsWith(root, StringComparison.Ordinal)) continue;
                    var relative = path.Substring(root.Length);

                    // records/ is a producer's own data, which may hold a world.json; anywhere else it is a second world.
                    if (relative != WorldFile && relative.EndsWith("/" + WorldFile, StringComparison.Ordinal)
                        && !relative.StartsWith(RecordsFolder + "/", StringComparison.Ordinal))
                    {
                        throw new OWFolderFormatException(
                            $"The archive holds a second world.json inside its world, at '{entry.FullName}'. One world "
                            + "per archive: inside a world, a world.json belongs only under records/; refusing the archive.");
                    }

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
        /// <exception cref="OWFolderFormatException">The entry inflates past the size it declares: never skipped.</exception>
        private static string TryReadText(ZipArchiveEntry entry)
        {
            MemoryStream held;
            try
            {
                held = InflateAsDeclared(entry);
            }
            catch (Exception)
            {
                return null;
            }

            if (held == null) throw Hostile(entry.FullName, $"inflates past the {entry.Length} bytes it declares");
            using (var reader = new StreamReader(held)) return reader.ReadToEnd();
        }

        /// <summary>An entry's bytes, inflated at most to the size it declares; null when it holds more.</summary>
        /// <remarks>
        /// The declared sizes were held to the budget before any entry was opened, so stopping each entry at its own
        /// keeps the whole read inside the budget, whatever the runtime's inflater does with a size that lies.
        /// </remarks>
        private static MemoryStream InflateAsDeclared(ZipArchiveEntry entry)
        {
            var held = new MemoryStream();
            var buffer = new byte[81920];
            using (var input = entry.Open())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (read > entry.Length - held.Length) return null;
                    held.Write(buffer, 0, read);
                }
            }

            held.Position = 0;
            return held;
        }

        private static OWFolderFormatException PastBudget(string archivePath, string what, string option)
            => new OWFolderFormatException(
                $"'{archivePath}' {what}, past the reader's budget (OWWorldArchiveReadOptions.{option}); "
                + "refusing the archive.");

        private static OWFolderFormatException Hostile(string entryName, string why)
            => new OWFolderFormatException($"The archive's entry '{entryName}' {why}; refusing the archive.");

        /// <summary>
        /// Why an entry's name (a folder's without its one trailing <c>/</c>) is not a plain relative path, or null:
        /// a backslash, a root (<c>/x</c>, <c>//server/x</c>), a drive (<c>C:x</c>), or an empty, <c>.</c> or
        /// <c>..</c> segment.
        /// </summary>
        /// <remarks>
        /// Strict, as folder spec §4.7 rules OW-16 ("whatever they would resolve to"): <c>w/a/../b.json</c> and
        /// <c>w\b.json</c> refuse the archive though they would land inside it. Resolving them is a second spelling of
        /// one name, and readers that resolve differently disagree on which file an entry is.
        /// </remarks>
        private static string PathFault(string name)
        {
            if (name.IndexOf('\\') >= 0) return "holds a backslash; a zip's only separator is '/'";
            if (name.StartsWith("/", StringComparison.Ordinal) || (name.Length > 1 && name[1] == ':'))
                return "is absolute (a root, a server or a drive)";

            foreach (var segment in name.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return "has an empty, '.' or '..' segment; an entry name is a plain relative path, whatever it would resolve to";
            }

            return null;
        }

        // -- What an entry may be ------------------------------------------------

        /// <summary>
        /// One entry's own refusals, from its central-directory record: a link or a special file, encryption, a method
        /// other than stored or deflate, and a name that would not land safely on every disk.
        /// </summary>
        /// <param name="record">The entry's central-directory record.</param>
        /// <param name="entryName">The name as written, for the message.</param>
        /// <param name="path">The name with <c>/</c> separators and no trailing slash.</param>
        private static void CheckEntry(DirectoryRecord record, string entryName, string path)
        {
            // A Unix mode sits in the external attributes' high 16 bits; none (0), a file or a folder is all a world holds.
            var kind = (record.ExternalAttributes >> 16) & UnixTypeMask;
            if (kind == UnixSymlink) throw Hostile(entryName, "is a symbolic link");
            if (kind != 0 && kind != UnixFile && kind != UnixFolder)
            {
                throw Hostile(entryName,
                    $"is a special file (Unix type {Convert.ToString(kind, 8)}), neither a file nor a folder");
            }

            // Bit 0 is encryption (ZipCrypto, or AES as method 99), bit 6 strong encryption.
            if ((record.Flags & 0x0041) != 0) throw Hostile(entryName, "is encrypted; an .owworld is never encrypted");

            if (record.Method != MethodStored && record.Method != MethodDeflate)
            {
                throw Hostile(entryName,
                    $"is compressed with method {record.Method}; an .owworld holds only stored (0) and deflated (8) entries");
            }

            // Without bit 11 a name's bytes are in a code page the zip does not name: past ASCII, its spelling is a guess.
            if (record.NonAscii && (record.Flags & 0x0800) == 0)
            {
                throw Hostile(entryName, "has a name that is not ASCII and lacks the zip's UTF-8 flag, so its spelling is a guess");
            }

            var fault = NameFault(path);
            if (fault != null) throw Hostile(entryName, fault);
        }

        /// <summary>
        /// Why a name would not land on every disk (Windows the strictest), or null: per segment, a reserved device
        /// name with or without an extension, a trailing dot or space, a character Windows refuses or a control
        /// character.
        /// </summary>
        /// <remarks>
        /// Runs after <see cref="PathFault"/>, so every segment is a name: none is empty, <c>.</c> or <c>..</c>.
        /// </remarks>
        private static string NameFault(string path)
        {
            foreach (var segment in path.Split('/'))
            {
                foreach (var c in segment)
                {
                    if (c < 0x20 || c == 0x7F)
                        return $"holds a control character (U+{(int)c:X4}), which no disk takes in a name";
                    if (WindowsRefused.IndexOf(c) >= 0)
                        return $"holds the character '{c}', which Windows refuses in a name";
                }

                var last = segment[segment.Length - 1];
                if (last == '.' || last == ' ')
                    return $"has a name ending in a {(last == '.' ? "dot" : "space")} ('{segment}'), which Windows drops";

                var stem = segment.Split('.')[0];
                if (WindowsDevices.Contains(stem))
                    return $"has the Windows reserved name '{stem}' ('{segment}'), a device on Windows whatever its extension";
            }

            return null;
        }

        /// <summary>The characters Windows refuses in a name (control characters aside).</summary>
        private const string WindowsRefused = "<>:\"|?*";

        /// <summary>Windows device names: reserved as a name's stem, whatever the extension and case.</summary>
        private static readonly HashSet<string> WindowsDevices = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL" }
                .Concat(Enumerable.Range(1, 9).Select(n => "COM" + n))
                .Concat(Enumerable.Range(1, 9).Select(n => "LPT" + n)),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Refuses a path (or any folder above it) that is another one once case and Unicode normalization are
        /// ignored: one file on Windows and on macOS. <paramref name="folded"/> maps each fold to the first spelling.
        /// </summary>
        private static void CheckCollision(string path, string entryName, Dictionary<string, string> folded)
        {
            var at = 0;
            while (at >= 0)
            {
                at = path.IndexOf('/', at + 1);
                var prefix = at < 0 ? path : path.Substring(0, at);
                var key = Fold(prefix);
                if (!folded.TryGetValue(key, out var first))
                {
                    folded.Add(key, prefix);
                }
                else if (!string.Equals(first, prefix, StringComparison.Ordinal))
                {
                    throw Hostile(entryName,
                        $"names '{prefix}', which is '{first}' once case and Unicode normalization are ignored (one name on "
                        + "Windows and macOS)");
                }
            }
        }

        /// <summary>
        /// One fold for a name: Unicode NFC, then a simple case fold (upper, then lower, so <c>ſ</c> meets <c>s</c>
        /// and the Kelvin sign meets <c>k</c>).
        /// </summary>
        private static string Fold(string name)
        {
            try
            {
                name = name.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                // Not valid UTF-16 (a lone surrogate): folded by case alone.
            }

            return name.ToUpperInvariant().ToLowerInvariant();
        }

        /// <summary>Adds every folder above <paramref name="path"/>, and the path itself when it is a folder.</summary>
        private static void AddFolders(string path, bool isFolder, HashSet<string> into)
        {
            if (isFolder) into.Add(path);
            for (var at = path.IndexOf('/'); at >= 0; at = path.IndexOf('/', at + 1)) into.Add(path.Substring(0, at));
        }

        private const int UnixTypeMask = 0xF000;
        private const int UnixFile = 0x8000;
        private const int UnixFolder = 0x4000;
        private const int UnixSymlink = 0xA000;
        private const int MethodStored = 0;
        private const int MethodDeflate = 8;

        // -- The central directory ------------------------------------------------

        /// <summary>One entry's central-directory record: the fields <see cref="ZipArchiveEntry"/> does not expose.</summary>
        private sealed class DirectoryRecord
        {
            public int Flags;
            public int Method;
            public uint ExternalAttributes;

            /// <summary>A name byte past 0x7F.</summary>
            public bool NonAscii;
        }

        /// <summary>
        /// The archive's central directory, one record per entry in its own order (the order
        /// <see cref="ZipArchive.Entries"/> keeps); null when it cannot be walked. Reads the end records and the
        /// directory only, never an entry's data.
        /// </summary>
        /// <remarks>
        /// .NET Standard 2.1's <see cref="ZipArchiveEntry"/> has no flags, method or raw name, and its
        /// <c>IsEncrypted</c> came later, so the records are read here: the end of central directory record (found
        /// from the file's end, as <see cref="ZipArchive"/> finds it), its zip64 form when a zip64 locator precedes
        /// it, then each <c>PK\1\2</c> record's fixed fields and name.
        /// </remarks>
        private static List<DirectoryRecord> ReadCentralDirectory(Stream stream)
        {
            try
            {
                var length = stream.Length;
                if (length < EndRecordSize) return null;

                var tailLength = (int)Math.Min(length, EndRecordSize + ushort.MaxValue + Zip64LocatorSize);
                var tail = ReadAt(stream, length - tailLength, tailLength);
                var end = -1;
                for (var i = tailLength - EndRecordSize; i >= 0; i--)
                {
                    if (U32(tail, i) == 0x06054b50) { end = i; break; }
                }

                if (end < 0) return null;

                long count = U16(tail, end + 10);
                long size = U32(tail, end + 12);
                long offset = U32(tail, end + 16);

                if (end >= Zip64LocatorSize && U32(tail, end - Zip64LocatorSize) == 0x07064b50)
                {
                    var at = (long)U64(tail, end - Zip64LocatorSize + 8);
                    if (at < 0 || at > length - Zip64EndRecordSize) return null;
                    var record = ReadAt(stream, at, Zip64EndRecordSize);
                    if (U32(record, 0) != 0x06064b50) return null;
                    count = (long)U64(record, 32);
                    size = (long)U64(record, 40);
                    offset = (long)U64(record, 48);
                }

                if (count < 0 || size < 0 || offset < 0 || size > length || offset > length - size) return null;
                if (size > int.MaxValue || count > size / DirectoryRecordSize) return null;

                var directory = ReadAt(stream, offset, (int)size);
                var records = new List<DirectoryRecord>((int)count);
                var pos = 0;
                for (long n = 0; n < count; n++)
                {
                    if (pos > directory.Length - DirectoryRecordSize || U32(directory, pos) != 0x02014b50) return null;

                    var nameLength = U16(directory, pos + 28);
                    var next = (long)pos + DirectoryRecordSize + nameLength + U16(directory, pos + 30) + U16(directory, pos + 32);
                    if (next > directory.Length) return null;

                    var nonAscii = false;
                    for (var b = pos + DirectoryRecordSize; b < pos + DirectoryRecordSize + nameLength; b++)
                    {
                        if (directory[b] > 0x7F) { nonAscii = true; break; }
                    }

                    records.Add(new DirectoryRecord
                    {
                        Flags = U16(directory, pos + 8),
                        Method = U16(directory, pos + 10),
                        ExternalAttributes = U32(directory, pos + 38),
                        NonAscii = nonAscii,
                    });
                    pos = (int)next;
                }

                return records;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private const int EndRecordSize = 22;
        private const int Zip64LocatorSize = 20;
        private const int Zip64EndRecordSize = 56;
        private const int DirectoryRecordSize = 46;

        /// <summary><paramref name="count"/> bytes from <paramref name="offset"/>; an <see cref="IOException"/> when the file is shorter.</summary>
        private static byte[] ReadAt(Stream stream, long offset, int count)
        {
            var bytes = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);
            var held = 0;
            while (held < count)
            {
                var read = stream.Read(bytes, held, count - held);
                if (read <= 0) throw new EndOfStreamException();
                held += read;
            }

            return bytes;
        }

        // Little-endian, as the zip format is, whatever the machine.
        private static int U16(byte[] b, int at) => b[at] | (b[at + 1] << 8);

        private static uint U32(byte[] b, int at) => (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));

        private static ulong U64(byte[] b, int at) => U32(b, at) | ((ulong)U32(b, at + 4) << 32);

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
        /// <param name="options">As <see cref="Read"/>: the budget. Null uses the defaults.</param>
        /// <remarks>
        /// Always read-only, whatever the world declares: an archive is a capture, with nothing to write back to.
        /// The importer's whole body is this call.
        /// </remarks>
        public static OWFolderReadResult LoadInto(
            OWWorldCache cache, string archivePath, string assetGuid, bool includeLegacySpatial = true,
            OWWorldArchiveReadOptions options = null)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            var origin = GuidOrigin(assetGuid);

            var read = Read(archivePath, includeLegacySpatial, options);
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
