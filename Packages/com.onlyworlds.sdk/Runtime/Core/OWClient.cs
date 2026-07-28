using System;
using System.Collections.Generic;
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
        public string ApiPin;
        public string BaseUrl = "https://www.onlyworlds.com/api/v2";

        /// <summary>Page size. The server silently clamps at 1000.</summary>
        public int PageSize = 100;

        public IOWTransport Transport;
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

        public string BaseUrl { get; }
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

            BaseUrl = (config.BaseUrl ?? "https://www.onlyworlds.com/api/v2").TrimEnd('/');
            PageSize = config.PageSize > 0 ? config.PageSize : 100;
            KeyKind = OWKey.DetectKind(config.ApiKey);
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

                var page = await ListAsync<T>(type, new OWListParams
                {
                    Limit = p.Limit,
                    Cursor = cursor,
                    Fields = p.Fields,
                }, ct).ConfigureAwait(false);

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
        /// POST /{type}/ -- creates, minting a client-side UUID when the payload has none.
        /// </summary>
        /// <remarks>
        /// The client mints the id so a retry is structurally safe: the same id plus the same
        /// Idempotency-Key means a replayed request cannot create a duplicate.
        /// </remarks>
        public Task<T> CreateAsync<T>(string type, object element, string idempotencyKey = null, CancellationToken ct = default)
        {
            var body = Sanitize(JObject.FromObject(element, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings)));

            if (body["id"] == null || string.IsNullOrEmpty(body["id"].ToString()))
            {
                body["id"] = Guid.NewGuid().ToString();
            }

            return RequestAsync<T>("POST", $"/{type}/", body: body,
                idempotencyKey: idempotencyKey ?? body["id"].ToString(), ct: ct);
        }

        /// <summary>
        /// PATCH /{type}/{id}/ -- partial update.
        /// </summary>
        /// <remarks>
        /// DESTRUCTIVE ON SENT FIELDS. Whatever is in the payload replaces what the server holds,
        /// so send only what changed. Never PATCH a link array to add one item -- that replaces the
        /// whole array; use <see cref="EditLinksAsync"/>.
        /// </remarks>
        public Task<T> PatchAsync<T>(string type, string id, object partial, CancellationToken ct = default)
            => RequestAsync<T>("PATCH", $"/{type}/{id}/",
                body: Sanitize(JObject.FromObject(partial, Newtonsoft.Json.JsonSerializer.Create(OWJson.Settings))), ct: ct);

        /// <summary>DELETE /{type}/{id}/</summary>
        public Task DeleteAsync(string type, string id, CancellationToken ct = default)
            => RequestAsync<object>("DELETE", $"/{type}/{id}/", allowEmpty: true, ct: ct);

        /// <summary>
        /// Atomic add/remove on a relationship, server-side.
        /// </summary>
        /// <remarks>
        /// The ONLY correct way to modify a link array. A PATCH carrying a link field replaces it
        /// wholesale, so two clients each adding one item means the second silently erases the
        /// first's addition.
        /// </remarks>
        public Task<T> EditLinksAsync<T>(
            string type, string id, string field,
            IEnumerable<string> add = null, IEnumerable<string> remove = null,
            CancellationToken ct = default)
        {
            var body = new JObject { ["field"] = field };
            if (add != null) body["add"] = new JArray(add);
            if (remove != null) body["remove"] = new JArray(remove);

            return RequestAsync<T>("POST", $"/{type}/{id}/editLinks/", body: body, ct: ct);
        }

        // -- Sync -------------------------------------------------------------

        /// <summary>
        /// GET /changes -- the incremental sync feed.
        /// </summary>
        /// <param name="cursor">Persisted cursor; null or empty starts from the beginning.</param>
        /// <param name="headOnly">Cheap tip check: returns <c>head</c> without the change bodies.</param>
        public Task<OWChangesPage> ChangesAsync(string cursor = null, bool headOnly = false, CancellationToken ct = default)
        {
            var query = new List<string>();
            if (!string.IsNullOrEmpty(cursor)) query.Add("cursor=" + Uri.EscapeDataString(cursor));
            if (headOnly) query.Add("head=true");

            return RequestAsync<OWChangesPage>("GET", "/changes/",
                query: query.Count > 0 ? string.Join("&", query) : null, ct: ct);
        }

        // -- Core -------------------------------------------------------------

        private async Task<T> RequestAsync<T>(
            string method, string path,
            string query = null, JObject body = null, string idempotencyKey = null,
            bool allowEmpty = false, CancellationToken ct = default)
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
                if (_apiPin != null) headers["API-Pin"] = _apiPin;
            }

            if (body != null) headers["Content-Type"] = "application/json";
            if (!string.IsNullOrEmpty(idempotencyKey)) headers["Idempotency-Key"] = idempotencyKey;

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

            if (!response.IsSuccess) throw BuildError(response);

            if (response.Status == 204 || allowEmpty)
            {
                return string.IsNullOrWhiteSpace(response.Body) ? default : OWJson.Deserialize<T>(response.Body);
            }

            return OWJson.Deserialize<T>(response.Body);
        }

        private static OWApiError BuildError(OWResponse response)
        {
            var status = (HttpStatusCode)response.Status;

            // A non-2xx is not guaranteed to carry the envelope -- a proxy or gateway can return
            // HTML. Parse defensively so a 502 surfaces as a clean error rather than a JSON
            // exception thrown from the error path itself.
            try
            {
                var envelope = JObject.Parse(response.Body ?? "{}");
                var error = envelope["error"] as JObject ?? envelope;

                return new OWApiError(
                    status,
                    error["message"]?.ToString(),
                    error["type"]?.ToString(),
                    error["code"]?.ToString(),
                    error["param"]?.ToString(),
                    error["doc_url"]?.ToString(),
                    response.Body);
            }
            catch (Exception)
            {
                return new OWApiError(status, Truncate(response.Body), rawBody: response.Body);
            }
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Length <= 200 ? s : s.Substring(0, 200) + "...";
        }

        /// <summary>
        /// Removes the five fields the server owns.
        /// </summary>
        /// <remarks>
        /// LAW, inherited from the npm SDK and worth restating: this is a BLACKLIST and must never
        /// become a whitelist. Namespaced extension fields (<c>x_*</c>) MUST pass through writes
        /// untouched -- every tool that round-trips its own state through other tools depends on it.
        /// A whitelist would silently strip them and corrupt cross-tool state.
        /// </remarks>
        private static JObject Sanitize(JObject payload)
        {
            if (payload == null) return null;

            foreach (var field in OWPayload.ReadOnlyFields)
            {
                payload.Remove(field);
            }

            return payload;
        }
    }
}
