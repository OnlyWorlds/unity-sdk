using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The generated models against a real world folder on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test in this suite feeds the models JSON that a test wrote. This one feeds them
    /// bodies a real authoring pipeline produced, which is the only way to find the fields nobody
    /// thought to put in a fixture. Sikelia is the SDK's first customer, so its folder is the
    /// closest thing to a production input this package has.
    /// </para>
    /// <para>
    /// The world lives outside this repository. When it is absent these tests report themselves
    /// INCONCLUSIVE rather than passing -- the same stance <see cref="OWFolderConformanceTests"/>
    /// takes, and for the same reason: a green run over a world that was never opened is the same
    /// class of non-result as a build over code that was never compiled in.
    /// </para>
    /// </remarks>
    public class GeneratedModelFolderTests
    {
        /// <summary>
        /// Where the Sikelia world folder lives, if it is on this machine.
        /// </summary>
        /// <remarks>
        /// Override with <c>OW_SIKELIA_WORLD</c>. The default is the author's local path, which is
        /// useless elsewhere -- deliberately, because the alternative is vendoring a copy of a
        /// live world into a package repository.
        /// </remarks>
        private static string WorldRoot =>
            Environment.GetEnvironmentVariable("OW_SIKELIA_WORLD")
            ?? @"C:\Users\Titus\Development\OnlyWorlds\sikelia-world";

        private static bool WorldAvailable =>
            Directory.Exists(WorldRoot) && File.Exists(Path.Combine(WorldRoot, "world.json"));

        private static void RequireWorld()
        {
            if (WorldAvailable) return;

            Assert.Ignore(
                $"Sikelia world folder not present at {WorldRoot}. These assertions did not run -- "
                + "treat this suite as unmeasured, not as passing.");
        }

        private static OWFolderReadResult Read() =>
            OWFolderReader.Read(WorldRoot, includeLegacySpatial: false);

        [Test]
        public void EveryElementInTheFolder_HasAGeneratedModel()
        {
            RequireWorld();
            var result = Read();

            Assert.Greater(result.Elements.Count, 0, "The folder read produced no elements.");

            var unmodeled = result.Elements
                .Select(e => e.Type)
                .Where(t => !OWElementTypes.IsKnown(t))
                .Distinct()
                .ToList();

            CollectionAssert.IsEmpty(unmodeled,
                "The folder holds element types the generated registry does not know: "
                + string.Join(", ", unmodeled));
        }

        [Test]
        public void EveryElementInTheFolder_DeserializesIntoItsTypedModel()
        {
            RequireWorld();
            var result = Read();

            foreach (var element in result.Elements)
            {
                var type = OWElementTypes.TypeFor(element.Type);
                if (type == null) continue; // reported by the test above

                var typed = (OWElement)JsonConvert.DeserializeObject(
                    element.Body.ToString(), type, OWJson.Settings);

                Assert.IsNotNull(typed, $"{element.Path} produced no object.");
                Assert.AreEqual(element.Id, typed.Id,
                    $"{element.Path} lost or changed its id on the way into {type.Name}.");
            }
        }

        [Test]
        public void NoElementInTheFolder_LosesAFieldTheModelShouldKnow()
        {
            // The real question this suite exists to answer. Every key in every body must either
            // be a field the model declares, or reach the extension bag, which carries it back
            // out on write. A real world always holds fields some tool added (Atlas stamps
            // atlas_shape on zones; not every tool prefixes x_), so an undeclared key is not a
            // failure: dropping one is. A hand-written fixture only ever contains the keys its
            // author remembered, which is what a real world folder is for.
            RequireWorld();
            var result = Read();

            var losses = new List<string>();
            var carried = new HashSet<string>(StringComparer.Ordinal);

            foreach (var element in result.Elements)
            {
                var type = OWElementTypes.TypeFor(element.Type);
                if (type == null) continue;

                var known = new HashSet<string>(
                    OWElementTypes.FieldNames[element.Type], StringComparer.Ordinal);
                foreach (var baseField in OWElementTypes.BaseFieldNames) known.Add(baseField);

                var undeclared = element.Body.Properties()
                    .Select(p => p.Name)
                    .Where(name => !known.Contains(name))
                    .ToList();
                if (undeclared.Count == 0) continue;

                var typed = (OWElement)JsonConvert.DeserializeObject(
                    element.Body.ToString(), type, OWJson.Settings);
                var bag = new HashSet<string>(typed.Extensions.Select(e => e.Key), StringComparer.Ordinal);

                foreach (var name in undeclared)
                {
                    if (bag.Contains(name)) carried.Add($"{element.Type}.{name}");
                    else losses.Add($"{element.Type}.{name} ({Path.GetFileName(element.Path)})");
                }
            }

            if (carried.Count > 0)
                TestContext.WriteLine("Undeclared fields carried by the extension bag: "
                    + string.Join(", ", carried.OrderBy(n => n, StringComparer.Ordinal)));

            CollectionAssert.IsEmpty(losses.Distinct().ToList(),
                "Fields present on disk that neither a generated model declares nor the extension "
                + "bag carries. Each one would be dropped on read and destroyed on write-back: "
                + string.Join(", ", losses.Distinct()));
        }

        [Test]
        public void EveryElementInTheFolder_RoundTripsWithoutLosingAKey()
        {
            // Read from disk, deserialize, serialize, and every key that was on disk must still
            // be there with the same value. This is the write-back safety property: an element
            // that survives a round trip cannot destroy another tool's state when it is PATCHed.
            RequireWorld();
            var result = Read();

            var failures = new List<string>();

            foreach (var element in result.Elements)
            {
                var type = OWElementTypes.TypeFor(element.Type);
                if (type == null) continue;

                var typed = JsonConvert.DeserializeObject(
                    element.Body.ToString(), type, OWJson.Settings);
                var back = OWFolderWriter.Parse(OWJson.Serialize(typed));

                foreach (var property in element.Body.Properties())
                {
                    var written = back[property.Name];
                    if (written == null)
                    {
                        failures.Add($"{Path.GetFileName(element.Path)}: '{property.Name}' vanished");
                        continue;
                    }

                    if (!JToken.DeepEquals(property.Value, written))
                    {
                        failures.Add(
                            $"{Path.GetFileName(element.Path)}: '{property.Name}' changed from "
                            + $"{property.Value.ToString(Formatting.None)} to "
                            + $"{written.ToString(Formatting.None)}");
                    }
                }
            }

            CollectionAssert.IsEmpty(failures,
                "Round trip through the typed models altered a real world's data:\n"
                + string.Join("\n", failures.Take(20)));
        }

        [Test]
        public void TheFolderExercisesMoreThanOneType()
        {
            // A guard on the guard. If the world folder ever collapses to a single type, the four
            // tests above would still go green while measuring almost nothing -- the same shape as
            // an IL2CPP build that stripped the code it was meant to prove.
            RequireWorld();
            var result = Read();

            var types = result.Elements.Select(e => e.Type).Distinct().ToList();
            Assert.Greater(types.Count, 5,
                "This world exercises too few types for the suite above to mean much. "
                + $"Found: {string.Join(", ", types)}");
        }
    }
}
