using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's calls this client gained in its first tranche (0.27.0): <see cref="OWClient.HealthAsync"/>,
    /// <see cref="OWClient.UpsertAsync{T}"/>, <see cref="OWClient.AdoptAsync{T}"/>, <see cref="OWClient.HideAsync"/>,
    /// <see cref="OWClient.UnhideAsync"/>, <see cref="OWClient.ListHiddenAsync"/>.
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

        // -- UpsertAsync ------------------------------------------------------------------------------------

        private const string Proposal =
            @"{""proposal"":{""kind"":""proposal"",""id"":""p1"",""element_type"":""location"",""element_id"":""l1"",""state"":""open""}}";

        // The TypeScript SDK's upsert() (3.0.0): PUT /{type}/{id}/ with the element cleaned as every element write is,
        // the PIN, X-OW-Agent and X-OW-Client, and no Idempotency-Key or If-Match (keel's document declares neither
        // there). Kills: the wrong verb or path, the read-only fields sent, a null scalar string sent as null (this SDK's
        // ruling sends ""), an extension key stripped, the PIN or either write header left off, or the answer reshaped.
        [Test]
        public async Task Upsert_SendsPutTypeId_TheBodyCleaned_WithThePinAgentAndClient_AndAnswersTheElement()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""l1"",""name"":""Harbour"",""change_seq"":4}" };
            var answer = await Make(t).UpsertAsync<OWLocation>("location", "l1", new JObject
            {
                ["id"] = "l1", ["name"] = "Harbour", ["description"] = null, ["x_atlas_pinned"] = true,
                ["world"] = "w9", ["type"] = "location", ["created_at"] = "2026-01-01T00:00:00Z",
                ["updated_at"] = "2026-01-02T00:00:00Z", ["change_seq"] = 3, ["known_of"] = false,
            });

            Assert.AreEqual("l1", answer.Id);
            Assert.AreEqual("Harbour", answer.Name);

            var a = t.Calls.Single();
            Assert.AreEqual("PUT", a.Method);
            Assert.AreEqual(Base + "/location/l1/", a.Url, "the path, no query");
            Assert.IsTrue(JToken.DeepEquals(
                JObject.Parse(@"{""id"":""l1"",""name"":""Harbour"",""description"":"""",""x_atlas_pinned"":true}"),
                JObject.Parse(a.Body)), a.Body);
            CollectionAssert.AreEquivalent(
                new[] { "API-Key", "API-Pin", "Content-Type", "X-OW-Agent", "X-OW-Client" }, a.Headers.Keys.ToArray());
            Assert.AreEqual("1234", a.Headers["API-Pin"]);
            Assert.AreEqual("1", a.Headers["X-OW-Agent"]);
            Assert.AreEqual("atlas", a.Headers["X-OW-Client"]);
        }

        // npm 4.58.0's vector: an agent seat's PUT on an existing element, in a world that takes its writes as proposals,
        // answers 202. Kills: the 202's proposal handed back as if it were the element.
        [Test]
        public void Upsert_A202_IsAnOWProposedError_CarryingTheProposal()
        {
            var t = new FakeTransport { Status = 202, ResponseBody = Proposal };
            var e = Assert.ThrowsAsync<OWProposedError>(
                () => Make(t).UpsertAsync<JObject>("location", "l1", new JObject { ["name"] = "Harbour" }));
            Assert.AreEqual(202, e.StatusCode);
            Assert.AreEqual("p1", e.Proposal.Id);
        }

        // Kills: a known-of stub PUT (it would blank every field it does not keep) or a null element reaching the
        // transport.
        [Test]
        public void Upsert_AKnownOfStub_OrNoElement_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var stub = Assert.ThrowsAsync<ArgumentException>(() => Make(t).UpsertAsync<JObject>("location", "l1",
                new JObject { ["id"] = "l1", ["name"] = "Harbour", ["known_of"] = true }));
            StringAssert.StartsWith("upsert: this body is a known-of stub", stub.Message);

            Assert.ThrowsAsync<ArgumentNullException>(() => Make(t).UpsertAsync<JObject>("location", "l1", null));
            Assert.IsEmpty(t.Calls);
        }

        // -- AdoptAsync -------------------------------------------------------------------------------------

        // The TypeScript SDK's adopt() (4.43.0): POST /{type}/{id}/adopt with { member } alone, a membership id or
        // 'owner', the PIN, X-OW-Agent and X-OW-Client (both declared there). Kills: the wrong verb or path, the member
        // under another key or beside others, 'owner' turned into null, the PIN or either write header left off, or
        // the answer reshaped.
        [Test]
        public async Task Adopt_SendsPostAdopt_TheMemberAlone_WithThePinAgentAndClient_AndAnswersTheElement()
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""l1"",""name"":""Harbour"",""created_by"":""m1""}" };
            var c = Make(t);

            var answer = await c.AdoptAsync<OWLocation>("location", "l1", "m1");
            Assert.AreEqual("l1", answer.Id);
            await c.AdoptAsync<JObject>("location", "l1", "owner");

            Assert.AreEqual(2, t.Calls.Count);
            var a = t.Calls[0];
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/location/l1/adopt", a.Url, "the path, no query");
            Assert.AreEqual(@"{""member"":""m1""}", a.Body);
            CollectionAssert.AreEquivalent(
                new[] { "API-Key", "API-Pin", "Content-Type", "X-OW-Agent", "X-OW-Client" }, a.Headers.Keys.ToArray());
            Assert.AreEqual("atlas", a.Headers["X-OW-Client"]);

            Assert.AreEqual(@"{""member"":""owner""}", t.Calls[1].Body, "'owner' goes as the word");
        }

        // Kills: an adopt with no member reaching the transport (keel would 422; the TypeScript SDK refuses it first).
        [Test]
        public void Adopt_NoMember_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            foreach (var member in new[] { null, "" })
            {
                var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).AdoptAsync<JObject>("location", "l1", member),
                    member ?? "null");
                StringAssert.Contains("member must be a membership id or 'owner'", e.Message);
                StringAssert.Contains("Nothing was sent", e.Message);
            }

            Assert.IsEmpty(t.Calls);
        }

        // -- HideAsync, UnhideAsync, ListHiddenAsync --------------------------------------------------------

        // The TypeScript SDK's hide() (4.45.0): PUT /{type}/{id}/hidden with {}, the PIN, a 204 with no body. keel's
        // document declares no header there, so neither X-OW-Agent nor X-OW-Client goes, where the TypeScript SDK sends
        // X-OW-Agent on an agent client (the wire wins). Kills: the wrong verb or path, a body with keys or none, the PIN
        // left off, a write header sent, or an empty 204 read as an error.
        [Test]
        public async Task Hide_SendsPutHidden_AnEmptyBody_WithThePin_NoAgentOrClientHeader()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).HideAsync("location", "l1");

            var a = t.Calls.Single();
            Assert.AreEqual("PUT", a.Method);
            Assert.AreEqual(Base + "/location/l1/hidden", a.Url, "the path, no query");
            Assert.AreEqual("{}", a.Body);
            CollectionAssert.AreEquivalent(new[] { "API-Key", "API-Pin", "Content-Type" }, a.Headers.Keys.ToArray());
            Assert.AreEqual("1234", a.Headers["API-Pin"]);
        }

        // The TypeScript SDK's unhide() (4.45.0): DELETE /{type}/{id}/hidden, no body, the PIN; no write header, as on
        // the hide. Kills: the wrong verb or path, a body sent, the PIN left off, or a write header sent.
        [Test]
        public async Task Unhide_SendsDeleteHidden_NoBody_WithThePin_NoAgentOrClientHeader()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).UnhideAsync("location", "l1");

            var a = t.Calls.Single();
            Assert.AreEqual("DELETE", a.Method);
            Assert.AreEqual(Base + "/location/l1/hidden", a.Url, "the path, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEquivalent(new[] { "API-Key", "API-Pin" }, a.Headers.Keys.ToArray());
        }

        // The TypeScript SDK's listHidden() (4.48.0): GET /world/hidden, the key alone (a read: no PIN), the rows of
        // keel's { data } in its order, newest first. Kills: the wrong path, a query or the PIN sent, the rows reordered or
        // a key misread, set_by's null (the owner) read as anything else, or an answer without data thrown on.
        [Test]
        public async Task ListHidden_SendsGetWorldHidden_TheKeyAlone_AndAnswersTheRowsInOrder()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""data"":[{""type"":""location"",""id"":""l2"",""set_at"":""2026-10-25T08:30:00Z"",""set_by"":""m-42""},"
                    + @"{""type"":""character"",""id"":""c1"",""set_at"":""2026-10-24T08:30:00Z"",""set_by"":null}]}",
            };
            var rows = await Make(t).ListHiddenAsync();

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/world/hidden", a.Url, "the path, no query");
            Assert.AreEqual(new[] { "API-Key" }, a.Headers.Keys.ToArray(), "a read: the key alone");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(("location", "l2", "2026-10-25T08:30:00Z", "m-42"), (rows[0].Type, rows[0].Id, rows[0].SetAt, rows[0].SetBy));
            Assert.AreEqual(("character", "c1", "2026-10-24T08:30:00Z", (string)null), (rows[1].Type, rows[1].Id, rows[1].SetAt, rows[1].SetBy));

            t.ResponseBody = @"{""data"":[]}";
            CollectionAssert.IsEmpty(await Make(t).ListHiddenAsync(), "none hidden");
            t.ResponseBody = "{}";
            CollectionAssert.IsEmpty(await Make(t).ListHiddenAsync(), "no data: no rows");
        }
    }
}
