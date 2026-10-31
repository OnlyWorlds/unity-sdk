using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The TypeScript SDK's account calls this client gained in its second tranche: the account's worlds and their keys
    /// (<see cref="OWClient.ListAccountWorldsAsync"/>, <see cref="OWClient.MintWorldKeyAsync"/>,
    /// <see cref="OWClient.RevokeWorldKeyAsync"/>). What each sends, what it answers, and what is refused before anything
    /// is sent, on every kind of client.
    /// </summary>
    public class OWAccountRouteTests
    {
        private const string Base = "https://example.test/api/v2";

        // An agent account client with its own name and a PIN: every header a write could carry, so an account route that
        // must leave one out is seen leaving it out (an account token sends its Bearer alone).
        private static OWClient Make(FakeTransport t, string key = "ow_a_acct", string pin = "1234", string world = null)
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

        private static string[] HeaderNames(OWRequest r) => r.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        /// <summary>Every account call of this tranche, each with a body it can read, for the refusal sweep below.</summary>
        private static readonly (string Name, Func<OWClient, Task> Run)[] Calls =
        {
            ("ListAccountWorldsAsync", c => c.ListAccountWorldsAsync()),
            ("MintWorldKeyAsync", c => c.MintWorldKeyAsync("w1")),
            ("RevokeWorldKeyAsync", c => c.RevokeWorldKeyAsync("w1", "k1")),
            ("AcceptJoinAsync", c => c.AcceptJoinAsync("ow_j_code")),
        };

        // Kills: a world key, a read key, a legacy key or no key reaching an /account route, which keel answers 401; the
        // TypeScript SDK refuses each with a TypeError before sending.
        [Test]
        public void EveryAccountCall_AnyKeyButAnAccountToken_IsRefused_NamingTheCall_NothingSent()
        {
            var clients = new (string Name, string Key, string World)[]
            {
                ("a write key", "ow_w_test", null), ("a read key", "ow_r_test", null), ("a legacy key", "0123456789", null),
                ("no key, World set", null, "w1"),
            };

            foreach (var call in Calls)
            {
                foreach (var (name, key, world) in clients)
                {
                    var t = new FakeTransport();
                    var e = Assert.ThrowsAsync<InvalidOperationException>(() => call.Run(Make(t, key, "1234", world)),
                        $"{call.Name} on {name}");
                    StringAssert.StartsWith(call.Name + " needs an account token (ow_a_)", e.Message, name);
                    StringAssert.Contains(key == null ? "holds none" : "holds a ", e.Message, name);
                    StringAssert.EndsWith("Nothing was sent.", e.Message, name);
                    Assert.IsEmpty(t.Calls, $"{call.Name} on {name}: nothing sent");
                }
            }
        }

        // Kills: an account route that adds world= (or refuses) on an account client with World set: the /account routes
        // name their world in the path, or none.
        [Test]
        public async Task EveryAccountCall_OnAnAccountTokenWithWorld_SendsNoWorld()
        {
            foreach (var call in Calls)
            {
                var t = new FakeTransport { ResponseBody = "{}" };
                await call.Run(Make(t, world: "w9"));
                Assert.AreEqual("", new Uri(t.Last.Url).Query, call.Name);
                Assert.AreEqual(new[] { "Authorization" }, HeaderNames(t.Last).Where(h => h != "Content-Type").ToArray(), call.Name);
            }
        }

        // -- ListAccountWorldsAsync -------------------------------------------------------------------------

        private const string WorldsBody =
            @"{""data"":[{""id"":""w1"",""name"":""Ostvik"",""role"":""owner"",""public_read"":true,""keys"":["
            + @"{""name"":""atlas"",""last4"":""abcd"",""scope"":""WRITE"",""revoked"":false},"
            + @"{""name"":"""",""last4"":""wxyz"",""scope"":""READ"",""revoked"":true}]},"
            + @"{""id"":""w2"",""name"":""Brine"",""role"":""collaborator"",""public_read"":false,""keys"":[],""member_role"":""co_builder""}]}";

        // The TypeScript SDK's listAccountWorlds() (4.45.0): GET /account/worlds, the Bearer alone (no PIN, no write
        // header), the rows of keel's { data } in its order. Kills: the wrong verb or path, a query, the PIN or a write
        // header sent, the rows reordered or a key misread (member_role on a collaborator's row, a key's scope or revoked),
        // or an answer without data thrown on.
        [Test]
        public async Task ListAccountWorlds_SendsGetAccountWorlds_TheBearerAlone_AndAnswersTheRowsInOrder()
        {
            var t = new FakeTransport { ResponseBody = WorldsBody };
            var worlds = await Make(t).ListAccountWorldsAsync();

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/account/worlds", a.Url, "the path, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
            Assert.AreEqual("Bearer ow_a_acct", a.Headers["Authorization"]);

            Assert.AreEqual(2, worlds.Count);
            Assert.AreEqual(("w1", "Ostvik", "owner", (string)null, true), (worlds[0].Id, worlds[0].Name, worlds[0].Role, worlds[0].MemberRole, worlds[0].PublicRead));
            Assert.AreEqual(2, worlds[0].Keys.Count);
            Assert.AreEqual(("atlas", "abcd", "WRITE", false), (worlds[0].Keys[0].Name, worlds[0].Keys[0].Last4, worlds[0].Keys[0].Scope, worlds[0].Keys[0].Revoked));
            Assert.AreEqual(("", "wxyz", "READ", true), (worlds[0].Keys[1].Name, worlds[0].Keys[1].Last4, worlds[0].Keys[1].Scope, worlds[0].Keys[1].Revoked));
            Assert.AreEqual(("w2", "collaborator", "co_builder", false), (worlds[1].Id, worlds[1].Role, worlds[1].MemberRole, worlds[1].PublicRead));
            CollectionAssert.IsEmpty(worlds[1].Keys);

            t.ResponseBody = "{}";
            CollectionAssert.IsEmpty(await Make(t).ListAccountWorldsAsync(), "no data: no rows");
        }

        // -- MintWorldKeyAsync ------------------------------------------------------------------------------

        private const string MintedBody =
            @"{""id"":""k9"",""name"":"""",""last4"":""q7r8"",""scope"":""WRITE"",""last_used_at"":null,""revoked"":false,"
            + @"""kept"":false,""member"":{""id"":""m1"",""username"":""ada""},""key"":""ow_w_fullmintedkeyq7r8""}";

        // The TypeScript SDK's mintWorldKey() (4.45.0; no X-OW-Agent from 4.120.0, 0e311fe): POST
        // /account/worlds/{id}/keys, the Bearer alone. keel's document declares no body on the route, so none is sent
        // (and no Content-Type), where the TypeScript SDK sends { scope?, name?, keep? }. Kills: the wrong verb or path,
        // the world id unescaped, a body (even {}) or a Content-Type sent, X-OW-Agent sent on an agent client (the
        // TypeScript SDK's way before 4.120.0), the PIN sent, or the answer's key, id or member misread.
        [Test]
        public async Task MintWorldKey_PostsKeys_NoBody_TheBearerAlone_AndAnswersTheKeyOnce()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = MintedBody };
            var minted = await Make(t).MintWorldKeyAsync("w 1/ä");

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%201%2F%C3%A4/keys", a.Url, "the path, the id escaped, no query");
            Assert.IsNull(a.Body, "keel's document declares no body here");
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));

            Assert.AreEqual("k9", minted.Id);
            Assert.AreEqual("ow_w_fullmintedkeyq7r8", minted.Key);
            Assert.AreEqual("q7r8", minted.Last4);
            Assert.AreEqual("WRITE", minted.Scope);
            Assert.IsNull(minted.LastUsedAt);
            Assert.IsFalse(minted.Revoked);
            Assert.IsFalse(minted.Kept);
            Assert.AreEqual("ada", (string)minted.Member["username"]);
        }

        // Kills: a 409 pin_required read as anything but IsPinRequired, or the error's message carrying a key.
        [Test]
        public void MintWorldKey_A409PinRequired_IsPinRequired()
        {
            var t = new FakeTransport
            {
                Status = 409,
                ResponseBody = @"{""error"":{""type"":""conflict_error"",""code"":""pin_required"",""message"":""Set an account PIN first.""}}",
            };
            var e = Assert.ThrowsAsync<OWApiError>(() => Make(t).MintWorldKeyAsync("w1"));
            Assert.IsTrue(e.IsPinRequired);
            Assert.AreEqual(409, e.StatusCode);
        }

        // -- RevokeWorldKeyAsync ----------------------------------------------------------------------------

        // The TypeScript SDK's revokeWorldKey() (4.78.0): DELETE /account/worlds/{id}/keys/{key_id}, the Bearer alone, a
        // 204 with no body. Kills: the wrong verb or path, either id unescaped, a body, the PIN or a write header sent, or
        // an empty 204 read as an error.
        [Test]
        public async Task RevokeWorldKey_SendsDeleteKeysKeyId_TheBearerAlone_AndTakesThe204()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).RevokeWorldKeyAsync("w/1", "k 9");

            var a = t.Calls.Single();
            Assert.AreEqual("DELETE", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%2F1/keys/k%209", a.Url, "the path, both ids escaped, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
        }

        // -- AcceptJoinAsync --------------------------------------------------------------------------------

        private const string JoinedBody =
            @"{""id"":""m7"",""world_id"":""w1"",""world_name"":""Ostvik"",""role"":""contributor"","
            + @"""joined_at"":""2026-10-31T01:00:00+00:00"",""roster_character"":null}";

        // The TypeScript SDK's acceptJoin() (4.12.0): POST /account/join with { code } alone, the Bearer, no PIN (accepting
        // needs none) and no write header. Kills: the wrong verb or path, the code under another key or beside others, the
        // PIN or a write header sent, or the membership misread.
        [Test]
        public async Task AcceptJoin_PostsAccountJoin_TheCodeAlone_TheBearer_AndAnswersTheMembership()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = JoinedBody };
            var joined = await Make(t).AcceptJoinAsync("ow_j_abc");

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/join", a.Url, "the path, no query");
            Assert.AreEqual(@"{""code"":""ow_j_abc""}", a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(a));

            Assert.AreEqual(("m7", "w1", "Ostvik", "contributor", "2026-10-31T01:00:00+00:00", (string)null),
                (joined.Id, joined.WorldId, joined.WorldName, joined.Role, joined.JoinedAt, joined.RosterCharacter));
        }

        // Kills: an empty code reaching the transport (keel's 404 would read as a bad code, not a missing one), and the
        // code itself in the message.
        [Test]
        public void AcceptJoin_NoCode_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            foreach (var code in new[] { null, "" })
            {
                var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).AcceptJoinAsync(code), code ?? "null");
                StringAssert.Contains("a join code is required", e.Message);
                StringAssert.Contains("Nothing was sent", e.Message);
            }

            Assert.IsEmpty(t.Calls);
        }

        // Kills: keel's two refusals read as anything else (each leaves the code unspent).
        [Test]
        public void AcceptJoin_A409AlreadyMember_IsAlreadyMember_AndA404IsNotFound()
        {
            var t = new FakeTransport
            {
                Status = 409,
                ResponseBody = @"{""error"":{""type"":""conflict_error"",""code"":""already_member"",""message"":""Already a member.""}}",
            };
            var member = Assert.ThrowsAsync<OWApiError>(() => Make(t).AcceptJoinAsync("ow_j_abc"));
            Assert.IsTrue(member.IsAlreadyMember);
            Assert.IsFalse(member.IsPinRequired);

            t.Status = 404;
            t.ResponseBody = @"{""error"":{""type"":""not_found_error"",""code"":""not_found"",""message"":""Not a valid link.""}}";
            var bad = Assert.ThrowsAsync<OWApiError>(() => Make(t).AcceptJoinAsync("ow_j_abc"));
            Assert.IsTrue(bad.IsNotFound);
            Assert.IsFalse(bad.IsAlreadyMember);
        }
    }
}
