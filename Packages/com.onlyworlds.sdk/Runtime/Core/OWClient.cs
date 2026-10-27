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
        public string ApiKey;

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
        /// Declare this client an AI agent: every element write (create, patch, delete, link edit,
        /// bulk) carries <c>X-OW-Agent</c>, and keel records it as agent-made
        /// (<see cref="OWElement.ChangedVia"/> reads <see cref="OWWriteDoor.Declared"/>; keel stores that
        /// the header was there, never its value). Off by default, and it must stay off in a tool
        /// people use: it describes the caller, so only the caller can set it. An agent seat's key is
        /// recorded as an agent without it.
        /// </summary>
        public bool Agent;

        /// <summary>
        /// This client's own name, sent as <c>X-OW-Client</c> on every element write (create, patch, delete, link
        /// edit, bulk) and on no read. keel stores it on the element as
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

        /// <summary>The name sent as <c>X-OW-Client</c> when <see cref="OWClientConfig.Client"/> is null.</summary>
        public const string DefaultClientName = "unity-sdk";

        public string BaseUrl { get; }

        /// <summary>The name this client sends as <c>X-OW-Client</c> on element writes.</summary>
        public string ClientName { get; }
        public int PageSize { get; }
        public OWKeyKind KeyKind { get; }

        public OWClient(OWClientConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(config.ApiKey))
                throw new ArgumentException("An API key is required.", nameof(config));
            if (config.Transport == null)
                throw new ArgumentException("A transport is required.", nameof(config));

            _apiKey = config.ApiKey;
            _apiPin = string.IsNullOrEmpty(config.ApiPin) ? null : config.ApiPin;
            _transport = config.Transport;
            _agent = config.Agent;

            BaseUrl = (config.BaseUrl ?? "https://www.onlyworlds.com/api/v2").TrimEnd('/');
            PageSize = config.PageSize > 0 ? config.PageSize : 100;
            KeyKind = OWKey.DetectKind(config.ApiKey);
            ClientName = ClientNameOf(config.Client);
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

        // -- World meta -------------------------------------------------------

        /// <summary>
        /// GET /world -- world metadata.
        /// </summary>
        /// <remarks>
        /// World-meta edits do NOT appear in <c>/changes</c> and do not bump <c>change_seq</c>.
        /// Freshness of world meta is only knowable by polling this and comparing
        /// <c>updated_at</c>.
        /// </remarks>
        public Task<JObject> GetWorldAsync(CancellationToken ct = default)
            => RequestAsync<JObject>("GET", "/world/", ct: ct);

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
        /// GET /me -- who this key is: <c>{world, scope, kind, role, membership, character, name?}</c>. <c>kind</c> is
        /// <c>owner</c> for an owner-minted or classic key, else the membership's (<c>person</c>, <c>agent</c>,
        /// <c>player</c>); <c>role</c> is <c>owner</c>, <c>co_builder</c>, <c>contributor</c> or <c>guest</c>;
        /// <c>character</c> is the Character id the key acts as, or null (also when it is hidden from this key).
        /// </summary>
        public Task<JObject> GetMeAsync(CancellationToken ct = default)
            => RequestAsync<JObject>("GET", "/me/", ct: ct);

        // -- Element reads ----------------------------------------------------

        /// <summary>GET /{type}/ -- one cursor page.</summary>
        public Task<OWPage<T>> ListAsync<T>(string type, OWListParams p = default, CancellationToken ct = default)
        {
            var query = new List<string>
            {
                "limit=" + (p.Limit ?? PageSize),
            };

            if (!string.IsNullOrEmpty(p.Cursor)) query.Add("cursor=" + Uri.EscapeDataString(p.Cursor));
            if (p.Fields != null && p.Fields.Length > 0)
                query.Add("fields=" + Uri.EscapeDataString(string.Join(",", p.Fields)));
            if (!string.IsNullOrEmpty(p.Name)) query.Add("name=" + Uri.EscapeDataString(p.Name));
            if (!string.IsNullOrEmpty(p.NameContains))
                query.Add("name__icontains=" + Uri.EscapeDataString(p.NameContains));
            if (!string.IsNullOrEmpty(p.Supertype)) query.Add("supertype=" + Uri.EscapeDataString(p.Supertype));
            if (!string.IsNullOrEmpty(p.Subtype)) query.Add("subtype=" + Uri.EscapeDataString(p.Subtype));

            return RequestAsync<OWPage<T>>("GET", $"/{type}/", query: string.Join("&", query), ct: ct);
        }

        /// <summary>
        /// Walks every page of a type. Drives from <c>has_more</c>, never from page size.
        /// </summary>
        /// <remarks>
        /// The server silently clamps limit at 1000, so a page shorter than requested is NOT a
        /// signal that iteration is done. Treating it as one loses elements with no error.
        /// </remarks>
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
                cursor = page.NextCursor;
            }
            while (true);

            return all;
        }

        /// <summary>GET /{type}/{id}/</summary>
        public Task<T> GetAsync<T>(string type, string id, CancellationToken ct = default)
            => RequestAsync<T>("GET", $"/{type}/{id}/", ct: ct);

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
        /// <exception cref="ArgumentException">The world has no name; nothing was sent.</exception>
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
        /// <c>createAccountWorld</c> (4.66.0), its refusal of any other key kind included.
        /// </para>
        /// </remarks>
        public async Task<OWAccountWorldCreated> CreateAccountWorldAsync(OWAccountWorldCreate world, CancellationToken ct = default)
        {
            if (KeyKind != OWKeyKind.Account)
            {
                throw new InvalidOperationException(
                    "CreateAccountWorldAsync needs an account token (ow_a_); this client holds a "
                    + $"{KeyKind.ToString().ToLowerInvariant()} key. Nothing was sent.");
            }

            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Name == null)
            {
                throw new ArgumentException(
                    "CreateAccountWorldAsync: Name is required, the world's name. Nothing was sent.", nameof(world));
            }

            // Only the members given: keel reads a null and a missing key alike here, so none is sent.
            var body = JObject.FromObject(world, Newtonsoft.Json.JsonSerializer.Create(
                new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore }));

            return await RequestAsync<OWAccountWorldCreated>("POST", "/account/worlds", body: body, ct: ct).ConfigureAwait(false);
        }

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
        public async Task<OWChangesPage> ChangesAsync(string cursor = null, bool headOnly = false, CancellationToken ct = default)
        {
            var query = new List<string>();
            if (headOnly)
            {
                query.Add("head=true");
            }
            else
            {
                if (!string.IsNullOrEmpty(cursor)) query.Add("since=" + Uri.EscapeDataString(cursor));
                query.Add("limit=" + Math.Min(PageSize, MaxChangesLimit));
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

        private async Task<T> RequestAsync<T>(
            string method, string path,
            string query = null, JObject body = null, string idempotencyKey = null,
            bool allowEmpty = false, bool write = false, CancellationToken ct = default, long? ifMatch = null,
            bool named = false, bool proposable = false)
        {
            var result = await RequestWithResponseAsync<T>(
                method, path, query, body, idempotencyKey, allowEmpty, write, ct,
                ifMatch: ifMatch, named: named, proposable: proposable).ConfigureAwait(false);

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
        private async Task<(T Value, OWResponse Response)> RequestWithResponseAsync<T>(
            string method, string path,
            string query = null, JObject body = null, string idempotencyKey = null,
            bool allowEmpty = false, bool write = false, CancellationToken ct = default,
            string ifNoneMatch = null, bool notModified = false, long? ifMatch = null,
            bool named = false, bool proposable = false)
        {
            var url = BaseUrl + path;
            if (!string.IsNullOrEmpty(query)) url += "?" + query;

            var headers = new Dictionary<string, string>();

            if (OWKey.UsesBearer(KeyKind))
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
            // on the element writes alone, the operations keel's document declares it on.
            if (_agent && write) headers["X-OW-Agent"] = "1";

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

                return new OWApiError(
                    status,
                    error["message"]?.ToString(),
                    error["type"]?.ToString(),
                    error["code"]?.ToString(),
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
