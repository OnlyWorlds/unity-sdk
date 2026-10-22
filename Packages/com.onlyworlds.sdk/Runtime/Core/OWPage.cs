using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// One cursor page of elements: <c>{data, has_more, next_cursor}</c>.
    /// </summary>
    /// <remarks>
    /// The v2 list surface is cursor-paginated, not offset-paginated. The server also SILENTLY
    /// CLAMPS limit at 1000 -- asking for more does not error, it just returns fewer than requested
    /// with <c>has_more</c> true. Code that treats a short page as "that was everything" will lose
    /// elements without any signal. Always drive iteration from <see cref="HasMore"/>.
    /// </remarks>
    [Serializable]
    public class OWPage<T>
    {
        [JsonProperty("data")]
        public List<T> Data = new List<T>();

        [JsonProperty("has_more")]
        public bool HasMore;

        [JsonProperty("next_cursor")]
        public string NextCursor;
    }

    /// <summary>Query options for a list request.</summary>
    public struct OWListParams
    {
        /// <summary>Page size. Server clamps at 1000.</summary>
        public int? Limit;

        /// <summary>Opaque cursor from a previous page's <c>next_cursor</c>.</summary>
        public string Cursor;

        /// <summary>Sparse fieldset, e.g. <c>id,name,supertype</c>.</summary>
        public string[] Fields;

        /// <summary>Exact match on name (<c>name</c>).</summary>
        public string Name;

        /// <summary>Case-insensitive substring match on name (<c>name__icontains</c>).</summary>
        public string NameContains;

        /// <summary>Exact match on supertype.</summary>
        public string Supertype;

        /// <summary>Exact match on subtype.</summary>
        public string Subtype;
    }

    /// <summary>A single entry from the <c>/changes</c> feed.</summary>
    [Serializable]
    public class OWChange
    {
        [JsonProperty("op")]
        public string Op;

        [JsonProperty("type")]
        public string Type;

        [JsonProperty("id")]
        public string Id;

        [JsonProperty("change_seq")]
        public long ChangeSeq;

        /// <summary>
        /// On an upsert: the element whole, as <c>GET /{type}/{id}</c> returns it. Null on a delete.
        /// </summary>
        [JsonProperty("element")]
        public JObject Element;

        /// <summary>On a delete: the membership whose write deleted it; null for the owner (keel #299).</summary>
        [JsonProperty("deleted_by")]
        public string DeletedBy;

        /// <summary>On a delete: the door it came through (<see cref="OWWriteDoor"/>), or null.</summary>
        [JsonProperty("deleted_via")]
        public string DeletedVia;

        /// <summary>
        /// On a delete: the name the deleting client gave in its <c>X-OW-Client</c> header (keel #520), as an
        /// element's <see cref="OWElement.ChangedClient"/>: the client's claim, never proof. Null when none was
        /// recorded, and on a <c>hidden</c> row.
        /// </summary>
        [JsonProperty("deleted_client")]
        public string DeletedClient;
    }

    /// <summary>A page of the <c>/changes</c> feed.</summary>
    /// <remarks>
    /// <para>
    /// Every write to the world in order, by <c>change_seq</c> then id: an upsert carries the whole
    /// element (<see cref="OWChange.Element"/>), a delete only its type and id.
    /// </para>
    /// <para>
    /// GOTCHA: world-meta edits do NOT appear here and do not bump <c>change_seq</c>. Poll
    /// <c>GET /world</c> and compare <c>updated_at</c> separately.
    /// </para>
    /// </remarks>
    [Serializable]
    public class OWChangesPage
    {
        /// <summary>
        /// Where the next page starts: pass it back as is. Opaque: never parse or build one (a guest
        /// key's has another shape than everyone else's). The server sends a bare number on a tip read
        /// and a token after a page; both arrive here as a string.
        /// </summary>
        [JsonProperty("cursor")]
        public string Cursor;

        [JsonProperty("changes")]
        public List<OWChange> Changes = new List<OWChange>();

        [JsonProperty("has_more")]
        public bool HasMore;

        /// <summary>The world's current <c>change_seq</c>. A persisted position past it means the world was rewound.</summary>
        [JsonProperty("head")]
        public long Head;

        /// <summary>
        /// A guest key's view epoch, null for every other key. It moves when what the guest may see
        /// changes, even with <see cref="Head"/> unchanged: compare it with the epoch you last synced
        /// under, never read it out of the cursor.
        /// </summary>
        [JsonProperty("epoch")]
        public long? Epoch;
    }
}
