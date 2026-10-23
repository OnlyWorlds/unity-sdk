using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// <see cref="OWClient.CreateAccountWorldAsync"/>'s request: a new world on the account (<c>POST /account/worlds</c>,
    /// npm's <c>OwCreateAccountWorldInput</c>). Only <see cref="Name"/> is required; a member left null is not sent.
    /// </summary>
    /// <remarks>
    /// keel reads a key left out and a key sent as <c>null</c> alike on this route (<c>accounts/api.py</c>
    /// <c>create_world</c>: <c>payload.get</c> for each), so null here means "not sent" and the client never sends a JSON
    /// null. The npm SDK sends a <c>null</c> it is given; the world keel makes is the same.
    /// </remarks>
    [Serializable]
    public class OWAccountWorldCreate
    {
        /// <summary>The world's name: 1 to 255 characters, not blank (keel trims it).</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>
        /// Only while the account has no PIN yet: 4 digits, 1000 to 9999, which becomes the account PIN and the write wall
        /// on every world of the account. An account with a PIN leaves it null. Either mistake is a 422 with
        /// <see cref="OWApiError.Param"/> <c>pin</c> and nothing made, so a tool that cannot tell which account it holds
        /// sends without it first and, on that 422, again with it.
        /// </summary>
        [JsonProperty("pin")]
        public string Pin;

        /// <summary>A name for the world's write key, as the website's key list shows it: 1 to 80 characters, trimmed. Null: no name.</summary>
        [JsonProperty("key_name")]
        public string KeyName;

        /// <summary>
        /// The folder format's <c>source</c> line (spec 4.2: <c>{server, world, at}</c>), from a tool that makes the world
        /// from a folder. Kept on the World as given and served by <see cref="OWClient.GetWorldAsync"/> as
        /// <c>origin.source</c>; <c>world</c>, when present, must be a string.
        /// </summary>
        [JsonProperty("source")]
        public JObject Source;

        /// <summary>
        /// The folder format's <c>producer</c> line (spec 4.2: <c>{tool, version, input}</c>). Kept on the World as given
        /// and served as <c>origin.producer</c>; <c>tool</c>, when present, must be a string.
        /// </summary>
        [JsonProperty("producer")]
        public JObject Producer;
    }

    /// <summary>
    /// <see cref="OWClient.CreateAccountWorldAsync"/>'s 201 (keel's <c>AccountWorldCreated</c>): the world's row as the
    /// account's world list shows it, plus the full write key, once, in <see cref="Key"/>.
    /// </summary>
    [Serializable]
    public class OWAccountWorldCreated
    {
        /// <summary>The new world's id.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The world's name, as sent, trimmed.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>Always <c>owner</c>: the account that made the world owns it.</summary>
        [JsonProperty("role")]
        public string Role;

        /// <summary>Whether anyone may read the world without a key. A new world starts private (false).</summary>
        [JsonProperty("public_read")]
        public bool PublicRead;

        /// <summary>The world's keys, never in full: here the one write key the route made.</summary>
        [JsonProperty("keys")]
        public List<OWAccountWorldKey> Keys;

        /// <summary>
        /// The full <c>ow_w_</c> write key, shown in this answer only: keep it, it is never shown again. Its last four
        /// characters are its row's <see cref="OWAccountWorldKey.Last4"/>.
        /// </summary>
        [JsonProperty("key")]
        public string Key;
    }

    /// <summary>A world's key as the account's world list shows it (keel's <c>AccountWorldKey</c>): never the key itself.</summary>
    [Serializable]
    public class OWAccountWorldKey
    {
        /// <summary>The key's name, as the website's key list shows it; empty when it has none.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>The key's last four characters, to tell keys apart.</summary>
        [JsonProperty("last4")]
        public string Last4;

        /// <summary><c>READ</c>, or <c>WRITE</c> (reads and writes).</summary>
        [JsonProperty("scope")]
        public string Scope;

        [JsonProperty("revoked")]
        public bool Revoked;
    }
}
