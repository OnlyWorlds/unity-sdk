using NUnit.Framework;
using UnityEngine;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The presentation sidecar, as vendored. These assert against the SHIPPED file rather than a
    /// fixture -- if a re-vendor drops the colors block or changes a family, that is exactly the
    /// regression worth failing on.
    /// </summary>
    public class OWPresentationTests
    {
        [Test]
        public void AllFourFamilies_HaveColors()
        {
            Assert.AreEqual(4, OWPresentation.FamilyOrder.Count,
                "Four families. A different count means the sidecar changed shape.");

            foreach (var family in OWPresentation.FamilyOrder)
            {
                Assert.IsNotNull(OWPresentation.ColorOfFamily(family), $"No colour for '{family}'.");
            }
        }

        [Test]
        public void FamilyOrder_IsPreserved_NotSorted()
        {
            // The order IS the CVD-safety mechanism of the source palette. Alphabetical would be
            // abstract/agents/temporal/world -- if that is what we see, something sorted it.
            CollectionAssert.AreEqual(
                new[] { "agents", "world", "abstract", "temporal" },
                OWPresentation.FamilyOrder);
        }

        [Test]
        public void WorldGreen_IsIdenticalInBothModes()
        {
            // Deliberate, and pinned by the accessibility budget: every brighter green collides
            // with Temporal amber for protan viewers. If this ever differs, someone "improved" it.
            var world = OWPresentation.ColorOfFamily("world");
            Assert.IsNotNull(world);
            Assert.AreEqual(world.Value.Light, world.Value.Dark,
                "World green is pinned at its low-contrast end in BOTH modes -- mitigate with icon "
                + "and label, never a hue shift.");
        }

        [Test]
        public void OtherFamilies_DifferBetweenModes()
        {
            // Light assumes near-white surfaces, dark near-black. A family whose two variants match
            // (other than world) means one mode was copied over the other.
            foreach (var family in new[] { "agents", "abstract", "temporal" })
            {
                var color = OWPresentation.ColorOfFamily(family);
                Assert.IsNotNull(color);
                Assert.AreNotEqual(color.Value.Light, color.Value.Dark,
                    $"'{family}' has identical light/dark variants -- the pair was measured separately.");
            }
        }

        [Test]
        public void EveryElementType_HasAFamilyAndAnIcon()
        {
            // The icon is what actually distinguishes 22 types; colour only distinguishes 4
            // families. A type missing its icon has lost the discriminating signal.
            foreach (var type in OWSync.ElementTypes)
            {
                Assert.IsNotNull(OWPresentation.FamilyOf(type), $"'{type}' has no family.");
                Assert.IsNotNull(OWPresentation.IconOf(type), $"'{type}' has no icon.");
            }
        }

        [Test]
        public void KnownTypes_MapToExpectedFamilies()
        {
            Assert.AreEqual("agents", OWPresentation.FamilyOf("character"));
            Assert.AreEqual("world", OWPresentation.FamilyOf("location"));
            Assert.AreEqual("abstract", OWPresentation.FamilyOf("ability"));
            Assert.AreEqual("temporal", OWPresentation.FamilyOf("event"));
        }

        [Test]
        public void UnknownType_FallsBackRatherThanThrowing()
        {
            // The standard can add a type before this SDK re-vendors. A viewer that crashes on an
            // unknown type is worse than one that draws it plainly.
            Assert.IsNull(OWPresentation.FamilyOf("not_a_type"));
            Assert.IsNull(OWPresentation.IconOf("not_a_type"));
            Assert.DoesNotThrow(() => OWPresentation.ColorFor("not_a_type", true));
            Assert.DoesNotThrow(() => OWPresentation.ColorFor(null, false));
        }

        [Test]
        public void ColorFor_SelectsTheModeVariant()
        {
            var agents = OWPresentation.ColorOfFamily("agents").Value;

            Assert.AreEqual(agents.Light, OWPresentation.ColorFor("character", darkSurface: false));
            Assert.AreEqual(agents.Dark, OWPresentation.ColorFor("character", darkSurface: true));
        }

        [Test]
        public void ColorsParse_AsRealColors_NotBlack()
        {
            // A failed hex parse yields default(Color) -- transparent black -- which would render
            // as "no swatch" rather than as an error. Assert the parse actually happened.
            foreach (var family in OWPresentation.FamilyOrder)
            {
                var c = OWPresentation.ColorOfFamily(family).Value;
                Assert.Greater(c.Light.a, 0.9f, $"'{family}' light parsed as transparent.");
                Assert.Greater(c.Dark.a, 0.9f, $"'{family}' dark parsed as transparent.");
                Assert.IsFalse(c.Light.r == 0f && c.Light.g == 0f && c.Light.b == 0f,
                    $"'{family}' light parsed as pure black -- likely a failed parse.");
            }
        }
    }
}
