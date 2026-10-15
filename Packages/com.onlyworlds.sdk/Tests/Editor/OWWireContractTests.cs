using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    /// what keel SERVES: the path is one keel has, the method is one it takes there, and every query
    /// parameter and header sent is one keel declares on that operation.
    /// </para>
    /// <para>
    /// The fixture is a snapshot of keel's published document (<c>Fixtures/wire/keel-wire.json</c>),
    /// refreshed by <c>tools/refresh_wire.py</c>, so the suite needs no network. A trailing slash is
    /// ignored when matching: keel serves both spellings (measured on <c>/changes/</c>, 2026-10-16).
    /// </para>
    /// </remarks>
    public class OWWireContractTests
    {
        private const string Base = "https://example.test/api/v2";

        // Never a declared parameter in OpenAPI; the body's media type.
        private static readonly string[] Undeclarable = { "Content-Type" };

        private static JObject _wire;

        private static JObject Wire
        {
            get
            {
                if (_wire != null) return _wire;

                var path = FindFixture();
                if (path == null)
                {
                    Assert.Ignore("Wire fixture not found. These assertions did not run -- "
                        + "treat them as unmeasured, not as passing.");
                }

                return _wire = JObject.Parse(File.ReadAllText(path));
            }
        }

        /// <summary>The package's own resolved path first, as the folder writer's fixture is found.</summary>
        private static string FindFixture()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWWireContractTests).Assembly);
            if (package == null) return null;

            var path = Path.Combine(package.resolvedPath, "Tests", "Editor", "Fixtures", "wire", "keel-wire.json");
            return File.Exists(path) ? path : null;
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
            await c.EditLinksAsync<JObject>("character", "c1", "friends", add: new[] { "a1" });
            await c.BulkAsync(new[] { new OWBulkItem { Type = "character", Element = new JObject { ["name"] = "B" } } });
            t.ResponseBody = @"{""changes"":[],""has_more"":false,""head"":0}";
            await c.ChangesAsync("4:069620b9-0794-708d-8000-42b0ea685f38");
            await c.ChangesAsync(headOnly: true);

            return t.Calls;
        }

        private static JObject FindOperation(OWRequest request, out string why)
        {
            var uri = new Uri(request.Url);
            var path = "/api/v2" + uri.AbsolutePath.Substring(new Uri(Base).AbsolutePath.Length).TrimEnd('/');

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
        public async Task TheRecordingCoversEveryCall()
        {
            // An empty recording would pass the three checks above with nothing checked.
            Assert.AreEqual(10, (await RecordEveryCall()).Count);
        }
    }
}
