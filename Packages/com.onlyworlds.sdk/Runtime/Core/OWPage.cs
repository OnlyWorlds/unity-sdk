using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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
    }

    /// <summary>A page of the <c>/changes</c> feed.</summary>
    /// <remarks>
    /// <para>
    /// <c>/changes</c> returns change RECORDS, not element bodies. It is the sync cursor -- lossless
    /// as a change feed, useless as an exporter on its own. To materialize a world you walk the
    /// per-type list endpoints.
    /// </para>
    /// <para>
    /// GOTCHA: world-meta edits do NOT appear here and do not bump <c>change_seq</c>. Poll
    /// <c>GET /world</c> and compare <c>updated_at</c> separately.
    /// </para>
    /// </remarks>
    [Serializable]
    public class OWChangesPage
    {
        [JsonProperty("cursor")]
        public string Cursor;

        [JsonProperty("changes")]
        public List<OWChange> Changes = new List<OWChange>();

        [JsonProperty("has_more")]
        public bool HasMore;

        /// <summary>The server's current tip. Compare against a persisted cursor.</summary>
        [JsonProperty("head")]
        public long Head;
    }
}
