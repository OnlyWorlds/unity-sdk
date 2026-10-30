using System;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// One row of the world's hidden list (<see cref="OWClient.ListHiddenAsync"/>, GET /world/hidden; npm's
    /// <c>OwHiddenRow</c>): an element that carries the hidden mark, and who set it when.
    /// </summary>
    [Serializable]
    public class OWHiddenRow
    {
        /// <summary>The element's type slug.</summary>
        [JsonProperty("type")]
        public string Type;

        /// <summary>The element's id.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>When it was hidden, ISO 8601 as served.</summary>
        [JsonProperty("set_at")]
        public string SetAt;

        /// <summary>
        /// The membership (a person's or a seat's) that hid it, or null when the world's owner did, as an element's
        /// <c>created_by</c> reads.
        /// </summary>
        [JsonProperty("set_by")]
        public string SetBy;
    }
}
