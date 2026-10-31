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
    /// refusal's reason or by the key its message names. Then <see cref="OWTools.All"/>, the baked tools list, through
    /// the same function. When the fixture moves upstream, re-vendor it byte for byte and update the hash and the count.
    /// </summary>
    public class OWOpenersTests
    {
        /// <summary>The fixture's sha256, as both packages' suites hold it.</summary>
        private const string FixtureSha256 = "351ddec25f3af8cf8ec2c9c901d6ed4719355286467ada091e45d6012f8ed05b";

        private const int CaseCount = 57;

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
                var rows = OWOpeners.For(tools, (string)c["kind"], (JObject)c["address"], surface);
                return new JObject { ["expect"] = new JArray(rows.Select(Row)) };
            }
            catch (OWAddressException e)
            {
                return new JObject { ["refuse"] = new JObject { ["error"] = "address", ["reason"] = e.Reason } };
            }
            catch (ArgumentException e) when (refuse != null && (string)refuse["error"] == "type")
            {
                // The fixture's TypeError: an ArgumentException whose message names the key.
                var names = (string)refuse["names"];
                return new JObject { ["refuse"] = e.Message.Contains(names) ? refuse.DeepClone() : new JObject { ["error"] = "type", ["message"] = e.Message } };
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
            CollectionAssert.AreEqual(new[] { OWOpeners.SiteId }.Concat(offered.Where(t => t.Open.Needs == null).Select(t => t.Id)).ToList(),
                open.Select(r => r.Id).ToList());
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
        public void Offline_TheSchemeIsReadInAnyCase_AndALinkWithNoSchemeIsNotAWebAddress()
        {
            var tools = new[]
            {
                new OWTool("loud", "Loud", "https://loud.example", null, OWTool.Listed, new OWToolOpen("HTTPS://loud.example/{world}", null, null)),
                new OWTool("plain", "Plain", "https://plain.example", null, OWTool.Listed, new OWToolOpen("Http://plain.example/{world}", null, null)),
                new OWTool("bare", "Bare", "https://bare.example", null, OWTool.Listed, new OWToolOpen("//bare.example/{world}", null, null)),
                new OWTool("app", "App", "https://app.example", null, OWTool.Listed, new OWToolOpen("app://open/{world}", null, null)),
            };
            var rows = OWOpeners.For(tools, OWOpeners.World, World, null, null,
                new OWOpenerSurface { Audience = OWOpenerSurface.Member, Online = false });
            CollectionAssert.AreEqual(
                new[] { OWOpenerState.NeedsAConnection, OWOpenerState.NeedsAConnection, OWOpenerState.NeedsAConnection, null, null },
                rows.Select(r => r.State).ToList());
        }
    }
}
