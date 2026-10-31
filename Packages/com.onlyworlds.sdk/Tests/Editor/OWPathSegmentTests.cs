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
    /// A caller's string never changes which route is asked: every type, id, link field and other caller-supplied value
    /// in a request path is sent as one escaped segment (<c>Uri.EscapeDataString</c>). keel's document declares each element
    /// id a UUID, so the escape changes nothing on a real id; on <c>abc/hidden</c> the raw form asked a different route.
    /// Parity with the TypeScript SDK 4.122.1 (its <c>test/path-segments.test.mjs</c>, ported). Two halves: the source scan
    /// holds every path the client builds, so a new route that interpolates a value raw, or builds its path some way the
    /// scan cannot read, fails here; the calls show the wire for the routes that sent raw.
    /// </summary>
    public class OWPathSegmentTests
    {
        // -- The source scan --------------------------------------------------------------------------------

        private static string Code(string file)
        {
            var root = TestPackage.Root();
            Assert.IsNotNull(root, "the package was not found");
            var text = File.ReadAllText(Path.Combine(root, "Runtime", "Core", file)).Replace("\r\n", "\n");
            // Code only: a line whose first non-space is // is a comment (doc comments name routes in prose).
            return string.Join("\n", text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        }

        private const string Lit = "\"[^\"\\n]*\"";
        private const string Interp = "\\$\"[^\"\\n]*\"";
        private const string Calls = @"\b(?:RequestAsync|RequestWithResponseAsync)<[\w<>, ]+>\(";

        // A path helper: `private static string XPath(string type, string id) => $"...";`. Found by shape, so a new
        // helper of the same shape joins the scan; any other helper is a path the scan cannot read.
        private static readonly Regex HelperDecl = new Regex(
            @"private static string (\w+Path)\(string type, string id\)\s*=>\s*(" + Interp + @")\s*;");

        private static readonly Regex Hole = new Regex(@"\{([^{}]*)\}");

        [Test]
        public void Source_EveryValueInARequestPath_IsEscaped_AndEveryPathIsOneTheScanReads()
        {
            var src = Code("OWClient.cs");

            var helpers = HelperDecl.Matches(src).Cast<Match>().ToList();
            var helperNames = helpers.Select(m => m.Groups[1].Value).ToList();
            CollectionAssert.IsNotEmpty(helperNames, "no path helper found; the scan lost its shape");
            var helperCall = "(?:" + string.Join("|", helperNames) + @")\(type, id\)";

            // Every typed request call's path is a whole literal, a whole interpolated string, `path` or a helper call.
            var arg = "(" + Lit + "|" + Interp + "|path|" + helperCall + ")";
            var calls = Regex.Matches(src, Calls + "\\s*\"[A-Z]+\",\\s*" + arg + "\\s*[,)]").Cast<Match>().ToList();

            // Every invocation is one of those, the two forwarders that pass the caller's (method, path) through, or a
            // declaration. Anything else (`"/x/" + id`, an unknown helper, a variable) is a path the scan cannot read.
            var all = Regex.Matches(src, Calls).Count;
            var forwarders = Regex.Matches(src, Calls + @"\s*method,\s*path\b").Count;
            var declarations = Regex.Matches(src, Calls + @"\s*string method,").Count;
            Assert.AreEqual(2, forwarders, "the forwarders of (method, path): the public RequestAsync and RequestAsync<T>");
            Assert.AreEqual(2, declarations, "RequestAsync<T> and RequestWithResponseAsync<T>");
            Assert.AreEqual(all, calls.Count + forwarders + declarations,
                "a request call whose method is not a literal, or whose path is not a whole literal, interpolated string, "
                + "`path` or a path helper");

            // The templates: each call's interpolated path, each `var path = $"..."`, each helper's body.
            var templates = calls.Select(m => m.Groups[1].Value).Where(a => a.StartsWith("$", StringComparison.Ordinal)).ToList();
            var pathVars = Regex.Matches(src, @"\bvar path\s*=").Count;
            var pathTemplates = Regex.Matches(src, @"\bvar path\s*=\s*(" + Interp + @")\s*;").Cast<Match>()
                .Select(m => m.Groups[1].Value).ToList();
            Assert.AreEqual(pathVars, pathTemplates.Count, "a `var path` that is not one interpolated string");
            templates.AddRange(pathTemplates);
            templates.AddRange(helpers.Select(m => m.Groups[2].Value));
            Assert.GreaterOrEqual(templates.Count, 30, $"found {templates.Count} path templates; the scan lost its shape");

            var raw = new List<string>();
            foreach (var t in templates)
            {
                foreach (Match h in Hole.Matches(t))
                {
                    var e = h.Groups[1].Value;
                    if (Regex.IsMatch(e, @"^Uri\.EscapeDataString\(\w+\)$")) continue;
                    if (Regex.IsMatch(e, "^" + helperCall + "$")) continue;
                    raw.Add($"{{{e}}} in {t}");
                }
            }

            CollectionAssert.IsEmpty(raw, "a raw segment in a request path:\n" + string.Join("\n", raw));
        }

        [Test]
        public void Source_EveryUrlTheClientsSendTo_IsTheFunnelsUrlOrAFixedRoute()
        {
            // Outside the funnel, a request's Url is the funnel's `url`, or a base and one fixed literal (sign-in).
            var client = Code("OWClient.cs");
            var urls = Regex.Matches(client, @"\bUrl\s*=\s*([^,\n]+?)\s*,").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.GreaterOrEqual(urls.Count, 5, $"found {urls.Count} Url assignments; the scan lost its shape");
            var odd = urls.Where(u => u != "url" && !Regex.IsMatch(u, "^(?:BaseUrl|auth) \\+ " + Lit + "$")).ToList();
            CollectionAssert.IsEmpty(odd, "a request Url built from a value:\n" + string.Join("\n", odd));

            // The join routes (no client) send to a base and a path that is always a literal.
            var join = Code("OWJoin.cs");
            var posts = Regex.Matches(join, @"\bPostAsync<\w+>\(").Count;
            var literal = Regex.Matches(join, @"\bPostAsync<\w+>\(\s*" + Lit + "\\s*,").Count;
            var declared = Regex.Matches(join, @"\bPostAsync<\w+>\(\s*string path,").Count;
            Assert.AreEqual(1, declared, "PostAsync's declaration");
            Assert.GreaterOrEqual(literal, 2, "the join routes");
            Assert.AreEqual(posts, literal + declared, "a join route whose path is not a literal");
        }

        [Test]
        public void Source_OnlyTheScannedFiles_BuildRequests()
        {
            // A request is built in OWClient.cs or OWJoin.cs, the two files the scans read; a new file that builds one
            // joins the scan before it ships.
            var root = TestPackage.Root();
            Assert.IsNotNull(root, "the package was not found");
            var builders = new[] { "Runtime", "Editor" }
                .Select(d => Path.Combine(root, d)).Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"new OWRequest\b|" + Calls))
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(new[] { "OWClient.cs", "OWJoin.cs" }, builders);
        }

        // -- The wire ---------------------------------------------------------------------------------------

        private const string Base = "https://example.test/api/v2";
        private const string Id = "abc/hidden?x=1#y";
        private const string Esc = "abc%2Fhidden%3Fx%3D1%23y";

        private static OWClient Make(FakeTransport t)
            => new OWClient(new OWClientConfig { ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = Base });

        private static IEnumerable<TestCaseData> Cases()
        {
            TestCaseData Case(string name, Func<OWClient, Task> call, string want)
                => new TestCaseData(call, want).SetName("Wire_TheCallersStringIsOneSegment_" + name);

            var x = new JObject { ["name"] = "x" };
            yield return Case("Get", c => c.GetAsync<JObject>("character", Id), $"/character/{Esc}/");
            yield return Case("Upsert", c => c.UpsertAsync<JObject>("character", Id, x), $"/character/{Esc}/");
            yield return Case("Patch", c => c.PatchAsync<JObject>("character", Id, x), $"/character/{Esc}/");
            yield return Case("Delete", c => c.DeleteAsync("character", Id), $"/character/{Esc}/");
            yield return Case("EditLinks", c => c.EditLinksAsync<JObject>("character", Id, "friends/x", add: new string[0]),
                $"/character/{Esc}/links/friends%2Fx");
            yield return Case("Adopt", c => c.AdoptAsync<JObject>("character", Id, "owner"), $"/character/{Esc}/adopt");
            yield return Case("Hide", c => c.HideAsync("character", Id), $"/character/{Esc}/hidden");
            yield return Case("Unhide", c => c.UnhideAsync("character", Id), $"/character/{Esc}/hidden");
            yield return Case("Draught", c => c.DraughtAsync(Id), $"/character/{Esc}/context");
            yield return Case("ListType", c => c.ListAsync<JObject>("character/x"), "/character%2Fx/");
            yield return Case("GetType", c => c.GetAsync<JObject>("character/x", "y"), "/character%2Fx/y/");
            yield return Case("CreateType", c => c.CreateAsync<JObject>("character/x", x), "/character%2Fx/");
            yield return Case("ListFilesType", c => c.ListFilesAsync("character/x", "y"), "/character%2Fx/y/files");
            yield return Case("ListProposalsType", c => c.ListProposalsAsync("character/x", "y"), "/character%2Fx/y/proposals");
            yield return Case("ReviewType", c => c.ReviewNewElementAsync("character/x", "y", OWReviewed.Decline),
                "/character%2Fx/y/review");
        }

        [TestCaseSource(nameof(Cases))]
        public async Task Wire(Func<OWClient, Task> call, string want)
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""name"":""x""}" };
            try
            {
                await call(Make(t));
            }
            catch (Exception)
            {
                // The fake's body need not satisfy the reader; the wire is the claim.
            }

            Assert.GreaterOrEqual(t.Calls.Count, 1, "nothing was sent");
            var url = t.Calls[0].Url;
            StringAssert.StartsWith(Base, url);
            var path = url.Substring(Base.Length);
            var q = path.IndexOf('?');
            Assert.AreEqual(want, q < 0 ? path : path.Substring(0, q));
        }
    }
}
