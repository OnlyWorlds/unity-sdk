using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>Construction options for <see cref="OWClient"/>.</summary>
    public class OWClientConfig
    {
        /// <summary>
        /// The key: an <c>ow_w_</c> (write) or <c>ow_r_</c> (read) world key, an <c>ow_a_</c> account token (sent as a
        /// Bearer, for the <c>/account</c> routes and the lens routes, and with <see cref="World"/> set for that world's
        /// reads), or a
        /// 10-digit legacy key. Empty with <see cref="World"/> set: a keyless reader of a public world. Empty with no
        /// <see cref="World"/>: the bare client, the TypeScript SDK's <c>new OwV2Client()</c>, which sends
        /// <see cref="OWClient.HealthAsync"/> and the sign-ins (<see cref="OWClient.SignInWithPasswordAsync"/>,
        /// <see cref="OWClient.SignInWithOnlyWorlds"/>, <see cref="OWClient.CompleteSignInAsync"/>), the client a person
        /// signing in has, and refuses every other call before sending. <see cref="OWJoin"/>'s keyless calls take no client.
        /// </summary>
        public string ApiKey;

        /// <summary>
        /// A world's id, for a client that holds an account token (<c>ow_a_</c>) or no key. <see cref="OWClient.ListAsync{T}"/>,
        /// <see cref="OWClient.ListAllAsync{T}"/> (every page), <see cref="OWClient.GetAsync{T}"/>,
        /// <see cref="OWClient.GetWorldAsync"/> and <see cref="OWClient.DraughtAsync"/> then read that world, sending
        /// <c>world=&lt;id&gt;</c>: an account token reads it as that account's own access there (the owner the whole world, a member its member key's view,
        /// anyone else the public view, and a world not open to the public is a 404), and no key reads it as the public
        /// does. Null by default.
        /// </summary>
        /// <remarks>
        /// An account token also sets its own lenses there with <see cref="OWClient.SetMyLensesAsync"/>
        /// (<c>world=&lt;id&gt;</c>), the one write it names. On such a client every other world call is an
        /// <see cref="InvalidOperationException"/> before anything is sent; an account token's <c>/account</c> calls and
        /// <see cref="OWClient.WorldLensesAsync"/> (the world in its path) work as without it. Given with a world key,
        /// or empty or white space, it is an <see cref="ArgumentException"/> when the client is built. Mirrors the
        /// TypeScript SDK's <c>world</c> option (4.88.0).
        /// </remarks>
        public string World;

        /// <summary>
        /// The PIN (or an agent seat's <c>ow_s_</c> secret), needed for writes when the world has one, and for a
        /// legacy key's reads of a world with a PIN. Sent as <c>API-Pin</c> only where keel checks it: on every
        /// write, on <see cref="OWClient.GetMeAsync"/> (the connection test), and on every request of a 10-digit
        /// legacy key. A prefixed key's other reads go without it, and an account token never sends it.
        /// </summary>
        public string ApiPin;
        public string BaseUrl = "https://www.onlyworlds.com/api/v2";

        /// <summary>Page size. The server silently clamps at 1000.</summary>
        public int PageSize = 100;

        public IOWTransport Transport;

        /// <summary>
        /// Declare this client an AI agent: every element write (create, upsert, patch, delete,
        /// link edit, bulk, adopt) carries <c>X-OW-Agent</c>, and keel records it as agent-made
        /// (<see cref="OWElement.ChangedVia"/> reads <see cref="OWWriteDoor.Declared"/>; keel stores that
        /// the header was there, never its value). Off by default, and it must stay off in a tool
        /// people use: it describes the caller, so only the caller can set it. An agent seat's key is
        /// recorded as an agent without it.
        /// </summary>
        public bool Agent;

        /// <summary>
        /// This client's own name, sent as <c>X-OW-Client</c> on every element write (create, upsert, patch, delete,
        /// link edit, bulk, adopt) and on no read. keel stores it on the element as
        /// <see cref="OWElement.ChangedClient"/>, and on a <c>/changes</c> delete as <see cref="OWChange.DeletedClient"/>,
        /// so a reader can tell which tool made a change. Null (the default) sends
        /// <see cref="OWClient.DefaultClientName"/>; a game or tool passes its own.
        /// </summary>
        /// <remarks>
        /// The rule is keel's: lower case letters, digits and hyphens, 1 to 32 characters (<c>[a-z0-9-]{1,32}</c>),
        /// no version (<c>atlas</c>, not <c>atlas-1.76</c>). Anything else, the empty string included, is an
        /// <see cref="ArgumentException"/> when the client is built, and nothing is trimmed (keel would store null
        /// for it). It is the client's claim about itself, not proof, and it changes no permission and no door:
        /// whether an agent made a write is <see cref="Agent"/>'s business.
        /// </remarks>
        public string Client;
    }

    /// <summary>
    /// What <see cref="OWClient.RequestAsync(string, string, OWRequestOptions, CancellationToken)"/> sends beside the
    /// method and path, and which of the client's rules it asks for. Every member left at its default sends nothing.
    /// </summary>
    public class OWRequestOptions
    {
        /// <summary>The query, without its <c>?</c>, sent as given: escape its values yourself. Null or empty: none.</summary>
        public string Query;

        /// <summary>
        /// The JSON body, sent exactly as given (with <c>Content-Type: application/json</c>): nothing is cleaned, so no
        /// read-only key is left out and a null stays null. Null: no body. Never on a GET.
        /// </summary>
        public JToken Body;

        /// <summary>Sent as <c>Idempotency-Key</c> when not null or empty.</summary>
        public string IdempotencyKey;

        /// <summary>A <c>change_seq</c> sent as <c>If-Match</c>, quoted; a whole number, else refused before sending.</summary>
        public long? IfMatch;

        /// <summary>
        /// A write keel's document lists <c>X-OW-Agent</c> on (the element writes, the links route, bulk, adopt, the file
        /// ticket): an agent client (<see cref="OWClientConfig.Agent"/>) sends it.
        /// </summary>
        public bool Write;

        /// <summary>
        /// One of the writes keel reads <c>X-OW-Client</c> on (create, put, patch, delete, links, bulk, adopt, propose,
        /// accept): the client's name (<see cref="OWClient.ClientName"/>) goes.
        /// </summary>
        public bool ElementWrite;

        /// <summary>
        /// A write keel may answer 202 with <c>{proposal}</c> (put, patch, links): a 202 is then an
        /// <see cref="OWProposedError"/>, not a body that is not the element.
        /// </summary>
        public bool Proposable;

        /// <summary>
        /// A GET keel serves an account token, or no key, with <c>world=&lt;id&gt;</c> (element list and get, the world
        /// GET): on a client with <see cref="OWClient.World"/> set, <c>world=&lt;id&gt;</c> is added to the query.
        /// </summary>
        public bool WorldRead;

        /// <summary>
        /// A route keel's document declares the account token on beside the world key (the lens routes, the roster): how an
        /// account client sends it. <see cref="OWAccountTokenRoute.None"/> on any other route.
        /// </summary>
        public OWAccountTokenRoute AccountToken;

        /// <summary>
        /// False for a route keel serves with no credential (the health pulse): no key, PIN or Bearer is sent, no
        /// <c>world=</c> is added, and no client is refused for the key it holds. True by default.
        /// </summary>
        public bool Auth = true;
    }

    /// <summary>How an account token sends a world route keel declares it on beside the world key.</summary>
    public enum OWAccountTokenRoute
    {
        /// <summary>Not such a route: an account token sends only the <c>/account</c> routes and the world reads.</summary>
        None,

        /// <summary>The path names the world; nothing is added.</summary>
        Path,

        /// <summary>The world is named with <c>world=&lt;id&gt;</c>, from <see cref="OWClient.World"/>, which must be set.</summary>
        World,
    }

    /// <summary>
    /// A thin typed client for the OnlyWorlds v2 API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately thin: no caching, no sync state, no retry policy. Those belong to callers --
    /// the cache layer, a sync engine, a game. What IS encoded here is the wire contract and its
    /// safety rails: read-only-field stripping, opaque cursors, client-minted UUIDs, and typed
    /// errors carrying doc URLs.
    /// </para>
    /// <para>
    /// Mirrors the npm SDK's <c>OwV2Client</c> deliberately. Two clients speaking one wire that
    /// disagree about its contract is a bug factory, so where behaviour is observable the shapes
    /// match and the divergence points are commented.
    /// </para>
    /// </remarks>
    public class OWClient
    {
        private readonly string _apiKey;
        private readonly string _apiPin;
        private readonly IOWTransport _transport;
        private readonly bool _agent;

        /// <summary>True for a client built with no key: a keyless reader of the world <see cref="World"/> names, or the bare client.</summary>
        private readonly bool _keyless;

        /// <summary>The name sent as <c>X-OW-Client</c> when <see cref="OWClientConfig.Client"/> is null.</summary>
        public const string DefaultClientName = "unity-sdk";

        /// <summary>The reads that send <c>world=&lt;id&gt;</c>, as the refusals name them.</summary>
        private const string WorldReads = "ListAsync, ListAllAsync, GetAsync, GetWorldAsync and DraughtAsync";

        public string BaseUrl { get; }

        /// <summary>The name this client sends as <c>X-OW-Client</c> on element writes.</summary>
        public string ClientName { get; }
        public int PageSize { get; }

        /// <summary>The key's kind; <see cref="OWKeyKind.Unknown"/> on a keyless client.</summary>
        public OWKeyKind KeyKind { get; }

        /// <summary>
        /// The world an account token or a keyless client reads (<see cref="OWClientConfig.World"/>), or null.
        /// </summary>
        public string World { get; }

        public OWClient(OWClientConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            _keyless = string.IsNullOrEmpty(config.ApiKey);
            KeyKind = OWKey.DetectKind(config.ApiKey);
            World = WorldOf(config.World, _keyless, KeyKind);

            if (config.Transport == null)
                throw new ArgumentException("A transport is required.", nameof(config));

            _apiKey = _keyless ? null : config.ApiKey;
            _apiPin = string.IsNullOrEmpty(config.ApiPin) ? null : config.ApiPin;
            _transport = config.Transport;
            _agent = config.Agent;

            BaseUrl = (config.BaseUrl ?? "https://www.onlyworlds.com/api/v2").TrimEnd('/');
            PageSize = config.PageSize > 0 ? config.PageSize : 100;
            ClientName = ClientNameOf(config.Client);
        }

        /// <summary>
        /// <see cref="OWClientConfig.World"/>, checked when the client is built: a world's id that is not empty or white
        /// space, on a client with an account token or no key. A world key reads its own world and takes none.
        /// </summary>
        private static string WorldOf(string value, bool keyless, OWKeyKind kind)
        {
            if (value == null) return null;

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"World is a world's id, not empty or white space; got \"{value}\". With it, an account token or no "
                    + $"key reads that world through {WorldReads}.",
                    "config");
            }

            if (!keyless && kind != OWKeyKind.Account)
            {
                throw new ArgumentException(
                    "A world key reads its own world; World is for an account token (ow_a_) or no key, which read the "
                    + $"world it names through {WorldReads}.",
                    "config");
            }

            return value;
        }

        /// <summary>
        /// The server's rule for <c>X-OW-Client</c>, exact here: keel trims and stores null for anything else, this
        /// client refuses it. Checked by hand, not by a regex, whose <c>$</c> would let a trailing newline through.
        /// </summary>
        private static string ClientNameOf(string value)
        {
            if (value == null) return DefaultClientName;

            var ok = value.Length >= 1 && value.Length <= 32;
            foreach (var c in value)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) ok = false;
            }

            if (!ok)
            {
                throw new ArgumentException(
                    $"Client must be lower case letters, digits and hyphens, 1 to 32 characters ([a-z0-9-]{{1,32}}), "
                    + $"such as 'atlas'; got \"{value}\". It is sent as X-OW-Client on element writes; leave it null "
                    + $"for '{DefaultClientName}'.",
                    "config");
            }

            return value;
        }

        // -- Health and world meta --------------------------------------------

        /// <summary>
        /// GET /health -- is the server up: keel's liveness answer, the body as served.
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// Sent with no credential at all, on every client: no <c>API-Key</c>, PIN or Bearer, and no <c>world</c> on a
        /// client with <see cref="World"/> set, so an account token and a keyless client call it as a world key does.
        /// Mirrors the TypeScript SDK's <c>health</c> (3.0.0; on a keyless client from 4.30.0).
        /// </remarks>
        public Task<JObject> HealthAsync(CancellationToken ct = default)
            => RequestAsync<JObject>("GET", "/health/", ct: ct, auth: false);

        /// <summary>
        /// GET /world -- world metadata.
        /// </summary>
        /// <remarks>
        /// World-meta edits do NOT appear in <c>/changes</c> and do not bump <c>change_seq</c>.
        /// Freshness of world meta is only knowable by polling this and comparing
        /// <c>updated_at</c>. On a client with <see cref="World"/> set (an account token or no key), it reads that
        /// world, sending <c>world=&lt;id&gt;</c>.
        /// </remarks>
        public Task<JObject> GetWorldAsync(CancellationToken ct = default)
            => RequestAsync<JObject>("GET", "/world/", ct: ct, worldRead: true);

        /// <summary>
        /// PATCH /world -- changes the world's own settings: only the keys sent. Returns the world as the server
        /// holds it after.
        /// </summary>
        /// <param name="partial">
        /// The keys to change: a <see cref="JObject"/>, a dictionary or any object. A <see cref="GetWorldAsync"/> body
        /// can be sent back whole. Not changed.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="ArgumentNullException"><paramref name="partial"/> is null.</exception>
        /// <exception cref="OWApiError">
        /// The server refused: a key it does not take is a 422; a key only the owner may change, sent by a member's key,
        /// is a 403 (<see cref="OWApiError.IsOwnerOnly"/>).
        /// </exception>
        /// <remarks>
        /// <para>
        /// The server-managed keys (<see cref="OWPayload.ReadOnlyFields"/>) and the world's own read-only ones
        /// (<see cref="OWPayload.WorldReadOnlyFields"/>: <c>id</c>, <c>slug</c>, <c>public_page</c>, <c>public_read</c>,
        /// <c>agent_narrator_rule</c>, <c>agent_writes_proposed</c>, <c>origin</c>, <c>counts</c>) are left out. Everything else is sent
        /// as given, <c>null</c> included.
        /// </para>
        /// <para>
        /// The world's <c>origin</c>, where it first came from, is written as the plain keys <c>source</c> and
        /// <c>producer</c> (each an object or <c>null</c>, which clears it; the folder format's provenance lines, §4.2) and
        /// served back under <c>origin</c>. Extension keys (<c>x_*</c>, <c>atlas_*</c>, <c>shadow_*</c>) are a tool's own
        /// settings on the world, kept as given at the top level of its body, at most 64 KB in all; <c>null</c> removes
        /// one.
        /// </para>
        /// <para>
        /// The owner's key may send any key the server takes; a co-builder's only <c>time_range_current</c>,
        /// <c>source</c>, <c>producer</c> and extension keys. Sent with the PIN, as every write is, and without
        /// <c>X-OW-Agent</c> or <c>X-OW-Client</c>, which the server reads on element writes only. A world-settings edit
        /// does not appear in <c>/changes</c>: read <see cref="GetWorldAsync"/>'s <c>updated_at</c> for it.
        /// </para>
        /// </remarks>
        public Task<JObject> PatchWorldAsync(object partial, CancellationToken ct = default)
        {
            if (partial == null) throw new ArgumentNullException(nameof(partial));

            var body = JObject.FromObject(partial, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));
            foreach (var field in OWPayload.ReadOnlyFields) body.Remove(field);
            foreach (var field in OWPayload.WorldReadOnlyFields) body.Remove(field);

            return RequestAsync<JObject>("PATCH", "/world/", body: body, ct: ct);
        }

        /// <summary>
        /// GET /me -- who this key is: <c>{world, scope, kind, role, membership, character, lenses, name?}</c>. <c>kind</c> is
        /// <c>owner</c> for an owner-minted or classic key, else the membership's (<c>person</c>, <c>agent</c>,
        /// <c>player</c>); <c>role</c> is <c>owner</c>, <c>co_builder</c>, <c>contributor</c> or <c>guest</c>;
        /// <c>character</c> is the Character id the key acts as, or null (also when it is hidden from this key);
        /// <c>lenses</c> is the lens ids its holder chose (<see cref="SetMyLensesAsync"/>), in order, empty for none, which
        /// is a choice (<see cref="OWLens.LensesFor"/> takes it as the membership's); <c>name</c> is served on agent and
        /// player seats only: your agent's or your player's name.
        /// </summary>
        public Task<JObject> GetMeAsync(CancellationToken ct = default)
            => RequestAsync<JObject>("GET", "/me/", ct: ct);

        /// <summary>
        /// PUT /me/character -- pick the roster Character that is you in this world, or none. Answers GET /me's body
        /// (<see cref="GetMeAsync"/>), its <c>character</c> the pick as kept.
        /// </summary>
        /// <param name="character">The Character's id, or null for none (sent as <c>null</c>).</param>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// A person member's write key, with its PIN. keel answers 422 for a hidden Character (<c>character_hidden</c>), a
        /// guest's pick it did not make, or a Character someone else holds (<c>param</c> <c>character</c>); 404 for one
        /// this key cannot see; 403 for an agent seat, a player, the owner (whose own is the world's
        /// <c>owner_character</c>, set with <see cref="PatchWorldAsync"/>) or a read key. A guest's view resets once. Sent
        /// without <c>X-OW-Agent</c> or <c>X-OW-Client</c>, which keel's document declares on neither. An account token
        /// is no member of one world and a keyless client holds no key: each is refused, and nothing is sent. Mirrors the
        /// TypeScript SDK's <c>setMyCharacter</c> (4.50.0).
        /// </remarks>
        public Task<JObject> SetMyCharacterAsync(string character, CancellationToken ct = default)
            => RequestAsync<JObject>("PUT", "/me/character", body: new JObject { ["character"] = character }, ct: ct);

        /// <summary>
        /// GET /members -- the world's roster, the rows of keel's <c>{data}</c> (no paging): each row's <c>id</c> (the
        /// membership id that <c>created_by</c> holds, null for the owner), <c>kind</c> (<c>owner</c>, <c>person</c>,
        /// <c>agent</c>, <c>player</c>), <c>role</c>, <c>character</c> (null too when it is hidden from this key),
        /// <c>status</c> (<c>active</c>, or <c>removed</c>: removed rows stay, so their elements keep <c>created_by</c>),
        /// <c>lenses</c> and <c>lenses_from</c>, and <c>name</c> where this key is served it. The list the message readers
        /// take as <c>members</c> (<see cref="OWMessage.SenderStatus"/>, <see cref="OWMessage.ReadMarkOf"/>).
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// A world key reads its own world's roster, sending no <c>world</c> and no PIN. An account token reads the roster
        /// of the world <see cref="World"/> names, sending <c>world=&lt;id&gt;</c> and its Bearer, with the token's grant
        /// there (a world where it has none answers 404); without <see cref="World"/> set it is an
        /// <see cref="InvalidOperationException"/>, and nothing is sent. A keyless client is refused, and nothing is sent:
        /// the roster has no public reader. Mirrors the TypeScript SDK's <c>members()</c> (4.22.0; on an account token
        /// from 4.116.0).
        /// </remarks>
        public async Task<JArray> GetMembersAsync(CancellationToken ct = default)
        {
            var page = await RequestAsync<JObject>("GET", "/members/", ct: ct, accountToken: OWAccountTokenRoute.World)
                .ConfigureAwait(false);
            return page?["data"] as JArray ?? new JArray();
        }

        // -- Lenses -----------------------------------------------------------
        //
        // A lens is a Construct with supertype Lens (OWLens reads one). These are the server's own lens routes: the
        // lenses a key's holder starts with, and the world's lenses as this key sees them. Mirrors the TypeScript SDK's
        // setMyLenses and worldLenses (4.92.0).

        /// <summary>
        /// PUT /me/lenses -- choose the lenses this key's holder starts with: lens ids in order, 0 to 20, repeats dropped
        /// by the server; an empty list is a choice too, and keeps a seat off its Titles' lenses. Null goes back to never
        /// chosen. Answers GET /me's body (<see cref="GetMeAsync"/>), its <c>lenses</c> the list as kept.
        /// </summary>
        /// <param name="lensIds">
        /// The lens ids, in the order the holder reads them; empty for none; null (sent as <c>null</c>) for never chosen,
        /// so a seat playing a Character starts with its Titles' lenses again (<c>lenses_from</c> says where a list comes
        /// from); on the owner's own keys null is the empty list.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// Any write key of the world, with its PIN, sets its own holder's list: a person member's, an agent seat's, or on
        /// the owner's keys the owner's own. A read key is a 403. The server refuses a lens this key cannot see (404) and
        /// answers 422 <c>not_a_lens</c>, <c>lens_is_template</c> or <c>lens_hidden</c> (<c>param</c> <c>lenses</c>). A lens
        /// only narrows what its holder may already read. Sent without <c>X-OW-Agent</c> or <c>X-OW-Client</c>, which the
        /// server's document declares on neither. An account token sets its own list in the world <see cref="World"/>
        /// names, sending <c>world=&lt;id&gt;</c> and its Bearer, never a PIN; without <see cref="World"/> set it is an
        /// <see cref="InvalidOperationException"/>, and nothing is sent. A world key sends no <c>world</c>: the key names
        /// its world. A keyless client is refused, and nothing is sent. Mirrors the TypeScript SDK's
        /// <c>setMyLenses</c> (4.92.0; on an account token from 4.94.0; null from 4.99.0).
        /// </remarks>
        public Task<JObject> SetMyLensesAsync(IEnumerable<string> lensIds, CancellationToken ct = default)
        {
            var body = new JObject { ["lenses"] = LensList(lensIds) };
            return RequestAsync<JObject>("PUT", "/me/lenses", body: body, ct: ct, accountToken: OWAccountTokenRoute.World);
        }

        /// <summary>
        /// GET /world/{world_id}/lenses -- every lens in the world this key can see, templates included and marked:
        /// <c>{data, read_as}</c>, each row its <c>id</c>, <c>name</c>, the lens <c>element</c> as this key is served it,
        /// <c>template</c>, <c>retired</c>, the recipe echoed with its defaults filled in (<c>x_lens</c>, null for an
        /// unreadable one, which then carries <c>unreadable</c>), the Titles it is the default for (<c>titles</c>) and
        /// whether this key chose it (<c>chosen</c>). The answer is the body as served.
        /// </summary>
        /// <param name="worldId">The key's own world (another is a 404). Escaped.</param>
        /// <param name="resolve">
        /// <c>true</c> adds each lens's <c>reading</c> for this key: the kept rows, the cut by reason, the mail part. Sent as
        /// given, <c>false</c> too; null sends nothing.
        /// </param>
        /// <param name="readAs">
        /// With <paramref name="resolve"/>, the owner reads as another reader: <c>guest</c>, <c>contributor</c>,
        /// <c>co-builder</c>, <c>public</c>, or a membership id of this world. Sent as <c>read_as</c>, escaped; the server
        /// answers 403 <c>read_as_owner_only</c> from any other key, and 422 without <c>resolve</c>. Null or empty sends nothing.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// The owner's rows also carry <c>chosen_by</c> and <c>health</c>. A read, so the key goes without its PIN. An
        /// account token reads the world <paramref name="worldId"/> names, as its owner or as its member there: its Bearer
        /// alone, no <c>world=</c> (<see cref="World"/> is not needed, and not sent when set). A keyless client is refused,
        /// and nothing is sent. Served <c>Cache-Control: private, no-store</c>. Mirrors the TypeScript SDK's
        /// <c>worldLenses</c> (4.92.0; on an account token from 4.94.0).
        /// </remarks>
        public Task<JObject> WorldLensesAsync(
            string worldId, bool? resolve = null, string readAs = null, CancellationToken ct = default)
        {
            var query = new List<string>();
            if (resolve.HasValue) query.Add("resolve=" + (resolve.Value ? "true" : "false"));
            if (!string.IsNullOrEmpty(readAs)) query.Add("read_as=" + Uri.EscapeDataString(readAs));

            return RequestAsync<JObject>("GET", $"/world/{Uri.EscapeDataString(worldId)}/lenses",
                query: string.Join("&", query), ct: ct, accountToken: OWAccountTokenRoute.Path);
        }

        // -- Element reads ----------------------------------------------------

        /// <summary>
        /// GET /{type}/ -- one cursor page. On a client with <see cref="World"/> set (an account token or no key), it
        /// reads that world, sending <c>world=&lt;id&gt;</c> beside the page's own parameters.
        /// </summary>
        /// <remarks>
        /// The parameters go in the TypeScript SDK's order: <c>limit</c>, <c>cursor</c>, <c>expand</c>, <c>fields</c>,
        /// <c>ids</c>, <c>created_by</c>, then the named filters and <see cref="OWListParams.Filter"/>'s, and <c>as</c>
        /// last. Mirrors the TypeScript SDK's <c>list</c> options <c>expand</c>, <c>ids</c>, <c>createdBy</c> and
        /// <c>viewAs</c> (<c>viewAs</c> from 4.37.0).
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <see cref="OWListParams.Filter"/> holds <c>world</c>, a key that is no field name, or a key a typed member sends
        /// in the same request; nothing was sent.
        /// </exception>
        public Task<OWPage<T>> ListAsync<T>(string type, OWListParams p = default, CancellationToken ct = default)
        {
            // Each typed parameter as (name, value), in order; a null value is not sent.
            var typed = new List<(string Name, string Value)>
            {
                ("limit", (p.Limit ?? PageSize).ToString(CultureInfo.InvariantCulture)),
                ("cursor", NullIfEmpty(p.Cursor)),
                ("expand", Joined(p.Expand)),
                ("fields", Joined(p.Fields)),
                ("ids", Joined(p.Ids)),
                ("created_by", NullIfEmpty(p.CreatedBy)),
                ("name", NullIfEmpty(p.Name)),
                ("name__icontains", NullIfEmpty(p.NameContains)),
                ("supertype", NullIfEmpty(p.Supertype)),
                ("subtype", NullIfEmpty(p.Subtype)),
            };
            var viewAs = NullIfEmpty(p.ViewAs);

            var query = new List<string>();
            foreach (var (name, value) in typed)
            {
                if (value != null) query.Add(name + "=" + Uri.EscapeDataString(value));
            }

            if (p.Filter != null)
            {
                // world is the client's, set when it is built: in a filter it would go out twice beside World's world=,
                // or to a world key that 422s it. Mirrors the TypeScript SDK's list() refusal (4.88.0).
                if (p.Filter.ContainsKey("world"))
                {
                    throw new ArgumentException(
                        "ListAsync: world is not a filter; an account token or no key names its world with World "
                        + "(new OWClientConfig { ApiKey, World }). Nothing was sent.", nameof(p));
                }

                var fields = new List<string>(p.Filter.Keys);
                fields.Sort(StringComparer.Ordinal);
                foreach (var field in fields)
                {
                    if (string.IsNullOrEmpty(field) || !IsFieldName(field))
                        throw new ArgumentException($"'{field}' is not a field name to filter on", nameof(p));

                    // A key a typed member sends here would go out twice, and keel reads one of the two.
                    if ((field == "as" && viewAs != null) || typed.Exists(t => t.Name == field && t.Value != null))
                    {
                        throw new ArgumentException(
                            $"ListAsync: '{field}' is sent by its own OWListParams member in this request; give it there "
                            + "or in Filter, not both. Nothing was sent.", nameof(p));
                    }

                    query.Add(field + "=" + Uri.EscapeDataString(p.Filter[field] ?? ""));
                }
            }

            if (viewAs != null) query.Add("as=" + Uri.EscapeDataString(viewAs));

            return RequestAsync<OWPage<T>>("GET", $"/{type}/", query: string.Join("&", query), ct: ct, worldRead: true);
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        /// <summary>A list of names joined by commas, or null for none (null or empty).</summary>
        private static string Joined(string[] names) => names == null || names.Length == 0 ? null : string.Join(",", names);

        /// <summary>A schema field's shape (<c>parent_location</c>): a lowercase letter, then lowercase letters, digits and <c>_</c>.</summary>
        private static bool IsFieldName(string s)
        {
            if (s[0] < 'a' || s[0] > 'z') return false;
            foreach (var c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        /// <summary>
        /// Walks every page of a type. Drives from <c>has_more</c>, never from page size.
        /// </summary>
        /// <remarks>
        /// The server silently clamps limit at 1000, so a page shorter than requested is NOT a
        /// signal that iteration is done. Treating it as one loses elements with no error. On a client
        /// with <see cref="World"/> set, every page is read from that world, as <see cref="ListAsync{T}"/> reads it.
        /// </remarks>
        /// <exception cref="OWTransportError">
        /// A page answered the cursor it was sent (the caller's starting cursor included) with more to come: the walk
        /// stops rather than read that page again, and the elements read so far are not returned. Mirrors the TypeScript
        /// SDK's <c>listAll</c> guard (4.88.0, its <c>OwNetworkError</c>).
        /// </exception>
        public async Task<List<T>> ListAllAsync<T>(
            string type, OWListParams p = default, CancellationToken ct = default,
            Action<int, int> onPage = null)
        {
            var all = new List<T>();
            var cursor = p.Cursor;
            var pageNumber = 0;

            do
            {
                ct.ThrowIfCancellationRequested();

                // Copy the caller's params and move only the cursor, so every filter rides on
                // every page; rebuilding the struct field by field drops whatever it forgets.
                var pageParams = p;
                pageParams.Cursor = cursor;
                var page = await ListAsync<T>(type, pageParams, ct).ConfigureAwait(false);

                if (page?.Data != null) all.AddRange(page.Data);

                pageNumber++;
                onPage?.Invoke(pageNumber, all.Count);

                // Guard against a server that says has_more but stops issuing cursors -- without
                // this, that shape is an infinite loop rather than a bad page.
                if (page == null || !page.HasMore || string.IsNullOrEmpty(page.NextCursor)) break;

                // A page that hands back the cursor it was sent would be read again forever.
                if (page.NextCursor == cursor)
                {
                    throw new OWTransportError(
                        $"ListAllAsync: GET /{type}/ answered the cursor it was sent, with more to come; the walk stopped "
                        + "rather than read the same page again");
                }

                cursor = page.NextCursor;
            }
            while (true);

            return all;
        }

        /// <summary>
        /// GET /{type}/{id}/. On a client with <see cref="World"/> set (an account token or no key), it reads that
        /// world, sending <c>world=&lt;id&gt;</c> beside the get's own parameters.
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <param name="expand">Link fields to expand one level into stubs (<c>friends</c>, <c>location</c>); sent as
        /// <c>expand</c>, joined by commas. Null or empty sends nothing.</param>
        /// <param name="fields">The fields to serve, a sparse body; sent as <c>fields</c>, joined by commas. Null or empty
        /// sends nothing.</param>
        /// <param name="viewAs">
        /// View as, sent as <c>as</c>: the owner's key reads as another reader of this world would, a membership id as
        /// that member's own key, a Character id as a guest of that Character, <c>public</c> as the public. A member key
        /// gets a 403 <c>view_as_owner_only</c>; an element hidden from that reader is a 404, never a 410; an unknown
        /// value is a 422 (<c>param</c> <c>as</c>). Null or empty sends nothing.
        /// </param>
        /// <remarks>
        /// The three options come last, so a caller passing <paramref name="ct"/> by position keeps working. Mirrors the
        /// TypeScript SDK's <c>get</c> options <c>expand</c>, <c>fields</c> and <c>viewAs</c> (<c>viewAs</c> from 4.37.0),
        /// sent in its order.
        /// </remarks>
        public Task<T> GetAsync<T>(
            string type, string id, CancellationToken ct = default,
            string[] expand = null, string[] fields = null, string viewAs = null)
        {
            var query = new List<string>();
            if (expand != null && expand.Length > 0) query.Add("expand=" + Uri.EscapeDataString(string.Join(",", expand)));
            if (fields != null && fields.Length > 0) query.Add("fields=" + Uri.EscapeDataString(string.Join(",", fields)));
            if (!string.IsNullOrEmpty(viewAs)) query.Add("as=" + Uri.EscapeDataString(viewAs));

            return RequestAsync<T>("GET", $"/{type}/{id}/", query: string.Join("&", query), ct: ct, worldRead: true);
        }

        // -- Element writes ---------------------------------------------------

        /// <summary>
        /// POST /{type}/ -- creates, minting a client-side UUIDv7 (<see cref="OWId"/>) when the payload has none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A retry is safe when the element carries its id: a create that already landed answers
        /// 409 <c>id_conflict</c> (<see cref="OWApiError.Code"/>), and the element is there. An
        /// element without one gets a fresh id on every call, so an unkeyed retry of it creates a
        /// second element. A caller who retries passes <paramref name="idempotencyKey"/>.
        /// </para>
        /// <para>
        /// <c>Idempotency-Key</c> is sent only when <paramref name="idempotencyKey"/> is given, as
        /// the npm SDK does. With one, keel keeps the first success for that key for 24 hours and
        /// answers an identical retry with it, writing nothing; the same key with another body is
        /// 409 <c>idempotency_error</c>. Pass a key when you want the replay. Never reuse one for a
        /// new write: an undo that re-creates a deleted element under the key that first created it
        /// gets the old answer back, and the element stays deleted.
        /// </para>
        /// <para>
        /// With a key and no id, the id is derived from the key (<see cref="OWId.Keyed"/>), not freshly
        /// minted, so a retry sends the same body and replays. The Python package and the npm SDK
        /// derive the same id for the same key.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The element is a known-of stub (<see cref="OWPayload.IsKnownOf"/>); nothing was sent.
        /// </exception>
        public Task<T> CreateAsync<T>(string type, object element, string idempotencyKey = null, CancellationToken ct = default)
        {
            var raw = JObject.FromObject(element, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));
            RefuseKnownOf(raw, "create", nameof(element));
            var body = Sanitize(raw, type);

            if (body["id"] == null || string.IsNullOrEmpty(body["id"].ToString()))
            {
                body["id"] = OWId.Mint(idempotencyKey, 0);
            }

            return RequestAsync<T>("POST", $"/{type}/", body: body,
                idempotencyKey: idempotencyKey, write: true, ct: ct);
        }

        /// <summary>
        /// PUT /{type}/{id}/ -- upsert by the client's own id: creates the element under <paramref name="id"/>, or
        /// writes it whole when it is there. The local-first write.
        /// </summary>
        /// <param name="type">The element type slug.</param>
        /// <param name="id">The element's id, minted by the caller (<see cref="OWId"/>).</param>
        /// <param name="element">The element as it should be: a <see cref="JObject"/>, a model or any object.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// The body is cleaned as every element write's is (<see cref="OWPayload.ReadOnlyFields"/> left out, a null scalar
        /// string sent as <c>""</c>), and goes with the PIN, <c>X-OW-Client</c> and, on an agent client,
        /// <c>X-OW-Agent</c>; keel's document declares no <c>Idempotency-Key</c> or <c>If-Match</c> here, so none is
        /// sent. An agent seat's PUT on an existing element, in a world that takes its writes as proposals, answers 202
        /// and throws an <see cref="OWProposedError"/>, as on <see cref="PatchAsync{T}"/>: the element did not change.
        /// Mirrors the TypeScript SDK's <c>upsert</c> (3.0.0; the 202 from 4.58.0).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="element"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException">
        /// The element is a known-of stub (<see cref="OWPayload.IsKnownOf"/>), whose PUT would blank every field it does
        /// not keep; nothing was sent.
        /// </exception>
        /// <exception cref="OWProposedError">The write became a proposal (202); nothing was written to the element.</exception>
        public Task<T> UpsertAsync<T>(string type, string id, object element, CancellationToken ct = default)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var raw = JObject.FromObject(element, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));
            RefuseKnownOf(raw, "upsert", nameof(element));
            return RequestAsync<T>("PUT", $"/{type}/{id}/", body: Sanitize(raw, type), write: true, ct: ct, proposable: true);
        }

        /// <summary>
        /// PATCH /{type}/{id}/ -- partial update.
        /// </summary>
        /// <remarks>
        /// DESTRUCTIVE ON SENT FIELDS. Whatever is in the payload replaces what the server holds,
        /// so send only what changed. Never PATCH a link array to add one item -- that replaces the
        /// whole array; use <see cref="EditLinksAsync"/>.
        /// <para>
        /// In a world that takes an agent seat's writes as proposals (the world's
        /// <c>agent_writes_proposed</c>), a seat's patch answers 202 with the proposal, never an element: this
        /// throws an <see cref="OWProposedError"/> and the element did not change. If-Match is checked first:
        /// a stale one is still a 412.
        /// </para>
        /// </remarks>
        /// <exception cref="OWProposedError">The patch became a proposal (202); nothing was written to the element.</exception>
        /// <param name="ifMatch">
        /// The element's <c>change_seq</c> as you read it, sent as <c>If-Match</c>. The update lands only if
        /// nobody changed the element since; otherwise nothing is written and this throws an <see cref="OWApiError"/>
        /// with <see cref="OWApiError.IsPreconditionFailed"/>, whose <see cref="OWApiError.Current"/> is the element as it
        /// is now. Leave it null for an unconditional write. Last, so a caller passing <paramref name="ct"/> by
        /// position keeps working.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The partial is a known-of stub (<see cref="OWPayload.IsKnownOf"/>); nothing was sent.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ifMatch"/> is negative; nothing was sent.</exception>
        public Task<T> PatchAsync<T>(string type, string id, object partial, CancellationToken ct = default, long? ifMatch = null)
        {
            RefuseNegative(ifMatch);
            var raw = JObject.FromObject(partial, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));
            RefuseKnownOf(raw, "patch", nameof(partial));
            return RequestAsync<T>("PATCH", $"/{type}/{id}/", body: Sanitize(raw, type), write: true, ct: ct, ifMatch: ifMatch, proposable: true);
        }

        /// <summary>DELETE /{type}/{id}/</summary>
        /// <param name="ifMatch">
        /// The element's <c>change_seq</c> as you read it, sent as <c>If-Match</c>. The delete lands only if
        /// nothing changed since; otherwise an <see cref="OWApiError"/> with <see cref="OWApiError.IsPreconditionFailed"/>
        /// whose <see cref="OWApiError.Current"/> is the element as it is now, and nothing is deleted. On an element
        /// already gone it is the idempotent 204. Last, so a caller passing <paramref name="ct"/> by position keeps working.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ifMatch"/> is negative; nothing was sent.</exception>
        public Task DeleteAsync(string type, string id, CancellationToken ct = default, long? ifMatch = null)
        {
            RefuseNegative(ifMatch);
            return RequestAsync<object>("DELETE", $"/{type}/{id}/", allowEmpty: true, write: true, ct: ct, ifMatch: ifMatch);
        }

        /// <summary>An <c>If-Match</c> is an element's <c>change_seq</c>, a whole number; anything else is refused before sending.</summary>
        private static void RefuseNegative(long? ifMatch)
        {
            if (ifMatch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ifMatch), ifMatch,
                    "ifMatch must be a whole number, an element's change_seq.");
            }
        }

        /// <summary>
        /// POST /bulk/ -- writes many elements of mixed types in one request.
        /// </summary>
        /// <param name="items">Elements to write, in the order the result slots will come back.</param>
        /// <param name="atomic">
        /// <c>false</c> (the default, and the server's) means each slot succeeds or fails on its
        /// own. <c>true</c> asks the server to apply all or nothing; when anything failed, nothing
        /// was written, though the other slots still say 201 (see <see cref="OWBulkSlot.Succeeded"/>).
        /// </param>
        /// <param name="idempotencyKey">
        /// Replaying the same key returns the original outcome rather than repeating the writes,
        /// and the result's <see cref="OWBulkResult.WasReplay"/> says which happened.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>A bulk request returns HTTP 200 even when slots failed.</b> The per-slot status is the
        /// only place the truth lives, which is why this returns a type whose failures are hard to
        /// ignore rather than a bare list. Check <see cref="OWBulkResult.Errors"/>, or call
        /// <see cref="OWBulkResult.ThrowIfAnyFailed"/> if partial success is not something the
        /// caller can act on.
        /// </para>
        /// <para>
        /// Each element is sanitized exactly as a single write is -- the six read-only fields
        /// are stripped from every item, and a null scalar string is sent as <c>""</c> -- and each
        /// gets a client-minted UUID when it has none. Under a key that id is derived from the key and
        /// the item's position (<see cref="OWId.Keyed"/>), so a retry sends the same body and replays,
        /// as the Python package's bulk does. Without a key each call mints fresh ids, so an unkeyed
        /// retry of id-less items writes them twice: a caller who retries passes a key.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// An item is a known-of stub (<see cref="OWPayload.IsKnownOf"/>), named by its index; nothing was sent.
        /// </exception>
        public async Task<OWBulkResult> BulkAsync(
            IEnumerable<OWBulkItem> items, bool atomic = false, string idempotencyKey = null,
            CancellationToken ct = default)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            var array = new JArray();
            var count = 0;

            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("A bulk item cannot be null.", nameof(items));
                if (string.IsNullOrEmpty(item.Type))
                {
                    throw new ArgumentException("Every bulk item needs a type.", nameof(items));
                }

                RefuseKnownOf(item.Element, $"bulk: items[{count}]", nameof(items));
                var element = Sanitize(item.Element != null ? (JObject)item.Element.DeepClone() : new JObject(), item.Type);

                if (element["id"] == null || string.IsNullOrEmpty(element["id"].ToString()))
                {
                    element["id"] = OWId.Mint(idempotencyKey, count);
                }

                array.Add(new JObject { ["type"] = item.Type, ["element"] = element });
                count++;
            }

            if (count == 0)
            {
                throw new ArgumentException("A bulk request needs at least one item.", nameof(items));
            }

            var body = new JObject { ["items"] = array, ["atomic"] = atomic };

            var (result, response) = await RequestWithResponseAsync<OWBulkResult>(
                "POST", "/bulk/", body: body, idempotencyKey: idempotencyKey, write: true, ct: ct)
                .ConfigureAwait(false);

            if (result == null) return null;

            result.WasReplay = ReadReplayHeader(response);
            return result;
        }

        /// <summary>
        /// Reads the replay flag, which arrives as <c>idempotent-replay</c> -- lowercase on the
        /// wire. Compared case-insensitively so a transport that preserves casing works too.
        /// </summary>
        private static bool ReadReplayHeader(OWResponse response)
            => string.Equals(ReadHeader(response, "idempotent-replay"), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// POST /{type}/{id}/links/{field} -- atomic add/remove on a relationship, server-side.
        /// </summary>
        /// <remarks>
        /// The ONLY correct way to modify a link array. A PATCH carrying a link field replaces it
        /// wholesale, so two clients each adding one item means the second silently erases the
        /// first's addition. A 202 (an agent seat's link edit that became a proposal) is thrown as an
        /// <see cref="OWProposedError"/>, as on <see cref="PatchAsync{T}"/>.
        /// </remarks>
        /// <param name="ifMatch">
        /// As on <see cref="PatchAsync{T}"/>: a link write moves the element too, so a stale
        /// <c>change_seq</c> is a 412 and nothing is written.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ifMatch"/> is negative; nothing was sent.</exception>
        public Task<T> EditLinksAsync<T>(
            string type, string id, string field,
            IEnumerable<string> add = null, IEnumerable<string> remove = null,
            CancellationToken ct = default, long? ifMatch = null)
        {
            RefuseNegative(ifMatch);
            var body = new JObject();
            if (add != null) body["add"] = new JArray(add);
            if (remove != null) body["remove"] = new JArray(remove);

            return RequestAsync<T>("POST", $"/{type}/{id}/links/{Uri.EscapeDataString(field)}",
                body: body, write: true, ct: ct, ifMatch: ifMatch, proposable: true);
        }

        /// <summary>
        /// POST /{type}/{id}/adopt -- change who made an element: its <c>created_by</c> becomes
        /// <paramref name="member"/>. Returns the element as written.
        /// </summary>
        /// <param name="member">
        /// A membership id of this world (a person or a seat, as <see cref="GetMembersAsync"/> lists them), or
        /// <c>owner</c>, which sets <c>created_by</c> to null.
        /// </param>
        /// <remarks>
        /// The world's owner key or a co-builder's (a person's or a seat's), with its PIN; a contributor's is a 403
        /// <c>not_author</c>, a member the world lacks a 404. Nothing else in the element changes, but the write stamps
        /// <c>changed_by</c> and <c>changed_via</c> and takes a new <c>change_seq</c>, so the feed carries the element with
        /// its new maker. A contributor changes only what it created, so this hands it an element someone else made, or
        /// takes one back. An element write: <c>X-OW-Client</c> goes with it, and <c>X-OW-Agent</c> on an agent client.
        /// Mirrors the TypeScript SDK's <c>adopt</c> (4.43.0).
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="member"/> is null or empty; nothing was sent.</exception>
        public Task<T> AdoptAsync<T>(string type, string id, string member, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(member))
            {
                throw new ArgumentException(
                    "adopt: member must be a membership id or 'owner'. Nothing was sent.", nameof(member));
            }

            return RequestAsync<T>("POST", $"/{type}/{id}/adopt", body: new JObject { ["member"] = member },
                write: true, ct: ct);
        }

        // -- The hidden mark -----------------------------------------------------
        //
        // A hidden element is seen only by the world's owner, its co-builders and the member who created it; to every
        // other key it is a 404, left out of lists, search, counts and the link arrays that name it
        // (OWPayload.IsHidden reads the mark on a body). keel's document declares no X-OW-Agent or X-OW-Client on these
        // routes, so neither is sent. Mirrors the TypeScript SDK's hide, unhide (4.45.0) and listHidden (4.48.0).

        /// <summary>
        /// PUT /{type}/{id}/hidden -- hide an element. Hiding a hidden element again changes nothing, and the element's
        /// body and <c>change_seq</c> do not move. Answers 204, no body.
        /// </summary>
        /// <remarks>
        /// The owner's key or a co-builder's (a person's or a seat's), with its PIN. The body is <c>{}</c>; no
        /// <c>Idempotency-Key</c>. The readers who still see the element get an upsert row on <c>/changes</c> carrying
        /// <see cref="OWChange.ChangedAt"/>, <see cref="OWChange.ChangedBy"/> and <see cref="OWChange.ChangedVia"/>; the
        /// others lose it from their copies as a delete.
        /// </remarks>
        public Task HideAsync(string type, string id, CancellationToken ct = default)
            => RequestAsync<object>("PUT", $"/{type}/{id}/hidden", body: new JObject(), allowEmpty: true, ct: ct);

        /// <summary>
        /// DELETE /{type}/{id}/hidden -- clear the mark; an element that is not hidden stays as it is. Answers 204. The
        /// same keys as <see cref="HideAsync"/>, with the PIN.
        /// </summary>
        public Task UnhideAsync(string type, string id, CancellationToken ct = default)
            => RequestAsync<object>("DELETE", $"/{type}/{id}/hidden", allowEmpty: true, ct: ct);

        /// <summary>
        /// GET /world/hidden -- every hidden element of the world, newest first, the rows of keel's <c>{data}</c> (no
        /// paging).
        /// </summary>
        /// <remarks>
        /// Any key of the owner's or a co-builder's, a read key included (a read: no PIN); any other key is a 403. Served
        /// <c>Cache-Control: no-store</c>: read it each time, keep no copy.
        /// </remarks>
        public async Task<List<OWHiddenRow>> ListHiddenAsync(CancellationToken ct = default)
        {
            var page = await RequestAsync<JObject>("GET", "/world/hidden", ct: ct).ConfigureAwait(false);
            return (page?["data"] as JArray)?.ToObject<List<OWHiddenRow>>(Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings))
                ?? new List<OWHiddenRow>();
        }

        // -- The draught -----------------------------------------------------------

        /// <summary>
        /// GET /character/{id}/context -- the draught: what one Character knows, as the recorded world holds it, in one
        /// read. Her element, what she holds and sits in, her open work, how she works, the Events she was in, the
        /// Narratives she knows, everything else that names her, her sheet, what changed around her
        /// (<see cref="OWDraughtPolicy.Since"/>) and who plays whom; the world's name and calendar beside; the room's
        /// messages, its other elements and elements written as proposed only when the policy's components name them. The
        /// answer is the packet as served.
        /// </summary>
        /// <param name="characterId">The Character's id. Escaped.</param>
        /// <param name="policy">What to serve and how; the default sends no query, and keel's defaults hold.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="ArgumentException">
        /// No Character's id, or a component that is null, empty or holds a comma; nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">
        /// The server refused: a Character this key may not read is a 404 (a player seat or a guest reads its own
        /// Character only); <see cref="OWDraughtPolicy.Lens"/> beside a depth or a budget is a 422.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Each row carries the list route's body for the same reader as <c>element</c>, beside <c>via</c>,
        /// <c>her_links</c> and <c>grade</c>; nothing is inferred. A <c>cut</c> that is not empty means the serving is
        /// partial. A Message without the room mark is placed as any Narrative unless <c>messages</c> is named, when it is
        /// there and in no other section; a room-marked Message is in <c>messages</c> or nowhere. With no policy key but
        /// <see cref="OWDraughtPolicy.Compact"/>, the packet adds one frame per lens chosen with
        /// <see cref="SetMyLensesAsync"/>.
        /// </para>
        /// <para>
        /// The policy's keys go in the TypeScript SDK's order (<c>components</c>, <c>mode</c>, <c>as_of</c>,
        /// <c>undated</c>, <c>since</c>, <c>budget</c>, <c>depth</c>, <c>compact</c>, <c>lens</c>). A read: the key goes
        /// without its PIN (a legacy key's goes with it), and with no write header. A world read: with
        /// <see cref="World"/> set, an account token reads the draught as the account's own access there (the Bearer and
        /// <c>world=&lt;id&gt;</c>), and no key reads it as the public reads the world (<c>world=&lt;id&gt;</c> alone).
        /// Served <c>Cache-Control: private, no-store</c>. Mirrors the TypeScript SDK's <c>draught</c> (4.47.0;
        /// <c>compact</c> from 4.69.0, <c>lens</c> from 4.92.0, a world read from 4.121.1).
        /// </para>
        /// </remarks>
        public Task<JObject> DraughtAsync(string characterId, OWDraughtPolicy policy = default, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(characterId))
                throw new ArgumentException("DraughtAsync: the Character's id is required. Nothing was sent.", nameof(characterId));

            if (policy.Components != null)
            {
                foreach (var component in policy.Components)
                {
                    if (string.IsNullOrEmpty(component) || component.Contains(","))
                    {
                        throw new ArgumentException(
                            "DraughtAsync: each component is one section's name, not empty and without a comma. Nothing "
                            + "was sent.", nameof(policy));
                    }
                }
            }

            var keys = new List<(string Name, string Value)>
            {
                ("components", Joined(policy.Components)),
                ("mode", NullIfEmpty(policy.Mode)),
                ("as_of", policy.AsOf?.ToString(CultureInfo.InvariantCulture)),
                ("undated", NullIfEmpty(policy.Undated)),
                ("since", NullIfEmpty(policy.Since)),
                ("budget", policy.Budget?.ToString(CultureInfo.InvariantCulture)),
                ("depth", policy.Depth?.ToString(CultureInfo.InvariantCulture)),
                ("compact", policy.Compact.HasValue ? (policy.Compact.Value ? "true" : "false") : null),
                ("lens", NullIfEmpty(policy.Lens)),
            };

            var query = new List<string>();
            foreach (var (name, value) in keys)
            {
                if (value != null) query.Add(name + "=" + Uri.EscapeDataString(value));
            }

            return RequestAsync<JObject>("GET", $"/character/{Uri.EscapeDataString(characterId)}/context",
                query: string.Join("&", query), ct: ct, worldRead: true);
        }

        // -- Images --------------------------------------------------------------
        //
        // Two steps: a ticket from keel, then one POST to the upload host the ticket names, carrying the ticket alone as its
        // Bearer. The key, the PIN and this client's write headers never leave for that host, and keel never sees the
        // bytes. keel's document names the host only in the tickets' own words (send the ticket to upload_url, to
        // remove_url as Authorization: Bearer); the host's answers are typed from the TypeScript SDK. Mirrors the TypeScript
        // SDK's createMediaTicket, uploadImage (4.6.0), createRemovalTicket and removeImage (4.8.0).

        /// <summary>
        /// POST /media/ticket -- permission to upload one image into this world: short-lived
        /// (<see cref="OWMediaTicket.Exp"/>), single-use, and counted toward the world's daily limit.
        /// <see cref="UploadImageAsync"/> asks for one itself; ask here to send the bytes yourself (for a progress bar,
        /// say): POST them to <see cref="OWMediaTicket.UploadUrl"/> with <c>Authorization: Bearer &lt;ticket&gt;</c>.
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="OWApiError">
        /// keel refused: 403 <c>storage_full</c> (the account's image storage is used up), 429 <c>quota_exceeded</c> (the
        /// world's daily limit, with <see cref="OWApiError.RetryAfter"/>) or <c>rate_limited</c> (wrong PINs), 503
        /// <c>media_unavailable</c>.
        /// </exception>
        /// <remarks>
        /// A write key with its PIN, and no body. No <c>X-OW-Agent</c> or <c>X-OW-Client</c>, which keel's document does
        /// not list here. An account token or a keyless client is refused, and nothing is sent. The ticket is a credential:
        /// keep it out of logs. Mirrors the TypeScript SDK's <c>createMediaTicket</c> (4.6.0).
        /// </remarks>
        public Task<OWMediaTicket> CreateMediaTicketAsync(CancellationToken ct = default)
            => RequestAsync<OWMediaTicket>("POST", "/media/ticket", ct: ct);

        /// <summary>
        /// Upload one image and get its permanent public URL: a ticket from keel (<see cref="CreateMediaTicketAsync"/>),
        /// then the bytes straight to the upload host. Answers the host's 201; set its <see cref="OWUploadedImage.Url"/> as
        /// an element's <c>image_url</c> yourself (<see cref="PatchAsync{T}"/>).
        /// </summary>
        /// <param name="image">The image's bytes: webp, png, jpeg or avif, read from the bytes (never SVG or gif).</param>
        /// <param name="key">
        /// The object key, sent as <c>X-Key</c>; it must start with the ticket's <see cref="OWMediaTicket.Prefix"/>. Null
        /// or empty: the host picks one.
        /// </param>
        /// <param name="ticket">A ticket from <see cref="CreateMediaTicketAsync"/> to use instead of asking keel for one.</param>
        /// <param name="ct">Cancels the requests.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException">
        /// The ticket has no ticket, or no absolute http(s) <c>upload_url</c>; nothing was sent to the host.
        /// </exception>
        /// <exception cref="OWApiError">
        /// An image larger than the ticket's <see cref="OWMediaTicket.MaxBytes"/> is a 413 <c>too_large</c> before any
        /// byte is sent. keel's refusals are <see cref="CreateMediaTicketAsync"/>'s; the host's arrive with its code: 401
        /// <c>ticket_expired</c>, <c>ticket_used</c> or <c>ticket_invalid</c>, 400 <c>bad_key</c>, 409 <c>exists</c>, 413
        /// <c>too_large</c>, 415 <c>unsupported_type</c>, 502 <c>write_failed</c> (nothing stored; the ticket is still
        /// unspent). A ticket is spent once the upload succeeds: a retry needs a new one.
        /// </exception>
        /// <exception cref="OWTransportError">The host did not answer; the message names its host, never the ticket.</exception>
        /// <remarks>
        /// The host's request is a POST of the raw bytes with <c>Authorization: Bearer &lt;ticket&gt;</c> and <c>X-Key</c>
        /// when given, and nothing else of this client's: no key, PIN or write header, and no Content-Type (the host reads
        /// the type from the bytes). Mirrors the TypeScript SDK's <c>uploadImage</c> (4.6.0).
        /// </remarks>
        public async Task<OWUploadedImage> UploadImageAsync(
            byte[] image, string key = null, OWMediaTicket ticket = null, CancellationToken ct = default)
        {
            if (image == null) throw new ArgumentNullException(nameof(image), "UploadImageAsync: the image's bytes. Nothing was sent.");

            ticket = ticket ?? await CreateMediaTicketAsync(ct).ConfigureAwait(false);
            RequireHost(nameof(UploadImageAsync), ticket?.Ticket, ticket?.UploadUrl, "upload_url");
            RefuseOverMax("Image", image.Length, ticket.MaxBytes);

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(key)) headers["X-Key"] = key;

            return await SendToHostAsync<OWUploadedImage>("image upload", ticket.UploadUrl, ticket.Ticket, image, headers, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// POST /media/remove-ticket -- permission to remove one uploaded image. The image's uploader may remove it, and so
        /// may the world's owner key; an agent seat answers as its sponsor. Only images uploaded with a ticket qualify.
        /// <see cref="OWRemovalTicket.Referenced"/> counts the world's elements still showing the image.
        /// </summary>
        /// <param name="keyOrUrl">The object key, or the whole <c>image_url</c> (its path after the host is taken, unescaped).</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="ArgumentException"><paramref name="keyOrUrl"/> is null or empty; nothing was sent.</exception>
        /// <exception cref="OWApiError">
        /// keel refused: 403 <c>permission_error</c> (neither uploader nor owner), 404 <c>not_found</c> (no removable image
        /// under this key), 409 <c>removal_in_flight</c> (a ticket for this image is already out) or <c>upload_pending</c>
        /// (an upload is not yet recorded), both with <see cref="OWApiError.RetryAfter"/>.
        /// </exception>
        /// <remarks>
        /// A write key with its PIN; the body is <c>{"key"}</c>. No <c>X-OW-Agent</c> or <c>X-OW-Client</c>, which keel's
        /// document does not list here. Mirrors the TypeScript SDK's <c>createRemovalTicket</c> (4.8.0).
        /// </remarks>
        public Task<OWRemovalTicket> CreateRemovalTicketAsync(string keyOrUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(keyOrUrl))
            {
                throw new ArgumentException(
                    "CreateRemovalTicketAsync: the image's object key or its image_url is required. Nothing was sent.",
                    nameof(keyOrUrl));
            }

            return RequestAsync<OWRemovalTicket>("POST", "/media/remove-ticket",
                body: new JObject { ["key"] = ObjectKey(keyOrUrl) }, ct: ct);
        }

        /// <summary>
        /// Remove one uploaded image: a removal ticket from keel (<see cref="CreateRemovalTicketAsync"/>), then the removal
        /// at the upload host. The bytes go back to the uploading account's storage. Answers the host's 200.
        /// </summary>
        /// <param name="keyOrUrl">The object key, or the whole <c>image_url</c>. Not read when a ticket is given.</param>
        /// <param name="ticket">A ticket from <see cref="CreateRemovalTicketAsync"/> to use instead of asking keel for one.</param>
        /// <param name="ct">Cancels the requests.</param>
        /// <exception cref="ArgumentException">
        /// No ticket and no key; or the ticket has no ticket, or no absolute http(s) <c>remove_url</c>. Nothing was sent to
        /// the host.
        /// </exception>
        /// <exception cref="OWApiError">
        /// keel's refusals are <see cref="CreateRemovalTicketAsync"/>'s; the host's arrive with its code: 401
        /// <c>ticket_expired</c>, <c>ticket_used</c> or <c>ticket_invalid</c>, 403 <c>not_removable</c>, 502
        /// <c>remove_failed</c> (nothing removed; the ticket is still unspent).
        /// </exception>
        /// <exception cref="OWTransportError">The host did not answer; the message names its host, never the ticket.</exception>
        /// <remarks>
        /// Removal does not touch elements: clear or replace their <c>image_url</c> yourself. A copy already cached by the
        /// network or a browser can keep answering for a while (<see cref="OWRemovedImage.Cache"/> <c>may_linger</c>). The
        /// host's request is a POST with <c>Authorization: Bearer &lt;ticket&gt;</c> alone and no body. Mirrors the
        /// TypeScript SDK's <c>removeImage</c> (4.8.0).
        /// </remarks>
        public async Task<OWRemovedImage> RemoveImageAsync(string keyOrUrl, OWRemovalTicket ticket = null, CancellationToken ct = default)
        {
            ticket = ticket ?? await CreateRemovalTicketAsync(keyOrUrl, ct).ConfigureAwait(false);
            RequireHost(nameof(RemoveImageAsync), ticket?.Ticket, ticket?.RemoveUrl, "remove_url");

            return await SendToHostAsync<OWRemovedImage>("image removal", ticket.RemoveUrl, ticket.Ticket, null, null, ct)
                .ConfigureAwait(false);
        }

        /// <summary>An image's object key: as given, or the path of a whole image_url without its leading slashes, unescaped.</summary>
        private static string ObjectKey(string keyOrUrl)
        {
            var url = keyOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || keyOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            return url ? Uri.UnescapeDataString(new Uri(keyOrUrl).AbsolutePath.TrimStart('/')) : keyOrUrl;
        }

        /// <summary>
        /// A ticket with somewhere to go: its secret, and an absolute http(s) URL on the upload host. Anything else is an
        /// <see cref="ArgumentException"/>, and nothing goes to the host; the message names neither the ticket nor the URL.
        /// </summary>
        private static void RequireHost(string method, string ticket, string url, string urlKey)
        {
            if (!string.IsNullOrEmpty(ticket) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return;
            }

            throw new ArgumentException(
                $"{method}: the ticket needs its ticket and an absolute http(s) {urlKey}, the upload host's. Nothing was sent "
                + "to the host.", "ticket");
        }

        /// <summary>
        /// Bytes past a ticket's <c>max_bytes</c>: a 413 <c>too_large</c> before any byte is sent, as the host would answer,
        /// with the TypeScript SDK's body.
        /// </summary>
        private static void RefuseOverMax(string what, long size, long max)
        {
            if (size <= max) return;

            var body = new JObject { ["error"] = "too_large", ["max_bytes"] = max, ["got"] = size };
            throw new OWApiError((HttpStatusCode)413,
                $"{what} is {size} bytes; this ticket accepts at most {max}. Nothing was sent.",
                code: "too_large", rawBody: body.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>
        /// The second step of a ticketed call: one POST to the upload host the ticket names, with the ticket as its Bearer
        /// and nothing of this client's (no key, PIN, Bearer token or write header), the bytes when there are any. Not a
        /// keel request, so none of <see cref="RequestWithResponseAsync{T}"/>'s rules apply. The host's refusal is an
        /// <see cref="OWApiError"/> with its code (<see cref="BuildError"/> reads its <c>{"error": "code"}</c>); no answer
        /// is an <see cref="OWTransportError"/> naming the host, never the ticket.
        /// </summary>
        private async Task<T> SendToHostAsync<T>(
            string what, string url, string ticket, byte[] bytes, Dictionary<string, string> headers, CancellationToken ct)
        {
            headers = headers ?? new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + ticket;

            var request = new OWRequest { Method = "POST", Url = url, Headers = headers, BodyBytes = bytes };

            OWResponse response;
            try
            {
                response = await _transport.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new OWTransportError($"OnlyWorlds {what} failed: POST to {new Uri(url).Host} got no answer", e);
            }

            if (!response.IsSuccess) throw BuildError(response);

            return OWJson.Deserialize<T>(response.Body);
        }

        // -- Files on elements -------------------------------------------------------
        //
        // A file is seen by whoever may see its element, and never rides on the element: it is not in its body, its
        // change_seq or /changes. An upload is the image's two steps: a ticket from keel, then the raw bytes to the upload
        // host with the ticket alone. Mirrors the TypeScript SDK's listFiles, worldFiles, createFileTicket, uploadFile,
        // fileDownloadLink and removeFile (4.32.0).

        /// <summary>
        /// GET /{type}/{id}/files -- the element's files, newest first, the rows of keel's <c>{data}</c> (no paging): each
        /// with its name, kind, size in bytes, label (or null), who added it (<see cref="OWFileAddedBy.Agent"/> true when an
        /// agent did) and when.
        /// </summary>
        /// <param name="type">The element's type, as the routes spell it (<c>location</c>).</param>
        /// <param name="id">The element's id. Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="OWApiError">An element this key cannot see is a 404 <c>not_found</c>.</exception>
        /// <remarks>Any key that may read the element; a read, with no PIN. Mirrors the TypeScript SDK's <c>listFiles</c> (4.32.0).</remarks>
        public async Task<List<OWFileRow>> ListFilesAsync(string type, string id, CancellationToken ct = default)
        {
            var page = await RequestAsync<JObject>("GET", FilesPath(type, id), ct: ct).ConfigureAwait(false);
            return RowsOf<OWFileRow>(page);
        }

        /// <summary>
        /// GET /world/files -- every file in the world this key may see, newest first, each with its element's
        /// <see cref="OWWorldFileRow.ElementType"/> and <see cref="OWWorldFileRow.ElementId"/>; and the world's total,
        /// <see cref="OWWorldFiles.UsedBytes"/> of <see cref="OWWorldFiles.CapBytes"/> (for a line like "41 MB of 250 MB").
        /// <c>UsedBytes</c> counts all the world's files, also those on elements this key cannot see.
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>Any key; a read, with no PIN. Mirrors the TypeScript SDK's <c>worldFiles</c> (4.32.0).</remarks>
        public Task<OWWorldFiles> WorldFilesAsync(CancellationToken ct = default)
            => RequestAsync<OWWorldFiles>("GET", "/world/files", ct: ct);

        /// <summary>
        /// POST /{type}/{id}/files -- permission to upload one file onto this element: short-lived
        /// (<see cref="OWFileTicket.Exp"/>, 10 minutes) and single-use. <see cref="UploadFileAsync"/> asks for one itself;
        /// ask here to send the bytes yourself: POST them, raw, to <see cref="OWFileTicket.UploadUrl"/> with
        /// <c>Authorization: Bearer &lt;ticket&gt;</c>.
        /// </summary>
        /// <param name="type">The element's type.</param>
        /// <param name="id">The element's id. Escaped.</param>
        /// <param name="name">The file's name with its extension, sent as given (keel cleans it).</param>
        /// <param name="size">The file's size in bytes.</param>
        /// <param name="kind">
        /// The file's kind, sent as given; null reads it from <paramref name="name"/>'s extension
        /// (<see cref="OWFileKinds.Of"/>).
        /// </param>
        /// <param name="label">
        /// A field of the element's type the file belongs to, or your own words (at most 80 characters); null sends none.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="ArgumentException">
        /// No name, or a name whose extension keel takes no file with and no <paramref name="kind"/>; the message names the
        /// extensions it takes. Nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">
        /// keel refused: 413 <c>file_too_large</c> (<see cref="OWApiError.IsFileTooLarge"/>, over 25 MB, param
        /// <c>size</c>); 403 <c>files_full</c> (<see cref="OWApiError.IsFilesFull"/>, the world's 250 MB has no room for
        /// it) or <c>not_author</c> (a contributor's key on an element someone else made); 422 <c>invalid_request</c>
        /// (param <c>kind</c>, <c>name</c>, <c>size</c> or <c>label</c>); 429 <c>quota_exceeded</c>
        /// (<see cref="OWApiError.IsQuotaExceeded"/>, the world's 200 tickets a day) or <c>rate_limited</c> (wrong PINs),
        /// both with <see cref="OWApiError.RetryAfter"/>; 503 <c>media_unavailable</c>.
        /// </exception>
        /// <remarks>
        /// A write key that may change the element, with its PIN. The body is <c>{name, kind, size}</c> and
        /// <c>label</c> when given. An agent client sends <c>X-OW-Agent</c> here, the one files route keel's document lists
        /// it on, so the file reads <see cref="OWFileAddedBy.Agent"/> true; no <c>X-OW-Client</c>, which keel does not read
        /// here, and no <c>Idempotency-Key</c>, by keel's rule: the single-use ticket is the idempotency, and a second
        /// call is a second ticket. The ticket is a credential: keep it out of logs. Mirrors the TypeScript SDK's
        /// <c>createFileTicket</c> (4.32.0; <c>X-OW-Agent</c> from 4.33.1).
        /// </remarks>
        public Task<OWFileTicket> CreateFileTicketAsync(
            string type, string id, string name, long size, string kind = null, string label = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "CreateFileTicketAsync needs the file's name, with its extension. Nothing was sent.", nameof(name));
            }

            var body = new JObject { ["name"] = name, ["kind"] = kind ?? RequireFileKind(name), ["size"] = size };
            if (label != null) body["label"] = label;

            return RequestAsync<OWFileTicket>("POST", FilesPath(type, id), body: body, agentOnly: true, ct: ct);
        }

        /// <summary>
        /// Attach one file to an element: a ticket from keel (<see cref="CreateFileTicketAsync"/>, with the bytes' length
        /// as its size), then the bytes straight to the upload host. Answers the host's 201: <see cref="OWUploadedFile.Key"/>,
        /// <see cref="OWUploadedFile.Fid"/> (the file's id), the bytes, the type and the ETag. The file appears in
        /// <see cref="ListFilesAsync"/> once the host has reported it to keel, which can be a moment after this returns.
        /// </summary>
        /// <param name="type">The element's type.</param>
        /// <param name="id">The element's id. Escaped.</param>
        /// <param name="file">The file's bytes.</param>
        /// <param name="name">The file's name with its extension; needed unless a ticket is given.</param>
        /// <param name="kind">As <see cref="CreateFileTicketAsync"/>'s.</param>
        /// <param name="label">As <see cref="CreateFileTicketAsync"/>'s.</param>
        /// <param name="ticket">
        /// A ticket from <see cref="CreateFileTicketAsync"/> to use instead of asking keel for one: then no name, kind or
        /// label is read, since the ticket carries them.
        /// </param>
        /// <param name="onProgress">
        /// Called once with (bytes sent, total), both the file's length, when the host has stored it; never on a failure.
        /// The transport reports no progress of its own.
        /// </param>
        /// <param name="ct">Cancels the requests.</param>
        /// <exception cref="ArgumentNullException"><paramref name="file"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException">
        /// No ticket and no name, or a name <see cref="CreateFileTicketAsync"/> refuses; or a ticket with no ticket or no
        /// absolute http(s) <c>upload_url</c>. Nothing was sent to the host.
        /// </exception>
        /// <exception cref="OWApiError">
        /// A file larger than the ticket's <see cref="OWFileTicket.MaxBytes"/> is a 413 <c>too_large</c> before any byte is
        /// sent. keel's refusals are <see cref="CreateFileTicketAsync"/>'s; the host's arrive with its code: 401
        /// <c>ticket_expired</c>, <c>ticket_invalid</c> or <c>ticket_used</c>; 409 <c>exists</c> (an earlier send with this
        /// ticket succeeded, so the file is there); 413 <c>too_large</c>; 415 <c>unsupported_type</c> (the bytes are not the
        /// kind: a pdf must start as one, a text file must be UTF-8 with no NUL in its first 8 KB, a zip must be a world's
        /// own bundle; nothing is stored and the ticket is still unspent); 502 <c>write_failed</c> (nothing stored; the
        /// ticket is still unspent). A ticket is spent once the upload succeeds: another file needs a new one.
        /// </exception>
        /// <exception cref="OWTransportError">The host did not answer; the message names its host, never the ticket.</exception>
        /// <remarks>
        /// The host's request is a POST of the raw bytes with <c>Authorization: Bearer &lt;ticket&gt;</c> alone: no key,
        /// PIN, write header or Content-Type. Mirrors the TypeScript SDK's <c>uploadFile</c> (4.32.0), as it runs without
        /// XMLHttpRequest.
        /// </remarks>
        public async Task<OWUploadedFile> UploadFileAsync(
            string type, string id, byte[] file, string name = null, string kind = null, string label = null,
            OWFileTicket ticket = null, Action<long, long> onProgress = null, CancellationToken ct = default)
        {
            if (file == null) throw new ArgumentNullException(nameof(file), "UploadFileAsync: the file's bytes. Nothing was sent.");

            if (ticket == null)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(
                        "UploadFileAsync needs the file's name, with its extension, or a ticket from CreateFileTicketAsync. "
                        + "Nothing was sent.", nameof(name));
                }

                ticket = await CreateFileTicketAsync(type, id, name, file.Length, kind, label, ct).ConfigureAwait(false);
            }

            RequireHost(nameof(UploadFileAsync), ticket?.Ticket, ticket?.UploadUrl, "upload_url");
            RefuseOverMax("File", file.Length, ticket.MaxBytes);

            var stored = await SendToHostAsync<OWUploadedFile>("file upload", ticket.UploadUrl, ticket.Ticket, file, null, ct)
                .ConfigureAwait(false);
            onProgress?.Invoke(file.Length, file.Length);
            return stored;
        }

        /// <summary>
        /// GET /{type}/{id}/files/{file_id}/download -- a link to the file and when it stops working:
        /// <see cref="OWFileDownload.Url"/> (the file as an attachment, from the download host),
        /// <see cref="OWFileDownload.Exp"/> (unix seconds, 5 minutes after issue), and the file's name, kind and size.
        /// </summary>
        /// <param name="type">The element's type.</param>
        /// <param name="id">The element's id. Escaped.</param>
        /// <param name="fileId">The file's id (<see cref="OWFileRow.Id"/>). Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// The link is the credential: anyone holding it can fetch the file until <c>Exp</c>, more than once. So ask for it
        /// when the file is opened, use it at once, and never log, store or share it. Any key that may read the element;
        /// a read, with no PIN. Mirrors the TypeScript SDK's <c>fileDownloadLink</c> (4.32.0).
        /// </remarks>
        public Task<OWFileDownload> FileDownloadLinkAsync(string type, string id, string fileId, CancellationToken ct = default)
            => RequestAsync<OWFileDownload>("GET", $"{FilesPath(type, id)}/{Uri.EscapeDataString(fileId)}/download", ct: ct);

        /// <summary>
        /// DELETE /{type}/{id}/files/{file_id} -- remove a file for real: it leaves the lists and the world's total at once,
        /// and its bytes are deleted from the store. Answers 204. Deleting an element removes its files too.
        /// </summary>
        /// <param name="type">The element's type.</param>
        /// <param name="id">The element's id. Escaped.</param>
        /// <param name="fileId">The file's id (<see cref="OWFileRow.Id"/>). Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="OWApiError">
        /// Any key but the world's owner's and that of whoever added it (an agent seat answers as its sponsor) is a 403
        /// <c>permission_error</c>; a file that is not there a 404 <c>not_found</c>.
        /// </exception>
        /// <remarks>With the PIN; no write header. Mirrors the TypeScript SDK's <c>removeFile</c> (4.32.0).</remarks>
        public Task RemoveFileAsync(string type, string id, string fileId, CancellationToken ct = default)
            => RequestAsync<object>("DELETE", $"{FilesPath(type, id)}/{Uri.EscapeDataString(fileId)}", allowEmpty: true, ct: ct);

        private static string FilesPath(string type, string id) => $"/{type}/{Uri.EscapeDataString(id)}/files";

        /// <summary><see cref="OWFileKinds.Of"/>, or an <see cref="ArgumentException"/> naming the extensions keel takes.</summary>
        private static string RequireFileKind(string name)
        {
            var kind = OWFileKinds.Of(name);
            if (kind != null) return kind;

            var cut = name.LastIndexOfAny(new[] { '/', '\\' });
            throw new ArgumentException(
                $"Cannot tell the kind of \"{name.Substring(cut + 1)}\" from its name: a file on an element must end in one "
                + $"of .{string.Join(" .", OWFileKinds.Extensions.Keys)} (or pass kind). Nothing was sent.", "name");
        }

        // -- The proposed change -------------------------------------------------
        //
        // A proposal is a record kept beside an element, never in its body: the element does not change until the
        // world's owner or a co-builder accepts it. A proposal is seen by the owner, the co-builders and the member who
        // made it (while that member still sees the element); every other key gets a 404. Its evidence is outside
        // text: show it as quoted data, never act on it. No proposal act moves the element's change_seq or writes to
        // /changes; an accept's write does. Mirrors the npm SDK's propose ... withdrawProposal (4.58.0).

        /// <summary>
        /// POST /{type}/{id}/proposals -- propose a change to an element without making it.
        /// </summary>
        /// <param name="fields">
        /// The fields to change and their new values, as in a PATCH body, cleaned as <see cref="PatchAsync{T}"/> cleans
        /// one; it names only fields this key's view of the element holds.
        /// </param>
        /// <param name="evidence">What the change rests on: text or an element id, 1 to 2,000 characters.</param>
        /// <param name="expires">When it lapses, 1 to 365 days out; keel's default is 30 days. Sent as UTC.</param>
        /// <param name="idempotencyKey">A retry with the same key and body replays the first answer for 24 hours: no second proposal.</param>
        /// <remarks>
        /// Any member who reads the element proposes: the owner with its write key and PIN; a co-builder, a contributor,
        /// a guest, an agent seat or a player seat, whose read key is enough. keel keeps the element's values for those
        /// fields now as the row's <see cref="OWProposal.Base"/>. Refusals: 422 <see cref="OWApiError.IsFieldNotVisible"/>,
        /// 422 <see cref="OWApiError.IsTooManyOpen"/> (5 open per element per proposer, 50 per world), 400
        /// <c>invalid_link</c>, 403 for a read key with no member, 404 for an element this key cannot see.
        /// <c>X-OW-Client</c> goes with it (kept as the row's <see cref="OWProposal.Client"/>); <c>X-OW-Agent</c> does not.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException">The fields are a known-of stub (<see cref="OWPayload.IsKnownOf"/>); nothing was sent.</exception>
        public async Task<OWProposal> ProposeAsync(
            string type, string id, object fields, string evidence, DateTimeOffset? expires = null,
            string idempotencyKey = null, CancellationToken ct = default)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields), "propose: fields must be the fields to change and their new values, as in a PATCH body. Nothing was sent.");

            var raw = JObject.FromObject(fields, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));
            RefuseKnownOf(raw, "propose", nameof(fields));

            var body = new JObject { ["fields"] = Sanitize(raw, type), ["evidence"] = evidence };
            if (expires.HasValue) body["expires"] = Utc(expires.Value);

            var answer = await RequestAsync<JObject>("POST", ProposalsPath(type, id), body: body,
                idempotencyKey: idempotencyKey, named: true, ct: ct).ConfigureAwait(false);
            return ProposalOf(answer);
        }

        /// <summary>
        /// GET /{type}/{id}/proposals -- one element's proposals, newest first: every row for the owner's and a
        /// co-builder's keys, a member's own for any other. Any member key that reads the element, a read key included.
        /// </summary>
        /// <exception cref="ArgumentException">The query names <see cref="OWProposalQuery.Element"/>, the world queue's alone; nothing was sent.</exception>
        public Task<OWPage<OWProposal>> ListProposalsAsync(
            string type, string id, OWProposalQuery query = default, CancellationToken ct = default)
        {
            if (query.Element != null)
            {
                throw new ArgumentException(
                    "listProposals: Element filters the world's queue (WorldProposalsAsync); one element's list is already one element's. Nothing was sent.",
                    nameof(query));
            }

            return RequestAsync<OWPage<OWProposal>>("GET", ProposalsPath(type, id), query: ProposalQuery(query), ct: ct);
        }

        /// <summary>
        /// GET /world/proposals -- the world's queue, newest first by <c>created_at</c>: every row for the owner's and a
        /// co-builder's keys, a member's own for any other; <see cref="OWProposalQuery.Element"/> keeps one element's rows.
        /// Any member key, a read key included; a read key with no member (a demo key, the owner's read key) gets a 404.
        /// </summary>
        /// <remarks>
        /// Two kinds of row in one order, told apart by <see cref="OWProposal.Kind"/>: a proposed change to an
        /// element, and a new element written as proposed, one carrying <c>x_review_proposed</c>
        /// (<see cref="OWProposal.IsNewElement"/>; listed under <c>open</c>, <c>lapsed</c> and <c>all</c>, never
        /// <c>accepted</c> or <c>declined</c>; settled with <see cref="ReviewNewElementAsync"/>). keel reads at most the
        /// 500 newest marked elements. The page's <c>next_cursor</c> is opaque: pass it back as it came.
        /// </remarks>
        public Task<OWPage<OWProposal>> WorldProposalsAsync(OWProposalQuery query = default, CancellationToken ct = default)
            => RequestAsync<OWPage<OWProposal>>("GET", "/world/proposals", query: ProposalQuery(query), ct: ct);

        /// <summary>
        /// POST /{type}/{id}/proposals/{pid}/accept -- write the proposal's fields to the element as one ordinary write
        /// by this key (<c>changed_by</c> is the keeper, a new <c>change_seq</c>, a <c>/changes</c> row); the row turns
        /// accepted. Returns the row and the element as written.
        /// </summary>
        /// <remarks>
        /// The world's owner key or a co-builder's, with its PIN; never the proposer's own (403
        /// <see cref="OWApiError.IsOwnProposal"/>), never an agent seat's while the world holds them (403
        /// <see cref="OWApiError.IsProposedOnly"/>). Refused with nothing written when a proposed field moved since (409
        /// <see cref="OWApiError.IsStaleProposal"/>, with <see cref="OWApiError.Conflicts"/> and
        /// <see cref="OWApiError.Current"/>; the row stays open) or a proposed link names an element deleted since (400
        /// <c>invalid_link</c>). A lapsed proposal may still be accepted; a decided one is a 422.
        /// </remarks>
        public Task<OWProposalAccepted> AcceptProposalAsync(string type, string id, string pid, CancellationToken ct = default)
            => RequestAsync<OWProposalAccepted>("POST", $"{ProposalsPath(type, id)}/{Uri.EscapeDataString(pid)}/accept",
                named: true, ct: ct);

        /// <summary>
        /// POST /{type}/{id}/proposals/{pid}/decline -- the row turns declined, with an optional
        /// <paramref name="reason"/> (at most 2,000 characters); the element does not change. The same keys and refusals
        /// as <see cref="AcceptProposalAsync"/>, but no stale check. Returns the row.
        /// </summary>
        public async Task<OWProposal> DeclineProposalAsync(
            string type, string id, string pid, string reason = null, CancellationToken ct = default)
        {
            var body = reason != null ? new JObject { ["reason"] = reason } : null;
            var answer = await RequestAsync<JObject>("POST", $"{ProposalsPath(type, id)}/{Uri.EscapeDataString(pid)}/decline",
                body: body, ct: ct).ConfigureAwait(false);
            return ProposalOf(answer);
        }

        /// <summary>
        /// DELETE /{type}/{id}/proposals/{pid} -- the proposer takes back its own undecided proposal, with any of its
        /// keys: the row goes (204). A keeper's key on another member's row is a 403; any other key a 404; a decided row
        /// a 422.
        /// </summary>
        public Task WithdrawProposalAsync(string type, string id, string pid, CancellationToken ct = default)
            => RequestAsync<object>("DELETE", $"{ProposalsPath(type, id)}/{Uri.EscapeDataString(pid)}", allowEmpty: true, ct: ct);

        /// <summary>
        /// POST /{type}/{id}/review -- settle a new element written as proposed, one carrying <c>x_review_proposed</c>
        /// (the queue's <see cref="OWProposal.IsNewElement"/> row). <see cref="OWReviewed.Accept"/> clears the
        /// mark (a write by this key: a new <c>change_seq</c>, one <c>/changes</c> row) and answers
        /// <see cref="OWReviewed.Element"/>; <see cref="OWReviewed.Decline"/> deletes the element (a tombstone, the links
        /// to it scrubbed, its open proposals dropped) and answers <see cref="OWReviewed.Deleted"/>, its id.
        /// </summary>
        /// <param name="decision"><c>accept</c> or <c>decline</c>.</param>
        /// <param name="reason">With a decline only (at most 2,000 characters): answered back and kept nowhere.</param>
        /// <remarks>
        /// The owner's key or a co-builder's, with its PIN. Refusals: 422 <see cref="OWApiError.IsNotProposed"/> (the
        /// element carries no mark: never marked, or an accept already landed), 403 <see cref="OWApiError.IsProposedOnly"/>
        /// (the member the mark names, or an agent seat while the world holds its writes), 403 for any key but a keeper's.
        /// <c>X-OW-Client</c> goes with it; <c>X-OW-Agent</c> does not. A proposed change to an existing element is decided
        /// with <see cref="AcceptProposalAsync"/> or <see cref="DeclineProposalAsync"/> instead. Mirrors the npm SDK's
        /// <c>reviewNewElement</c> (4.66.0) and the Python package's <c>review_new_element</c> (0.35.0), whose refusal of
        /// a reason on an accept this follows (keel answers one 422).
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The decision is not <c>accept</c> or <c>decline</c>, or a reason goes with an accept; nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">The server refused, or answered 200 without the key the decision settles.</exception>
        public async Task<OWReviewed> ReviewNewElementAsync(
            string type, string id, string decision, string reason = null, CancellationToken ct = default)
        {
            if (decision != OWReviewed.Accept && decision != OWReviewed.Decline)
            {
                throw new ArgumentException(
                    $"review: decision must be 'accept' or 'decline', got {(decision == null ? "null" : "'" + decision + "'")}. Nothing was sent.",
                    nameof(decision));
            }

            if (reason != null && decision != OWReviewed.Decline)
            {
                throw new ArgumentException("review: a reason goes with a decline only. Nothing was sent.", nameof(reason));
            }

            var body = new JObject { ["decision"] = decision };
            if (reason != null) body["reason"] = reason;

            var path = $"/{type}/{Uri.EscapeDataString(id)}/review";
            var answer = await RequestAsync<OWReviewed>("POST", path, body: body, named: true, ct: ct).ConfigureAwait(false);

            // keel answers exactly one of the two; a 200 without the one this decision settles is never read as settled.
            var settled = decision == OWReviewed.Accept ? answer?.Element != null : answer?.Deleted != null;
            if (!settled)
            {
                throw new OWApiError((HttpStatusCode)200,
                    $"POST {path} answered 200 without its {(decision == OWReviewed.Accept ? "element" : "deleted")}: the review's outcome is unknown");
            }

            return answer;
        }

        /// <summary>The proposals of one element: /{type}/{id}/proposals.</summary>
        private static string ProposalsPath(string type, string id) => $"/{type}/{Uri.EscapeDataString(id)}/proposals";

        private string ProposalQuery(OWProposalQuery q)
        {
            var query = new List<string>();
            if (!string.IsNullOrEmpty(q.State)) query.Add("state=" + Uri.EscapeDataString(q.State));
            if (q.Since.HasValue) query.Add("since=" + Uri.EscapeDataString(Utc(q.Since.Value)));
            if (!string.IsNullOrEmpty(q.Element)) query.Add("element=" + Uri.EscapeDataString(q.Element));
            query.Add("limit=" + (q.Limit ?? PageSize));
            if (!string.IsNullOrEmpty(q.Cursor)) query.Add("cursor=" + Uri.EscapeDataString(q.Cursor));
            return string.Join("&", query);
        }

        /// <summary>A time as keel's ISO 8601, in UTC, to the second.</summary>
        private static string Utc(DateTimeOffset at)
            => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        /// <summary>The row under <c>proposal</c> in a <c>{proposal}</c> answer.</summary>
        private static OWProposal ProposalOf(JObject answer)
            => (answer?["proposal"] as JObject)?.ToObject<OWProposal>(Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));

        // -- The account's worlds (account token) -----------------------------

        /// <summary>
        /// POST /account/worlds -- make a world on the account, with its one write key. Returns the world's row and the
        /// full <c>ow_w_</c> key, shown this once (<see cref="OWAccountWorldCreated.Key"/>).
        /// </summary>
        /// <param name="world">
        /// The world: <see cref="OWAccountWorldCreate.Name"/> is required; each other member is sent only when it is not
        /// null (<see cref="OWAccountWorldCreate.KeyName"/> as <c>key_name</c>).
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="world"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException">
        /// The world has no name, or its <see cref="OWAccountWorldCreate.KeyName"/> is blank or past 80 characters after
        /// keel's trim, each a 422 at keel; nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">
        /// The server refused. A 422 with <see cref="OWApiError.Param"/> <c>pin</c> is the PIN: the account has none and
        /// <see cref="OWAccountWorldCreate.Pin"/> was not sent, or it has one and it was. Nothing was made; send again
        /// with or without it.
        /// </exception>
        /// <remarks>
        /// <para>
        /// An account token only, sent as its Bearer and never with <c>API-Pin</c>. No <c>X-OW-Agent</c> or
        /// <c>X-OW-Client</c>, which keel's document does not list here, and no <c>Idempotency-Key</c>, which it does
        /// not take: if the answer is lost, the world is on the account's world list without its full key, and a new
        /// key is minted for it there.
        /// </para>
        /// <para>
        /// <see cref="OWAccountWorldCreate.Source"/> and <see cref="OWAccountWorldCreate.Producer"/> are the folder
        /// format's provenance lines, from a tool that makes the world from a folder; keel keeps them as given and
        /// serves them back under the world's <c>origin</c> (<see cref="GetWorldAsync"/>). Mirrors the npm SDK's
        /// <c>createAccountWorld</c> (4.66.0), its refusal of any other key kind included. The key's name is checked as
        /// <see cref="MintWorldKeyAsync"/> checks its own, trimmed as Python's <c>str.strip()</c> trims and counted in code
        /// points, so no name keel takes is refused here; the TypeScript SDK leaves both checks to keel.
        /// </para>
        /// </remarks>
        public async Task<OWAccountWorldCreated> CreateAccountWorldAsync(OWAccountWorldCreate world, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(CreateAccountWorldAsync));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Name == null)
            {
                throw new ArgumentException(
                    "CreateAccountWorldAsync: Name is required, the world's name. Nothing was sent.", nameof(world));
            }

            // keel's _clean_key_name: blank is a 422 on this route (the mint reads it as no name), and so is a name past
            // KeyNameMax; the name goes as given, keel trims what it stores.
            var keyName = world.KeyName?.Trim(PythonSpace);
            if (keyName != null && (keyName.Length == 0 || CodePoints(keyName) > KeyNameMax))
            {
                throw new ArgumentException(
                    $"CreateAccountWorldAsync: KeyName is 1 to {KeyNameMax} characters once trimmed, as keel holds it; "
                    + "null for a key with no name. Nothing was sent.",
                    nameof(world));
            }

            // Only the members given: keel reads a null and a missing key alike here, so none is sent.
            var body = OnlyGiven(world);

            return await RequestAsync<OWAccountWorldCreated>("POST", "/account/worlds", body: body, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /account/worlds -- every world the account owns or is a member of, the rows of keel's <c>{data}</c> (no
        /// paging), each with its keys by name and last four: every key on a world it owns, only its own member keys on a
        /// shared one.
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>
        /// An account token only, sent as its Bearer, with or without <see cref="World"/> (never sent here). Mirrors the
        /// TypeScript SDK's <c>listAccountWorlds</c> (4.45.0).
        /// </remarks>
        public async Task<List<OWAccountWorld>> ListAccountWorldsAsync(CancellationToken ct = default)
        {
            RequireAccountToken(nameof(ListAccountWorldsAsync));
            var page = await RequestAsync<JObject>("GET", "/account/worlds", ct: ct).ConfigureAwait(false);
            return RowsOf<OWAccountWorld>(page);
        }

        /// <summary>
        /// POST /account/worlds/{world_id}/keys -- mint a key for a world of the account, a write key unless
        /// <paramref name="scope"/> asks for a read key: the owner mints a world key, a member a key bound to its own
        /// membership. Returns the key's row and the full key, shown this once (<see cref="OWMintedKey.Key"/>).
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <param name="scope">
        /// <c>read</c> or <c>write</c> (<c>r</c> and <c>w</c> the same, in any case, as keel reads it); null or empty:
        /// a write key.
        /// </param>
        /// <param name="name">
        /// The key's name, as the website's key list shows it: at most 80 characters once keel trims it. Null, empty
        /// or blank: no name.
        /// </param>
        /// <param name="keep">
        /// True keeps the key across a sign-out: no revoke of the token that minted it takes it, while disconnecting the
        /// tool, or a replay of the sign-in code that gave the token, still does (a tool that stores the key on the
        /// device passes it). Null or false: revoking the token revokes the key, unless the tool holds another live
        /// token. The answer says <see cref="OWMintedKey.Kept"/>.
        /// </param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="scope"/> is neither read nor write, or <paramref name="name"/> is past 80 characters after
        /// keel's trim, each a 422 at keel; nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">
        /// The server refused: a world the account neither owns nor is a member of is a 404; a member's write key while
        /// the account has no PIN is a 409 <c>pin_required</c> (<see cref="OWApiError.IsPinRequired"/>).
        /// </exception>
        /// <remarks>
        /// <para>
        /// Each option is sent only when given, as given, under keel's own key (<c>scope</c>, <c>name</c>,
        /// <c>keep</c>); with none given no body is sent, which keel's document makes optional, all of it, and mints with
        /// its defaults: a write key, with no name, not kept (the TypeScript SDK sends <c>{}</c> there, the same mint).
        /// The name is checked as keel checks it, trimmed of what Python's <c>str.strip()</c> trims and counted in code
        /// points, so no name keel takes is refused here; keel trims the name it stores.
        /// </para>
        /// <para>
        /// An account token only, sent as its Bearer, never with <c>API-Pin</c>; no <c>X-OW-Agent</c> or
        /// <c>X-OW-Client</c>, which keel's document does not list here, and no <c>Idempotency-Key</c>: if the answer is
        /// lost, the key is on the world's key list without its full key, so revoke it and mint another. The options
        /// come after <paramref name="ct"/>, so a caller passing it by position keeps working. Mirrors the TypeScript
        /// SDK's <c>mintWorldKey</c> (4.45.0; without <c>X-OW-Agent</c> from 4.120.0).
        /// </para>
        /// </remarks>
        public Task<OWMintedKey> MintWorldKeyAsync(
            string worldId, CancellationToken ct = default, string scope = null, string name = null, bool? keep = null)
        {
            RequireAccountToken(nameof(MintWorldKeyAsync));
            if (!string.IsNullOrEmpty(scope)
                && !Array.Exists(KeyScopes, s => string.Equals(s, scope, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"MintWorldKeyAsync: scope is read or write (r or w), got \"{scope}\". Nothing was sent.", nameof(scope));
            }

            if (!string.IsNullOrEmpty(name) && CodePoints(name.Trim(PythonSpace)) > KeyNameMax)
            {
                throw new ArgumentException(
                    $"MintWorldKeyAsync: name is at most {KeyNameMax} characters once trimmed, as keel holds it. "
                    + "Nothing was sent.",
                    nameof(name));
            }

            var body = new JObject();
            if (!string.IsNullOrEmpty(scope)) body["scope"] = scope;
            if (!string.IsNullOrEmpty(name)) body["name"] = name;
            if (keep.HasValue) body["keep"] = keep.Value;

            return RequestAsync<OWMintedKey>("POST", $"/account/worlds/{Uri.EscapeDataString(worldId)}/keys",
                body: body.Count > 0 ? body : null, ct: ct);
        }

        // keel's key mint (accounts/api.py _scope_param, _key_name_param, KEY_NAME_MAX): the scopes it takes, lower-cased
        // first; a name's longest, in code points after Python's str.strip(), whose whitespace is this set (.NET's Trim()
        // keeps U+001C to U+001F). The world create's key_name is held to the same (_clean_key_name).
        private static readonly string[] KeyScopes = { "read", "write", "r", "w" };
        private const int KeyNameMax = 80;

        private static readonly char[] PythonSpace =
        {
            '\t', '\n', '\v', '\f', '\r', '\u001c', '\u001d', '\u001e', '\u001f', ' ', '\u0085', '\u00a0', '\u1680',
            '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200a',
            '\u2028', '\u2029', '\u202f', '\u205f', '\u3000',
        };

        private static int CodePoints(string text)
        {
            var n = 0;
            for (var i = 0; i < text.Length; i++, n++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            }

            return n;
        }

        /// <summary>
        /// DELETE /account/worlds/{world_id}/keys/{key_id} -- revoke a world key, by the <see cref="OWMintedKey.Id"/> its
        /// mint answered: from then on it answers 401 <c>key_revoked</c>. Answers 204.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="keyId">The key's id. Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="OWApiError">
        /// The server refused: an id this account may not revoke is a 404 (<see cref="OWApiError.IsNotFound"/>); an
        /// account token already revoked or expired is a 401 (<see cref="OWApiError.IsAuthError"/>), and the key was not
        /// revoked.
        /// </exception>
        /// <remarks>
        /// The owner revokes any of the world's keys, a member only its own. Revoking a revoked key changes nothing and
        /// answers the same 204, so a tool that signs out can revoke its key without knowing whether it already did. An
        /// account token only, sent as its Bearer. Mirrors the TypeScript SDK's <c>revokeWorldKey</c> (4.78.0).
        /// </remarks>
        public Task RevokeWorldKeyAsync(string worldId, string keyId, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(RevokeWorldKeyAsync));
            return RequestAsync<object>("DELETE",
                $"/account/worlds/{Uri.EscapeDataString(worldId)}/keys/{Uri.EscapeDataString(keyId)}",
                allowEmpty: true, ct: ct);
        }

        /// <summary>
        /// POST /account/join -- accept a person's invite link as the account this client's token belongs to: 201 with the
        /// new membership.
        /// </summary>
        /// <param name="code">
        /// The link's <c>ow_j_</c> code (<see cref="OWJoin.ParseLink"/> reads it out of a pasted link), sent as given.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentException">The code is null or empty; nothing was sent.</exception>
        /// <exception cref="OWApiError">
        /// The server refused, and the code is not spent: every bad code (unknown, used, revoked or expired), and a code
        /// made for an AI agent, is the same 404 <c>not_found</c> (<see cref="OWApiError.IsNotFound"/>); an account already
        /// in the world is a 409 <c>already_member</c> (<see cref="OWApiError.IsAlreadyMember"/>).
        /// </exception>
        /// <remarks>
        /// Accepting needs no account PIN, for any role; a write key for the membership waits for one
        /// (<see cref="MintWorldKeyAsync"/>, <see cref="OWApiError.IsPinRequired"/>). An account token only, sent as its
        /// Bearer: the body is <c>{"code"}</c> alone. A player's or an agent's code is redeemed with no key,
        /// <see cref="OWJoin.RedeemAsync"/>. Mirrors the TypeScript SDK's <c>acceptJoin</c> (4.12.0).
        /// </remarks>
        public Task<OWJoinedMembership> AcceptJoinAsync(string code, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(AcceptJoinAsync),
                "A world key cannot join a world; a player's or an agent's code is redeemed with OWJoin.RedeemAsync.");
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("AcceptJoinAsync: a join code is required. Nothing was sent.", nameof(code));

            return RequestAsync<OWJoinedMembership>("POST", "/account/join", body: new JObject { ["code"] = code }, ct: ct);
        }

        /// <summary>
        /// POST /account/worlds/{world_id}/links -- a new invite link for a world the account owns. Returns the link's
        /// row with its code, the link and the line to send, shown in this answer only
        /// (<see cref="OWWorldLinkCreated.Code"/>).
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="link">
        /// The link: <see cref="OWWorldLinkCreate.Reader"/> is required; each other member is sent only when it is not
        /// null, in keel's spelling (<c>inviter_name</c>, <c>with_character</c>, <c>max_uses</c>).
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="link"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException">The link names no reader; nothing was sent.</exception>
        /// <exception cref="OWApiError">
        /// The server refused: anyone but the world's owner, and a world that does not exist, is a 404 <c>not_found</c>
        /// (<see cref="OWApiError.IsNotFound"/>); an option the reader does not take is a 422 naming it
        /// (<see cref="OWApiError.Param"/>).
        /// </exception>
        /// <remarks>
        /// A person's link is accepted signed in (<see cref="AcceptJoinAsync"/>), an agent's and a player's are redeemed
        /// with no key (<see cref="OWJoin.RedeemAsync"/>). A table (a player link with <see cref="OWWorldLinkCreate.MaxUses"/>
        /// 2 to 50) counts once against the world's 50 pending invites; its seats count against the 50 players as each is
        /// made, so a full world refuses the pick, never the link. An account token only, sent as its Bearer; no
        /// <c>Idempotency-Key</c>, so a lost answer leaves a link whose code no one holds: revoke it and make another.
        /// Mirrors the TypeScript SDK's <c>createWorldLink</c> (4.24.0).
        /// </remarks>
        public Task<OWWorldLinkCreated> CreateWorldLinkAsync(string worldId, OWWorldLinkCreate link, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(CreateWorldLinkAsync), "Only the world's owner makes links, signed in.");
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (link.Reader == null)
            {
                throw new ArgumentException(
                    "CreateWorldLinkAsync: Reader is required: person, agent or player. Nothing was sent.", nameof(link));
            }

            return RequestAsync<OWWorldLinkCreated>("POST", $"/account/worlds/{Uri.EscapeDataString(worldId)}/links",
                body: OnlyGiven(link), ct: ct);
        }

        /// <summary>
        /// GET /account/worlds/{world_id}/links -- the world's invite links, the rows of keel's <c>{data}</c> (no paging),
        /// never their codes: each with its <see cref="OWWorldLink.Status"/> and <see cref="OWWorldLink.Uses"/>.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>
        /// The world's owner only, as <see cref="CreateWorldLinkAsync"/>: anyone else is a 404. An account token only, sent
        /// as its Bearer. Mirrors the TypeScript SDK's <c>listWorldLinks</c> (4.24.0).
        /// </remarks>
        public async Task<List<OWWorldLink>> ListWorldLinksAsync(string worldId, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(ListWorldLinksAsync), "Only the world's owner sees its links.");
            var page = await RequestAsync<JObject>("GET", $"/account/worlds/{Uri.EscapeDataString(worldId)}/links", ct: ct)
                .ConfigureAwait(false);
            return RowsOf<OWWorldLink>(page);
        }

        /// <summary>
        /// DELETE /account/worlds/{world_id}/links/{link_id} -- revoke a link: its code no longer works. Answers 204, and
        /// the same for a used, revoked or expired link, which stays as it was.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="linkId">The link's id (<see cref="OWWorldLink.Id"/>). Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>
        /// Whoever joined with it stays a member (remove them from the roster); a table revokes at any time before it is
        /// full, and the players already seated stay. The world's owner only. An account token only, sent as its Bearer.
        /// Mirrors the TypeScript SDK's <c>revokeWorldLink</c> (4.24.0).
        /// </remarks>
        public Task RevokeWorldLinkAsync(string worldId, string linkId, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(RevokeWorldLinkAsync), "Only the world's owner revokes links.");
            return RequestAsync<object>("DELETE",
                $"/account/worlds/{Uri.EscapeDataString(worldId)}/links/{Uri.EscapeDataString(linkId)}",
                allowEmpty: true, ct: ct);
        }

        /// <summary>
        /// PATCH /account/worlds/{world_id}/members/{member_id} with <c>lenses</c> alone -- set the lenses a row of the
        /// world starts with: lens ids in order, 0 to 20, repeats dropped by the server; an empty list is a choice too, and
        /// keeps a seat off its Titles' lenses. Null puts the row back to never chosen. Answers the row as served, a
        /// <see cref="JObject"/>: <c>{id, role, kind, lenses}</c>, and
        /// <c>x_served_trimmed</c> naming <c>lenses</c> when the list keeps a lens you cannot see (which stays at its
        /// place in the stored list and is left out of the answer's).
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="memberId">The membership's id, or <c>owner</c> for the owner's own list. Escaped.</param>
        /// <param name="lensIds">
        /// The lens ids, in the order the row reads them; empty for none; null (sent as <c>null</c>) for never chosen, so a
        /// seat playing a Character starts with its Titles' lenses again; for <c>owner</c>, null is the empty list.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <remarks>
        /// <para>
        /// A member sets its own row (any role), a seat it answers for (its sponsor), or any seat as a co-builder; the
        /// owner any seat, and its own list with <c>owner</c>. Any other row is a 404; the owner naming a person's row is
        /// a 422 (a person sets its own, with <see cref="SetMyLensesAsync"/>). Each id must be a lens of this world that
        /// you and the row's holder can both see: one either cannot see is the same 404, a Construct that is no lens 422
        /// <c>not_a_lens</c>, a template 422 <c>lens_is_template</c>, a hidden lens 422 <c>lens_hidden</c>
        /// (<c>param</c> <c>lenses</c>).
        /// </para>
        /// <para>
        /// The body is <c>{"lenses": [...]}</c> and nothing else: the route also takes a role, a Character or a sponsor,
        /// and none of them is sent here. An account token only, sent as its Bearer, never with <c>API-Pin</c>,
        /// <c>X-OW-Agent</c> or <c>X-OW-Client</c>. Mirrors the TypeScript SDK's <c>setMemberLenses</c> (4.94.0; null
        /// from 4.99.0).
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        public Task<JObject> SetMemberLensesAsync(
            string worldId, string memberId, IEnumerable<string> lensIds, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(SetMemberLensesAsync), "A world key sets its own holder's list with SetMyLensesAsync.");
            var body = new JObject { ["lenses"] = LensList(lensIds) };
            return RequestAsync<JObject>("PATCH",
                $"/account/worlds/{Uri.EscapeDataString(worldId)}/members/{Uri.EscapeDataString(memberId)}",
                body: body, ct: ct);
        }

        /// <summary>A lens list as the lens routes take it: the ids in order, or <c>null</c> for never chosen.</summary>
        private static JToken LensList(IEnumerable<string> lensIds)
            => lensIds == null ? JValue.CreateNull() : new JArray(lensIds);

        // -- Seats for your own AI (account token) -----------------------------

        /// <summary>
        /// POST /account/worlds/{world_id}/seats -- a seat for your own AI in a world you own or are a member of: its own
        /// membership, write key and <c>ow_s_</c> secret, no join link. You answer for it, and its writes are marked as an
        /// agent's. Returns the seat, its key, its secret and the <c>.env</c> lines, shown in this answer only.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="name">The AI's own name in the world, 1 to 80 characters. Required.</param>
        /// <param name="role">
        /// At most your own (the owner may give any). Null: contributor, or your own role when that is lower.
        /// </param>
        /// <param name="character">
        /// <c>new</c> (a Character of its own), <c>none</c>, or <c>mine</c> (members only: it plays the Character you
        /// play). Null: <c>new</c>.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is null; nothing was sent.</exception>
        /// <remarks>
        /// The body is <c>{name}</c>, with <c>role</c> and <c>character</c> when given. No <c>Idempotency-Key</c>, by keel's
        /// rule (a replay would have to keep the key): if the answer is lost, <see cref="ListSeatsAsync"/> shows the seat
        /// without its key, and <see cref="RekeySeatAsync"/> gives it a new one. An account token only, sent as its
        /// Bearer, never with a PIN or a write header. Mirrors the TypeScript SDK's <c>createSeat</c> (4.28.0).
        /// </remarks>
        public Task<OWSeatCreated> CreateSeatAsync(
            string worldId, string name, string role = null, string character = null, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(CreateSeatAsync), "A seat is made by the person who answers for it, signed in.");
            if (name == null)
            {
                throw new ArgumentException(
                    "CreateSeatAsync: name is required, the AI's own name in the world. Nothing was sent.", nameof(name));
            }

            var body = new JObject { ["name"] = name };
            if (role != null) body["role"] = role;
            if (character != null) body["character"] = character;

            return RequestAsync<OWSeatCreated>("POST", $"/account/worlds/{Uri.EscapeDataString(worldId)}/seats",
                body: body, ct: ct);
        }

        /// <summary>
        /// GET /account/worlds/{world_id}/seats -- the live agent seats you can see, the rows of keel's <c>{data}</c> (no
        /// paging): your own AI's, or, as the world's owner, every seat. No keys or secrets.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>An account token only, sent as its Bearer. Mirrors the TypeScript SDK's <c>listSeats</c> (4.28.0).</remarks>
        public async Task<List<OWSeat>> ListSeatsAsync(string worldId, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(ListSeatsAsync));
            var page = await RequestAsync<JObject>("GET", $"/account/worlds/{Uri.EscapeDataString(worldId)}/seats", ct: ct)
                .ConfigureAwait(false);
            return RowsOf<OWSeat>(page);
        }

        /// <summary>
        /// POST /account/worlds/{world_id}/seats/{seat_id}/key -- a new key and secret for a seat whose AI lost its own: the
        /// old key and secret stop at once (the old key answers 401 <c>key_revoked</c>); nothing else about the seat
        /// changes. Answers as <see cref="CreateSeatAsync"/> does, shown in this answer only.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="seatId">The seat's membership id (<see cref="OWSeat.Id"/>). Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>
        /// Its sponsor or the world's owner may. Sent with no body, and no <c>Idempotency-Key</c>, by keel's rule: if this
        /// answer is lost too, ask for another. An account token only, sent as its Bearer. Mirrors the TypeScript SDK's
        /// <c>rekeySeat</c> (4.29.0).
        /// </remarks>
        public Task<OWSeatCreated> RekeySeatAsync(string worldId, string seatId, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(RekeySeatAsync));
            return RequestAsync<OWSeatCreated>("POST",
                $"/account/worlds/{Uri.EscapeDataString(worldId)}/seats/{Uri.EscapeDataString(seatId)}/key", ct: ct);
        }

        /// <summary>
        /// DELETE /account/worlds/{world_id}/seats/{seat_id} -- remove an agent seat: its key and secret stop at once; a
        /// Character it made that nobody touched goes with it, any other stays. Answers 204.
        /// </summary>
        /// <param name="worldId">The world. Escaped.</param>
        /// <param name="seatId">The seat's membership id. Escaped.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>
        /// Its sponsor or the world's owner may; another member's seat is the same 404 as none. An account token only, sent
        /// as its Bearer. Mirrors the TypeScript SDK's <c>removeSeat</c> (4.28.0).
        /// </remarks>
        public Task RemoveSeatAsync(string worldId, string seatId, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(RemoveSeatAsync));
            return RequestAsync<object>("DELETE",
                $"/account/worlds/{Uri.EscapeDataString(worldId)}/seats/{Uri.EscapeDataString(seatId)}",
                allowEmpty: true, ct: ct);
        }

        // -- Signing in ---------------------------------------------------------------
        //
        // Two ways to an account token (ow_a_), on any client, keyless included, and with no credential of the client's:
        // a password (three requests: the sign-in service, the mint, the session's end), or Sign in with OnlyWorlds
        // (OAuth 2.1's code with PKCE: an authorize URL for the system browser, then one exchange). The SDK keeps no
        // token: the caller stores it and makes a client with it. Every refusal after the call is an OWSignInError, which
        // holds no secret and no inner exception. Mirrors the TypeScript SDK's signInWithPassword (4.31.0),
        // signInWithOnlyWorlds and completeSignIn (4.46.0; expiresAt from 4.94.0).

        /// <summary>How long the password sign-in waits for its session's end before it answers anyway.</summary>
        internal static TimeSpan SessionEndLimit = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Sign in with an email address (or a username) and password, and get a new account token (<c>ow_a_</c>) for this
        /// device. Make a client with it (<c>new OWClientConfig { ApiKey = token }</c>) and store it as any account token.
        /// </summary>
        /// <param name="email">The account's email address; null when signing in by username. Trimmed.</param>
        /// <param name="username">The account's username; null when signing in by email. Trimmed.</param>
        /// <param name="password">The password: sent once, to the sign-in service alone, and held nowhere.</param>
        /// <param name="ct">Cancels the requests (the session's end still goes).</param>
        /// <exception cref="ArgumentException">
        /// Both or neither of <paramref name="email"/> and <paramref name="username"/>, or no password; nothing was sent.
        /// The message never holds the password.
        /// </exception>
        /// <exception cref="OWSignInError">
        /// The sign-in was refused; <see cref="OWSignInError.Reason"/> says why: <c>invalid_credentials</c>,
        /// <c>email_unverified</c>, <c>rate_limited</c> (with <see cref="OWSignInError.RetryAfter"/> when sent),
        /// <c>offline</c>, <c>mint_failed</c> (signed in, but no token was made), or <c>failed</c> (with the server's words
        /// when they hold no secret).
        /// </exception>
        /// <remarks>
        /// <para>
        /// Three requests, none with a key, PIN or Bearer of this client's whatever it holds: POST
        /// <c>{BaseUrl}/auth/app/v1/auth/login</c> <c>{email|username, password}</c> at keel's sign-in service, which answers
        /// a short-lived session; POST <c>/account/tokens</c> with that session as <c>X-Session-Token</c>, which mints the
        /// token; then DELETE <c>{BaseUrl}/auth/app/v1/auth/session</c> with the session, whether the mint worked or not,
        /// given five seconds at most and its failure ignored. Only the token is kept: the session never leaves this call.
        /// </para>
        /// <para>
        /// The mint goes with no body: keel's document declares none there, though keel reads an optional
        /// <c>{name}</c> and the TypeScript SDK sends its <c>tokenName</c> in one. So the token is listed with no name on
        /// the account's tokens page, where it can be revoked; a name comes when keel documents the body. A person signing
        /// in holds no key: the bare client (<c>new OWClientConfig { Transport }</c>) is theirs. Mirrors the TypeScript
        /// SDK's <c>signInWithPassword</c> (4.31.0).
        /// </para>
        /// </remarks>
        public Task<OWSignedIn> SignInWithPasswordAsync(
            string email, string username, string password, CancellationToken ct = default)
        {
            var login = email?.Trim() ?? "";
            var user = username?.Trim() ?? "";
            if ((login.Length > 0) == (user.Length > 0))
            {
                throw new ArgumentException(
                    "SignInWithPasswordAsync takes exactly one of email and username. Nothing was sent.",
                    login.Length > 0 ? nameof(username) : nameof(email));
            }

            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("SignInWithPasswordAsync needs a password. Nothing was sent.", nameof(password));

            var body = login.Length > 0
                ? new JObject { ["email"] = login, ["password"] = password }
                : new JObject { ["username"] = user, ["password"] = password };

            return SignInWithPasswordCoreAsync(body, password, ct);
        }

        private async Task<OWSignedIn> SignInWithPasswordCoreAsync(JObject credentials, string password, CancellationToken ct)
        {
            var auth = BaseUrl + "/auth/app/v1";

            var res = await SendSignInAsync(new OWRequest
            {
                Method = "POST",
                Url = auth + "/auth/login",
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                Body = credentials.ToString(Newtonsoft.Json.Formatting.None),
            }, "OnlyWorlds could not be reached: signing in needs a connection.", ct).ConfigureAwait(false);

            var retryAfter = OWApiError.ParseRetryAfter(ReadHeader(res, "Retry-After"), DateTimeOffset.UtcNow);
            if (res.Status == 429)
            {
                // allauth's 429 may be an HTML page: the status alone says it.
                throw new OWSignInError(OWSignInReason.RateLimited, "Too many sign-in attempts. Wait a while, then try again.",
                    429, retryAfter: retryAfter);
            }

            var answer = JsonObjectOrNull(res.Body);
            var session = (answer?["meta"] as JObject)?["session_token"] is JValue s && s.Type == JTokenType.String
                && (string)s != "" ? (string)s : null;

            // Any text the server sends back is shown only if it holds no secret.
            string Safe(string text, string fallback)
                => string.IsNullOrEmpty(text) || text.Contains(password) || (session != null && text.Contains(session))
                    ? fallback : text;

            if (!res.IsSuccess || session == null)
            {
                // A pending flow can still hand out a session: it is ended too.
                if (session != null) await EndAuthSessionAsync(auth, session).ConfigureAwait(false);

                var errors = answer?["errors"] as JArray ?? new JArray();
                var codes = new List<string>();
                string first = null;
                foreach (var e in errors)
                {
                    if (e is JObject row && row["code"]?.Type == JTokenType.String) codes.Add((string)row["code"]);
                    if (first == null && e is JObject said && said["message"]?.Type == JTokenType.String) first = (string)said["message"];
                }

                var code = codes.Find(c => Safe(c, null) != null);
                var mismatch = codes.Find(c => c == "email_password_mismatch" || c == "username_password_mismatch");
                if (mismatch != null)
                {
                    throw new OWSignInError(OWSignInReason.InvalidCredentials,
                        Safe(first, "The email address and/or password you specified are not correct."), res.Status, mismatch);
                }

                if (codes.Contains("too_many_login_attempts"))
                {
                    throw new OWSignInError(OWSignInReason.RateLimited,
                        Safe(first, "Too many failed sign-in attempts. Try again later."), res.Status, "too_many_login_attempts",
                        retryAfter);
                }

                var flows = (answer?["data"] as JObject)?["flows"] as JArray ?? new JArray();
                var unverified = false;
                foreach (var f in flows)
                {
                    if (f is JObject flow && flow["id"]?.Type == JTokenType.String && (string)flow["id"] == "verify_email"
                        && flow["is_pending"]?.Type == JTokenType.Boolean && (bool)flow["is_pending"]) unverified = true;
                }

                if (res.Status == 401 && unverified)
                {
                    throw new OWSignInError(OWSignInReason.EmailUnverified,
                        "Confirm the account's email address first: the link is in the mail onlyworlds.com sent.", res.Status, code);
                }

                throw new OWSignInError(OWSignInReason.Failed,
                    Safe(first, res.IsSuccess ? "Signing in did not work: onlyworlds.com sent no session." : $"Signing in did not work ({res.Status})."),
                    res.Status, code);
            }

            try
            {
                // No body: keel's document declares none on the mint (the TypeScript SDK sends { name: tokenName }).
                var minted = await SendSignInAsync(new OWRequest
                {
                    Method = "POST",
                    Url = BaseUrl + "/account/tokens",
                    Headers = new Dictionary<string, string> { ["X-Session-Token"] = session },
                }, "The connection dropped after signing in: no token was made. Try again.", ct).ConfigureAwait(false);

                var row = JsonObjectOrNull(minted.Body);
                var token = row?["token"] is JValue t && t.Type == JTokenType.String && (string)t != "" ? (string)t : null;
                if (minted.Status != 201 || token == null)
                {
                    // keel's envelope: { error: { code, message, ... } }.
                    var err = row?["error"] as JObject;
                    string SafeMint(string text, string fallback)
                        => token != null && text != null && text.Contains(token) ? fallback : Safe(text, fallback);

                    var said = err?["code"]?.Type == JTokenType.String ? (string)err["code"] : null;
                    throw new OWSignInError(OWSignInReason.MintFailed,
                        SafeMint(err?["message"]?.Type == JTokenType.String ? (string)err["message"] : null,
                            $"Signed in, but no token was made ({minted.Status}). Try again."),
                        minted.Status, SafeMint(said, null));
                }

                return new OWSignedIn
                {
                    Token = token,
                    Id = row["id"]?.Type == JTokenType.String ? (string)row["id"] : "",
                    Name = row["name"]?.Type == JTokenType.String ? (string)row["name"] : "",
                    Last4 = row["last4"]?.Type == JTokenType.String ? (string)row["last4"] : token.Substring(Math.Max(0, token.Length - 4)),
                };
            }
            finally
            {
                await EndAuthSessionAsync(auth, session).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The password sign-in's session ended: DELETE with the session alone, given <see cref="SessionEndLimit"/> at most
        /// (the token is already minted, and a request hanging on a bad network must not hold the sign-in), its failure
        /// ignored.
        /// </summary>
        private async Task EndAuthSessionAsync(string auth, string session)
        {
            var cut = new CancellationTokenSource();
            try
            {
                var send = _transport.SendAsync(new OWRequest
                {
                    Method = "DELETE",
                    Url = auth + "/auth/session",
                    Headers = new Dictionary<string, string> { ["X-Session-Token"] = session },
                }, cut.Token);

                // Its failure, whenever it comes, is observed here and dropped.
                _ = send.ContinueWith(x => x.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                await Task.WhenAny(send, Task.Delay(SessionEndLimit, cut.Token)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallowed on purpose.
            }
            finally
            {
                cut.Cancel();
            }
        }

        /// <summary>
        /// Begin Sign in with OnlyWorlds: a fresh state and PKCE verifier, and the authorize URL on the site
        /// (<c>https://www.onlyworlds.com/signin/authorize</c>) to open in the system browser. No request is sent, on any
        /// client. Keep the answer until the return reaches the app, then hand both to <see cref="CompleteSignInAsync"/>.
        /// </summary>
        /// <param name="clientId">The <c>client_id</c> keel's registry lists the tool under (<c>GET /api/v2/auth/clients</c>).</param>
        /// <param name="redirectUri">
        /// The registered return address, the exact string. A web tool's is <c>https:</c>. A tool registered as native
        /// may pass a loopback address with the port it bound for this sign-in (<c>http://127.0.0.1:&lt;port&gt;/&lt;path&gt;</c>
        /// or <c>http://[::1]:&lt;port&gt;/&lt;path&gt;</c>, registered without the port), or its own scheme as registered
        /// (<c>obsidian://onlyworlds-signin</c>). <c>localhost</c>, a loopback without its port, userinfo, a query, a
        /// fragment, a dot segment and the reserved schemes are refused.
        /// </param>
        /// <exception cref="ArgumentException">No client id, or a return address keel cannot match, saying why and what it takes.</exception>
        /// <remarks>
        /// The URL carries <c>client_id</c>, <c>redirect_uri</c>, <c>state</c>, <c>code_challenge</c> (the verifier's
        /// S256, <see cref="OWSignIn.PkceChallenge"/>) and <c>code_challenge_method=S256</c>, in that order, written as a
        /// browser's <c>URLSearchParams</c> writes them; the verifier never goes in it. The site is <see cref="BaseUrl"/>'s
        /// origin. The TypeScript SDK also keeps the record in a storage and sends the browser itself; here the answer is the
        /// record, and opening the URL is the caller's (<c>Application.OpenURL</c>). Mirrors the TypeScript SDK's
        /// <c>signInWithOnlyWorlds</c> (4.46.0).
        /// </remarks>
        public OWSignInBegun SignInWithOnlyWorlds(string clientId, string redirectUri)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("SignInWithOnlyWorlds needs clientId, the tool's id in keel's registry.", nameof(clientId));

            var why = OWSignIn.RedirectProblem(redirectUri);
            if (why != null)
            {
                throw new ArgumentException(
                    $"SignInWithOnlyWorlds: redirectUri {why}. Pass the registered return address as a string: "
                    + "https://<host>/<path>, or for a native app http://127.0.0.1:<port>/<path> or http://[::1]:<port>/<path> "
                    + "with the port it bound, or its own scheme, <scheme>://<path>", nameof(redirectUri));
            }

            var state = OWSignIn.RandomToken(32);
            var verifier = OWSignIn.RandomToken(32); // 43 characters of base64url: within PKCE's 43 to 128

            var url = new Uri(BaseUrl).GetLeftPart(UriPartial.Authority) + "/signin/authorize"
                + "?client_id=" + OWSignIn.FormEncode(clientId)
                + "&redirect_uri=" + OWSignIn.FormEncode(redirectUri)
                + "&state=" + OWSignIn.FormEncode(state)
                + "&code_challenge=" + OWSignIn.FormEncode(OWSignIn.PkceChallenge(verifier))
                + "&code_challenge_method=S256";

            return new OWSignInBegun
            {
                Url = url, State = state, ClientId = clientId, RedirectUri = redirectUri, Verifier = verifier, Pending = true,
            };
        }

        /// <summary>
        /// Finish Sign in with OnlyWorlds: check the return's <c>state</c> against the pending sign-in and its page against
        /// the registered return address (scheme, host with port, and path, each exactly), take <c>code</c> out of it, and
        /// trade the code once at POST /auth/token with the PKCE verifier and no credential. Answers the <c>ow_a_</c> token
        /// with its id, its expiry and the account.
        /// </summary>
        /// <param name="pending">The sign-in <see cref="SignInWithOnlyWorlds"/> began.</param>
        /// <param name="url">The address the return reached: what the app's loopback server or scheme handler received.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pending"/> is null; nothing was sent.</exception>
        /// <exception cref="ArgumentException"><paramref name="url"/> is null or empty; nothing was sent.</exception>
        /// <exception cref="OWSignInError">
        /// <c>state_mismatch</c> (no sign-in pending, another state or none, or the wrong page; nothing sent),
        /// <c>invalid_grant</c> (keel's 400: the code used, expired, or another tool's), <c>rate_limited</c>,
        /// <c>offline</c>, <c>failed</c>.
        /// </exception>
        /// <remarks>
        /// The sign-in is spent (<see cref="OWSignInBegun.Pending"/> false) once a return carries its state, on success and
        /// on refusal; a return with another state leaves it pending, since anyone can open a custom scheme's address and a
        /// forged one must not cancel a sign-in in flight. The body is <c>{grant_type, code, code_verifier, client_id,
        /// redirect_uri}</c>, with <c>Content-Type</c> and nothing else of this client's, on any client. The SDK keeps no
        /// token: the caller does. Mirrors the TypeScript SDK's <c>completeSignIn</c> (4.46.0; <c>expiresAt</c> from
        /// 4.94.0), whose storage and address bar are the caller's here.
        /// </remarks>
        public Task<OWSignInResult> CompleteSignInAsync(OWSignInBegun pending, string url, CancellationToken ct = default)
        {
            if (pending == null) throw new ArgumentNullException(nameof(pending), "CompleteSignInAsync: the pending sign-in. Nothing was sent.");
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("CompleteSignInAsync: the address the return reached. Nothing was sent.", nameof(url));

            if (!pending.Pending || string.IsNullOrEmpty(pending.State) || string.IsNullOrEmpty(pending.Verifier)
                || string.IsNullOrEmpty(pending.RedirectUri) || string.IsNullOrEmpty(pending.ClientId))
            {
                throw new OWSignInError(OWSignInReason.StateMismatch,
                    "No sign-in is pending for this tool here: start again from the sign-in button.");
            }

            // A return that does not carry this sign-in's state leaves it pending. Once the state matches, the sign-in that
            // was started here has come back, and it is spent whatever happens next.
            var code = OWSignIn.QueryValue(url, "code");
            if (OWSignIn.QueryValue(url, "state") != pending.State)
            {
                throw new OWSignInError(OWSignInReason.StateMismatch,
                    "This page was not reached from the sign-in that was started here: start again.");
            }

            pending.Pending = false;
            if (string.IsNullOrEmpty(code))
            {
                throw new OWSignInError(OWSignInReason.StateMismatch,
                    "This page was not reached from the sign-in that was started here: start again.");
            }

            if (!OWSignIn.SamePage(url, pending.RedirectUri))
            {
                throw new OWSignInError(OWSignInReason.StateMismatch,
                    "This page is not the return address the sign-in was started with.");
            }

            return CompleteSignInCoreAsync(pending, code, ct);
        }

        private async Task<OWSignInResult> CompleteSignInCoreAsync(OWSignInBegun pending, string code, CancellationToken ct)
        {
            var body = new JObject
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = pending.Verifier,
                ["client_id"] = pending.ClientId,
                ["redirect_uri"] = pending.RedirectUri,
            };

            var res = await SendSignInAsync(new OWRequest
            {
                Method = "POST",
                Url = BaseUrl + "/auth/token",
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                Body = body.ToString(Newtonsoft.Json.Formatting.None),
            }, "OnlyWorlds could not be reached: signing in needs a connection.", ct).ConfigureAwait(false);

            var retryAfter = OWApiError.ParseRetryAfter(ReadHeader(res, "Retry-After"), DateTimeOffset.UtcNow);
            var answer = JsonObjectOrNull(res.Body);
            var err = answer?["error"] as JObject;
            var errCode = err?["code"]?.Type == JTokenType.String ? (string)err["code"] : null;
            var said = err?["message"]?.Type == JTokenType.String ? (string)err["message"] : null;

            // keel's words, unless they hold the code or the verifier.
            string Words(string fallback)
                => said == null || said.Contains(code) || said.Contains(pending.Verifier) ? fallback : said;

            if (errCode != null && (errCode.Contains(code) || errCode.Contains(pending.Verifier))) errCode = null;

            if (res.Status == 429)
            {
                throw new OWSignInError(OWSignInReason.RateLimited, Words("Too many sign-in attempts. Wait a while, then try again."),
                    429, errCode, retryAfter);
            }

            if (res.Status == 400 && errCode == "invalid_grant")
            {
                throw new OWSignInError(OWSignInReason.InvalidGrant,
                    Words("The sign-in code could not be exchanged: it was used, expired, or made for another tool. Start again."),
                    400, errCode);
            }

            var token = answer?["token"] is JValue t && t.Type == JTokenType.String && (string)t != "" ? (string)t : null;
            var account = answer?["account"] as JObject;
            if (!res.IsSuccess || token == null || account?["id"]?.Type != JTokenType.String)
            {
                throw new OWSignInError(OWSignInReason.Failed,
                    token != null && said != null && said.Contains(token) ? $"Signing in did not work ({res.Status})." : Words($"Signing in did not work ({res.Status})."),
                    res.Status, errCode);
            }

            string NonEmpty(JToken v) => v?.Type == JTokenType.String && (string)v != "" ? (string)v : null;

            return new OWSignInResult
            {
                Token = token,
                TokenId = NonEmpty(answer["token_id"]),
                ExpiresAt = NonEmpty(answer["expires_at"]),
                TokenType = "bearer",
                Account = new OWSignInAccount
                {
                    Id = (string)account["id"],
                    Username = account["username"]?.Type == JTokenType.String ? (string)account["username"] : "",
                },
            };
        }

        /// <summary>
        /// One request of a sign-in, sent as built: none of <see cref="RequestWithResponseAsync{T}"/>'s rules, no key, PIN or
        /// Bearer. No answer is an <see cref="OWSignInError"/> <c>offline</c> with <paramref name="offline"/> as its words
        /// and no inner exception: a transport's failure can carry the request, and the request holds the secret.
        /// </summary>
        private async Task<OWResponse> SendSignInAsync(OWRequest request, string offline, CancellationToken ct)
        {
            try
            {
                return await _transport.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new OWSignInError(OWSignInReason.Offline, offline);
            }
        }

        /// <summary>A body as a JSON object, or null when it is empty, not JSON or not an object (allauth can answer with an HTML page).</summary>
        private static JObject JsonObjectOrNull(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return OWJson.ParseObject(body);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // -- Ask your world -------------------------------------------------------
        //
        // keel's AI proxy, metered per account: one token rating a day that ask, sort and the Council spend from
        // (AllowanceAsync reads it). A refusal does not count. Mirrors the TypeScript SDK's ask (4.13.0), sort (4.18.0)
        // and allowance (4.21.0).

        /// <summary>
        /// POST /account/ask -- a question about a world the account owns or is in, answered by keel's AI proxy from
        /// <paramref name="context"/> only: <c>The world doesn't say.</c> when the context does not hold the answer.
        /// </summary>
        /// <param name="worldId">The world the question is about, sent as <c>world_id</c>.</param>
        /// <param name="question">The question, up to 500 characters.</param>
        /// <param name="context">
        /// The lines the answer may come from, in order, sent as given: built from the elements the user sees (the
        /// TypeScript SDK's <c>buildAskContext</c> leaves proposed, hidden and room elements out unless asked).
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentException">No world, question or context; nothing was sent.</exception>
        /// <exception cref="OWApiError">
        /// keel refused, and nothing was counted: 429 <c>quota_exceeded</c> (<see cref="OWApiError.IsQuotaExceeded"/>, the
        /// day's tokens used up, <see cref="OWApiError.RetryAfter"/> running to midnight UTC), 503 <c>ask_unavailable</c>
        /// (<see cref="OWApiError.IsAskUnavailable"/>: switched off, or paused for the day), 502 <c>ask_failed</c>
        /// (<see cref="OWApiError.IsAskFailed"/>), 422 for a question over 500 characters or a context past its limits.
        /// </exception>
        /// <remarks>
        /// An account token only, sent as its Bearer, never with <c>API-Pin</c>, and with no write header. Mirrors the
        /// TypeScript SDK's <c>ask</c> (4.13.0).
        /// </remarks>
        public Task<OWAskAnswer> AskAsync(
            string worldId, string question, IEnumerable<string> context, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(AskAsync), "The proxy meters per account.");
            RequireWorldId(nameof(AskAsync), worldId);
            if (question == null) throw new ArgumentNullException(nameof(question), "AskAsync: the question. Nothing was sent.");
            if (context == null) throw new ArgumentNullException(nameof(context), "AskAsync: the context lines. Nothing was sent.");

            var body = new JObject
            {
                ["world_id"] = worldId,
                ["question"] = question,
                ["context"] = new JArray(new List<string>(context).ToArray()),
            };

            return RequestAsync<OWAskAnswer>("POST", "/account/ask", body: body, ct: ct);
        }

        /// <summary>
        /// POST /account/sort -- the user's unsorted notes, and the elements the app's own search found for them, sorted by
        /// the same proxy as <see cref="AskAsync"/> into rows to review (<see cref="OWSortRow"/>: <c>make</c>,
        /// <c>add_to</c>, <c>link</c>, <c>ask</c>). Nothing is written; the app applies what the user accepts.
        /// </summary>
        /// <param name="worldId">The world, sent as <c>world_id</c>.</param>
        /// <param name="notes">
        /// 1 to 8 notes, each up to 4,000 characters and 16,000 in all, at most four pictures, each uploaded into this world.
        /// A note of words alone goes as a string, one with a picture as <c>{text?, image_url}</c>.
        /// </param>
        /// <param name="slice">
        /// Up to 100 elements the notes may be about (id, lower-case type, name up to 200 characters, anchor up to 120);
        /// an anchor goes only when given. Every id in a row is from the slice: keel moves any other row to
        /// <see cref="OWSortAnswer.NotProposed"/> with its reason.
        /// </param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <exception cref="ArgumentException">
        /// No world, notes or slice, or a note that is null or holds neither words nor a picture; nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">
        /// <see cref="AskAsync"/>'s refusals, and a 422 naming the param past its limits.
        /// </exception>
        /// <remarks>
        /// The same account token and token rating as <see cref="AskAsync"/> (it spends the tokens it uses). Mirrors the
        /// TypeScript SDK's <c>sort</c> (4.18.0).
        /// </remarks>
        public Task<OWSortAnswer> SortAsync(
            string worldId, IEnumerable<OWSortNote> notes, IEnumerable<OWSortRef> slice, CancellationToken ct = default)
        {
            RequireAccountToken(nameof(SortAsync), "The proxy meters per account.");
            RequireWorldId(nameof(SortAsync), worldId);
            if (notes == null) throw new ArgumentNullException(nameof(notes), "SortAsync: the notes. Nothing was sent.");
            if (slice == null) throw new ArgumentNullException(nameof(slice), "SortAsync: the slice. Nothing was sent.");

            var sent = new JArray();
            foreach (var note in notes)
            {
                var json = note?.ToJson();
                if (json == null)
                {
                    throw new ArgumentException(
                        "SortAsync: each note is its words, a picture's image_url, or both. Nothing was sent.", nameof(notes));
                }

                sent.Add(json);
            }

            var rows = new JArray();
            foreach (var row in slice)
            {
                if (row == null) throw new ArgumentException("SortAsync: a slice row is null. Nothing was sent.", nameof(slice));
                rows.Add(OnlyGiven(row));
            }

            var body = new JObject { ["world_id"] = worldId, ["notes"] = sent, ["slice"] = rows };
            return RequestAsync<OWSortAnswer>("POST", "/account/sort", body: body, ct: ct);
        }

        /// <summary>
        /// GET /account/allowance -- the account's token rating, the one daily meter <see cref="AskAsync"/>,
        /// <see cref="SortAsync"/> and the Council spend from: the rating, what is used and left today, and when it resets
        /// (the next midnight UTC). Spends nothing.
        /// </summary>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="InvalidOperationException">This client's key is not an account token (<c>ow_a_</c>); nothing was sent.</exception>
        /// <remarks>An account token only, sent as its Bearer. Mirrors the TypeScript SDK's <c>allowance</c> (4.21.0).</remarks>
        public Task<OWAllowance> AllowanceAsync(CancellationToken ct = default)
        {
            RequireAccountToken(nameof(AllowanceAsync), "The allowance is per account.");
            return RequestAsync<OWAllowance>("GET", "/account/allowance", ct: ct);
        }

        /// <summary>A world's id, not null or empty: an <see cref="ArgumentException"/> naming the call, before sending.</summary>
        private static void RequireWorldId(string method, string worldId)
        {
            if (string.IsNullOrEmpty(worldId))
                throw new ArgumentException($"{method}: the world's id is required. Nothing was sent.", nameof(worldId));
        }

        /// <summary>
        /// A request model as a body of only the members given, in keel's spelling: a member left null is not sent, so
        /// keel's default holds.
        /// </summary>
        private static JObject OnlyGiven(object model)
            => JObject.FromObject(model, Newtonsoft.Json.JsonSerializer.Create(
                new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore }));

        /// <summary>
        /// An account route's own refusal: a client whose key is not an account token (<c>ow_a_</c>) is an
        /// <see cref="InvalidOperationException"/> naming the call, and nothing is sent.
        /// </summary>
        /// <param name="method">The call, as the message names it.</param>
        /// <param name="hint">A sentence after the key it holds (where such a key goes instead), or null.</param>
        private void RequireAccountToken(string method, string hint = null)
        {
            if (KeyKind == OWKeyKind.Account) return;

            throw new InvalidOperationException(
                $"{method} needs an account token (ow_a_); this client holds "
                + (_keyless ? "none" : $"a {KeyKind.ToString().ToLowerInvariant()} key") + "."
                + (hint == null ? "" : " " + hint) + " Nothing was sent.");
        }

        /// <summary>The rows of a <c>{data}</c> answer with no paging; an answer without them is an empty list.</summary>
        private static List<TRow> RowsOf<TRow>(JObject page)
            => (page?["data"] as JArray)?.ToObject<List<TRow>>(Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings))
                ?? new List<TRow>();

        // -- Sync -------------------------------------------------------------

        /// <summary>
        /// GET /changes -- the incremental sync feed.
        /// </summary>
        /// <param name="cursor">
        /// The previous page's <c>cursor</c>, passed back as is; null or empty starts from the
        /// beginning. It goes out as <c>since</c>: the element lists page with <c>cursor</c>, this
        /// feed takes <c>since</c>, and keel refuses any other parameter with a 422. A bare
        /// <c>change_seq</c> works for an owner or member key, but a guest key's feed answers it
        /// with 409 <c>resync_required</c>: persist the cursor, not a number.
        /// </param>
        /// <param name="headOnly">Cheap tip check: returns <c>head</c>, a <c>cursor</c> to follow from, and <c>epoch</c>, with no changes.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <param name="after">
        /// Only the changes made at or after this time (an upsert's <c>updated_at</c>, a delete's <c>deleted_at</c>), sent
        /// as <c>after</c> in UTC, to the microsecond (a finer fraction is cut, never rounded up past the time asked for).
        /// A filter, not a position: the order, <c>cursor</c>, <c>has_more</c> and <c>head</c> are as without it, so to
        /// read the whole window pass each page's cursor back with the same <paramref name="after"/> until
        /// <c>has_more</c> is false. "Since yesterday" by the clock; a kept cursor is still the exact "since my last
        /// visit". Null sends nothing. Mirrors the TypeScript SDK's <c>after</c> on <c>changes</c> (4.44.0).
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="after"/> with <paramref name="headOnly"/>: the tip read takes no filter. Nothing was sent.
        /// </exception>
        /// <remarks>
        /// <para>
        /// A page holds <see cref="PageSize"/> changes, at most 1000: the feed refuses a larger
        /// <c>limit</c> with a 422 where the element lists clamp it.
        /// </para>
        /// <para>
        /// The tip read is conditional: keel tags its answer with an ETag covering the head and the
        /// epoch, the client keeps the last one and sends it as <c>If-None-Match</c> on its next tip read, exactly as
        /// received (<c>W/</c> and quotes included, never compared). When nothing moved keel answers 304 with no
        /// body, refunded against the request budget, and this returns the last 200's head, cursor and epoch with
        /// <see cref="OWChangesPage.Unchanged"/> true. A poller reads the tip, waits when it is unchanged, and walks
        /// the feed when head or epoch moved past what it applied; the interval is the caller's, the client never
        /// polls on its own. The tag is per client instance. A 409 <c>resync_required</c> from any call here drops
        /// it, so the next tip read is a full 200. A 304 while no tag is held is an <see cref="OWApiError"/> with
        /// status 304: the client never makes up a head.
        /// </para>
        /// </remarks>
        public async Task<OWChangesPage> ChangesAsync(
            string cursor = null, bool headOnly = false, CancellationToken ct = default, DateTimeOffset? after = null)
        {
            if (headOnly && after.HasValue)
            {
                throw new ArgumentException(
                    "ChangesAsync: after filters a page of the feed; the tip read (headOnly) takes none. Nothing was sent.",
                    nameof(after));
            }

            var query = new List<string>();
            if (headOnly)
            {
                query.Add("head=true");
            }
            else
            {
                if (!string.IsNullOrEmpty(cursor)) query.Add("since=" + Uri.EscapeDataString(cursor));
                query.Add("limit=" + Math.Min(PageSize, MaxChangesLimit));
                if (after.HasValue)
                {
                    var at = after.Value.ToUniversalTime()
                        .ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFF'Z'", CultureInfo.InvariantCulture);
                    query.Add("after=" + Uri.EscapeDataString(at));
                }
            }

            // One reference, swapped whole, so a tip read racing another sees one poll's state or the other's.
            var held = headOnly ? _headPoll : null;

            OWChangesPage page;
            OWResponse response;
            try
            {
                (page, response) = await RequestWithResponseAsync<OWChangesPage>(
                    "GET", "/changes/", query: string.Join("&", query), ct: ct,
                    ifNoneMatch: held?.Tag, notModified: headOnly).ConfigureAwait(false);
            }
            catch (OWApiError e) when (e.IsResyncRequired)
            {
                _headPoll = null;
                throw;
            }

            if (!headOnly) return page;

            if (response.Status == 304)
            {
                if (held == null)
                {
                    throw new OWApiError((HttpStatusCode)304,
                        "GET /changes?head=true answered 304 Not Modified while nothing is held: no tip read has "
                        + "answered 200 on this client, so there is no head to return", rawBody: response.Body);
                }

                return new OWChangesPage { Cursor = held.Cursor, Head = held.Head, Epoch = held.Epoch, Unchanged = true };
            }

            var tag = ReadHeader(response, "ETag");
            _headPoll = page == null ? null : new HeadPoll
            {
                Tag = string.IsNullOrEmpty(tag) ? null : tag,
                Cursor = page.Cursor,
                Head = page.Head,
                Epoch = page.Epoch,
            };

            return page;
        }

        /// <summary>keel's cap on one <c>/changes</c> page (<c>core/changes.py</c>); over it is a 422.</summary>
        private const int MaxChangesLimit = 1000;

        /// <summary>The last tip read's ETag (null when it sent none) and the head, cursor and epoch its 200 answered.</summary>
        private sealed class HeadPoll
        {
            public string Tag;
            public string Cursor;
            public long Head;
            public long? Epoch;
        }

        private volatile HeadPoll _headPoll;

        // -- Core -------------------------------------------------------------

        /// <summary>
        /// Any route of keel's, on this client's own rules: the escape hatch for a route no typed call here makes. Answers
        /// the body as served (a <see cref="JToken"/>, its dates kept as the strings they were), or null for a 204 or an
        /// empty body.
        /// </summary>
        /// <param name="method"><c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>, in capitals.</param>
        /// <param name="path">The path under <see cref="BaseUrl"/>, starting with <c>/</c> (<c>/world/files</c>); its query goes in <see cref="OWRequestOptions.Query"/>.</param>
        /// <param name="options">The query, the body, and which of the client's rules the route asks for; null for none.</param>
        /// <param name="ct">Cancels the request.</param>
        /// <exception cref="ArgumentException">
        /// Another method; a path that is empty, does not start with <c>/</c> or carries a <c>?</c> or <c>#</c>; a body on a
        /// GET; a negative <see cref="OWRequestOptions.IfMatch"/>. Nothing was sent.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The client's own refusals, as every typed call has them: an account token on a route it does not send, or no key
        /// on a route that needs one. Nothing was sent.
        /// </exception>
        /// <exception cref="OWApiError">The server refused; a 202 on a <see cref="OWRequestOptions.Proposable"/> write is an <see cref="OWProposedError"/>.</exception>
        /// <exception cref="OWTransportError">The request never got an answer.</exception>
        /// <remarks>
        /// <para>
        /// What it adds is what every typed call adds: the key as <c>API-Key</c>, or an account token as its Bearer; the PIN
        /// only where keel checks it (any method but GET, <c>/me</c>, and every request of a legacy key); <c>world=</c> on a
        /// <see cref="OWRequestOptions.WorldRead"/> GET or an <see cref="OWAccountTokenRoute.World"/> route of a client with
        /// <see cref="World"/> set; <c>Content-Type</c> with a body; <c>X-OW-Agent</c> on an agent client's
        /// <see cref="OWRequestOptions.Write"/> and <c>X-OW-Client</c> on an <see cref="OWRequestOptions.ElementWrite"/>.
        /// An account token and a keyless client are refused here exactly where the typed calls refuse them.
        /// </para>
        /// <para>
        /// What it never does: clean the body. The typed writes leave the read-only keys out, send a null scalar string as
        /// <c>""</c> and refuse a known-of stub; this sends the body as given, so the caller owns its shape. Prefer the typed
        /// calls for elements. Mirrors the TypeScript SDK's <c>request</c> (public from 4.0.0), which does not apply
        /// <c>sanitizePayload</c> either.
        /// </para>
        /// </remarks>
        public Task<JToken> RequestAsync(
            string method, string path, OWRequestOptions options = null, CancellationToken ct = default)
        {
            if (Array.IndexOf(RequestMethods, method) < 0)
            {
                throw new ArgumentException(
                    $"RequestAsync: the method is one of {string.Join(", ", RequestMethods)}, in capitals. Nothing was sent.",
                    nameof(method));
            }

            if (string.IsNullOrEmpty(path) || path[0] != '/' || path.IndexOf('?') >= 0 || path.IndexOf('#') >= 0)
            {
                throw new ArgumentException(
                    "RequestAsync: the path is the route under BaseUrl, starting with '/', and its query goes in "
                    + "OWRequestOptions.Query. Nothing was sent.", nameof(path));
            }

            var o = options ?? new OWRequestOptions();
            if (o.Body != null && method == "GET")
                throw new ArgumentException("RequestAsync: a GET carries no body. Nothing was sent.", nameof(options));
            RefuseNegative(o.IfMatch);

            return RequestAsync<JToken>(method, path, query: o.Query, body: o.Body, idempotencyKey: o.IdempotencyKey,
                allowEmpty: true, write: o.Write && o.ElementWrite, ct: ct, ifMatch: o.IfMatch,
                named: o.ElementWrite && !o.Write, proposable: o.Proposable, worldRead: o.WorldRead,
                accountToken: o.AccountToken, auth: o.Auth, agentOnly: o.Write && !o.ElementWrite);
        }

        /// <summary>The methods <see cref="RequestAsync(string, string, OWRequestOptions, CancellationToken)"/> sends: keel's five.</summary>
        private static readonly string[] RequestMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

        private async Task<T> RequestAsync<T>(
            string method, string path,
            string query = null, JToken body = null, string idempotencyKey = null,
            bool allowEmpty = false, bool write = false, CancellationToken ct = default, long? ifMatch = null,
            bool named = false, bool proposable = false, bool worldRead = false,
            OWAccountTokenRoute accountToken = OWAccountTokenRoute.None, bool auth = true, bool agentOnly = false)
        {
            var result = await RequestWithResponseAsync<T>(
                method, path, query, body, idempotencyKey, allowEmpty, write, ct,
                ifMatch: ifMatch, named: named, proposable: proposable, worldRead: worldRead,
                accountToken: accountToken, auth: auth, agentOnly: agentOnly).ConfigureAwait(false);

            return result.Value;
        }

        /// <summary>
        /// As <see cref="RequestAsync{T}"/>, but hands back the raw response alongside the parsed
        /// body. Needed wherever a response HEADER carries meaning -- bulk's replay flag and the
        /// tip read's ETag -- and duplicating the auth/error path to reach them would be worse.
        /// </summary>
        /// <param name="write">An element write: <c>X-OW-Agent</c> (for an agent client) and <c>X-OW-Client</c>.</param>
        /// <param name="ifNoneMatch">A tag sent as <c>If-None-Match</c> exactly as given (the tip read).</param>
        /// <param name="notModified">A 304 is returned with a default value instead of thrown; the caller reads the status.</param>
        /// <param name="ifMatch">A <c>change_seq</c> sent as <c>If-Match</c>, quoted.</param>
        /// <param name="named">
        /// A write keel reads <c>X-OW-Client</c> on but not <c>X-OW-Agent</c> (propose and accept).
        /// </param>
        /// <param name="proposable">
        /// A write keel may answer 202 with <c>{proposal}</c> (a patch, a link edit): a 202 then throws an
        /// <see cref="OWProposedError"/> instead of returning a body that is not the element.
        /// </param>
        /// <param name="worldRead">
        /// A GET keel serves an account token, or no key, with <c>world=&lt;id&gt;</c> (element list and get, the world
        /// GET). On a client with <see cref="World"/> set, <c>world=&lt;id&gt;</c> is added to the query.
        /// </param>
        /// <param name="accountToken">
        /// A world route keel's document declares the account token on beside the world key (the lens routes): an account
        /// client sends it with its Bearer token. <see cref="OWAccountTokenRoute.Path"/>: the path names the world, nothing is
        /// added. <see cref="OWAccountTokenRoute.World"/>: <c>world=&lt;id&gt;</c> is added from <see cref="World"/>, and an
        /// account client without it is refused before sending. A world key sends as without it; a keyless client is
        /// still refused.
        /// </param>
        /// <param name="auth">
        /// False for a route keel serves with no credential (the health pulse): no key, PIN or Bearer is sent, and no
        /// client is refused for the key it holds.
        /// </param>
        /// <param name="agentOnly">
        /// A write keel's document lists <c>X-OW-Agent</c> on but not <c>X-OW-Client</c> (the file ticket): the agent's
        /// header on an agent client, and no client name.
        /// </param>
        private async Task<(T Value, OWResponse Response)> RequestWithResponseAsync<T>(
            string method, string path,
            string query = null, JToken body = null, string idempotencyKey = null,
            bool allowEmpty = false, bool write = false, CancellationToken ct = default,
            string ifNoneMatch = null, bool notModified = false, long? ifMatch = null,
            bool named = false, bool proposable = false, bool worldRead = false,
            OWAccountTokenRoute accountToken = OWAccountTokenRoute.None, bool auth = true, bool agentOnly = false)
        {
            // The one rule for what an account token or no key may send, read from the request itself (keel's
            // account reads, ruled 2026-10-26): an /account route, a GET marked as a world read, or a route marked as
            // one the account token is declared on (the lens routes). Anything else keel answers such a client with a
            // 401, so it is refused here and nothing is sent. Mirrors the TypeScript SDK's request() (4.88.0; the
            // account token's lens routes from 4.94.0).
            var reads = auth && worldRead && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
            var tokenRoute = auth && KeyKind == OWKeyKind.Account ? accountToken : OWAccountTokenRoute.None;

            if (auth && _keyless && !(reads && World != null))
            {
                throw new InvalidOperationException(
                    $"{method} {path} needs a key, and this client holds none: without one, only HealthAsync, "
                    + "SignInWithPasswordAsync, SignInWithOnlyWorlds and CompleteSignInAsync work (and OWJoin's PreviewAsync "
                    + $"and RedeemAsync, which take no client), and {WorldReads} with World set. Nothing was sent.");
            }

            if (auth && KeyKind == OWKeyKind.Account && !IsAccountRoute(path))
            {
                if (!reads && tokenRoute == OWAccountTokenRoute.None)
                {
                    throw new InvalidOperationException(
                        $"{method} {path} needs a world key: an account token sends the /account routes, "
                        + $"WorldLensesAsync, SetMyLensesAsync and GetMembersAsync, and with World set reads a world through {WorldReads} "
                        + "only. Nothing was sent.");
                }

                if (reads && World == null)
                {
                    throw new InvalidOperationException(
                        $"{method} {path}: an account token reads a world only with World set "
                        + $"(new OWClientConfig {{ ApiKey, World }}), through {WorldReads}. Nothing was sent.");
                }

                if (tokenRoute == OWAccountTokenRoute.World && World == null)
                {
                    throw new InvalidOperationException(
                        $"{method} {path}: an account token names the world it sends to with World: set it "
                        + "(new OWClientConfig { ApiKey, World }). Nothing was sent.");
                }
            }

            if ((reads || tokenRoute == OWAccountTokenRoute.World) && World != null)
            {
                var world = "world=" + Uri.EscapeDataString(World);
                query = string.IsNullOrEmpty(query) ? world : query + "&" + world;
            }

            var url = BaseUrl + path;
            if (!string.IsNullOrEmpty(query)) url += "?" + query;

            var headers = new Dictionary<string, string>();

            if (!auth || _keyless)
            {
                // A route served with no credential, or a keyless client, which reaches here otherwise only for a world
                // read with World set: no credential at all.
            }
            else if (OWKey.UsesBearer(KeyKind))
            {
                headers["Authorization"] = "Bearer " + _apiKey;
            }
            else
            {
                headers["API-Key"] = _apiKey;

                // The PIN only where keel checks it (Skeld's ruling, 2026-10-22): keel verifies a PIN
                // wherever it is sent (argon2, 64 MiB each), and scripts reading with it OOM-killed staging on a
                // cold cache (2026-10-22). Sent on every write (any method but GET), on GET /me, the connection
                // test, and on every request of a 10-digit legacy key, which needs it on reads of a
                // world with a PIN (v1 parity). Derived from the method and path, as the npm SDK does.
                var read = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
                var me = path == "/me/" || path == "/me";
                if (_apiPin != null && (!read || me || KeyKind == OWKeyKind.Legacy)) headers["API-Pin"] = _apiPin;
            }

            if (body != null) headers["Content-Type"] = "application/json";
            if (!string.IsNullOrEmpty(idempotencyKey)) headers["Idempotency-Key"] = idempotencyKey;

            // Presence is the declaration, the value is never stored. Opt-in, and
            // on the element writes and the file ticket alone, the operations keel's document declares it on.
            if (_agent && (write || agentOnly)) headers["X-OW-Agent"] = "1";

            // The client's own name, on the element writes and on propose and accept (where the
            // document lists X-OW-Client and not X-OW-Agent), never on a read nor on a decline or a withdrawal.
            if (write || named) headers["X-OW-Client"] = ClientName;

            if (ifNoneMatch != null) headers["If-None-Match"] = ifNoneMatch;
            if (ifMatch.HasValue) headers["If-Match"] = "\"" + ifMatch.Value.ToString(CultureInfo.InvariantCulture) + "\"";

            var request = new OWRequest
            {
                Method = method,
                Url = url,
                Headers = headers,
                Body = body?.ToString(Newtonsoft.Json.Formatting.None),
            };

            OWResponse response;
            try
            {
                response = await _transport.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // The request never got an answer. Distinct from an API rejection -- see OWTransportError.
                throw new OWTransportError($"OnlyWorlds request failed: {method} {path}", e);
            }

            // A 304 has no body: the caller that sent a tag reads the status it is handed.
            if (notModified && response.Status == 304) return (default, response);

            if (!response.IsSuccess) throw BuildError(response);

            if (proposable && response.Status == 202) throw Proposed(response, method, path);

            if (response.Status == 204 || allowEmpty)
            {
                var empty = string.IsNullOrWhiteSpace(response.Body)
                    ? default
                    : OWJson.Deserialize<T>(response.Body);
                return (empty, response);
            }

            return (OWJson.Deserialize<T>(response.Body), response);
        }

        /// <summary>
        /// The account routes, <c>/account</c> and everything under it, which an account token sends with or without
        /// <see cref="World"/>. A path that only starts with the word (<c>/accounts</c>) is not one.
        /// </summary>
        private static bool IsAccountRoute(string path)
            => path == "/account" || path.StartsWith("/account/", StringComparison.Ordinal)
               || path.StartsWith("/account?", StringComparison.Ordinal);

        /// <summary>
        /// A 202 on a write that may become a proposal: an <see cref="OWProposedError"/> carrying the row. A
        /// 202 whose body holds no proposal is a plain <see cref="OWApiError"/> with status 202: the client never hands
        /// back a body that is not the element, and never makes up a proposal.
        /// </summary>
        private static OWApiError Proposed(OWResponse response, string method, string path)
        {
            OWProposal proposal = null;
            try
            {
                if (OWJson.ParseObject(response.Body ?? "{}")["proposal"] is JObject row) proposal = row.ToObject<OWProposal>(Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings));
            }
            catch (Exception)
            {
                // Not JSON: no proposal, the plain error below.
            }

            if (proposal == null)
            {
                return new OWApiError((HttpStatusCode)202,
                    $"{method} {path} answered 202 without a proposal: the write was not applied, and the answer is not an element",
                    rawBody: response.Body);
            }

            return new OWProposedError(proposal,
                $"{method} {path} became a proposal (202): this world takes an agent seat's writes as proposals, so the "
                + "element did not change. Proposal is the row; the owner or a co-builder accepts or declines it.",
                response.Body);
        }

        /// <summary>keel's error envelope as a typed error; <see cref="OWJoin"/> shares it.</summary>
        internal static OWApiError BuildError(OWResponse response)
        {
            var status = (HttpStatusCode)response.Status;
            var retryAfter = OWApiError.ParseRetryAfter(ReadHeader(response, "Retry-After"), DateTimeOffset.UtcNow);

            // A non-2xx is not guaranteed to carry the envelope -- a proxy or gateway can return
            // HTML. Parse defensively so a 502 surfaces as a clean error rather than a JSON
            // exception thrown from the error path itself.
            try
            {
                var envelope = OWJson.ParseObject(response.Body ?? "{}");
                var error = envelope["error"] as JObject ?? envelope;

                // The upload host refuses with {"error": "<code>"}, not keel's envelope: the string is the code, as the
                // TypeScript SDK's parseErrorEnvelope reads it.
                var code = error["code"]?.ToString()
                    ?? (envelope["error"]?.Type == JTokenType.String ? envelope["error"].ToString() : null);

                return new OWApiError(
                    status,
                    error["message"]?.ToString(),
                    error["type"]?.ToString(),
                    code,
                    error["param"]?.ToString(),
                    error["doc_url"]?.ToString(),
                    response.Body,
                    retryAfter);
            }
            catch (Exception)
            {
                return new OWApiError(status, Truncate(response.Body), rawBody: response.Body, retryAfter: retryAfter);
            }
        }

        /// <summary>A response header by name, case-insensitively (keel sends them lowercase), or null.</summary>
        private static string ReadHeader(OWResponse response, string name)
        {
            if (response.Headers == null) return null;

            foreach (var pair in response.Headers)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }

            return null;
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Length <= 200 ? s : s.Substring(0, 200) + "...";
        }

        /// <summary>
        /// A known-of stub (<see cref="OWPayload.IsKnownOf"/>) is an <see cref="ArgumentException"/> before
        /// anything is sent: no create, PATCH or bulk item is made from one (folder format §4.4, rule 3).
        /// </summary>
        /// <remarks>
        /// A stub's empty fields are not the element's values. Stripping the marker and sending the rest
        /// would turn keel's 422 into a write that blanks the element (a PATCH clears every field it names)
        /// for any key that may write it, such as an owner pushing a copy a player made. The npm SDK
        /// (<c>refuseKnownOf</c>) and the Python package (<c>_refuse_known_of</c>) refuse the same bodies
        /// with the same words.
        /// </remarks>
        private static void RefuseKnownOf(JObject body, string where, string paramName)
        {
            if (!OWPayload.IsKnownOf(body)) return;

            throw new ArgumentException(
                $"{where}: this body is a known-of stub (known_of: true), not the element: its empty fields are not "
                + "its values, and writing it would blank them. Write from a whole copy, or send only the fields you "
                + "mean to change.",
                paramName);
        }

        /// <summary>
        /// Removes the six read-only fields (<see cref="OWPayload.ReadOnlyFields"/>), and sends a null scalar string as <c>""</c>.
        /// </summary>
        /// <param name="payload">The body about to be sent. Changed in place.</param>
        /// <param name="type">The element type slug, which says which keys are scalar strings.</param>
        /// <remarks>
        /// <para>
        /// LAW, inherited from the npm SDK and worth restating: this is a BLACKLIST and must never
        /// become a whitelist. Namespaced extension fields (<c>x_*</c>) MUST pass through writes
        /// untouched -- every tool that round-trips its own state through other tools depends on it.
        /// A whitelist would silently strip them and corrupt cross-tool state.
        /// </para>
        /// <para>
        /// rulings.yaml <c>string-empty-is-unset</c>: for a scalar string, <c>""</c> IS the wire's
        /// unset and keel has no null to store, so a client MUST NOT send a distinction the wire
        /// cannot carry. A <c>null</c> left in a model (read from a folder file, or set by a caller)
        /// goes out as <c>""</c>. Only keys the schema declares as scalar strings are touched
        /// (<see cref="OWElementTypes.IsStringField"/>): a single link's <c>null</c> is its unset and
        /// stays, and an <c>x_*</c> value is another tool's and is never reinterpreted. An ABSENT
        /// key stays absent -- on a PATCH that means "leave it alone", which is not a value.
        /// </para>
        /// </remarks>
        private static JObject Sanitize(JObject payload, string type)
        {
            if (payload == null) return null;

            foreach (var field in OWPayload.ReadOnlyFields)
            {
                payload.Remove(field);
            }

            foreach (var property in payload.Properties())
            {
                if (property.Value.Type == JTokenType.Null && OWElementTypes.IsStringField(type, property.Name))
                {
                    property.Value = string.Empty;
                }
            }

            return payload;
        }
    }
}
