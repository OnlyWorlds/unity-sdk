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
    /// before anything is inflated. The defaults are folder spec §4.7's floor, the size every reader accepts; lower
    /// them to hold less, or raise them for one read of a world known to be larger.
    /// </remarks>
    public sealed class OWWorldArchiveReadOptions
    {
        /// <summary>
        /// The most bytes the archive's files may hold uncompressed, all files counted, by their declared sizes.
        /// <see cref="OWWorldArchive.DefaultMaxBytes"/> (2 GiB) by default.
        /// </summary>
        public long MaxBytes = OWWorldArchive.DefaultMaxBytes;

        /// <summary>
        /// The most files the archive may hold, directories not counted. <see cref="OWWorldArchive.DefaultMaxEntries"/>
        /// (500,000) by default.
        /// </summary>
        public int MaxEntries = OWWorldArchive.DefaultMaxEntries;
    }

    /// <summary>
    /// Why <see cref="OWWorldArchive.Read"/> refused an archive: the values of
    /// <see cref="OWFolderFormatException.Reason"/>, one cause from folder spec §4.7's shared list.
    /// </summary>
    /// <remarks>
    /// The names are the spec's, the same the TypeScript reader and the conformance fixture set use, so a refusal can
    /// be compared across readers ("a reader that refuses for a different cause fails the case"). The exception's
    /// message says which entry and why, in words.
    /// </remarks>
    public static class OWWorldArchiveReason
    {
        /// <summary>Not a zip, or a zip that cannot be read (its central directory cannot be walked; spanned).</summary>
        public const string NotOpenable = "not_openable";

        /// <summary>No <c>world.json</c> at the root or in one top-level folder, or one that is not a world.</summary>
        public const string NotAWorld = "not_a_world";

        /// <summary>More than one world: two top-level worlds, or a second <c>world.json</c> inside the world.</summary>
        public const string MoreThanOne = "more_than_one";

        /// <summary>An entry name that is not a plain relative path, or a path both a file and a folder.</summary>
        public const string UnsafePath = "unsafe_path";

        /// <summary>Past the budget's bytes, declared or inflated.</summary>
        public const string TooBig = "too_big";

        /// <summary>Past the budget's file count.</summary>
        public const string TooMany = "too_many";

        /// <summary>Two entries of one name.</summary>
        public const string Duplicate = "duplicate";

        /// <summary>Two names that are one after NFC then simple case folding.</summary>
        public const string Collision = "collision";

        /// <summary>A symbolic link or another special file, by its Unix mode.</summary>
        public const string Link = "link";

        /// <summary>An encrypted entry, or one compressed with a method other than stored or deflate.</summary>
        public const string Method = "method";

        /// <summary>A name that would not land on every disk, or a non-ASCII name without the UTF-8 flag.</summary>
        public const string Name = "name";

        /// <summary>A <c>mimetype</c> marker that holds other bytes than <see cref="OWWorldArchive.MediaType"/>.</summary>
        public const string Marker = "marker";
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
    /// elements (rule 4). One exception, rule 11: a producer whose manifest entry says <c>"private": true</c> is left
    /// out whole unless the caller asks for it, and <c>RECORDS.json</c> still goes in byte for byte (folder spec §4.7,
    /// OW-8).
    /// </para>
    /// <para>
    /// <b>The rule: no key travels.</b> <c>world.json</c>'s <c>api</c> block is dropped unless the caller asks to keep
    /// it (a private copy, not a portable <c>.owworld</c>). That block holds an API key, and an archive is made to be
    /// shared: committed with a project, sent, shipped in a build. Nothing else in <c>world.json</c> changes. A read
    /// strips the block from whatever archive it meets and never returns it (folder spec §4.7, OW-10).
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
        /// The <c>.owworld</c> media type, the bytes of the marker entry (folder spec §4.7, OW-6): ASCII, no newline.
        /// </summary>
        /// <remarks>
        /// A reader never requires the marker (every file made before §4.7 has none), and refuses one that holds
        /// other bytes. The type is in the <c>vnd.</c> tree; its registration with IANA is a separate act.
        /// </remarks>
        public const string MediaType = "application/vnd.onlyworlds.world+zip";

        /// <summary>The marker entry's name, at the archive's root: EPUB's shape, the media type as the whole file.</summary>
        internal const string MarkerEntry = "mimetype";

        private static readonly byte[] MediaTypeBytes = Encoding.ASCII.GetBytes(MediaType);

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
        /// The most an archive's files may hold uncompressed, by default: 2 GiB, the spec's floor.
        /// <see cref="Read"/> refuses past it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Folder spec §4.7 (OW-15): "the floor every reader accepts is 500,000 entries and 2 GiB", which means zip64
        /// on read (a classic zip holds at most 65,535 entries; <see cref="Read"/> reads zip64). A smaller default
        /// would refuse a file every other reader opens. Sikelia today is 25 MB in 20,570 files.
        /// </para>
        /// <para>
        /// <see cref="OWWorldArchiveReadOptions"/> sets another budget for one read: lower, for a caller that wants
        /// less held in memory, or higher, for a world known to be larger. A media file is inflated only to check its
        /// CRC-32, through one buffer, and never held, so only an archive whose elements reach the budget costs that
        /// much memory.
        /// </para>
        /// </remarks>
        public const long DefaultMaxBytes = 2L << 30;

        /// <summary>
        /// The most files an archive may hold, by default: 500,000, the spec's floor (folder spec §4.7, OW-15; see
        /// <see cref="DefaultMaxBytes"/>).
        /// </summary>
        public const int DefaultMaxEntries = 500_000;

        // -- Pack ---------------------------------------------------------------

        /// <summary>
        /// Packs a world folder into an <c>.owworld</c>. Writes only the archive; never touches the folder.
        /// </summary>
        /// <param name="worldFolder">Directory containing <c>world.json</c>.</param>
        /// <param name="archivePath">The archive to write. Replaced if it exists. Must not be inside the folder.</param>
        /// <param name="keepApiBlock">
        /// Keep <c>world.json</c>'s <c>api</c> block: a private copy, for the caller's own machine, and <b>not a
        /// portable <c>.owworld</c></b> (folder spec §4.7, OW-10: a writer never emits the block). Off by default:
        /// that block holds an API key, and an archive is a copy made to be shared -- committed, sent, shipped. A
        /// <c>world.json</c> with no <c>api</c> block goes in byte for byte; one that loses it is re-emitted in the
        /// format's own bytes. <see cref="Read"/> strips the block from any archive, this one included.
        /// </param>
        /// <returns>The entry names written, in order.</returns>
        /// <remarks>
        /// <para>
        /// Inside the archive the world sits in one top-level folder named after <paramref name="worldFolder"/>, so
        /// extracting it by hand gives back a recognisable folder. Every dot-folder and dot-file is left out (a
        /// tool's private state: <c>.atlas/</c>, <c>.git/</c>, <c>.ow/</c>), and so is every Unity <c>.meta</c>
        /// (present when the folder sits under <c>Assets/</c>, and never the world's); <c>records/</c> goes in whole
        /// but for its private producers (see the class's first rule, and the overload that asks for them).
        /// </para>
        /// <para>
        /// Deterministic: entries in the order of their names' UTF-8 bytes (code point order, folder spec §4.7,
        /// OW-13), each stamped <see cref="EntryTime"/>, so the same folder packs to the same bytes whatever the file
        /// system's listing order or the files' mtimes. Same bytes across runtimes is not claimed: the deflate stream
        /// is the runtime's zlib.
        /// </para>
        /// <para>
        /// The first entry is the <c>mimetype</c> marker (folder spec §4.7, OW-6): <see cref="MediaType"/>'s bytes,
        /// stored (method 0), with no extra field, the EPUB shape. Every other entry is deflated, an empty one stored. The zip is written
        /// here rather than by <see cref="ZipArchive"/>, because Mono's writes
        /// <see cref="CompressionLevel.NoCompression"/> as deflate (method 8) and so cannot store the marker. A
        /// classic zip, and zip64 from 65,535 entries or past 4 GiB, as §4.7 asks of a writer; a non-ASCII name
        /// carries the UTF-8 flag (bit 11).
        /// </para>
        /// </remarks>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        /// <exception cref="OWFolderFormatException">
        /// No readable <c>world.json</c>, or no id (a non-empty string) in it; or a <c>records/RECORDS.json</c> that
        /// cannot be read (not UTF-8 JSON, not an array of objects, a private entry whose producer is not one
        /// directory name), so which producers are private cannot be told.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="archivePath"/> is inside the folder.</exception>
        public static List<string> Pack(string worldFolder, string archivePath, bool keepApiBlock = false)
            => Pack(worldFolder, archivePath, keepApiBlock, includePrivateRecords: false);

        /// <summary>
        /// Packs a world folder into an <c>.owworld</c>, saying whether private records go too.
        /// </summary>
        /// <param name="worldFolder">Directory containing <c>world.json</c>.</param>
        /// <param name="archivePath">The archive to write. Replaced if it exists. Must not be inside the folder.</param>
        /// <param name="keepApiBlock">As the other overload: a private copy that keeps <c>world.json</c>'s <c>api</c> block.</param>
        /// <param name="includePrivateRecords">
        /// Carry the producers <c>records/RECORDS.json</c> marks <c>"private": true</c> (folder spec §4.5 rule 11).
        /// Off by default: an export, a share or a snapshot leaves them out, so a tool's own notes do not travel the
        /// first time a world is shared. Either way a private producer's directory goes whole or not at all, and the
        /// manifest goes in byte for byte, its private entries kept: a reader learns a producer was left out, not
        /// that it was never there. On, the manifest is not read.
        /// </param>
        /// <returns>The entry names written, in order.</returns>
        /// <remarks>
        /// A producer is matched to its directory ignoring case, so a manifest that says <c>fold</c> leaves out
        /// <c>Fold/</c> on a file system that tells them apart: the side a mistake should fall on.
        /// </remarks>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        /// <exception cref="OWFolderFormatException">
        /// No readable <c>world.json</c>, or no id in it; or, leaving private records out, a <c>records/RECORDS.json</c>
        /// that cannot be read (see the other overload).
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="archivePath"/> is inside the folder.</exception>
        public static List<string> Pack(string worldFolder, string archivePath, bool keepApiBlock,
            bool includePrivateRecords)
            => Pack(worldFolder, archivePath, keepApiBlock, includePrivateRecords, includeHidden: true);

        /// <summary>
        /// Packs a world folder into an <c>.owworld</c>, saying whether private records go too and whether the file is a
        /// share.
        /// </summary>
        /// <param name="worldFolder">Directory containing <c>world.json</c>.</param>
        /// <param name="archivePath">The archive to write. Replaced if it exists. Must not be inside the folder.</param>
        /// <param name="keepApiBlock">As the first overload.</param>
        /// <param name="includePrivateRecords">As the overload above.</param>
        /// <param name="includeHidden">
        /// Carry element files marked hidden (folder spec §4.8). On by default in every overload: a world file is a copy,
        /// as keel's own download keeps an owner's hidden elements, and a file is also how a person moves or backs up their
        /// own world. A tool that makes the file to hand to someone else passes <c>false</c>: every <c>.json</c> file
        /// directly in a type folder of <c>elements/</c> or the legacy <c>spatial/</c> whose JSON object carries a mark or
        /// a doubt (<see cref="OWPayload.IsHidden"/>) is left out whole. A file there that is not UTF-8 JSON, or
        /// not an object, is kept. Nothing else is rewritten: a visible element's link to a hidden one keeps its id.
        /// </param>
        /// <param name="hiddenLeftOut">
        /// When given, receives the paths inside the world (forward slashes, code point order) of the element files left
        /// out for the mark; its count is the count. Nothing is added when <paramref name="includeHidden"/> is on.
        /// </param>
        /// <returns>The entry names written, in order.</returns>
        /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
        /// <exception cref="OWFolderFormatException">As the overload above.</exception>
        /// <exception cref="ArgumentException"><paramref name="archivePath"/> is inside the folder.</exception>
        public static List<string> Pack(string worldFolder, string archivePath, bool keepApiBlock,
            bool includePrivateRecords, bool includeHidden, ICollection<string> hiddenLeftOut = null)
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
            var plan = PackPlan(root, includePrivateRecords);
            if (!includeHidden)
            {
                plan.RemoveAll(entry =>
                {
                    if (!IsHiddenElementFile(entry.relative, entry.source)) return false;
                    hiddenLeftOut?.Add(entry.relative);
                    return true;
                });
            }

            var top = Path.GetFileName(root);
            if (string.IsNullOrEmpty(top)) top = "world";

            var dir = Path.GetDirectoryName(archive);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var written = new List<string>(plan.Count + 1);
            using (var stream = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var zip = new ZipWriter(stream);
                zip.Add(MarkerEntry, MediaTypeBytes, deflate: false);
                written.Add(MarkerEntry);

                foreach (var (relative, source) in plan)
                {
                    var name = top + "/" + relative;
                    zip.Add(name, relative == WorldFile ? worldBytes : File.ReadAllBytes(source), deflate: true);
                    written.Add(name);
                }

                zip.Finish();
            }

            return written;
        }

        /// <summary>
        /// A zip writer for <see cref="Pack"/>: each entry's bytes whole, so its sizes and CRC-32 go in its local header
        /// (no data descriptor), every entry stamped <see cref="EntryTime"/>, no extra field below zip64's limits.
        /// </summary>
        /// <remarks>
        /// The layout is APPNOTE 6.3.10's: local header and data per entry, then the central directory, then the end
        /// record; the zip64 end record and its locator before it when the entry count reaches 65,535 or the directory
        /// starts or ends past 4 GiB, and a zip64 extra field on an entry whose size or offset reaches 4 GiB. Version
        /// made by is MS-DOS (host 0), so no Unix mode is claimed for any entry.
        /// </remarks>
        internal sealed class ZipWriter
        {
            private const uint Max32 = 0xFFFFFFFFu;
            private const ushort DosTime = 0;                          // 00:00:00
            private const ushort DosDate = (0 << 9) | (1 << 5) | 1;    // 1980-01-01
            private const ushort Utf8Flag = 1 << 11;

            private readonly Stream _out;
            private readonly List<Entry> _entries = new List<Entry>();
            private long _offset;

            private sealed class Entry
            {
                public byte[] Name;
                public ushort Flags;
                public ushort Method;
                public uint Crc;
                public long Compressed;
                public long Size;
                public long Offset;
            }

            public ZipWriter(Stream output) => _out = output;

            public void Add(string name, byte[] bytes, bool deflate)
            {
                deflate &= bytes.Length > 0; // an empty file is stored: no deflate stream to depend on
                var entry = new Entry
                {
                    Name = Encoding.UTF8.GetBytes(name),
                    Method = (ushort)(deflate ? 8 : 0),
                    Crc = ~Crc32Update(Crc32Start, bytes, bytes.Length),
                    Size = bytes.Length,
                    Offset = _offset,
                };
                if (entry.Name.Length != name.Length) entry.Flags = Utf8Flag; // more bytes than units: not ASCII

                var data = bytes;
                if (deflate)
                {
                    using (var packed = new MemoryStream())
                    {
                        using (var deflater = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                            deflater.Write(bytes, 0, bytes.Length);
                        data = packed.ToArray();
                    }
                }

                entry.Compressed = data.Length;

                var big = entry.Size >= Max32 || entry.Compressed >= Max32;
                var header = new BinaryWriter(new MemoryStream(), Encoding.UTF8);
                header.Write(0x04034b50u);
                header.Write((ushort)(big ? 45 : 20));
                header.Write(entry.Flags);
                header.Write(entry.Method);
                header.Write(DosTime);
                header.Write(DosDate);
                header.Write(entry.Crc);
                header.Write(big ? Max32 : (uint)entry.Compressed);
                header.Write(big ? Max32 : (uint)entry.Size);
                header.Write((ushort)entry.Name.Length);
                header.Write((ushort)(big ? 20 : 0));
                header.Write(entry.Name);
                if (big)
                {
                    header.Write((ushort)0x0001);
                    header.Write((ushort)16);
                    header.Write(entry.Size);
                    header.Write(entry.Compressed);
                }

                Emit(header);
                _out.Write(data, 0, data.Length);
                _offset += data.Length;
                _entries.Add(entry);
            }

            public void Finish()
            {
                var start = _offset;
                foreach (var entry in _entries)
                {
                    var extra = new List<long>();
                    if (entry.Size >= Max32) extra.Add(entry.Size);
                    if (entry.Compressed >= Max32) extra.Add(entry.Compressed);
                    if (entry.Offset >= Max32) extra.Add(entry.Offset);

                    var record = new BinaryWriter(new MemoryStream(), Encoding.UTF8);
                    record.Write(0x02014b50u);
                    record.Write((ushort)(extra.Count > 0 ? 45 : 20)); // made by: MS-DOS host, this version
                    record.Write((ushort)(extra.Count > 0 ? 45 : 20));
                    record.Write(entry.Flags);
                    record.Write(entry.Method);
                    record.Write(DosTime);
                    record.Write(DosDate);
                    record.Write(entry.Crc);
                    record.Write(entry.Compressed >= Max32 ? Max32 : (uint)entry.Compressed);
                    record.Write(entry.Size >= Max32 ? Max32 : (uint)entry.Size);
                    record.Write((ushort)entry.Name.Length);
                    record.Write((ushort)(extra.Count == 0 ? 0 : 4 + 8 * extra.Count));
                    record.Write((ushort)0); // comment
                    record.Write((ushort)0); // disk
                    record.Write((ushort)0); // internal attributes
                    record.Write(0u);        // external attributes
                    record.Write(entry.Offset >= Max32 ? Max32 : (uint)entry.Offset);
                    record.Write(entry.Name);
                    if (extra.Count > 0)
                    {
                        record.Write((ushort)0x0001);
                        record.Write((ushort)(8 * extra.Count));
                        foreach (var value in extra) record.Write(value);
                    }

                    Emit(record);
                }

                var size = _offset - start;
                var count = _entries.Count;
                var zip64 = count >= 0xFFFF || start >= Max32 || size >= Max32 || _offset >= Max32;
                var end = new BinaryWriter(new MemoryStream(), Encoding.UTF8);
                if (zip64)
                {
                    var record = _offset;
                    end.Write(0x06064b50u);
                    end.Write(44L);           // the record's size after this field
                    end.Write((ushort)45);
                    end.Write((ushort)45);
                    end.Write(0u);            // this disk
                    end.Write(0u);            // the directory's disk
                    end.Write((long)count);
                    end.Write((long)count);
                    end.Write(size);
                    end.Write(start);

                    end.Write(0x07064b50u);
                    end.Write(0u);            // the zip64 end record's disk
                    end.Write(record);
                    end.Write(1u);            // disks
                }

                end.Write(0x06054b50u);
                end.Write((ushort)0);
                end.Write((ushort)0);
                end.Write((ushort)Math.Min(count, 0xFFFF));
                end.Write((ushort)Math.Min(count, 0xFFFF));
                end.Write(size >= Max32 ? Max32 : (uint)size);
                end.Write(start >= Max32 ? Max32 : (uint)start);
                end.Write((ushort)0); // comment
                Emit(end);
                _out.Flush();
            }

            private void Emit(BinaryWriter writer)
            {
                writer.Flush();
                var buffer = (MemoryStream)writer.BaseStream;
                _out.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
                _offset += buffer.Length;
            }
        }

        /// <summary>
        /// What <see cref="Pack"/> would write: each file's path inside the world (forward slashes) and on disk, in
        /// code point order. Reads directory listings, and <c>records/RECORDS.json</c> unless private records go too.
        /// </summary>
        internal static List<(string relative, string source)> PackPlan(string worldFolder,
            bool includePrivateRecords = false)
        {
            var root = FullDirectory(worldFolder);
            var plan = new List<(string relative, string source)> { (WorldFile, Path.Combine(root, WorldFile)) };

            foreach (var folder in ContentFolders)
            {
                var dir = Path.Combine(root, folder);
                if (Directory.Exists(dir)) Collect(dir, folder, plan);
            }

            var records = Path.Combine(root, RecordsFolder);
            if (Directory.Exists(records))
            {
                var leftOut = includePrivateRecords ? null : PrivateProducers(records);
                foreach (var file in Directory.GetFiles(records))
                {
                    var name = Path.GetFileName(file);
                    if (!IsUnityMeta(records, name)) plan.Add((RecordsFolder + "/" + name, file));
                }

                foreach (var sub in Directory.GetDirectories(records))
                {
                    var name = Path.GetFileName(sub);
                    if (leftOut != null && leftOut.Contains(Fold(name))) continue;
                    CollectRecords(sub, RecordsFolder + "/" + name, plan);
                }
            }

            // By code point, never the file system's order: NTFS lists case-insensitively, ext4 in hash order, and
            // the archive's bytes must not depend on which one packed it.
            plan.Sort((a, b) => CompareByCodePoint(a.relative, b.relative));
            return plan;
        }

        /// <summary>
        /// Whether a planned file is an element file a share leaves out (folder spec §4.8): a <c>.json</c> file (any case)
        /// directly in a type folder of <c>elements/</c> or <c>spatial/</c>, whose JSON is an object carrying the mark or a
        /// doubt. Not UTF-8, not JSON, or not an object: no element, kept. The npm writer's <c>isHiddenElement</c>.
        /// </summary>
        internal static bool IsHiddenElementFile(string relative, string source)
        {
            var parts = relative.Split('/');
            if (parts.Length != 3 || (parts[0] != "elements" && parts[0] != "spatial")
                || !parts[2].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                var text = StrictUtf8.GetString(File.ReadAllBytes(source));
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
                return JToken.Parse(text) is JObject body && OWPayload.IsHidden(body);
            }
            catch (DecoderFallbackException) { return false; }
            catch (Newtonsoft.Json.JsonException) { return false; }
        }

        /// <summary>
        /// Orders two names by code point, which is the order of their UTF-8 bytes (folder spec §4.7, OW-13), where
        /// <see cref="string.CompareOrdinal(string, string)"/> orders UTF-16 code units.
        /// </summary>
        /// <remarks>
        /// The two differ only where a surrogate (a code point past U+FFFF, such as an emoji) meets a unit from
        /// U+E000 to U+FFFF (a fullwidth letter, a private-use character): UTF-16 puts the surrogate first, code point
        /// order puts it last. Shifting the surrogates above that range and that range down below them gives code
        /// point order unit by unit, with no decoding.
        /// </remarks>
        private static int CompareByCodePoint(string a, string b)
        {
            var shared = Math.Min(a.Length, b.Length);
            for (var i = 0; i < shared; i++)
            {
                if (a[i] != b[i]) return InCodePointOrder(a[i]) - InCodePointOrder(b[i]);
            }

            return a.Length - b.Length;
        }

        private static int InCodePointOrder(char unit)
            => unit < 0xD800 ? unit : unit >= 0xE000 ? unit - 0x800 : unit + 0x2000;

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
        /// The producers <c>records/RECORDS.json</c> marks <c>"private": true</c> (§4.5 rule 11), each by OW-18's
        /// <see cref="Fold"/>, which is how a directory is matched to its entry (§4.7, OW-8): <c>Fold</c> names
        /// <c>fold/</c>, and a decomposed <c>é</c> the composed one.
        /// </summary>
        /// <remarks>
        /// No manifest means no private producer: a world from before rule 11 packs as it always did. A manifest the
        /// writer cannot read refuses the pack and leaves nothing out by guess, since leaving a producer's notes in by
        /// mistake is the harm the rule is for: one that is not UTF-8 JSON (a byte order mark is read past), not an
        /// array of objects, or marks private an entry whose <c>producer</c> is not one directory name. An entry whose
        /// <c>private</c> is anything but the boolean <c>true</c> marks nothing.
        /// </remarks>
        private static HashSet<string> PrivateProducers(string recordsDir)
        {
            var producers = new HashSet<string>(StringComparer.Ordinal);
            var path = Path.Combine(recordsDir, RecordsManifest);
            if (!File.Exists(path)) return producers;

            JArray manifest;
            try
            {
                var text = StrictUtf8.GetString(File.ReadAllBytes(path));
                manifest = JToken.Parse(text.Length > 0 && text[0] == (char)0xFEFF ? text.Substring(1) : text) as JArray;
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException(
                    $"{RecordsFolder}/{RecordsManifest} in '{recordsDir}' is not valid JSON, so which records are "
                    + "private cannot be told; fix it, or pack with includePrivateRecords.", e);
            }

            if (manifest == null)
            {
                throw new OWFolderFormatException(
                    $"{RecordsFolder}/{RecordsManifest} in '{recordsDir}' is not a JSON array (folder spec §4.5 rule 3), "
                    + "so which records are private cannot be told; fix it, or pack with includePrivateRecords.");
            }

            foreach (var token in manifest)
            {
                if (!(token is JObject entry))
                {
                    throw new OWFolderFormatException(
                        $"{RecordsFolder}/{RecordsManifest} in '{recordsDir}' holds an entry that is not an object, so "
                        + "which records are private cannot be told; fix it, or pack with includePrivateRecords.");
                }

                if (!(entry["private"] is JValue mark && mark.Type == JTokenType.Boolean && (bool)mark)) continue;

                var name = entry["producer"] is JValue value && value.Type == JTokenType.String ? (string)value : null;
                if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(PathSeparators) >= 0)
                {
                    throw new OWFolderFormatException(
                        $"{RecordsFolder}/{RecordsManifest} in '{recordsDir}' marks private an entry whose producer "
                        + $"({entry["producer"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "none"}) is not one directory name, so "
                        + "which records are private cannot be told; fix it, or pack with includePrivateRecords.");
                }

                producers.Add(Fold(name));
            }

            return producers;
        }

        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>The records manifest at the root of <c>records/</c> (folder spec §4.5 rule 3).</summary>
        private const string RecordsManifest = "RECORDS.json";

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

            if (keepApiBlock || world.Property(ApiBlock) == null) return bytes;

            world.Remove(ApiBlock);
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
        /// No world (its <see cref="OWFolderFormatException.Reason"/> <c>not_a_world</c>), or more than one
        /// (<c>more_than_one</c>). Never a guess: a zip with two worlds in it opened as one of them is the wrong world
        /// with a correct-looking UI, the failure the cache's key exists to prevent.
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
                throw new OWFolderFormatException(OWWorldArchiveReason.NotAWorld,
                    "No world.json in the archive. An .owworld holds one world: world.json at its root, or one "
                    + "top-level folder holding world.json.");
            }

            var named = string.Join(", ", roots.Select(r => r.Length == 0 ? "(root)" : r.TrimEnd('/')));
            throw new OWFolderFormatException(OWWorldArchiveReason.MoreThanOne,
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
        /// (<c>elements/character/x.json</c>, forward slashes). Its <c>World</c> never carries an <c>api</c> block:
        /// one in the archive is stripped (folder spec §4.7, OW-10), so the result is never
        /// <see cref="OWFolderReadResult.IsSyncable"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// In memory because extracting to disk was nearly the whole cost: Tuvsh Ul's 3,832 entries took 5.5-8 s
        /// through a temp folder, against 0.33 s to unzip in memory (harness, 2026-10-20). <c>media/</c> and
        /// <c>records/</c> are not returned (nothing reads them yet), but every file in the archive is checked against
        /// its size and CRC-32, as the TypeScript reader checks them: a corrupt image refuses the archive.
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
        /// passes the budget. The TypeScript reader's rule; the default numbers are the spec's floor. An entry that is
        /// read must also hold exactly the bytes it declares and match its CRC-32 (OW-14), or the archive is refused
        /// (<c>not_openable</c>), never read as other text; so must every other file in the archive, which is checked
        /// through one buffer and never held.
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
        /// <para>
        /// A <c>mimetype</c> entry at the archive's root is the marker (OW-6): never required, and when present it
        /// must hold exactly <see cref="MediaType"/>, or the archive is refused. Its shape (first, stored) is a
        /// writer's rule, not checked here.
        /// </para>
        /// </remarks>
        /// <exception cref="FileNotFoundException">The archive does not exist.</exception>
        /// <exception cref="OWFolderFormatException">
        /// Its <see cref="OWFolderFormatException.Reason"/> names the cause (<see cref="OWWorldArchiveReason"/>). Not a
        /// zip; an entry name that is not a plain relative path, or is repeated, colliding or unsafe; a path both a
        /// file and a folder, a link, an encrypted entry or one in a method other than stored or deflate; past the
        /// budget, or an entry inflating past its declared size; an entry that is broken, short or fails its CRC-32,
        /// or whose deflate stream does not end at the compressed size it declares;
        /// a marker holding other bytes; no world or more than
        /// one in it, a nested one included; or the world's own <c>world.json</c> is unreadable.
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
                throw new OWFolderFormatException(OWWorldArchiveReason.NotOpenable,
                    $"'{archivePath}' is not a zip archive.", e);
            }

            // The deflate walk (EndsWhereItDeclares) reads each entry's raw bytes through a stream of its own: entries
            // are walked in the archive's order, so it reads on from where the last one ended, never seeking, where on
            // the zip's own stream every entry would cost two seeks and the reads after them (measured on Sikelia).
            using (zip)
            using (var raw = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, RawBufferSize))
            {
                // What ZipArchiveEntry leaves out (a name's raw bytes and flags, the method, a Unix mode) is read from
                // the central directory by hand, one record per entry, in the same order. The zip reads its own first.
                var entryCount = zip.Entries.Count;
                var directory = ReadCentralDirectory(stream);
                if (directory == null || directory.Count != entryCount)
                {
                    throw new OWFolderFormatException(OWWorldArchiveReason.NotOpenable,
                        $"'{archivePath}' has a central directory that cannot be walked entry by entry; refusing the archive.");
                }

                // Every name and the budget are checked against the archive first, before the world is even found
                // and before one byte is inflated: the central directory gives each entry's name and declared size.
                var named = new List<(ZipArchiveEntry entry, string path, DirectoryRecord record)>(zip.Entries.Count);
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
                    if (fault != null) throw Hostile(OWWorldArchiveReason.UnsafePath, entry.FullName, fault);

                    CheckEntry(directory[i], entry.FullName, path);

                    // A stored entry holds its bytes as they are, so its stored size is its size: one that holds more
                    // than it declares lies as a zip bomb does (OW-14), found here without reading it.
                    if (directory[i].Method == MethodStored && entry.CompressedLength != entry.Length)
                    {
                        throw Hostile(
                            entry.CompressedLength > entry.Length ? OWWorldArchiveReason.TooBig : OWWorldArchiveReason.NotOpenable,
                            entry.FullName, $"is stored and declares {entry.Length} bytes but holds {entry.CompressedLength}");
                    }

                    if (!names.Add(isFolder ? path + "/" : path))
                        throw Hostile(OWWorldArchiveReason.Duplicate, entry.FullName, $"is a second entry named '{path}'");
                    CheckCollision(path, entry.FullName, folded);
                    AddFolders(path, isFolder, folderPaths);
                    if (!isFolder) filePaths.Add(path);

                    if (isFolder) continue;

                    if (++files > budget.MaxEntries)
                    {
                        throw PastBudget(OWWorldArchiveReason.TooMany, archivePath,
                            $"holds more than {budget.MaxEntries} files", nameof(budget.MaxEntries));
                    }

                    // Compared by what is left, never by adding: two declared sizes can overflow a long (and a zip64
                    // size past long's range reads negative).
                    if (entry.Length < 0 || entry.Length > budget.MaxBytes - declared)
                    {
                        throw PastBudget(OWWorldArchiveReason.TooBig, archivePath,
                            $"declares more than {budget.MaxBytes} bytes uncompressed", nameof(budget.MaxBytes));
                    }

                    declared += entry.Length;
                    named.Add((entry, path, directory[i]));
                }

                // A path that is a file and also a folder of another path cannot both land on a disk.
                foreach (var folder in folderPaths)
                {
                    if (filePaths.Contains(folder))
                    {
                        throw new OWFolderFormatException(OWWorldArchiveReason.UnsafePath,
                            $"The archive's path '{folder}' is both a file and a folder; refusing the archive.");
                    }
                }

                // The marker (OW-6) is never required, but one that is there must say exactly what the file is.
                foreach (var (entry, path, record) in named)
                {
                    if (path == MarkerEntry && !HoldsTheMediaType(raw, entry, record))
                    {
                        throw Hostile(OWWorldArchiveReason.Marker, entry.FullName,
                            $"is the marker but does not hold exactly '{MediaType}' (ASCII, no newline)");
                    }
                }

                var root = FindWorldRoot(named.Select(n => n.path));

                // Then the world's own entries: every name is already a plain path, so its place is its name.
                foreach (var (entry, path, _) in named)
                {
                    if (!path.StartsWith(root, StringComparison.Ordinal)) continue;
                    var relative = path.Substring(root.Length);

                    // records/ is a producer's own data, which may hold a world.json; anywhere else it is a second world.
                    if (relative != WorldFile && relative.EndsWith("/" + WorldFile, StringComparison.Ordinal)
                        && !relative.StartsWith(RecordsFolder + "/", StringComparison.Ordinal))
                    {
                        throw new OWFolderFormatException(OWWorldArchiveReason.MoreThanOne,
                            $"The archive holds a second world.json inside its world, at '{entry.FullName}'. One world "
                            + "per archive: inside a world, a world.json belongs only under records/; refusing the archive.");
                    }
                }

                // Every name and the shape are settled before one byte of content is inflated, as the TypeScript reader
                // orders it, so an archive with two faults is refused for the same one by every reader.
                // Every file is checked against its headers, in the archive's order; only the world's read entries are
                // kept. The rest (media/, records/, what sits beside the world) are inflated through one buffer and let
                // go: a corrupt image refuses the archive as a corrupt element does (§4.7: "an entry whose bytes fail its
                // CRC"), and holding them costs no memory.
                var entries = new List<KeyValuePair<string, string>>();
                foreach (var (entry, path, record) in named)
                {
                    if (path == MarkerEntry) continue; // checked above
                    var relative = path.StartsWith(root, StringComparison.Ordinal) ? path.Substring(root.Length) : null;
                    if (relative == null || !IsRead(relative, includeLegacySpatial))
                    {
                        Inflate(raw, entry, record, keep: false);
                        continue;
                    }

                    entries.Add(new KeyValuePair<string, string>(relative, ReadText(raw, entry, record)));
                }

                OWFolderReadResult read;
                try
                {
                    read = OWFolderReader.ReadEntries(entries, includeLegacySpatial, archivePath);
                }
                catch (OWFolderFormatException e)
                {
                    // The folder read refuses only the world's own world.json: unparseable, not an object, no id.
                    throw new OWFolderFormatException(OWWorldArchiveReason.NotAWorld, e.Message, e);
                }

                // OW-10: an archive is a copy made to be shared, so an api block in it is a key in the wrong place.
                // It is stripped and never returned (so the world reads as unlinked), and the file is still read.
                read.World?.Remove(ApiBlock);
                return read;
            }
        }

        /// <summary><c>world.json</c>'s key holding a linked folder's API key: never written to an archive, never read from one.</summary>
        private const string ApiBlock = "api";

        /// <summary>
        /// True when the marker entry holds exactly <see cref="MediaType"/>'s bytes. Its declared size is compared
        /// first, so a marker of another length is never inflated.
        /// </summary>
        /// <exception cref="OWFolderFormatException">The marker is a broken entry, as <see cref="Inflate"/> refuses one.</exception>
        private static bool HoldsTheMediaType(Stream archive, ZipArchiveEntry entry, DirectoryRecord record)
        {
            if (entry.Length != MediaTypeBytes.Length) return false;
            return Inflate(archive, entry, record).ToArray().SequenceEqual(MediaTypeBytes);
        }

        /// <summary>True for the paths the folder reader looks at: world.json, elements/, spatial/.</summary>
        private static bool IsRead(string relative, bool includeLegacySpatial)
            => relative == WorldFile
               || relative.StartsWith("elements/", StringComparison.Ordinal)
               || (includeLegacySpatial && relative.StartsWith("spatial/", StringComparison.Ordinal));

        /// <summary>An entry's text as <c>File.ReadAllText</c> reads a file (UTF-8, BOM honoured).</summary>
        /// <exception cref="OWFolderFormatException">The entry is broken or lies about its size (<see cref="Inflate"/>).</exception>
        private static string ReadText(Stream archive, ZipArchiveEntry entry, DirectoryRecord record)
        {
            using (var reader = new StreamReader(Inflate(archive, entry, record))) return reader.ReadToEnd();
        }

        /// <summary>
        /// An entry's bytes, inflated at most to the size it declares and checked against its headers: every byte is
        /// the one the zip was made with, or the archive is refused.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The declared sizes were held to the budget before any entry was opened, so stopping each entry at its own
        /// keeps the whole read inside the budget, whatever the runtime's inflater does with a size that lies.
        /// </para>
        /// <para>
        /// Folder spec §4.7 (OW-14): an entry whose bytes fail its CRC refuses the archive. Mono's
        /// <see cref="ZipArchive"/> never compares the CRC-32 it is given, so a flipped bit in a deflate stream reads
        /// as other text (an element with a broken id) with no error at all. The CRC is taken here in the same pass that
        /// copies the bytes, one table lookup a byte, and compared with the central directory's.
        /// </para>
        /// <para>
        /// The size bullet speaks of the compressed size too (Skeld h58 #11968): a deflated entry's stream must end in
        /// the last byte of the compressed size it declares. Mono's inflater stops at a stream's end and ignores what
        /// follows, and at the end of its input it stops as if the stream were whole, so neither a stream ending early
        /// nor one that never ends is an error to it. <see cref="EndsWhereItDeclares"/> walks the raw bytes to find
        /// the end, once the bytes have passed every check above.
        /// </para>
        /// </remarks>
        /// <exception cref="OWFolderFormatException">
        /// <c>too_big</c> when it inflates past the size it declares; <c>not_openable</c> when its deflate stream is
        /// broken, when it holds fewer bytes than it declares, when its bytes fail its CRC-32, or when its deflate
        /// stream does not end in the last byte of its declared compressed size. A broken entry is never skipped as an
        /// unreadable file would be in a folder: it is a broken archive, and the TypeScript reader and the reference
        /// checker refuse it.
        /// </exception>
        /// <param name="archive">The archive's file, read in entry order for the entry's raw bytes (<see cref="EndsWhereItDeclares"/>).</param>
        /// <param name="entry">The entry.</param>
        /// <param name="record">Its central-directory record, for the CRC-32 and the local header.</param>
        /// <param name="keep">False checks the entry and keeps nothing: the result is null.</param>
        private static MemoryStream Inflate(Stream archive, ZipArchiveEntry entry, DirectoryRecord record, bool keep = true)
        {
            var held = keep ? new MemoryStream() : null;
            var buffer = new byte[81920];
            var crc = Crc32Start;
            long total = 0;
            try
            {
                using (var input = entry.Open())
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (read > entry.Length - total)
                            throw Hostile(OWWorldArchiveReason.TooBig, entry.FullName, $"inflates past the {entry.Length} bytes it declares");
                        crc = Crc32Update(crc, buffer, read);
                        held?.Write(buffer, 0, read);
                        total += read;
                    }
                }
            }
            catch (OWFolderFormatException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException(OWWorldArchiveReason.NotOpenable,
                    $"The archive's entry '{entry.FullName}' cannot be inflated ({e.GetType().Name}: {e.Message}); refusing the archive.", e);
            }

            if (total != entry.Length)
            {
                throw Hostile(OWWorldArchiveReason.NotOpenable, entry.FullName,
                    $"holds {total} bytes and declares {entry.Length}");
            }

            var actual = ~crc;
            if (actual != record.Crc32)
            {
                throw Hostile(OWWorldArchiveReason.NotOpenable, entry.FullName,
                    $"fails its CRC-32 (its bytes give {actual:x8}, its header says {record.Crc32:x8})");
            }

            if (record.Method == MethodDeflate) EndsWhereItDeclares(archive, entry, record);

            if (held != null) held.Position = 0;
            return held;
        }

        /// <summary>
        /// Refuses a deflated entry whose stream does not end in the last byte of the compressed size it declares
        /// (folder spec §4.7's size bullet, OW-14): one that ends before it, with bytes after its end that are no part
        /// of it, and one that never ends inside it. The bits after the end in that last byte are padding.
        /// </summary>
        /// <exception cref="OWFolderFormatException"><c>not_openable</c>.</exception>
        /// <param name="raw">The archive's file, read in entry order: positioned where the last walk ended, it is not sought.</param>
        private static void EndsWhereItDeclares(Stream raw, ZipArchiveEntry entry, DirectoryRecord record)
        {
            var declared = entry.CompressedLength;
            long used;
            try
            {
                // The zip has opened this local header already (its signature checked), so only its lengths are read,
                // and the name and extra field are read through rather than sought past.
                if (record.LocalHeader < 0) throw new InvalidDataException("its local header cannot be found");
                if (raw.Position != record.LocalHeader) raw.Seek(record.LocalHeader, SeekOrigin.Begin);
                var header = ReadExactly(raw, LocalHeaderSize);
                ReadExactly(raw, U16(header, 26) + U16(header, 28));
                used = OWDeflateEnd.Measure(raw, declared);
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException)
            {
                throw new OWFolderFormatException(OWWorldArchiveReason.NotOpenable,
                    $"The archive's entry '{entry.FullName}' cannot be read to the {declared} compressed bytes it declares "
                    + $"({e.Message}); refusing the archive.", e);
            }

            if (used != declared)
            {
                throw Hostile(OWWorldArchiveReason.NotOpenable, entry.FullName,
                    $"has a deflate stream that ends {declared - used} bytes before the {declared} compressed bytes it declares");
            }
        }

        private const int LocalHeaderSize = 30;

        /// <summary>The walk's stream's buffer: most entries are a few hundred bytes, so one read holds many.</summary>
        private const int RawBufferSize = 1 << 16;

        // -- CRC-32 (the zip format's: IEEE 802.3, reflected, polynomial 0xEDB88320) --------

        private const uint Crc32Start = 0xFFFFFFFFu;

        private static readonly uint[] Crc32Table = MakeCrc32Table();

        private static uint[] MakeCrc32Table()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            return table;
        }

        /// <summary>Runs <paramref name="crc"/> over <paramref name="count"/> bytes; start at <see cref="Crc32Start"/>, finish with <c>~</c>.</summary>
        private static uint Crc32Update(uint crc, byte[] bytes, int count)
        {
            for (var i = 0; i < count; i++) crc = Crc32Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static OWFolderFormatException PastBudget(string reason, string archivePath, string what, string option)
            => new OWFolderFormatException(reason,
                $"'{archivePath}' {what}, past the reader's budget (OWWorldArchiveReadOptions.{option}); "
                + "refusing the archive.");

        private static OWFolderFormatException Hostile(string reason, string entryName, string why)
            => new OWFolderFormatException(reason, $"The archive's entry '{entryName}' {why}; refusing the archive.");

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
            if (kind == UnixSymlink) throw Hostile(OWWorldArchiveReason.Link, entryName, "is a symbolic link");
            if (kind != 0 && kind != UnixFile && kind != UnixFolder)
            {
                throw Hostile(OWWorldArchiveReason.Link, entryName,
                    $"is a special file (Unix type {Convert.ToString(kind, 8)}), neither a file nor a folder");
            }

            // Bit 0 is encryption (ZipCrypto, or AES as method 99), bit 6 strong encryption.
            if ((record.Flags & 0x0041) != 0)
                throw Hostile(OWWorldArchiveReason.Method, entryName, "is encrypted; an .owworld is never encrypted");

            if (record.Method != MethodStored && record.Method != MethodDeflate)
            {
                throw Hostile(OWWorldArchiveReason.Method, entryName,
                    $"is compressed with method {record.Method}; an .owworld holds only stored (0) and deflated (8) entries");
            }

            // Without bit 11 a name's bytes are in a code page the zip does not name: past ASCII, its spelling is a guess.
            if (record.NonAscii && (record.Flags & 0x0800) == 0)
            {
                throw Hostile(OWWorldArchiveReason.Name, entryName,
                    "has a name that is not ASCII and lacks the zip's UTF-8 flag, so its spelling is a guess");
            }

            // With bit 11 the bytes must be UTF-8; the runtime would read bad ones as U+FFFD, another name than written.
            if (record.NotUtf8 && (record.Flags & 0x0800) != 0)
            {
                throw Hostile(OWWorldArchiveReason.Name, entryName,
                    "has the zip's UTF-8 flag but a name whose bytes are not UTF-8, so its spelling is a guess");
            }

            var fault = NameFault(path);
            if (fault != null) throw Hostile(OWWorldArchiveReason.Name, entryName, fault);
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
                    throw Hostile(OWWorldArchiveReason.Collision, entryName,
                        $"names '{prefix}', which is '{first}' once case and Unicode normalization are ignored (one name on "
                        + "Windows and macOS)");
                }
            }
        }

        /// <summary>
        /// One fold for a name, the one every reader uses (folder spec §4.7, OW-18): Unicode NFC, then simple case
        /// folding from Unicode's own table (<see cref="OWCaseFold"/>), so <c>ς</c> meets <c>σ</c>, <c>ſ</c> meets
        /// <c>s</c>, and <c>ß</c> stays apart from <c>ss</c>, on every runtime.
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

            return OWCaseFold.Fold(name);
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

            /// <summary>The CRC-32 of the entry's bytes, as the central directory records it.</summary>
            public uint Crc32;

            /// <summary>Where the entry's local header starts in the file (its zip64 form when the record carries one); -1 when unknown.</summary>
            public long LocalHeader = -1;

            /// <summary>A name byte past 0x7F.</summary>
            public bool NonAscii;

            /// <summary>The name's bytes are not well-formed UTF-8 (checked only when they are not ASCII).</summary>
            public bool NotUtf8;
        }

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

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

                    var notUtf8 = false;
                    if (nonAscii)
                    {
                        try { StrictUtf8.GetString(directory, pos + DirectoryRecordSize, nameLength); }
                        catch (ArgumentException) { notUtf8 = true; } // DecoderFallbackException is one
                    }

                    records.Add(new DirectoryRecord
                    {
                        Flags = U16(directory, pos + 8),
                        Method = U16(directory, pos + 10),
                        ExternalAttributes = U32(directory, pos + 38),
                        Crc32 = U32(directory, pos + 16),
                        LocalHeader = LocalHeaderOf(directory, pos, nameLength),
                        NonAscii = nonAscii,
                        NotUtf8 = notUtf8,
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

        /// <summary>
        /// A central record's local-header offset (field 42), or its zip64 form: when the field is <c>0xFFFFFFFF</c>
        /// the zip64 extra field (tag 1) holds it, after the uncompressed and compressed sizes when those fields are
        /// <c>0xFFFFFFFF</c> too (APPNOTE 4.5.3), as <see cref="ZipArchive"/> reads it. -1 when it cannot be found.
        /// </summary>
        private static long LocalHeaderOf(byte[] directory, int pos, int nameLength)
        {
            long offset = U32(directory, pos + 42);
            if (offset != uint.MaxValue) return offset;

            var skip = (U32(directory, pos + 24) == uint.MaxValue ? 8 : 0) + (U32(directory, pos + 20) == uint.MaxValue ? 8 : 0);
            var at = pos + DirectoryRecordSize + nameLength;
            var end = at + U16(directory, pos + 30);
            while (at + 4 <= end)
            {
                var tag = U16(directory, at);
                var size = U16(directory, at + 2);
                if (at + 4 + size > end) return -1;
                if (tag == 1) return size >= skip + 8 ? (long)U64(directory, at + 4 + skip) : -1;
                at += 4 + size;
            }

            return -1;
        }

        private const int EndRecordSize = 22;
        private const int Zip64LocatorSize = 20;
        private const int Zip64EndRecordSize = 56;
        private const int DirectoryRecordSize = 46;

        /// <summary><paramref name="count"/> bytes from <paramref name="offset"/>; an <see cref="IOException"/> when the file is shorter.</summary>
        private static byte[] ReadAt(Stream stream, long offset, int count)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            return ReadExactly(stream, count);
        }

        /// <summary>The next <paramref name="count"/> bytes; an <see cref="EndOfStreamException"/> when the file is shorter.</summary>
        private static byte[] ReadExactly(Stream stream, int count)
        {
            var bytes = new byte[count];
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
