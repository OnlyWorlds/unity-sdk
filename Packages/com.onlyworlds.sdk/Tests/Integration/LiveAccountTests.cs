using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Integration
{
    /// <summary>
    /// A password sign-in from Unity's own transport, then the new token's own life: listed among the account's tokens by
    /// the id the sign-in answered, revoked by it, and refused after.
    /// </summary>
    /// <remarks>
    /// These sign in to a real account (<c>OW_LIVE_USERNAME</c>, <c>OW_LIVE_PASSWORD</c>) and mint a token on it, which
    /// they revoke, also when an assertion fails. Use an account made for testing, never one anyone keeps.
    /// </remarks>
    [Category("Integration")]
    [Explicit("Needs a test account's login and a network: signs in, mints a token, then revokes it.")]
    public class LiveAccountTests
    {
        private static string Username => Environment.GetEnvironmentVariable("OW_LIVE_USERNAME");
        private static string Password => Environment.GetEnvironmentVariable("OW_LIVE_PASSWORD");

        [Test]
        public async Task PasswordSignIn_ItsTokenIsListed_ThenRevokedByItsOwnId_AndRefusedAfter()
        {
            if (string.IsNullOrEmpty(Username)) Assert.Ignore("OW_LIVE_USERNAME not set -- skipping live account tests.");

            var bare = new OWClient(new OWClientConfig { Transport = new UnityWebRequestTransport() });
            var name = "unity-live-test " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var signed = await bare.SignInWithPasswordAsync(null, Username, Password, tokenName: name);
            TestContext.WriteLine($"signed in: token {signed.Name}, last four {signed.Last4}");

            var me = new OWClient(new OWClientConfig { ApiKey = signed.Token, Transport = new UnityWebRequestTransport() });
            var revoked = false;
            try
            {
                Assert.IsFalse(string.IsNullOrEmpty(signed.Id), "the sign-in answers the token's id");
                var tokens = await me.ListAccountTokensAsync();
                var mine = tokens.SingleOrDefault(t => t.Id == signed.Id);
                Assert.IsNotNull(mine, "the new token is listed by the id the sign-in answered");
                Assert.AreEqual((name, signed.Last4, false), (mine.Name, mine.Last4, mine.Revoked));
                TestContext.WriteLine($"listed: {tokens.Count} tokens on the account, this one by name and last four");

                await me.RevokeAccountTokenAsync(signed.Id);
                revoked = true;
            }
            finally
            {
                if (!revoked) await me.RevokeAccountTokenAsync(signed.Id);
            }

            // Awaited, never Assert.ThrowsAsync: it blocks the main thread, which UnityWebRequest needs to complete, and
            // the run hangs there (seen 10-31, after the revoke had gone through).
            OWApiError after = null;
            try
            {
                await me.ListAccountTokensAsync();
            }
            catch (OWApiError e)
            {
                after = e;
            }

            Assert.IsNotNull(after, "a revoked token is refused");
            TestContext.WriteLine($"after the revoke: {after.StatusCode} {after.Code}");
            Assert.IsTrue(after.IsAuthError, "refused as a bad credential");
        }
    }
}
