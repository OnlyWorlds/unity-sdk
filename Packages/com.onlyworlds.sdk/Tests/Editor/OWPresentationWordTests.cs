using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// A colour word not in <c>colors.order</c> fails loudly, naming the word, and never draws a default colour: the lens
    /// profile's Nodes bullet and <c>presentation.json</c>'s <c>_meta.groups</c> line, schema-dist serial 87. Each test
    /// reads a sidecar from its text (<see cref="OWPresentation.LoadText"/>), the package's own with one word changed, so
    /// it runs with or without the engine; the shipped file is put back after each.
    /// </summary>
    public class OWPresentationWordTests
    {
        private static JObject Shipped()
        {
            var root = TestPackage.Root();
            Assert.IsNotNull(root, "the package was not found");
            return JObject.Parse(File.ReadAllText(Path.Combine(root, "Runtime", "Resources", "ow-presentation.json")));
        }

        /// <summary>Reads the sidecar; the colours need the engine's parser, so a run without one stops after the words.</summary>
        private static void Load(JObject sidecar)
        {
            try
            {
                OWPresentation.LoadText(sidecar.ToString());
            }
            catch (MissingMethodException)
            {
                // No engine behind ColorUtility: the order and the type table are read before the colours.
            }
        }

        [TearDown]
        public void TheShippedSidecar_IsPutBack() => OWPresentation.Reload();

        [Test]
        public void TheShippedSidecar_Loads_EveryTypesWordInTheOrder()
        {
            var sidecar = Shipped();
            Assert.DoesNotThrow(() => Load(sidecar));
            var order = ((JArray)sidecar["colors"]["order"]).ToObject<string[]>();
            foreach (var type in OWElementTypes.Slugs)
            {
                CollectionAssert.Contains(order, OWPresentation.GroupOf(type), type);
#pragma warning disable CS0618 // the old name, held to the same word
                CollectionAssert.Contains(order, OWPresentation.FamilyOf(type), type);
#pragma warning restore CS0618
            }
        }

        [Test]
        public void AnOldWordOnAType_FailsTheLoad_NamingTheWord()
        {
            var sidecar = Shipped();
            sidecar["types"]["construct"]["family"] = "abstract";
            var ex = Assert.Catch<Exception>(() => Load(sidecar));
            StringAssert.Contains("'abstract'", ex.Message);
            StringAssert.Contains("construct", ex.Message);
        }

        [Test]
        public void AfterAFailedLoad_NoReaderDrawsADefault()
        {
            var sidecar = Shipped();
            sidecar["types"]["construct"]["family"] = "abstract";
            Assert.Catch<Exception>(() => Load(sidecar));

            // The failure stays: every reader of the sidecar meets it again, none answers a family or a colour.
#pragma warning disable CS0618 // the old name meets the failure too
            StringAssert.Contains("'abstract'", Assert.Catch<Exception>(() => OWPresentation.FamilyOf("character")).Message);
#pragma warning restore CS0618
            StringAssert.Contains("'abstract'", Assert.Catch<Exception>(() => OWPresentation.ColorFor("construct", true)).Message);
            StringAssert.Contains("'abstract'", Assert.Catch<Exception>(() => OWPresentation.IconOf("construct")).Message);
        }

        [Test]
        public void AGraphNode_NeverCarriesAWordNotInTheOrder()
        {
            var sidecar = Shipped();
            sidecar["types"]["construct"]["family"] = "abstract";
            Assert.Catch<Exception>(() => Load(sidecar));
            var world = new JArray(new JObject { ["id"] = "k1", ["type"] = "construct", ["name"] = "The Loom" });
            var ex = Assert.Catch<Exception>(() => OWGraph.Read(world, OWLensReader.Owner()));
            StringAssert.Contains("'abstract'", ex.Message);
        }

        [Test]
        public void ASidecarWithNoOrder_FailsTheLoad_NamingAWord()
        {
            var sidecar = Shipped();
            ((JObject)sidecar["colors"]).Remove("order");
            var ex = Assert.Catch<Exception>(() => Load(sidecar));
            StringAssert.Contains("not in colors.order", ex.Message);
        }

        [Test]
        public void AWordInTheOrderWithNoColour_FailsTheLoad_NamingTheWord()
        {
            var sidecar = Shipped();
            ((JObject)sidecar["colors"]["families"]).Remove("pink");
            var ex = Assert.Catch<Exception>(() => Load(sidecar));
            StringAssert.Contains("'pink'", ex.Message);
            StringAssert.Contains("colors.families", ex.Message);
            // The types drawn in that word are named, so a reader knows what would have been drawn grey.
            foreach (var type in new[] { "construct", "language", "law", "title", "trait" })
                StringAssert.Contains(type, ex.Message);

            // Never a grey for a known type: the failure is the answer for every type, a pink one and any other.
            StringAssert.Contains("'pink'", Assert.Catch<Exception>(() => OWPresentation.ColorFor("language", true)).Message);
            StringAssert.Contains("'pink'", Assert.Catch<Exception>(() => OWPresentation.ColorFor("character", false)).Message);
        }

        [Test]
        public void AColourMissingOneMode_FailsTheLoad_NamingTheWordAndTheMode()
        {
            var sidecar = Shipped();
            ((JObject)sidecar["colors"]["families"]["blue"]).Remove("dark");
            var ex = Assert.Catch<Exception>(() => Load(sidecar));
            StringAssert.Contains("'blue'", ex.Message);
            StringAssert.Contains("dark", ex.Message);
        }

        [Test]
        public void AHexThatWillNotParse_FailsTheLoad_NamingTheHex()
        {
            foreach (var hex in new[] { "#c98z00", "c98500", "#c985", "", "amber" })
            {
                var sidecar = Shipped();
                sidecar["colors"]["families"]["amber"]["dark"] = hex;
                var ex = Assert.Catch<Exception>(() => Load(sidecar), hex);
                StringAssert.Contains("'" + hex + "'", ex.Message, hex);
                StringAssert.Contains("'amber'", ex.Message, hex);
                StringAssert.Contains("event", ex.Message, hex);
                OWPresentation.Reload();
            }
        }

        [Test]
        public void AColourAskedForAWordNotInTheOrder_FailsNamingTheWord()
        {
            Load(Shipped());
#pragma warning disable CS0618 // the old name, deprecated since 0.58.0
            StringAssert.Contains("'agents'", Assert.Catch<Exception>(() => OWPresentation.ColorOfFamily("agents")).Message);
            Assert.IsNull(OWPresentation.ColorOfFamily(null), "no word is no colour, not a failure");
#pragma warning restore CS0618
        }

        // ---- npm 5.0's names: groupOf, GROUP_ORDER, GROUP_COLORS (the TypeScript SDK's src/v2/palette.ts) ----
        // The engine's family names always answered the colour words, which are npm's group words; npm's own familyOf
        // answers the pre-4.102.0 words (agents, world, abstract, temporal). So the group names are the twins.

        [Test]
        public void GroupOrder_IsTheSidecarsOrder_TheFourColourWords()
        {
            var sidecar = Shipped();
            Load(sidecar);
            CollectionAssert.AreEqual(((JArray)sidecar["colors"]["order"]).ToObject<string[]>(), OWPresentation.GroupOrder);
            CollectionAssert.AreEqual(new[] { "blue", "green", "pink", "amber" }, OWPresentation.GroupOrder);
        }

        [Test]
        public void GroupOf_AnswersEachTypesColourWord_AndNullForNoType()
        {
            var sidecar = Shipped();
            Load(sidecar);
            foreach (var type in OWElementTypes.Slugs)
                Assert.AreEqual((string)sidecar["types"][type]["family"], OWPresentation.GroupOf(type), type);
            Assert.AreEqual("blue", OWPresentation.GroupOf("character"));
            Assert.AreEqual("green", OWPresentation.GroupOf("location"));
            Assert.AreEqual("pink", OWPresentation.GroupOf("construct"));
            Assert.AreEqual("amber", OWPresentation.GroupOf("ability"));
            Assert.IsNull(OWPresentation.GroupOf("not_a_type"));
            Assert.IsNull(OWPresentation.GroupOf(null));
        }

        [Test]
        public void GroupOf_FailsAsTheLoadFailed_NamingTheWord()
        {
            var sidecar = Shipped();
            sidecar["types"]["construct"]["family"] = "abstract";
            Assert.Catch<Exception>(() => Load(sidecar));
            StringAssert.Contains("'abstract'", Assert.Catch<Exception>(() => OWPresentation.GroupOf("character")).Message);
            StringAssert.Contains("'abstract'", Assert.Catch<Exception>(() => OWPresentation.ColorOfGroup("blue")).Message);
        }

        [Test]
        public void ColorOfGroup_FailsForAWordNotInTheOrder_AndIsNullForNoWord()
        {
            Load(Shipped());
            StringAssert.Contains("'agents'", Assert.Catch<Exception>(() => OWPresentation.ColorOfGroup("agents")).Message);
            Assert.IsNull(OWPresentation.ColorOfGroup(null), "no word is no colour, not a failure");
        }

        [Test]
        public void ColorOfGroup_AndColorOfType_AnswerTheSameColours_AsTheFamilyNames()
        {
            // Without the engine the colours do not parse and every answer is null; in the Editor each is the hex pair.
            Load(Shipped());
#pragma warning disable CS0618 // the old names, compared with the new
            foreach (var word in OWPresentation.GroupOrder)
            {
                OWGroupColor? group = OWPresentation.ColorOfGroup(word);
                var family = OWPresentation.ColorOfFamily(word);
                Assert.AreEqual(family.HasValue, group.HasValue, word);
                if (group.HasValue)
                {
                    Assert.AreEqual(family.Value.Light, group.Value.Light, word);
                    Assert.AreEqual(family.Value.Dark, group.Value.Dark, word);
                }
            }
            foreach (var type in OWElementTypes.Slugs)
            {
                Assert.AreEqual(OWPresentation.FamilyOf(type), OWPresentation.GroupOf(type), type);
                OWGroupColor? byType = OWPresentation.ColorOfType(type);
                Assert.AreEqual(OWPresentation.ColorOfGroup(OWPresentation.GroupOf(type)), byType, type);
                OWFamilyColor? asBefore = OWPresentation.ColorOfType(type); // a caller that typed the old struct still compiles
                Assert.AreEqual(byType.HasValue, asBefore.HasValue, type);
            }
#pragma warning restore CS0618
        }

        [Test]
        public void OWGroupColor_For_PicksTheSurfacesVariant()
        {
            var c = new OWGroupColor { Light = new UnityEngine.Color(1f, 0f, 0f, 1f), Dark = new UnityEngine.Color(0f, 0f, 1f, 1f) };
            Assert.AreEqual(c.Light, c.For(darkSurface: false));
            Assert.AreEqual(c.Dark, c.For(darkSurface: true));
        }

        [Test]
        public void TheFamilyNames_AreDeprecatedInFavourOfTheGroupNames_AsAWarning()
        {
            var sdk = typeof(OWPresentation);
            var family = sdk.Assembly.GetType("OnlyWorlds.Sdk.OWFamilyColor");
            var marks = new (System.Reflection.MemberInfo member, string use)[]
            {
                (sdk.GetMethod("FamilyOf"), "Use GroupOf"),
                (sdk.GetProperty("FamilyOrder"), "Use GroupOrder"),
                (sdk.GetMethod("ColorOfFamily"), "Use ColorOfGroup"),
                (family, "Use OWGroupColor"),
                (family.GetField("Light"), "Use OWGroupColor.Light"),
                (family.GetField("Dark"), "Use OWGroupColor.Dark"),
                (family.GetMethod("For"), "Use OWGroupColor.For"),
                (family.GetMethod("op_Implicit"), "Use OWGroupColor"),
            };
            foreach (var (member, use) in marks)
            {
                Assert.IsNotNull(member, use);
                var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(member, typeof(ObsoleteAttribute), false);
                Assert.IsNotNull(obsolete, $"{member.Name} is not marked [Obsolete]");
                Assert.IsFalse(obsolete.IsError, $"{member.Name}: a warning, not an error");
                StringAssert.StartsWith("Since 0.58.0. Leaves in 1.0. " + use, obsolete.Message, member.Name);
            }
            foreach (var name in new[] { "GroupOf", "GroupOrder", "ColorOfGroup", "ColorOfType" })
                Assert.IsNull(Attribute.GetCustomAttribute(sdk.GetMember(name)[0], typeof(ObsoleteAttribute)), name + " is not marked");
            Assert.IsNull(Attribute.GetCustomAttribute(typeof(OWGroupColor), typeof(ObsoleteAttribute)), "OWGroupColor is not marked");
        }
    }
}
