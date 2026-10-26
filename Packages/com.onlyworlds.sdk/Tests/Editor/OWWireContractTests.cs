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

                var root = PackageRoot();
                var path = root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "wire", "keel-wire.json");
                if (path == null || !File.Exists(path))
                {
                    Assert.Ignore("Wire fixture not found. These assertions did not run -- "
                        + "treat them as unmeasured, not as passing.");
                }

                return _wire = JObject.Parse(File.ReadAllText(path));
            }
        }

        /// <summary>
        /// The package's own resolved path, as the folder writer's fixture is found. Outside the Editor (a compile
        /// and test run with no engine, where the package manager cannot be asked) the working directory's
        /// <c>Packages/com.onlyworlds.sdk</c>, when it is there.
        /// </summary>
        private static string PackageRoot()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWWireContractTests).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }

        /// <summary>Every public call on the client, recorded through a fake transport.</summary>
        private static async Task<List<OWRequest>> RecordEveryCall()
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

            await c.GetWorldAsync();
            // The world's origin written as its two plain keys, and an extension key.
            await c.PatchWorldAsync(new JObject
            {
                ["name"] = "W", ["source"] = new JObject { ["world"] = "w0" }, ["producer"] = new JObject { ["tool"] = "t" },
                ["x_names"] = new JObject { ["version"] = 1 },
            });
            await c.GetMeAsync();
            t.ResponseBody = @"{""data"":[],""has_more"":false}";
            await c.ListAsync<JObject>("character", new OWListParams
            {
                Limit = 5, Cursor = "c", Fields = new[] { "name" }, Name = "n", NameContains = "m",
                Supertype = "s", Subtype = "u",
            });
            t.ResponseBody = "{}";
            await c.GetAsync<JObject>("character", "c1");
            await c.CreateAsync<JObject>("character", new JObject { ["name"] = "N" });
            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" });
            await c.DeleteAsync("character", "c1");
            await c.DeleteAsync("character", "c1", ifMatch: 7);
            await c.PatchAsync<JObject>("character", "c1", new JObject { ["name"] = "M" }, ifMatch: 7);
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" }, ifMatch: 7);
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" });
            await c.BulkAsync(new[] { new OWBulkItem { Type = "character", Element = new JObject { ["name"] = "B" } } });
            t.ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}";
            await c.ChangesAsync("4:069620b9-0794-708d-8000-42b0ea685f38");
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

            // The account's world create takes an account token: every member given, on the same transport.
            var account = new OWClient(new OWClientConfig { ApiKey = "ow_a_test", ApiPin = "0000", Transport = t, BaseUrl = Base, Agent = true });
            t.ResponseBody = @"{""id"":""w1"",""name"":""W"",""role"":""owner"",""public_read"":false,""keys"":[],""key"":""ow_w_x""}";
            await account.CreateAccountWorldAsync(new OWAccountWorldCreate
            {
                Name = "W", Pin = "4821", KeyName = "k",
                Source = new JObject { ["world"] = "w0" }, Producer = new JObject { ["tool"] = "t" },
            });

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

        [TestCase(typeof(OWAccountWorldCreate), "/api/v2/account/worlds", "POST")]
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

        [Test]
        public void EveryErrorCodeAHelperNames_IsOneKeelLists()
        {
            var listed = (JObject)Wire["error_codes"];
            Assert.IsNotNull(listed, "the fixture has no error_codes: refresh it with tools/refresh_wire.py");

            // The helpers' own text: every `Code == "x"` and `case "x":` in OWApiError, so a helper added later is
            // checked unasked. Reflection cannot see a property's body, so the source is read instead.
            var root = PackageRoot();
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
            Assert.AreEqual(27, calls.Count);

            // The conditional headers are on the wire here, so the header check above saw them.
            Assert.That(calls.Any(c => c.Headers.ContainsKey("If-Match")), "a delete with ifMatch is recorded");
            Assert.That(calls.Any(c => c.Headers.ContainsKey("If-None-Match")), "a tip read with a tag is recorded");
        }
    }
}
