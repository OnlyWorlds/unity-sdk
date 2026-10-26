using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The person who answers for a seat, from <see cref="OWFrontDoor.Sponsor"/>.
    /// </summary>
    [Serializable]
    public class OWFrontDoorSponsor
    {
        /// <summary>Their membership id; null for the world's owner.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary><c>owner</c> or <c>person</c>.</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>Their Character's name; null when they play none this key can read.</summary>
        [JsonProperty("name")]
        public string Name;
    }

    /// <summary>
    /// A live AI or player seat the key's person answers for, from <see cref="OWFrontDoor.MySeats"/>.
    /// </summary>
    [Serializable]
    public class OWFrontDoorSeat
    {
        /// <summary>The seat's membership id, as <c>GET /members</c> lists it.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary><c>agent</c> or <c>player</c>.</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>The seat's name; null when it has none.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>The Character the seat plays; null when it plays none this key can read.</summary>
        [JsonProperty("character")]
        public string Character;
    }

    /// <summary>
    /// A world's front door for one key (keel #558, D152): who you are here, who answers for you, and the house
    /// rules. On <c>GET /me</c> (<see cref="Of"/> reads it from <see cref="OWClient.GetMeAsync"/>'s answer) and on an
    /// agent's join answer (<see cref="OWJoined.FrontDoor"/>); npm's <c>OwFrontDoor</c>.
    /// </summary>
    /// <remarks>
    /// Every id is one the key may read; anything hidden from it, or outside what its role sees, is null or left out.
    /// Read <see cref="Charter"/> first.
    /// </remarks>
    [Serializable]
    public class OWFrontDoor
    {
        /// <summary>
        /// The person who answers for this seat (an AI's or a player's). Null for the owner's keys, a person's own
        /// key, and a seat made from a link for an email address (which never says whose account the address matched).
        /// </summary>
        [JsonProperty("sponsor")]
        public OWFrontDoorSponsor Sponsor;

        /// <summary>The sponsor's Character, the one to address a message to, or null.</summary>
        [JsonProperty("sponsor_character")]
        public string SponsorCharacter;

        /// <summary>Your own Character (the owner's: the world's <c>owner_character</c>), or null.</summary>
        [JsonProperty("my_character")]
        public string MyCharacter;

        /// <summary>
        /// The live AI and player seats the key's person answers for, agent seats first (keel #704): how a tool on a
        /// person's key tells which Character is that person's AI. Empty for a seat's own key, for anyone who answers
        /// for none, and on a keel from before it; a seat made from a link for an email address is never listed.
        /// </summary>
        [JsonProperty("my_seats")]
        public List<OWFrontDoorSeat> MySeats = new List<OWFrontDoorSeat>();

        /// <summary>
        /// The world's Charter: the newest Law with supertype Charter made by the owner or a co-builder, or null.
        /// Every member may read it.
        /// </summary>
        [JsonProperty("charter")]
        public string Charter;

        /// <summary>The Objects the Charter's description names (by <c>ow://</c> link or id), in the order written, at most five; empty when none.</summary>
        [JsonProperty("charter_skill")]
        public List<string> CharterSkill = new List<string>();

        /// <summary>
        /// The front door in a <c>GET /me</c> answer (<see cref="OWClient.GetMeAsync"/>), or null when it carries
        /// none (a keel from before #558).
        /// </summary>
        public static OWFrontDoor Of(JObject me)
        {
            if (!(me?["front_door"] is JObject door)) return null;
            return OWJson.Deserialize<OWFrontDoor>(door.ToString(Formatting.None));
        }
    }
}
