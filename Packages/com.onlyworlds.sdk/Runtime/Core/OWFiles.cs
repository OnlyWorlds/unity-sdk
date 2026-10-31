using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// What a file on an element can be, read from its name (keel's <c>FileKind</c> and <c>NAME_EXTS</c>): text
    /// (<c>txt</c>, <c>md</c>, <c>json</c>, <c>csv</c>, <c>yaml</c>, <c>toml</c>), <c>pdf</c>, and <c>zip</c> only as a
    /// world's own bundle. The upload host checks each against the file's first bytes. Mirrors the TypeScript SDK's
    /// <c>FILE_EXTENSIONS</c> and <c>fileKindOf</c> (4.33.0).
    /// </summary>
    public static class OWFileKinds
    {
        /// <summary>
        /// Every extension a file on an element may end in, to the kind it gives: <c>yml</c> is <c>yaml</c> and
        /// <c>markdown</c> is <c>md</c>. A file picker's filter reads these keys.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Extensions = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>
            {
                ["pdf"] = "pdf", ["txt"] = "txt", ["md"] = "md", ["markdown"] = "md", ["json"] = "json",
                ["csv"] = "csv", ["yaml"] = "yaml", ["yml"] = "yaml", ["toml"] = "toml", ["zip"] = "zip",
            });

        /// <summary>
        /// The kind a file name's extension gives, read as keel reads it: the last path part (after <c>/</c> or
        /// <c>\</c>), then what follows its last dot, which must have something before it, trimmed and lower-cased. Null
        /// when there is none, or keel takes no file with it.
        /// </summary>
        public static string Of(string name)
        {
            if (name == null) return null;

            var cut = name.LastIndexOfAny(new[] { '/', '\\' });
            var last = cut < 0 ? name : name.Substring(cut + 1);
            var dot = last.LastIndexOf('.');
            var ext = dot > 0 ? last.Substring(dot + 1).Trim().ToLowerInvariant() : "";

            return Extensions.TryGetValue(ext, out var kind) ? kind : null;
        }
    }

    /// <summary>Who added a file (npm's <c>OwFileAddedBy</c>).</summary>
    [Serializable]
    public class OWFileAddedBy
    {
        /// <summary>A member's or a seat's name; the owner's username for the owner's keys.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>Their membership id; null for the owner's keys.</summary>
        [JsonProperty("membership")]
        public string Membership;

        /// <summary>True when an agent added it: an agent seat, the MCP surface, or a key that declared an agent.</summary>
        [JsonProperty("agent")]
        public bool Agent;
    }

    /// <summary>
    /// One file on an element, as <see cref="OWClient.ListFilesAsync"/> lists it (GET /{type}/{id}/files; npm's
    /// <c>OwFileRow</c>). Files never ride on the element: they are not in its body, its <c>change_seq</c> or /changes.
    /// </summary>
    [Serializable]
    public class OWFileRow
    {
        /// <summary>The file's id: <see cref="OWClient.FileDownloadLinkAsync"/> and <see cref="OWClient.RemoveFileAsync"/> take it.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The file's name as uploaded.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>One of <see cref="OWFileKinds.Extensions"/>' kinds.</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>Bytes, as the upload host stored them.</summary>
        [JsonProperty("size")]
        public long Size;

        /// <summary>A field of the element's type the file belongs to, free text (at most 80 characters), or null.</summary>
        [JsonProperty("label")]
        public string Label;

        [JsonProperty("added_by")]
        public OWFileAddedBy AddedBy;

        /// <summary>When the upload host stored it: ISO 8601, as served.</summary>
        [JsonProperty("added_at")]
        public string AddedAt;
    }

    /// <summary>One file in the world's list (npm's <c>OwWorldFileRow</c>): a file row with its element's type and id.</summary>
    [Serializable]
    public class OWWorldFileRow : OWFileRow
    {
        [JsonProperty("element_type")]
        public string ElementType;

        [JsonProperty("element_id")]
        public string ElementId;
    }

    /// <summary>
    /// <see cref="OWClient.WorldFilesAsync"/>'s answer (GET /world/files; npm's <c>OwWorldFiles</c>): every file this key
    /// may see, newest first, and the world's total against its cap.
    /// </summary>
    [Serializable]
    public class OWWorldFiles
    {
        /// <summary>The rows; empty when keel sends none.</summary>
        [JsonProperty("data")]
        public List<OWWorldFileRow> Data = new List<OWWorldFileRow>();

        /// <summary>The bytes of the world's live files: all of them, also those on elements this key cannot see.</summary>
        [JsonProperty("used_bytes")]
        public long UsedBytes;

        /// <summary>The world's room for files (250 MB on onlyworlds.com).</summary>
        [JsonProperty("cap_bytes")]
        public long CapBytes;
    }

    /// <summary>
    /// <see cref="OWClient.CreateFileTicketAsync"/>'s 201 (POST /{type}/{id}/files; npm's <c>OwFileTicket</c>): a
    /// short-lived, single-use permission to upload one file onto one element, the image ticket's shape with the file's
    /// own key and id.
    /// </summary>
    [Serializable]
    public class OWFileTicket
    {
        /// <summary>The ticket, opaque: sent to <see cref="UploadUrl"/> as <c>Authorization: Bearer &lt;ticket&gt;</c>, as is. A credential: keep it out of logs.</summary>
        [JsonProperty("ticket")]
        public string Ticket;

        /// <summary>Where the bytes go (<c>https://upload.onlyworlds.com/v1/file</c> on onlyworlds.com).</summary>
        [JsonProperty("upload_url")]
        public string UploadUrl;

        /// <summary><c>w/&lt;world_id&gt;/&lt;element_id&gt;/</c>.</summary>
        [JsonProperty("prefix")]
        public string Prefix;

        /// <summary>The object key the file lands under: <c>w/&lt;world_id&gt;/&lt;element_id&gt;/&lt;file_id&gt;.&lt;kind&gt;</c>.</summary>
        [JsonProperty("key")]
        public string Key;

        /// <summary>The most this ticket accepts, in bytes: 25 MB, or what the world has left, whichever is smaller.</summary>
        [JsonProperty("max_bytes")]
        public long MaxBytes;

        /// <summary>Unix seconds after which the upload host refuses the ticket (10 minutes after issue).</summary>
        [JsonProperty("exp")]
        public long Exp;

        /// <summary>Always 1: one ticket, one upload.</summary>
        [JsonProperty("uses")]
        public int Uses;

        /// <summary>Who answers for the upload.</summary>
        [JsonProperty("issued_to")]
        public OWTicketIssuedTo IssuedTo;

        /// <summary>Always <c>file</c> (an image ticket has none).</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>The file's id, minted before a byte moves: the <see cref="OWFileRow.Id"/> its row will have.</summary>
        [JsonProperty("file_id")]
        public string FileId;
    }

    /// <summary>The upload host's 201 for one file (<see cref="OWClient.UploadFileAsync"/>; npm's <c>OwUploadedFile</c>).</summary>
    [Serializable]
    public class OWUploadedFile
    {
        /// <summary>The object key, the ticket's <see cref="OWFileTicket.Key"/>.</summary>
        [JsonProperty("key")]
        public string Key;

        /// <summary>The file's id, the ticket's <see cref="OWFileTicket.FileId"/>.</summary>
        [JsonProperty("fid")]
        public string Fid;

        /// <summary>The bytes stored.</summary>
        [JsonProperty("bytes")]
        public long Bytes;

        /// <summary>The MIME type the file is served with.</summary>
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("etag")]
        public string Etag;
    }

    /// <summary>
    /// <see cref="OWClient.FileDownloadLinkAsync"/>'s answer (GET /{type}/{id}/files/{file_id}/download; npm's
    /// <c>OwFileDownload</c>): a link to the file and when it stops working.
    /// </summary>
    [Serializable]
    public class OWFileDownload
    {
        /// <summary>
        /// The file on the download host, as an attachment. The link is the credential: anyone who holds it can fetch the
        /// file until <see cref="Exp"/>, more than once. Use it at once; never log, store or share it.
        /// </summary>
        [JsonProperty("url")]
        public string Url;

        /// <summary>Unix seconds after which the link is refused (5 minutes after issue).</summary>
        [JsonProperty("exp")]
        public long Exp;

        [JsonProperty("name")]
        public string Name;

        [JsonProperty("kind")]
        public string Kind;

        [JsonProperty("size")]
        public long Size;
    }
}
