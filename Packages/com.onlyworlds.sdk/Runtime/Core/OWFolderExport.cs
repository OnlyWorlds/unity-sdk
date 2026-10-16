using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

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
        /// <c>User-Agent</c> (the media host refuses a request with none).
        /// </summary>
        public Func<string, CancellationToken, Task<byte[]>> FetchMedia;

        /// <summary>Keep each Map's image as local media. On by default.</summary>
        public bool IncludeMedia = true;
    }

    /// <summary>
    /// An account world written to a world folder, read-only: the folder the onlyworlds Python package's
    /// <c>export_world</c> writes, elements byte for byte, with each Map's image kept the way Atlas keeps local media.
    /// </summary>
    public static class OWFolderExport
    {
        /// <summary>The OW Folder Format version an export declares in <c>world.json</c>.</summary>
        public const string FormatVersion = "0.3.7";

        /// <summary>
        /// The <c>world.json</c> body for a wire <c>GET /world/</c> body: every key carried in received order,
        /// <c>time_range_current</c> renamed <c>time_current</c> in place, <c>format_version</c> added when absent.
        /// The input is not changed.
        /// </summary>
        public static JObject WorldForDisk(JObject wireWorld)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Reads the world the client's key reaches and writes it as a world folder at <paramref name="folder"/>.
        /// Only GETs: the world is never written.
        /// </summary>
        /// <param name="folder">Must not exist, or be an empty directory: an export is a folder's birth.</param>
        public static Task<OWFolderExportReport> ExportAsync(
            OWClient client, string folder, OWFolderExportOptions options = null,
            IProgress<OWFolderExportProgress> progress = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
