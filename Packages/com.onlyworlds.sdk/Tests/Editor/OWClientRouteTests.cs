using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's calls this client gained in its first tranche (0.27.0): <see cref="OWClient.HealthAsync"/>.
    /// What each sends, what it answers, and what is refused before anything is sent, on every kind of client.
    /// </summary>
    public class OWClientRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        // An agent client with its own name and a PIN: every header an element write would carry, so a call that must
        // leave one out is seen leaving it out.
        private static OWClient Make(FakeTransport t, string key = "ow_w_test", string pin = "1234", string world = null)
            => new OWClient(new OWClientConfig
            {
                ApiKey = key,
                ApiPin = pin,
                Transport = t,
                BaseUrl = Base,
                World = world,
                Agent = true,
                Client = "atlas",
            });

        private static string Query(OWRequest sent) => new Uri(sent.Url).Query.TrimStart('?');

        // -- HealthAsync ------------------------------------------------------------------------------------

        // The TypeScript SDK's health() (3.0.0; a keyless client from 4.30.0): GET /health/ with no credential, on every
        // client. Kills: the key, PIN or Bearer sent (keel's document declares no header there), world= added on a client
        // with World set, a keyless or an account client refused, or the answer reshaped.
        [Test]
        public async Task Health_SendsGetHealth_WithNoCredential_OnEveryKindOfClient()
        {
            var clients = new (string Name, string Key, string Pin, string World)[]
            {
                ("a write key with its PIN", "ow_w_test", "1234", null),
                ("a read key", "ow_r_test", null, null),
                ("a legacy key with its PIN", "0123456789", "1234", null),
                ("an account token", "ow_a_test", "1234", null),
                ("an account token with World", "ow_a_test", null, "w1"),
                ("no key, World set", null, null, "w1"),
            };

            foreach (var (name, key, pin, world) in clients)
            {
                var t = new FakeTransport { ResponseBody = @"{""status"":""ok""}" };
                var answer = await Make(t, key, pin, world).HealthAsync();
                Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""status"":""ok""}"), answer), $"{name}: the answer as served");

                var a = t.Calls.Single();
                Assert.AreEqual("GET", a.Method, name);
                Assert.AreEqual(Base + "/health/", a.Url, $"{name}: the path, no query");
                Assert.IsNull(a.Body, name);
                CollectionAssert.IsEmpty(a.Headers.Keys, $"{name}: no header at all");
            }
        }
    }
}
