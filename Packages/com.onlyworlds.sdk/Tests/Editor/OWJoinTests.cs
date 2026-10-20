using System.Threading.Tasks;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Joining by link (keel #212, #353): the code's shape in a pasted link, the two keyless POSTs,
    /// and both of /join's answers. The bodies below are keel's schemas field for field
    /// (<c>accounts/join_api.py</c>: PLAYER_JOINED_SCHEMA, JOIN_SCHEMA, PREVIEW_SCHEMA).
    /// </summary>
    public class OWJoinTests
    {
        private const string Base = "https://example.test/api/v2";
        private const string Code = "ow_j_EoEUrAfQ9xumy-lbBJqXix3q1xl49-P4";

        private const string PlayerJoined = @"{""kind"":""player"",""key"":""ow_r_guest"",
            ""world"":{""id"":""0196f0a2-0000-7000-8000-000000000001"",""name"":""Moppetopia"",""description"":""""},
            ""character"":{""id"":""0196f0a2-0000-7000-8000-0000000000c1"",""name"":""Pip""},""role"":""guest""}";

        private const string AgentJoined = @"{""kind"":""agent"",
            ""world"":{""id"":""0196f0a2-0000-7000-8000-000000000001"",""name"":""Moppetopia"",""description"":""""},
            ""seat"":{""id"":""s1"",""role"":""contributor"",""agent_name"":""Scribe""},
            ""with_character"":false,""character"":null,""key"":""ow_w_seat"",""pin"":""ow_s_secret"",
            ""base_url"":""https://example.test/api/v2"",""env"":""OW_KEY=ow_w_seat"",""next"":[],""members"":[]}";

        private const string Preview = @"{""reader"":""player"",""world"":{""name"":""Moppetopia"",""description"":""A demo""},
            ""invited_by"":""Sam"",""owner_character"":""The Mapmaker"",""role"":""guest"",""with_character"":true,
            ""expires_at"":""2026-10-26T14:00:00+00:00""}";

        // A party link's preview: keel's table_preview keys after the seven (accounts/agents.py, PR 110).
        private const string TablePreview = @"{""reader"":""player"",""world"":{""name"":""Moppetopia"",""description"":""A demo""},
            ""invited_by"":""Sam"",""owner_character"":null,""role"":""guest"",""with_character"":true,
            ""expires_at"":""2026-10-26T14:00:00+00:00"",
            ""max_uses"":4,""uses_left"":3,""name_required"":false,
            ""characters"":[{""id"":""c-ana"",""name"":""Ana"",""image_url"":null,""taken"":false},
                            {""id"":""c-bo"",""name"":""Bo"",""image_url"":""https://media.onlyworlds.com/bo.webp"",""taken"":true}]}";

        private static FakeTransport Refusing(string code, string param) => new FakeTransport
        {
            Status = 422,
            ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""" + code + @""",""param"":"
                + (param == null ? "null" : @"""" + param + @"""") + @",""message"":""refused""}}",
        };

        // -- ParseLink --------------------------------------------------------

        [TestCase("https://www.onlyworlds.com/join#" + Code, Code)]
        [TestCase("  http://localhost:8000/join#" + Code + "\n", Code)]
        [TestCase("https://www.onlyworlds.com/join/0196f0a2-0000-7000-8000-000000000001#" + Code, Code)]
        [TestCase(Code, Code)]
        [TestCase("#" + Code, Code)]
        public void ParseLink_FindsTheCode(string text, string expected)
        {
            Assert.AreEqual(expected, OWJoin.ParseLink(text));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("https://www.onlyworlds.com/join")]
        [TestCase("https://www.onlyworlds.com/join#")]
        [TestCase("ow_j_")]
        [TestCase("ow_r_readkey")]
        [TestCase("ow_j_two words")]
        public void ParseLink_RefusesTextWithNoCode(string text)
        {
            Assert.IsNull(OWJoin.ParseLink(text));
        }

        // -- The requests -----------------------------------------------------

        [Test]
        public async Task Redeem_PostsTheCodeAndNameWithNoCredential()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = PlayerJoined };
            await OWJoin.RedeemAsync(Code, t, name: "Ana", baseUrl: Base);

            Assert.AreEqual("POST", t.Last.Method);
            Assert.AreEqual(Base + "/join", t.Last.Url);
            Assert.AreEqual(Code, (string)t.LastBody["code"]);
            Assert.AreEqual("Ana", (string)t.LastBody["name"]);
            Assert.IsFalse(t.LastBody.ContainsKey("agent_name"));
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Key"));
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Pin"));
            Assert.IsFalse(t.Last.Headers.ContainsKey("Authorization"));
        }

        [Test]
        public async Task Redeem_LeavesOutWhatTheCallerLeftOut()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = PlayerJoined };
            await OWJoin.RedeemAsync(Code, t, baseUrl: Base);

            // A blank name means the Character's own name; an absent one must not arrive as null.
            Assert.AreEqual(1, t.LastBody.Count);
        }

        [Test]
        public async Task Redeem_SendsAnAgentsName()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = AgentJoined };
            await OWJoin.RedeemAsync(Code, t, agentName: "Scribe", baseUrl: Base);

            Assert.AreEqual("Scribe", (string)t.LastBody["agent_name"]);
            Assert.IsFalse(t.LastBody.ContainsKey("name"));
        }

        [Test]
        public async Task Preview_PostsTheCodeWithNoCredential()
        {
            var t = new FakeTransport { ResponseBody = Preview };
            await OWJoin.PreviewAsync(Code, t, Base);

            Assert.AreEqual(Base + "/join/preview", t.Last.Url);
            Assert.AreEqual(1, t.LastBody.Count);
            Assert.IsFalse(t.Last.Headers.ContainsKey("API-Key"));
            Assert.IsFalse(t.Last.Headers.ContainsKey("Authorization"));
        }

        [Test]
        public async Task TheDefaultHostIsProduction()
        {
            var t = new FakeTransport { ResponseBody = Preview };
            await OWJoin.PreviewAsync(Code, t);

            Assert.AreEqual("https://www.onlyworlds.com/api/v2/join/preview", t.Last.Url);
        }

        // -- The answers ------------------------------------------------------

        [Test]
        public async Task APlayersAnswer_IsAReadKeyAndItsCharacter()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = PlayerJoined };
            var joined = await OWJoin.RedeemAsync(Code, t, baseUrl: Base);

            Assert.IsTrue(joined.IsPlayer);
            Assert.IsFalse(joined.IsAgent);
            Assert.AreEqual("ow_r_guest", joined.Key);
            Assert.AreEqual(OWKeyKind.Read, OWKey.DetectKind(joined.Key));
            Assert.IsNull(joined.Pin);
            Assert.AreEqual("0196f0a2-0000-7000-8000-000000000001", joined.World.Id);
            Assert.AreEqual("Pip", joined.Character.Name);
            Assert.AreEqual("guest", joined.RoleInWorld);
            Assert.IsNull(joined.Seat);
        }

        [Test]
        public async Task AnAgentsAnswer_IsASeatKeyAndSecret()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = AgentJoined };
            var joined = await OWJoin.RedeemAsync(Code, t, agentName: "Scribe", baseUrl: Base);

            Assert.IsTrue(joined.IsAgent);
            Assert.AreEqual("ow_w_seat", joined.Key);
            Assert.AreEqual("ow_s_secret", joined.Pin);
            Assert.AreEqual("contributor", joined.RoleInWorld);
            Assert.AreEqual("Scribe", joined.Seat.AgentName);
            Assert.IsNull(joined.Character);
            Assert.AreEqual("OW_KEY=ow_w_seat", joined.Env);
        }

        [Test]
        public async Task APreview_ReadsEveryField()
        {
            var t = new FakeTransport { ResponseBody = Preview };
            var p = await OWJoin.PreviewAsync(Code, t, Base);

            Assert.AreEqual("player", p.Reader);
            Assert.AreEqual("Moppetopia", p.World.Name);
            Assert.AreEqual("A demo", p.World.Description);
            Assert.IsNull(p.World.Id);
            Assert.AreEqual("Sam", p.InvitedBy);
            Assert.AreEqual("The Mapmaker", p.OwnerCharacter);
            Assert.AreEqual("guest", p.Role);
            Assert.IsTrue(p.WithCharacter);
            Assert.AreEqual("2026-10-26T14:00:00+00:00", p.ExpiresAt);

            // A link for one carries none of a table's keys.
            Assert.IsFalse(p.IsTable);
            Assert.IsNull(p.MaxUses);
            Assert.IsNull(p.UsesLeft);
            Assert.IsNull(p.NameRequired);
            Assert.IsNull(p.Characters);
        }

        [Test]
        public async Task ATablePreview_ReadsItsPlacesAndItsCharactersInOrder()
        {
            var t = new FakeTransport { ResponseBody = TablePreview };
            var p = await OWJoin.PreviewAsync(Code, t, Base);

            Assert.IsTrue(p.IsTable);
            Assert.AreEqual(4, p.MaxUses);
            Assert.AreEqual(3, p.UsesLeft);
            Assert.AreEqual(false, p.NameRequired);
            Assert.IsNull(p.OwnerCharacter);
            Assert.AreEqual(new[] { "c-ana", "c-bo" }, p.Characters.ConvertAll(c => c.Id));
            Assert.AreEqual("Bo", p.Characters[1].Name);
            Assert.IsNull(p.Characters[0].ImageUrl);
            Assert.AreEqual("https://media.onlyworlds.com/bo.webp", p.Characters[1].ImageUrl);
            Assert.AreEqual((false, true), (p.Characters[0].Taken, p.Characters[1].Taken));
        }

        [Test]
        public async Task Redeem_SendsThePick()
        {
            var t = new FakeTransport { Status = 201, ResponseBody = PlayerJoined };
            await OWJoin.RedeemAsync(Code, t, baseUrl: Base, character: "c-ana");

            Assert.AreEqual("c-ana", (string)t.LastBody["character"]);
            Assert.AreEqual(2, t.LastBody.Count, "the code and the pick, nothing else");
        }

        // -- Refusals ---------------------------------------------------------

        [Test]
        public void ABadCode_IsKeelsOneNotFound()
        {
            var t = new FakeTransport
            {
                Status = 404,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""not_found"",
                    ""message"":""This join code is not valid. It may have been used, revoked, or expired.""}}",
            };

            var ex = Assert.ThrowsAsync<OWApiError>(async () => await OWJoin.RedeemAsync(Code, t, baseUrl: Base));
            Assert.IsTrue(ex.IsNotFound);
        }

        [Test]
        public void AMissingAgentName_NamesItsParam()
        {
            var t = new FakeTransport
            {
                Status = 422,
                ResponseBody = @"{""error"":{""type"":""invalid_request"",""code"":""invalid_request"",
                    ""param"":""agent_name"",""message"":""agent_name is required.""}}",
            };

            var ex = Assert.ThrowsAsync<OWApiError>(async () => await OWJoin.RedeemAsync(Code, t, baseUrl: Base));
            Assert.AreEqual("agent_name", ex.Param);
        }

        [Test]
        public void ATakenPick_IsCharacterTakenAndNothingElse()
        {
            var ex = Assert.ThrowsAsync<OWApiError>(async () =>
                await OWJoin.RedeemAsync(Code, Refusing("character_taken", "character"), baseUrl: Base, character: "c-bo"));
            Assert.AreEqual((true, false, false), (ex.IsCharacterTaken, ex.IsCharacterInvalid, ex.IsWorldFull));
        }

        [Test]
        public void APickOffTheList_IsCharacterInvalid_ButAMissingName_IsNot()
        {
            var off = Assert.ThrowsAsync<OWApiError>(async () =>
                await OWJoin.RedeemAsync(Code, Refusing("invalid_request", "character"), baseUrl: Base, character: "c-zed"));
            Assert.AreEqual((false, true, false), (off.IsCharacterTaken, off.IsCharacterInvalid, off.IsWorldFull));

            var noName = Assert.ThrowsAsync<OWApiError>(async () =>
                await OWJoin.RedeemAsync(Code, Refusing("invalid_request", "name"), baseUrl: Base));
            Assert.IsFalse(noName.IsCharacterInvalid);
            Assert.AreEqual("name", noName.Param);
        }

        [Test]
        public void AFullWorld_IsWorldFull_OnlyAsA422()
        {
            var full = Assert.ThrowsAsync<OWApiError>(async () =>
                await OWJoin.RedeemAsync(Code, Refusing("world_full", null), baseUrl: Base));
            Assert.AreEqual((false, false, true), (full.IsCharacterTaken, full.IsCharacterInvalid, full.IsWorldFull));

            var t = Refusing("world_full", null);
            t.Status = 409;
            var other = Assert.ThrowsAsync<OWApiError>(async () => await OWJoin.RedeemAsync(Code, t, baseUrl: Base));
            Assert.IsFalse(other.IsWorldFull, "the code on another status is not it");
        }

        [Test]
        public void NoCodeOrNoTransport_IsTheCallersError()
        {
            var t = new FakeTransport();

            Assert.ThrowsAsync<System.ArgumentException>(async () => await OWJoin.RedeemAsync("", t));
            Assert.ThrowsAsync<System.ArgumentNullException>(async () => await OWJoin.PreviewAsync(Code, null));
            Assert.IsEmpty(t.Calls);
        }
    }
}
