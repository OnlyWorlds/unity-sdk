using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>A world, as a join code's answers name it.</summary>
    [Serializable]
    public class OWJoinWorld
    {
        /// <summary>Null in a preview: the code is not spent, so the world's id is not handed out.</summary>
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public string Name;

        [JsonProperty("description")]
        public string Description;
    }

    /// <summary>A Character, by id and name, as a join answer names it.</summary>
    [Serializable]
    public class OWJoinCharacter
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public string Name;
    }

    /// <summary>
    /// One Character a party link offers, from its preview's <see cref="OWJoinPreview.Characters"/>: the player
    /// picks one not <see cref="Taken"/> and sends its id to <see cref="OWJoin.RedeemAsync"/>.
    /// </summary>
    [Serializable]
    public class OWJoinPick
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public string Name;

        [JsonProperty("image_url")]
        public string ImageUrl;

        /// <summary>A live seat in the world plays it already, a person's included. Keel's order puts these last.</summary>
        [JsonProperty("taken")]
        public bool Taken;
    }

    /// <summary>An agent's seat in the world, from an agent code's answer.</summary>
    [Serializable]
    public class OWJoinSeat
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("role")]
        public string Role;

        [JsonProperty("agent_name")]
        public string AgentName;
    }

    /// <summary>
    /// POST /join/preview's answer: what a join code holds, without using it (keel's
    /// <c>preview_join</c>; npm's <c>OwJoinPreview</c>).
    /// </summary>
    [Serializable]
    public class OWJoinPreview
    {
        /// <summary>
        /// Who the code is for: <c>player</c> (a friend with no account; <see cref="OWJoin.RedeemAsync"/>
        /// gives a read key), <c>agent</c> (an AI agent; the same call gives a seat's write key and
        /// secret), or <c>person</c> (a signed-in account accepts it at <c>/account/join</c>, not here).
        /// </summary>
        [JsonProperty("reader")]
        public string Reader;

        [JsonProperty("world")]
        public OWJoinWorld World;

        /// <summary>The name the inviter signed the link with, unchecked, or null.</summary>
        [JsonProperty("invited_by")]
        public string InvitedBy;

        /// <summary>The owner's chosen Character's name, never a username; null when none is set.</summary>
        [JsonProperty("owner_character")]
        public string OwnerCharacter;

        /// <summary>The role joining gives; always <c>guest</c> for a player.</summary>
        [JsonProperty("role")]
        public string Role;

        /// <summary>Whether joining gives a Character: always true for a player, false for a person.</summary>
        [JsonProperty("with_character")]
        public bool WithCharacter;

        /// <summary>ISO 8601, UTC, as sent.</summary>
        [JsonProperty("expires_at")]
        public string ExpiresAt;

        // A party link (a table: one link, up to 50 players) adds the four below; a link for one sends none of them.

        /// <summary>How many players the link seats, 2 to 50; null on a link for one.</summary>
        [JsonProperty("max_uses")]
        public int? MaxUses;

        /// <summary>How many places are left at the table; null on a link for one.</summary>
        [JsonProperty("uses_left")]
        public int? UsesLeft;

        /// <summary>
        /// True when the table lists no Characters: redeem with a <c>name</c> and no <c>character</c>, and the
        /// player gets a fresh Character. Null on a link for one.
        /// </summary>
        [JsonProperty("name_required")]
        public bool? NameRequired;

        /// <summary>The Characters to pick from, in the owner's order; empty when <see cref="NameRequired"/>, null on a link for one.</summary>
        [JsonProperty("characters")]
        public List<OWJoinPick> Characters;

        /// <summary>A party link: more than one player joins by it, each as a picked or a fresh Character.</summary>
        [JsonIgnore]
        public bool IsTable => MaxUses.HasValue && MaxUses.Value > 1;
    }

    /// <summary>
    /// POST /join's answer, a player's or an agent's, told apart by <see cref="Kind"/> (npm's
    /// <c>OwJoined</c>, one class here because C# has no union). Shown once: the code is spent and a
    /// lost answer cannot be replayed, so keep <see cref="Key"/> (and an agent's <see cref="Pin"/>).
    /// </summary>
    [Serializable]
    public class OWJoined
    {
        /// <summary><c>player</c> or <c>agent</c>.</summary>
        [JsonProperty("kind")]
        public string Kind;

        /// <summary>A player's read key (<c>ow_r_</c>, no PIN), or an agent seat's write key.</summary>
        [JsonProperty("key")]
        public string Key;

        /// <summary>An agent seat's secret (<c>ow_s_</c>), sent as <c>API-Pin</c>; null for a player.</summary>
        [JsonProperty("pin")]
        public string Pin;

        [JsonProperty("world")]
        public OWJoinWorld World;

        /// <summary>The Character a player reads as; an agent's when its link gave one, else null.</summary>
        [JsonProperty("character")]
        public OWJoinCharacter Character;

        /// <summary>A player's role (<c>guest</c>). An agent's is on its <see cref="Seat"/>: read <see cref="RoleInWorld"/>.</summary>
        [JsonProperty("role")]
        public string Role;

        /// <summary>An agent's seat; null for a player.</summary>
        [JsonProperty("seat")]
        public OWJoinSeat Seat;

        /// <summary>An agent's: the API's base URL its key is for.</summary>
        [JsonProperty("base_url")]
        public string BaseUrl;

        /// <summary>An agent's: the lines of a <c>.env</c> file.</summary>
        [JsonProperty("env")]
        public string Env;

        /// <summary>An agent's: the world's front door for the new seat, its sponsor, its Character and the Charter to read first; null for a player.</summary>
        [JsonProperty("front_door")]
        public OWFrontDoor FrontDoor;

        /// <summary>
        /// An agent's: the world's reading aids as the new seat reads them, the Charter, the Guide and the Domains (the
        /// same packet as <c>GET /me</c>'s, <see cref="OWReadingAids.Of"/>); null for a player or an older keel.
        /// </summary>
        public OWReadingAids ReadingAids => OWReadingAids.Read(_readingAids);

        [JsonProperty("reading_aids")]
        private JObject _readingAids;

        public bool IsPlayer => Kind == "player";
        public bool IsAgent => Kind == "agent";

        /// <summary>The role in the world, wherever this kind of answer carries it.</summary>
        public string RoleInWorld => Role ?? Seat?.Role;
    }

    /// <summary>
    /// Joining a world by link, with no account: a player's code (a friend reading as one Character)
    /// or an AI agent's. Sends no credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static, beside <see cref="OWClient"/> rather than on it: the client cannot be built without a
    /// key, and joining is how a program gets one. npm puts the same two calls on its client
    /// (<c>previewJoin</c>, <c>redeemJoin</c>) and sends them with no credential whatever key it holds;
    /// the wire is the same.
    /// </para>
    /// <para>
    /// A link is <c>&lt;site&gt;/join#&lt;code&gt;</c>, or <c>&lt;site&gt;/join/&lt;world_id&gt;#&lt;code&gt;</c> when
    /// its owner turned the preview on: the code sits after <c>#</c>, so it never reaches a
    /// server log. Every bad code (unknown, used, revoked, expired, or for another reader) is the same
    /// 404 <c>not_found</c>, byte for byte: <see cref="OWApiError.IsNotFound"/>.
    /// </para>
    /// </remarks>
    public static class OWJoin
    {
        /// <summary>Every join code starts with this.</summary>
        public const string CodePrefix = "ow_j_";

        public const string DefaultBaseUrl = "https://www.onlyworlds.com/api/v2";

        /// <summary>
        /// The join code in a pasted link (<c>https://www.onlyworlds.com/join#ow_j_…</c> or
        /// <c>…/join/&lt;world_id&gt;#ow_j_…</c>, any host) or
        /// a bare code, or null when the text holds none. Only the shape is checked: the server is the
        /// only judge of whether a code is good.
        /// </summary>
        public static string ParseLink(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var s = text.Trim();
            var hash = s.LastIndexOf('#');
            if (hash >= 0) s = s.Substring(hash + 1).Trim();

            if (!s.StartsWith(CodePrefix, StringComparison.Ordinal) || s.Length == CodePrefix.Length) return null;

            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c)) return null;
            }

            return s;
        }

        /// <summary>
        /// POST /join/preview -- who invites, to which world, in what role, and for which
        /// <see cref="OWJoinPreview.Reader"/>. The code stays unused.
        /// </summary>
        public static Task<OWJoinPreview> PreviewAsync(
            string code, IOWTransport transport, string baseUrl = null, CancellationToken ct = default)
            => PostAsync<OWJoinPreview>("/join/preview", new JObject { ["code"] = code }, transport, baseUrl, ct);

        /// <summary>
        /// POST /join -- redeem a player's or an agent's code. 201 with <see cref="OWJoined.Kind"/>
        /// telling the answers apart. <paramref name="name"/> is a player's (optional: the Character's
        /// name, or "Player", when left out); <paramref name="agentName"/> an agent's (required for an
        /// agent code, a 422 on <c>agent_name</c> without it). A person's code is a 404 here.
        /// <paramref name="character"/> is a party link's pick, an id from the preview's
        /// <see cref="OWJoinPreview.Characters"/> (refused as <see cref="OWApiError.IsCharacterTaken"/> or
        /// <see cref="OWApiError.IsCharacterInvalid"/>); a table that lists none wants <paramref name="name"/>
        /// instead, and a link for one ignores it. It comes last so a caller passing the others by position keeps
        /// working.
        /// </summary>
        public static Task<OWJoined> RedeemAsync(
            string code, IOWTransport transport, string name = null, string agentName = null,
            string baseUrl = null, CancellationToken ct = default, string character = null)
        {
            var body = new JObject { ["code"] = code };
            if (name != null) body["name"] = name;
            if (agentName != null) body["agent_name"] = agentName;
            if (character != null) body["character"] = character;

            return PostAsync<OWJoined>("/join", body, transport, baseUrl, ct);
        }

        private static async Task<T> PostAsync<T>(
            string path, JObject body, IOWTransport transport, string baseUrl, CancellationToken ct)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (string.IsNullOrEmpty((string)body["code"])) throw new ArgumentException("A join code is required.", "code");

            var request = new OWRequest
            {
                Method = "POST",
                Url = (baseUrl ?? DefaultBaseUrl).TrimEnd('/') + path,
                // No API-Key and no Bearer: keel declares no security on either route.
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                Body = body.ToString(Formatting.None),
            };

            OWResponse response;
            try
            {
                response = await transport.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new OWTransportError($"OnlyWorlds request failed: POST {path}", e);
            }

            if (!response.IsSuccess) throw OWClient.BuildError(response);

            return OWJson.Deserialize<T>(response.Body);
        }
    }
}
