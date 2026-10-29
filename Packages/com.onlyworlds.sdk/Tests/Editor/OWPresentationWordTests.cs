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
                CollectionAssert.Contains(order, OWPresentation.FamilyOf(type), type);
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
            StringAssert.Contains("'abstract'", Assert.Catch<Exception>(() => OWPresentation.FamilyOf("character")).Message);
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
                OWPresentation.Reload();
            }
        }

        [Test]
        public void AColourAskedForAWordNotInTheOrder_FailsNamingTheWord()
        {
            Load(Shipped());
            StringAssert.Contains("'agents'", Assert.Catch<Exception>(() => OWPresentation.ColorOfFamily("agents")).Message);
            Assert.IsNull(OWPresentation.ColorOfFamily(null), "no word is no colour, not a failure");
        }
    }
}
