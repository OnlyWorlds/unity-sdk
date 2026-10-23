using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace OnlyWorlds.Sdk
{
    /// <summary>What <see cref="OWFolderExport.ExportAsync"/> wrote.</summary>
    public sealed class OWFolderExportReport
    {
        /// <summary>The folder written, absolute.</summary>
        public string Folder;

        /// <summary>The world's id and name, from its <c>world.json</c>.</summary>
        public string WorldId;
        public string WorldName;

        /// <summary>Elements written per type (the element type's slug), types with none left out.</summary>
        public Dictionary<string, int> Counts = new Dictionary<string, int>();

        /// <summary>Media files written, absolute: each Map's image, as served, and its sidecar.</summary>
        public List<string> Media = new List<string>();

        /// <summary>
        /// Map images that could not be kept, one line each (<c>"&lt;map name&gt;: &lt;reason&gt;"</c>). The export
        /// still succeeds: a world opens without a picture, but never without its elements.
        /// </summary>
        public List<string> MediaProblems = new List<string>();

        /// <summary>Every element written.</summary>
        public int Total
        {
            get
            {
                var total = 0;
                foreach (var n in Counts.Values) total += n;
                return total;
            }
        }
    }

    /// <summary>One step of an export, for a progress line.</summary>
    public struct OWFolderExportProgress
    {
        /// <summary><c>"world"</c>, <c>"elements"</c> or <c>"media"</c>.</summary>
        public string Stage;

        /// <summary>The element type being read, in the <c>"elements"</c> stage.</summary>
        public string Type;

        /// <summary>Steps done and steps known in this stage.</summary>
        public int Done;
        public int Total;
    }

    /// <summary>How <see cref="OWFolderExport.ExportAsync"/> runs.</summary>
    public sealed class OWFolderExportOptions
    {
        /// <summary>
        /// Fetches a media file's bytes by its URL. Null uses a UnityWebRequest GET that names itself in
        /// <c>User-Agent</c>, on Unity's main thread as <see cref="UnityWebRequestTransport"/> sends.
        /// </summary>
        /// <remarks>
        /// The media host refuses some library agents outright (Python's <c>urllib</c> default is a 403;
        /// none at all, curl's or UnityWebRequest's is served, measured 2026-10-16), so the default
        /// says who it is rather than hoping its agent is not on a list. The function is given only
        /// the URL: it never sees the key or the PIN.
        /// </remarks>
        public Func<string, CancellationToken, Task<byte[]>> FetchMedia;

        /// <summary>Keep each Map's image as local media. On by default.</summary>
        public bool IncludeMedia = true;

        /// <summary>The export's clock for <c>source.at</c>. Null reads the system's (a test pins it).</summary>
        public Func<DateTimeOffset> Clock;
    }

    /// <summary>
    /// An account world written to a world folder, read-only: the folder the onlyworlds Python package's
    /// <c>export_world</c> writes, elements byte for byte, with each Map's image kept the way Atlas keeps local media.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second exporter of one format, so it is a port, not a design: <c>world_for_disk</c> and
    /// <c>write_folder</c> from the package's <c>folder.py</c>, rule for rule, and the media step from
    /// <c>tools/ow-export</c>'s <c>write_base_image</c>. Where this class and the package could disagree
    /// they must not, and the place they would is named on the member that guards it.
    /// </para>
    /// <para>
    /// <b>A folder is born whole or not at all.</b> The tree is built in a sibling temp directory
    /// (<c>.ow-&lt;8 hex&gt;</c>) and moved into place as the last step, so a cancel, an error or a refusal
    /// from the server leaves the folder as it was (an empty directory, or none) -- never one that looks
    /// complete and is missing a type. A process killed mid-build leaves only that dot-directory beside it.
    /// </para>
    /// <para>
    /// <b>The key and the PIN stay in the client.</b> Nothing here reads them; no file, report line or
    /// exception this class makes carries them. Errors are the client's own: an
    /// <see cref="OWApiError"/> when the server said no, an <see cref="OWTransportError"/> when it never
    /// answered, an <see cref="OperationCanceledException"/> on a cancel.
    /// </para>
    /// </remarks>
    public static class OWFolderExport
    {
        /// <summary>The OW Folder Format version an export declares in <c>world.json</c>.</summary>
        public const string FormatVersion = "0.3.17";

        /// <summary>The name this package publishes under: the <c>tool</c> of the <c>producer</c> line it writes.</summary>
        public const string ProducerTool = "com.onlyworlds.sdk";

        /// <summary>
        /// This package's version, as its <c>package.json</c> says: the <c>version</c> of the <c>producer</c> line it writes.
        /// </summary>
        /// <remarks>
        /// A constant, not read at run time: a player build carries no <c>package.json</c>, and the package manager that
        /// could read it is Editor-only. A test holds it equal to <c>package.json</c>'s <c>version</c>, so a release that
        /// moves one without the other fails.
        /// </remarks>
        public const string ProducerVersion = "0.18.0";

        /// <summary>
        /// The <c>producer</c> line (folder spec §4.2, v0.3.14) every folder <see cref="ExportAsync"/> makes carries:
        /// <c>{"tool": "com.onlyworlds.sdk", "version": "&lt;this package's&gt;", "input": "server"}</c>, the spec's key
        /// order, <c>server</c> its word for an export from onlyworlds.com. A new object on every read.
        /// </summary>
        public static JObject ExportProducer => new JObject
        {
            ["tool"] = ProducerTool,
            ["version"] = ProducerVersion,
            ["input"] = "server",
        };

        /// <summary>
        /// Wire key to disk key for <c>world.json</c> (spec §5, v0.3.5): the file follows the canonical
        /// schema, and the wire renamed one key the standard never did.
        /// </summary>
        private static readonly Dictionary<string, string> WireToDiskWorldKeys = new Dictionary<string, string>
        {
            { "time_range_current", "time_current" },
        };

        private const string UserAgent = "onlyworlds-unity-sdk";

        /// <summary>
        /// The keys every known-of stub keeps (§4.4, keel's <c>KNOWN_OF_BASE</c>), written even when empty.
        /// </summary>
        public static IReadOnlyCollection<string> KnownOfKeeps => KnownOfKeepSet;

        private static readonly HashSet<string> KnownOfKeepSet = new HashSet<string>(StringComparer.Ordinal)
        {
            "id", "type", "name", "description", "supertype", "subtype", "image_url", "created_at", "updated_at", "change_seq",
        };

        /// <summary>
        /// A known-of stub as §4.4 writes it, its keys in received order, then <c>known_of: true</c>: every
        /// <see cref="KnownOfKeeps"/> key, and every other key the server sent with a value. The input is not changed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The package's <c>known_of_for_disk</c>. The unset fields keel serves on a stub (<c>""</c>, <c>null</c>,
        /// <c>[]</c>) are left out, never written: on disk they would read as a whole element with nothing filled
        /// in. Anything else it sent is kept, whatever the field (a Character's public <c>physicality</c>,
        /// <c>height</c>, <c>species</c>: keel PR 117), since a writer never drops data it has (§3). <c>0</c> and
        /// <c>false</c> are values. This reads the body, never a list of public fields, so a type that gains
        /// public fields needs no change here.
        /// </para>
        /// <para>
        /// The export applies it to each body that <see cref="OWPayload.IsKnownOf"/> calls a stub, and only those:
        /// a whole element is written as served. A stub and a whole body for one id write whichever came last.
        /// </para>
        /// </remarks>
        public static JObject KnownOfForDisk(JObject body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var disk = new JObject();
            foreach (var property in body.Properties())
            {
                if (property.Name == OWPayload.KnownOf) continue;
                if (KnownOfKeepSet.Contains(property.Name) || IsSet(property.Value)) disk[property.Name] = property.Value.DeepClone();
            }

            disk[OWPayload.KnownOf] = true;
            return disk;
        }

        /// <summary>A value is anything but <c>""</c>, <c>null</c> and <c>[]</c>: <c>0</c> and <c>false</c> are values.</summary>
        private static bool IsSet(JToken value)
            => !(value.Type == JTokenType.Null
                 || (value.Type == JTokenType.String && ((string)value).Length == 0)
                 || (value.Type == JTokenType.Array && !((JArray)value).HasValues));

        /// <summary>
        /// The <c>world.json</c> body for a wire <c>GET /world/</c> body: every key carried in received order,
        /// <c>time_range_current</c> renamed <c>time_current</c> in place, <c>format_version</c> added when absent.
        /// The input is not changed.
        /// </summary>
        /// <param name="wireWorld">A wire world body, or a world read from a folder.</param>
        /// <param name="counts">
        /// Elements written per type. Read only when the body is a snapshot's (it has <c>snapshot_of</c>),
        /// whose <c>snapshot_counts</c> is recounted from it; null removes the key.
        /// </param>
        /// <exception cref="OWFolderFormatException">
        /// No non-empty string <c>id</c>, or no string <c>name</c> (it may be empty): the two keys a
        /// <c>world.json</c> requires (§4.1).
        /// </exception>
        /// <remarks>
        /// <para>
        /// If both spellings of the now are present, both are kept: dropping either would be a drop
        /// (§4.1: a writer MUST NOT drop, MUST NOT invent).
        /// </para>
        /// <para>
        /// <c>snapshot_counts</c> belongs to snapshots alone (§4.2, v0.3.6) and is never carried forward: a
        /// count copied from a source is a claim about files this writer did not write.
        /// </para>
        /// <para>
        /// A <c>format_version</c> the body already carries is kept as it is, even null: the version is
        /// written at a folder's birth and never rewritten.
        /// </para>
        /// <para>
        /// <c>origin</c> (§4.2, v0.3.16) is the world's first import as the server keeps it, <c>{source, producer}</c>:
        /// carried as given, from <c>GET /world</c> or a read <c>world.json</c>, and never derived from <c>source</c> or
        /// <c>producer</c>, which name this folder's maker. A world that has none gets none. The World's extension keys
        /// (<c>x_</c>, <c>atlas_</c>, <c>shadow_</c>, at the top level of its body) ride along like any other key.
        /// </para>
        /// </remarks>
        public static JObject WorldForDisk(JObject wireWorld, IReadOnlyDictionary<string, int> counts = null)
        {
            if (wireWorld == null) throw new ArgumentNullException(nameof(wireWorld));

            var id = wireWorld["id"];
            if (id == null || id.Type != JTokenType.String || string.IsNullOrEmpty((string)id))
            {
                throw new OWFolderFormatException("world.json requires a non-empty string id (spec §4.1).");
            }

            var name = wireWorld["name"];
            if (name == null || name.Type != JTokenType.String)
            {
                throw new OWFolderFormatException("world.json requires a string name, which may be empty (spec §4.1).");
            }

            var disk = new JObject();
            foreach (var property in wireWorld.Properties())
            {
                // Renamed only when the disk spelling is not also present: with both, each keeps its own.
                var key = WireToDiskWorldKeys.TryGetValue(property.Name, out var diskKey) && !wireWorld.ContainsKey(diskKey)
                    ? diskKey
                    : property.Name;
                disk[key] = property.Value.DeepClone();
            }

            if (disk.ContainsKey("snapshot_counts"))
            {
                if (disk.ContainsKey("snapshot_of") && counts != null)
                {
                    var recount = new JObject();
                    foreach (var pair in counts)
                    {
                        if (pair.Value != 0) recount[pair.Key] = pair.Value;
                    }

                    disk["snapshot_counts"] = recount;
                }
                else
                {
                    disk.Remove("snapshot_counts");
                }
            }

            if (!disk.ContainsKey("format_version")) disk["format_version"] = FormatVersion;
            return disk;
        }

        /// <summary>
        /// Reads the world the client's key reaches and writes it as a world folder at <paramref name="folder"/>.
        /// Only GETs: the world is never written.
        /// </summary>
        /// <param name="client">A client holding the world's key (and PIN, when it needs one).</param>
        /// <param name="folder">Must not exist, or be an empty directory: an export is a folder's birth.</param>
        /// <param name="options">Null for the defaults: media on, fetched with UnityWebRequest.</param>
        /// <param name="progress">
        /// Told each step: <c>"world"</c> once, <c>"elements"</c> per type (all 22, in the standard's order),
        /// <c>"media"</c> per Map with an image. Called from whichever thread the export resumed on, so pass
        /// a <see cref="Progress{T}"/> made on the main thread to touch Unity objects from it.
        /// </param>
        /// <param name="ct">Cancels between pages and between images; the half-built folder is removed.</param>
        /// <returns>What was written, including each Map image that could not be kept.</returns>
        /// <exception cref="IOException">
        /// The folder exists and is not an empty directory; or, on Windows, it is too deep for the files this
        /// world needs (a <see cref="PathTooLongException"/>, thrown after the reads and before any write).
        /// </exception>
        /// <exception cref="OWApiError">The server refused a read (a wrong key or PIN: <see cref="OWApiError.IsAuthError"/>).</exception>
        /// <exception cref="OWTransportError">A read never got an answer.</exception>
        /// <exception cref="OWFolderFormatException">
        /// The world or an element cannot be written as the format requires: no id, or one id under two types.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The world's elements are all read before the first file is written, as the package does, so a
        /// refusal on the last type costs no disk. Within a type a repeated id keeps its last body: a page
        /// walk that met a write mid-way serves an element twice, never skips one (§4.2), and the later copy
        /// is the newer.
        /// </para>
        /// <para>
        /// A Map whose image cannot be fetched or written is a line in
        /// <see cref="OWFolderExportReport.MediaProblems"/>, not a failure. The image lands at
        /// <c>media/&lt;map id&gt;/base-image/&lt;filename as served&gt;</c> with a <c>&lt;media id&gt;.meta.json</c>
        /// sidecar beside it (Atlas's <c>MediaItem</c>); the Map's body is not touched. Bytes are kept as
        /// served, so a WEBP stays a WEBP: decoding it for Unity is the caller's step.
        /// </para>
        /// </remarks>
        public static async Task<OWFolderExportReport> ExportAsync(
            OWClient client, string folder, OWFolderExportOptions options = null,
            IProgress<OWFolderExportProgress> progress = null, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            var root = RootOf(folder, out var parent);
            RefuseOccupied(root);

            // -- Read everything first ---------------------------------------------------------------

            var at = (options?.Clock ?? (() => DateTimeOffset.UtcNow))();
            progress?.Report(new OWFolderExportProgress { Stage = "world", Done = 0, Total = 1 });
            var wireWorld = await client.GetWorldAsync(ct).ConfigureAwait(false);
            if (wireWorld == null) throw new OWFolderFormatException("GET /world/ returned no world object.");
            var slice = await ExportedAsAsync(client, ct).ConfigureAwait(false);
            progress?.Report(new OWFolderExportProgress { Stage = "world", Done = 1, Total = 1 });

            // The provenance lines (§4.2, v0.3.9 and v0.3.14) after the wire's keys, as the package's export_world adds
            // them. An export is a folder's birth, so source and producer are always this export's own: a body that
            // already carries either (keel keeps them from a folder upload) names the maker of another folder, and its
            // line is replaced where it stands. The body's origin (v0.3.16), the world's first import, is the server's and
            // rides along as served; none served, none written.
            wireWorld = (JObject)wireWorld.DeepClone();
            wireWorld["source"] = new JObject
            {
                ["server"] = new Uri(client.BaseUrl).GetLeftPart(UriPartial.Authority),
                ["world"] = wireWorld["id"]?.DeepClone(),
                ["at"] = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            };
            if (slice != null) wireWorld["exported_as"] = slice;
            wireWorld["producer"] = ExportProducer;

            var types = OWElementTypes.Slugs;
            var byType = new Dictionary<string, List<JObject>>();
            for (var i = 0; i < types.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new OWFolderExportProgress { Stage = "elements", Type = types[i], Done = i, Total = types.Length });
                var rows = await client.ListAllAsync<JObject>(types[i], ct: ct).ConfigureAwait(false);
                if (rows.Count > 0) byType[types[i]] = rows;
            }

            progress?.Report(new OWFolderExportProgress { Stage = "elements", Done = types.Length, Total = types.Length });

            return await WriteAsync(wireWorld, byType, root, parent, options, progress, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The portal's one-file export (<see cref="OWEnvelopeReader"/>, the file an owner downloads from a world's page)
        /// written as a world folder at <paramref name="folder"/>: the same folder <see cref="ExportAsync"/> writes from the
        /// API, each Map's image fetched from its <c>image_url</c> (the file carries none).
        /// </summary>
        /// <param name="path">The downloaded file.</param>
        /// <param name="folder">Must not exist, or be an empty directory: an export is a folder's birth.</param>
        /// <param name="options">Null for the defaults: media on, fetched with UnityWebRequest.</param>
        /// <param name="progress">
        /// Told <c>"world"</c> once, <c>"elements"</c> once when the file is read (Done = Total), <c>"media"</c> per Map
        /// with an image, as <see cref="ExportAsync"/> tells them.
        /// </param>
        /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
        /// <exception cref="OWFolderFormatException">
        /// The file is not an export (<see cref="OWEnvelopeReader.Parse"/>), or it holds what a folder cannot keep whole:
        /// an element the reader skipped (no id), a type that is not one of the standard's, or one id under two types.
        /// </exception>
        /// <exception cref="IOException">As <see cref="ExportAsync"/>: an occupied folder, or one too deep on Windows.</exception>
        /// <remarks>
        /// <para>
        /// The bodies are written as the file holds them and <c>world.json</c> as <see cref="WorldForDisk"/> makes it, so
        /// every key the file's world carries rides along (§4.1: never dropped). An element the reader would skip is a
        /// refusal here, not a skip: a folder is born whole or not at all.
        /// </para>
        /// <para>
        /// The file's maker is the folder's maker: the <c>producer</c> line its world carries (keel's) is written verbatim,
        /// as its <c>source</c> is, and a file whose world carries none gives a folder with none (§4.2, v0.3.14: absent is
        /// the honest value, and a later writer never invents one). Its <c>origin</c> (v0.3.16), the world's first import,
        /// is carried the same way, and a file with none gives a folder with none.
        /// </para>
        /// </remarks>
        public static async Task<OWFolderExportReport> ExportFileAsync(
            string path, string folder, OWFolderExportOptions options = null,
            IProgress<OWFolderExportProgress> progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            var root = RootOf(folder, out var parent);
            RefuseOccupied(root);

            progress?.Report(new OWFolderExportProgress { Stage = "world", Done = 0, Total = 1 });
            var read = OWEnvelopeReader.Read(path);
            progress?.Report(new OWFolderExportProgress { Stage = "world", Done = 1, Total = 1 });

            if (read.Skipped.Count > 0)
            {
                var first = read.Skipped[0];
                throw new OWFolderFormatException(
                    $"{read.Skipped.Count} element(s) in the file cannot be written to a folder, the first {first.Path} ({first.Reason}).");
            }

            var types = OWElementTypes.Slugs;
            var byType = new Dictionary<string, List<JObject>>();
            foreach (var element in read.Elements)
            {
                if (Array.IndexOf(types, element.Type) < 0)
                {
                    throw new OWFolderFormatException(
                        $"{element.Path} is a '{element.Type}', which is not one of the standard's element types: a folder has no place for it.");
                }

                if (!byType.TryGetValue(element.Type, out var list)) byType[element.Type] = list = new List<JObject>();
                list.Add(element.Body);
            }

            progress?.Report(new OWFolderExportProgress { Stage = "elements", Done = types.Length, Total = types.Length });

            return await WriteAsync(read.World, byType, root, parent, options, progress, ct).ConfigureAwait(false);
        }

        /// <summary>The member roles whose slice is the role's, with no Character (<c>co_builder</c> as keel serves it).</summary>
        public static readonly string[] MemberRoles = { "co_builder", "contributor" };

        /// <summary>
        /// The <c>exported_as</c> line (§4.2, v0.3.9) for a <c>GET /me</c> answer, or null: write none. The spec owner's
        /// table (Skeld h50 #7910), as the package's <c>exported_as</c> maps it, rule for rule:
        /// </summary>
        /// <remarks>
        /// <c>kind: owner</c> (an owner-minted or classic key) is the owner, role owner, no Character. A member with role
        /// <c>co_builder</c> or <c>contributor</c> is a member, its role, no Character (a contributor reads the whole world,
        /// so the slice is the role's, not its Character's). A guest of any kind (person, agent seat, player) is a
        /// Character, role guest, <c>{id, name}</c>, the name from <paramref name="characterName"/>. Anything else is null:
        /// no answer, a guest with no Character, a Character whose name cannot be read, a kind or role the table does not
        /// name. The slice is then not known, and the line is absent, never guessed. An empty name is a name the server
        /// served, and is written.
        /// </remarks>
        public static JObject ExportedAs(JObject me, Func<string, string> characterName)
        {
            if (me == null) return null;
            var kind = me["kind"]?.Type == JTokenType.String ? (string)me["kind"] : null;
            var role = me["role"]?.Type == JTokenType.String ? (string)me["role"] : null;
            if (kind == "owner") return new JObject { ["kind"] = "owner", ["role"] = "owner", ["character"] = null };
            if (role != null && Array.IndexOf(MemberRoles, role) >= 0)
                return new JObject { ["kind"] = "member", ["role"] = role, ["character"] = null };
            if (role != "guest") return null;

            var id = me["character"]?.Type == JTokenType.String ? (string)me["character"] : null;
            if (string.IsNullOrEmpty(id)) return null;
            var name = characterName?.Invoke(id);
            if (name == null) return null;
            return new JObject
            {
                ["kind"] = "character", ["role"] = "guest", ["character"] = new JObject { ["id"] = id, ["name"] = name },
            };
        }

        /// <summary>
        /// <see cref="ExportedAs"/> for the client's key: <c>GET /me</c>, and for a guest its Character's name. A refusal, no
        /// answer or a body that is not what was asked makes it null; only a cancel goes through.
        /// </summary>
        private static async Task<JObject> ExportedAsAsync(OWClient client, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            JObject me;
            try { me = await client.GetMeAsync(ct).ConfigureAwait(false); }
            catch (Exception e) when (Unavailable(e)) { return null; }

            string name = null;
            var id = me?["role"]?.ToString() == "guest" && me["character"]?.Type == JTokenType.String ? (string)me["character"] : null;
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    var character = await client.GetAsync<JObject>("character", id, ct).ConfigureAwait(false);
                    name = character?["name"]?.Type == JTokenType.String ? (string)character["name"] : null;
                }
                catch (Exception e) when (Unavailable(e)) { name = null; }
            }

            return ExportedAs(me, _ => name);
        }

        /// <summary>The ways a read is refused or not what was asked: the slice is then unknown, not the export failed.</summary>
        private static bool Unavailable(Exception e) =>
            e is OWApiError || e is OWTransportError || e is Newtonsoft.Json.JsonException || e is InvalidCastException;

        /// <summary>The folder's full path, its separators trimmed, and the directory it is built beside.</summary>
        private static string RootOf(string folder, out string parent)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            parent = Path.GetDirectoryName(root);
            if (string.IsNullOrEmpty(parent))
            {
                throw new ArgumentException($"'{folder}' has no parent directory to build the folder beside.", nameof(folder));
            }

            return root;
        }

        /// <summary>
        /// The write half of an export, whatever the world was read from: the elements deduplicated and named, the tree built
        /// beside <paramref name="root"/>, each Map's image kept, then moved into place.
        /// </summary>
        private static async Task<OWFolderExportReport> WriteAsync(
            JObject wireWorld, Dictionary<string, List<JObject>> byType, string root, string parent,
            OWFolderExportOptions options, IProgress<OWFolderExportProgress> progress, CancellationToken ct)
        {
            options = options ?? new OWFolderExportOptions();
            var types = OWElementTypes.Slugs;
            var elements = Deduplicate(byType);
            var filenames = elements.ToDictionary(pair => pair.Key, pair => ResolveFilenames(pair.Value));
            var withImage = options.IncludeMedia && elements.TryGetValue("map", out var maps)
                ? maps.Select(pair => pair.Value).Where(m => ImageUrlOf(m) != null).ToList()
                : new List<JObject>();
            var report = new OWFolderExportReport { Folder = root };

            // -- Build beside, then move into place ----------------------------------------------------

            // Short, not the package's ".<name>.tmp-<hex>": the tree is built at this path first, and on
            // Windows a temp name longer than the folder's own is what pushes a deep file past the limit.
            var temp = Path.Combine(parent, ".ow-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            RefuseTooLong(Math.Max(root.Length, temp.Length), root, filenames, withImage);

            Directory.CreateDirectory(temp);
            try
            {
                // On a pool thread. UnityWebRequestTransport completes each page on the main thread and the
                // client's continuations run inline, so without this every file of the world would be
                // serialized and written inside one frame.
                await Task.Run(() =>
                {
                    foreach (var type in types)
                    {
                        if (!elements.TryGetValue(type, out var bodies)) continue;

                        var directory = Path.Combine(temp, "elements", type);
                        Directory.CreateDirectory(directory);
                        var names = filenames[type];
                        foreach (var pair in bodies)
                        {
                            var body = OWPayload.IsKnownOf(pair.Value) ? KnownOfForDisk(pair.Value) : pair.Value;
                            WriteFile(Path.Combine(directory, names[pair.Key]), OWFolderWriter.Serialize(body));
                        }

                        report.Counts[type] = bodies.Count;
                    }

                    var world = WorldForDisk(wireWorld, report.Counts);
                    WriteFile(Path.Combine(temp, "world.json"), OWFolderWriter.Serialize(world));
                    report.WorldId = (string)world["id"];
                    report.WorldName = (string)world["name"];
                }, ct).ConfigureAwait(false);

                var media = new List<string>();
                if (withImage.Count > 0)
                {
                    var fetch = options.FetchMedia ?? FetchWithUnityWebRequest;
                    for (var i = 0; i < withImage.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new OWFolderExportProgress { Stage = "media", Done = i, Total = withImage.Count });
                        await KeepBaseImage(temp, withImage[i], fetch, media, report.MediaProblems, ct).ConfigureAwait(false);
                    }

                    progress?.Report(new OWFolderExportProgress { Stage = "media", Done = withImage.Count, Total = withImage.Count });
                }

                ct.ThrowIfCancellationRequested();

                // Checked again: the folder may have been filled while the world was read.
                RefuseOccupied(root);
                if (Directory.Exists(root)) Directory.Delete(root);
                Directory.Move(temp, root);

                foreach (var relative in media) report.Media.Add(Path.Combine(root, relative));
                return report;
            }
            catch
            {
                RemoveQuietly(temp);
                throw;
            }
        }

        /// <summary>Throws unless <paramref name="root"/> is absent or an empty directory.</summary>
        /// <remarks>
        /// A folder is written whole, once (the package's <c>write_folder</c>): writing into one that holds
        /// a world would leave the files of renamed and deleted elements beside the new ones, and the reader,
        /// keyed on id, would serve them as live.
        /// </remarks>
        private static void RefuseOccupied(string root)
        {
            if (File.Exists(root) || (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()))
            {
                throw new IOException(
                    $"'{root}' exists and is not an empty directory; a world folder is written whole, once.");
            }
        }

        /// <summary>
        /// On Windows, throws before the first write when a file or directory the export would make is past
        /// the path limit (259 characters for a file, 247 for a directory).
        /// </summary>
        /// <remarks>
        /// Unity's Mono does not take Windows' long paths, whatever the system's setting (measured
        /// 2026-10-16: a 265-character sidecar path failed with "Could not find a part of the path" on a
        /// machine with <c>LongPathsEnabled</c> on). Left to fail on its own, a deep folder loses its Map images
        /// to unreadable problem lines, or fails half-way with that message; checked here, it fails once,
        /// first, and says what to do. The deepest files are a Map's sidecar and an element under the
        /// full-id fallback, about 100 characters below the folder.
        /// </remarks>
        private static void RefuseTooLong(
            int baseLength, string root, Dictionary<string, Dictionary<string, string>> filenames, List<JObject> withImage)
        {
            if (Path.DirectorySeparatorChar != '\\') return;

            const int fileLimit = 259, directoryLimit = 247;
            var file = "world.json".Length;
            var directory = 0;

            foreach (var type in filenames)
            {
                var typeDirectory = ("elements\\" + type.Key).Length;
                directory = Math.Max(directory, typeDirectory);
                foreach (var name in type.Value.Values) file = Math.Max(file, typeDirectory + 1 + name.Length);
            }

            foreach (var map in withImage)
            {
                var id = (string)map["id"];
                if (!CanNameFolder(id)) continue;

                var mediaDirectory = ("media\\" + id + "\\base-image").Length;
                directory = Math.Max(directory, mediaDirectory);
                var longest = Math.Max(FilenameOf(ImageUrlOf(map), id).Length, Guid.Empty.ToString().Length + ".meta.json".Length);
                file = Math.Max(file, mediaDirectory + 1 + longest);
            }

            var over = Math.Max(baseLength + 1 + file - fileLimit, baseLength + 1 + directory - directoryLimit);
            if (over > 0)
            {
                throw new PathTooLongException(
                    $"'{root}' is too deep for this world: its longest file would be {baseLength + 1 + file} characters, and "
                    + $"Windows allows {fileLimit}. Choose a folder at least {over} characters shorter.");
            }
        }

        /// <summary>True when the id can name one folder, and not climb out of the one it sits in.</summary>
        private static bool CanNameFolder(string id)
            => !string.IsNullOrEmpty(id) && id != "." && id != ".." && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        /// <summary>
        /// Type to (id to body), each id once, bodies in the order they arrived, a repeat keeping its last
        /// body at its last position (the package: <c>slot.pop(eid)</c>, then <c>slot[eid] = body</c>).
        /// </summary>
        private static Dictionary<string, List<KeyValuePair<string, JObject>>> Deduplicate(
            Dictionary<string, List<JObject>> byType)
        {
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            var result = new Dictionary<string, List<KeyValuePair<string, JObject>>>();

            foreach (var type in OWElementTypes.Slugs)
            {
                if (!byType.TryGetValue(type, out var rows)) continue;

                var slot = new List<KeyValuePair<string, JObject>>();
                var at = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var body in rows)
                {
                    var idToken = body?["id"];
                    if (idToken == null || idToken.Type != JTokenType.String || string.IsNullOrEmpty((string)idToken))
                    {
                        throw new OWFolderFormatException($"A {type} element came back without a non-empty string id.");
                    }

                    var id = (string)idToken;
                    if (owner.TryGetValue(id, out var first) && first != type)
                    {
                        throw new OWFolderFormatException($"Element id {id} came back under both {first} and {type}.");
                    }

                    owner[id] = type;
                    if (at.TryGetValue(id, out var index))
                    {
                        slot.RemoveAt(index);
                        at.Clear();
                        for (var i = 0; i < slot.Count; i++) at[slot[i].Key] = i;
                    }

                    at[id] = slot.Count;
                    slot.Add(new KeyValuePair<string, JObject>(id, body));
                }

                if (slot.Count > 0) result[type] = slot;
            }

            return result;
        }

        /// <summary>
        /// Id to filename for one type directory, by the writer collision rule (spec §3.2, MUST).
        /// </summary>
        /// <remarks>
        /// Two elements whose short names land on one path, compared case-folded because NTFS and APFS
        /// resolve them to one file, are settled id-ascending: the lowest id keeps the short form, every
        /// other takes the full id. Whatever order the pages came in, every writer and every re-run picks
        /// the same names. <see cref="OWFolderWriter.WriteElement"/> is not used here: it settles a
        /// collision by which element was written first, and it rereads the whole directory per element.
        /// </remarks>
        private static Dictionary<string, string> ResolveFilenames(List<KeyValuePair<string, JObject>> bodies)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var pair in bodies)
            {
                var name = NameOf(pair.Value);
                names[pair.Key] = name;
                var bucket = OWFolderWriter.ElementFilename(pair.Key, name).ToLowerInvariant();
                if (!buckets.TryGetValue(bucket, out var ids)) buckets[bucket] = ids = new List<string>();
                ids.Add(pair.Key);
            }

            var filenames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ids in buckets.Values)
            {
                ids.Sort(StringComparer.Ordinal);
                for (var i = 0; i < ids.Count; i++)
                {
                    filenames[ids[i]] = i == 0
                        ? OWFolderWriter.ElementFilename(ids[i], names[ids[i]])
                        : OWFolderWriter.ElementFilenameFullId(ids[i], names[ids[i]]);
                }
            }

            return filenames;
        }

        private static string NameOf(JObject body)
        {
            var name = body["name"];
            return name != null && name.Type == JTokenType.String ? (string)name : null;
        }

        // -- Media ---------------------------------------------------------------------------------------

        private static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { ".webp", "image/webp" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
        };

        /// <summary>The Map's <c>image_url</c> when it is a web address, else null.</summary>
        private static string ImageUrlOf(JObject map)
        {
            var url = map["image_url"];
            return url != null && url.Type == JTokenType.String && ((string)url).StartsWith("http", StringComparison.Ordinal)
                ? (string)url
                : null;
        }

        /// <summary>
        /// Fetches one Map's image and writes it with its sidecar under <paramref name="temp"/>; a failure
        /// leaves no file of that Map's behind and becomes a line in <paramref name="problems"/>.
        /// </summary>
        private static async Task KeepBaseImage(
            string temp, JObject map, Func<string, CancellationToken, Task<byte[]>> fetch,
            List<string> media, List<string> problems, CancellationToken ct)
        {
            var id = (string)map["id"];
            var label = NameOf(map);
            if (string.IsNullOrEmpty(label)) label = id;
            var url = ImageUrlOf(map);

            // The id names a folder, so one that cannot (or would climb out of media/) is refused before
            // any path is built from it.
            if (!CanNameFolder(id))
            {
                problems.Add($"{label}: its id cannot name a folder");
                return;
            }

            var relativeFolder = Path.Combine("media", id, "base-image");
            var directory = Path.Combine(temp, relativeFolder);
            try
            {
                var bytes = await fetch(url, ct).ConfigureAwait(false);
                if (bytes == null) throw new IOException("the fetch returned nothing");

                var filename = FilenameOf(url, id);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, filename), bytes);

                var mediaId = Guid.NewGuid().ToString();
                Mime.TryGetValue(Path.GetExtension(filename).ToLowerInvariant(), out var mime);

                // Atlas's MediaItem, its keys in its order; added_at as Atlas's clock writes it (toISOString).
                var sidecar = new JObject
                {
                    ["id"] = mediaId,
                    ["element_id"] = id,
                    ["folder"] = "base-image",
                    ["filename"] = filename,
                    ["mime"] = mime ?? "application/octet-stream",
                    ["size_bytes"] = bytes.LongLength,
                    ["added_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                };
                WriteFile(Path.Combine(directory, mediaId + ".meta.json"), OWFolderWriter.Serialize(sidecar));

                media.Add(Path.Combine(relativeFolder, filename));
                media.Add(Path.Combine(relativeFolder, mediaId + ".meta.json"));
            }
            catch (Exception e) when (!(e is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                RemoveQuietly(Path.Combine(temp, "media", id));
                problems.Add($"{label}: {e.Message}");
            }
        }

        /// <summary>
        /// The last segment of the URL's path, as served (not unescaped), or <c>&lt;map id&gt;.img</c> when
        /// there is none or it cannot name a file.
        /// </summary>
        /// <remarks>
        /// Read from the string rather than through <see cref="Uri"/>, whose <c>AbsolutePath</c> escapes
        /// what the address left raw: the package keeps the path's own spelling, and so does this.
        /// </remarks>
        private static string FilenameOf(string url, string mapId)
        {
            var path = url;
            var end = path.IndexOfAny(new[] { '?', '#' });
            if (end >= 0) path = path.Substring(0, end);

            var scheme = path.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                var slash = path.IndexOf('/', scheme + 3);
                path = slash >= 0 ? path.Substring(slash) : string.Empty;
            }

            var name = path.Substring(path.LastIndexOf('/') + 1);
            var usable = name.Length > 0 && name != "." && name != ".."
                         && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            return usable ? name : mapId + ".img";
        }

        /// <summary>
        /// The default <see cref="OWFolderExportOptions.FetchMedia"/>: a GET with UnityWebRequest, issued on
        /// the main thread (<see cref="OWMainThread"/>), as <see cref="UnityWebRequestTransport"/> issues
        /// every request.
        /// </summary>
        private static Task<byte[]> FetchWithUnityWebRequest(string url, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            if (OWMainThread.IsMainThread)
            {
                DispatchFetch(url, ct, tcs);
            }
            else
            {
                OWMainThread.Run(() => DispatchFetch(url, ct, tcs));
            }

            return tcs.Task;
        }

        private static void DispatchFetch(string url, CancellationToken ct, TaskCompletionSource<byte[]> tcs)
        {
            var uwr = UnityWebRequest.Get(url);
            uwr.timeout = 120;
            uwr.SetRequestHeader("User-Agent", UserAgent);

            CancellationTokenRegistration registration = default;
            if (ct.CanBeCanceled)
            {
                registration = ct.Register(() =>
                {
                    if (!uwr.isDone) uwr.Abort();
                });
            }

            var operation = uwr.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(ct);
                        return;
                    }

                    if (uwr.result != UnityWebRequest.Result.Success)
                    {
                        var reason = uwr.result == UnityWebRequest.Result.ProtocolError
                            ? $"HTTP {uwr.responseCode}"
                            : uwr.error;
                        tcs.TrySetException(new OWTransportError($"GET {url} failed: {reason}"));
                        return;
                    }

                    tcs.TrySetResult(uwr.downloadHandler.data);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(new OWTransportError($"GET {url} failed.", e));
                }
                finally
                {
                    registration.Dispose();
                    uwr.Dispose();
                }
            };
        }

        // -- Files ---------------------------------------------------------------------------------------

        /// <summary>UTF-8 with no BOM, the format's encoding; the text is already LF.</summary>
        private static void WriteFile(string path, string text)
            => File.WriteAllText(path, text, new UTF8Encoding(false));

        private static void RemoveQuietly(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // A temp folder that will not go is litter beside the world, not a reason to hide the
                // error that brought us here.
            }
        }
    }
}
