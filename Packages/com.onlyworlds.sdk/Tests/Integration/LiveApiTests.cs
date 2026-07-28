using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Integration
{
    /// <summary>
    /// Smoke tests against the real v2 API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXCLUDED FROM THE DEFAULT RUN. These need credentials and a network, and they fail for
    /// reasons unrelated to this code -- a flaky connection, a server deploy, an expired key. A
    /// suite that goes red for those reasons stops being believed.
    /// </para>
    /// <para>
    /// Their job is narrow and important: the fakes prove the client's logic; ONE real run proves
    /// the fake. A fake that has silently drifted from the wire is worse than no fake, because
    /// every test built on it is confidently wrong.
    /// </para>
    /// <para>
    /// Run them from the Test Runner by selecting the Integration category, or set the credentials
    /// below. They read from environment variables so no key is ever committed.
    /// </para>
    /// </remarks>
    [Category("Integration")]
    [Explicit("Needs credentials and network. Run deliberately, not on every build.")]
    public class LiveApiTests
    {
        private static string Key => System.Environment.GetEnvironmentVariable("MANIFEST_KEY");
        private static string Pin => System.Environment.GetEnvironmentVariable("MANIFEST_PIN");

        private static OWClient MakeClient()
        {
            if (string.IsNullOrEmpty(Key))
            {
                Assert.Ignore("MANIFEST_KEY not set -- skipping live tests.");
            }

            return new OWClient(new OWClientConfig
            {
                ApiKey = Key,
                ApiPin = Pin,
                Transport = new UnityWebRequestTransport(),
            });
        }

        [Test]
        public void KeyKind_IsDetectedFromTheRealKey()
        {
            if (string.IsNullOrEmpty(Key)) Assert.Ignore("MANIFEST_KEY not set.");
            Assert.AreEqual(OWKeyKind.Write, OWKey.DetectKind(Key));
        }

        [Test]
        public async Task GetWorld_ReturnsTheManifest()
        {
            var world = await MakeClient().GetWorldAsync();

            Assert.IsNotNull(world);
            Assert.IsNotNull(world["id"], "A world must carry an id.");
            TestContext.WriteLine($"World: {world["name"]} ({world["id"]})");
        }

        [Test]
        public async Task ListCharacters_ParsesRealElements()
        {
            var page = await MakeClient().ListAsync<OWCharacter>("character");

            Assert.IsNotNull(page);
            Assert.IsNotNull(page.Data);
            TestContext.WriteLine($"{page.Data.Count} characters, has_more={page.HasMore}");

            foreach (var c in page.Data)
            {
                // Only name is required. Everything else may legitimately be unset, and the point
                // of this assertion is that unset does not crash the reader.
                Assert.IsNotNull(c.Name, "Every element carries a name.");
                TestContext.WriteLine($"  {c.Name} [{c.Supertype}] id={c.Id}");
            }
        }

        [Test]
        public async Task SparseFieldsets_Work()
        {
            // The Sealed Cards discipline in code: ask for identity only, never bodies.
            var page = await MakeClient().ListAsync<OWElement>("narrative", new OWListParams
            {
                Fields = new[] { "id", "name", "supertype" },
                Limit = 5,
            });

            Assert.IsNotNull(page.Data);
            TestContext.WriteLine($"{page.Data.Count} narratives (sparse)");
        }

        [Test]
        public async Task Changes_HeadOnly_ReturnsATip()
        {
            var page = await MakeClient().ChangesAsync(headOnly: true);

            Assert.IsNotNull(page);
            Assert.GreaterOrEqual(page.Head, 0, "head is the server's current change_seq tip.");
            TestContext.WriteLine($"head={page.Head}");
        }

        [Test]
        public async Task NullableInts_ArriveAsExplicitNull_OnRealData()
        {
            // The wire fact the whole nullable design rests on, verified against production rather
            // than a fixture: the API sends explicit null, not omission.
            var client = MakeClient();
            var page = await client.ListAsync<JObject>("character", new OWListParams { Limit = 5 });

            var sawExplicitNull = false;
            foreach (var element in page.Data)
            {
                foreach (var property in element.Properties())
                {
                    if (property.Value.Type == JTokenType.Null)
                    {
                        sawExplicitNull = true;
                        TestContext.WriteLine($"explicit null: {property.Name}");
                    }
                }
            }

            Assert.IsTrue(sawExplicitNull,
                "Expected at least one explicit null on real data -- if this fails, the wire "
                + "contract changed and SerializableNullable's premise needs re-checking.");
        }

        [Test]
        public async Task BadKey_ThrowsTypedAuthError()
        {
            var client = new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_definitely_not_a_real_key",
                ApiPin = "0000",
                Transport = new UnityWebRequestTransport(),
            });

            var ex = Assert.ThrowsAsync<OWApiError>(async () => await client.GetWorldAsync());
            TestContext.WriteLine($"status={ex.StatusCode} code={ex.Code} doc={ex.DocUrl}");
            Assert.IsTrue(ex.IsAuthError || ex.IsValidationError);
        }
    }
}
