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
    /// <see cref="OWClient.RevokeWorldKeyAsync"/>), a person's join (<see cref="OWClient.AcceptJoinAsync"/>), a world's
    /// invite links (<see cref="OWClient.CreateWorldLinkAsync"/>, <see cref="OWClient.ListWorldLinksAsync"/>,
    /// <see cref="OWClient.RevokeWorldLinkAsync"/>) and seats for one's own AI (<see cref="OWClient.CreateSeatAsync"/>,
    /// <see cref="OWClient.ListSeatsAsync"/>, <see cref="OWClient.RekeySeatAsync"/>, <see cref="OWClient.RemoveSeatAsync"/>).
    /// What each sends, what it answers, and what is refused before anything is sent, on every kind of client.
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
            ("CreateWorldLinkAsync", c => c.CreateWorldLinkAsync("w1", new OWWorldLinkCreate { Reader = "person" })),
            ("ListWorldLinksAsync", c => c.ListWorldLinksAsync("w1")),
            ("RevokeWorldLinkAsync", c => c.RevokeWorldLinkAsync("w1", "l1")),
            ("CreateSeatAsync", c => c.CreateSeatAsync("w1", "Scout")),
            ("ListSeatsAsync", c => c.ListSeatsAsync("w1")),
            ("RekeySeatAsync", c => c.RekeySeatAsync("w1", "s1")),
            ("RemoveSeatAsync", c => c.RemoveSeatAsync("w1", "s1")),
            ("ListAccountTokensAsync", c => c.ListAccountTokensAsync()),
            ("RevokeAccountTokenAsync", c => c.RevokeAccountTokenAsync("t1")),
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
        // /account/worlds/{id}/keys, the Bearer alone. With no option given no body is sent (and no Content-Type): keel's
        // document makes the body optional, all of it, and says no body mints a write key with no name, not kept, where
        // the TypeScript SDK sends {}. Kills: the wrong verb or path, the world id unescaped, a body (even {}) or a
        // Content-Type sent with nothing given, X-OW-Agent sent on an agent client (the TypeScript SDK's way before
        // 4.120.0), the PIN sent, or the answer's key, id or member misread.
        [Test]
        public async Task MintWorldKey_PostsKeys_NoBody_TheBearerAlone_AndAnswersTheKeyOnce()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = MintedBody };
            var minted = await Make(t).MintWorldKeyAsync("w 1/ä");

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%201%2F%C3%A4/keys", a.Url, "the path, the id escaped, no query");
            Assert.IsNull(a.Body, "no option given: no body, keel's defaults");
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

        // The TypeScript SDK's mintWorldKey({ scope, name, keep }) (4.45.0), as keel's document declares the body (keel
        // a5158cb): each option sent only when given, under its own key, in the TypeScript SDK's order, as given (keel
        // trims the name and reads the scope in any case); null or empty sends nothing. Kills: an option sent under
        // another key or when not given, keep sent as a string, the name trimmed or the scope's case changed before
        // sending, the order moved, or a body sent without its Content-Type.
        [TestCase("read", null, null, @"{""scope"":""read""}")]
        [TestCase("write", null, null, @"{""scope"":""write""}")]
        [TestCase("r", null, null, @"{""scope"":""r""}")]
        [TestCase("READ", null, null, @"{""scope"":""READ""}")]
        [TestCase("", "Atlas on my laptop", null, @"{""name"":""Atlas on my laptop""}")]
        [TestCase(null, null, true, @"{""keep"":true}")]
        [TestCase(null, "", false, @"{""keep"":false}")]
        [TestCase("read", "  Atlas  ", true, @"{""scope"":""read"",""name"":""  Atlas  "",""keep"":true}")]
        public async Task MintWorldKey_EachOptionGiven_IsSentAsGiven_TheRestLeftOut(
            string scope, string name, bool? keep, string body)
        {
            var t = new FakeTransport { Status = 201, ResponseBody = MintedBody };
            await Make(t).MintWorldKeyAsync("w1", scope: scope, name: name, keep: keep);

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w1/keys", a.Url);
            Assert.AreEqual(body, a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(a));
        }

        // A caller that passes ct by position, as before the options, compiles and sends no body.
        [Test]
        public async Task MintWorldKey_CtByPosition_StillTheDefaults()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = MintedBody };
            await Make(t).MintWorldKeyAsync("w1", System.Threading.CancellationToken.None);
            Assert.IsNull(t.Calls.Single().Body);
        }

        // keel's rule for the scope (accounts/api.py _scope_param): read or write, r and w the same, in any case; anything
        // else is a 422. Kills: a scope keel refuses sent anyway, or the refusal not naming the parameter.
        [TestCase("admin")]
        [TestCase("reader")]
        [TestCase(" read")]
        [TestCase("rw")]
        public void MintWorldKey_AScopeKeelRefuses_IsRefused_NothingSent(string scope)
        {
            var t = new FakeTransport { Status = 201, ResponseBody = MintedBody };
            var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).MintWorldKeyAsync("w1", scope: scope));
            Assert.AreEqual("scope", e.ParamName);
            StringAssert.Contains("Nothing was sent", e.Message);
            CollectionAssert.IsEmpty(t.Calls);
        }

        // keel's rule for the name (accounts/api.py _key_name_param, Keel #990): trimmed as Python's str.strip() trims, then
        // at most 80 characters, counted as Python counts them (code points), a 422 past it. Kills: the length taken before
        // trimming, trimmed by .NET's whitespace (which keeps U+001C to U+001F, Python's separators), or counted in UTF-16
        // units (an emoji is two), each refusing a name keel takes; or a name past 80 sent.
        [Test]
        public async Task MintWorldKey_ANameOf80AfterKeelsTrim_IsSent_81IsRefused_NothingSent()
        {
            var eighty = new string('a', 80);
            var emoji = string.Concat(Enumerable.Repeat("\U0001F5FA", 80));
            var t = new FakeTransport { Status = 201, ResponseBody = MintedBody };
            var c = Make(t);
            var taken = new[]
            {
                eighty, " \t" + eighty + "\n ", "\u001c" + eighty + "\u001f", "\u3000" + eighty + "\u00a0", emoji,
            };
            foreach (var name in taken)
            {
                await c.MintWorldKeyAsync("w1", name: name);
                Assert.AreEqual(new JObject { ["name"] = name }.ToString(Newtonsoft.Json.Formatting.None), t.Last.Body,
                    "80 characters after keel's trim: sent, as given");
            }

            Assert.AreEqual(5, t.Calls.Count);
            foreach (var name in new[] { eighty + "b", " " + eighty + "b ", emoji + "\U0001F5FA" })
            {
                var e = Assert.ThrowsAsync<ArgumentException>(() => c.MintWorldKeyAsync("w1", name: name));
                Assert.AreEqual("name", e.ParamName);
                StringAssert.Contains("80", e.Message);
            }

            Assert.AreEqual(5, t.Calls.Count, "a name past 80 is never sent");
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

        // -- CreateWorldLinkAsync, ListWorldLinksAsync, RevokeWorldLinkAsync --------------------------------

        private const string CreatedLinkBody =
            @"{""id"":""l1"",""reader"":""player"",""role"":""guest"",""label"":"""",""inviter_name"":""Ada"","
            + @"""with_character"":true,""last4"":""wxyz"",""created_at"":""2026-10-31T01:00:00+00:00"","
            + @"""expires_at"":""2026-11-07T01:00:00+00:00"",""uses"":0,""status"":""pending"",""minted_by"":null,"
            + @"""preview"":false,""character"":null,""max_uses"":3,"
            + @"""characters"":[{""id"":""c1"",""name"":""Brine"",""taken"":false},{""id"":""c2"",""name"":null,""taken"":true}],"
            + @"""code"":""ow_j_secretcodewxyz"",""link"":""https://www.onlyworlds.com/join#ow_j_secretcodewxyz"","
            + @"""line"":""Ada invites you to Ostvik: https://www.onlyworlds.com/join#ow_j_secretcodewxyz""}";

        // The TypeScript SDK's createWorldLink() (4.24.0): POST /account/worlds/{id}/links with the reader alone when
        // nothing else is given, the Bearer, no PIN and no write header. Kills: the wrong verb or path, the world id
        // unescaped, a member left null sent (as null) instead of left out, a write header sent, or the answer's code,
        // link, line, table or picks misread.
        [Test]
        public async Task CreateWorldLink_PostsLinks_TheReaderAlone_TheBearer_AndAnswersTheCodeOnce()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = CreatedLinkBody };
            var made = await Make(t).CreateWorldLinkAsync("w 1", new OWWorldLinkCreate { Reader = "player" });

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%201/links", a.Url, "the path, the id escaped, no query");
            Assert.AreEqual(@"{""reader"":""player""}", a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(a));

            Assert.AreEqual("ow_j_secretcodewxyz", made.Code);
            StringAssert.EndsWith("#ow_j_secretcodewxyz", made.Link);
            StringAssert.StartsWith("Ada invites you", made.Line);
            Assert.AreEqual(("l1", "player", "guest", "pending", 0, "wxyz"), (made.Id, made.Reader, made.Role, made.Status, made.Uses, made.Last4));
            Assert.AreEqual((true, (bool?)false, (string)null), (made.WithCharacter, made.Preview, made.MintedBy));
            Assert.IsNull(made.Character);
            Assert.AreEqual(3, made.MaxUses);
            Assert.IsTrue(made.IsTable);
            Assert.AreEqual(2, made.Characters.Count);
            Assert.AreEqual(("c2", (string)null, true), (made.Characters[1].Id, made.Characters[1].Name, made.Characters[1].Taken));
        }

        // npm builds the body key by key, each only when given. Kills: a member sent under its C# name or camelCase, an
        // empty Characters list dropped (on a link for one keel answers it 422, so it is the caller's to send), or a false
        // or a 0 left out as if it were null.
        [Test]
        public async Task CreateWorldLink_EveryMemberGiven_IsSentInKeelsSpelling_FalseAndEmptyIncluded()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = CreatedLinkBody };
            await Make(t).CreateWorldLinkAsync("w1", new OWWorldLinkCreate
            {
                Reader = "player", Role = "guest", Days = 30, InviterName = "Ada", WithCharacter = false, Character = "new",
                Preview = false, MaxUses = 1, Characters = new List<string>(),
            });

            var expected = JObject.Parse(@"{""reader"":""player"",""role"":""guest"",""days"":30,""inviter_name"":""Ada"","
                + @"""with_character"":false,""character"":""new"",""preview"":false,""max_uses"":1,""characters"":[]}");
            Assert.IsTrue(JToken.DeepEquals(expected, JObject.Parse(t.Last.Body)), t.Last.Body);

            await Make(t).CreateWorldLinkAsync("w1", new OWWorldLinkCreate { Reader = "player", Characters = new List<string> { "c1", "c2" } });
            Assert.AreEqual(@"{""reader"":""player"",""characters"":[""c1"",""c2""]}", t.Last.Body, "the picks in the caller's order");
        }

        // Kills: a link with no reader (keel's 422) or no options at all reaching the transport.
        [Test]
        public void CreateWorldLink_NoLink_OrNoReader_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            Assert.ThrowsAsync<ArgumentNullException>(() => Make(t).CreateWorldLinkAsync("w1", null));
            var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).CreateWorldLinkAsync("w1", new OWWorldLinkCreate { Days = 7 }));
            StringAssert.Contains("Reader is required", e.Message);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.IsEmpty(t.Calls);
        }

        // The TypeScript SDK's listWorldLinks() (4.24.0): GET /account/worlds/{id}/links, the Bearer alone, the rows of
        // keel's { data } in its order, no codes. Kills: the wrong path, a query, the PIN sent, the rows reordered, a
        // player link's Character or an agent link's absent preview misread, or an answer without data thrown on.
        [Test]
        public async Task ListWorldLinks_SendsGetLinks_TheBearerAlone_AndAnswersTheRowsInOrder()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""data"":[{""id"":""l2"",""reader"":""player"",""role"":""guest"",""uses"":1,""status"":""used"","
                    + @"""preview"":true,""character"":{""id"":""c1"",""name"":""Brine""}},"
                    + @"{""id"":""l3"",""reader"":""agent"",""role"":""contributor"",""uses"":0,""status"":""revoked"",""minted_by"":""m4""}]}",
            };
            var links = await Make(t).ListWorldLinksAsync("w1");

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w1/links", a.Url, "the path, no query");
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));

            Assert.AreEqual(2, links.Count);
            Assert.AreEqual(("l2", "player", "used", 1, (bool?)true), (links[0].Id, links[0].Reader, links[0].Status, links[0].Uses, links[0].Preview));
            Assert.AreEqual(("c1", "Brine"), (links[0].Character.Id, links[0].Character.Name));
            Assert.IsFalse(links[0].IsTable);
            Assert.AreEqual(("l3", "agent", "revoked", "m4"), (links[1].Id, links[1].Reader, links[1].Status, links[1].MintedBy));
            Assert.IsNull(links[1].Preview, "an agent link has no preview");

            t.ResponseBody = "{}";
            CollectionAssert.IsEmpty(await Make(t).ListWorldLinksAsync("w1"), "no data: no rows");
        }

        // The TypeScript SDK's revokeWorldLink() (4.24.0): DELETE /account/worlds/{id}/links/{link_id}, the Bearer alone,
        // a 204. Kills: the wrong verb or path, either id unescaped, a body or a write header sent.
        [Test]
        public async Task RevokeWorldLink_SendsDeleteLinksLinkId_TheBearerAlone_AndTakesThe204()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).RevokeWorldLinkAsync("w/1", "l 2");

            var a = t.Calls.Single();
            Assert.AreEqual("DELETE", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%2F1/links/l%202", a.Url, "the path, both ids escaped, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
        }

        // -- ListAccountTokensAsync, RevokeAccountTokenAsync ------------------------------------------------

        // The TypeScript SDK's listAccountTokens() (4.122.0), its test vector's two rows: GET /account/tokens, the Bearer
        // alone, keel's { data } in its order. Kills: the wrong path, a query, the PIN or a write header sent, an mcp
        // row's client and return host misread, a null last use or expiry read as a value, or no data thrown on.
        [Test]
        public async Task ListAccountTokens_SendsGetTokens_TheBearerAlone_AndAnswersTheRowsInOrder()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""data"":[{""id"":""018f0000-0000-7000-8000-0000000000t1"",""name"":""atlas"",""last4"":""abcd"","
                    + @"""last_used_at"":""2026-10-31T06:00:00Z"",""revoked"":false,""expires_at"":null,""keys_minted"":2,""audience"":""""},"
                    + @"{""id"":""018f0000-0000-7000-8000-0000000000t2"",""name"":""claude"",""last4"":""wxyz"",""last_used_at"":null,"
                    + @"""revoked"":false,""expires_at"":""2026-10-31T07:00:00Z"",""keys_minted"":0,""audience"":""mcp"","
                    + @"""client"":""Claude"",""return_host"":""claude.ai""}]}",
            };
            var tokens = await Make(t).ListAccountTokensAsync();

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/account/tokens", a.Url, "the path, no query");
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
            Assert.AreEqual("Bearer ow_a_acct", a.Headers["Authorization"]);

            Assert.AreEqual(2, tokens.Count);
            var (h, m) = (tokens[0], tokens[1]);
            Assert.AreEqual(("018f0000-0000-7000-8000-0000000000t1", "atlas", "abcd", "2026-10-31T06:00:00Z", false, 2L, ""),
                (h.Id, h.Name, h.Last4, h.LastUsedAt, h.Revoked, h.KeysMinted, h.Audience));
            Assert.IsNull(h.ExpiresAt, "a token that never expires");
            Assert.IsNull(h.Client, "only an mcp token names its client");
            Assert.AreEqual(("claude", "mcp", "Claude", "claude.ai", "2026-10-31T07:00:00Z"),
                (m.Name, m.Audience, m.Client, m.ReturnHost, m.ExpiresAt));
            Assert.IsNull(m.LastUsedAt, "never used");

            t.ResponseBody = "{}";
            CollectionAssert.IsEmpty(await Make(t).ListAccountTokensAsync(), "no data: no rows");
        }

        // The TypeScript SDK's revokeAccountToken() (4.122.0): DELETE /account/tokens/{token_id}, the id escaped, no body,
        // the Bearer alone even on an agent client, a 204. Kills: the wrong verb or path, the id unescaped, a body or a
        // write header sent.
        [Test]
        public async Task RevokeAccountToken_SendsDeleteTokensId_Escaped_TheBearerAlone_AndTakesThe204()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).RevokeAccountTokenAsync("a b/c");

            var a = t.Calls.Single();
            Assert.AreEqual("DELETE", a.Method);
            Assert.AreEqual(Base + "/account/tokens/a%20b%2Fc", a.Url, "the path, the id escaped, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
        }

        // An empty or null id is refused before sending, as npm's TypeError; an id that is not the account's is keel's 404,
        // read as IsNotFound. Kills: a DELETE /account/tokens/ sent, or the 404 read as anything else.
        [Test]
        public async Task RevokeAccountToken_NoId_IsRefused_NothingSent_AndAnotherAccountsIdIsTheNotFound()
        {
            foreach (var bad in new[] { "", null })
            {
                var t = new FakeTransport { Status = 204, ResponseBody = "" };
                var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).RevokeAccountTokenAsync(bad), $"id {bad ?? "null"}");
                StringAssert.Contains("Nothing was sent.", e.Message);
                Assert.AreEqual("tokenId", e.ParamName);
                Assert.IsEmpty(t.Calls);
            }

            var nf = new FakeTransport
            {
                Status = 404,
                ResponseBody = @"{""error"":{""type"":""not_found"",""code"":""not_found"",""message"":""token 'x' not found."",""param"":null}}",
            };
            var err = Assert.ThrowsAsync<OWApiError>(() => Make(nf).RevokeAccountTokenAsync("x"));
            Assert.IsTrue(err.IsNotFound);
            await Task.CompletedTask;
        }

        // -- CreateSeatAsync, ListSeatsAsync, RekeySeatAsync, RemoveSeatAsync -------------------------------

        private const string SeatBody =
            @"{""world"":{""id"":""w1"",""name"":""Ostvik""},""seat"":{""id"":""s1"",""name"":""Scout"",""role"":""contributor"",""kind"":""agent""},"
            + @"""character"":""c9"",""key"":""ow_w_seatkeyabcd"",""pin"":""ow_s_seatsecret"","
            + @"""env"":""OW_API_KEY=ow_w_seatkeyabcd\nOW_API_PIN=ow_s_seatsecret\nOW_WORLD=w1\n""}";

        // The TypeScript SDK's createSeat() (4.28.0): POST /account/worlds/{id}/seats with { name } alone when nothing else
        // is given, then role and character as given, the Bearer, no PIN, no write header and no Idempotency-Key. Kills:
        // the wrong verb or path, the world id unescaped, a null role or character sent, either under another key, a
        // header the document does not list, or the key, secret or .env lines misread.
        [Test]
        public async Task CreateSeat_PostsSeats_TheNameAlone_ThenRoleAndCharacter_TheBearer_AndAnswersTheKeyAndSecretOnce()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = SeatBody };
            var c = Make(t);
            var made = await c.CreateSeatAsync("w/1", "Scout");
            await c.CreateSeatAsync("w1", "Scout", role: "guest", character: "mine");

            var a = t.Calls[0];
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%2F1/seats", a.Url, "the path, the id escaped, no query");
            Assert.AreEqual(@"{""name"":""Scout""}", a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization", "Content-Type" }, HeaderNames(a));
            Assert.AreEqual(@"{""name"":""Scout"",""role"":""guest"",""character"":""mine""}", t.Calls[1].Body);

            Assert.AreEqual(("w1", "Ostvik"), (made.World.Id, made.World.Name));
            Assert.AreEqual(("s1", "Scout", "contributor", "agent"), (made.Seat.Id, made.Seat.Name, made.Seat.Role, made.Seat.Kind));
            Assert.AreEqual(("c9", "ow_w_seatkeyabcd", "ow_s_seatsecret"), (made.Character, made.Key, made.Pin));
            StringAssert.StartsWith("OW_API_KEY=ow_w_seatkeyabcd\n", made.Env);
        }

        // Kills: a seat with no name (keel's 422) reaching the transport.
        [Test]
        public void CreateSeat_NoName_IsRefused_NothingSent()
        {
            var t = new FakeTransport();
            var e = Assert.ThrowsAsync<ArgumentException>(() => Make(t).CreateSeatAsync("w1", null));
            StringAssert.Contains("name is required", e.Message);
            StringAssert.Contains("Nothing was sent", e.Message);
            Assert.IsEmpty(t.Calls);
        }

        // The TypeScript SDK's listSeats() (4.28.0): GET /account/worlds/{id}/seats, the Bearer alone, the rows of keel's
        // { data } in its order. Kills: the wrong path, a query or the PIN sent, the rows reordered, a null sponsor or
        // Character misread, or an answer without data thrown on.
        [Test]
        public async Task ListSeats_SendsGetSeats_TheBearerAlone_AndAnswersTheRowsInOrder()
        {
            var t = new FakeTransport
            {
                ResponseBody = @"{""data"":[{""id"":""s1"",""name"":""Scout"",""role"":""contributor"",""kind"":""agent"",""character"":""c9"","
                    + @"""sponsor"":""ada"",""joined_at"":""2026-10-30T09:00:00+00:00"",""last_active"":null},"
                    + @"{""id"":""s2"",""name"":""Tally"",""role"":""guest"",""kind"":""agent"",""character"":null,""sponsor"":null,"
                    + @"""joined_at"":null,""last_active"":""2026-10-31T00:30:00+00:00""}]}",
            };
            var seats = await Make(t).ListSeatsAsync("w1");

            var a = t.Calls.Single();
            Assert.AreEqual("GET", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w1/seats", a.Url, "the path, no query");
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));

            Assert.AreEqual(2, seats.Count);
            Assert.AreEqual(("s1", "Scout", "contributor", "agent", "c9", "ada"),
                (seats[0].Id, seats[0].Name, seats[0].Role, seats[0].Kind, seats[0].Character, seats[0].Sponsor));
            Assert.AreEqual(("2026-10-30T09:00:00+00:00", (string)null), (seats[0].JoinedAt, seats[0].LastActive));
            Assert.AreEqual(("s2", (string)null, (string)null, "2026-10-31T00:30:00+00:00"),
                (seats[1].Id, seats[1].Character, seats[1].Sponsor, seats[1].LastActive));

            t.ResponseBody = "{}";
            CollectionAssert.IsEmpty(await Make(t).ListSeatsAsync("w1"), "no data: no rows");
        }

        // The TypeScript SDK's rekeySeat() (4.29.0): POST /account/worlds/{id}/seats/{seat_id}/key with no body, the
        // Bearer alone, answered as a created seat. Kills: the wrong path (the seat itself, which is the removal's), either
        // id unescaped, a body or a Content-Type sent (keel's document declares none), or the new key misread.
        [Test]
        public async Task RekeySeat_PostsSeatKey_NoBody_TheBearerAlone_AndAnswersTheNewKeyOnce()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = SeatBody };
            var made = await Make(t).RekeySeatAsync("w 1", "s/1");

            var a = t.Calls.Single();
            Assert.AreEqual("POST", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w%201/seats/s%2F1/key", a.Url, "the path, both ids escaped, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
            Assert.AreEqual(("ow_w_seatkeyabcd", "ow_s_seatsecret"), (made.Key, made.Pin));
        }

        // The TypeScript SDK's removeSeat() (4.28.0): DELETE /account/worlds/{id}/seats/{seat_id}, the Bearer alone, a
        // 204. Kills: the wrong verb or path, either id unescaped, a body or a write header sent.
        [Test]
        public async Task RemoveSeat_SendsDeleteSeatsSeatId_TheBearerAlone_AndTakesThe204()
        {
            var t = new FakeTransport { Status = 204, ResponseBody = "" };
            await Make(t).RemoveSeatAsync("w1", "s 1");

            var a = t.Calls.Single();
            Assert.AreEqual("DELETE", a.Method);
            Assert.AreEqual(Base + "/account/worlds/w1/seats/s%201", a.Url, "the path, the ids escaped, no query");
            Assert.IsNull(a.Body);
            CollectionAssert.AreEqual(new[] { "Authorization" }, HeaderNames(a));
        }

        // A key or secret in an answer reaches the caller's model and nothing else: no exception the client builds from a
        // success carries the body. Kills: a malformed 201 thrown with its body in the message (the key with it).
        [Test]
        public void ASecretInAMalformedAnswer_NeverReachesTheExceptionMessage()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = @"{""key"":""ow_w_leakme"",""pin"":""ow_s_leakme"",""seat"":[" };
            var e = Assert.CatchAsync<Exception>(() => Make(t).CreateSeatAsync("w1", "Scout"));
            StringAssert.DoesNotContain("leakme", e.ToString());
        }
    }
}
