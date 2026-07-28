using System;
using System.Net;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A structured error returned by the OnlyWorlds v2 API.
    /// </summary>
    /// <remarks>
    /// The API returns an RFC-7807-style envelope: <c>{type, code, message, param, doc_url}</c>.
    /// Surfacing those fields typed -- rather than as a flat message string -- is what lets calling
    /// code branch on <see cref="Code"/> and point a user at <see cref="DocUrl"/>.
    ///
    /// Distinct from <see cref="OWTransportError"/>: this means the server answered and said no.
    /// A DNS failure or a dropped connection is a different problem with different remedies, and
    /// conflating them makes "offline" indistinguishable from "your key is wrong".
    /// </remarks>
    public sealed class OWApiError : Exception
    {
        /// <summary>HTTP status code.</summary>
        public HttpStatusCode Status { get; }

        /// <summary>Numeric HTTP status, for callers that prefer the raw int.</summary>
        public int StatusCode => (int)Status;

        /// <summary>Broad error class, e.g. <c>invalid_request</c>.</summary>
        public string Type { get; }

        /// <summary>Specific machine-readable code, e.g. <c>invalid_link</c>.</summary>
        public string Code { get; }

        /// <summary>The offending field, when the error is about one.</summary>
        public string Param { get; }

        /// <summary>Documentation link for this error code.</summary>
        public string DocUrl { get; }

        /// <summary>The raw response body, for diagnostics the envelope did not model.</summary>
        public string RawBody { get; }

        public bool IsValidationError => StatusCode == 422 || StatusCode == 400;

        public bool IsAuthError => StatusCode == 401 || StatusCode == 403;

        public bool IsNotFound => StatusCode == 404;

        /// <summary>
        /// True when retrying the same request unchanged could plausibly succeed -- server faults
        /// and rate limits. A 4xx other than 429 will fail again identically.
        /// </summary>
        public bool IsRetryable => StatusCode == 429 || StatusCode >= 500;

        public OWApiError(
            HttpStatusCode status,
            string message,
            string type = null,
            string code = null,
            string param = null,
            string docUrl = null,
            string rawBody = null)
            : base(BuildMessage(status, message, code, param))
        {
            Status = status;
            Type = type;
            Code = code;
            Param = param;
            DocUrl = docUrl;
            RawBody = rawBody;
        }

        private static string BuildMessage(HttpStatusCode status, string message, string code, string param)
        {
            var text = $"OnlyWorlds API {(int)status}";
            if (!string.IsNullOrEmpty(code)) text += $" [{code}]";
            if (!string.IsNullOrEmpty(param)) text += $" (field: {param})";
            if (!string.IsNullOrEmpty(message)) text += $": {message}";
            return text;
        }
    }

    /// <summary>
    /// A network-layer failure: the request never got an answer.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <see cref="OWApiError"/>. "The server rejected this" and "the server was
    /// unreachable" want different handling -- the first is a bug in the request, the second is a
    /// condition to retry or surface as offline.
    /// </remarks>
    public sealed class OWTransportError : Exception
    {
        public OWTransportError(string message, Exception inner = null) : base(message, inner) { }
    }
}
