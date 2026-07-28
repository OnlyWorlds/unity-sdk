using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OnlyWorlds.Sdk
{
    /// <summary>A request the client wants sent.</summary>
    public struct OWRequest
    {
        public string Method;
        public string Url;
        public Dictionary<string, string> Headers;

        /// <summary>Serialized JSON body, or null for a bodiless request.</summary>
        public string Body;
    }

    /// <summary>What came back.</summary>
    public struct OWResponse
    {
        public int Status;
        public string Body;
        public Dictionary<string, string> Headers;

        public bool IsSuccess => Status >= 200 && Status < 300;

        public string Header(string name)
        {
            if (Headers == null) return null;

            // HTTP headers are case-insensitive, and the wire sends some of these lowercase
            // (Idempotent-Replay arrives lowercase per the npm SDK's fixtures). A case-sensitive
            // lookup would silently miss it and report every replay as a fresh write.
            foreach (var pair in Headers)
            {
                if (string.Equals(pair.Key, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The seam between the client's wire logic and however bytes actually move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists so the client can be tested without a network. Contract tests assert on the REQUEST
    /// the client would have sent -- that PATCH strips the five read-only fields, that an account
    /// key uses Bearer while a world key uses API-Key/API-Pin, that a create mints a UUID. Those
    /// are the assertions that catch wire drift, and they must not depend on a live server.
    /// </para>
    /// <para>
    /// It is also the platform seam: <c>UnityWebRequest</c> for runtime/WebGL/mobile, potentially
    /// something else in the editor. Keeping it behind an interface means the wire logic is written
    /// once and neither implementation can quietly diverge from it.
    /// </para>
    /// </remarks>
    public interface IOWTransport
    {
        Task<OWResponse> SendAsync(OWRequest request, CancellationToken cancellationToken = default);
    }
}
