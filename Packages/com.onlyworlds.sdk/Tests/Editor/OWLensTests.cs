using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The lens profile's conformance fixture (schema-dist <c>profiles/lens.fixture.json</c>, vendored byte for byte in
    /// <c>Tests/Editor/Fixtures/lens/</c> from dist.94), replayed case by case through <see cref="OWLens"/>: the pair and
    /// the template (<see cref="OWLens.IsLens"/>, <see cref="OWLens.IsTemplate"/>), the legal values of <c>x_lens</c>
    /// (<see cref="OWLens.CheckKeys"/>), every resolution over the fixture's worlds and their readers
    /// (<see cref="OWLens.Resolve(IEnumerable{JToken}, JObject, OWLensReader, OWLensOptions)"/>), the invariants, and
    /// where a seat's choice lives (<see cref="OWLens.LensesFor"/>). The profile's tables (anchor fields, type order, date
    /// table, room pairs, cut reasons, defaults) are held equal to the fixture's and to the generated models. keel's
    /// reference reader replays the same cases. When the dist moves the fixture, re-vendor it and update the hash below.
    /// </summary>
    /// <remarks>
    /// A fixture world lists its elements as its owner is served them and, apart, the ids its hidden mark keeps; the
    /// world handed to the reader here carries that mark on each of them (<c>hidden: true</c>, the folder's key, which
    /// <see cref="OWPayload.IsHidden"/> reads), so the reader's view is built from the bodies alone.
    /// </remarks>
    public class OWLensTests
    {
        /// <summary>The fixture's sha256 as dist.94's MANIFEST.json records it.</summary>
        private const string DistSha256 = "a7520d6470e5ac5b4ef87d8c6fa73a53dcf7b9fa4fbf3065eca9fe16865e4f09";

        /// <summary>The fixture's cases by kind, as dist.94 holds them (186 in all; the graph kind is the graph reader's).</summary>
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>
        {
            ["shape"] = 6, ["keys"] = 25, ["resolve"] = 99, ["invariant"] = 4, ["uses"] = 5, ["graph"] = 47,
        };

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "lens", "lens.fixture.json");
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
                yield return new TestCaseData(string.Empty).SetName("Lens_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                if ((string)c["kind"] == "graph") continue; // the graph section's cases: the graph reader's to replay
                var name = (string)c["name"];
                yield return new TestCaseData(name).SetName("Lens_" + name.Replace('-', '_'));
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored lens fixture was not found: " + path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.94's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist88sCases()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(186, cases.Count, "dist.94's fixture holds 186 cases");
            var counts = cases.GroupBy(c => (string)c["kind"]).ToDictionary(g => g.Key, g => g.Count());
            CollectionAssert.AreEquivalent(Counts, counts);
        }

        [Test]
        public void TheTables_AreTheFixtures_AndTheGeneratedModels()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var f = Fixture;
            Assert.AreEqual("lens", (string)f["profile"]);
            Assert.AreEqual(((string)f["pair"]["type"], (string)f["pair"]["supertype"]), OWLens.Pair);
            Assert.AreEqual((string)f["template_subtype"], OWLens.TemplateSubtype);
            Assert.AreEqual((string)f["field"], OWLens.Field);
            Assert.AreEqual((string)f["uses_field"], OWLens.UsesField);

            CollectionAssert.AreEqual(Strings(f["anchor_fields"]), OWLens.AnchorFields);
            var constructLists = OWLens.LinkFieldsOf("construct").Where(l => l.many).Select(l => l.field)
                .Where(n => n != "titles").ToList();
            CollectionAssert.AreEqual(constructLists, OWLens.AnchorFields,
                "the anchor fields are the Construct's link lists in the schema's order, less titles");

            CollectionAssert.AreEqual(Strings(f["type_order"]), OWElementTypes.Slugs, "the budget's type order is the schema's");

            var dates = ((JObject)f["date_table"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
            CollectionAssert.AreEquivalent(dates, OWLens.DateFields);
            foreach (var pair in OWLens.DateFields)
                CollectionAssert.Contains(OWElementTypes.FieldNames[pair.Key], pair.Value, pair.Key);

            var roomWords = OWRoom.Pairs.Select(p => OWProfileText.Fold(p.type) + "/" + OWProfileText.Fold(p.supertype));
            CollectionAssert.AreEqual(Strings(f["room_pairs"]), roomWords.ToList());
            var aidWords = OWRoom.ReadingAidPairs.Select(p => OWProfileText.Fold(p.type) + "/" + OWProfileText.Fold(p.supertype));
            CollectionAssert.AreEqual(Strings(f["reading_aid_pairs"]), aidWords.ToList());
            Assert.AreEqual((string)f["reading_aid_mark"]["field"], OWRoom.ReadingAidMarkField);

            CollectionAssert.AreEqual(Strings(f["cut_reasons"]), OWLensCut.All);
            CollectionAssert.AreEqual(Strings(f["owner_only_reasons"]), OWLensCut.OwnerOnly);

            var d = f["defaults"];
            Assert.AreEqual((int)d["depth"], OWLens.DefaultDepth);
            Assert.AreEqual((int)d["budget"], OWLens.DefaultBudget);
            Assert.AreEqual((string)d["undated"], OWLens.DefaultUndated);
            Assert.AreEqual((string)d["show"], OWLens.DefaultShow);
            Assert.AreEqual((string)d["mail"], OWLens.DefaultMail);
            Assert.AreEqual((int)d["mail_days"], OWLens.MailDays);
        }

        [Test]
        public void TheLinkFields_AreTheGeneratedModels_SingleAndList()
        {
            // A spot check of the generated models' links, read for the hop: single links and lists both, scalars never.
            CollectionAssert.AreEqual(new[] { "birthplace", "location" },
                OWLens.LinkFieldsOf("character").Where(l => !l.many).Select(l => l.field).ToArray());
            CollectionAssert.Contains(OWLens.LinkFieldsOf("character").ToList(), ("family", true));
            CollectionAssert.Contains(OWLens.LinkFieldsOf("location").Select(l => l.field).ToList(), "primary_power");
            CollectionAssert.DoesNotContain(OWLens.LinkFieldsOf("location").Select(l => l.field).ToList(), "founding_date");
            CollectionAssert.DoesNotContain(OWLens.LinkFieldsOf("zone").Select(l => l.field).ToList(), "shape");
            CollectionAssert.AreEqual(new[] { "map", "element_id" }, OWLens.LinkFieldsOf("pin").Select(l => l.field).ToArray());
            Assert.AreEqual(("actor", false), OWLens.LinkFieldsOf("relation").First());
            Assert.IsEmpty(OWLens.LinkFieldsOf("not-a-type"));
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_IsAnsweredAsTheProfileSays(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var kind = (string)c["kind"];
            switch (kind)
            {
                case "shape":
                {
                    var element = (JObject)c["element"];
                    Assert.AreEqual((bool)c["is_lens"], OWLens.IsLens(element), name + ": is_lens");
                    Assert.AreEqual((bool)c["is_template"], OWLens.IsTemplate(element), name + ": is_template");
                    return;
                }
                case "keys":
                {
                    var bad = OWLens.CheckKeys(c["x_lens"]);
                    Assert.AreEqual(c["bad"].Type == JTokenType.Null ? null : (string)c["bad"], bad, name + ": the key refused");
                    Assert.AreEqual((bool)c["legal"], bad == null, name + ": legal");
                    return;
                }
                case "uses":
                {
                    var got = OWLens.LensesFor(Maybe(c["membership"]), Maybe(c["x_lens_uses"]));
                    CollectionAssert.AreEqual(Strings(c["lenses"]), got, name);
                    return;
                }
                case "resolve":
                    Resolves(c);
                    return;
                case "invariant":
                    Holds(c);
                    return;
                default:
                    Assert.Fail($"case {name}: a case of unknown kind {kind}");
                    return;
            }
        }

        /// <summary>
        /// The served reader: the bodies a contributor's key is served (its whole view, each body's links to what it
        /// cannot see left out), read as they are, give the contributor's reading of every contributor case in the vale
        /// and the office.
        /// </summary>
        [Test]
        public void TheServedReader_OnAContributorsBodies_ReadsAsTheContributor()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var checkedCases = 0;
            foreach (var c in Fixture["cases"].Cast<JObject>())
            {
                if ((string)c["kind"] != "resolve" || (string)c["reader"] != "contributor") continue;
                var fixtureWorld = (JObject)Fixture["worlds"][(string)c["world"]];
                var world = WorldOf(fixtureWorld);
                var contributor = OWLensReader.Contributor((string)fixtureWorld["readers"]["contributor"]["character"]);
                var served = OWLens.ServedView(world, contributor);
                var options = OptionsOf(c, fixtureWorld);
                var want = Json(ResolveCase(world, c, contributor, options));
                var got = Json(ResolveCase(served, c, OWLensReader.Served(contributor.Character), options));
                Assert.IsTrue(JToken.DeepEquals(want, got), $"{c["name"]}: served {got.ToString(Formatting.None)}, contributor {want.ToString(Formatting.None)}");
                checkedCases++;
            }

            Assert.Greater(checkedCases, 20);
        }

        [Test]
        public void AnUnreadableLens_NamesItsKey_AndResolvesNothing()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var vale = (JObject)Fixture["worlds"]["vale"];
            var lens = new JObject
            {
                ["id"] = "019a7f00-0000-7000-8000-0000000000ff", ["type"] = "construct", ["supertype"] = "Lens",
                ["characters"] = new JArray("019a7f00-0000-7000-8000-000000000001"),
                ["x_lens"] = new JObject { ["depth"] = 3 },
            };
            var got = OWLens.Resolve(WorldOf(vale), lens, OWLensReader.Owner(), new OWLensOptions { Now = 1200 });
            Assert.AreEqual("depth", got.RefusedKey);
            Assert.IsFalse(got.IsReadable);
            Assert.IsEmpty(got.Kept);
            Assert.IsEmpty(got.Cut);
        }

        /// <summary>
        /// The window's edge, which the fixture's window case names ("1190 included") and holds no element at: an element
        /// is inside when its date is at or after <c>as_of</c> minus the window, in the world's own unit, compared as it is.
        /// </summary>
        [Test]
        public void AWindow_HoldsItsEdge_InTheWorldsUnit()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var world = WorldOf((JObject)Fixture["worlds"]["vale"]);
            const string edge = "019a7f00-0000-7000-8000-0000000000e1";
            const string before = "019a7f00-0000-7000-8000-0000000000e2";
            world.Add(new JObject { ["id"] = edge, ["type"] = "event", ["name"] = "On the edge", ["start_date"] = 1190 });
            world.Add(new JObject { ["id"] = before, ["type"] = "event", ["name"] = "Just before", ["start_date"] = 1189 });
            var lens = new JObject
            {
                ["id"] = "019a7f00-0000-7000-8000-0000000000e3", ["type"] = "construct", ["supertype"] = "Lens",
                ["x_lens"] = new JObject { ["types"] = new JArray("event"), ["window"] = 10 },
            };
            var got = OWLens.Resolve(world, lens, OWLensReader.Contributor(), new OWLensOptions { Now = 1200 });
            CollectionAssert.Contains(got.Kept, edge, "1190 is ten years before 1200: inside");
            CollectionAssert.DoesNotContain(got.Kept, before, "1189 is outside");
            Assert.AreEqual(2, got.Cut[OWLensCut.Window], "the First Lamp (1150) and the year before the edge");
        }

        /// <summary>
        /// A proposed element is in no reader's view when its <c>x_review_proposed</c> holds any value but null: a doubt
        /// (a string here) as well as a mark, the review profile's rule for a reader that hands a world on, as the lens
        /// profile names it and the TypeScript SDK and the Python package read it.
        /// </summary>
        [Test]
        public void AProposedDoubt_IsInNoView()
        {
            var world = new List<JObject>
            {
                new JObject { ["id"] = "c1", ["type"] = "character", ["name"] = "Ada", ["location"] = "p1" },
                new JObject { ["id"] = "p1", ["type"] = "location", ["name"] = "Doubtful Keep", [OWPayload.ReviewProposed] = "unsure" },
            };
            var lens = new JObject { ["id"] = "L", ["type"] = "construct", ["supertype"] = "Lens", ["characters"] = new JArray("c1") };
            var got = OWLens.Resolve(world, lens, OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1" }, got.Kept, "a doubt is not there to hop to");
            Assert.AreEqual(1, got.Cut.TryGetValue(OWLensCut.NotVisible, out var n) ? n : 0, "the owner counts it as not visible");
        }

        /// <summary>
        /// An empty mark (<c>{}</c>, <c>[]</c>, a string of White_Space) is no mark and no doubt (the review profile's
        /// rule 1, serial 87, as keel's lens reader reads it through <c>doubt_or_mark_q</c>): the element is in every
        /// reader's view and a hop reaches it.
        /// </summary>
        [Test]
        public void AnEmptyMark_IsInTheView()
        {
            foreach (var empty in new JToken[] { new JObject(), new JArray(), " \t" })
            {
                var world = new List<JObject>
                {
                    new JObject { ["id"] = "c1", ["type"] = "character", ["name"] = "Ada", ["location"] = "p1" },
                    new JObject { ["id"] = "p1", ["type"] = "location", ["name"] = "Settled Keep", [OWPayload.ReviewProposed] = empty },
                };
                var lens = new JObject { ["id"] = "L", ["type"] = "construct", ["supertype"] = "Lens", ["characters"] = new JArray("c1") };
                foreach (var reader in new[] { OWLensReader.Owner(), OWLensReader.Public() })
                {
                    var got = OWLens.Resolve(world, lens, reader);
                    CollectionAssert.AreEqual(new[] { "c1", "p1" }, got.Kept, empty.ToString(Newtonsoft.Json.Formatting.None) + " is no mark");
                }
            }
        }

        /// <summary>An entry whose type is not one of the standard's is not in the world: a filter lens never keeps it.</summary>
        [Test]
        public void AnElementOfNoStandardType_IsNotInTheWorld()
        {
            var world = new List<JObject>
            {
                new JObject { ["id"] = "c1", ["type"] = "character", ["name"] = "Ada" },
                new JObject { ["id"] = "w1", ["type"] = "widget", ["name"] = "Gadget" },
                new JObject { ["id"] = "n1", ["name"] = "No type" },
            };
            var filter = new JObject { ["id"] = "L", ["type"] = "construct", ["supertype"] = "Lens" };
            var got = OWLens.Resolve(world, filter, OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1" }, got.Kept);
        }

        /// <summary>An element that is not a lens resolves to nothing, its <c>x_lens</c> unread: no keys refused, nothing kept.</summary>
        [Test]
        public void AnElementThatIsNotALens_ResolvesToNothing()
        {
            var world = new List<JObject>
            {
                new JObject { ["id"] = "c1", ["type"] = "character", ["name"] = "Ada" },
                new JObject { ["id"] = "c2", ["type"] = "character", ["name"] = "Brin" },
            };
            var notALens = new JObject
            {
                ["id"] = "k1", ["type"] = "construct", ["supertype"] = "Charter", ["characters"] = new JArray("c1"),
                ["x_lens"] = new JObject { ["depth"] = 9 },
            };
            var got = OWLens.Resolve(world, notALens, OWLensReader.Owner());
            Assert.IsTrue(got.IsReadable, "refused " + got.RefusedKey);
            Assert.IsEmpty(got.Kept);
            notALens.Remove("x_lens");
            Assert.IsEmpty(OWLens.Resolve(world, notALens, OWLensReader.Owner()).Kept, "a Construct that is not a lens keeps nothing");
        }

        // -- The replay ------------------------------------------------------------------

        private static void Resolves(JObject c)
        {
            var name = (string)c["name"];
            var fixtureWorld = (JObject)Fixture["worlds"][(string)c["world"]];
            var world = WorldOf(fixtureWorld);
            var got = ResolveCase(world, c, ReaderOf(fixtureWorld, (string)c["reader"]), OptionsOf(c, fixtureWorld));
            var gotJson = Json(got);

            Assert.IsTrue(got.IsReadable, $"{name}: refused {got.RefusedKey}");
            var names = world.ToDictionary(e => (string)e["id"], e => (string)e["name"]);
            string Named(IEnumerable<string> ids) => string.Join(", ", ids.Select(i => names.TryGetValue(i, out var n) ? n : i));
            CollectionAssert.AreEqual(Strings(c["kept"]), got.Kept,
                $"{name}: kept [{Named(got.Kept)}], the fixture says [{Named(Strings(c["kept"]))}]");
            Assert.IsTrue(JToken.DeepEquals(c["cut"], gotJson["cut"]),
                $"{name}: cut {gotJson["cut"].ToString(Formatting.None)}, the fixture says {c["cut"].ToString(Formatting.None)}");
            if (c.TryGetValue("rows", out var rows))
                Assert.IsTrue(JToken.DeepEquals(rows, gotJson["rows"]),
                    $"{name}: rows {gotJson["rows"].ToString(Formatting.None)}, the fixture says {rows.ToString(Formatting.None)}");
            if (c.TryGetValue("notes_ignored", out var ignored))
                Assert.AreEqual((int)ignored, got.NotesIgnored, name + ": notes_ignored");
            if (c.TryGetValue("mail", out var mailToken))
            {
                Assert.IsNotNull(got.Mail, name + ": no mail part");
                foreach (var key in ((JObject)mailToken).Properties())
                    Assert.IsTrue(JToken.DeepEquals(key.Value, gotJson["mail"][key.Name]),
                        $"{name}: mail.{key.Name} {gotJson["mail"][key.Name]?.ToString(Formatting.None)}, the fixture says {key.Value.ToString(Formatting.None)}");
            }
        }

        private static void Holds(JObject c)
        {
            var name = (string)c["name"];
            var fixtureWorld = (JObject)Fixture["worlds"][(string)c["world"]];
            var world = WorldOf(fixtureWorld);
            var readers = Strings(c["readers"]);
            var options = OptionsOf(c, fixtureWorld);
            switch ((string)c["claim"])
            {
                case "subset":
                {
                    var owner = ResolveCase(world, c, ReaderOf(fixtureWorld, "owner"), options);
                    var ownerCut = Json(owner)["cut"];
                    var secrets = Strings(c["secrets"]);
                    var secretNames = world.Where(e => secrets.Contains((string)e["id"])).Select(e => (string)e["name"]).ToList();
                    foreach (var r in readers)
                    {
                        var got = ResolveCase(world, c, ReaderOf(fixtureWorld, r), options);
                        CollectionAssert.IsSubsetOf(got.Kept, owner.Kept, $"{name}: {r}'s kept is not within the owner's");
                        if ((string)fixtureWorld["readers"][r]["rung"] == "owner") continue;
                        var text = Json(got).ToString(Formatting.None);
                        foreach (var s in secrets.Concat(secretNames))
                            StringAssert.DoesNotContain(s, text, $"{name}: {r}'s result names a secret");
                        Assert.IsFalse(((JObject)ownerCut).Count > 0 && JToken.DeepEquals(ownerCut, Json(got)["cut"]),
                            $"{name}: {r}'s counts equal the owner's");
                    }

                    return;
                }
                case "same_without":
                {
                    var without = new HashSet<string>(Strings(c["without"]));
                    var less = WorldOf(fixtureWorld).Where(e => !without.Contains((string)e["id"])).ToList();
                    foreach (var r in readers)
                    {
                        var a = Json(ResolveCase(world, c, ReaderOf(fixtureWorld, r), options));
                        var b = Json(ResolveCase(less, c, ReaderOf(fixtureWorld, r), options));
                        Assert.IsTrue(JToken.DeepEquals(a, b),
                            $"{name}: {r} reads {a.ToString(Formatting.None)} with, {b.ToString(Formatting.None)} without");
                    }

                    return;
                }
                case "unchanged":
                {
                    var before = new JArray(world.Select(e => e.DeepClone()));
                    foreach (var r in readers) ResolveCase(world, c, ReaderOf(fixtureWorld, r), options);
                    Assert.IsTrue(JToken.DeepEquals(before, new JArray(world)), name + ": a reading moved the world");
                    return;
                }
                default:
                    Assert.Fail($"{name}: a claim of unknown kind {c["claim"]}");
                    return;
            }
        }

        // -- The date table and DateOf (the TypeScript SDK 4.96.0's DATE_FIELD and dateOf, the Python package 0.79.0's
        // DATE_FIELD and date_of) ------------------------------------------------------------------------------------

        /// <summary>
        /// The TypeScript SDK 4.96.0's vector (<c>test/date.test.mjs</c>): the table is the fixture's <c>date_table</c>, every
        /// type of the standard is either dated (12) or timeless (10), each date field is an integer field of its type, and
        /// the table cannot be written to. (The prose table of <c>profiles/lens.md</c> is not vendored in the package; the
        /// fixture's table is its machine copy.) Kills: a table a caller can change under the fence, a field that is not a
        /// whole number in the generated model, a type in neither half.
        /// </summary>
        [Test]
        public void DateFields_IsTheFixturesTable_EveryTypeDatedOrTimeless_EachFieldAnInteger_AndReadOnly()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var dates = ((JObject)Fixture["date_table"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
            CollectionAssert.AreEquivalent(dates, OWLens.DateFields);
            Assert.AreEqual(12, OWLens.DateFields.Count);
            Assert.AreEqual(10, OWElementTypes.Slugs.Count(s => !OWLens.DateFields.ContainsKey(s)), "the ten timeless types");
            CollectionAssert.IsSubsetOf(OWLens.DateFields.Keys, OWElementTypes.Slugs);

            foreach (var pair in OWLens.DateFields)
            {
                var field = OWElementTypes.TypeFor(pair.Key)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .SingleOrDefault(f => f.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName == pair.Value);
                Assert.IsNotNull(field, $"{pair.Key}.{pair.Value} is a field of the generated model");
                Assert.AreEqual(typeof(SerializableNullable<int>), field.FieldType, $"{pair.Key}.{pair.Value} is an integer field");
            }

            var asDictionary = OWLens.DateFields as IDictionary<string, string>;
            Assert.IsTrue(asDictionary == null || asDictionary.IsReadOnly, "the table cannot be written to, not even through a cast");
        }

        /// <summary>
        /// The TypeScript SDK 4.96.0's vector: the whole number in the type's date field, 0 and a date before 0 included,
        /// the type trimmed and folded. Kills: 0 read as no date, a negative date dropped, a type compared as written.
        /// </summary>
        [Test]
        public void DateOf_ReadsTheWholeNumber_InTheTypesDateField_ZeroIncluded()
        {
            foreach (var pair in OWLens.DateFields)
            {
                Assert.AreEqual(1204, OWLens.DateOf(new JObject { ["type"] = pair.Key, [pair.Value] = 1204 }), pair.Key);
                Assert.AreEqual(0, OWLens.DateOf(new JObject { ["type"] = pair.Key, [pair.Value] = 0 }), pair.Key + " at 0");
                Assert.AreEqual(-40, OWLens.DateOf(new JObject { ["type"] = pair.Key, [pair.Value] = -40 }), pair.Key + " before 0");
            }

            Assert.AreEqual(7, OWLens.DateOf(JObject.Parse(@"{""type"":"" Event "",""start_date"":7}")), "the type is trimmed and folded");
            Assert.AreEqual(7, OWLens.DateOf(JObject.Parse(@"{""type"":""EVENT"",""start_date"":7}")));
            // A number with no fraction is whole, as the Python package 0.79.0 tests it (date_of(start_date 7.0) == 7)
            // and as JavaScript reads it (7.0 is 7).
            Assert.AreEqual(7, OWLens.DateOf(OWJson.ParseObject(@"{""type"":""event"",""start_date"":7.0}")), "7.0 is whole");
            Assert.AreEqual(-40, OWLens.DateOf(new JObject { ["type"] = "event", ["start_date"] = -40.0 }), "-40.0 is whole");
        }

        /// <summary>
        /// The TypeScript SDK 4.96.0's vector: null for a timeless type, another type's field, a value that is not a whole
        /// number, an absent field, no type, a type that is no type, a known-of stub (it serves no date), and anything that
        /// is not an element body. Kills: a timeless type read by a field it does not have, a string or a fraction read as
        /// a date, a stub dated by a field it was never served.
        /// </summary>
        [Test]
        public void DateOf_IsNull_ForTheTimeless_TheUntyped_AndWhatIsNotADate()
        {
            foreach (var type in OWElementTypes.Slugs.Where(s => !OWLens.DateFields.ContainsKey(s)))
            {
                Assert.IsNull(OWLens.DateOf(new JObject { ["type"] = type, ["start_date"] = 5, ["birth_date"] = 5, ["date"] = 5 }), type);
            }

            Assert.IsNull(OWLens.DateOf(JObject.Parse(@"{""type"":""event"",""birth_date"":5}")), "another type's field");
            foreach (var v in new JToken[]
            {
                JValue.CreateNull(), new JValue("12"), new JValue(1.5), new JValue(true), new JValue(double.NaN),
                new JArray(3), new JObject { ["year"] = 3 },
            })
            {
                Assert.IsNull(OWLens.DateOf(new JObject { ["type"] = "event", ["start_date"] = v }), v.ToString(Formatting.None));
            }

            Assert.IsNull(OWLens.DateOf(JObject.Parse(@"{""type"":""event""}")), "absent");
            Assert.IsNull(OWLens.DateOf(JObject.Parse(@"{""start_date"":5}")), "no type");
            Assert.IsNull(OWLens.DateOf(JObject.Parse(@"{""type"":3,""start_date"":5}")), "a type that is not text");
            Assert.IsNull(OWLens.DateOf(JObject.Parse(@"{""type"":""constructor"",""constructor"":5}")), "a word that is no type");
            Assert.IsNull(OWLens.DateOf(JObject.Parse(@"{""type"":""event"",""known_of"":true,""name"":""A stub""}")), "a stub serves no date");
            foreach (var v in new JToken[] { null, JValue.CreateNull(), new JValue("event"), new JValue(3), JArray.Parse(@"[{""type"":""event"",""start_date"":1}]") })
            {
                Assert.IsNull(OWLens.DateOf(v), v == null ? "null" : v.ToString(Formatting.None));
            }
        }

        // The TypeScript SDK 4.93.1's vector (test/lens.test.mjs; the Python package 0.77.1 holds the same): a lens a guest
        // knows only as a stub, named by its id, reads as nothing, as the server's own reader answers it. A stub serves no
        // links and no recipe, so the stored body's anchors are never followed. Whole, the same lens reads. This SDK's
        // Resolve already read only a lens served whole; the vector holds it there. Kills: a lens id looked up in
        // everything the reader sees (a stub included) rather than in what it is served whole.
        [Test]
        public void ALensAGuestKnowsOnlyAsAStub_ReadsAsNothing_ById_AndWhole_ItReads()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""L"",""type"":""construct"",""name"":""Ada and hers"",""supertype"":""Lens"",""characters"":[""c1""],""x_lens"":{""depth"":1}}"),
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada"",""friends"":[""c2""]}"),
                JObject.Parse(@"{""id"":""c2"",""type"":""character"",""name"":""Bo""}"),
            };

            var stub = OWLensReader.Guest(new[] { "c1", "c2" }, new[] { "L" });
            var r = OWLens.Resolve(world, "L", stub);
            CollectionAssert.IsEmpty(r.Kept, "a stub lens keeps nothing");
            CollectionAssert.IsEmpty(r.Cut.Where(p => p.Value > 0), "a stub lens cuts nothing");
            Assert.IsNull(r.RefusedKey, "a stub lens is not an unreadable one");
            CollectionAssert.IsEmpty(OWGraph.Read(world, stub, new OWGraphOptions { LensId = "L" }).Nodes,
                "the graph reads the lens as the resolver does");

            var whole = OWLens.Resolve(world, "L", OWLensReader.Guest(new[] { "L", "c1", "c2" }, new string[0]));
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, whole.Kept);
        }

        // Rule 1 at serial 82: a lens the reader is served only as a known-of stub resolves as not there, by its body too.
        // A served stub carries no links and no x_lens, so read as a body it would be a lens with no anchors: a filter over
        // the reader's whole view. Kills: a stub body read as a filter.
        [Test]
        public void ALensBodyServedAsAStub_ReadsAsNothing_NeverAsAFilter()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada""}"),
                JObject.Parse(@"{""id"":""c2"",""type"":""character"",""name"":""Bo""}"),
            };
            var stub = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""name"":""Ada and hers"",""supertype"":""Lens"",""known_of"":true}");

            var r = OWLens.Resolve(world, stub, OWLensReader.Owner());
            CollectionAssert.IsEmpty(r.Kept, "a stub lens body keeps nothing");
            CollectionAssert.IsEmpty(r.Cut, "a stub lens body cuts nothing");
            Assert.IsNull(r.RefusedKey);
            CollectionAssert.IsEmpty(OWGraph.Read(world, OWLensReader.Owner(), new OWGraphOptions { Lens = stub }).Nodes);

            stub["known_of"] = false;
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, OWLens.Resolve(world, stub, OWLensReader.Owner()).Kept,
                "known_of false is a whole body: a lens with no anchors is a filter");
        }

        // notes_ignored counts as keel's reader does: the notes on ids that are not anchors (the lens's anchors and the
        // ones the reader cannot see), only where the reader sees the id. A note keyed to a hidden anchor went with the
        // anchor (rule 6), one keyed to an element the reader cannot see is no note of its, and one keyed to an id in no
        // element names nothing. Kills: a hidden anchor's note counted, which tells a guest a hidden element exists.
        [Test]
        public void NotesIgnored_CountsOnlyNonAnchorsTheReaderSees()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""a"",""type"":""character"",""name"":""Seen anchor""}"),
                JObject.Parse(@"{""id"":""ha"",""type"":""character"",""name"":""Hidden anchor"",""hidden"":true}"),
                JObject.Parse(@"{""id"":""s"",""type"":""character"",""name"":""Seen, not an anchor""}"),
                JObject.Parse(@"{""id"":""h"",""type"":""character"",""name"":""Hidden, not an anchor"",""hidden"":true}"),
            };
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""supertype"":""Lens"",""characters"":[""a"",""ha""],""x_lens"":{""depth"":0,""notes"":{
                ""a"":""an anchor's note"",""ha"":""a hidden anchor's note"",""s"":""seen, not an anchor"",""h"":""hidden, not an anchor"",""nowhere"":""an id in no element""}}}");

            Assert.AreEqual(1, OWLens.Resolve(world, lens, OWLensReader.Contributor()).NotesIgnored, "the contributor");
            Assert.AreEqual(2, OWLens.Resolve(world, lens, OWLensReader.Owner()).NotesIgnored, "the owner sees h");
        }

        // hops_not_served never counts a reading aid that types would not keep, as it never counts such a room element
        // (keel's reader; the ruling rides serial 83's fixture). Kills: an aid the lens would cut as types counted as a
        // hop not served.
        [Test]
        public void HopsNotServed_CountsAReadingAidOnlyWhenTypesWouldKeepIt()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""A"",""type"":""institution"",""name"":""The Desk"",""allies"":[""B""]}"),
                JObject.Parse(@"{""id"":""B"",""type"":""institution"",""name"":""The Bindery""}"),
                JObject.Parse(@"{""id"":""ch"",""type"":""law"",""name"":""House Rules"",""supertype"":""Charter"",""author"":""B"",""x_charter_of"":""w1""}"),
            };
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""supertype"":""Lens"",""institutions"":[""A""],""x_lens"":{""depth"":2}}");

            var r = OWLens.Resolve(world, lens, OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "A", "B" }, r.Kept);
            CollectionAssert.IsEmpty(r.Cut, "the charter at the second hop, which types would not keep, is not counted");

            lens["x_lens"]["types"] = new JArray("institution", "law/charter");
            r = OWLens.Resolve(world, lens, OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "A", "B" }, r.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.HopsNotServed] = 1 }, r.Cut,
                "a lens that names the pair counts it");
        }

        /// <summary>
        /// The room mark's five shapes (<see cref="OWRoomTests.MarkShapes"/>) through every place the reader asks whether
        /// an element is a room element: the public reader's view, <c>types</c> (a room element kept only by its pair), a
        /// Relation that is no edge to its parties, no hop out of a room element, and <c>hops_not_served</c>. Kills: a
        /// path of the lens that reads the mark apart from <see cref="OWRoom.IsRoomElement"/>.
        /// </summary>
        [TestCaseSource(typeof(OWRoomTests), nameof(OWRoomTests.MarkShapes))]
        public void TheRoomMark_CountsByPresence_InTheLens(string markJson, bool marks)
        {
            var mark = JToken.Parse(markJson);
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada""}"),
                JObject.Parse(@"{""id"":""m1"",""type"":""narrative"",""name"":""A line"",""supertype"":""Message"",""characters"":[""c1"",""c3""]}"),
                JObject.Parse(@"{""id"":""c3"",""type"":""character"",""name"":""Cy""}"),
            };
            world[1][OWRoom.MarkField] = mark.DeepClone();
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""supertype"":""Lens"",""characters"":[""c1""],""x_lens"":{""depth"":1}}");

            var r = OWLens.Resolve(world, lens, OWLensReader.Public());
            CollectionAssert.AreEqual(marks ? new[] { "c1" } : new[] { "c1", "m1" }, r.Kept, "the public reader's view");
            CollectionAssert.IsEmpty(r.Cut, "the public reader counts nothing it does not see");

            r = OWLens.Resolve(world, lens, OWLensReader.Owner());
            CollectionAssert.AreEqual(marks ? new[] { "c1" } : new[] { "c1", "m1" }, r.Kept, "the owner, no types");
            CollectionAssert.AreEquivalent(marks ? new Dictionary<string, int> { [OWLensCut.Types] = 1 } : new Dictionary<string, int>(),
                r.Cut, "a room element is kept only by its pair");

            // A Vote among c1 and c2, and a second hop from the message to c3: depth 2 with the pairs named.
            world.Add(JObject.Parse(@"{""id"":""r1"",""type"":""relation"",""name"":""Aye"",""supertype"":""Vote"",""characters"":[""c1"",""c2""]}"));
            world.Add(JObject.Parse(@"{""id"":""c2"",""type"":""character"",""name"":""Bo""}"));
            world[3][OWRoom.MarkField] = mark.DeepClone();
            lens["x_lens"] = JObject.Parse(@"{""depth"":2,""types"":[""character"",""narrative/message"",""relation/vote""]}");

            r = OWLens.Resolve(world, lens, OWLensReader.Owner());
            if (marks)
            {
                CollectionAssert.AreEquivalent(new[] { "c1", "m1", "r1" }, r.Kept, "the pairs kept; the room Vote is no edge to c2");
                CollectionAssert.IsEmpty(r.Cut, "no hop goes out of a room element, so c3 is never reached");
            }
            else
            {
                CollectionAssert.AreEquivalent(new[] { "c1", "c2" }, r.Kept, "a lore Vote is an edge to c2, in the same hop");
                CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Types] = 2, [OWLensCut.HopsNotServed] = 1 },
                    r.Cut, "the lore message and Vote cut as types; c3 at the second hop not served");
            }
        }

        // -- An umbrella's children, as keel's reader reads them (the ruling rides serial 83) ----------------------------
        // Each child is resolved by its own whole recipe, its own mail rule and budget among it; the union then takes the
        // umbrella's mail rule and budget on top, and every row is built by the umbrella's recipe alone.

        private static JObject Lens(string id, string xLens, string links) =>
            JObject.Parse(@"{""id"":""" + id + @""",""type"":""construct"",""name"":""" + id + @""",""supertype"":""Lens"",""x_lens"":"
                          + xLens + (links == null ? "" : "," + links) + "}");

        private static JObject Character(string id, string links = null) =>
            JObject.Parse(@"{""id"":""" + id + @""",""type"":""character"",""name"":""" + id + @""",""description"":""d""" +
                          (links == null ? "" : "," + links) + "}");

        /// <summary>Kills: a child's own budget unread (only the union capped), a child's over-budget ids not counted.</summary>
        [Test]
        public void AnUmbrellasChild_KeepsAtMostItsOwnBudget_TheRestCountedBudget()
        {
            var world = new List<JObject>
            {
                Lens("U", "{}", @"""constructs"":[""C""]"),
                Lens("C", @"{""depth"":0,""budget"":1}", @"""characters"":[""c1"",""c2""]"),
                Character("c1"),
                Character("c2"),
            };
            var r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1" }, r.Kept, "the child keeps its budget's worth");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Budget] = 1 }, r.Cut);

            // The umbrella's budget on top of a child with none of its own.
            world[0] = Lens("U", @"{""budget"":1}", @"""constructs"":[""C""]");
            world[1] = Lens("C", @"{""depth"":0}", @"""characters"":[""c1"",""c2""]");
            r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1" }, r.Kept, "the umbrella's budget caps the union");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Budget] = 1 }, r.Cut);

            // The child's budget counts what it keeps before the union, an id the umbrella keeps too among it. The umbrella
            // reads at depth 0: a hop from c1 would reach its child C as an element (rule 3, serial 83).
            world[0] = Lens("U", @"{""depth"":0}", @"""characters"":[""c1""],""constructs"":[""C""]");
            world[1] = Lens("C", @"{""depth"":0,""budget"":2}", @"""characters"":[""c1"",""c2"",""c3""]");
            world.Add(Character("c3"));
            r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, r.Kept, "the child's two, one of them the umbrella's own");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Budget] = 1 }, r.Cut);
        }

        /// <summary>Kills: a row kept through a child shown by the child's <c>show</c>.</summary>
        [Test]
        public void AnUmbrellasChildsRow_IsShownByTheUmbrellasShow()
        {
            var world = new List<JObject>
            {
                Lens("U", @"{""show"":""full""}", @"""constructs"":[""C""]"),
                Lens("C", @"{""depth"":0,""show"":""name""}", @"""characters"":[""c1""]"),
                Character("c1"),
            };
            var row = OWLens.Resolve(world, "U", OWLensReader.Owner()).Rows.Single();
            Assert.AreEqual("d", (string)row["description"], "full under a full umbrella: " + row);

            world[0] = Lens("U", @"{""show"":""name""}", @"""constructs"":[""C""]");
            world[1] = Lens("C", @"{""depth"":0,""show"":""full""}", @"""characters"":[""c1""]");
            row = OWLens.Resolve(world, "U", OWLensReader.Owner()).Rows.Single();
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""c1""}"), row),
                "a name row under a name umbrella: " + row);
        }

        /// <summary>
        /// Kills: a child's <c>x_lens.notes</c> on a row, or counted in the umbrella's <c>notes_ignored</c> (rows are built
        /// with the umbrella's recipe only).
        /// </summary>
        [Test]
        public void AnUmbrellasChildsNotes_NeverReachARow()
        {
            var world = new List<JObject>
            {
                Lens("U", "{}", @"""constructs"":[""C""]"),
                Lens("C", @"{""depth"":0,""notes"":{""c1"":""the child's note on its own anchor"",""c2"":""on none of its anchors""}}",
                    @"""characters"":[""c1""]"),
                Character("c1"),
                Character("c2"),
            };
            var r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1" }, r.Kept);
            Assert.IsNull(r.Rows.Single()["note"], r.Rows.Single().ToString());
            Assert.AreEqual(0, r.NotesIgnored, "the child's notes are not the umbrella's to count");
        }

        /// <summary>
        /// Kills: a child's own mail rule unread (its message left in the union), and a child's mail served or counted
        /// under the umbrella. Under an umbrella with mail <c>none</c> the child's messages are simply gone.
        /// </summary>
        [Test]
        public void AnUmbrellasChildsMailRule_TakesItsMessagesOut_NeitherServedNorCounted()
        {
            var world = new List<JObject>
            {
                Lens("U", "{}", @"""constructs"":[""C""]"),
                Lens("C", @"{""depth"":1,""mail"":""all""}", @"""characters"":[""me""]"),
                Character("me"),
                Character("a"),
                JObject.Parse(@"{""id"":""m1"",""type"":""narrative"",""name"":""A letter"",""supertype"":""Message"",""narrator"":""a"",
                    ""characters"":[""me""],""created_at"":""2026-10-20T12:00:00Z""}"),
            };
            var options = new OWLensOptions { Clock = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero) };

            var child = OWLens.Resolve(world, "C", OWLensReader.Owner("me"), options);
            CollectionAssert.AreEqual(new[] { "me" }, child.Kept, "read alone, the child's mail rule takes the letter out");
            CollectionAssert.AreEqual(new[] { "m1" }, child.Mail.Unread, "and serves it in its mail");

            var r = OWLens.Resolve(world, "U", OWLensReader.Owner("me"), options);
            CollectionAssert.AreEqual(new[] { "me" }, r.Kept, "under the umbrella the letter is out of the child's kept");
            Assert.IsNull(r.Mail, "the umbrella's mail is none: the child's mail is not served");
            CollectionAssert.IsEmpty(r.Cut, "nor counted");
        }

        /// <summary>
        /// Rule 3 at serial 83: a sibling lens a child's hop reaches is an element of the result it was reached into, even
        /// when the umbrella enters it as a later child; the union drops an element only by its id. Entered first, the
        /// sibling is visited and the hop never reaches it. Reached and cut, it stays counted under its reason, as keel's
        /// reader counts it. Kills: entered lens ids dropped from the union or from the cut.
        /// </summary>
        [Test]
        public void AnUmbrellasSiblingLens_ReachedByAChildsHop_IsKept_UnlessEnteredFirst()
        {
            var world = new List<JObject>
            {
                Lens("U", @"{""depth"":0}", @"""constructs"":[""W"",""R""]"),
                Lens("W", @"{""depth"":1}", @"""characters"":[""h""]"),
                Lens("R", @"{""depth"":0}", @"""characters"":[""h""]"),
                Character("h"),
            };
            var r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "h", "R" }, r.Kept, "W's hop from h reaches R before R is entered");
            CollectionAssert.IsEmpty(r.Cut);

            world[0] = Lens("U", @"{""depth"":0}", @"""constructs"":[""R"",""W""]");
            r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "h" }, r.Kept, "R entered first is visited: W's hop never reaches it");
            CollectionAssert.IsEmpty(r.Cut);

            world[0] = Lens("U", @"{""depth"":0}", @"""constructs"":[""W"",""R""]");
            world[1] = Lens("W", @"{""depth"":1,""types"":[""character""]}", @"""characters"":[""h""]");
            r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "h" }, r.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Types] = 1 }, r.Cut,
                "R reached outside W's types is counted, though the umbrella enters it next");

            // The umbrella's own hop reaches its child the same way, before entering it.
            world = new List<JObject>
            {
                Lens("U", "{}", @"""characters"":[""h""],""constructs"":[""R""]"),
                Lens("R", @"{""depth"":0}", @"""characters"":[""h""]"),
                Character("h"),
            };
            r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "h", "R" }, r.Kept, "U's hop from h reaches R, its own child");
            CollectionAssert.IsEmpty(r.Cut);
        }

        /// <summary>
        /// The profile's <c>notes</c>: a note keyed to an element that is not an anchor is ignored, counted in
        /// <c>notes_ignored</c>, even when that element is kept (reached by a hop, or kept through a child). keel's reader
        /// puts it on the row; the profile is followed here. Kills: a note on a kept non-anchor's row.
        /// </summary>
        [Test]
        public void ANoteOnAKeptElementThatIsNotAnAnchor_IsIgnored_NotOnItsRow()
        {
            var world = new List<JObject>
            {
                Lens("L", @"{""depth"":1,""notes"":{""c1"":""an anchor's note"",""c2"":""reached, not an anchor""}}", @"""characters"":[""c1""]"),
                Character("c1", @"""friends"":[""c2""]"),
                Character("c2"),
            };
            var r = OWLens.Resolve(world, "L", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, r.Kept);
            Assert.AreEqual("an anchor's note", (string)r.Rows[0]["note"]);
            Assert.IsNull(r.Rows[1]["note"], "a hop's element: " + r.Rows[1]);
            Assert.AreEqual(1, r.NotesIgnored);

            world[0] = Lens("U", @"{""notes"":{""c2"":""kept through the child, not an anchor""}}", @"""characters"":[""c1""],""constructs"":[""C""]");
            world.Add(Lens("C", @"{""depth"":0}", @"""characters"":[""c2""]"));
            world[1] = Character("c1");
            r = OWLens.Resolve(world, "U", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, r.Kept);
            Assert.IsNull(r.Rows[1]["note"], "a child's anchor: " + r.Rows[1]);
            Assert.AreEqual(1, r.NotesIgnored);
        }

        // Ruling 13 (the lens profile's owner, ahead of its next serial): a whole float as an x_lens key value reads whole
        // in every reader, as an element's date does (depth 2.0 is 2); only a fraction is refused, the key named. It
        // retires serial 83's refusal of 2.0 where a reader sees the text. Kills: depth 2.0 refused, 2.5 read, a date of
        // 7.0 read as undated.
        [Test]
        public void Ruling13_AWholeFloatInAnXLensKey_ReadsWhole_AFractionIsRefused()
        {
            foreach (var (key, value) in new[]
                     {
                         ("depth", "2.0"), ("window", "3.0"), ("budget", "10.0"), ("changed_within", "7.0"),
                     })
                Assert.IsNull(OWLens.CheckKeys(JObject.Parse("{\"" + key + "\":" + value + "}")), key + " " + value);
            Assert.IsNull(OWLens.CheckKeys(JObject.Parse(@"{""from"":1.0,""to"":5}")), "from 1.0");
            Assert.IsNull(OWLens.CheckKeys(JObject.Parse(@"{""from"":1,""to"":5.0}")), "to 5.0");
            foreach (var key in new[] { "depth", "window", "budget", "changed_within", "from" })
                Assert.AreEqual(key, OWLens.CheckKeys(JObject.Parse("{\"" + key + "\":1.5}")), key + " 1.5");
            Assert.AreEqual("depth", OWLens.CheckKeys(JObject.Parse(@"{""depth"":3.0}")), "3.0 is 3, past depth's table");

            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""e"",""type"":""event"",""name"":""After"",""start_date"":1300.0}"),
                JObject.Parse(@"{""id"":""c"",""type"":""character"",""name"":""c"",""friends"":[""f""]}"),
                JObject.Parse(@"{""id"":""f"",""type"":""character"",""name"":""f""}"),
            };
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""supertype"":""Lens"",""characters"":[""c""],""x_lens"":{""depth"":1.0,""budget"":1.0}}");
            var read = OWLens.Resolve(world, lens, OWLensReader.Owner());
            Assert.IsNull(read.RefusedKey, "depth 1.0 and budget 1.0 read");
            CollectionAssert.AreEqual(new[] { "c" }, read.Kept, "budget 1.0 is 1");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Budget] = 1 }, read.Cut, "depth 1.0 reached f");

            lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""supertype"":""Lens"",""events"":[""e""],""characters"":[""c""]}");
            lens["x_lens"] = new JObject { ["depth"] = 0 };
            var r = OWLens.Resolve(world, lens, OWLensReader.Owner(), new OWLensOptions { AsOf = 1200 });
            CollectionAssert.AreEqual(new[] { "c" }, r.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AfterAsOf] = 1 }, r.Cut,
                "the fence reads the element's 1300.0 as 1300");
            Assert.AreEqual(7, OWLens.DateOf(JObject.Parse(@"{""type"":""event"",""start_date"":7.0}")));
        }

        // changed_within (serial 82): a whole number of days from 1, refused by name otherwise, between undated and show
        // in the table's order. The fixture's keys cases do not carry it.
        [Test]
        public void ChangedWithin_IsAWholeNumberOfDaysFromOne()
        {
            Assert.IsNull(OWLens.CheckKeys(JObject.Parse(@"{""changed_within"":1}")));
            Assert.IsNull(OWLens.CheckKeys(JObject.Parse(@"{""changed_within"":null}")), "null is absent");
            foreach (var bad in new[] { "0", "-3", "1.5", "\"7\"", "true", "[7]" })
                Assert.AreEqual("changed_within", OWLens.CheckKeys(JObject.Parse(@"{""changed_within"":" + bad + "}")), bad);
            Assert.AreEqual("undated", OWLens.CheckKeys(JObject.Parse(@"{""undated"":""no"",""changed_within"":0}")), "undated first");
            Assert.AreEqual("changed_within", OWLens.CheckKeys(JObject.Parse(@"{""changed_within"":0,""show"":""no""}")), "before show");
            Assert.IsNull(OWLens.CheckKeys(JObject.Parse(@"{""types"":[""law/charter"",""Construct/Guide"",""construct/domain""]}")),
                "a reading aid's pair is a type word");
        }

        // changed_within reads the server's updated_at, or a folder element's server_updated_at or local_updated_at, the
        // newer; no time, no cut; never an anchor. Kills: the folder's local edit ignored, an element with no time cut.
        [Test]
        public void ChangedWithin_ReadsUpdatedAt_OrTheNewerOfAFoldersTwo_AndNeverAnAnchor()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""a"",""type"":""institution"",""name"":""Anchor"",""objects"":[""o1"",""o2"",""o3"",""o4""],""updated_at"":""2026-01-01T00:00:00Z""}"),
                JObject.Parse(@"{""id"":""o1"",""type"":""object"",""name"":""Fresh"",""updated_at"":""2026-10-26T00:00:00Z""}"),
                JObject.Parse(@"{""id"":""o2"",""type"":""object"",""name"":""Old on the server, edited here"",""server_updated_at"":""2026-09-01T00:00:00Z"",""local_updated_at"":""2026-10-25T00:00:00Z""}"),
                JObject.Parse(@"{""id"":""o3"",""type"":""object"",""name"":""Old both ways"",""server_updated_at"":""2026-09-01T00:00:00Z"",""local_updated_at"":""2026-09-02T00:00:00Z""}"),
                JObject.Parse(@"{""id"":""o4"",""type"":""object"",""name"":""No time""}"),
            };
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""supertype"":""Lens"",""institutions"":[""a""],""x_lens"":{""changed_within"":7}}");
            var options = new OWLensOptions { Clock = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero) };

            var r = OWLens.Resolve(world, lens, OWLensReader.Owner(), options);
            CollectionAssert.AreEquivalent(new[] { "a", "o1", "o2", "o4" }, r.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Stale] = 1 }, r.Cut);
        }

        // The TypeScript SDK 4.98.0's vector (test/lens.test.mjs; the Python package 0.80.0 holds the same): an element
        // exactly that many days old is kept, a second older is stale, an offset is read, a filter is cut too, every
        // refused value names the key, and null is absent. Kills: the edge read as stale, an offset ignored, a filter
        // left uncut, an unreadable time cut.
        [Test]
        public void ChangedWithin_KeepsTheExactEdge_CutsASecondOlder_ReadsAnOffset_AndCutsAFilter()
        {
            var clock = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""a"",""type"":""institution"",""name"":""Anchor"",""updated_at"":""2020-01-01T00:00:00Z"",""objects"":[""edge"",""past"",""none"",""bad""]}"),
                JObject.Parse(@"{""id"":""edge"",""type"":""object"",""name"":""Seven days"",""updated_at"":""2026-10-20T12:00:00Z""}"),
                JObject.Parse(@"{""id"":""past"",""type"":""object"",""name"":""Seven days and a second"",""updated_at"":""2026-10-20T11:59:59Z""}"),
                JObject.Parse(@"{""id"":""none"",""type"":""object"",""name"":""No time""}"),
                JObject.Parse(@"{""id"":""bad"",""type"":""object"",""name"":""Unreadable"",""updated_at"":""yesterday""}"),
            };
            JObject LensOn(string anchors, string xLens) => JObject.Parse(
                @"{""id"":""L"",""type"":""construct"",""name"":""L"",""supertype"":""Lens""" + anchors + @",""x_lens"":" + xLens + "}");
            var options = new OWLensOptions { Clock = clock };
            var anchored = @",""institutions"":[""a""]";

            var r = OWLens.Resolve(world, LensOn(anchored, @"{""changed_within"":7}"), OWLensReader.Owner(), options);
            CollectionAssert.AreEqual(new[] { "a", "edge", "bad", "none" }, r.Kept, "no readable time: never cut, sorted last");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Stale] = 1 }, r.Cut);

            world[2]["updated_at"] = "2026-10-20T13:00:00+01:00";
            CollectionAssert.IsEmpty(OWLens.Resolve(world, LensOn(anchored, @"{""changed_within"":7}"), OWLensReader.Owner(), options).Cut,
                "13:00+01:00 is the same instant as the edge");

            var f = OWLens.Resolve(world, LensOn("", @"{""types"":[""object""],""changed_within"":1}"), OWLensReader.Owner(), options);
            CollectionAssert.AreEqual(new[] { "bad", "none" }, f.Kept, "a filter is cut too");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Stale] = 2 }, f.Cut);

            foreach (var bad in new[] { "0", "-1", "1.5", "\"7\"", "true", "[7]" })
                Assert.AreEqual("changed_within",
                    OWLens.Resolve(world, LensOn(anchored, @"{""changed_within"":" + bad + "}"), OWLensReader.Owner()).RefusedKey, bad);
            Assert.IsNull(OWLens.Resolve(world, LensOn(anchored, @"{""changed_within"":null}"), OWLensReader.Owner(), options).RefusedKey);
        }

        // The TypeScript SDK 4.98.0's folder vector: a folder element reads the newer of its two sync stamps; a folder
        // stamp, where one reads, wins over updated_at; an unreadable one is no stamp. Kills: updated_at read over a
        // readable folder stamp, an unreadable folder stamp read as a time or as hiding updated_at.
        [Test]
        public void ChangedWithin_ReadsAFolderElement_ByTheNewerOfItsTwoSyncStamps()
        {
            var options = new OWLensOptions { Clock = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero) };
            var lens = JObject.Parse(@"{""id"":""L"",""type"":""construct"",""name"":""L"",""supertype"":""Lens"",""institutions"":[""a""],""x_lens"":{""changed_within"":7}}");
            bool Kept(string stamps)
            {
                var world = new List<JObject>
                {
                    JObject.Parse(@"{""id"":""a"",""type"":""institution"",""name"":""Anchor"",""objects"":[""o""]}"),
                    JObject.Parse(@"{""id"":""o"",""type"":""object"",""name"":""Folder copy""" + stamps + "}"),
                };
                return OWLens.Resolve(world, lens, OWLensReader.Owner(), options).Kept.Contains("o");
            }

            Assert.IsTrue(Kept(@",""server_updated_at"":""2026-09-01T00:00:00Z"",""local_updated_at"":""2026-10-26T00:00:00Z"""));
            Assert.IsTrue(Kept(@",""server_updated_at"":""2026-10-26T00:00:00Z"",""local_updated_at"":""2026-09-01T00:00:00Z"""));
            Assert.IsFalse(Kept(@",""server_updated_at"":""2026-09-01T00:00:00Z"",""local_updated_at"":""2026-09-02T00:00:00Z"""));
            Assert.IsFalse(Kept(@",""updated_at"":""2026-10-26T00:00:00Z"",""local_updated_at"":""2026-09-02T00:00:00Z"""),
                "a folder stamp, where one reads, wins over updated_at");
            Assert.IsTrue(Kept(@",""updated_at"":""2026-10-26T00:00:00Z"",""local_updated_at"":""soon"""),
                "an unreadable folder stamp is no stamp");
        }

        // -- The lens profile owner's rulings on readings no fixture case decided yet (each a case in its next serial) ---
        // Rulings 4 and 5 (an umbrella's children are not in its hop's known set; a cut lens id stays counted once
        // entered) were already this reader's: AnUmbrellasSiblingLens_ReachedByAChildsHop_IsKept_UnlessEnteredFirst.

        /// <summary>
        /// Ruling 1: a link reaches only an element of its field's target type. Ada's species names a Location and an
        /// Event's locations names Ada: neither is a hop, either way. Her location is. Kills: a hop that follows any id.
        /// </summary>
        [Test]
        public void Ruling1_ALinkReachesOnlyAnElementOfItsFieldsTargetType()
        {
            var world = new List<JObject>
            {
                Lens("L", "{}", @"""characters"":[""a""]"),
                Character("a", @"""species"":[""port""],""location"":""home"""),
                JObject.Parse(@"{""id"":""port"",""type"":""location"",""name"":""Port""}"),
                JObject.Parse(@"{""id"":""home"",""type"":""location"",""name"":""Home""}"),
                JObject.Parse(@"{""id"":""fair"",""type"":""event"",""name"":""Fair"",""locations"":[""a""]}"),
            };
            var r = OWLens.Resolve(world, "L", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "a", "home" }, r.Kept, "Ada and her location only");
            CollectionAssert.IsEmpty(r.Cut, "a wrongly typed id is no hop, so nothing to count");
        }

        /// <summary>
        /// Ruling 2: a generic pair links only when its type key names one of the 22, and then only to an element of that
        /// type. Kills: a Pin's <c>element_id</c> followed by its id alone.
        /// </summary>
        [Test]
        public void Ruling2_AGenericPairLinksOnlyByOneOfThe22()
        {
            var world = new List<JObject>
            {
                Lens("L", "{}", @"""characters"":[""a""]"),
                Character("a"),
                JObject.Parse(@"{""id"":""p1"",""type"":""pin"",""name"":""Dragon pin"",""element_type"":""dragon"",""element_id"":""a""}"),
                JObject.Parse(@"{""id"":""p2"",""type"":""pin"",""name"":""Ada's pin"",""element_type"":""character"",""element_id"":""a""}"),
                JObject.Parse(@"{""id"":""p3"",""type"":""pin"",""name"":""Wrong pin"",""element_type"":""location"",""element_id"":""a""}"),
                JObject.Parse(@"{""id"":""p4"",""type"":""pin"",""name"":""Bare pin"",""element_id"":""a""}"),
            };
            CollectionAssert.AreEqual(new[] { "a", "p2" }, OWLens.Resolve(world, "L", OWLensReader.Owner()).Kept,
                "only the pin whose pair names a Character");
        }

        /// <summary>
        /// The generic pair's type key compares exact against the 22 slugs, as stored, nothing folded: a stored
        /// <c>"Character"</c> or <c>" character "</c> links nothing, and a reader below the owner is served that pair
        /// unset. Kills: the type key trimmed or case-folded before it is matched.
        /// </summary>
        [Test]
        public void RulingA_AGenericPairsTypeKey_ComparesExact()
        {
            var world = new List<JObject>
            {
                Lens("L", "{}", @"""characters"":[""a""]"),
                Character("a"),
                JObject.Parse(@"{""id"":""p1"",""type"":""pin"",""name"":""Capital pin"",""element_type"":""Character"",""element_id"":""a""}"),
                JObject.Parse(@"{""id"":""p2"",""type"":""pin"",""name"":""Ada's pin"",""element_type"":""character"",""element_id"":""a""}"),
                JObject.Parse(@"{""id"":""p3"",""type"":""pin"",""name"":""Spaced pin"",""element_type"":"" character "",""element_id"":""a""}"),
            };
            CollectionAssert.AreEqual(new[] { "a", "p2" }, OWLens.Resolve(world, "L", OWLensReader.Owner()).Kept,
                "only the pin whose type key is the slug as written");
            var served = OWLens.ServedView(world, OWLensReader.Contributor()).ToDictionary(e => (string)e["id"]);
            foreach (var pin in new[] { "p1", "p3" })
            {
                Assert.AreEqual("", (string)served[pin]["element_type"], pin + ": served an unset pair");
                Assert.AreEqual(JTokenType.Null, served[pin]["element_id"].Type, pin + ": served an unset pair");
            }

            Assert.AreEqual("a", (string)served["p2"]["element_id"], "the slug as written is served whole");
        }

        /// <summary>
        /// Ruling 3: an anchor id no element holds is counted for the owner as <c>anchor_not_visible</c>, never skipped; so
        /// is one of another type than its field's. Below the owner nothing is counted. Kills: a dangling anchor skipped.
        /// </summary>
        [Test]
        public void Ruling3_ADanglingAnchorIsCountedForTheOwner()
        {
            var world = new List<JObject>
            {
                Lens("L", @"{""depth"":0}", @"""characters"":[""a"",""ghost"",""port""]"),
                Character("a"),
                JObject.Parse(@"{""id"":""port"",""type"":""location"",""name"":""Port""}"),
            };
            var r = OWLens.Resolve(world, "L", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "a" }, r.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 2 }, r.Cut,
                "the missing id and the Location under characters");
            CollectionAssert.IsEmpty(OWLens.Resolve(world, "L", OWLensReader.Contributor()).Cut, "a contributor is not told");

            // The lens's own id under characters is no lens of the reading (keel keys an anchor by its field's type).
            world[0] = Lens("L", @"{""depth"":0}", @"""characters"":[""L"",""a""]");
            r = OWLens.Resolve(world, "L", OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "a" }, r.Kept, "never in its own result");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 1 }, r.Cut,
                "the lens's id under characters, counted too");
        }

        /// <summary>
        /// Ruling 7: a known-of stub is shaped as keel serves it: the base and public fields kept (a public link redacted to
        /// what the reader sees), every other field at its unset value, extension keys dropped, <c>known_of</c> true.
        /// Kills: a stub that drops its unset fields, its public link, or <c>change_seq</c>.
        /// </summary>
        [Test]
        public void Ruling7_AStubKeepsBaseAndPublic_AndUnsetsTheRest()
        {
            var world = new List<JObject>
            {
                Lens("L", @"{""depth"":0}", @"""characters"":[""s""]"),
                JObject.Parse(@"{""id"":""s"",""type"":""character"",""name"":""Sable"",""description"":""Quiet."",""supertype"":""Smith"",
                    ""change_seq"":5,""physicality"":""Tall"",""height"":180,""species"":[""sp1"",""sp2""],""mentality"":""Calm"",
                    ""background"":""Secret."",""birth_date"":12,""birthplace"":""loc"",""friends"":[""sp1""],""x_custom"":""kept nowhere"",
                    ""created_by"":""m1"",""world"":""w1""}"),
                JObject.Parse(@"{""id"":""sp1"",""type"":""species"",""name"":""Elves""}"),
                JObject.Parse(@"{""id"":""sp2"",""type"":""species"",""name"":""Hidden kin""}"),
                JObject.Parse(@"{""id"":""loc"",""type"":""location"",""name"":""Forge""}"),
            };
            var row = OWLens.Resolve(world, "L", OWLensReader.Guest(new[] { "L", "sp1" }, new[] { "s" })).Rows.Single();
            var want = JObject.Parse(@"{""id"":""s"",""type"":""character"",""name"":""Sable"",""description"":""Quiet."",""supertype"":""Smith"",
                ""change_seq"":5,""physicality"":""Tall"",""height"":180,""species"":[""sp1""],""mentality"":"""",""background"":"""",
                ""birth_date"":null,""birthplace"":null,""friends"":[],""created_by"":null,""known_of"":true}");
            Assert.IsTrue(JToken.DeepEquals(want, row), "keel's stub: " + row.ToString(Formatting.None));
        }

        /// <summary>
        /// Ruling 11, as reversed by the profile's owner: two messages with one <c>created_at</c> keep the order of the list
        /// they came in, as the message profile already rules; the mail part never orders them by id. A tie listed in
        /// reverse id order stays as listed. Kills: a tie ordered by id.
        /// </summary>
        [Test]
        public void Ruling11_AMailTieKeepsTheListsOrder()
        {
            const string first = "m2", second = "m1";
            JObject Letter(string id) => new JObject
            {
                ["id"] = id, ["type"] = "narrative", ["name"] = id, ["supertype"] = "Message", ["narrator"] = "a",
                ["characters"] = new JArray("me"), ["created_at"] = "2026-10-20T12:00:00Z",
            };
            var world = new List<JObject>
            {
                Lens("L", @"{""types"":[""narrative/message""],""mail"":""all""}", null),
                Character("me"),
                Character("a"),
                Letter(first),
                Letter(second),
            };
            var options = new OWLensOptions { Clock = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero) };
            var r = OWLens.Resolve(world, "L", OWLensReader.Owner("me"), options);
            CollectionAssert.AreEqual(new[] { first, second }, r.Mail.Unread, "m2 before m1, as listed");
            world.Reverse(3, 2);
            CollectionAssert.AreEqual(new[] { second, first }, OWLens.Resolve(world, "L", OWLensReader.Owner("me"), options).Mail.Unread,
                "listed the other way, read the other way");
        }

        /// <summary>
        /// A single link stored as <c>""</c> is served as stored to every reader below the owner: <c>""</c>, never null,
        /// and never named in <c>x_served_trimmed</c>, since no id was left out (keel's redact strips only an id the reader
        /// cannot see). A Message whose <c>narrator</c> is <c>""</c> claims no hidden sender. Kills: <c>""</c> served as
        /// null, or marked trimmed.
        /// </summary>
        [Test]
        public void AnEmptySingleLink_IsServedAsStored_AndNeverMarkedTrimmed()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""o1"",""type"":""object"",""name"":""Cup"",""language"":""""}"),
                Character("c1"),
                JObject.Parse(@"{""id"":""m1"",""type"":""narrative"",""supertype"":""Message"",""name"":""Hi"",""narrator"":"""",""characters"":[""c1""]}"),
                Lens("L", @"{""depth"":0}", @"""objects"":[""o1""],""narratives"":[""m1""]"),
            };
            var readers = new[]
            {
                ("contributor", OWLensReader.Contributor()), ("guest", OWLensReader.Guest(new[] { "o1", "c1", "m1", "L" })),
                ("public", OWLensReader.Public()),
            };
            foreach (var (name, reader) in readers)
            {
                var served = OWLens.ServedView(world, reader);
                foreach (var (id, field) in new[] { ("m1", "narrator"), ("o1", "language") })
                {
                    var body = served.Single(e => (string)e["id"] == id);
                    Assert.AreEqual(JTokenType.String, body[field].Type, $"{name}: {id}.{field} served as \"\"");
                    Assert.AreEqual("", (string)body[field], $"{name}: {id}.{field}");
                    Assert.IsNull(body[OWMessage.ServedTrimmedKey], $"{name}: {id} carries no trimmed mark");
                }

                var rows = OWLens.Resolve(world, "L", reader).Rows;
                var message = rows.Single(r => (string)r["id"] == "m1");
                Assert.AreEqual("", (string)message["narrator"], $"{name}: the kept message's narrator");
                Assert.IsNull(message[OWMessage.ServedTrimmedKey], $"{name}: the kept message claims no hidden sender");
            }
        }

        /// <summary>
        /// An empty string is no id, so a lens whose anchor fields hold only empty strings names no anchor and is a filter,
        /// as rule 7 reads a lens with empty lists. One real id beside them anchors it, and a served lens whose
        /// <c>x_served_trimmed</c> names an anchor field stays anchored. Kills: <c>[""]</c> read as naming an anchor.
        /// </summary>
        [Test]
        public void ALensWhoseAnchorFieldsHoldOnlyEmptyStrings_NamesNoAnchor_AndIsAFilter()
        {
            var world = new List<JObject> { JObject.Parse(@"{""id"":""e03"",""type"":""law"",""name"":""Law""}"), Character("a") };
            CollectionAssert.AreEqual(new[] { "a", "e03" },
                OWLens.Resolve(world, Lens("L", "{}", @"""narratives"":[""""]"), OWLensReader.Owner()).Kept, "a filter");
            CollectionAssert.AreEqual(new[] { "a", "e03" },
                OWLens.Resolve(world, Lens("L", "{}", @"""characters"":["""",""""],""laws"":[""""]"), OWLensReader.Public()).Kept,
                "a filter, for any reader");
            CollectionAssert.AreEqual(new[] { "a" },
                OWLens.Resolve(world, Lens("L", "{}", @"""characters"":["""",""a""]"), OWLensReader.Owner()).Kept, "one id anchors it");
            CollectionAssert.IsEmpty(OWLens.Resolve(world,
                    Lens("L", "{}", @"""narratives"":[""""],""x_served_trimmed"":[""narratives""]"), OWLensReader.Served()).Kept,
                "an anchor field the server trimmed still names an anchor");
        }

        /// <summary>
        /// A row is the body as the wire serves it to that reader, and a served body of an element under the hidden mark
        /// carries <c>hidden: true</c>: the owner's full row of a hidden element, by the world's list or by its own key;
        /// below the owner only a guest's row of its own hidden creation. A <c>name</c> row never carries
        /// it. The world stays as it was.
        /// Kills: the row as stored, the world's list left off the row.
        /// </summary>
        [Test]
        public void AnOwnersFullRowOfAHiddenElement_CarriesHiddenTrue_AsServed()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""e05"",""type"":""collective"",""name"":""Guild""}"),
                JObject.Parse(@"{""id"":""e06"",""type"":""collective"",""name"":""Choir"",""hidden"":true}"),
                Lens("L", "{}", null),
            };
            var before = new JArray(world.Select(e => e.DeepClone())).ToString(Formatting.None);
            var options = new OWLensOptions { HiddenIds = new[] { "e05" } };
            var rows = OWLens.Resolve(world, "L", OWLensReader.Owner(), options).Rows;
            Assert.AreEqual(@"[{""id"":""e05"",""type"":""collective"",""name"":""Guild"",""hidden"":true},{""id"":""e06"",""type"":""collective"",""name"":""Choir"",""hidden"":true}]",
                new JArray(rows).ToString(Formatting.None), "the world's list and the element's own key, one mark");
            var guest = OWLens.Resolve(world, "L", OWLensReader.Guest(new[] { "L", "e05" }), options).Rows;
            Assert.AreEqual(@"[{""id"":""e05"",""type"":""collective"",""name"":""Guild"",""hidden"":true}]",
                new JArray(guest).ToString(Formatting.None), "a guest's own hidden creation");
            world[2] = Lens("L", @"{""show"":""name""}", null);
            Assert.IsNull(OWLens.Resolve(world, "L", OWLensReader.Owner(), options).Rows[0]["hidden"], "a name row");
            world[2] = Lens("L", "{}", null);
            Assert.AreEqual(before, new JArray(world.Select(e => e.DeepClone())).ToString(Formatting.None), "the world is not changed");
        }

        /// <summary>
        /// Hidden beats known_of: a hidden element named in a guest's <c>known_of</c> that is not its own creation is not
        /// in that guest's view, so no stub, no row, no count and no mark; a hidden id in <c>whole</c> stays its own
        /// creation. A filter keeps nothing of it; an anchor on it is one the guest cannot see, counted only for the owner
        /// reading as that guest; a link to it is left out of the guest's served list (no gap), no edge, counted only for
        /// the owner as <c>not_visible</c>. Kills: the hidden element kept as a stub.
        /// </summary>
        [Test]
        public void AHiddenElementInAGuestsKnownOf_IsNotInItsView_NoStubNoRowNoCountNoMark()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""e05"",""type"":""character"",""name"":""Heir"",""hidden"":true}"),
                JObject.Parse(@"{""id"":""c1"",""type"":""character"",""name"":""Ada"",""friends"":[""e05"",""e06""]}"),
                JObject.Parse(@"{""id"":""e06"",""type"":""character"",""name"":""Brin""}"),
                Lens("F", @"{""types"":[""character""]}", null),
                Lens("A", @"{""depth"":0}", @"""characters"":[""e05""]"),
            };
            var guest = OWLensReader.Guest(new[] { "F", "A", "c1", "e06" }, new[] { "e05" });
            var filter = OWLens.Resolve(world, "F", guest);
            CollectionAssert.AreEqual(new[] { "c1", "e06" }, filter.Kept, "a filter keeps no stub of it");
            CollectionAssert.IsEmpty(filter.Cut);
            var anchored = OWLens.Resolve(world, "A", guest);
            CollectionAssert.IsEmpty(anchored.Kept, "no stub row");
            CollectionAssert.IsEmpty(anchored.Cut, "nothing counted for the guest");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 1 },
                OWLens.Resolve(world, "A", guest.WithOwnerCounts()).Cut, "the owner reading as the guest counts the anchor");
            var served = OWLens.ServedView(world, guest);
            Assert.IsFalse(served.Any(b => (string)b["id"] == "e05"), "not in the guest's view");
            Assert.AreEqual(@"[""e06""]", served.Single(b => (string)b["id"] == "c1")["friends"].ToString(Formatting.None),
                "left out of the guest's served list");
            var own = OWLens.Resolve(world, "F", OWLensReader.Guest(new[] { "F", "e05" })).Rows;
            Assert.AreEqual(true, (bool)own.Single()["hidden"], "a hidden id in whole stays its own creation, marked");
        }

        /// <summary>
        /// keel serves no <c>hidden</c> key on a body no mark hides, so a stored <c>hidden: false</c> is dropped from the
        /// row, for the owner and every reader below it. Kills: the stored key carried onto the row.
        /// </summary>
        [Test]
        public void AStoredHiddenFalse_IsDropped_FromTheRow()
        {
            var world = new List<JObject>
            {
                JObject.Parse(@"{""id"":""e05"",""type"":""collective"",""name"":""Guild"",""hidden"":false}"),
                Lens("L", "{}", null),
            };
            foreach (var (name, reader) in new[]
                     {
                         ("owner", OWLensReader.Owner()), ("contributor", OWLensReader.Contributor()),
                         ("guest", OWLensReader.Guest(new[] { "L", "e05" })), ("public", OWLensReader.Public()),
                     })
            {
                Assert.AreEqual(@"[{""id"":""e05"",""type"":""collective"",""name"":""Guild""}]",
                    new JArray(OWLens.Resolve(world, "L", reader).Rows).ToString(Formatting.None), name);
            }
        }

        /// <summary>
        /// The cut counts each element once under its first reason, and a kept element is never also counted as cut; an
        /// anchor resolves by its field's type and the id. One id an umbrella and its child anchor under two wrong types
        /// (a Location under <c>characters</c>, then <c>objects</c>) is one element, counted once; one the umbrella
        /// anchors under the wrong type and the child under its own is kept and not counted, and so the other way round.
        /// Guards what this reader already does (it keys the cut by id). Kills: the cut keyed by (type, id).
        /// </summary>
        [Test]
        public void TheCutCountsEachElementOnce_AndAKeptElementIsNeverAlsoCut()
        {
            List<JObject> World(string childLinks) => new List<JObject>
            {
                Lens("C", @"{""depth"":0}", childLinks),
                JObject.Parse(@"{""id"":""x"",""type"":""location"",""name"":""Harbour""}"),
            };
            var r = OWLens.Resolve(World(@"""objects"":[""x""]"), Lens("U", @"{""depth"":0}", @"""characters"":[""x""],""constructs"":[""C""]"),
                OWLensReader.Owner());
            CollectionAssert.IsEmpty(r.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 1 }, r.Cut, "one element, once");
            r = OWLens.Resolve(World(@"""locations"":[""x""]"), Lens("U", @"{""depth"":0}", @"""characters"":[""x""],""constructs"":[""C""]"),
                OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "x" }, r.Kept);
            CollectionAssert.IsEmpty(r.Cut, "kept by the child, so never also cut");
            r = OWLens.Resolve(World(@"""characters"":[""x""]"), Lens("U", @"{""depth"":0}", @"""locations"":[""x""],""constructs"":[""C""]"),
                OWLensReader.Owner());
            CollectionAssert.AreEqual(new[] { "x" }, r.Kept);
            CollectionAssert.IsEmpty(r.Cut, "kept by the umbrella, the child's wrong type not counted");
        }

        /// <summary>
        /// Counts are per reader: below the owner an element whose owner-side reason was <c>anchor_not_visible</c> (a
        /// Location the umbrella names under <c>characters</c>) and which a child cuts by <c>types</c> is counted under
        /// the child's reason; for the owner <c>anchor_not_visible</c> comes first and it is counted once there. Guards
        /// what this reader already does. Kills: the unseen anchor recorded for every reader and dropped at the count.
        /// </summary>
        [Test]
        public void BelowTheOwner_AnUnseenAnchorACutChildReaches_IsCountedUnderTheChildsReason()
        {
            var world = new List<JObject>
            {
                Lens("U", @"{""depth"":0}", @"""characters"":[""x""],""constructs"":[""C""]"),
                Lens("C", @"{""types"":[""character""]}", @"""characters"":[""y""]"),
                Character("y", @"""birthplace"":""x"""),
                JObject.Parse(@"{""id"":""x"",""type"":""location"",""name"":""Harbour""}"),
            };
            var contributor = OWLens.Resolve(world, "U", OWLensReader.Contributor());
            CollectionAssert.AreEqual(new[] { "y" }, contributor.Kept);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Types] = 1 }, contributor.Cut, "the child's reason");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.Types] = 1 },
                OWLens.Resolve(world, "U", OWLensReader.Public()).Cut, "the public reader's too");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 1 },
                OWLens.Resolve(world, "U", OWLensReader.Owner()).Cut, "the owner's first reason, once");
        }

        /// <summary>
        /// A note goes with its anchor: an anchor that reaches nothing (a Language named under <c>constructs</c>) carries
        /// its note onto no row, even where another path (a child filter) keeps the element, for the owner and below it;
        /// and the note is not counted ignored. Guards what this reader already does. Kills: an unseen id among the
        /// anchors whose notes ride.
        /// </summary>
        [Test]
        public void ANoteGoesWithItsAnchor_AnAnchorThatReachesNothingCarriesItOntoNoRow()
        {
            var world = new List<JObject>
            {
                Lens("U", @"{""notes"":{""e08"":""the old tongue""}}", @"""constructs"":[""e08"",""L1""]"),
                Lens("L1", "{}", null),
                JObject.Parse(@"{""id"":""e08"",""type"":""language"",""name"":""Old""}"),
            };
            foreach (var (name, reader) in new[] { ("owner", OWLensReader.Owner()), ("contributor", OWLensReader.Contributor()) })
            {
                var r = OWLens.Resolve(world, "U", reader);
                CollectionAssert.AreEqual(new[] { "e08" }, r.Kept, name + ": kept by the child");
                Assert.IsNull(r.Rows[0]["note"], name + ": no note rides");
                Assert.AreEqual(0, r.NotesIgnored, name + ": a note on an anchor is never counted ignored");
            }
        }

        /// <summary>
        /// Visited never comes before visibility, for any anchor: a hidden lens in its own <c>constructs</c>, read as a
        /// body through a contributor's view, counts itself for the owner as <c>anchor_not_visible</c>; so does a child
        /// lens pointing back at its hidden umbrella, its own objects kept. Guards what this reader already does. Kills:
        /// an anchor already visited skipped before it is counted.
        /// </summary>
        [Test]
        public void AnUnseenAnchor_IsCountedBeforeTheVisitedCheck_TheLensItselfIncluded()
        {
            var reader = OWLensReader.Contributor().WithOwnerCounts();
            var self = JObject.Parse(@"{""id"":""L0"",""type"":""construct"",""name"":""L0"",""supertype"":""Lens"",""hidden"":true,""constructs"":[""L0""],""x_lens"":{}}");
            var r = OWLens.Resolve(new List<JObject> { self }, self, reader);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 1 }, r.Cut, "the lens's own id");

            var umbrella = JObject.Parse(@"{""id"":""U"",""type"":""construct"",""name"":""U"",""supertype"":""Lens"",""hidden"":true,""constructs"":[""C""],""x_lens"":{""depth"":0}}");
            var world = new List<JObject>
            {
                umbrella,
                Lens("C", @"{""depth"":0}", @"""constructs"":[""U""],""objects"":[""o1""]"),
                JObject.Parse(@"{""id"":""o1"",""type"":""object"",""name"":""Cup""}"),
            };
            r = OWLens.Resolve(world, umbrella, reader);
            CollectionAssert.AreEqual(new[] { "o1" }, r.Kept, "the child's own objects");
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { [OWLensCut.AnchorNotVisible] = 1 }, r.Cut, "the hidden umbrella, once");
        }

        /// <summary>The case's lens, a body or the id of a lens in the world.</summary>
        private static OWLensResult ResolveCase(List<JObject> world, JObject c, OWLensReader reader, OWLensOptions options)
        {
            var lens = c["lens"];
            return lens.Type == JTokenType.String
                ? OWLens.Resolve(world, (string)lens, reader, options)
                : OWLens.Resolve(world, (JObject)lens, reader, options);
        }

        /// <summary>The fixture world's elements, each its hidden ids keep carrying the hidden mark.</summary>
        private static List<JObject> WorldOf(JObject fixtureWorld)
        {
            var hidden = new HashSet<string>(Strings(fixtureWorld["hidden"]));
            var list = new List<JObject>();
            foreach (var e in fixtureWorld["elements"].Cast<JObject>())
            {
                var copy = (JObject)e.DeepClone();
                if (hidden.Contains((string)copy["id"])) copy[OWPayload.Hidden] = true;
                list.Add(copy);
            }

            return list;
        }

        /// <summary>A fixture reader: its rung, its character, a guest's lists; <c>as</c> is the owner reading as another.</summary>
        private static OWLensReader ReaderOf(JObject fixtureWorld, string name)
        {
            var r = (JObject)fixtureWorld["readers"][name];
            if (r.TryGetValue("as", out var asName)) return ReaderOf(fixtureWorld, (string)asName).WithOwnerCounts();
            var character = r["character"]?.Type == JTokenType.String ? (string)r["character"] : null;
            switch ((string)r["rung"])
            {
                case "owner": return OWLensReader.Owner(character);
                case "contributor": return OWLensReader.Contributor(character);
                case "public": return OWLensReader.Public();
                case "guest": return OWLensReader.Guest(Strings(r["whole"]), Strings(r["known_of"]), character);
                default: throw new ArgumentException("a fixture reader of unknown rung: " + r["rung"]);
            }
        }

        private static OWLensOptions OptionsOf(JObject c, JObject fixtureWorld) => new OWLensOptions
        {
            AsOf = c.TryGetValue("as_of", out var asOf) ? (int?)asOf : null,
            Now = fixtureWorld["now"] == null || fixtureWorld["now"].Type == JTokenType.Null ? null : (int?)fixtureWorld["now"],
            Clock = c.TryGetValue("clock", out var clock)
                ? DateTimeOffset.Parse((string)clock, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
                : (DateTimeOffset?)null,
            Members = fixtureWorld["members"] as JArray,
        };

        /// <summary>A result in the fixture's words: kept, cut (a reason with no count absent), rows, notes_ignored, mail.</summary>
        private static JObject Json(OWLensResult r)
        {
            var o = new JObject
            {
                ["kept"] = new JArray(r.Kept),
                ["cut"] = Counted(r.Cut),
                ["rows"] = new JArray(r.Rows.Select(x => x.DeepClone())),
                ["notes_ignored"] = r.NotesIgnored,
            };
            if (r.Mail != null)
            {
                o["mail"] = new JObject
                {
                    ["unread"] = new JArray(r.Mail.Unread),
                    ["read"] = new JArray(r.Mail.Read),
                    ["rows"] = new JArray(r.Mail.Rows.Select(x => x.DeepClone())),
                    ["cut"] = Counted(r.Mail.Cut),
                    ["totals"] = new JObject { ["unread"] = r.Mail.TotalUnread, ["read"] = r.Mail.TotalRead },
                };
            }

            return o;
        }

        private static JObject Counted(IReadOnlyDictionary<string, int> cut)
        {
            var o = new JObject();
            foreach (var pair in cut.Where(p => p.Value > 0)) o[pair.Key] = pair.Value;
            return o;
        }

        private static List<string> Strings(JToken token) =>
            token is JArray array ? array.Select(t => (string)t).ToList() : new List<string>();

        private static List<string> Maybe(JToken token) => token is JArray array ? array.Select(t => (string)t).ToList() : null;
    }
}
