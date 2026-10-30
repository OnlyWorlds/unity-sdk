using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The lens twin's Unity side: read a cases file of seeded random small worlds, answer each through this SDK's lens
    /// and graph readers, and write the answers in the shape the TypeScript SDK's side of the twin writes, so one compare
    /// reads both. Not a test: it runs only when <c>TW_CASES</c> (the cases file) and <c>TW_OUT</c> (the answers file)
    /// are set, and is ignored otherwise, so the suite never runs it: set both and run this one class from any NUnit host.
    /// </summary>
    /// <remarks>
    /// Each case is one JSON line: <c>{i, kind: "resolve", world, lens, reader, as_of, clock}</c> or
    /// <c>{i, kind: "graph", world, reader, options}</c>, the world being <c>{elements, now, members, hidden}</c>. Each
    /// answer is <c>{i, got}</c>: for resolve <c>{kept, cut, rows, notes_ignored, mail, unreadable}</c>, for graph
    /// <see cref="OWWorldGraph.ToJson"/>, and for a graph flag the reader refuses <c>{threw: "refused", names, msg}</c>,
    /// names being the flags its message names (the TypeScript SDK's TypeError). Any other exception answers
    /// <c>{threw: "error", names, msg}</c>. The reader and the options are read as the fixture tests read them; the
    /// world's <c>hidden</c> list rides in <c>HiddenIds</c>, as the TypeScript SDK reads it off the world.
    /// </remarks>
    public class OWLensTwinRun
    {
        [Test]
        public void AnswersTheCasesFile()
        {
            var cases = Environment.GetEnvironmentVariable("TW_CASES");
            var outPath = Environment.GetEnvironmentVariable("TW_OUT");
            if (string.IsNullOrEmpty(cases) || string.IsNullOrEmpty(outPath))
                Assert.Ignore("the lens twin runs only with TW_CASES and TW_OUT set");

            var clock = Stopwatch.StartNew();
            var lines = new List<string>();
            // A node's family and icon: outside the Editor the sidecar is read from its text, as the graph tests read it.
            var sidecar = new OWGraphTests();
            sidecar.TheSidecar_IsRead();
            try
            {
                foreach (var line in File.ReadLines(cases, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var c = OWJson.ParseObject(line);
                    var row = new JObject { ["i"] = (long)c["i"], ["got"] = Answer(c) };
                    lines.Add(row.ToString(Formatting.None));
                }
            }
            finally
            {
                sidecar.TheSidecar_IsPutBack();
            }

            File.WriteAllText(outPath, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            TestContext.WriteLine($"unity: {lines.Count} results in {clock.Elapsed.TotalSeconds:F1} s");
        }

        private static JObject Answer(JObject c)
        {
            var world = (JObject)c["world"];
            var elements = ((JArray)world["elements"]).Cast<JToken>().ToList();
            var hidden = world["hidden"] is JArray list ? list.Select(t => (string)t).ToList() : null;
            try
            {
                var reader = OWGraphTests.ReaderOf((JObject)c["reader"]);
                var lensOptions = OWLensEdgeTests.OptionsOf(c);
                lensOptions.HiddenIds = hidden;
                if ((string)c["kind"] == "resolve")
                {
                    var r = c["lens"] is JObject body
                        ? OWLens.Resolve(elements, body, reader, lensOptions)
                        : OWLens.Resolve(elements, (string)c["lens"], reader, lensOptions);
                    var got = OWLensEdgeTests.Json(r);
                    if (got["mail"] == null) got["mail"] = null;
                    got["unreadable"] = r.RefusedKey;
                    return got;
                }

                var options = OWGraphTests.OptionsOf(c);
                options.Now = lensOptions.Now;
                options.HiddenIds = hidden;
                return OWGraph.Read(elements, reader, options).ToJson();
            }
            catch (OWGraphRefusedException ex)
            {
                return Threw("refused", ex.Message);
            }
            catch (Exception ex)
            {
                return Threw("error", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static JObject Threw(string kind, string msg) => new JObject
        {
            ["threw"] = kind,
            ["names"] = new JArray(OWGraph.Flags.Where(msg.Contains)),
            ["msg"] = msg,
        };
    }
}
