using System;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A live agent seat, as <see cref="OWClient.ListSeatsAsync"/> lists it (GET /account/worlds/{world_id}/seats; npm's
    /// <c>OwSeat</c>): never its key or secret.
    /// </summary>
    [Serializable]
    public class OWSeat
    {
        /// <summary>The seat's membership id: <see cref="OWClient.RekeySeatAsync"/> and <see cref="OWClient.RemoveSeatAsync"/> take it, and GET /members names the seat by it.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The AI's own name in the world.</summary>
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("role")]
        public string Role;

        /// <summary>Always <c>agent</c>: a seat has no account of its own.</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>The Character the seat plays, or null. A seat made with <c>mine</c> plays its sponsor's own.</summary>
        [JsonProperty("character")]
        public string Character;

        /// <summary>
        /// Who answers for the seat: a username when they are the world's owner or a member; null for a seat from an email
        /// invite, or a sponsor who left.
        /// </summary>
        [JsonProperty("sponsor")]
        public string Sponsor;

        /// <summary>ISO 8601, UTC, as served, or null.</summary>
        [JsonProperty("joined_at")]
        public string JoinedAt;

        /// <summary>The latest use of the seat's key, as served, or null.</summary>
        [JsonProperty("last_active")]
        public string LastActive;
    }

    /// <summary>
    /// <see cref="OWClient.CreateSeatAsync"/>'s and <see cref="OWClient.RekeySeatAsync"/>'s 201 (npm's
    /// <c>OwSeatCreated</c>): the seat, its write key and its secret, shown in this answer only.
    /// </summary>
    [Serializable]
    public class OWSeatCreated
    {
        [JsonProperty("world")]
        public OWSeatCreatedWorld World;

        [JsonProperty("seat")]
        public OWSeatCreatedSeat Seat;

        /// <summary>The Character the seat plays, or null for none.</summary>
        [JsonProperty("character")]
        public string Character;

        /// <summary>The seat's write key (<c>ow_w_</c>), sent as <c>API-Key</c>. Shown once.</summary>
        [JsonProperty("key")]
        public string Key;

        /// <summary>The seat's <c>ow_s_</c> secret, sent as <c>API-Pin</c> (<see cref="OWClientConfig.ApiPin"/>). Shown once.</summary>
        [JsonProperty("pin")]
        public string Pin;

        /// <summary>
        /// The lines of a <c>.env</c> file for the AI: <c>OW_API_KEY</c>, <c>OW_API_PIN</c>, <c>OW_WORLD</c>,
        /// <c>OW_CHARACTER</c> when it plays one, <c>OW_API_BASE</c>. It holds the key and the secret.
        /// </summary>
        [JsonProperty("env")]
        public string Env;
    }

    /// <summary>The world a seat was made in.</summary>
    [Serializable]
    public class OWSeatCreatedWorld
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public string Name;
    }

    /// <summary>The seat a key was made for.</summary>
    [Serializable]
    public class OWSeatCreatedSeat
    {
        /// <summary>The seat's membership id.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The AI's own name in the world.</summary>
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("role")]
        public string Role;

        /// <summary>Always <c>agent</c>.</summary>
        [JsonProperty("kind")]
        public string Kind;
    }
}
