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
    /// <c>Tests/Editor/Fixtures/lens/</c> from dist.81), replayed case by case through <see cref="OWLens"/>: the pair and
    /// the template (<see cref="OWLens.IsLens"/>, <see cref="OWLens.IsTemplate"/>), the legal values of <c>x_lens</c>
    /// (<see cref="OWLens.CheckKeys"/>), every resolution over the fixture's four worlds and their readers
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
        /// <summary>The fixture's sha256 as dist.81's MANIFEST.json records it.</summary>
        private const string DistSha256 = "4b65b474f4174d54705f4f76e910f124bb99423788e0bf643b4d55bb36b85499";

        /// <summary>The fixture's cases by kind, as dist.81 holds them (122 in all; the graph kind is the graph reader's).</summary>
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>
        {
            ["shape"] = 6, ["keys"] = 23, ["resolve"] = 51, ["invariant"] = 4, ["uses"] = 4, ["graph"] = 34,
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
                "the vendored fixture is not dist.81's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void TheFixture_HoldsDist81sCases()
        {
            Assert.IsNotNull(Fixture, "the vendored lens fixture was not found");
            var cases = Fixture["cases"].Cast<JObject>().ToList();
            Assert.AreEqual(122, cases.Count, "dist.81's fixture holds 122 cases");
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
