using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Every request <see cref="OWClient"/> can make, checked against keel's own OpenAPI document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client tests beside this one assert what the client SENDS. That cannot catch a client
    /// that sends the wrong thing consistently: <c>EditLinksAsync</c> posted to <c>/editLinks/</c>
    /// and <c>ChangesAsync</c> sent <c>cursor=</c> from 0.1.0 on, each with a green test asserting
    /// exactly that, while keel answered 404 and 422 (both found 2026-10-16). This suite asserts
    /// what keel SERVES: the path is one keel has, the method is one it takes there, every query
    /// parameter and header sent is one keel declares on that operation, and every key of a JSON
    /// body sent is one its request schema names. The other way round, for what the SDK reads: every
    /// key the join models map is one keel's answer declares, and every error code the
    /// <see cref="OWApiError"/> helpers branch on is one keel's document lists.
    /// </para>
    /// <para>
    /// The fixture is a snapshot of keel's published document (<c>Fixtures/wire/keel-wire.json</c>),
    /// refreshed by <c>tools/refresh_wire.py</c>, so the suite needs no network. A trailing slash is
    /// ignored when matching: keel serves both spellings (measured on <c>/changes/</c>, 2026-10-16).
    /// Body and answer keys go one level down there: <c>world.name</c>, <c>characters[].taken</c>.
    /// </para>
    /// </remarks>
    public class OWWireContractTests
    {
        private const string Base = "https://example.test/api/v2";

        // Never a declared parameter in OpenAPI; the body's media type.
        private static readonly string[] Undeclarable = { "Content-Type" };

        /// <summary>
        /// Keys a model maps that keel does not serve on this answer, on purpose, each with why. Each must stay
        /// absent from the document: when keel starts serving one, drop it here.
        /// </summary>
        private static readonly Dictionary<string, string> NotServed = new Dictionary<string, string>
        {
            // OWJoinWorld is shared with OWJoined, whose world carries the id; a preview's never does (the code is unspent).
            ["/api/v2/join/preview world.id"] = "OWJoinWorld.Id is null in a preview by design",
        };

        /// <summary>
        /// Codes a helper names that keel's document does not list yet, each with why. Each must stay unlisted:
        /// when keel lists one, drop it here.
        /// </summary>
        private static readonly Dictionary<string, string> Reserved = new Dictionary<string, string>
        {
            ["world_gone"] = "OWApiError.IsAuthError names it as reserved (keel has no world_gone row yet)",
        };

        private static JObject _wire;

        private static JObject Wire
        {
            get
            {
                if (_wire != null) return _wire;

                var root = TestPackage.Root();
                var path = root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "wire", "keel-wire.json");
                if (path == null || !File.Exists(path))
                {
                    Assert.Ignore("Wire fixture not found. These assertions did not run -- "
                        + "treat them as unmeasured, not as passing.");
                }

                return _wire = JObject.Parse(File.ReadAllText(path));
            }
        }

        /// <summary>Where the recording's tickets send their second request: the upload host, not keel.</summary>
        private const string UploadHost = "https://upload.example.test/v1/";

        /// <summary>A ticket answer for every ticketed call, image or file, and an empty list beside it.</summary>
        private const string Ticketed =
            @"{""ticket"":""tk.sig"",""upload_url"":""" + UploadHost + @"upload"",""remove_url"":""" + UploadHost
            + @"remove"",""max_bytes"":100,""data"":[]}";

        /// <summary>The draught's policy with every key given, so the recording sends each of its query parameters.</summary>
        private static readonly OWDraughtPolicy DraughtWithEveryKey = new OWDraughtPolicy
        {
            Components = new[] { "self", "messages" }, Mode = "act", AsOf = 300, Undated = "exclude", Since = "4:x",
            Budget = 20, Depth = 1, Compact = true, Lens = "l1",
        };

        /// <summary>
        /// The password sign-in's service (allauth's app API under the base URL), which keel's document does not
        /// describe: its two requests are left out of the checks as the upload host's are; the mint between them is keel's.
        /// </summary>
        private const string SignInService = Base + "/auth/app/";

        /// <summary>
        /// A request to keel: under the client's base URL, and not to the sign-in service. The upload host's are not
        /// keel's to judge.
        /// </summary>
        private static bool IsKeel(OWRequest request)
            => request.Url.StartsWith(Base + "/", StringComparison.Ordinal)
               && !request.Url.StartsWith(SignInService, StringComparison.Ordinal);

        /// <summary>
        /// Every public call on the client, recorded through a fake transport: keel's requests, which the checks below
        /// judge against keel's document. A two-step call's request to the upload host is left out here and kept by
        /// <see cref="RecordAllCalls"/>; its keel request is judged as every other.
        /// </summary>
        private static async Task<List<OWRequest>> RecordEveryCall()
            => (await RecordAllCalls()).Where(IsKeel).ToList();

        /// <summary>Every request the recording sends, keel's and the upload host's, in order.</summary>
        private static async Task<List<OWRequest>> RecordAllCalls()
        {
            var t = new FakeTransport();
            var c = new OWClient(new OWClientConfig
            {
                ApiKey = "ow_w_test",
                ApiPin = "0000",
                Transport = t,
                BaseUrl = Base,
                Agent = true,
            });

            // The health pulse, sent with no credential.
            await c.HealthAsync();
            await c.GetWorldAsync();
            // The world's origin written as its two plain keys, and an extension key.
            await c.PatchWorldAsync(new JObject
            {
                ["name"] = "W", ["source"] = new JObject { ["world"] = "w0" }, ["producer"] = new JObject { ["tool"] = "t" },
                ["x_names"] = new JObject { ["version"] = 1 },
            });
            await c.GetMeAsync();
            await c.GetMembersAsync();
            // The lens routes: the holder's choice (PUT, a body) and the world's lenses with both options.
            await c.SetMyLensesAsync(new[] { "l1", "l2" });
            await c.SetMyLensesAsync(null); // never chosen
            await c.WorldLensesAsync("w1", resolve: true, readAs: "guest");
            // The member's own Character, an id and none.
            await c.SetMyCharacterAsync("c1");
            await c.SetMyCharacterAsync(null);
            t.ResponseBody = @"{""data"":[],""has_more"":false}";
            await c.ListAsync<JObject>("character", new OWListParams
            {
                Limit = 5, Cursor = "c", Fields = new[] { "name" }, Name = "n", NameContains = "m",
                Supertype = "s", Subtype = "u",
            });
            // The typed options npm's list() takes: each must be a query parameter keel declares there.
            await c.ListAsync<JObject>("character", new OWListParams
            {
                Expand = new[] { "friends" }, Ids = new[] { "c1", "c2" }, CreatedBy = "owner", ViewAs = "public",
            });
            // Single-link filters: each field must be one keel declares on that list operation.
            await c.ListAsync<JObject>("location", new OWListParams
            {
                Filter = new System.Collections.Generic.Dictionary<string, string> { ["parent_location"] = "p1", ["zone"] = "" },
            });
            t.ResponseBody = "{}";
            await c.GetAsync<JObject>("character", "c1");
            await c.GetAsync<JObject>("character", "c1", expand: new[] { "friends" }, fields: new[] { "name" }, viewAs: "public");
            await c.CreateAsync<JObject>("character", new JObject { ["name"] = "N" });
            await c.UpsertAsync<JObject>("character", "c1", new JObject { ["name"] = "U" });
            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" });
            await c.DeleteAsync("character", "c1");
            await c.DeleteAsync("character", "c1", ifMatch: 7);
            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" }, ifMatch: 7);
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" }, ifMatch: 7);
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" });
            await c.BulkAsync(new[] { new OWBulkItem { Type = "character", Element = new JObject { ["name"] = "B" } } });
            await c.AdoptAsync<JObject>("character", "c1", "m1");
            // The hidden mark on an agent client: neither write header goes, which the header check holds.
            await c.HideAsync("character", "c1");
            await c.UnhideAsync("character", "c1");
            await c.ListHiddenAsync();
            // The draught, bare and with every policy key, so each query parameter is checked.
            await c.DraughtAsync("c1");
            await c.DraughtAsync("c1", DraughtWithEveryKey);
            // The image tickets, and the two calls that spend one at the upload host (a ticket passed: the host alone).
            t.ResponseBody = Ticketed;
            await c.CreateMediaTicketAsync();
            await c.UploadImageAsync(new byte[] { 1, 2, 3 }, key: "u/w1/x.png");
            await c.UploadImageAsync(new byte[] { 1 }, ticket: new OWMediaTicket { Ticket = "tk.sig", UploadUrl = UploadHost + "upload", MaxBytes = 8 });
            await c.CreateRemovalTicketAsync("https://media.example.test/u/w1/x.png");
            await c.RemoveImageAsync("u/w1/x.png");
            // Files on an element, on the agent client: the ticket with a label (X-OW-Agent, the one files route keel lists
            // it on), the upload (its ticket judged, the bytes to the upload host left out), the reads and the removal.
            await c.ListFilesAsync("character", "c1");
            await c.WorldFilesAsync();
            await c.CreateFileTicketAsync("character", "c1", "notes.md", 3, label: "description");
            await c.UploadFileAsync("character", "c1", new byte[] { 1, 2, 3 }, name: "notes.md", kind: "md", label: "description");
            await c.FileDownloadLinkAsync("character", "c1", "f1");
            await c.RemoveFileAsync("character", "c1", "f1");
            t.ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}";
            await c.ChangesAsync("4:069620b9-0794-708d-8000-42b0ea685f38");
            await c.ChangesAsync("4:069620b9-0794-708d-8000-42b0ea685f38", after: new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero));
            t.ResponseHeaders = new Dictionary<string, string> { ["etag"] = "W/\"0:-\"" };
            await c.ChangesAsync(headOnly: true);
            await c.ChangesAsync(headOnly: true); // sends If-None-Match
            t.ResponseHeaders = new Dictionary<string, string>();

            // The proposed change, every option given, so each query key, body key and header is checked.
            t.ResponseBody = "{}";
            var at = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            await c.ProposeAsync("character", "c1", new JObject { ["name"] = "P" }, "seen", expires: at, idempotencyKey: "k");
            await c.ListProposalsAsync("character", "c1", new OWProposalQuery { State = "all", Since = at, Limit = 5, Cursor = "c" });
            await c.WorldProposalsAsync(new OWProposalQuery { State = "open", Since = at, Element = "c1", Limit = 5, Cursor = "c" });
            await c.AcceptProposalAsync("character", "c1", "p1");
            await c.DeclineProposalAsync("character", "c1", "p1", "not canon");
            await c.WithdrawProposalAsync("character", "c1", "p1");
            // A new element written as proposed: each decision, the decline with its reason.
            t.ResponseBody = @"{""element"":{},""deleted"":""c1""}";
            await c.ReviewNewElementAsync("character", "c1", OWReviewed.Accept);
            await c.ReviewNewElementAsync("character", "c1", OWReviewed.Decline, "not canon");

            // The escape hatch on the agent client: a read, and an element write with every header it can be asked for.
            t.ResponseBody = "{}";
            await c.RequestAsync("GET", "/world/");
            await c.RequestAsync("PATCH", "/character/c1/", new OWRequestOptions
            {
                Body = new JObject { ["name"] = "R" }, Write = true, ElementWrite = true, Proposable = true, IfMatch = 7,
            });

            // The account's world create takes an account token: every member given, on the same transport.
            var account = new OWClient(new OWClientConfig { ApiKey = "ow_a_test", ApiPin = "0000", Transport = t, BaseUrl = Base, Agent = true });
            t.ResponseBody = @"{""id"":""w1"",""name"":""W"",""role"":""owner"",""public_read"":false,""keys"":[],""key"":""ow_w_x""}";
            await account.CreateAccountWorldAsync(new OWAccountWorldCreate
            {
                Name = "W", Pin = "4821", KeyName = "k",
                Source = new JObject { ["world"] = "w0" }, Producer = new JObject { ["tool"] = "t" },
            });
            // The account's worlds and their keys, on the agent account client: no write header goes on any.
            t.ResponseBody = @"{""data"":[]}";
            await account.ListAccountWorldsAsync();
            t.ResponseBody = "{}";
            // The key mint with every option, so each body key is checked.
            await account.MintWorldKeyAsync("w1", scope: "read", name: "laptop", keep: true);
            await account.RevokeWorldKeyAsync("w1", "k1");
            await account.AcceptJoinAsync("ow_j_x");
            // An invite link with every option at once (no real link takes all of them), so each body key is checked.
            await account.CreateWorldLinkAsync("w1", new OWWorldLinkCreate
            {
                Reader = "player", Role = "guest", Days = 30, InviterName = "Ada", WithCharacter = true, Character = "c1",
                Preview = true, MaxUses = 4, Characters = new List<string> { "c1", "c2" },
            });
            t.ResponseBody = @"{""data"":[]}";
            await account.ListWorldLinksAsync("w1");
            t.ResponseBody = "{}";
            await account.RevokeWorldLinkAsync("w1", "l1");
            // A seat for one's own AI, every option given; its list, a new key, its removal.
            await account.CreateSeatAsync("w1", "Scout", role: "contributor", character: "mine");
            t.ResponseBody = @"{""data"":[]}";
            await account.ListSeatsAsync("w1");
            t.ResponseBody = "{}";
            await account.RekeySeatAsync("w1", "s1");
            await account.RemoveSeatAsync("w1", "s1");
            // The AI routes: a question, notes of each shape (words, a picture, both) with a slice row anchored and one
            // not, so each body key is checked, and the meter.
            await account.AskAsync("w1", "Who rules?", new[] { "Ostvik: a town." });
            await account.SortAsync("w1",
                new[] { (OWSortNote)"Mira.", new OWSortNote { ImageUrl = "https://media.example.test/u/w1/n.png" },
                    new OWSortNote { Text = "Tor.", ImageUrl = "https://media.example.test/u/w1/t.png" } },
                new[] { new OWSortRef { Id = "c1", Type = "character", Name = "Mira", Anchor = "the ferrywoman" },
                    new OWSortRef { Id = "l1", Type = "location", Name = "Ostvik" } });
            await account.AllowanceAsync();

            // The lens routes on an account token: its own list in the world World names (world=), the world's lenses by
            // the path's world, and a row's list (PATCH with lenses alone).
            var lenses = new OWClient(new OWClientConfig { ApiKey = "ow_a_test", World = "w1", Transport = t, BaseUrl = Base });
            t.ResponseBody = "{}";
            await lenses.SetMyLensesAsync(new[] { "l1" });
            await lenses.WorldLensesAsync("w1", resolve: true, readAs: "guest");
            await lenses.SetMemberLensesAsync("w1", "owner", new[] { "l1" });
            await lenses.SetMemberLensesAsync("w1", "owner", null); // never chosen
            await lenses.GetMembersAsync();

            // The world reads an account token, or no key, sends with World set: world= beside each read's own query.
            foreach (var reader in new[]
            {
                new OWClient(new OWClientConfig { ApiKey = "ow_a_test", World = "w1", Transport = t, BaseUrl = Base }),
                new OWClient(new OWClientConfig { World = "w1", Transport = t, BaseUrl = Base }),
            })
            {
                t.ResponseBody = "{}";
                await reader.GetWorldAsync();
                await reader.GetAsync<JObject>("character", "c1");
                t.ResponseBody = @"{""data"":[],""has_more"":false}";
                await reader.ListAsync<JObject>("character", new OWListParams
                {
                    Limit = 5, Cursor = "c", Fields = new[] { "name" }, Name = "n", NameContains = "m",
                    Supertype = "s", Subtype = "u",
                });
            }

            // Signing in, on a keyless client: the password's three requests (the sign-in and the session's end go to the
            // sign-in service, left out of the checks; the mint between them is keel's), then Sign in with OnlyWorlds'
            // exchange.
            var signer = new OWClient(new OWClientConfig { World = "w1", Transport = t, BaseUrl = Base });
            t.Then(200, @"{""meta"":{""session_token"":""sess""}}");
            t.Then(201, @"{""id"":""tok-1"",""name"":"""",""last4"":""wxyz"",""token"":""ow_a_xwxyz""}");
            t.Then(401, "{}");
            await signer.SignInWithPasswordAsync("ana@example.org", null, "pw");
            var pending = signer.SignInWithOnlyWorlds("atlas", "https://atlas.example.test/cb");
            t.ResponseBody = @"{""token"":""ow_a_x"",""token_id"":""t1"",""token_type"":""bearer"",""account"":{""id"":""u1"",""username"":""a""}}";
            await signer.CompleteSignInAsync(pending, "https://atlas.example.test/cb?code=c1&state=" + pending.State);

            // Joining is keyless, so it is not on the client; the same transport records it. The redeem passes every
            // option at once (no real code takes all three) so each key it can put in the body is on the wire here.
            t.ResponseBody = @"{""reader"":""player"",""world"":{""name"":""W"",""description"":""""}}";
            await OWJoin.PreviewAsync("ow_j_x", t, Base);
            t.ResponseBody = @"{""kind"":""player"",""key"":""ow_r_x""}";
            await OWJoin.RedeemAsync("ow_j_x", t, name: "N", agentName: "A", baseUrl: Base, character: "c1");

            return t.Calls;
        }

        private static JObject FindOperation(OWRequest request, out string why)
        {
            var path = KeelPath(request);

            foreach (var entry in (JObject)Wire["paths"])
            {
                var pattern = "^" + Regex.Replace(Regex.Escape(entry.Key), @"\\\{[^}]+}", "[^/]+") + "$";
                if (!Regex.IsMatch(path, pattern)) continue;

                var op = entry.Value[request.Method] as JObject;
                why = op == null ? $"keel serves {entry.Key} but not {request.Method} there" : null;
                return op;
            }

            why = $"keel serves no path matching {path}";
            return null;
        }

        private static string KeelPath(OWRequest request)
        {
            var uri = new Uri(request.Url);
            return "/api/v2" + uri.AbsolutePath.Substring(new Uri(Base).AbsolutePath.Length).TrimEnd('/');
        }

        /// <summary>
        /// A JSON value's keys as the fixture names them: <c>a</c>, then one level down <c>a.b</c> or
        /// <c>a[].b</c>, but only where the document itself names keys under <c>a</c> (a free-form object is not
        /// walked into).
        /// </summary>
        private static IEnumerable<string> KeysOf(JObject body, ICollection<string> declared)
        {
            foreach (var p in body.Properties())
            {
                yield return p.Name;

                var below = declared.Any(d => d.StartsWith(p.Name + ".") || d.StartsWith(p.Name + "[]."));
                if (!below) continue;

                if (p.Value is JObject inner)
                {
                    foreach (var child in inner.Properties()) yield return p.Name + "." + child.Name;
                }
                else if (p.Value is JArray items)
                {
                    foreach (var name in items.OfType<JObject>().SelectMany(o => o.Properties()).Select(c => c.Name).Distinct())
                    {
                        yield return p.Name + "[]." + name;
                    }
                }
            }
        }

        /// <summary>
        /// The keys a model maps through <see cref="JsonPropertyAttribute"/>, as the fixture names them: a member's
        /// own name, and one level down the members of a model it holds (<c>world.name</c>) or a list of
        /// (<c>characters[].taken</c>). Read from the attributes, so a key added to the model is checked unasked.
        /// </summary>
        private static List<string> MappedKeys(Type model, bool descend = true)
        {
            var keys = new List<string>();
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
            var members = model.GetFields(Flags).Cast<MemberInfo>().Concat(model.GetProperties(Flags));

            foreach (var m in members)
            {
                var attr = m.GetCustomAttribute<JsonPropertyAttribute>();
                if (attr == null) continue;

                var name = attr.PropertyName ?? m.Name;
                keys.Add(name);
                if (!descend) continue;

                var type = m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;
                var item = type.IsArray ? type.GetElementType()
                    : type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type) ? type.GetGenericArguments()[0]
                    : null;

                if (item != null) keys.AddRange(MappedKeys(item, false).Select(k => name + "[]." + k));
                else keys.AddRange(MappedKeys(type, false).Select(k => name + "." + k));
            }

            return keys;
        }

        [Test]
        public async Task EveryCall_HitsAPathAndMethodKeelServes()
        {
            var failures = new List<string>();

            foreach (var call in await RecordEveryCall())
            {
                if (FindOperation(call, out var why) == null) failures.Add($"{call.Method} {call.Url}: {why}");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public async Task EveryQueryParameter_IsOneKeelDeclares()
        {
            var failures = new List<string>();

            foreach (var call in await RecordEveryCall())
            {
                var op = FindOperation(call, out _);
                if (op == null) continue;

                var declared = op["query"].Values<string>().ToList();
                var query = new Uri(call.Url).Query.TrimStart('?');
                if (query.Length == 0) continue;

                foreach (var name in query.Split('&').Select(pair => Uri.UnescapeDataString(pair.Split('=')[0])))
                {
                    if (!declared.Contains(name))
                    {
                        failures.Add($"{call.Method} {call.Url}: '{name}' is not a query parameter keel declares "
                            + $"there ({string.Join(", ", declared)})");
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public async Task EveryHeader_IsOneKeelDeclares()
        {
            var failures = new List<string>();
            var auth = Wire["auth_headers"].Values<string>().ToList();

            foreach (var call in await RecordEveryCall())
            {
                var op = FindOperation(call, out _);
                if (op == null) continue;

                var declared = op["header"].Values<string>().ToList();

                foreach (var name in call.Headers.Keys)
                {
                    var ok = auth.Contains(name, StringComparer.OrdinalIgnoreCase)
                        || declared.Contains(name, StringComparer.OrdinalIgnoreCase)
                        || Undeclarable.Contains(name, StringComparer.OrdinalIgnoreCase);

                    if (!ok)
                    {
                        failures.Add($"{call.Method} {call.Url}: header '{name}' is not one keel declares there");
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public async Task EveryBodyKey_IsOneKeelDeclares()
        {
            var failures = new List<string>();
            var checkedOps = new HashSet<string>();

            foreach (var call in await RecordEveryCall())
            {
                if (string.IsNullOrEmpty(call.Body)) continue;
                var op = FindOperation(call, out _);
                if (op == null) continue;

                var where = $"{call.Method} {KeelPath(call)}";
                if (!(op["body"] is JArray list))
                {
                    failures.Add($"{where}: sends a body where keel declares no JSON body");
                    continue;
                }

                var declared = list.Values<string>().ToList();
                var patterns = (op["body_patterns"] as JArray)?.Values<string>().ToList() ?? new List<string>();
                foreach (var key in KeysOf(JObject.Parse(call.Body), declared))
                {
                    checkedOps.Add(where);
                    if (!declared.Contains(key) && !MatchesAPattern(key, patterns))
                    {
                        failures.Add($"{where}: body key '{key}' is not one keel declares there "
                            + $"({string.Join(", ", declared.Where(d => !d.Contains('.')))})");
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));

            // An empty walk passes with nothing checked; the join routes carry the party link's keys.
            Assert.That(checkedOps, Does.Contain("POST /api/v2/join").And.Contain("POST /api/v2/join/preview"));
            Assert.That(checkedOps, Does.Contain("PATCH /api/v2/world"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/account/worlds"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/account/join"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/account/worlds/w1/links"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/account/worlds/w1/seats"));
            Assert.That(checkedOps, Does.Contain("PUT /api/v2/me/lenses"));
            Assert.That(checkedOps, Does.Contain("PUT /api/v2/me/character"));
            Assert.That(checkedOps, Does.Contain("PATCH /api/v2/account/worlds/w1/members/owner"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/media/remove-ticket"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/character/c1/files"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/account/ask").And.Contain("POST /api/v2/account/sort"));
            Assert.That(checkedOps, Does.Contain("POST /api/v2/auth/token"));
        }

        /// <summary>
        /// A top-level body key keel's request schema allows by pattern (<c>body_patterns</c>: its extension keys,
        /// <c>^(atlas_|shadow_|x_)</c>), never a nested one: the pattern is the schema's own, not a prefix list of ours.
        /// </summary>
        private static bool MatchesAPattern(string key, List<string> patterns)
            => !key.Contains('.') && !key.Contains("[]") && patterns.Any(p => Regex.IsMatch(key, p));

        [Test]
        public async Task PatchWorld_TakesBackEveryKeyTheWorldBodyServes_SendingOnlyWhatWorldPatchTakes()
        {
            // The Python package's wire vector: a GET /world body with every key the World serves, handed back, sends only
            // what PATCH /world takes, its names and its extension pattern. The next read-only key keel adds to the World
            // fails here, not as a caller's 422.
            var get = (JObject)Wire["paths"]["/api/v2/world"]["GET"];
            var patch = (JObject)Wire["paths"]["/api/v2/world"]["PATCH"];
            var takes = patch["body"].Values<string>().ToList();
            var patterns = patch["body_patterns"]?.Values<string>().ToList() ?? new List<string>();
            Assert.IsNotEmpty(patterns, "keel's WorldPatch declares its extension keys by pattern");

            var body = new JObject();
            foreach (var key in get["response"].Values<string>().Where(k => !k.Contains('.'))) body[key] = null;
            body["id"] = "w";
            body["x_names"] = null;

            var t = new FakeTransport();
            await new OWClient(new OWClientConfig { ApiKey = "ow_w_test", ApiPin = "0000", Transport = t, BaseUrl = Base })
                .PatchWorldAsync(body);

            var sent = JObject.Parse(t.Last.Body).Properties().Select(p => p.Name).ToList();
            var leaked = sent.Where(k => !takes.Contains(k) && !MatchesAPattern(k, patterns)).ToList();
            Assert.IsEmpty(leaked, "PatchWorldAsync still sends " + string.Join(", ", leaked));
            CollectionAssert.Contains(sent, "x_names");
            CollectionAssert.Contains(sent, "name");
        }

        [TestCase(typeof(OWJoinPreview), "/api/v2/join/preview", "POST")]
        [TestCase(typeof(OWJoined), "/api/v2/join", "POST")]
        [TestCase(typeof(OWChangesPage), "/api/v2/changes", "GET")]
        [TestCase(typeof(OWPage<OWProposal>), "/api/v2/world/proposals", "GET")]
        [TestCase(typeof(OWPage<OWProposal>), "/api/v2/character/{id}/proposals", "GET")]
        [TestCase(typeof(OWProposalAccepted), "/api/v2/character/{id}/proposals/{pid}/accept", "POST")]
        [TestCase(typeof(OWReviewed), "/api/v2/character/{id}/review", "POST")]
        [TestCase(typeof(OWAccountWorldCreated), "/api/v2/account/worlds", "POST")]
        [TestCase(typeof(OWMintedKey), "/api/v2/account/worlds/{world_id}/keys", "POST")]
        [TestCase(typeof(OWJoinedMembership), "/api/v2/account/join", "POST")]
        [TestCase(typeof(OWWorldLinkCreated), "/api/v2/account/worlds/{world_id}/links", "POST")]
        [TestCase(typeof(OWSeatCreated), "/api/v2/account/worlds/{world_id}/seats", "POST")]
        [TestCase(typeof(OWSeatCreated), "/api/v2/account/worlds/{world_id}/seats/{seat_id}/key", "POST")]
        [TestCase(typeof(OWMediaTicket), "/api/v2/media/ticket", "POST")]
        [TestCase(typeof(OWRemovalTicket), "/api/v2/media/remove-ticket", "POST")]
        [TestCase(typeof(OWFileTicket), "/api/v2/character/{id}/files", "POST")]
        [TestCase(typeof(OWFileDownload), "/api/v2/character/{id}/files/{file_id}/download", "GET")]
        [TestCase(typeof(OWWorldFiles), "/api/v2/world/files", "GET")]
        [TestCase(typeof(OWAskAnswer), "/api/v2/account/ask", "POST")]
        [TestCase(typeof(OWSortAnswer), "/api/v2/account/sort", "POST")]
        [TestCase(typeof(OWAllowance), "/api/v2/account/allowance", "GET")]
        [TestCase(typeof(OWSignedIn), "/api/v2/account/tokens", "POST")]
        [TestCase(typeof(OWSignInResult), "/api/v2/auth/token", "POST")]
        public void EveryKeyAModelReads_IsOneKeelServes(Type model, string path, string method)
        {
            var op = Wire["paths"]?[path]?[method] as JObject;
            Assert.IsNotNull(op, $"{method} {path} is not in the fixture");
            Assert.IsNotNull(op["response"], $"{method} {path} declares no JSON answer in the fixture");

            var served = op["response"].Values<string>().ToList();
            var mapped = MappedKeys(model);
            Assert.IsNotEmpty(mapped, $"{model.Name} maps no keys");

            var missing = mapped.Where(k => !served.Contains(k) && !NotServed.ContainsKey(path + " " + k)).ToList();
            Assert.IsEmpty(missing, $"{model.Name} reads keys {method} {path} does not serve: {string.Join(", ", missing)} "
                + $"(served: {string.Join(", ", served)})");

            var stale = NotServed.Keys.Where(k => k.StartsWith(path + " ")).Where(k => served.Contains(k.Substring(path.Length + 1)));
            Assert.IsEmpty(stale, "keel serves these now; drop them from NotServed: " + string.Join(", ", stale));
        }

        [TestCase(typeof(OWAccountWorld), "/api/v2/account/worlds")]
        [TestCase(typeof(OWHiddenRow), "/api/v2/world/hidden")]
        [TestCase(typeof(OWWorldLink), "/api/v2/account/worlds/{world_id}/links")]
        [TestCase(typeof(OWSeat), "/api/v2/account/worlds/{world_id}/seats")]
        [TestCase(typeof(OWFileRow), "/api/v2/character/{id}/files")]
        [TestCase(typeof(OWWorldFileRow), "/api/v2/world/files")]
        public void EveryKeyAListRowReads_IsOneTheListServes_AndEveryOneItServesIsRead(Type row, string path)
        {
            // ListAccountWorldsAsync, ListHiddenAsync, ListWorldLinksAsync and ListSeatsAsync read the rows of { data }, so
            // the row's keys are checked one level down.
            var served = Wire["paths"][path]["GET"]["response"].Values<string>().ToList();
            var mapped = MappedKeys(row, false).Select(k => "data[]." + k).ToList();

            Assert.IsNotEmpty(mapped, $"{row.Name} maps no keys");
            CollectionAssert.AreEquivalent(served.Where(k => k.StartsWith("data[].")).ToList(), mapped);
        }

        [Test]
        public void TheAccountsWorldsAndTheKeyMint_AreTheRowsKeelDocuments()
        {
            // keel documents these since a5158cb (PR 385): the mint's body (scope, name, keep, none required), its 201 row
            // with the full key, and GET /account/worlds' rows. MintWorldKeyAsync sends exactly the body's three keys, and
            // OWMintedKey reads every key of the 201 (member as served), so a key keel adds there fails here.
            var mint = (JObject)Wire["paths"]["/api/v2/account/worlds/{world_id}/keys"]["POST"];
            CollectionAssert.AreEquivalent(new[] { "scope", "name", "keep" }, mint["body"].Values<string>());
            CollectionAssert.AreEquivalent(
                mint["response"].Values<string>().Where(k => !k.Contains('.')),
                MappedKeys(typeof(OWMintedKey), false));

            // The fixture lists a list row's keys one level down, so a world's keys stop at data[].keys: keel's document
            // names theirs (name, last4, scope READ or WRITE, revoked, all required; read off it by hand 2026-10-31), which
            // OWAccountWorldKey maps. When the fixture reaches them, this fails: check OWAccountWorldKey against it then.
            var worlds = Wire["paths"]["/api/v2/account/worlds"]["GET"]["response"].Values<string>().ToList();
            Assert.That(worlds, Does.Contain("data[].keys").And.Not.Contains("data[].keys[].name"),
                "the fixture names a world's keys' keys now: check OWAccountWorldKey against them");
            CollectionAssert.AreEquivalent(
                new[] { "name", "last4", "scope", "revoked" }, MappedKeys(typeof(OWAccountWorldKey)));

            // The created world's row is the listed row's, plus its key (keel's AccountWorldCreated).
            var created = Wire["paths"]["/api/v2/account/worlds"]["POST"]["response"].Values<string>().ToList();
            var listed = MappedKeys(typeof(OWAccountWorld)).Where(k => k != "member_role").ToList();
            CollectionAssert.AreEquivalent(created.Where(k => k != "key").ToList(), listed);
        }

        [Test]
        public void TheUploadHostsAnswers_AreNotKeels_AndTheModelsMapWhatTheTypeScriptSDKTypes()
        {
            // The second step of a ticketed call answers from the upload host, which keel's document does not describe
            // (its tickets name it only in words: send the ticket to upload_url, to remove_url). So the host's answers are
            // held to the TypeScript SDK's types at 4.120.0 (OwUploadedImage, OwRemovedImage, OwUploadedFile) by their keys.
            foreach (var path in ((JObject)Wire["paths"]).Properties().Select(p => p.Name))
            {
                StringAssert.DoesNotStartWith("/v1/", path, "keel's document describes an upload host route now: check the models against it");
            }

            CollectionAssert.AreEquivalent(new[] { "url", "key", "bytes", "type", "etag" }, MappedKeys(typeof(OWUploadedImage)));
            CollectionAssert.AreEquivalent(new[] { "removed", "bytes", "cache" }, MappedKeys(typeof(OWRemovedImage)));
            CollectionAssert.AreEquivalent(new[] { "key", "fid", "bytes", "type", "etag" }, MappedKeys(typeof(OWUploadedFile)));

            // keel's document names a file row's added_by but not its keys: held to the TypeScript SDK's OwFileAddedBy.
            var rows = Wire["paths"]["/api/v2/character/{id}/files"]["GET"]["response"].Values<string>().ToList();
            Assert.That(rows, Does.Contain("data[].added_by").And.Not.Contains("data[].added_by.name"),
                "keel's document names added_by's keys now: check OWFileAddedBy against it");
            CollectionAssert.AreEquivalent(new[] { "name", "membership", "agent" }, MappedKeys(typeof(OWFileAddedBy)));
        }

        [Test]
        public void TheSignInService_AndTheTokenMintsBody_AreWhatKeelLeavesUndocumented()
        {
            // The password sign-in's first and last requests go to allauth's app API under the base URL, which keel's
            // document does not describe: they are left out of the checks, as the upload host's are. The day keel
            // documents a path there, check SignInWithPasswordAsync's two requests against it.
            foreach (var path in ((JObject)Wire["paths"]).Properties().Select(p => p.Name))
                StringAssert.DoesNotStartWith("/api/v2/auth/app/", path, "keel's document describes the sign-in service now");

            // keel's mint reads an optional {name} (accounts/api.py mint_token: _name_param), and the TypeScript SDK sends
            // { name: tokenName }, but the document declares no body there, so SignInWithPasswordAsync sends none and its
            // token is minted with no name. The day keel documents the body, this fails: add the token's name then.
            var mint = (JObject)Wire["paths"]["/api/v2/account/tokens"]["POST"];
            Assert.IsNull(mint["body"], "keel's document declares the mint's body now: send the token's name");
            CollectionAssert.IsEmpty(mint["header"], "X-Session-Token is one of the document's auth headers, not the operation's");
            CollectionAssert.Contains(Wire["auth_headers"].Values<string>().ToList(), "X-Session-Token");
        }

        [Test]
        public async Task TheDraughtsPolicy_SendsEveryQueryParameterKeelDeclares_ButWorld()
        {
            // The policy's nine keys are every one keel's document declares on the draught, but `world`: an account
            // token's or a keyless reader's world, which the TypeScript SDK's draught never sends (it refuses both
            // clients), so neither does this one. A key keel adds to the draught fails here: add it to OWDraughtPolicy.
            var declared = Wire["paths"]["/api/v2/character/{id}/context"]["GET"]["query"].Values<string>().ToList();

            var t = new FakeTransport();
            await new OWClient(new OWClientConfig { ApiKey = "ow_w_test", Transport = t, BaseUrl = Base })
                .DraughtAsync("c1", DraughtWithEveryKey);
            var sent = new Uri(t.Last.Url).Query.TrimStart('?').Split('&').Select(p => p.Split('=')[0]).ToList();

            CollectionAssert.AreEquivalent(declared.Where(k => k != "world"), sent);
            CollectionAssert.Contains(declared, "world", "keel declares world= on the draught; the client leaves it out");
        }

        [TestCase(typeof(OWAccountWorldCreate), "/api/v2/account/worlds", "POST")]
        [TestCase(typeof(OWWorldLinkCreate), "/api/v2/account/worlds/{world_id}/links", "POST")]
        public void EveryKeyARequestModelMaps_IsOneKeelTakes(Type model, string path, string method)
        {
            // The body is serialized from the model's attributes, so a key the model maps that keel's request schema does
            // not name fails here, sent or not.
            var op = Wire["paths"]?[path]?[method] as JObject;
            Assert.IsNotNull(op?["body"], $"{method} {path} declares no JSON body in the fixture");

            var takes = op["body"].Values<string>().ToList();
            var mapped = MappedKeys(model);
            Assert.IsNotEmpty(mapped, $"{model.Name} maps no keys");

            var unknown = mapped.Where(k => !takes.Contains(k)).ToList();
            Assert.IsEmpty(unknown, $"{model.Name} maps keys {method} {path} does not take: {string.Join(", ", unknown)}");
        }

        [TestCase("/api/v2/me", "GET")]
        [TestCase("/api/v2/join", "POST")]
        public void EveryFrontDoorKey_IsOneKeelServes_AndEveryOneItServesIsRead(string path, string method)
        {
            // front_door on /me and on an agent's join. GetMeAsync answers a JObject, so the model is checked here.
            var served = Wire["paths"][path][method]["response"].Values<string>()
                .Where(k => k.StartsWith("front_door.")).ToList();
            var mapped = MappedKeys(typeof(OWFrontDoor), false).Select(k => "front_door." + k).ToList();

            Assert.IsNotEmpty(served, $"{method} {path} serves no front_door keys in the fixture");
            CollectionAssert.AreEquivalent(served, mapped);
        }

        [TestCase("/api/v2/me", "GET")]
        [TestCase("/api/v2/join", "POST")]
        public void TheReadingAids_AreThePacketOWReadingAidsReads(string path, string method)
        {
            // reading_aids beside front_door: OWReadingAids.Of reads {charter, guide, domains, strand}, the packet ToJson
            // writes. keel always serves the strand (serial 100), six null slots for a world with none.
            var served = Wire["paths"][path][method]["response"].Values<string>()
                .Where(k => k.StartsWith("reading_aids.")).ToList();
            var read = OWReadingAids.Of(new JObject { ["reading_aids"] = new JObject
                {
                    ["charter"] = null, ["guide"] = null, ["domains"] = new JArray(),
                    ["strand"] = new JObject
                    {
                        ["narrative"] = null, ["event"] = null, ["location"] = null,
                        ["character"] = null, ["now"] = null, ["same"] = null,
                    },
                } }).ToJson().Properties().Select(p => "reading_aids." + p.Name).ToList();

            Assert.IsNotEmpty(served, $"{method} {path} serves no reading_aids keys in the fixture");
            CollectionAssert.AreEquivalent(served, read);
        }

        [TestCase("/api/v2/me", "GET")]
        [TestCase("/api/v2/me/lenses", "PUT")]
        public void TheMeAnswer_ServesLenses(string path, string method)
        {
            // GetMeAsync and SetMyLensesAsync answer a JObject, so no model maps the key; their docs say it is there.
            var served = Wire["paths"]?[path]?[method]?["response"]?.Values<string>().ToList();
            Assert.IsNotNull(served, $"{method} {path} declares no JSON answer in the fixture");
            CollectionAssert.Contains(served, "lenses");
        }

        [Test]
        public void EveryErrorCodeAHelperNames_IsOneKeelLists()
        {
            var listed = (JObject)Wire["error_codes"];
            Assert.IsNotNull(listed, "the fixture has no error_codes: refresh it with tools/refresh_wire.py");

            // The helpers' own text: every `Code == "x"` and `case "x":` in OWApiError, so a helper added later is
            // checked unasked. Reflection cannot see a property's body, so the source is read instead.
            var root = TestPackage.Root();
            var file = root == null ? null : Path.Combine(root, "Runtime", "Core", "OWApiError.cs");
            if (file == null || !File.Exists(file)) Assert.Ignore("OWApiError.cs not found: unmeasured, not passing.");
            var source = File.ReadAllText(file);

            var named = Regex.Matches(source, @"Code == ""([a-z_]+)""|case ""([a-z_]+)"":")
                .Cast<Match>().Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
            Assert.That(named.Count, Is.GreaterThan(5), "the helpers' codes were not read: " + string.Join(", ", named));

            var failures = named.Where(c => listed[c] == null && !Reserved.ContainsKey(c))
                .Select(c => $"'{c}' is named by an OWApiError helper but keel's document does not list it").ToList();

            // Where a helper pairs the code with a status (`StatusCode == 422 && Code == "x"`), keel's must agree.
            foreach (Match m in Regex.Matches(source, @"StatusCode == (\d+) && Code == ""([a-z_]+)"""))
            {
                var code = m.Groups[2].Value;
                if (listed[code] == null) continue;
                if ((int)listed[code] != int.Parse(m.Groups[1].Value))
                {
                    failures.Add($"'{code}': a helper checks status {m.Groups[1].Value}, keel lists {listed[code]}");
                }
            }

            failures.AddRange(Reserved.Keys.Where(c => listed[c] != null)
                .Select(c => $"'{c}' is listed by keel now; drop it from Reserved"));

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public async Task TheRecordingCoversEveryCall()
        {
            // An empty recording would pass the checks above with nothing checked.
            var calls = await RecordEveryCall();
            Assert.AreEqual(84, calls.Count);

            // The upload host's and the sign-in service's requests are recorded and left out of the checks; the keel
            // request of each two-step call, and the password sign-in's mint, are in, so the checks above judged them.
            var all = (await RecordAllCalls()).Where(c => !IsKeel(c)).ToList();
            var signIn = all.Where(c => c.Url.StartsWith(SignInService, StringComparison.Ordinal)).ToList();
            CollectionAssert.AreEqual(new[] { "POST " + SignInService + "v1/auth/login", "DELETE " + SignInService + "v1/auth/session" },
                signIn.Select(c => c.Method + " " + c.Url).ToArray(), "the sign-in and the session's end, at the sign-in service");
            Assert.AreEqual(1, calls.Count(c => c.Method == "POST" && new Uri(c.Url).AbsolutePath.EndsWith("/account/tokens")),
                "the password sign-in's mint is judged");
            Assert.AreEqual(1, calls.Count(c => c.Method == "POST" && new Uri(c.Url).AbsolutePath.EndsWith("/auth/token")),
                "Sign in with OnlyWorlds' exchange is judged");
            var host = all.Where(c => !c.Url.StartsWith(SignInService, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(4, host.Count, "the two image uploads, the removal and the file upload reached the upload host");
            Assert.AreEqual(2, calls.Count(c => c.Method == "POST" && new Uri(c.Url).AbsolutePath.EndsWith("/character/c1/files")),
                "the file ticket alone and the upload's own ticket are judged");
            Assert.That(host.All(c => c.Url.StartsWith(UploadHost, StringComparison.Ordinal)), "only the upload host's requests are left out");
            Assert.AreEqual(2, calls.Count(c => c.Method == "POST" && new Uri(c.Url).AbsolutePath.EndsWith("/media/ticket")),
                "the ticket alone and the upload's own ticket are judged");
            Assert.AreEqual(2, calls.Count(c => c.Method == "POST" && new Uri(c.Url).AbsolutePath.EndsWith("/media/remove-ticket")),
                "the removal ticket alone and the removal's own ticket are judged");

            // The lens routes are on the wire here, so the checks above saw them.
            Assert.That(calls.Any(c => c.Method == "PUT" && new Uri(c.Url).AbsolutePath.EndsWith("/me/lenses")), "PUT /me/lenses is recorded");
            Assert.That(calls.Any(c => c.Method == "GET" && new Uri(c.Url).AbsolutePath.EndsWith("/world/w1/lenses")
                && new Uri(c.Url).Query.Contains("read_as=")), "GET /world/{world_id}/lenses with its options is recorded");
            Assert.That(calls.Any(c => c.Method == "PUT" && new Uri(c.Url).AbsolutePath.EndsWith("/me/lenses")
                && new Uri(c.Url).Query.Contains("world=")), "PUT /me/lenses on an account token, with world=, is recorded");
            Assert.That(calls.Any(c => c.Method == "PATCH" && new Uri(c.Url).AbsolutePath.EndsWith("/account/worlds/w1/members/owner")),
                "PATCH /account/worlds/{world_id}/members/{member_id} is recorded");
            Assert.AreEqual(2, calls.Count(c => c.Method == "GET" && new Uri(c.Url).AbsolutePath.EndsWith("/api/v2/members/")),
                "GET /members on a world key and on an account token is recorded");

            // The world reads of an account token and of no key, and the account token's PUT /me/lenses and GET /members,
            // so the query check above saw world=.
            Assert.AreEqual(8, calls.Count(c => new Uri(c.Url).Query.Contains("world=")), "the world reads are recorded");

            // The conditional headers are on the wire here, so the header check above saw them.
            Assert.That(calls.Any(c => c.Headers.ContainsKey("If-Match")), "a delete with ifMatch is recorded");
            Assert.That(calls.Any(c => c.Headers.ContainsKey("If-None-Match")), "a tip read with a tag is recorded");
        }
    }
}
