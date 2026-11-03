using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The Open with menu's rows: <see cref="OWOpeners.For(IReadOnlyList{OWTool}, string, JObject, OWOpenerSurface)"/> on
    /// every case of the shared fixture (<c>Tests/Editor/Fixtures/openers/openers.fixture.json</c>, the same bytes as the
    /// TypeScript SDK's <c>test/fixtures/openers.fixture.json</c> and the Python package's
    /// <c>tests/fixtures/openers.fixture.json</c>), every expected row compared whole, each refusal by the address
    /// refusal's reason or by the key its message names and its kind (an unknown key, worded "unknown key 'k'", or a
    /// refused value). Then <see cref="OWTools.All"/>, the baked tools list, through
    /// the same function. When the fixture moves upstream, re-vendor it byte for byte and update the hash and the count.
    /// </summary>
    public class OWOpenersTests
    {
        /// <summary>The fixture's sha256, as both packages' suites hold it.</summary>
        private const string FixtureSha256 = "7c84e4f624fb7ecc7eca1e56b1d9807196a5055e359e0a1c26f2065c9808e0d5";

        private const int CaseCount = 100;

        private const string World = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e0f";
        private const string Element = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e1a";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "openers", "openers.fixture.json");
            }
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = FixturePath;
                return path == null || !File.Exists(path) ? null : _fixture = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        /// <summary>One case per fixture case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("Openers_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Openers_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheSharedBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored openers fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(FixtureSha256, hash,
                "the vendored fixture is not the packages' bytes: re-vendor it (a checkout's line endings change the bytes)");
        }

        [Test]
        public void EveryCase_IsAnswered_AndNoneIsDropped()
        {
            Assert.IsNotNull(Fixture, "the vendored openers fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(CaseCount, cases.Count, $"the fixture holds {CaseCount} cases");
            Assert.AreEqual(CaseCount, cases.Select(c => (string)c["name"]).Distinct().Count(), "every case's name is its own");
            var wrong = new List<string>();
            var answered = 0;
            foreach (var c in cases)
            {
                var got = Answer(Fixture, c);
                var want = c["refuse"] != null ? new JObject { ["refuse"] = c["refuse"].DeepClone() } : new JObject { ["expect"] = c["expect"].DeepClone() };
                if (!JToken.DeepEquals(want, got))
                    wrong.Add($"{c["name"]}: gave {got.ToString(Newtonsoft.Json.Formatting.None)}, want {want.ToString(Newtonsoft.Json.Formatting.None)}");
                answered++;
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong));
            Assert.AreEqual(CaseCount, answered);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_GivesTheFixturesRows(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored openers fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            Assert.IsTrue(Fixture["lists"][(string)c["tools"]] is JArray, $"{name}: no list {c["tools"]}");
            var got = Answer(Fixture, c);
            var want = c["refuse"] != null ? new JObject { ["refuse"] = c["refuse"].DeepClone() } : new JObject { ["expect"] = c["expect"].DeepClone() };
            Assert.IsTrue(JToken.DeepEquals(want, got),
                $"{name}: gave {got.ToString(Newtonsoft.Json.Formatting.None)}, the fixture says {want.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        /// <summary>One case's answer in the fixture's own shape: <c>{ expect }</c> or <c>{ refuse }</c>.</summary>
        private static JObject Answer(JObject fixture, JObject c)
        {
            var refuse = c["refuse"] as JObject;
            try
            {
                var tools = OWTool.FromJson((JArray)fixture["lists"][(string)c["tools"]]);
                var surface = OWOpenerSurface.FromJson((JObject)c["surface"]);
                // The fixture's options, when present, are the fifth argument as written ({ element }).
                var rows = c["options"] is JObject options
                    ? OWOpeners.For(tools, (string)c["kind"], (JObject)c["address"], surface, OWOpenersOptions.FromJson(options))
                    : OWOpeners.For(tools, (string)c["kind"], (JObject)c["address"], surface);
                return new JObject { ["expect"] = new JArray(rows.Select(Row)) };
            }
            catch (OWAddressException e)
            {
                return new JObject { ["refuse"] = new JObject { ["error"] = "address", ["reason"] = e.Reason } };
            }
            catch (ArgumentException e) when (refuse != null && (string)refuse["error"] == "type")
            {
                // The fixture's TypeError: an ArgumentException whose message names the key, and its kind. This
                // package's own words for a key the object does not take; any other refusal is of a value.
                var names = (string)refuse["names"];
                var kind = e.Message.Contains($"unknown key '{names}'") ? "unknown_key" : "bad_value";
                return new JObject
                {
                    ["refuse"] = e.Message.Contains(names)
                        ? new JObject { ["error"] = "type", ["names"] = names, ["kind"] = kind }
                        : new JObject { ["error"] = "type", ["message"] = e.Message }
                };
            }
            catch (Exception e)
            {
                return new JObject { ["threw"] = e.GetType().Name + ": " + e.Message };
            }
        }

        private static JObject Row(OWOpener r) => new JObject
        {
            ["id"] = r.Id,
            ["name"] = r.Name,
            ["href"] = r.Href,
            ["state"] = r.State == null ? JValue.CreateNull() : new JValue(r.State),
        };

        // -- Beside the fixture -------------------------------------------------------------------

        [Test]
        public void AReadingFromParseLink_AndTheParts_GiveTheSameRowsAsTheJsonAddress()
        {
            var link = $"https://www.onlyworlds.com/w/{World}/character/{Element}@3?lens={Element}";
            var surface = new OWOpenerSurface { Audience = OWOpenerSurface.Member, SignedIn = false };
            var plain = new JObject { ["form"] = "text", ["world"] = World, ["type"] = "character", ["id"] = Element };
            var want = OWOpeners.For(OWTools.All, OWOpeners.Element, plain, surface).Select(Row).ToList();
            var read = OWOpeners.For(OWTools.All, OWOpeners.Element, OWAddress.ParseLink(link), surface).Select(Row).ToList();
            var parts = OWOpeners.For(OWTools.All, OWOpeners.Element, World, "character", Element, surface).Select(Row).ToList();
            Assert.IsTrue(JToken.DeepEquals(new JArray(want), new JArray(read)));
            Assert.IsTrue(JToken.DeepEquals(new JArray(want), new JArray(parts)));
        }

        [Test]
        public void AReadingBySlug_IsRefusedAsBadWorld()
        {
            var reading = OWAddress.ParseLink("https://www.onlyworlds.com/w/old-harbour");
            Assert.IsTrue(reading.WorldIsSlug);
            var e = Assert.Throws<OWAddressException>(() =>
                OWOpeners.For(OWTools.All, OWOpeners.World, reading, new OWOpenerSurface { Audience = OWOpenerSurface.Member }));
            Assert.AreEqual(OWAddressRefusal.BadWorld, e.Reason);
        }

        [Test]
        public void TheBakedList_GivesTheSitesRow_ThenEachListedToolWithADoor_InTheListsOrder()
        {
            Assert.GreaterOrEqual(OWTools.All.Count, 1);
            var member = OWOpeners.For(OWTools.All, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member, SignedIn = true });
            var offered = OWTools.All.Where(t => t.Rung == OWTool.Listed && t.Open?.World != null).ToList();
            CollectionAssert.AreEqual(new[] { OWOpeners.SiteId }.Concat(offered.Select(t => t.Id)).ToList(), member.Select(r => r.Id).ToList());
            Assert.AreEqual($"https://www.onlyworlds.com/w/{World}", member[0].Href);
            foreach (var r in member.Skip(1))
                Assert.IsTrue(r.Href.StartsWith("https://", StringComparison.Ordinal) && r.Href.Contains(World) && !r.Href.Contains("{"), r.Href);
            var open = OWOpeners.For(OWTools.All, OWOpeners.World, World, null, null, new OWOpenerSurface { Audience = OWOpenerSurface.Public });
            // The public: a tool that needs the device is not there; one that needs an account says so.
            CollectionAssert.AreEqual(new[] { OWOpeners.SiteId }.Concat(offered.Where(t => t.Open.Needs != OWToolOpen.Device).Select(t => t.Id)).ToList(),
                open.Select(r => r.Id).ToList());
            foreach (var r in open.Skip(1))
                Assert.AreEqual(offered.Single(t => t.Id == r.Id).Open.Needs == OWToolOpen.Account ? OWOpenerState.NeedsAnAccount : null, r.State, r.Id);
        }

        [Test]
        public void TheBakedList_HasLittleLens_AndTheMenuOffersItToAnyoneForAWorldAndForAnElement()
        {
            var lens = OWTools.All.SingleOrDefault(t => t.Id == "little-lens");
            Assert.IsNotNull(lens, "Little Lens is a listed row of the baked list");
            Assert.AreEqual(OWTool.Listed, lens.Rung);
            Assert.IsNull(lens.Open.Needs, "it needs no account");
            var world = OWOpeners.For(OWTools.All, OWOpeners.World, World, null, null, new OWOpenerSurface { Audience = OWOpenerSurface.Public });
            Assert.AreEqual($"https://little-lens.onlyworlds.com/#/open/{World}", world.Single(r => r.Id == "little-lens").Href);
            var element = OWOpeners.For(OWTools.All, OWOpeners.Element, World, "character", Element, new OWOpenerSurface { Audience = OWOpenerSurface.Public });
            Assert.AreEqual($"https://little-lens.onlyworlds.com/#/open/{World}/element/{Element}", element.Single(r => r.Id == "little-lens").Href);
        }

        [Test]
        public void TheBakedList_IsTheListAtThePinnedCommit()
        {
            Assert.AreEqual(40, OWTools.Commit.Length);
            Assert.AreEqual(64, OWTools.Sha256.Length);
            Assert.AreEqual("https://github.com/OnlyWorlds/tools", OWTools.Repository);
            CollectionAssert.AllItemsAreUnique(OWTools.All.Select(t => t.Id).ToList());
        }

        [Test]
        public void AGame_OutsideABrowser_SaysOpensInYourBrowser_OnAnAccountTool()
        {
            var tools = new[] { new OWTool("zephyr", "Zephyr", "https://zephyr.example", "zephyr", OWTool.Listed,
                new OWToolOpen("https://zephyr.example/{world}", null, OWToolOpen.Account)) };
            var rows = OWOpeners.For(tools, OWOpeners.Element, World, "character", Element,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member, SignedIn = false, Browser = false });
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(OWOpenerState.OpensInYourBrowser, rows[1].State);
            Assert.AreEqual($"https://zephyr.example/{World}", rows[1].Href);
        }

        [TestCase("\"no\"")]
        [TestCase("null")]
        [TestCase("0")]
        [TestCase("1")]
        [TestCase("\"false\"")]
        public void AnOnlineThatIsNotABoolean_IsRefusedByName(string value)
        {
            var e = Assert.Throws<ArgumentException>(() =>
                OWOpenerSurface.FromJson(JObject.Parse("{\"audience\": \"member\", \"online\": " + value + "}")));
            StringAssert.Contains("surface online is a boolean", e.Message);
        }

        [Test]
        public void Offline_TheBakedList_KeepsEveryHref_AndOnlineIsTheDefault()
        {
            var on = new OWOpenerSurface { Audience = OWOpenerSurface.Member, SignedIn = true };
            Assert.IsTrue(on.Online);
            var online = OWOpeners.For(OWTools.All, OWOpeners.World, World, null, null, on).Select(Row).ToList();
            var offline = OWOpeners.For(OWTools.All, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member, SignedIn = true, Online = false });
            var fromJson = OWOpeners.For(OWTools.All, OWOpeners.World, World, null, null,
                OWOpenerSurface.FromJson(JObject.Parse("{\"audience\": \"member\", \"signed_in\": true, \"online\": true}")));
            Assert.IsTrue(JToken.DeepEquals(new JArray(online), new JArray(fromJson.Select(Row))));
            CollectionAssert.AreEqual(online.Select(r => (string)r["href"]).ToList(), offline.Select(r => r.Href).ToList());
            foreach (var r in offline)
            {
                var web = r.Href.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || r.Href.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
                Assert.AreEqual(web ? OWOpenerState.NeedsAConnection : null, r.State, r.Href);
            }
        }

        [Test]
        public void Offline_TheSchemeIsReadInAnyCase_AndAnAppLinkIsNotAWebAddress()
        {
            var tools = new[]
            {
                new OWTool("loud", "Loud", "https://loud.example", null, OWTool.Listed, new OWToolOpen("HTTPS://loud.example/{world}", null, null)),
                new OWTool("plain", "Plain", "https://plain.example", null, OWTool.Listed, new OWToolOpen("Http://plain.example/{world}", null, null)),
                new OWTool("app", "App", "https://app.example", null, OWTool.Listed, new OWToolOpen("app://open/{world}", null, null)),
            };
            var rows = OWOpeners.For(tools, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member, Online = false });
            CollectionAssert.AreEqual(
                new[] { OWOpenerState.NeedsAConnection, OWOpenerState.NeedsAConnection, OWOpenerState.NeedsAConnection, null },
                rows.Select(r => r.State).ToList());
        }

        [Test]
        public void ALinkWithNoScheme_IsRefused_ARowBuiltInCodeToo()
        {
            // The fixture's rows come through OWTool.FromJson; a row built in code is held to the same rule.
            var tools = new[] { new OWTool("bare", "Bare", "https://bare.example", null, OWTool.Listed, new OWToolOpen("//bare.example/{world}", null, null)) };
            var e = Assert.Throws<ArgumentException>(() => OWOpeners.For(tools, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member }));
            StringAssert.Contains("tool bare: open.world begins with its scheme", e.Message);
        }

        // -- element_skips beside the fixture -----------------------------------------------------------

        [Test]
        public void TheBakedList_LittleLensSkipsRoomHiddenProposedAndMapSpace_AtlasAndLanternNone()
        {
            CollectionAssert.AreEqual(new[] { "room", "hidden", "proposed", "map_space" }, OWTools.All.Single(t => t.Id == "little-lens").Open.ElementSkips);
            Assert.IsEmpty(OWTools.All.Single(t => t.Id == "atlas").Open.ElementSkips);
            Assert.IsEmpty(OWTools.All.Single(t => t.Id == "lantern").Open.ElementSkips);
            var pin = new JObject { ["id"] = Element, ["type"] = "pin", ["name"] = "A pin" };
            var rows = OWOpeners.For(OWTools.All, OWOpeners.Element, World, "pin", Element, new OWOpenerSurface { Audience = OWOpenerSurface.Member },
                new OWOpenersOptions { Element = pin });
            CollectionAssert.AreEqual(new[] { OWOpeners.SiteId, "atlas", "lantern" }, rows.Select(r => r.Id).ToList());
        }

        [Test]
        public void ASkipOutsideTheWords_IsRefused_ARowBuiltInCodeToo_EvenWithoutAnElement()
        {
            var tools = new[] { new OWTool("thorn", "Thorn", "https://thorn.example", null, OWTool.Listed,
                new OWToolOpen("https://thorn.example/{world}", "https://thorn.example/{world}/{id}", null, new[] { "private" })) };
            var e = Assert.Throws<ArgumentException>(() => OWOpeners.For(tools, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member }));
            StringAssert.Contains("tool thorn: open.element_skips", e.Message);
        }

        [TestCase("{\"world\": null, \"element\": null, \"needs\": null, \"element_skips\": \"hidden\"}")]
        [TestCase("{\"world\": null, \"element\": null, \"needs\": null, \"element_skips\": [7]}")]
        public void ASkipsListThatIsNotAListOfWords_IsRefusedByName(string open)
        {
            var row = JObject.Parse("{\"id\": \"x\", \"name\": \"X\", \"open\": " + open + "}");
            var e = Assert.Throws<ArgumentException>(() => OWTool.FromJson(row));
            StringAssert.Contains("open.element_skips", e.Message);
        }

        // -- element_types beside the fixture -----------------------------------------------------------

        [Test]
        public void ARowWithoutTheTypesKey_OpensEveryType_AsARowBuiltWithoutThem()
        {
            // A list older than the key has none: null, every type, both from JSON and from the four-argument constructor.
            var row = OWTool.FromJson(JObject.Parse("{\"id\": \"wren\", \"name\": \"Wren\", \"rung\": \"listed\", \"open\": {\"world\": null, \"element\": \"https://wren.example/{world}/{id}\", \"needs\": null}}"));
            Assert.IsNull(row.Open.ElementTypes);
            Assert.IsNull(new OWToolOpen(null, "https://wren.example/{world}/{id}", null, null).ElementTypes);
            var rows = OWOpeners.For(new[] { row }, OWOpeners.Element, World, "location", Element, new OWOpenerSurface { Audience = OWOpenerSurface.Member });
            CollectionAssert.AreEqual(new[] { OWOpeners.SiteId, "wren" }, rows.Select(r => r.Id).ToList());
        }

        [TestCase("[\"character\", \"character\"]")]
        [TestCase("[\"Character\"]")]
        public void ARepeatOrAWordInAnotherCase_IsRefusedByName_EvenOnAWorldsMenu(string types)
        {
            var row = OWTool.FromJson(JObject.Parse("{\"id\": \"lark\", \"name\": \"Lark\", \"rung\": \"listed\", \"open\": {\"world\": null, \"element\": \"https://lark.example/{world}/{id}\", \"needs\": null, \"element_types\": " + types + "}}"));
            var e = Assert.Throws<ArgumentException>(() => OWOpeners.For(new[] { row }, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member }));
            StringAssert.Contains("tool lark: open.element_types", e.Message);
        }

        [Test]
        public void ATypesListBuiltInCodeWithANull_IsRefused()
        {
            var tools = new[] { new OWTool("lark", "Lark", "https://lark.example", null, OWTool.Listed,
                new OWToolOpen(null, "https://lark.example/{world}/{id}", null, null, new string[] { null })) };
            var e = Assert.Throws<ArgumentException>(() => OWOpeners.For(tools, OWOpeners.Element, World, "character", Element,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member }));
            StringAssert.Contains("tool lark: open.element_types", e.Message);
        }

        [TestCase("\"character\"")]
        [TestCase("[7]")]
        [TestCase("{}")]
        public void ATypesValueThatIsNotAListOfWords_IsRefusedByName(string types)
        {
            var row = JObject.Parse("{\"id\": \"x\", \"name\": \"X\", \"open\": {\"world\": null, \"element\": \"https://x.example/{id}\", \"needs\": null, \"element_types\": " + types + "}}");
            var e = Assert.Throws<ArgumentException>(() => OWTool.FromJson(row));
            StringAssert.Contains("open.element_types", e.Message);
            StringAssert.DoesNotContain("unknown key", e.Message);
        }

        /// <summary>
        /// The menu reads an element's marks as <see cref="OWGraph.Read"/> does: on every element of the lens fixture's
        /// worlds (<c>Fixtures/lens/lens.fixture.json</c>), a door that skips one word is left out exactly when the graph
        /// reader, asked about that element alone, says it carries that mark: room, hidden and proposed from a node's marks
        /// with every flag on, map space from the cut with that one flag off, a reading aid from the one kind no flag brings
        /// back. An element under the world's hidden list carries <c>hidden: true</c>, as the owner is served it. The
        /// TypeScript SDK's <c>openers-marks.test.mjs</c>, ported.
        /// </summary>
        [Test]
        public void EverySkipWord_LeavesADoorOut_ExactlyWhenTheGraphReadsThatMark()
        {
            var root = TestPackage.Root();
            var path = root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "lens", "lens.fixture.json");
            Assert.IsTrue(path != null && File.Exists(path), "the vendored lens fixture was not found: " + path);
            var lens = OWJson.ParseObject(File.ReadAllText(path));
            // A node's family and icon are the presentation sidecar's; outside the Editor Resources loads nothing, so the
            // package's own sidecar is read from its text and put back after, as OWGraphTests does.
            string family = null;
            try
            {
                family = OWPresentation.FamilyOf("character");
            }
            catch (Exception)
            {
                // No engine behind Resources; read the text below.
            }

            var fromText = family == null;
            if (fromText)
            {
                try
                {
                    OWPresentation.LoadText(File.ReadAllText(Path.Combine(root, "Runtime", "Resources", "ow-presentation.json")));
                }
                catch (Exception)
                {
                    // The colours need the engine's parser; the type table is read before them.
                }
            }

            try
            {
                MarksAgreeWithTheGraph(lens);
            }
            finally
            {
                if (fromText) OWPresentation.Reload();
            }
        }

        private static void MarksAgreeWithTheGraph(JObject lens)
        {
            string DoorId(string word) => "skip-" + word.Replace('_', '-');
            var tools = OWOpeners.ElementMarks.Select(w => new OWTool(DoorId(w), w, "https://x.example", null, OWTool.Listed,
                new OWToolOpen("https://x.example/#/w/{world}", "https://x.example/#/w/{world}/e/{id}", null, new[] { w }))).ToList();
            var all = new OWGraphOptions { Rooms = true, Hidden = true, Proposed = true, MapSpace = true };
            var noMap = new OWGraphOptions { Rooms = true, Hidden = true, Proposed = true, MapSpace = false };

            var n = 0;
            var seen = new HashSet<string>();
            var wrong = new List<string>();
            foreach (var w in ((JObject)lens["worlds"]).Properties().Select(p => (JObject)p.Value))
            {
                var hidden = new HashSet<string>((w["hidden"] as JArray ?? new JArray()).Select(t => (string)t));
                foreach (var raw in ((JArray)w["elements"]).Cast<JObject>())
                {
                    var el = (JObject)raw.DeepClone();
                    if (hidden.Contains((string)el["id"])) el["hidden"] = true;
                    var one = new JArray(el);
                    var full = OWGraph.Read(one, OWLensReader.Owner(), all);
                    var want = new HashSet<string>(full.Nodes.Count == 1 ? full.Nodes[0].Marks : Array.Empty<string>());
                    var lessMap = OWGraph.Read(one, OWLensReader.Owner(), noMap);
                    if (full.Nodes.Count == 1 && lessMap.Nodes.Count == 0 && lessMap.Cut.TryGetValue(OWGraphCut.MapSpace, out var m) && m == 1)
                        want.Add(OWGraphCut.MapSpace);
                    if (full.Nodes.Count == 0 && full.Cut.TryGetValue(OWGraphCut.ReadingAid, out var a) && a == 1)
                        want.Add(OWGraphCut.ReadingAid);
                    seen.UnionWith(want);

                    var address = new JObject { ["form"] = "human", ["world"] = (string)w["id"], ["type"] = ((string)el["type"]).ToLowerInvariant(), ["id"] = (string)el["id"] };
                    var rows = OWOpeners.For(tools, OWOpeners.Element, address, new OWOpenerSurface { Audience = OWOpenerSurface.Member },
                        new OWOpenersOptions { Element = el });
                    foreach (var word in OWOpeners.ElementMarks)
                    {
                        var gone = rows.All(r => r.Id != DoorId(word));
                        if (gone != want.Contains(word))
                            wrong.Add($"{w["name"]} {el["type"]} {el["name"] ?? el["id"]}: {word} {(gone ? "skipped" : "kept")}, the graph {(want.Contains(word) ? "marks it" : "does not")}");
                    }

                    n++;
                }
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong));
            Assert.GreaterOrEqual(n, 100, $"read {n} elements");
            // Every word met at least once, so no check passes for want of a case.
            CollectionAssert.AreEquivalent(OWOpeners.ElementMarks, seen);
        }
    }
}
