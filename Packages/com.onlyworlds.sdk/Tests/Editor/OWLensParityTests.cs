using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Readings where the lens reader once answered differently from the TypeScript SDK (4.96.0) and the Python package
    /// (0.79.1), which agree with each other. Each case is a small world cut down from a generated reading that showed
    /// the difference; its answer is the Python package's <c>resolve_lens</c> (or <c>world_graph</c>,
    /// <c>lenses_in_use</c>) on the same input.
    /// </summary>
    public class OWLensParityTests
    {
        // --- The lens read, and every lens entered, is never in its own result (profile rule 3) ---

        [Test]
        public void ALensThatAnchorsItself_IsNotInItsOwnResult()
        {
            var r = Read(@"[{""id"":""L"",""type"":""construct"",""name"":""L"",""supertype"":""Lens"",""x_lens"":{},
                ""characters"":[""L"",""a""]},{""id"":""a"",""type"":""object"",""name"":""a""}]", "L", OWLensReader.Owner());
            Same(@"[""a""]", Kept(r));
            Same("{}", Cut(r));
        }

        [Test]
        public void AnUmbrellasHop_NeverReachesItsOwnChild()
        {
            var r = Read(@"[{""id"":""U"",""type"":""construct"",""name"":""U"",""supertype"":""Lens"",""x_lens"":{""types"":[""character""]},
                ""characters"":[""c1""],""constructs"":[""C""]},
                {""id"":""C"",""type"":""construct"",""name"":""C"",""supertype"":""Lens"",""x_lens"":{""depth"":0},""characters"":[""c1""]},
                {""id"":""c1"",""type"":""character"",""name"":""c1""}]", "U", OWLensReader.Owner());
            Same(@"[""c1""]", Kept(r));
            Same("{}", Cut(r), "the child lens C, reached by the umbrella's hop, is not counted as cut by types");
        }

        // --- A link to an id in no element is a dangling link, not a visibility fact ---

        [Test]
        public void ALinkToAnIdInNoElement_IsNotCountedNotVisible()
        {
            var r = Read(@"[{""id"":""L"",""type"":""construct"",""name"":""L"",""supertype"":""Lens"",""x_lens"":{},""objects"":[""o""]},
                {""id"":""o"",""type"":""object"",""name"":""o"",""location"":""missing""}]", "L", OWLensReader.Owner());
            Same(@"[""o""]", Kept(r));
            Same("{}", Cut(r));
        }

        // --- An anchor in no element is skipped, never counted ---

        [Test]
        public void AnAnchorInNoElement_IsSkippedUncounted()
        {
            var r = Read(@"[{""id"":""L"",""type"":""construct"",""name"":""L"",""supertype"":""Lens"",""x_lens"":{""depth"":0},
                ""characters"":[""a"",""missing""]},{""id"":""a"",""type"":""character"",""name"":""a""}]", "L", OWLensReader.Owner());
            Same(@"[""a""]", Kept(r));
            Same("{}", Cut(r));
        }

        // --- Helpers ---

        /// <summary>
        /// A graph node's family and icon are the presentation sidecar's; outside the Editor <c>Resources</c> loads
        /// nothing, so the vendored sidecar is read from its text (as <see cref="OWGraphTests"/> does), and put back after.
        /// </summary>
        [OneTimeSetUp]
        public void TheSidecar_IsRead()
        {
            string family = null;
            try
            {
                family = OWPresentation.FamilyOf("character");
            }
            catch (System.Exception)
            {
                // No engine behind Resources; read the text below.
            }

            if (family != null) return;
            var root = TestPackage.Root();
            if (root == null) return;
            _readFromText = true;
            try
            {
                OWPresentation.LoadText(System.IO.File.ReadAllText(
                    System.IO.Path.Combine(root, "Runtime", "Resources", "ow-presentation.json")));
            }
            catch (System.Exception)
            {
                // The colours need the engine's parser; the type table is read before them.
            }
        }

        private static bool _readFromText;

        [OneTimeTearDown]
        public void TheSidecar_IsPutBack()
        {
            if (!_readFromText) return;
            _readFromText = false;
            OWPresentation.Reload();
        }

        private static OWLensResult Read(string elements, string lensId, OWLensReader reader, OWLensOptions options = null) =>
            OWLens.Resolve(Parse(elements), lensId, reader, options);

        private static JArray Parse(string json)
        {
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JArray.Load(reader);
            }
        }

        private static JToken Kept(OWLensResult r) => new JArray(r.Kept);

        private static JToken Rows(OWLensResult r) => new JArray(r.Rows.Select(x => x.DeepClone()));

        private static JToken Cut(OWLensResult r)
        {
            var o = new JObject();
            foreach (var pair in r.Cut.Where(p => p.Value > 0)) o[pair.Key] = pair.Value;
            return o;
        }

        private static void Same(string want, JToken got, string message = null)
        {
            var w = JToken.Parse(want);
            Assert.IsTrue(JToken.DeepEquals(w, got),
                (message == null ? "" : message + ": ") + "gave " + got.ToString(Formatting.None) + ", want "
                + w.ToString(Formatting.None));
        }
    }
}
