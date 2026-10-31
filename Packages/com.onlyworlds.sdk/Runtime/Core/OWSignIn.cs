// Signing in: an account token (ow_a_) from a password (OWClient.SignInWithPasswordAsync), or from Sign in with
// OnlyWorlds, OAuth 2.1's authorization code with PKCE (S256) for public clients (OWClient.SignInWithOnlyWorlds, then
// OWClient.CompleteSignInAsync). The SDK keeps no token: the caller does. Mirrors the TypeScript SDK's sign-in.ts and
// oauth.ts (4.31.0, 4.46.0).

using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>Why a sign-in was refused: <see cref="OWSignInError.Reason"/>'s words, the TypeScript SDK's <c>OwSignInReason</c>.</summary>
    public static class OWSignInReason
    {
        /// <summary>A wrong email, username or password.</summary>
        public const string InvalidCredentials = "invalid_credentials";

        /// <summary><see cref="OWClient.CompleteSignInAsync"/>: keel's 400, the code used, expired, or another tool's. Start again.</summary>
        public const string InvalidGrant = "invalid_grant";

        /// <summary>
        /// <see cref="OWClient.CompleteSignInAsync"/>: no sign-in is pending, the return carries another state or none, or
        /// it reached another page than the registered return address. Nothing was sent.
        /// </summary>
        public const string StateMismatch = "state_mismatch";

        /// <summary>The account's email address is not confirmed yet: the link is in the mail onlyworlds.com sent.</summary>
        public const string EmailUnverified = "email_unverified";

        /// <summary>Too many attempts; <see cref="OWSignInError.RetryAfter"/> when the server sent it.</summary>
        public const string RateLimited = "rate_limited";

        /// <summary>The server was not reached.</summary>
        public const string Offline = "offline";

        /// <summary>The password was right, but no token was made; the session was still ended.</summary>
        public const string MintFailed = "mint_failed";

        /// <summary>Any other refusal; the message is the server's when it gave one that holds no secret.</summary>
        public const string Failed = "failed";
    }

    /// <summary>
    /// A refused sign-in (the TypeScript SDK's <c>OwSignInError</c>). <see cref="Reason"/> is what to tell the user;
    /// <see cref="Status"/> and <see cref="Code"/> are the server's answer when there was one.
    /// </summary>
    /// <remarks>
    /// No password, session, PKCE verifier, code or token is ever in its message or in any property, and it carries no
    /// inner exception: a transport's failure can carry the request, and the request holds the secret. Text from the
    /// server that holds one is replaced by the SDK's own words, and a code that holds one is left out.
    /// </remarks>
    public sealed class OWSignInError : Exception
    {
        /// <summary>One of <see cref="OWSignInReason"/>'s words.</summary>
        public string Reason { get; }

        /// <summary>The HTTP status of the refusing answer; null when nothing came back (<see cref="OWSignInReason.Offline"/>) or nothing was sent.</summary>
        public int? Status { get; }

        /// <summary>The server's error code, when its answer carried one (<c>email_password_mismatch</c>, <c>invalid_grant</c>).</summary>
        public string Code { get; }

        /// <summary>Seconds to wait before trying again, from <c>Retry-After</c>.</summary>
        public int? RetryAfter { get; }

        public OWSignInError(string reason, string message, int? status = null, string code = null, int? retryAfter = null)
            : base(message)
        {
            Reason = reason;
            Status = status;
            Code = code;
            RetryAfter = retryAfter;
        }
    }

    /// <summary>
    /// <see cref="OWClient.SignInWithPasswordAsync"/>'s answer (npm's <c>OwSignedIn</c>): the new account token, shown
    /// once, and its row on the account's tokens page (POST /account/tokens' 201).
    /// </summary>
    [Serializable]
    public class OWSignedIn
    {
        /// <summary>The account token (<c>ow_a_</c>): keep it, it is the only copy. A credential: keep it out of logs.</summary>
        [JsonProperty("token")]
        public string Token;

        /// <summary>The token's id, for revoking it (<c>DELETE /account/tokens/{token_id}</c>); empty when the server sent none.</summary>
        [JsonProperty("id")]
        public string Id;

        /// <summary>The name the account's tokens page lists it under, as the server made it.</summary>
        [JsonProperty("name")]
        public string Name;

        /// <summary>Its last four characters, as the account's tokens page shows them.</summary>
        [JsonProperty("last4")]
        public string Last4;
    }

    /// <summary>
    /// A sign-in begun with <see cref="OWClient.SignInWithOnlyWorlds"/> and not yet completed (npm's
    /// <c>OwSignInBegun</c> and the record it keeps in storage): open <see cref="Url"/> in the system browser, keep this
    /// until the return reaches the app, then hand both to <see cref="OWClient.CompleteSignInAsync"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Verifier"/> is a secret: it proves the return's code is this sign-in's. Keep the record in memory, or
    /// wherever the app keeps a credential while the browser is open, never in a log; it is spent once a return carries
    /// its <see cref="State"/> (<see cref="Pending"/> false), whatever happens next.
    /// </remarks>
    [Serializable]
    public class OWSignInBegun
    {
        /// <summary>The authorize URL on the site (<c>https://www.onlyworlds.com/signin/authorize?...</c>), carrying the challenge, never the verifier.</summary>
        public string Url;

        /// <summary>The state the return must carry back, 32 random bytes in base64url.</summary>
        public string State;

        /// <summary>The <c>client_id</c> keel's registry lists the tool under.</summary>
        public string ClientId;

        /// <summary>The registered return address, as given.</summary>
        public string RedirectUri;

        /// <summary>The PKCE verifier, 32 random bytes in base64url (43 characters). A secret.</summary>
        public string Verifier;

        /// <summary>True until a return carries this sign-in's state; a return with another state leaves it pending.</summary>
        public bool Pending = true;
    }

    /// <summary>
    /// <see cref="OWClient.CompleteSignInAsync"/>'s answer (npm's <c>OwSignInResult</c>): the account token, shown only
    /// here, from POST /auth/token's 200.
    /// </summary>
    [Serializable]
    public class OWSignInResult
    {
        /// <summary>An <c>ow_a_</c> account token: keep it, it is the only copy (the SDK stores none).</summary>
        [JsonProperty("token")]
        public string Token;

        /// <summary>
        /// The token's id: a tool done with the token revokes it with <c>DELETE /account/tokens/{token_id}</c>, sending the
        /// token itself as the Bearer. Null from a server that does not send it.
        /// </summary>
        [JsonProperty("token_id")]
        public string TokenId;

        /// <summary>
        /// When the token stops working (ISO 8601, as served), for a tool whose registration asks for a lifetime; null for
        /// one with none, whose token works until it is revoked, and from a server that does not send it.
        /// </summary>
        [JsonProperty("expires_at")]
        public string ExpiresAt;

        /// <summary><c>bearer</c>.</summary>
        [JsonProperty("token_type")]
        public string TokenType;

        [JsonProperty("account")]
        public OWSignInAccount Account;
    }

    /// <summary>The account a sign-in's token belongs to.</summary>
    [Serializable]
    public class OWSignInAccount
    {
        [JsonProperty("id")]
        public string Id;

        /// <summary>The account's username; empty when the server sent none.</summary>
        [JsonProperty("username")]
        public string Username;
    }

    /// <summary>
    /// Sign in with OnlyWorlds' pieces that send nothing: PKCE (RFC 7636) and the return addresses keel takes at
    /// authorize. The TypeScript SDK exports the first two as <c>base64url</c> and <c>pkceChallenge</c>.
    /// </summary>
    public static class OWSignIn
    {
        /// <summary>Base64url without padding (RFC 4648 §5), as PKCE wants it.</summary>
        public static string Base64Url(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>The S256 code challenge of a verifier (RFC 7636 §4.2): base64url of its SHA-256.</summary>
        public static string PkceChallenge(string verifier)
        {
            if (verifier == null) throw new ArgumentNullException(nameof(verifier));
            using (var sha = SHA256.Create())
            {
                return Base64Url(sha.ComputeHash(Encoding.UTF8.GetBytes(verifier)));
            }
        }

        /// <summary>Random bytes in base64url: a state or a verifier.</summary>
        internal static string RandomToken(int bytes)
        {
            var buffer = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buffer);
            return Base64Url(buffer);
        }

        // The return addresses keel takes at authorize (its accounts/signin.py: uri_problem, native_uri_problem and
        // return_matches), as the tool presents them. A native app's (RFC 8252) are a loopback literal with the port the
        // app bound (registered with none, any port matching) and a custom scheme matched exactly. Whether the client is
        // registered as native is keel's to check: the SDK does not read the client list.
        private static readonly string[] LoopbackHosts = { "127.0.0.1", "[::1]" };
        private static readonly string[] RefusedSchemes = { "http", "https", "javascript", "data", "file" };

        /// <summary>Why a presented return address is not one keel can match, or null. The TypeScript SDK's words.</summary>
        internal static string RedirectProblem(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return "is not a string";
            foreach (var c in uri)
            {
                if (c < 0x21 || c > 0x7e) return "carries whitespace or a character outside printable ASCII";
            }

            if (uri.Contains("*")) return "carries a wildcard: a return address is exact";
            if (uri.Contains("#")) return "carries a fragment";
            if (uri.Contains("?")) return "carries a query: the code and state are the only query";
            var sep = uri.IndexOf("://", StringComparison.Ordinal);
            if (sep < 0) return "is not <scheme>://<path>";

            var scheme = uri.Substring(0, sep);
            var rest = uri.Substring(sep + 3);
            var slash = rest.IndexOf('/');
            var netloc = slash < 0 ? rest : rest.Substring(0, slash);
            var path = slash < 0 ? "" : rest.Substring(slash);

            if (scheme == "https")
            {
                if (netloc.Contains("@")) return "carries userinfo";
                var host = netloc.Split(':')[0];
                if (host == "" || host.Contains("[")) return "has no plain host";
                if (host != host.ToLowerInvariant()) return "has an upper-case host";
                if (path == "") return "has no path";
                return PathProblem(path);
            }

            if (scheme == "http")
            {
                if (netloc.Contains("@")) return "carries userinfo";
                if (netloc.Split(':')[0].ToLowerInvariant() == "localhost")
                    return "names localhost: a loopback return is the literal 127.0.0.1 or [::1]";
                if (Array.IndexOf(LoopbackHosts, netloc) >= 0) return "names no port: a loopback return carries the port the app bound";
                var host = Array.Find(LoopbackHosts, h => netloc.StartsWith(h + ":", StringComparison.Ordinal));
                if (host == null) return "is http on a host that is not a loopback literal (127.0.0.1 or [::1])";
                var port = netloc.Substring(host.Length + 1);
                if (!IsPort(port)) return "carries a port that is not a whole number from 1 to 65535, written plainly";
                if (path == "") return "has no path";
                return PathProblem(path);
            }

            if (scheme != scheme.ToLowerInvariant()) return "has an upper-case scheme: a custom scheme is lower-case";
            if (!IsScheme(scheme)) return "has a scheme of characters a scheme may not hold";
            if (Array.IndexOf(RefusedSchemes, scheme) >= 0) return "uses a scheme a return address may never use";
            if (rest == "") return "has no path after the scheme";
            if (rest.Contains("@")) return "carries userinfo";
            return PathProblem(rest);
        }

        private static string PathProblem(string path)
        {
            foreach (var segment in path.Split('/'))
            {
                if (segment == "." || segment == "..") return "carries a dot segment";
            }

            return null;
        }

        /// <summary><c>^[a-z][a-z0-9+.-]*$</c>, by hand.</summary>
        private static bool IsScheme(string s)
        {
            if (s.Length == 0 || s[0] < 'a' || s[0] > 'z') return false;
            foreach (var c in s)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '.' || c == '-')) return false;
            }

            return true;
        }

        /// <summary><c>^[1-9][0-9]{0,4}$</c> and at most 65535.</summary>
        private static bool IsPort(string s)
        {
            if (s.Length == 0 || s.Length > 5 || s[0] < '1' || s[0] > '9') return false;
            foreach (var c in s)
            {
                if (c < '0' || c > '9') return false;
            }

            return int.Parse(s, System.Globalization.CultureInfo.InvariantCulture) <= 65535;
        }

        /// <summary>
        /// The scheme, the host with its port, and the path, each exactly. Never the origin alone: a custom scheme's would
        /// let <c>evil://onlyworlds-signin</c> match <c>obsidian://onlyworlds-signin</c>. A trailing slash is tolerated on
        /// https alone, where a static host may answer <c>/callback</c> by sending the browser to <c>/callback/</c>; a
        /// loopback or custom-scheme return reaches the app as it was registered.
        /// </summary>
        internal static bool SamePage(string a, string b)
        {
            if (!TryPage(a, out var x) || !TryPage(b, out var y)) return false;
            if (x.Scheme != y.Scheme || x.Host != y.Host) return false;
            if (x.Scheme == "https") return TrimOneSlash(x.Path) == TrimOneSlash(y.Path);
            return x.Path == y.Path;
        }

        private static string TrimOneSlash(string path) => path.EndsWith("/", StringComparison.Ordinal) ? path.Substring(0, path.Length - 1) : path;

        /// <summary>
        /// An address's scheme (lower case), host with its port, and path, as a browser's URL reads them: http(s) through
        /// <see cref="Uri"/> (the host lower-cased, a default port dropped, the path normalized), any other scheme as
        /// written up to its query.
        /// </summary>
        private static bool TryPage(string url, out (string Scheme, string Host, string Path) page)
        {
            page = default;
            var sep = url.IndexOf("://", StringComparison.Ordinal);
            if (sep <= 0) return false;
            var scheme = url.Substring(0, sep).ToLowerInvariant();

            if (scheme == "http" || scheme == "https")
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
                page = (scheme, uri.Authority.ToLowerInvariant(), uri.AbsolutePath);
                return true;
            }

            var rest = url.Substring(sep + 3);
            var end = rest.IndexOfAny(new[] { '?', '#' });
            if (end >= 0) rest = rest.Substring(0, end);
            var slash = rest.IndexOf('/');
            page = (scheme, slash < 0 ? rest : rest.Substring(0, slash), slash < 0 ? "" : rest.Substring(slash));
            return true;
        }

        /// <summary>A query parameter's first value, as a browser's <c>URLSearchParams.get</c> reads it (form-decoded), or null.</summary>
        internal static string QueryValue(string url, string name)
        {
            var start = url.IndexOf('?');
            if (start < 0) return null;
            var query = url.Substring(start + 1);
            var hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);

            foreach (var pair in query.Split('&'))
            {
                var eq = pair.IndexOf('=');
                var key = FormDecode(eq < 0 ? pair : pair.Substring(0, eq));
                if (key == name) return FormDecode(eq < 0 ? "" : pair.Substring(eq + 1));
            }

            return null;
        }

        private static string FormDecode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        /// <summary>
        /// A value as a browser's <c>URLSearchParams</c> writes it (application/x-www-form-urlencoded): letters, digits and
        /// <c>*-._</c> as they are, a space as <c>+</c>, every other byte of its UTF-8 percent-encoded in capitals.
        /// </summary>
        internal static string FormEncode(string s)
        {
            var sb = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                var c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '*' || c == '-'
                    || c == '.' || c == '_')
                {
                    sb.Append(c);
                }
                else if (c == ' ')
                {
                    sb.Append('+');
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            return sb.ToString();
        }
    }
}
