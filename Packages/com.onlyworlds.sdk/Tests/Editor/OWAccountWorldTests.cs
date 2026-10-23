using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// CreateAccountWorldAsync (POST /account/worlds, keel #363's key_name, keel #571's source and producer, keel #579's
    /// AccountWorldCreated): the account Bearer, a body of the name and only the members given, in keel's spelling; the
    /// PIN dance (an account with no PIN answers 422 param pin until one is sent); the 201 read as keel's document gives
    /// it. The TypeScript SDK's test/account-worlds.test.mjs (4.66.0, 9b4d901), ported; the Python package has no such
    /// call. Nothing here reaches a server.
    /// </summary>
    public class OWAccountWorldTests
    {
        private const string Api = "https://www.onlyworlds.com/api/v2";

        /// <summary>The 201 as keel's handler builds it: the world's row (role owner) plus key.</summary>
        private static readonly JObject Created = new JObject
        {
            ["id"] = "018f0000-0000-7000-8000-0000000000f1", ["name"] = "Ostvik", ["role"] = "owner", ["public_read"] = false,
            ["keys"] = new JArray(new JObject { ["name"] = "atlas · windows", ["last4"] = "abcd", ["scope"] = "WRITE", ["revoked"] = false }),
            ["key"] = "ow_w_fullwritekeyabcd",
        };

        private static JObject Source => new JObject { ["server"] = "https://www.onlyworlds.com", ["world"] = "w0", ["at"] = "2026-10-23T08:00:00Z" };

        private static JObject Producer => new JObject { ["tool"] = "atlas", ["version"] = "1.83.0", ["input"] = "folder" };

        private static FakeTransport Answering() => new FakeTransport { Status = 201, ResponseBody = Created.ToString() };

        private static OWClient Account(FakeTransport t)
            => new OWClient(new OWClientConfig { ApiKey = "ow_a_acct", Transport = t, BaseUrl = Api });

        /// <summary>The body sent, parsed verbatim (a plain parse would turn the line's <c>at</c> into a date).</summary>
        private static JObject Sent(FakeTransport t) => OWJson.ParseObject(t.Last.Body);

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        [Test]
        public async Task CreateAccountWorld_PostsTheNameAlone_WithTheAccountBearer_AndReadsThe201()
        {
            var t = Answering();
            var made = await Account(t).CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "Ostvik" });

            Assert.AreEqual(1, t.Calls.Count);
            Assert.AreEqual($"{Api}/account/worlds", t.Last.Url);
            Assert.AreEqual("POST", t.Last.Method);
            Assert.AreEqual(@"{""name"":""Ostvik""}", t.Last.Body);
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(t.Last));
            Assert.AreEqual("Bearer ow_a_acct", t.Last.Headers["Authorization"]);

            Assert.AreEqual("018f0000-0000-7000-8000-0000000000f1", made.Id);
            Assert.AreEqual("Ostvik", made.Name);
            Assert.AreEqual("owner", made.Role);
            Assert.IsFalse(made.PublicRead);
            Assert.AreEqual("ow_w_fullwritekeyabcd", made.Key);
            Assert.AreEqual(1, made.Keys.Count);
            Assert.AreEqual("atlas · windows", made.Keys[0].Name);
            Assert.AreEqual("abcd", made.Keys[0].Last4);
            Assert.AreEqual("WRITE", made.Keys[0].Scope);
            Assert.IsFalse(made.Keys[0].Revoked);
        }

        [Test]
        public async Task CreateAccountWorld_EachMemberAlone_IsSentInKeelsSpelling_AndNothingElseBesideTheName()
        {
            var cases = new (OWAccountWorldCreate Given, JObject Sent)[]
            {
                (new OWAccountWorldCreate { Pin = "4821" }, new JObject { ["pin"] = "4821" }),
                (new OWAccountWorldCreate { KeyName = "atlas · windows" }, new JObject { ["key_name"] = "atlas · windows" }),
                (new OWAccountWorldCreate { Source = Source }, new JObject { ["source"] = Source }),
                (new OWAccountWorldCreate { Producer = Producer }, new JObject { ["producer"] = Producer }),
            };

            foreach (var (given, sent) in cases)
            {
                var t = Answering();
                given.Name = "Ostvik";
                await Account(t).CreateAccountWorldAsync(given);

                var expected = new JObject { ["name"] = "Ostvik" };
                expected.Merge(sent);
                Assert.IsTrue(JToken.DeepEquals(expected, Sent(t)), $"{sent}: sent {t.Last.Body}");
            }

            var all = Answering();
            await Account(all).CreateAccountWorldAsync(new OWAccountWorldCreate
            {
                Name = "Ostvik", Pin = "4821", KeyName = "k", Source = Source, Producer = Producer,
            });
            var whole = new JObject
            {
                ["name"] = "Ostvik", ["pin"] = "4821", ["key_name"] = "k", ["source"] = Source, ["producer"] = Producer,
            };
            Assert.IsTrue(JToken.DeepEquals(whole, Sent(all)), all.Last.Body);
        }

        [Test]
        public async Task CreateAccountWorld_AMemberLeftNull_IsLeftOutOfTheBody_NeverSentAsAKey()
        {
            // npm's undefined: left out. npm sends a null it is given (key_name, source, producer); keel reads a null and a
            // missing key alike on this route (accounts/api.py create_world), so this client never sends one.
            var t = Answering();
            await Account(t).CreateAccountWorldAsync(new OWAccountWorldCreate
            {
                Name = "Ostvik", Pin = null, KeyName = null, Source = null, Producer = null,
            });

            Assert.AreEqual(@"{""name"":""Ostvik""}", t.Last.Body);
        }

        [Test]
        public async Task CreateAccountWorld_AProvenanceLine_IsSentAsGiven_ItsOwnNullsIncluded()
        {
            // keel keeps source and producer as given, so a line's own unknown parts go as they are.
            var t = Answering();
            var source = new JObject { ["server"] = "https://www.onlyworlds.com", ["world"] = "w0", ["at"] = null };
            await Account(t).CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "Ostvik", Source = source });

            Assert.IsTrue(JToken.DeepEquals(new JObject { ["name"] = "Ostvik", ["source"] = source }, Sent(t)), t.Last.Body);
        }

        [Test]
        public async Task CreateAccountWorld_AnAgentClientWithANameAndAPin_SendsNoAgentHeader_NoClientName_NoPin_NoIdempotencyKey()
        {
            // The document lists no header on this route, and an account token never sends API-Pin.
            var t = Answering();
            var c = new OWClient(new OWClientConfig
            {
                ApiKey = "ow_a_acct", ApiPin = "1234", Transport = t, BaseUrl = Api, Agent = true, Client = "atlas",
            });
            await c.CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "Ostvik" });

            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(t.Last));
        }

        [Test]
        public async Task CreateAccountWorld_ThePinDance_AFreshAccounts422ParamPin_ThenTheRetryWithThePinMakesTheWorld()
        {
            var t = new FakeTransport();
            var pinRequired = new JObject
            {
                ["error"] = new JObject
                {
                    ["type"] = "invalid_request_error", ["code"] = "invalid_request",
                    ["message"] = "Your account has no PIN yet. Provide 'pin' (4 digits, 1000-9999) with your first world; "
                        + "it becomes your account PIN and the write wall on all your worlds.",
                    ["param"] = "pin",
                },
            };
            t.Then(422, pinRequired.ToString());
            t.Then(201, Created.ToString());
            var c = Account(t);

            var e = Assert.ThrowsAsync<OWApiError>(() => c.CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "Ostvik", KeyName = "k" }));
            Assert.AreEqual(422, e.StatusCode);
            Assert.AreEqual("invalid_request", e.Code);
            Assert.AreEqual("pin", e.Param);
            Assert.IsTrue(e.IsValidationError);

            var made = await c.CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "Ostvik", KeyName = "k", Pin = "4821" });
            Assert.AreEqual("ow_w_fullwritekeyabcd", made.Key);
            Assert.AreEqual(@"{""name"":""Ostvik"",""key_name"":""k""}", t.Calls[0].Body);
            Assert.AreEqual(@"{""name"":""Ostvik"",""pin"":""4821"",""key_name"":""k""}", t.Calls[1].Body);
        }

        [Test]
        public void CreateAccountWorld_NoName_NoWorld_OrAnyKeyButAnAccountToken_IsRefusedBeforeAnythingIsSent()
        {
            var t = Answering();
            var c = Account(t);

            var noName = Assert.ThrowsAsync<ArgumentException>(() => c.CreateAccountWorldAsync(new OWAccountWorldCreate()));
            StringAssert.Contains("Name is required", noName.Message);
            Assert.ThrowsAsync<ArgumentNullException>(() => c.CreateAccountWorldAsync(null));

            foreach (var key in new[] { "ow_w_world", "ow_r_read", "0123456789" })
            {
                var other = new OWClient(new OWClientConfig { ApiKey = key, ApiPin = "1234", Transport = t, BaseUrl = Api });
                var e = Assert.ThrowsAsync<InvalidOperationException>(
                    () => other.CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "Ostvik" }), key);
                StringAssert.Contains("account token (ow_a_)", e.Message);
            }

            Assert.AreEqual(0, t.Calls.Count, "nothing was sent");
        }

        [Test]
        public void ThePublishedTypes_TheRequestsFiveKeys_TheAnswerTheWorldRowPlusKey()
        {
            // npm's published-types vector, in keel's spelling: what each class maps is what the wire carries.
            CollectionAssert.AreEqual(new[] { "name", "pin", "key_name", "source", "producer" }, Mapped(typeof(OWAccountWorldCreate)));
            CollectionAssert.AreEqual(new[] { "id", "name", "role", "public_read", "keys", "key" }, Mapped(typeof(OWAccountWorldCreated)));
            CollectionAssert.AreEqual(new[] { "name", "last4", "scope", "revoked" }, Mapped(typeof(OWAccountWorldKey)));

            var method = typeof(OWClient).GetMethod(nameof(OWClient.CreateAccountWorldAsync));
            Assert.AreEqual(typeof(Task<OWAccountWorldCreated>), method.ReturnType);
            Assert.AreEqual(typeof(OWAccountWorldCreate), method.GetParameters()[0].ParameterType);
        }

        private static string[] Mapped(Type model)
            => model.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName)
                .Where(n => n != null).ToArray();
    }
}
