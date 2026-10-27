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
    /// The address profile's conformance fixture (schema-dist <c>profiles/address.fixture.json</c>, vendored byte for
    /// byte in <c>Tests/Editor/Fixtures/address/</c> from dist.77), read case by case through
    /// <see cref="OWAddress.ElementAddress(JObject)"/> (a format case) and <see cref="OWAddress.TryParseLink"/> (a parse
    /// case), each refusal by the reason the fixture names. The TypeScript SDK and the Python package replay the same
    /// cases through <c>elementAddress</c> and <c>parseOwLink</c>. When the dist moves the fixture, re-vendor it and
    /// update the hash below.
    /// </summary>
    public class OWAddressTests
    {
        /// <summary>The fixture's sha256 as dist.77's MANIFEST.json records it.</summary>
        private const string DistSha256 = "13b321c8244d3d4bce28e25e29b6d8c57b2450187be0cdfcd7380bea36c1ea72";

        private const string World = "0199c3a1-7b2e-7c3d-8e4f-5a6b7c8d9e0f";
        private const string Element = "019a1f3c-2b4e-7d10-8c3a-6f2e9b0d4a71";
        private const string Lens = "019a2b7e-4c1d-7f20-9b3e-1a2c3d4e5f60";

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "address", "address.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("Address_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Address_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored address fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.77's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist77sCases()
        {
            Assert.IsNotNull(Fixture, "the vendored address fixture was not found");
            Assert.AreEqual("address", (string)Fixture["profile"]);
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(133, cases.Count, "dist.77's fixture holds 133 cases");
            Assert.AreEqual(32, cases.Count(c => (string)c["kind"] == "format"));
            Assert.AreEqual(101, cases.Count(c => (string)c["kind"] == "parse"));
            Assert.AreEqual(70, cases.Count(c => c["refuse"] != null), "16 format refusals and 54 parse refusals");
        }

        [Test]
        public void TheTypeWords_AreTheGeneratedList_AndTheRefusalsAreTheFixtures()
        {
            Assert.IsNotNull(Fixture, "the vendored address fixture was not found");
            CollectionAssert.AreEquivalent(OWElementTypes.Slugs, Fixture["types"].Select(t => (string)t).ToArray());
            CollectionAssert.AreEqual(Fixture["refusals"].Select(t => (string)t).ToArray(), OWAddressRefusal.All,
                "the refusals, in the order a parser checks them");
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored address fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var refuse = (string)c["refuse"];

            switch ((string)c["kind"])
            {
                case "format":
                {
                    string written = null, refusal = null;
                    try
                    {
                        written = OWAddress.ElementAddress((JObject)c["address"]);
                    }
                    catch (OWAddressException e)
                    {
                        refusal = e.Reason;
                    }

                    if (refuse != null)
                        Assert.AreEqual(refuse, refusal, $"format/{name}: wrote {written ?? "nothing"}, the fixture refuses it as {refuse}");
                    else
                        Assert.AreEqual((string)c["expect"], written, $"format/{name}: refused as {refusal ?? "nothing"}");
                    break;
                }
                case "parse":
                {
                    var ok = OWAddress.TryParseLink((string)c["text"], out var reading, out var refusal);
                    if (refuse != null)
                    {
                        Assert.IsFalse(ok, $"parse/{name}: read {(reading == null ? "nothing" : Reading(reading).ToString(Newtonsoft.Json.Formatting.None))}, the fixture refuses it as {refuse}");
                        Assert.IsNull(reading);
                        Assert.AreEqual(refuse, refusal, $"parse/{name}");
                    }
                    else
                    {
                        Assert.IsTrue(ok, $"parse/{name}: refused as {refusal}");
                        Assert.IsNull(refusal);
                        var got = Reading(reading);
                        var want = (JObject)c["expect"];
                        Assert.IsTrue(JToken.DeepEquals(want, got),
                            $"parse/{name}: gave {got.ToString(Newtonsoft.Json.Formatting.None)}, the fixture says {want.ToString(Newtonsoft.Json.Formatting.None)}");
                    }
                    break;
                }
                default:
                    Assert.Fail($"case {name}: a case of unknown kind {c["kind"]}");
                    break;
            }
        }

        /// <summary>A reading as the fixture writes it: every key, null as JSON null.</summary>
        private static JObject Reading(OWAddressReading r) => new JObject
        {
            ["form"] = r.Form,
            ["world"] = Str(r.World),
            ["world_is_slug"] = r.WorldIsSlug,
            ["type"] = Str(r.Type),
            ["id"] = Str(r.Id),
            ["lens"] = Str(r.Lens),
            ["moment"] = r.Moment.HasValue ? new JValue((long)r.Moment.Value) : JValue.CreateNull(),
            ["canonical"] = r.Canonical,
        };

        private static JToken Str(string value) => value == null ? JValue.CreateNull() : new JValue(value);

        // -- Beside the fixture -------------------------------------------------------------------

        [Test]
        public void EveryReading_WritesBackCanonical_AndThatStringReadsCanonical()
        {
            Assert.IsNotNull(Fixture, "the vendored address fixture was not found");
            var read = 0;
            foreach (var c in Fixture["cases"].Cast<JObject>().Where(x => (string)x["kind"] == "parse" && x["expect"] != null))
            {
                var reading = OWAddress.ParseLink((string)c["text"]);
                Assert.IsNotNull(reading, (string)c["name"]);
                if (reading.WorldIsSlug)
                {
                    // A writer never writes a slug.
                    var e = Assert.Throws<OWAddressException>(() => OWAddress.ElementAddress(reading), (string)c["name"]);
                    Assert.AreEqual(OWAddressRefusal.BadWorld, e.Reason);
                    continue;
                }

                var written = OWAddress.ElementAddress(reading);
                if (reading.Canonical) Assert.AreEqual((string)c["text"], written, (string)c["name"]);
                var again = OWAddress.ParseLink(written);
                Assert.IsTrue(again.Canonical, (string)c["name"] + ": " + written);
                Assert.AreEqual(written, OWAddress.ElementAddress(again));
                read++;
            }

            Assert.Greater(read, 40);
        }

        [Test]
        public void TheTypedWriter_WritesTheThreeSpellings_AndAWorld()
        {
            Assert.AreEqual($"https://www.onlyworlds.com/w/{World}/character/{Element}",
                OWAddress.ElementAddress(OWAddress.Human, World, "character", Element));
            Assert.AreEqual($"ow://object/{Element}@-7?lens={Lens}",
                OWAddress.ElementAddress(OWAddress.Text, null, "object", Element, Lens, -7));
            Assert.AreEqual($"ow://{World}/location/{Element}@{int.MaxValue}",
                OWAddress.ElementAddress(OWAddress.Text, World, "location", Element, moment: int.MaxValue));
            Assert.AreEqual($"ow://{World}", OWAddress.ElementAddress(OWAddress.Text, World));
            Assert.AreEqual($"https://www.onlyworlds.com/w/{World}@0", OWAddress.ElementAddress(OWAddress.Human, World, moment: 0));

            Assert.AreEqual(OWAddressRefusal.NoWorld,
                Assert.Throws<OWAddressException>(() => OWAddress.ElementAddress(OWAddress.Human, null, "character", Element)).Reason);
            Assert.AreEqual(OWAddressRefusal.BadForm,
                Assert.Throws<OWAddressException>(() => OWAddress.ElementAddress("Human", World)).Reason);
            Assert.AreEqual(OWAddressRefusal.Segments,
                Assert.Throws<OWAddressException>(() => OWAddress.ElementAddress(OWAddress.Text, null, null, Element)).Reason);
        }

        [Test]
        public void ParseLink_IsNullForARefusal_AndAReadingSaysWhetherItIsAWorld()
        {
            Assert.IsNull(OWAddress.ParseLink(null));
            Assert.IsFalse(OWAddress.TryParseLink(null, out _, out var refusal));
            Assert.AreEqual(OWAddressRefusal.NotAnAddress, refusal);
            Assert.IsNull(OWAddress.ParseLink($"ow://dragon/{Element}"));

            var world = OWAddress.ParseLink($"ow://{World}");
            Assert.IsTrue(world.IsWorld);
            Assert.AreEqual(OWAddress.Text, world.Form);

            var element = OWAddress.ParseLink($"ow://object/{Element}");
            Assert.IsFalse(element.IsWorld);
            Assert.IsNull(element.World);
        }

        // -- Where the reference packages agree beyond the fixture ----------------------------------
        // The TypeScript SDK (4.89.1) and the Python package (0.73.1) answer these alike; the fixture does not hold them.

        private static string Refusal(string json)
            => Assert.Throws<OWAddressException>(() => OWAddress.ElementAddress(JObject.Parse(json)), json).Reason;

        [Test]
        public void TheWriter_RefusesAFormFirst_ThenSegments_ThenNoWorld()
        {
            // A type without an id and no world: segments before no_world.
            Assert.AreEqual(OWAddressRefusal.Segments, Refusal("{\"form\": \"human\", \"type\": \"pin\"}"));
            Assert.AreEqual(OWAddressRefusal.Segments, Refusal($"{{\"form\": \"human\", \"id\": \"{Element}\"}}"));
            Assert.AreEqual(OWAddressRefusal.Segments,
                Assert.Throws<OWAddressException>(() => OWAddress.ElementAddress(OWAddress.Human, null, "character")).Reason);

            // A form it does not know before either.
            Assert.AreEqual(OWAddressRefusal.BadForm, Refusal("{\"form\": \"url\", \"type\": \"pin\"}"));
            Assert.AreEqual(OWAddressRefusal.BadForm, Refusal("{\"type\": \"pin\"}"));

            // With a whole triple, or nothing, the missing world is what refuses.
            Assert.AreEqual(OWAddressRefusal.NoWorld, Refusal($"{{\"form\": \"human\", \"type\": \"pin\", \"id\": \"{Element}\"}}"));
            Assert.AreEqual(OWAddressRefusal.NoWorld, Refusal("{\"form\": \"text\"}"));

            // Then the reader's order: the world before the type, the type before the id.
            Assert.AreEqual(OWAddressRefusal.BadWorld, Refusal("{\"form\": \"text\", \"world\": \"hyperion\", \"type\": \"dragon\", \"id\": 7}"));
            Assert.AreEqual(OWAddressRefusal.UnknownType, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"type\": \"dragon\", \"id\": 7}}"));
        }

        [Test]
        public void AMomentWithAZeroFraction_IsAWholeStep()
        {
            Assert.AreEqual($"ow://character/{Element}@3",
                OWAddress.ElementAddress(JObject.Parse($"{{\"form\": \"text\", \"type\": \"character\", \"id\": \"{Element}\", \"moment\": 3.0}}")));
            Assert.AreEqual($"ow://{World}@0",
                OWAddress.ElementAddress(JObject.Parse($"{{\"form\": \"text\", \"world\": \"{World}\", \"moment\": -0.0}}")));
            Assert.AreEqual($"ow://{World}@-2147483648",
                OWAddress.ElementAddress(JObject.Parse($"{{\"form\": \"text\", \"world\": \"{World}\", \"moment\": -2147483648.0}}")));

            // A fraction, a whole number past the 32-bit range (as a float, or as an integer past 64 bits), refuses.
            Assert.AreEqual(OWAddressRefusal.BadMoment, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"moment\": 1.5}}"));
            Assert.AreEqual(OWAddressRefusal.BadMoment, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"moment\": 2147483648.0}}"));
            Assert.AreEqual(OWAddressRefusal.BadMoment, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"moment\": 1e20}}"));
            Assert.AreEqual(OWAddressRefusal.BadMoment,
                Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"moment\": 1000000000000000000000000000000}}"));

            // A refusal before the moment still names itself, whatever the moment holds.
            Assert.AreEqual(OWAddressRefusal.NoWorld, Refusal("{\"form\": \"human\", \"moment\": 1000000000000000000000000000000}"));
        }

        [Test]
        public void AnEmptyString_IsPresent_OnlyNullOrAMissingKeyIsAbsent()
        {
            Assert.AreEqual(OWAddressRefusal.BadWorld, Refusal($"{{\"form\": \"text\", \"world\": \"\", \"type\": \"character\", \"id\": \"{Element}\"}}"));
            Assert.AreEqual(OWAddressRefusal.BadWorld, Refusal("{\"form\": \"human\", \"world\": \"\"}"));
            Assert.AreEqual(OWAddressRefusal.BadLens, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"lens\": \"\"}}"));
            Assert.AreEqual(OWAddressRefusal.UnknownType, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"type\": \"\", \"id\": \"\"}}"));
            Assert.AreEqual(OWAddressRefusal.UnknownType, Refusal($"{{\"form\": \"text\", \"type\": \"\", \"id\": \"{Element}\"}}"));
            Assert.AreEqual(OWAddressRefusal.BadId, Refusal("{\"form\": \"text\", \"type\": \"character\", \"id\": \"\"}"));
            Assert.AreEqual(OWAddressRefusal.Segments, Refusal($"{{\"form\": \"text\", \"world\": \"{World}\", \"type\": \"\"}}"));

            // null and a missing key are the same absence.
            Assert.AreEqual($"ow://{World}",
                OWAddress.ElementAddress(JObject.Parse($"{{\"form\": \"text\", \"world\": \"{World}\", \"type\": null, \"id\": null, \"lens\": null, \"moment\": null}}")));
        }

        [Test]
        public void AKeyTheShapeDoesNotHold_IsAnArgumentError_NotARefusal()
        {
            foreach (var key in new[] { "wrold", "Type", "ID", "x" })
            {
                var json = $"{{\"form\": \"text\", \"world\": \"{World}\", \"{key}\": null}}";
                var e = Assert.Throws<ArgumentException>(() => OWAddress.ElementAddress(JObject.Parse(json)), json);
                StringAssert.Contains("'" + key + "'", e.Message);
            }

            // A reading's two flags are keys of the shape, and ignored.
            Assert.AreEqual($"ow://{World}",
                OWAddress.ElementAddress(JObject.Parse($"{{\"form\": \"text\", \"world\": \"{World}\", \"world_is_slug\": true, \"canonical\": \"no\"}}")));
        }
    }
}
