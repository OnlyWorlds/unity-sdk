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

        /// <summary>
        /// A name for the world's write key, as the website's key list shows it: 1 to 80 characters once keel trims it,
        /// sent as given. Null: no name. Blank or longer is refused before sending, as keel answers either with a 422.
        /// </summary>
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

    /// <summary>
    /// One world of the account, as <see cref="OWClient.ListAccountWorldsAsync"/> lists it (GET /account/worlds; npm's
    /// <c>OwAccountWorld</c>): a world the account owns or is a member of, with its keys by name and last four.
    /// </summary>
    [Serializable]
    public class OWAccountWorld
    {
        /// <summary>The world's id.</summary>
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public string Name;

        /// <summary><c>owner</c> for a world the account owns, <c>collaborator</c> for one it is a member of.</summary>
        [JsonProperty("role")]
        public string Role;

        /// <summary>
        /// On a collaborator's row only: the membership's role (<c>co_builder</c>, <c>contributor</c>, <c>guest</c>).
        /// Null on an owned world.
        /// </summary>
        [JsonProperty("member_role")]
        public string MemberRole;

        /// <summary>Whether anyone may read the world without a key.</summary>
        [JsonProperty("public_read")]
        public bool PublicRead;

        /// <summary>
        /// The world's keys, never in full: every key on an owned world, only the account's own member keys on a shared one.
        /// </summary>
        [JsonProperty("keys")]
        public List<OWAccountWorldKey> Keys;
    }

    /// <summary>
    /// <see cref="OWClient.MintWorldKeyAsync"/>'s 201 (npm's <c>OwMintedKey</c>): the key's row as the world's key list
    /// shows it, plus the full key, once, in <see cref="Key"/>.
    /// </summary>
    [Serializable]
    public class OWMintedKey
    {
        /// <summary>The key's id: <see cref="OWClient.RevokeWorldKeyAsync"/> takes it.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The key's name, as the website's key list shows it; empty when it has none.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>The key's last four characters, to tell keys apart.</summary>
        [JsonProperty("last4")]
        public string Last4;

        /// <summary><c>READ</c>, or <c>WRITE</c> (reads and writes).</summary>
        [JsonProperty("scope")]
        public string Scope;

        /// <summary>The key's latest use, ISO 8601 as served, or null (a new key has none).</summary>
        [JsonProperty("last_used_at")]
        public string LastUsedAt;

        [JsonProperty("revoked")]
        public bool Revoked;

        /// <summary>
        /// The full key, shown in this answer only: keep it, it is never shown again. Its last four characters are
        /// <see cref="Last4"/>.
        /// </summary>
        [JsonProperty("key")]
        public string Key;

        /// <summary>
        /// On a member's key only: the membership it is bound to (<c>id</c>, and <c>username</c>, or a seat's
        /// <c>agent_name</c> or a player's <c>kind</c> and <c>name</c>), as served. Null on the owner's key.
        /// </summary>
        [JsonProperty("member")]
        public JObject Member;

        /// <summary>
        /// True when the key is kept across a sign-out: no revoke of the token that minted it takes it. keel sets it on a
        /// mint that asked for it (<see cref="OWClient.MintWorldKeyAsync"/>'s <c>keep</c>).
        /// </summary>
        [JsonProperty("kept")]
        public bool Kept;
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

    /// <summary>
    /// One of the account's tokens, as <see cref="OWClient.ListAccountTokensAsync"/> lists it (GET /account/tokens; npm's
    /// <c>OwAccountToken</c>): by name and last four, never the token itself.
    /// </summary>
    [Serializable]
    public class OWAccountToken
    {
        /// <summary>
        /// The token's id: <see cref="OWClient.RevokeAccountTokenAsync"/> takes it, and a sign-in answers it, as
        /// <see cref="OWSignedIn.Id"/> (by password) or <see cref="OWSignInResult.TokenId"/> (Sign in with OnlyWorlds).
        /// </summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The label given at mint (a device, a tool).</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>The token's last four characters, to tell tokens apart.</summary>
        [JsonProperty("last4")]
        public string Last4;

        /// <summary>When the token was last used, as keel sends it; null when never.</summary>
        [JsonProperty("last_used_at")]
        public string LastUsedAt;

        [JsonProperty("revoked")]
        public bool Revoked;

        /// <summary>
        /// When the token stops working, for a sign-in tool's token with a lifetime; null never expires. Past it the token
        /// is refused as a revoked one.
        /// </summary>
        [JsonProperty("expires_at")]
        public string ExpiresAt;

        /// <summary>How many live world keys this token minted.</summary>
        [JsonProperty("keys_minted")]
        public long KeysMinted;

        /// <summary>
        /// Who the token is for: empty for a token made by hand or a sign-in tool's; <c>mcp</c> for a hosted client's
        /// grant on the MCP server.
        /// </summary>
        [JsonProperty("audience")]
        public string Audience;

        /// <summary>On an <c>mcp</c> token only: the client's name, as it registered itself, or its id when it is no longer registered.</summary>
        [JsonProperty("client")]
        public string Client;

        /// <summary>On an <c>mcp</c> token only: the host the client returns the person to.</summary>
        [JsonProperty("return_host")]
        public string ReturnHost;
    }
}
