using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// <see cref="OWClientConfig.World"/>: an account token, or no key, reads the world it names. keel serves element list
    /// and get and the world GET to <c>Authorization: Bearer ow_a_...</c> with <c>world=&lt;id&gt;</c> and no API-Key, read
    /// only, as the account's own access there (ruled 2026-10-26); the same <c>world=&lt;id&gt;</c> with no credential is
    /// the keyless public reader. Every other world route answers an account token 401, so the client refuses those
    /// before sending, by one rule in its request path. Mirrors the TypeScript SDK's <c>world-reads</c> tests (4.88.0).
    /// </summary>
    public class OWWorldReadTests
    {
        private const string Api = "https://example.test/api/v2";
        private const string W = "0193f1c2-7a00-7000-8000-00000000beef";
        private const string El = "0193f1c2-7a00-7000-8000-000000000001";
        private const string Pid = "0193f1c2-7a00-7000-8000-0000000000aa";

        // One body every call below can read.
        private const string Body =
            @"{""id"":""" + El + @""",""name"":""Harbour"",""data"":[],""has_more"":false,""next_cursor"":null,"
            + @"""cursor"":""0"",""changes"":[],""head"":0,""proposal"":{""id"":""" + Pid + @"""},"
            + @"""element"":{""id"":""" + El + @"""},""deleted"":""" + El + @""",""key"":""ow_w_x"",""items"":[]}";

        private static FakeTransport Answering() => new FakeTransport { ResponseBody = Body };

        private static OWClient Account(FakeTransport t, string world = W)
            => new OWClient(new OWClientConfig { ApiKey = "ow_a_acct", ApiPin = "1234", World = world, Transport = t, BaseUrl = Api });

        private static OWClient Keyless(FakeTransport t, string world = W)
            => new OWClient(new OWClientConfig { ApiPin = "1234", World = world, Transport = t, BaseUrl = Api });

        private static readonly (string Name, string Path, Func<OWClient, Task> Run)[] Reads =
        {
            ("ListAsync", "/location/", c => c.ListAsync<JObject>("location")),
            ("ListAllAsync", "/location/", c => c.ListAllAsync<JObject>("location")),
            ("GetAsync", $"/location/{El}/", c => c.GetAsync<JObject>("location", El)),
            ("GetWorldAsync", "/world/", c => c.GetWorldAsync()),
        };

        /// <summary>A query string's values for one name, unescaped, in order.</summary>
        private static List<string> Values(OWRequest r, string name)
        {
            var query = new Uri(r.Url).Query.TrimStart('?');
            return query.Length == 0
                ? new List<string>()
                : query.Split('&').Select(p => p.Split(new[] { '=' }, 2))
                    .Where(p => Uri.UnescapeDataString(p[0]) == name)
                    .Select(p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "").ToList();
        }

        private static string One(OWRequest r, string name)
        {
            var v = Values(r, name);
            return v.Count == 0 ? null : v.Single();
        }

        // -- The four reads ------------------------------------------------------

        [Test]
        public async Task AccountWithWorld_TheFourReads_SendWorldOnce_TheBearer_NoApiKeyNoPin()
        {
            foreach (var read in Reads)
            {
                var t = Answering();
                await read.Run(Account(t));

                Assert.AreEqual(1, t.Calls.Count, $"{read.Name}: one request");
                var sent = t.Last;
                Assert.AreEqual("GET", sent.Method, read.Name);
                Assert.AreEqual(Api + read.Path, sent.Url.Split('?')[0], read.Name);
                CollectionAssert.AreEqual(new[] { W }, Values(sent, "world"), $"{read.Name}: world=<id> once");
                CollectionAssert.AreEquivalent(new[] { "Authorization" }, sent.Headers.Keys, $"{read.Name}: the Bearer alone");
                Assert.AreEqual("Bearer ow_a_acct", sent.Headers["Authorization"], read.Name);
            }
        }

        [Test]
        public async Task NoKeyWithWorld_TheFourReads_SendWorldOnce_AndNoHeaderAtAll()
        {
            foreach (var read in Reads)
            {
                var t = Answering();
                await read.Run(Keyless(t));

                Assert.AreEqual(1, t.Calls.Count, $"{read.Name}: one request");
                var sent = t.Last;
                Assert.AreEqual("GET", sent.Method, read.Name);
                Assert.AreEqual(Api + read.Path, sent.Url.Split('?')[0], read.Name);
                CollectionAssert.AreEqual(new[] { W }, Values(sent, "world"), $"{read.Name}: world=<id> once");
                CollectionAssert.IsEmpty(sent.Headers.Keys, $"{read.Name}: no credential, no header");
            }
        }

        [Test]
        public async Task AWorldKey_TheFourReads_SendItsApiKey_AndNoWorld_AsBefore()
        {
            foreach (var read in Reads)
            {
                var t = Answering();
                await read.Run(new OWClient(new OWClientConfig { ApiKey = "ow_r_test", Transport = t, BaseUrl = Api }));

                Assert.IsEmpty(Values(t.Last, "world"), read.Name);
                Assert.AreEqual("ow_r_test", t.Last.Headers["API-Key"], read.Name);
                Assert.IsFalse(t.Last.Headers.ContainsKey("Authorization"), read.Name);
            }
        }

        [Test]
        public void AnAccountTokenWithoutWorld_TheFourReads_AreRefused_NamingWorld_NothingSent()
        {
            foreach (var read in Reads)
            {
                var t = Answering();
                var e = Assert.ThrowsAsync<InvalidOperationException>(() => read.Run(Account(t, world: null)), read.Name);
                StringAssert.Contains("only with World set", e.Message, read.Name);
                StringAssert.Contains("ListAsync, ListAllAsync, GetAsync and GetWorldAsync", e.Message, read.Name);
                StringAssert.Contains("Nothing was sent", e.Message, read.Name);
                Assert.IsEmpty(t.Calls, $"{read.Name}: nothing sent");
            }
        }

        // -- The query -----------------------------------------------------------

        [Test]
        public async Task World_GoesBesideTheListsOwnQuery_AndTheGets()
        {
            foreach (var make in new Func<FakeTransport, OWClient>[] { t => Account(t), t => Keyless(t) })
            {
                var t = Answering();
                var c = make(t);
                await c.ListAsync<JObject>("character", new OWListParams
                {
                    Limit = 7, Cursor = "abc=", Fields = new[] { "name", "species" }, Name = "Ha rb&our",
                    NameContains = "harb", Supertype = "hero", Subtype = "old",
                });
                await c.GetAsync<JObject>("character", El);

                var list = t.Calls[0];
                Assert.AreEqual(W, One(list, "world"));
                Assert.AreEqual("7", One(list, "limit"));
                Assert.AreEqual("abc=", One(list, "cursor"));
                Assert.AreEqual("name,species", One(list, "fields"));
                Assert.AreEqual("Ha rb&our", One(list, "name"));
                Assert.AreEqual("harb", One(list, "name__icontains"));
                Assert.AreEqual("hero", One(list, "supertype"));
                Assert.AreEqual("old", One(list, "subtype"));

                Assert.AreEqual($"{Api}/character/{El}/?world={W}", t.Calls[1].Url);
            }
        }

        [Test]
        public async Task World_IsEscaped_AndTheDefaultLimitStillGoes()
        {
            const string slug = "my world/äb&c";
            var t = Answering();
            var c = Keyless(t, slug);
            await c.GetWorldAsync();
            await c.ListAsync<JObject>("location");

            Assert.AreEqual($"{Api}/world/?world=my%20world%2F%C3%A4b%26c", t.Calls[0].Url);
            Assert.AreEqual(slug, One(t.Calls[0], "world"));
            Assert.AreEqual(slug, One(t.Calls[1], "world"));
            Assert.AreEqual("100", One(t.Calls[1], "limit"));
        }

        [Test]
        public async Task ListAll_KeepsWorldOnEveryPageOfTheWalk()
        {
            foreach (var make in new Func<FakeTransport, OWClient>[] { t => Account(t), t => Keyless(t) })
            {
                var t = new FakeTransport();
                t.Bodies.Enqueue(@"{""data"":[{""id"":""a""}],""has_more"":true,""next_cursor"":""c2""}");
                t.Bodies.Enqueue(@"{""data"":[{""id"":""b""}],""has_more"":true,""next_cursor"":""c3""}");
                t.Bodies.Enqueue(@"{""data"":[{""id"":""c""}],""has_more"":false,""next_cursor"":null}");

                var all = await make(t).ListAllAsync<JObject>("character", new OWListParams { Limit = 1, Supertype = "hero" });

                CollectionAssert.AreEqual(new[] { "a", "b", "c" }, all.Select(e => (string)e["id"]).ToArray());
                Assert.AreEqual(3, t.Calls.Count);
                CollectionAssert.AreEqual(new[] { W, W, W }, t.Calls.Select(r => One(r, "world")).ToArray());
                CollectionAssert.AreEqual(new[] { null, "c2", "c3" }, t.Calls.Select(r => One(r, "cursor")).ToArray());
                CollectionAssert.AreEqual(new[] { "hero", "hero", "hero" }, t.Calls.Select(r => One(r, "supertype")).ToArray());
                CollectionAssert.AreEqual(new[] { "1", "1", "1" }, t.Calls.Select(r => One(r, "limit")).ToArray());
            }
        }

        // -- Construction ---------------------------------------------------------

        [Test]
        public void Construction_WorldWithAWorldKey_IsRefused()
        {
            foreach (var key in new[] { "ow_w_test", "ow_r_test", "0123456789", "some-other-key" })
            {
                var e = Assert.Throws<ArgumentException>(
                    () => new OWClient(new OWClientConfig { ApiKey = key, World = W, Transport = new FakeTransport() }), key);
                StringAssert.Contains("A world key reads its own world; World is for an account token (ow_a_) or no key", e.Message, key);
                StringAssert.Contains("ListAsync, ListAllAsync, GetAsync and GetWorldAsync", e.Message, key);
            }
        }

        [Test]
        public void Construction_WorldMustBeAnId_NotEmptyOrWhiteSpace()
        {
            foreach (var world in new[] { "", "   ", "\t\n" })
            {
                foreach (var key in new[] { "ow_a_acct", null, "" })
                {
                    var e = Assert.Throws<ArgumentException>(
                        () => new OWClient(new OWClientConfig { ApiKey = key, World = world, Transport = new FakeTransport() }),
                        $"\"{world}\" with {key ?? "null"}");
                    StringAssert.Contains("World is a world's id", e.Message);
                }
            }
        }

        [Test]
        public void Construction_NoKeyNoWorld_IsStillRefused_AndTheOnesThatStand()
        {
            foreach (var key in new[] { null, "" })
            {
                var e = Assert.Throws<ArgumentException>(
                    () => new OWClient(new OWClientConfig { ApiKey = key, Transport = new FakeTransport() }));
                StringAssert.Contains("An API key is required", e.Message);
            }

            Assert.DoesNotThrow(() => new OWClient(new OWClientConfig { ApiKey = "ow_a_acct", World = W, Transport = new FakeTransport() }));
            Assert.DoesNotThrow(() => new OWClient(new OWClientConfig { World = W, Transport = new FakeTransport() }));
            Assert.DoesNotThrow(() => new OWClient(new OWClientConfig { ApiKey = "ow_w_test", Transport = new FakeTransport() }));
            Assert.DoesNotThrow(() => new OWClient(new OWClientConfig { ApiKey = "ow_a_acct", Transport = new FakeTransport() }));

            var keyless = new OWClient(new OWClientConfig { World = W, Transport = new FakeTransport() });
            Assert.AreEqual(W, keyless.World);
            Assert.AreEqual(OWKeyKind.Unknown, keyless.KeyKind);
        }

        // -- Every public method, by what it does ------------------------------------
        //
        // `Read` sends world= (the Bearer on an account client, nothing on a keyless one), `Named` sends world= and the
        // Bearer on an account client (SetMyLensesAsync, the one write that names its world so, and GetMembersAsync, the
        // roster) and is refused on a keyless one, `Path` sends the Bearer to a world route whose path names the world,
        // without world= (WorldLensesAsync), `Account` sends the Bearer to an /account route, `Open` sends no credential
        // and no world= on any client (HealthAsync), `Refused` is an InvalidOperationException before the transport is
        // called. A method the table does not hold fails the sweep, so a new call is classified when it is added.

        private enum Does { Read, Named, Path, Account, Open, Refused }

        private static readonly Dictionary<string, (Does Does, Func<OWClient, Task> Run)> Methods =
            new Dictionary<string, (Does, Func<OWClient, Task>)>
            {
                ["HealthAsync"] = (Does.Open, c => c.HealthAsync()),
                ["GetWorldAsync"] = (Does.Read, c => c.GetWorldAsync()),
                ["PatchWorldAsync"] = (Does.Refused, c => c.PatchWorldAsync(new JObject { ["name"] = "W" })),
                ["GetMeAsync"] = (Does.Refused, c => c.GetMeAsync()),
                ["SetMyLensesAsync"] = (Does.Named, c => c.SetMyLensesAsync(new string[0])),
                ["SetMyCharacterAsync"] = (Does.Refused, c => c.SetMyCharacterAsync(El)),
                ["GetMembersAsync"] = (Does.Named, c => c.GetMembersAsync()),
                ["WorldLensesAsync"] = (Does.Path, c => c.WorldLensesAsync(W)),
                ["ListAsync"] = (Does.Read, c => c.ListAsync<JObject>("location")),
                ["ListAllAsync"] = (Does.Read, c => c.ListAllAsync<JObject>("location")),
                ["GetAsync"] = (Does.Read, c => c.GetAsync<JObject>("location", El)),
                ["CreateAsync"] = (Does.Refused, c => c.CreateAsync<JObject>("location", new JObject { ["name"] = "H" })),
                ["UpsertAsync"] = (Does.Refused, c => c.UpsertAsync<JObject>("location", El, new JObject { ["name"] = "H" })),
                ["AdoptAsync"] = (Does.Refused, c => c.AdoptAsync<JObject>("location", El, "owner")),
                ["HideAsync"] = (Does.Refused, c => c.HideAsync("location", El)),
                ["UnhideAsync"] = (Does.Refused, c => c.UnhideAsync("location", El)),
                ["ListHiddenAsync"] = (Does.Refused, c => c.ListHiddenAsync()),
                ["DraughtAsync"] = (Does.Refused, c => c.DraughtAsync(El)),
                ["CreateMediaTicketAsync"] = (Does.Refused, c => c.CreateMediaTicketAsync()),
                ["UploadImageAsync"] = (Does.Refused, c => c.UploadImageAsync(new byte[] { 1 })),
                ["CreateRemovalTicketAsync"] = (Does.Refused, c => c.CreateRemovalTicketAsync("u/w1/x.png")),
                ["RemoveImageAsync"] = (Does.Refused, c => c.RemoveImageAsync("u/w1/x.png")),
                ["ListFilesAsync"] = (Does.Refused, c => c.ListFilesAsync("location", El)),
                ["WorldFilesAsync"] = (Does.Refused, c => c.WorldFilesAsync()),
                ["CreateFileTicketAsync"] = (Does.Refused, c => c.CreateFileTicketAsync("location", El, "notes.md", 1)),
                ["UploadFileAsync"] = (Does.Refused, c => c.UploadFileAsync("location", El, new byte[] { 1 }, name: "notes.md")),
                ["FileDownloadLinkAsync"] = (Does.Refused, c => c.FileDownloadLinkAsync("location", El, "f1")),
                ["RemoveFileAsync"] = (Does.Refused, c => c.RemoveFileAsync("location", El, "f1")),
                ["PatchAsync"] = (Does.Refused, c => c.PatchAsync<JObject>("location", El, new JObject { ["name"] = "H" })),
                ["DeleteAsync"] = (Does.Refused, c => c.DeleteAsync("location", El)),
                ["BulkAsync"] = (Does.Refused, c => c.BulkAsync(new[] { new OWBulkItem { Type = "location", Element = new JObject { ["name"] = "H" } } })),
                ["EditLinksAsync"] = (Does.Refused, c => c.EditLinksAsync<JObject>("location", El, "zones", add: new[] { El })),
                ["ProposeAsync"] = (Does.Refused, c => c.ProposeAsync("location", El, new JObject { ["name"] = "B" }, "seen")),
                ["ListProposalsAsync"] = (Does.Refused, c => c.ListProposalsAsync("location", El)),
                ["WorldProposalsAsync"] = (Does.Refused, c => c.WorldProposalsAsync()),
                ["AcceptProposalAsync"] = (Does.Refused, c => c.AcceptProposalAsync("location", El, Pid)),
                ["DeclineProposalAsync"] = (Does.Refused, c => c.DeclineProposalAsync("location", El, Pid)),
                ["WithdrawProposalAsync"] = (Does.Refused, c => c.WithdrawProposalAsync("location", El, Pid)),
                ["ReviewNewElementAsync"] = (Does.Refused, c => c.ReviewNewElementAsync("location", El, OWReviewed.Accept)),
                ["CreateAccountWorldAsync"] = (Does.Account, c => c.CreateAccountWorldAsync(new OWAccountWorldCreate { Name = "W" })),
                ["SetMemberLensesAsync"] = (Does.Account, c => c.SetMemberLensesAsync(W, "owner", new string[0])),
                ["ListAccountWorldsAsync"] = (Does.Account, c => c.ListAccountWorldsAsync()),
                ["MintWorldKeyAsync"] = (Does.Account, c => c.MintWorldKeyAsync(W)),
                ["RevokeWorldKeyAsync"] = (Does.Account, c => c.RevokeWorldKeyAsync(W, "k1")),
                ["AcceptJoinAsync"] = (Does.Account, c => c.AcceptJoinAsync("ow_j_code")),
                ["CreateWorldLinkAsync"] = (Does.Account, c => c.CreateWorldLinkAsync(W, new OWWorldLinkCreate { Reader = "person" })),
                ["ListWorldLinksAsync"] = (Does.Account, c => c.ListWorldLinksAsync(W)),
                ["RevokeWorldLinkAsync"] = (Does.Account, c => c.RevokeWorldLinkAsync(W, "l1")),
                ["CreateSeatAsync"] = (Does.Account, c => c.CreateSeatAsync(W, "Scout")),
                ["ListSeatsAsync"] = (Does.Account, c => c.ListSeatsAsync(W)),
                ["RekeySeatAsync"] = (Does.Account, c => c.RekeySeatAsync(W, "s1")),
                ["RemoveSeatAsync"] = (Does.Account, c => c.RemoveSeatAsync(W, "s1")),
                ["AskAsync"] = (Does.Account, c => c.AskAsync(W, "q", new[] { "x" })),
                ["SortAsync"] = (Does.Account, c => c.SortAsync(W, new OWSortNote[] { "x" }, new OWSortRef[0])),
                ["AllowanceAsync"] = (Does.Account, c => c.AllowanceAsync()),
                ["ChangesAsync"] = (Does.Refused, c => c.ChangesAsync()),
                // The escape hatch as a world read; its other rules are OWRequestRouteTests'.
                ["RequestAsync"] = (Does.Read, c => c.RequestAsync("GET", "/world/", new OWRequestOptions { WorldRead = true })),
            };

        [Test]
        public void TheTable_ClassifiesEveryPublicMethodOfTheClient()
        {
            var names = typeof(OWClient)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Distinct()
                .ToList();

            var unclassified = names.Where(n => !Methods.ContainsKey(n)).ToList();
            Assert.IsEmpty(unclassified,
                "methods this test does not classify (add each to Methods with what it does on an account client): "
                + string.Join(", ", unclassified));

            var stale = Methods.Keys.Where(n => !names.Contains(n)).ToList();
            Assert.IsEmpty(stale, "rows for methods the client no longer has: " + string.Join(", ", stale));
        }

        [Test]
        public async Task EveryMethod_OnAnAccountClientWithWorld_SendsAReadOrAnAccountRoute_OrIsRefusedBeforeSending()
        {
            foreach (var row in Methods)
            {
                var name = row.Key;
                var t = Answering();

                if (row.Value.Does == Does.Refused)
                {
                    var e = Assert.ThrowsAsync<InvalidOperationException>(() => row.Value.Run(Account(t)), name);
                    StringAssert.Contains("needs a world key", e.Message, name);
                    StringAssert.Contains("Nothing was sent", e.Message, name);
                    Assert.IsEmpty(t.Calls, $"{name}: nothing sent");
                    continue;
                }

                await row.Value.Run(Account(t));
                Assert.IsNotEmpty(t.Calls, $"{name}: sent");

                foreach (var sent in t.Calls)
                {
                    if (row.Value.Does == Does.Open)
                    {
                        CollectionAssert.IsEmpty(sent.Headers.Keys, $"{name}: no header");
                        Assert.IsEmpty(Values(sent, "world"), $"{name}: no world=");
                        continue;
                    }

                    Assert.AreEqual("Bearer ow_a_acct", sent.Headers["Authorization"], name);
                    Assert.IsFalse(sent.Headers.ContainsKey("API-Key") || sent.Headers.ContainsKey("API-Pin"), $"{name}: no API-Key or API-Pin");

                    var path = new Uri(sent.Url).AbsolutePath;
                    if (row.Value.Does == Does.Account)
                    {
                        StringAssert.StartsWith("/api/v2/account/", path, name);
                        Assert.IsEmpty(Values(sent, "world"), $"{name}: no world=");
                    }
                    else if (row.Value.Does == Does.Path)
                    {
                        StringAssert.Contains("/" + W + "/", path, $"{name}: the world in the path");
                        Assert.IsEmpty(Values(sent, "world"), $"{name}: no world=");
                    }
                    else
                    {
                        Assert.AreEqual(W, One(sent, "world"), $"{name}: world=");
                    }
                }
            }
        }

        [Test]
        public async Task EveryMethod_OnAKeylessClientWithWorld_SendsAReadWithNoCredential_OrIsRefusedBeforeSending()
        {
            foreach (var row in Methods)
            {
                var name = row.Key;
                var t = Answering();

                if (row.Value.Does != Does.Read && row.Value.Does != Does.Open)
                {
                    var e = Assert.ThrowsAsync<InvalidOperationException>(() => row.Value.Run(Keyless(t)), name);
                    StringAssert.Contains("holds none", e.Message, name);
                    StringAssert.Contains("Nothing was sent", e.Message, name);
                    Assert.IsEmpty(t.Calls, $"{name}: nothing sent");
                    continue;
                }

                await row.Value.Run(Keyless(t));
                Assert.AreEqual(1, t.Calls.Count, name);
                CollectionAssert.IsEmpty(t.Last.Headers.Keys, $"{name}: no header");
                if (row.Value.Does == Does.Open) Assert.IsEmpty(Values(t.Last, "world"), $"{name}: no world=");
                else Assert.AreEqual(W, One(t.Last, "world"), $"{name}: world=");
            }
        }

        [Test]
        public void TheRule_ReadsThePath_AnAccountRouteIsTheWordAndWhatIsUnderIt()
        {
            // The client's own calls cannot reach a path that only starts with "account"; the rule is the request's,
            // so it is read here as the request path reads it.
            var rule = typeof(OWClient).GetMethod("IsAccountRoute", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(rule, "the request path's account-route rule");

            foreach (var path in new[] { "/account", "/account/worlds", "/account/worlds/w1/keys", "/account?x=1" })
                Assert.IsTrue((bool)rule.Invoke(null, new object[] { path }), path);

            foreach (var path in new[] { "/accounts/x", "/world/account/", "/accountworlds", "/character/", "/world/" })
                Assert.IsFalse((bool)rule.Invoke(null, new object[] { path }), path);
        }
    }
}
