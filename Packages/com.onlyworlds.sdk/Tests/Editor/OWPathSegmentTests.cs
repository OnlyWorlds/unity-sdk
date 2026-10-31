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
    /// in a request path goes through <c>Seg</c>, which refuses a value that is not one segment before anything is sent
    /// (a type a lower-case word, anything else letters, digits, <c>_ . -</c>, not starting with a dot or dash) and
    /// escapes what passes. keel decodes an escaped slash before it routes (<c>/character/abc%2Fhidden/</c> answers as
    /// <c>/character/abc/hidden/</c> does), so the escape alone cannot hold one. Parity with the TypeScript SDK 4.123.3
    /// (its <c>test/path-segments.test.mjs</c>, ported). Two halves: the source scan holds every path the client builds,
    /// so a new route that skips <c>Seg</c>, or builds its path some way the scan cannot read, fails here; the calls show
    /// each refusal sends nothing, and a value that is one segment goes out as given.
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
        // An interpolated string on one line, its holes read whole (a hole may quote: Seg("id", id)).
        private const string Interp = "\\$\"(?:[^\"{}\\n]|\\{[^{}\\n]*\\})*\"";
        private const string Calls = @"\b(?:RequestAsync|RequestWithResponseAsync)<[\w<>, ]+>\(";

        // A path helper: `private static string XPath(string type, string id) => $"...";`. Found by shape, so a new
        // helper of the same shape joins the scan; any other helper is a path the scan cannot read.
        private static readonly Regex HelperDecl = new Regex(
            @"private static string (\w+Path)\(string type, string id\)\s*=>\s*(" + Interp + @")\s*;");

        private static readonly Regex Hole = new Regex(@"\{([^{}]*)\}");

        [Test]
        public void Source_EveryValueInARequestPath_GoesThroughSeg_AndEveryPathIsOneTheScanReads()
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
                    // Seg("id", id): the checking helper, its name the variable's, so the refusal names what the
                    // caller passed. A bare escape is not enough: keel decodes %2F before it routes.
                    var seg = Regex.Match(e, @"^Seg\(""(\w+)"", (\w+)\)$");
                    if (seg.Success && seg.Groups[1].Value == seg.Groups[2].Value) continue;
                    if (Regex.IsMatch(e, "^" + helperCall + "$")) continue;
                    raw.Add($"{{{e}}} in {t}");
                }
            }

            CollectionAssert.IsEmpty(raw, "a segment in a request path that does not go through Seg:\n" + string.Join("\n", raw));

            // Seg checks before it escapes: a type a lower-case word, anything else one segment, both anchored at the
            // string's very end (\z: a .NET $ also matches before a trailing newline).
            var body = Regex.Match(src, @"private static string Seg\(string name, string value\)\s*\{(.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(body.Success, "the Seg helper was not found");
            StringAssert.Contains("Uri.EscapeDataString(value)", body.Groups[1].Value, "Seg escapes what passes");
            StringAssert.Contains(@"@""\A[a-z][a-z0-9_]*\z""", src, "the type's pattern");
            StringAssert.Contains(@"@""\A[A-Za-z0-9][A-Za-z0-9_.-]*\z""", src, "the value's pattern");
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
        private const string Uuid = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e0f";
        private const string Id = Uuid + "/hidden";

        private static OWClient Make(FakeTransport t, bool account = false)
            => new OWClient(account
                ? new OWClientConfig { ApiKey = "ow_a_acct", Transport = t, BaseUrl = Base }
                : new OWClientConfig { ApiKey = "ow_w_test", ApiPin = "1234", Transport = t, BaseUrl = Base });

        /// <summary>
        /// The TypeScript SDK's 16 refusals (its <c>test/path-segments.test.mjs</c>), then this client's other routes
        /// with a value in the path (files, proposals, review, create, the account routes), then a trailing newline,
        /// which a .NET <c>$</c> would let through.
        /// </summary>
        private static IEnumerable<TestCaseData> Refusals()
        {
            TestCaseData Case(string name, Func<OWClient, Task> call, string what, bool account = false)
                => new TestCaseData(call, what, account).SetName("Refused_NotOneSegment_NothingSent_" + name);

            var x = new JObject { ["name"] = "x" };
            yield return Case("Get", c => c.GetAsync<JObject>("character", Id), "id");
            yield return Case("Upsert", c => c.UpsertAsync<JObject>("character", Id, x), "id");
            yield return Case("Patch", c => c.PatchAsync<JObject>("character", Id, x), "id");
            yield return Case("Delete", c => c.DeleteAsync("character", Id), "id");
            yield return Case("EditLinksId", c => c.EditLinksAsync<JObject>("character", Id, "friends", add: new string[0]), "id");
            yield return Case("EditLinksField", c => c.EditLinksAsync<JObject>("character", Uuid, "friends/x", add: new string[0]), "field");
            yield return Case("Adopt", c => c.AdoptAsync<JObject>("character", Id, "owner"), "id");
            yield return Case("Hide", c => c.HideAsync("character", Id), "id");
            yield return Case("Unhide", c => c.UnhideAsync("character", Id), "id");
            yield return Case("Draught", c => c.DraughtAsync(Id), "characterId");
            yield return Case("ListType", c => c.ListAsync<JObject>("character/x"), "type");
            yield return Case("GetTypeCapitalised", c => c.GetAsync<JObject>("Character", Uuid), "type");
            yield return Case("GetEscapedSlash", c => c.GetAsync<JObject>("character", Uuid + "%2Fhidden"), "id");
            yield return Case("GetDotSegment", c => c.GetAsync<JObject>("character", ".."), "id");
            yield return Case("GetEmpty", c => c.GetAsync<JObject>("character", ""), "id");
            yield return Case("RevokeAccountToken", c => c.RevokeAccountTokenAsync("t/../x"), "tokenId", true);

            yield return Case("CreateType", c => c.CreateAsync<JObject>("character/x", x), "type");
            yield return Case("ListFilesType", c => c.ListFilesAsync("character/x", Uuid), "type");
            yield return Case("FileDownloadLinkFileId", c => c.FileDownloadLinkAsync("character", Uuid, "f/1"), "fileId");
            yield return Case("RemoveFileFileId", c => c.RemoveFileAsync("character", Uuid, ".f"), "fileId");
            yield return Case("ListProposalsId", c => c.ListProposalsAsync("character", Id), "id");
            yield return Case("AcceptProposalPid", c => c.AcceptProposalAsync("character", Uuid, "p/1"), "pid");
            yield return Case("WithdrawProposalPid", c => c.WithdrawProposalAsync("character", Uuid, "-p"), "pid");
            yield return Case("ReviewType", c => c.ReviewNewElementAsync("character/x", Uuid, OWReviewed.Decline), "type");
            yield return Case("WorldLensesWorldId", c => c.WorldLensesAsync("w/1"), "worldId", true);
            yield return Case("RevokeWorldKeyKeyId", c => c.RevokeWorldKeyAsync("w1", "k 9"), "keyId", true);
            yield return Case("RevokeWorldLinkWorldId", c => c.RevokeWorldLinkAsync("w/1", "l2"), "worldId", true);
            yield return Case("CreateSeatWorldId", c => c.CreateSeatAsync("w/1", "Scout"), "worldId", true);
            yield return Case("RekeySeatSeatId", c => c.RekeySeatAsync("w1", "s/1"), "seatId", true);
            yield return Case("SetMemberLensesMemberId", c => c.SetMemberLensesAsync("w1", "m/1", new[] { "l1" }), "memberId", true);
            yield return Case("GetTrailingNewline", c => c.GetAsync<JObject>("character", Uuid + "\n"), "id");
            yield return Case("GetIdAbcNewline", c => c.GetAsync<JObject>("character", "abc\n"), "id");
            yield return Case("GetTypeNewline", c => c.GetAsync<JObject>("character\n", Uuid), "type");
        }

        [TestCaseSource(nameof(Refusals))]
        public void Refused(Func<OWClient, Task> call, string what, bool account)
        {
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""name"":""x""}" };
            var c = Make(t, account);
            var e = Assert.ThrowsAsync<ArgumentException>(async () => await call(c));
            StringAssert.StartsWith(what + " ", e.Message, "the error names the value");
            StringAssert.Contains("is not one path segment. Nothing was sent.", e.Message);
            Assert.AreEqual(what, e.ParamName);
            Assert.AreEqual(0, t.Calls.Count, "something was sent");
        }

        [Test]
        public void TheRefusal_ShowsTheValueAsJson()
        {
            var t = new FakeTransport();
            var e = Assert.ThrowsAsync<ArgumentException>(async () => await Make(t).GetAsync<JObject>("character", "a\"b/c"));
            StringAssert.StartsWith("id \"a\\\"b/c\" is not one path segment. Nothing was sent.", e.Message);
            e = Assert.ThrowsAsync<ArgumentException>(async () => await Make(t).GetAsync<JObject>(null, Uuid));
            StringAssert.StartsWith("type null is not one path segment. Nothing was sent.", e.Message);
            Assert.AreEqual(0, t.Calls.Count, "something was sent");
        }

        [Test]
        public async Task AValueThatIsOneSegment_GoesOutAsGiven()
        {
            // A uuid, dots, dashes and underscores inside, and the link field.
            var t = new FakeTransport { ResponseBody = @"{""id"":""x"",""name"":""x""}" };
            var c = Make(t);
            foreach (var call in new Func<Task>[]
                     {
                         () => c.GetAsync<JObject>("character", Uuid),
                         () => c.GetAsync<JObject>("character", "a.b_c-D9"),
                         () => c.EditLinksAsync<JObject>("character", Uuid, "friends", add: new string[0]),
                     })
            {
                try
                {
                    await call();
                }
                catch (Exception)
                {
                    // The fake's body need not satisfy the reader; the wire is the claim.
                }
            }

            CollectionAssert.AreEqual(
                new[] { $"/character/{Uuid}/", "/character/a.b_c-D9/", $"/character/{Uuid}/links/friends" },
                t.Calls.Select(r => r.Url.Substring(Base.Length).Split('?')[0]).ToList());
        }
    }
}
