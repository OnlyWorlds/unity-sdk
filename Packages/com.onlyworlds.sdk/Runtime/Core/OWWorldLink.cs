using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// <see cref="OWClient.CreateWorldLinkAsync"/>'s request: a new invite link for a world the account owns (POST
    /// /account/worlds/{world_id}/links; the TypeScript SDK's <c>createWorldLink</c> options). Only <see cref="Reader"/>
    /// is required; a member left null is not sent, and keel's default holds.
    /// </summary>
    [Serializable]
    public class OWWorldLinkCreate
    {
        /// <summary>
        /// Who the link is for: <c>person</c> (accepted signed in, <see cref="OWClient.AcceptJoinAsync"/>), <c>agent</c>
        /// (an AI agent redeems it at POST /join) or <c>player</c> (a friend with no account, <see cref="OWJoin.RedeemAsync"/>,
        /// for a read key).
        /// </summary>
        [JsonProperty("reader")]
        public string Reader;

        /// <summary>
        /// The role the link gives. Null: keel's default, guest for a person link, contributor for an agent link; a player
        /// link is guest and refuses any other.
        /// </summary>
        [JsonProperty("role")]
        public string Role;

        /// <summary>How long the link works: 1, 7 or 30 days. Null: 7.</summary>
        [JsonProperty("days")]
        public int? Days;

        /// <summary>The name the invite line is signed with. Null: none (the line then reads "Someone").</summary>
        [JsonProperty("inviter_name")]
        public string InviterName;

        /// <summary>An agent link only: whether redeeming gives the agent a Character of its own. Null: true.</summary>
        [JsonProperty("with_character")]
        public bool? WithCharacter;

        /// <summary>
        /// A player link for one only: the Character's id the player reads as, or <c>new</c> (the default) for a fresh
        /// one. A table takes none, not even <c>new</c> (422, <see cref="OWApiError.Param"/> <c>character</c>).
        /// </summary>
        [JsonProperty("character")]
        public string Character;

        /// <summary>
        /// Person and player links: true puts the world's id in the link's path, so a chat that unfurls it shows the
        /// world's name and picture (the code stays after <c>#</c>). Null: false. True on an agent link is a 422.
        /// </summary>
        [JsonProperty("preview")]
        public bool? Preview;

        /// <summary>
        /// Person and player links: how many times the link works, 1 to 50. Null: 1, a link for one. On a person link 2 to
        /// 50 lets in that many accounts; on a player link it makes a table, one link that seats that many players.
        /// </summary>
        [JsonProperty("max_uses")]
        public int? MaxUses;

        /// <summary>
        /// A table only: the Characters its players may pick, ids of the world in the order a pick screen shows them (a
        /// hidden one is a 422). Empty or null: each player names a fresh Character after themselves. On a link for one,
        /// even empty, it is a 422.
        /// </summary>
        [JsonProperty("characters")]
        public List<string> Characters;
    }

    /// <summary>
    /// One of a world's invite links, as <see cref="OWClient.ListWorldLinksAsync"/> lists it (npm's <c>OwWorldLink</c>):
    /// never its code.
    /// </summary>
    [Serializable]
    public class OWWorldLink
    {
        /// <summary>The link's id: <see cref="OWClient.RevokeWorldLinkAsync"/> takes it.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary><c>person</c>, <c>agent</c> or <c>player</c> (<see cref="OWWorldLinkCreate.Reader"/>).</summary>
        [JsonProperty("reader")]
        public string Reader;

        [JsonProperty("role")]
        public string Role;

        /// <summary>The name the owner gave an agent link on the website, or empty.</summary>
        [JsonProperty("label")]
        public string Label;

        /// <summary>The name the link is signed with, or empty.</summary>
        [JsonProperty("inviter_name")]
        public string InviterName;

        /// <summary>Whether redeeming gives an agent a Character of its own: false on a person link, true on a player link.</summary>
        [JsonProperty("with_character")]
        public bool WithCharacter;

        /// <summary>The code's last four characters, to tell links apart.</summary>
        [JsonProperty("last4")]
        public string Last4;

        /// <summary>ISO 8601, UTC, as served.</summary>
        [JsonProperty("created_at")]
        public string CreatedAt;

        /// <summary>ISO 8601, UTC, as served.</summary>
        [JsonProperty("expires_at")]
        public string ExpiresAt;

        /// <summary>0 or 1 for a link for one; a table's seats made, removed ones included.</summary>
        [JsonProperty("uses")]
        public int Uses;

        /// <summary>
        /// <c>pending</c>, <c>used</c>, <c>revoked</c> or <c>expired</c>. A table is <c>pending</c> until every place is
        /// taken, then <c>used</c>.
        /// </summary>
        [JsonProperty("status")]
        public string Status;

        /// <summary>The membership of the co-builder who minted the link; null for one the owner made.</summary>
        [JsonProperty("minted_by")]
        public string MintedBy;

        /// <summary>Whether the link shows the world's name and picture when pasted into a chat; null on an agent link.</summary>
        [JsonProperty("preview")]
        public bool? Preview;

        /// <summary>
        /// A player link only: the Character the player reads as; null while a link for a fresh Character is unused, and
        /// always on a table.
        /// </summary>
        [JsonProperty("character")]
        public OWWorldLinkCharacter Character;

        /// <summary>A table only: how many times it works. Null on a link for one.</summary>
        [JsonProperty("max_uses")]
        public int? MaxUses;

        /// <summary>A table only: the Characters its players may pick, in the owner's order; null on a link for one.</summary>
        [JsonProperty("characters")]
        public List<OWWorldLinkPick> Characters;

        /// <summary>A party link: more than one player joins by it.</summary>
        [JsonIgnore]
        public bool IsTable => MaxUses.HasValue && MaxUses.Value > 1;
    }

    /// <summary>
    /// <see cref="OWClient.CreateWorldLinkAsync"/>'s 201 (npm's <c>OwCreatedWorldLink</c>): the new link's row, plus its code,
    /// shown in this answer only (keel keeps a hash).
    /// </summary>
    [Serializable]
    public class OWWorldLinkCreated : OWWorldLink
    {
        /// <summary>The <c>ow_j_</c> join code. Shown once; it can never be read again.</summary>
        [JsonProperty("code")]
        public string Code;

        /// <summary>The invite link: the /join page (<c>/join/&lt;world_id&gt;</c> when it previews) with the code after <c>#</c>.</summary>
        [JsonProperty("link")]
        public string Link;

        /// <summary>The one line to send: who invites, to which world, and the link.</summary>
        [JsonProperty("line")]
        public string Line;
    }

    /// <summary>A player link's Character: its id, and its name, null when it was deleted since.</summary>
    [Serializable]
    public class OWWorldLinkCharacter
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public string Name;
    }

    /// <summary>One Character a table's players may pick.</summary>
    [Serializable]
    public class OWWorldLinkPick
    {
        [JsonProperty("id")]
        public string Id;

        /// <summary>The Character's name; null when it was deleted since.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>True while a live seat in the world plays it.</summary>
        [JsonProperty("taken")]
        public bool Taken;
    }
}
