using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's sign-in this client gained in its fourth tranche: a password turned into an account token
    /// (<see cref="OWClient.SignInWithPasswordAsync"/>), and Sign in with OnlyWorlds, OAuth's code with PKCE
    /// (<see cref="OWClient.SignInWithOnlyWorlds"/>, which sends nothing, and <see cref="OWClient.CompleteSignInAsync"/>).
    /// What each sends and to where, what it answers, each refusal before sending and after, and that no password,
    /// verifier, code or token reaches an error. Ported from the TypeScript SDK's own tests at 4.121.0
    /// (<c>sign-in.test.mjs</c>, <c>signin-oauth.test.mjs</c>): its bodies (keel's and allauth's, as production sent
    /// them), its PKCE vector (RFC 7636 appendix B) and its tables of return addresses.
    /// </summary>
    public class OWSignInTests
    {
        private const string Base = "https://www.onlyworlds.com/api/v2";
        private const string Auth = Base + "/auth/app/v1";
        private const string Password = "pw-Zq81!never-shown";
        private const string Session = "sess_abc123";

        private delegate Task<OWResponse> Responder(OWRequest request, CancellationToken ct);

        /// <summary>Answers each request with the next responder, and records what was sent.</summary>
        private sealed class Script : IOWTransport
        {
            public readonly List<OWRequest> Calls = new List<OWRequest>();
            private readonly Responder[] _next;

            public Script(params Responder[] next) { _next = next; }

            public Task<OWResponse> SendAsync(OWRequest request, CancellationToken ct = default)
            {
                Calls.Add(request);
                if (Calls.Count > _next.Length) throw new AssertionException($"unexpected request {request.Method} {request.Url}");
                return _next[Calls.Count - 1](request, ct);
            }
        }

        private static Responder Json(int status, string body, string retryAfter = null)
            => (r, ct) => Task.FromResult(new OWResponse
            {
                Status = status,
                Body = body,
                Headers = retryAfter == null
                    ? new Dictionary<string, string> { ["content-type"] = "application/json" }
                    : new Dictionary<string, string> { ["content-type"] = "application/json", ["retry-after"] = retryAfter },
            });

        private static Responder Html(int status, string text, string retryAfter = null)
            => (r, ct) => Task.FromResult(new OWResponse
            {
                Status = status,
                Body = text,
                Headers = retryAfter == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Retry-After"] = retryAfter },
            });

        // No connection, and an exception that carries the request it failed on (a transport's may): it must not surface.
        private static readonly Responder Offline = (r, ct) => throw new System.Net.WebException("fetch failed: " + r.Url + " " + r.Body);

        private static readonly Responder LoggedIn = Json(200,
            @"{""status"":200,""data"":{""user"":{""id"":7,""email"":""ana@example.org""},""methods"":[]},"
            + @"""meta"":{""is_authenticated"":true,""session_token"":""" + Session + @"""}}");

        private static readonly Responder Minted = Json(201,
            @"{""id"":""tok-1"",""name"":""Lantern on iPhone"",""last4"":""wxyz"",""last_used_at"":null,""revoked"":false,"
            + @"""token"":""ow_a_secretwxyz""}");

        // allauth's answer to ending the session: 401 is its signed-out state (seen on production, 2026-10-17).
        private static readonly Responder SignedOut = Json(401, @"{""status"":401,""data"":{""flows"":[]},""meta"":{""is_authenticated"":false}}");

        private static readonly Responder Mismatch = Json(400,
            @"{""status"":400,""errors"":[{""message"":""The email address and/or password you specified are not correct."","
            + @"""code"":""email_password_mismatch"",""param"":""password""}]}");

        // A person signing in holds no key. A Unity client needs a key or World, so the keyless client here names one.
        private static OWClient Keyless(IOWTransport t, string baseUrl = Base)
            => new OWClient(new OWClientConfig { World = "w1", Transport = t, BaseUrl = baseUrl });

        private static Task<OWSignedIn> SignIn(OWClient c) => c.SignInWithPasswordAsync("ana@example.org", null, Password);

        private static readonly string[] Credentials = { "API-Key", "API-Pin", "Authorization" };

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        /// <summary>The refusal, checked to carry no secret anywhere: message, text, code, and no inner exception.</summary>
        private static OWSignInError Refused(Func<Task> call, params string[] secrets)
        {
            var e = Assert.ThrowsAsync<OWSignInError>(() => call());
            foreach (var secret in secrets.Concat(new[] { Password }))
            {
                StringAssert.DoesNotContain(secret, e.Message);
                StringAssert.DoesNotContain(secret, e.ToString());
                if (e.Code != null) StringAssert.DoesNotContain(secret, e.Code);
            }

            Assert.IsNull(e.InnerException, "no cause attached: a transport's failure can carry the request");
            return e;
        }

        // == Signing in with a password ======================================================================

        // The TypeScript SDK's signInWithPassword (4.31.0): a sign-in at keel's sign-in service, a token minted with the
        // session, the session ended. Kills: a key the client holds sent anywhere, the password sent past the sign-in, the
        // session sent on the sign-in or not on the mint and the end, a body on the mint (keel's document declares none
        // there), the wrong order, or the token's row misread.
        [Test]
        public async Task SignInWithPassword_SignsIn_MintsTheToken_EndsTheSession_AndAnswersOnlyTheToken()
        {
            var f = new Script(LoggedIn, Minted, SignedOut);
            var got = await SignIn(Keyless(f));

            Assert.AreEqual(("ow_a_secretwxyz", "tok-1", "Lantern on iPhone", "wxyz"), (got.Token, got.Id, got.Name, got.Last4));
            CollectionAssert.AreEqual(
                new[] { "POST " + Auth + "/auth/login", "POST " + Base + "/account/tokens", "DELETE " + Auth + "/auth/session" },
                f.Calls.Select(x => x.Method + " " + x.Url).ToArray());

            CollectionAssert.AreEqual(new[] { "Content-Type" }, HeaderNames(f.Calls[0]));
            Assert.AreEqual("application/json", f.Calls[0].Headers["Content-Type"]);
            Assert.AreEqual(@"{""email"":""ana@example.org"",""password"":""" + Password + @"""}", f.Calls[0].Body);

            CollectionAssert.AreEqual(new[] { "X-Session-Token" }, HeaderNames(f.Calls[1]), "the session alone, and no body");
            Assert.AreEqual(Session, f.Calls[1].Headers["X-Session-Token"]);
            Assert.IsNull(f.Calls[1].Body);

            CollectionAssert.AreEqual(new[] { "X-Session-Token" }, HeaderNames(f.Calls[2]));
            Assert.AreEqual(Session, f.Calls[2].Headers["X-Session-Token"]);
            Assert.IsNull(f.Calls[2].Body);
        }

        // The password goes to exactly the host the TypeScript SDK sends it to, the sign-in service, once, and nowhere
        // else; whatever the client holds, no key of its own is sent. Kills: the password in a later request's body,
        // URL or header; a key, PIN or Bearer of the client's sent on any of the three.
        [Test]
        public async Task SignInWithPassword_ThePasswordGoesOnceToTheSignInService_AndNoKeyOfTheClientsIsSent()
        {
            var clients = new Func<IOWTransport, OWClient>[]
            {
                t => Keyless(t),
                t => new OWClient(new OWClientConfig { ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = Base, Agent = true }),
                t => new OWClient(new OWClientConfig { ApiKey = "ow_a_other", World = "w1", Transport = t, BaseUrl = Base }),
                t => new OWClient(new OWClientConfig { ApiKey = "0123456789", ApiPin = "1234", Transport = t, BaseUrl = Base }),
            };

            foreach (var make in clients)
            {
                var f = new Script(LoggedIn, Minted, SignedOut);
                Assert.AreEqual("ow_a_secretwxyz", (await SignIn(make(f))).Token);
                Assert.AreEqual(3, f.Calls.Count);

                for (var i = 0; i < f.Calls.Count; i++)
                {
                    var x = f.Calls[i];
                    foreach (var h in Credentials) Assert.IsFalse(x.Headers.ContainsKey(h), $"{x.Method} {x.Url}: no {h}");
                    Assert.IsFalse(x.Headers.ContainsKey("X-OW-Agent") || x.Headers.ContainsKey("X-OW-Client"), $"{x.Method} {x.Url}");
                    StringAssert.StartsWith(Base + "/", x.Url);
                    StringAssert.DoesNotContain(Password, x.Url);
                    Assert.IsFalse(x.Headers.Values.Any(v => v.Contains(Password)), $"{x.Method} {x.Url}: no password in a header");
                    if (i > 0) Assert.IsFalse((x.Body ?? "").Contains(Password), $"{x.Method} {x.Url}: the password only to the sign-in");
                }

                StringAssert.StartsWith(Auth + "/auth/login", f.Calls[0].Url);
                StringAssert.Contains(Password, f.Calls[0].Body);
            }
        }

        [Test]
        public async Task SignInWithPassword_AUsername_SignsInAsUsernameAndPassword()
        {
            var f = new Script(LoggedIn, Minted, SignedOut);
            await Keyless(f).SignInWithPasswordAsync(null, " Walker ", Password);
            Assert.AreEqual(@"{""username"":""Walker"",""password"":""" + Password + @"""}", f.Calls[0].Body, "trimmed, as the TypeScript SDK trims");
        }

        [Test]
        public async Task SignInWithPassword_EndingTheSessionFailingOnTheNetwork_IsStillASignIn()
        {
            var f = new Script(LoggedIn, Minted, Offline);
            Assert.AreEqual("ow_a_secretwxyz", (await SignIn(Keyless(f))).Token);
            Assert.AreEqual(3, f.Calls.Count);
        }

        // Kills: production's wrong-password answer read as anything but invalid_credentials, keel's words dropped, or
        // the mint tried after a refusal.
        [Test]
        public void SignInWithPassword_AWrongPassword_IsInvalidCredentials_NothingMoreSent()
        {
            var f = new Script(Mismatch);
            var e = Refused(() => SignIn(Keyless(f)));
            Assert.AreEqual(OWSignInReason.InvalidCredentials, e.Reason);
            Assert.AreEqual(400, e.Status);
            Assert.AreEqual("email_password_mismatch", e.Code);
            Assert.AreEqual("The email address and/or password you specified are not correct.", e.Message);
            Assert.AreEqual(1, f.Calls.Count);

            var g = new Script(Json(400, @"{""status"":400,""errors"":[{""message"":""The username and/or password you specified are not correct."",""code"":""username_password_mismatch"",""param"":""password""}]}"));
            Assert.AreEqual(OWSignInReason.InvalidCredentials, Refused(() => Keyless(g).SignInWithPasswordAsync(null, "Walker", Password)).Reason);
        }

        [Test]
        public void SignInWithPassword_TheFailedLoginThrottle_IsRateLimited()
        {
            var f = new Script(Json(400, @"{""status"":400,""errors"":[{""message"":""Too many failed login attempts. Try again later."",""code"":""too_many_login_attempts""}]}"));
            var e = Refused(() => SignIn(Keyless(f)));
            Assert.AreEqual((OWSignInReason.RateLimited, (int?)400, "too_many_login_attempts"), (e.Reason, e.Status, e.Code));
        }

        [Test]
        public void SignInWithPassword_A429HtmlPageWithRetryAfter30_IsRateLimited_RetryAfter30()
        {
            var f = new Script(Html(429, "<!doctype html><title>Too many requests</title>", "30"));
            var e = Refused(() => SignIn(Keyless(f)));
            Assert.AreEqual((OWSignInReason.RateLimited, (int?)429, (int?)30), (e.Reason, e.Status, e.RetryAfter));
            Assert.AreEqual(1, f.Calls.Count);
        }

        // An unconfirmed email (a pending verify_email flow) is email_unverified; a pending flow that hands out a session
        // has that session ended too. Kills: the flow unread, or a session left open.
        [Test]
        public void SignInWithPassword_AnUnconfirmedEmail_IsEmailUnverified_AndAnySessionIsEnded()
        {
            const string pending = @"{""status"":401,""data"":{""flows"":[{""id"":""login""},{""id"":""verify_email"",""is_pending"":true}]},""meta"":{""is_authenticated"":false}}";
            var f = new Script(Json(401, pending));
            var e = Refused(() => SignIn(Keyless(f)));
            Assert.AreEqual((OWSignInReason.EmailUnverified, (int?)401), (e.Reason, e.Status));
            Assert.AreEqual(1, f.Calls.Count);

            var g = new Script(Json(401, pending.Replace(@"""is_authenticated"":false", @"""is_authenticated"":false,""session_token"":""" + Session + @"""")), SignedOut);
            Assert.AreEqual(OWSignInReason.EmailUnverified, Refused(() => SignIn(Keyless(g))).Reason);
            CollectionAssert.AreEqual(new[] { "POST " + Auth + "/auth/login", "DELETE " + Auth + "/auth/session" },
                g.Calls.Select(x => x.Method + " " + x.Url).ToArray());
            Assert.AreEqual(Session, g.Calls[1].Headers["X-Session-Token"]);
        }

        [Test]
        public void SignInWithPassword_NoConnection_IsOffline_NothingMoreSent()
        {
            var f = new Script(Offline);
            var e = Refused(() => SignIn(Keyless(f)));
            Assert.AreEqual(OWSignInReason.Offline, e.Reason);
            Assert.IsNull(e.Status);
            Assert.AreEqual(1, f.Calls.Count);
        }

        [Test]
        public void SignInWithPassword_OtherRefusals_AreFailed_WithKeelsMessageWhenThereIsOne()
        {
            var f = new Script(Json(400, @"{""status"":400,""errors"":[{""message"":""This account is inactive."",""code"":""account_inactive""}]}"));
            var e = Refused(() => SignIn(Keyless(f)));
            Assert.AreEqual((OWSignInReason.Failed, "account_inactive", "This account is inactive."), (e.Reason, e.Code, e.Message));

            var g = new Script(Html(502, "<html>Bad gateway</html>"));
            e = Refused(() => SignIn(Keyless(g)));
            Assert.AreEqual((OWSignInReason.Failed, (int?)502), (e.Reason, e.Status));

            // A 200 without a session token: nothing to mint with.
            var h = new Script(Json(200, @"{""status"":200,""meta"":{""is_authenticated"":true}}"));
            e = Refused(() => SignIn(Keyless(h)));
            Assert.AreEqual((OWSignInReason.Failed, (int?)200), (e.Reason, e.Status));
            Assert.AreEqual(1, h.Calls.Count);
        }

        // A refused mint (403, 422, or a 201 without a token) is mint_failed with keel's status and code, and the session
        // is still ended. Kills: the session left open on a refused mint, a 200 taken as a mint, keel's code dropped.
        [Test]
        public void SignInWithPassword_ARefusedMint_IsMintFailed_AndTheSessionIsStillEnded()
        {
            foreach (var (answer, status, code) in new (Responder, int, string)[]
            {
                (Json(403, @"{""error"":{""type"":""permission_denied"",""code"":""forbidden"",""message"":""Not allowed.""}}"), 403, "forbidden"),
                (Json(422, @"{""error"":{""type"":""invalid_request"",""code"":""validation_error"",""message"":""name: too long"",""param"":""name""}}"), 422, "validation_error"),
                (Json(201, @"{""id"":""tok-1"",""name"":""x"",""last4"":""wxyz""}"), 201, null),
                (Json(200, @"{""id"":""tok-1"",""token"":""ow_a_secretwxyz""}"), 200, null),
            })
            {
                var f = new Script(LoggedIn, answer, SignedOut);
                var e = Refused(() => SignIn(Keyless(f)), "ow_a_secretwxyz");
                Assert.AreEqual((OWSignInReason.MintFailed, (int?)status, code), (e.Reason, e.Status, e.Code), $"{status}");
                CollectionAssert.AreEqual(new[] { "POST", "POST", "DELETE" }, f.Calls.Select(x => x.Method).ToArray(), $"session ended after a {status}");
                Assert.AreEqual(Session, f.Calls[2].Headers["X-Session-Token"]);
            }
        }

        [Test]
        public void SignInWithPassword_TheConnectionDroppingOnTheMint_IsOffline_AndTheSessionIsStillEnded()
        {
            var f = new Script(LoggedIn, Offline, SignedOut);
            Assert.AreEqual(OWSignInReason.Offline, Refused(() => SignIn(Keyless(f)), Session).Reason);
            CollectionAssert.AreEqual(
                new[] { "POST " + Auth + "/auth/login", "POST " + Base + "/account/tokens", "DELETE " + Auth + "/auth/session" },
                f.Calls.Select(x => x.Method + " " + x.Url).ToArray());
        }

        // Both email and username, or neither, or a blank email, or no password: an ArgumentException before sending,
        // with no password in it.
        [Test]
        public void SignInWithPassword_BothOrNeitherOfEmailAndUsername_OrNoPassword_IsRefused_NothingSent()
        {
            var f = new Script();
            var c = Keyless(f);
            foreach (var (email, username, password) in new (string, string, string)[]
            {
                ("ana@example.org", "Walker", Password), (null, null, Password), ("  ", null, Password), (null, "", Password),
                ("ana@example.org", null, ""), ("ana@example.org", null, null),
            })
            {
                var e = Assert.Throws<ArgumentException>(() => c.SignInWithPasswordAsync(email, username, password));
                StringAssert.DoesNotContain(Password, e.Message);
                StringAssert.Contains("Nothing was sent", e.Message);
            }

            Assert.IsEmpty(f.Calls);
        }

        [Test]
        public async Task SignInWithPassword_ACustomBaseUrl_DerivesTheSignInServiceTheSameWay()
        {
            var f = new Script(LoggedIn, Minted, SignedOut);
            await SignIn(Keyless(f, "http://localhost:8000/api/v2/"));
            CollectionAssert.AreEqual(new[]
            {
                "http://localhost:8000/api/v2/auth/app/v1/auth/login",
                "http://localhost:8000/api/v2/account/tokens",
                "http://localhost:8000/api/v2/auth/app/v1/auth/session",
            }, f.Calls.Select(x => x.Url).ToArray());
        }

        // Kills: keel's or allauth's text shown when it holds the password, the session or the token.
        [Test]
        public void SignInWithPassword_KeelTextThatHoldsASecret_IsNeverShown()
        {
            var f = new Script(Json(400, @"{""status"":400,""errors"":[{""message"":""bad password " + Password + @""",""code"":""email_password_mismatch""}]}"));
            Assert.AreEqual(OWSignInReason.InvalidCredentials, Refused(() => SignIn(Keyless(f))).Reason);

            var g = new Script(LoggedIn, Json(422, @"{""error"":{""code"":""validation_error"",""message"":""got " + Password + @"""}}"), SignedOut);
            Assert.AreEqual(OWSignInReason.MintFailed, Refused(() => SignIn(Keyless(g))).Reason);

            var h = new Script(LoggedIn, Json(422, @"{""error"":{""code"":""x_" + Session + @""",""message"":""session " + Session + @" spent""}}"), SignedOut);
            var e = Refused(() => SignIn(Keyless(h)), Session);
            Assert.IsNull(e.Code, "a code that holds a secret is dropped");

            var i = new Script(Json(400, @"{""status"":400,""errors"":[{""message"":""no"",""code"":""" + Password + @"""}]}"));
            Assert.IsNull(Refused(() => SignIn(Keyless(i))).Code);
        }

        // A session end that never answers is cut off (five seconds in the client; shortened here): the sign-in still
        // answers its token. Kills: the end awaited without a limit.
        [Test]
        public async Task SignInWithPassword_ASessionEndThatNeverAnswers_IsCutOff_TheSignInStillAnswersItsToken()
        {
            var saved = OWClient.SessionEndLimit;
            OWClient.SessionEndLimit = TimeSpan.FromMilliseconds(200);
            try
            {
                Responder hang = (r, ct) => new TaskCompletionSource<OWResponse>().Task;
                var f = new Script(LoggedIn, Minted, hang);
                var clock = Stopwatch.StartNew();
                Assert.AreEqual("ow_a_secretwxyz", (await SignIn(Keyless(f))).Token);
                Assert.Less(clock.ElapsedMilliseconds, 3000, "cut off, not left hanging");
                Assert.AreEqual(3, f.Calls.Count);
            }
            finally
            {
                OWClient.SessionEndLimit = saved;
            }

            Assert.AreEqual(TimeSpan.FromSeconds(5), OWClient.SessionEndLimit, "five seconds, as the TypeScript SDK's");
        }

        // == Sign in with OnlyWorlds ============================================================================

        [Test]
        public void Pkce_MatchesRfc7636AppendixB_AndBase64UrlHasNoPadding()
        {
            Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OWSignIn.PkceChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
            Assert.AreEqual("-_8", OWSignIn.Base64Url(new byte[] { 251, 255 }));
            Assert.AreEqual("", OWSignIn.Base64Url(new byte[0]));
            Assert.AreEqual("AQID", OWSignIn.Base64Url(new byte[] { 1, 2, 3 }));
        }

        private static OWClient Client(IOWTransport t = null) => Keyless(t ?? new Script());

        // The TypeScript SDK's signInWithOnlyWorlds (4.46.0): the authorize URL on the site's origin, its five parameters
        // in order, form-encoded as the browser's URLSearchParams writes them; a fresh state and verifier (43 characters of
        // base64url), the challenge the verifier's S256; no request on any client. Kills: the URL on the API's path, a
        // parameter misnamed or reordered, the challenge not the verifier's, the verifier in the URL, a state reused.
        [Test]
        public void SignInWithOnlyWorlds_BuildsTheAuthorizeUrl_AndHandsBackThePendingSignIn_SendingNothing()
        {
            foreach (var key in new[] { null, "ow_w_test", "ow_a_acct" })
            {
                var t = new Script();
                var c = new OWClient(new OWClientConfig { ApiKey = key, ApiPin = "1234", World = key == "ow_w_test" ? null : "w1", Transport = t, BaseUrl = Base });
                var p = c.SignInWithOnlyWorlds("atlas", "https://atlas.onlyworlds.com/signin/callback");
                Assert.IsEmpty(t.Calls, "no request is sent");

                Assert.AreEqual("https://www.onlyworlds.com/signin/authorize?client_id=atlas"
                    + "&redirect_uri=https%3A%2F%2Fatlas.onlyworlds.com%2Fsignin%2Fcallback&state=" + p.State
                    + "&code_challenge=" + OWSignIn.PkceChallenge(p.Verifier) + "&code_challenge_method=S256", p.Url);
                Assert.AreEqual(("atlas", "https://atlas.onlyworlds.com/signin/callback", true), (p.ClientId, p.RedirectUri, p.Pending));
                Assert.AreEqual(43, p.Verifier.Length);
                Assert.AreEqual(43, p.State.Length);
                StringAssert.IsMatch("^[A-Za-z0-9_-]+$", p.Verifier);
                StringAssert.DoesNotContain(p.Verifier, p.Url);
            }

            var a = Client().SignInWithOnlyWorlds("atlas", "https://atlas.onlyworlds.com/signin/callback");
            var b = Client().SignInWithOnlyWorlds("atlas", "https://atlas.onlyworlds.com/signin/callback");
            Assert.AreNotEqual(a.State, b.State);
            Assert.AreNotEqual(a.Verifier, b.Verifier);
            Assert.AreNotEqual(a.State, a.Verifier);

            // URLSearchParams' form encoding, not a URI component's: ~ goes %7E, ( and ) %28 and %29.
            StringAssert.Contains("&redirect_uri=https%3A%2F%2Fatlas.onlyworlds.com%2F%7Eann%2F%281%29&",
                Client().SignInWithOnlyWorlds("atlas", "https://atlas.onlyworlds.com/~ann/(1)").Url);

            var local = new OWClient(new OWClientConfig { World = "w1", Transport = new Script(), BaseUrl = "http://localhost:8000/api/v2" })
                .SignInWithOnlyWorlds("obsidian", "obsidian://onlyworlds-signin");
            StringAssert.StartsWith("http://localhost:8000/signin/authorize?client_id=obsidian&redirect_uri=obsidian%3A%2F%2Fonlyworlds-signin&", local.Url);
        }

        [Test]
        public void SignInWithOnlyWorlds_NoClientId_IsRefused()
        {
            foreach (var id in new[] { null, "" })
                Assert.Throws<ArgumentException>(() => Client().SignInWithOnlyWorlds(id, "https://atlas.onlyworlds.com/cb"));
        }

        // A native app's return addresses (RFC 8252), as keel takes them at authorize: a loopback literal with the port
        // the app bound, or a custom scheme exactly as registered. The TypeScript SDK's tables, case for case.
        private static readonly string[] NativeOk =
        {
            "http://127.0.0.1:53682/onlyworlds-signin",
            "http://[::1]:53682/onlyworlds-signin",
            "http://127.0.0.1:1/",
            "http://127.0.0.1:65535/a/b",
            "obsidian://onlyworlds-signin",
            "com.example.app://signin/done",
            "https://atlas.onlyworlds.com/signin/callback",
            "https://atlas.onlyworlds.com:8443/signin/callback",
        };

        private static readonly (string Uri, string Why)[] NativeRefused =
        {
            ("http://localhost:53682/onlyworlds-signin", "localhost"),
            ("http://LOCALHOST:53682/onlyworlds-signin", "localhost"),
            ("http://127.0.0.1/onlyworlds-signin", "no port"),
            ("http://[::1]/onlyworlds-signin", "no port"),
            ("http://127.0.0.1:0/cb", "port"),
            ("http://127.0.0.1:65536/cb", "port"),
            ("http://127.0.0.1:080/cb", "port"),
            ("http://127.0.0.1:+80/cb", "port"),
            ("http://127.0.0.1:/cb", "port"),
            ("http://127.0.0.1:53682", "no path"),
            ("http://10.0.0.1:53682/cb", "not a loopback"),
            ("http://atlas.onlyworlds.com/cb", "not a loopback"),
            ("http://user@127.0.0.1:53682/cb", "userinfo"),
            ("https://user@atlas.onlyworlds.com/cb", "userinfo"),
            ("obsidian://user@onlyworlds-signin", "userinfo"),
            ("obsidian://onlyworlds-signin#x", "fragment"),
            ("https://atlas.onlyworlds.com/cb#x", "fragment"),
            ("obsidian://onlyworlds-signin?x=1", "query"),
            ("Obsidian://onlyworlds-signin", "upper-case scheme"),
            ("1app://x", "scheme of characters"),
            ("ob_sidian://x", "scheme of characters"),
            ("javascript://x", "never use"),
            ("data://x", "never use"),
            ("file:///etc/passwd", "never use"),
            ("obsidian://", "no path"),
            ("obsidian:onlyworlds-signin", @"redirectUri is not <scheme>://<path>"),
            ("obsidian://a/../b", "dot segment"),
            ("http://127.0.0.1:53682/a/./b", "dot segment"),
            ("https://Atlas.onlyworlds.com/cb", "upper-case host"),
            ("https://atlas.onlyworlds.com", "no path"),
            ("obsidian://onlyworlds signin", "whitespace"),
            ("", "not a string"),
            (null, "not a string"),
        };

        [Test]
        public void SignInWithOnlyWorlds_TakesANativeAppsLoopbackWithItsPort_AndItsOwnScheme_SendingEachAsGiven()
        {
            foreach (var uri in NativeOk)
            {
                var p = Client().SignInWithOnlyWorlds("obsidian", uri);
                Assert.AreEqual(uri, p.RedirectUri, uri);
                var sent = Regex.Match(p.Url, "[?&]redirect_uri=([^&]*)").Groups[1].Value;
                Assert.AreEqual(uri, Uri.UnescapeDataString(sent.Replace('+', ' ')), uri);
            }
        }

        [Test]
        public void SignInWithOnlyWorlds_RefusesLocalhost_ALoopbackWithoutItsPort_Userinfo_AFragment_TheReservedSchemes_AndSaysWhatItTakes()
        {
            var accepted = new List<string>();
            foreach (var (uri, why) in NativeRefused)
            {
                ArgumentException e;
                try
                {
                    Client().SignInWithOnlyWorlds("obsidian", uri);
                    accepted.Add(uri);
                    continue;
                }
                catch (ArgumentException caught)
                {
                    e = caught;
                }

                StringAssert.Contains(why, e.Message, uri);
                StringAssert.IsMatch(@"https://<host>/<path>.*http://127\.0\.0\.1:<port>/<path>.*<scheme>://<path>", e.Message, uri);
            }

            CollectionAssert.IsEmpty(accepted);
        }

        private const string Exchanged = @"{""token"":""ow_a_new"",""token_id"":""t1"",""token_type"":""bearer"",""account"":{""id"":""u1"",""username"":""ann""}}";

        private static OWSignInBegun Begun(string redirectUri = "https://atlas.onlyworlds.com/signin/callback", string clientId = "atlas")
            => Client().SignInWithOnlyWorlds(clientId, redirectUri);

        private static string Callback(OWSignInBegun p, string code = "c1") => $"{p.RedirectUri}?code={code}&state={p.State}";

        // The TypeScript SDK's completeSignIn (4.46.0; expires_at from 4.94.0): the code traded once at POST /auth/token
        // with the five body keys and no credential, on any client; the token, its id, its expiry, the account. Kills: a
        // key or Bearer sent, a body key misnamed or missing, the verifier not the one minted, the registered address not
        // the one sent, the sign-in left pending after its return, the answer misread.
        [Test]
        public async Task CompleteSignIn_TradesTheCodeOnce_WithTheFiveKeys_AndNoCredential_AndAnswersTheToken()
        {
            foreach (var key in new[] { null, "ow_w_test", "ow_a_acct" })
            {
                var p = Begun();
                var t = new Script(Json(200, Exchanged.Replace(@"""token_type""", @"""expires_at"":""2026-11-03T18:00:00Z"",""token_type""")));
                var c = new OWClient(new OWClientConfig { ApiKey = key, ApiPin = "1234", World = key == "ow_w_test" ? null : "w1", Transport = t, BaseUrl = Base, Agent = true });
                var got = await c.CompleteSignInAsync(p, Callback(p));

                Assert.AreEqual(("ow_a_new", "t1", "2026-11-03T18:00:00Z", "bearer"), (got.Token, got.TokenId, got.ExpiresAt, got.TokenType));
                Assert.AreEqual(("u1", "ann"), (got.Account.Id, got.Account.Username));

                var sent = t.Calls.Single();
                Assert.AreEqual(("POST", Base + "/auth/token"), (sent.Method, sent.Url));
                CollectionAssert.AreEqual(new[] { "Content-Type" }, HeaderNames(sent), "no credential, no write header");
                Assert.AreEqual(@"{""grant_type"":""authorization_code"",""code"":""c1"",""code_verifier"":""" + p.Verifier
                    + @""",""client_id"":""atlas"",""redirect_uri"":""https://atlas.onlyworlds.com/signin/callback""}", sent.Body);
                Assert.IsFalse(p.Pending, "a return that carried its state ends the sign-in");
            }
        }

        // The query is read as a browser reads it: the first of each, form-decoded.
        [Test]
        public async Task CompleteSignIn_ReadsTheCodeAndStateAsTheBrowserDoes()
        {
            var p = Begun();
            var t = new Script(Json(200, Exchanged));
            await Client(t).CompleteSignInAsync(p, $"{p.RedirectUri}?keep=1&code=a%2Fb+c&state={p.State}&code=second");
            Assert.AreEqual("a/b c", (string)JObject.Parse(t.Calls.Single().Body)["code"]);
        }

        [Test]
        public async Task CompleteSignIn_TokenIdIsNullWhenTheServerSendsNone_NeverAMadeUpId()
        {
            foreach (var tokenId in new[] { null, @"""""", "7" })
            {
                var p = Begun();
                var body = tokenId == null ? Exchanged.Replace(@"""token_id"":""t1"",", "") : Exchanged.Replace(@"""t1""", tokenId);
                var got = await Client(new Script(Json(200, body))).CompleteSignInAsync(p, Callback(p));
                Assert.IsNull(got.TokenId, tokenId ?? "absent");
            }
        }

        [Test]
        public async Task CompleteSignIn_CarriesExpiresAtAsServed_AbsentEmptyOrNotAStringIsNull()
        {
            foreach (var (served, want) in new (string, string)[]
            {
                (@"""2026-11-03T18:00:00Z""", "2026-11-03T18:00:00Z"), ("null", null), (null, null), (@"""""", null), ("7", null),
                (@"""2026-11-03T18:00:00.123456+00:00""", "2026-11-03T18:00:00.123456+00:00"),
            })
            {
                var p = Begun();
                var body = served == null ? Exchanged : Exchanged.Replace(@"""token_type""", @"""expires_at"":" + served + @",""token_type""");
                var got = await Client(new Script(Json(200, body))).CompleteSignInAsync(p, Callback(p));
                Assert.AreEqual(want, got.ExpiresAt, served ?? "absent");
            }
        }

        // A wrong state, a wrong page, a cancelled sign-in and a spent one are state_mismatch before sending; a wrong
        // state leaves the sign-in pending, a matching one ends it. Kills: the code traded from a page the sign-in did not
        // start, a forged return cancelling a real one, a sign-in completed twice.
        [Test]
        public void CompleteSignIn_RefusesAWrongStateAWrongPageACancelledOrASpentSignIn_BeforeSending()
        {
            foreach (var (url, why, kept) in new (string, string, bool)[]
            {
                ("https://atlas.onlyworlds.com/signin/callback?code=c1&state=other", "state", true),
                ("https://atlas.onlyworlds.com/signin/callback?code=c1", "no state", true),
                ("https://evil.example/signin/callback?code=c1&state=STATE", "page", false),
                ("https://atlas.onlyworlds.com/signin/callback?error=access_denied&state=STATE", "cancelled", false),
            })
            {
                var p = Begun();
                var t = new Script();
                var e = Refused(() => Client(t).CompleteSignInAsync(p, url.Replace("STATE", p.State)), p.Verifier);
                Assert.AreEqual(OWSignInReason.StateMismatch, e.Reason, why);
                Assert.IsEmpty(t.Calls, why);
                Assert.AreEqual(kept, p.Pending, why);
            }

            var spent = Begun();
            spent.Pending = false;
            var s = new Script();
            Assert.AreEqual(OWSignInReason.StateMismatch, Refused(() => Client(s).CompleteSignInAsync(spent, Callback(spent))).Reason);
            Assert.IsEmpty(s.Calls);

            Assert.Throws<ArgumentNullException>(() => Client().CompleteSignInAsync(null, "https://atlas.onlyworlds.com/signin/callback?code=c1&state=s"));
            Assert.Throws<ArgumentException>(() => Client().CompleteSignInAsync(Begun(), null));
        }

        [Test]
        public async Task CompleteSignIn_HoldsTheSchemeTheHostWithItsPortAndThePathExactly()
        {
            foreach (var (registered, here, ok) in new (string, string, bool)[]
            {
                ("obsidian://onlyworlds-signin", "obsidian://onlyworlds-signin?code=c1&state=STATE", true),
                ("obsidian://onlyworlds-signin", "evil://onlyworlds-signin?code=c1&state=STATE", false),
                ("obsidian://onlyworlds-signin", "obsidian://elsewhere?code=c1&state=STATE", false),
                ("obsidian://onlyworlds-signin", "obsidian://onlyworlds-signin/?code=c1&state=STATE", false),
                ("com.example.app://signin/done", "com.example.app://signin/done?code=c1&state=STATE", true),
                ("com.example.app://signin/done", "com.example.other://signin/done?code=c1&state=STATE", false),
                ("http://127.0.0.1:53682/onlyworlds-signin", "http://127.0.0.1:53682/onlyworlds-signin?code=c1&state=STATE", true),
                ("http://127.0.0.1:53682/onlyworlds-signin", "http://127.0.0.1:53683/onlyworlds-signin?code=c1&state=STATE", false),
                ("http://127.0.0.1:53682/onlyworlds-signin", "http://[::1]:53682/onlyworlds-signin?code=c1&state=STATE", false),
                ("http://127.0.0.1:53682/onlyworlds-signin", "http://127.0.0.1:53682/onlyworlds-signin/?code=c1&state=STATE", false),
                ("http://[::1]:53682/onlyworlds-signin", "http://[::1]:53682/onlyworlds-signin?code=c1&state=STATE", true),
                ("https://atlas.onlyworlds.com/signin/callback", "https://atlas.onlyworlds.com/signin/callback/?code=c1&state=STATE", true),
                ("https://atlas.onlyworlds.com/signin/callback", "https://atlas.onlyworlds.com:8443/signin/callback?code=c1&state=STATE", false),
                ("https://atlas.onlyworlds.com/signin/callback", "https://evil.example/signin/callback?code=c1&state=STATE", false),
            })
            {
                var p = Begun(registered, "obsidian");
                var t = new Script(Json(200, Exchanged));
                var what = $"{registered} at {here}";
                if (ok)
                {
                    var got = await Client(t).CompleteSignInAsync(p, here.Replace("STATE", p.State));
                    Assert.AreEqual("ow_a_new", got.Token, what);
                    Assert.AreEqual(registered, (string)JObject.Parse(t.Calls.Single().Body)["redirect_uri"], what);
                }
                else
                {
                    var e = Refused(() => Client(t).CompleteSignInAsync(p, here.Replace("STATE", p.State)));
                    Assert.AreEqual(OWSignInReason.StateMismatch, e.Reason, what);
                    Assert.IsEmpty(t.Calls, what);
                }
            }
        }

        [Test]
        public void CompleteSignIn_ReadsKeelsRefusals_400InvalidGrant_429RateLimited_AnythingElseFailed()
        {
            foreach (var (status, body, reason, retryAfter) in new (int, string, string, string)[]
            {
                (400, @"{""error"":{""code"":""invalid_grant"",""message"":""spent""}}", OWSignInReason.InvalidGrant, null),
                (429, @"{""error"":{""code"":""rate_limited""}}", OWSignInReason.RateLimited, "12"),
                (500, @"{""error"":{""code"":""api_error""}}", OWSignInReason.Failed, null),
                (502, "<html>Bad gateway</html>", OWSignInReason.Failed, null),
                (200, @"{""token"":""ow_a_new"",""account"":{}}", OWSignInReason.Failed, null),
                (200, @"{""token"":"""",""account"":{""id"":""u1""}}", OWSignInReason.Failed, null),
            })
            {
                var p = Begun();
                var e = Refused(() => Client(new Script(Json(status, body, retryAfter))).CompleteSignInAsync(p, Callback(p)), p.Verifier, "ow_a_new");
                Assert.AreEqual((reason, (int?)status), (e.Reason, e.Status), $"{status} {body}");
                if (retryAfter != null) Assert.AreEqual(12, e.RetryAfter);
                Assert.IsFalse(p.Pending);
            }

            var q = Begun();
            Assert.AreEqual("spent", Refused(() => Client(new Script(Json(400, @"{""error"":{""code"":""invalid_grant"",""message"":""spent""}}")))
                .CompleteSignInAsync(q, Callback(q))).Message, "keel's words when they hold no secret");
        }

        // Kills: the code or the verifier in an error, through keel's words or a transport's exception.
        [Test]
        public void CompleteSignIn_NeitherTheCodeNorTheVerifier_ReachesAnError()
        {
            var p = Begun();
            var code = "code-8f3b-secret";
            var e = Refused(() => Client(new Script(Json(400, @"{""error"":{""code"":""invalid_grant"",""message"":""code " + code + @" was used""}}")))
                .CompleteSignInAsync(p, Callback(p, code)), code, p.Verifier);
            Assert.AreEqual(OWSignInReason.InvalidGrant, e.Reason);

            p = Begun();
            e = Refused(() => Client(new Script(Offline)).CompleteSignInAsync(p, Callback(p, code)), code, p.Verifier);
            Assert.AreEqual(OWSignInReason.Offline, e.Reason);

            p = Begun();
            Refused(() => Client(new Script(Json(500, @"{""error"":{""message"":""verifier " + p.Verifier + @" bad""}}"))).CompleteSignInAsync(p, Callback(p, code)), code, p.Verifier);
        }

        [Test]
        public async Task AForgedCustomSchemeReturn_DoesNotCancelASignInInFlight_TheRealReturnStillSignsIn()
        {
            var p = Begun("obsidian://onlyworlds-signin", "obsidian");
            var t = new Script(Json(200, Exchanged));
            foreach (var forged in new[] { "obsidian://onlyworlds-signin?code=evil&state=guess", "obsidian://onlyworlds-signin?code=evil" })
            {
                Assert.AreEqual(OWSignInReason.StateMismatch, Refused(() => Client(t).CompleteSignInAsync(p, forged)).Reason, forged);
                Assert.IsTrue(p.Pending, forged);
            }

            Assert.IsEmpty(t.Calls);
            Assert.AreEqual("ow_a_new", (await Client(t).CompleteSignInAsync(p, Callback(p))).Token);
            Assert.AreEqual(1, t.Calls.Count);
            Assert.IsFalse(p.Pending);
        }
    }
}
