using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The <c>atlas_calendar</c> profile's conformance fixture (schema-dist <c>profiles/calendar.fixture.json</c>, tag
    /// <c>v0.33.0-dist.89</c>, the profile's version 2: 75 cases, 41 of them refusals; serial 89 added 11 written from its
    /// rulings and turned <c>intercalary-after-month-out-of-range</c> into a refusal), vendored byte for byte. It is
    /// generated from the TypeScript SDK's kernel (4.76.0) against the profile page: every reader refuses the same values and reads the same words, or one tool
    /// shows a calendar where another shows numbers. When the dist moves it, re-vendor and update the hash below.
    /// </summary>
    public class OWCalendarFixtureTests
    {
        /// <summary>The fixture's sha256 as dist.89's MANIFEST.json records it.</summary>
        private const string DistSha256 = "d9fd008326558c07bf079fd744bb8364cc83299ecbe0352cb1e2000e15e3aaea";

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                Assert.IsNotNull(root, "the package's own path is how the fixture is found");
                var path = Path.Combine(root, "Tests", "Editor", "Fixtures", "calendar", "calendar.fixture.json");
                Assert.IsTrue(File.Exists(path), path);
                return path;
            }
        }

        [Test]
        public void TheFixture_IsTheDistsBytes()
        {
            using var sha = SHA256.Create();
            var hash = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(FixturePath))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(DistSha256, hash,
                "the vendored fixture is not dist.89's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void EveryCase_ReadsAsTheProfileSays()
        {
            var fixture = JObject.Parse(File.ReadAllText(FixturePath));
            var misses = new List<string>();
            int refusals = 0, calendars = 0, steps = 0, founds = 0;
            foreach (JObject c in fixture["cases"])
            {
                var name = (string)c["name"];
                CalendarConfig cfg;
                try { cfg = OWCalendarLens.ReadCalendarValue(c["value"]); }
                catch (System.Exception e) { misses.Add($"{name}: threw {e.GetType().Name} (a reader refuses, never throws)"); continue; }

                if (!(bool)c["valid"])
                {
                    refusals++;
                    if (cfg != null) misses.Add($"{name}: read as a calendar, the profile refuses it ({c["why"]})");
                    continue;
                }
                calendars++;
                if (cfg == null) { misses.Add($"{name}: refused, the profile reads it ({c["why"]})"); continue; }

                foreach (JObject s in c["steps"])
                {
                    steps++;
                    var step = (long)s["step"];
                    var words = OWCalendar.FormatStep(cfg, step);
                    if (words != (string)s["words"]) misses.Add($"{name} @{step}: '{words}', the profile reads '{s["words"]}'");

                    var b = OWCalendar.BreakdownStep(cfg, step);
                    var want = (JObject)s["breakdown"];
                    void Field(string key, object got)
                    {
                        var w = want[key] == null || want[key].Type == JTokenType.Null ? null : want[key].ToString();
                        if (w != got?.ToString()) misses.Add($"{name} @{step}: {key} {got ?? "null"}, the profile reads {w ?? "null"}");
                    }
                    Field("absYear", b.AbsYear);
                    Field("year", b.Year);
                    Field("era", b.Era?.Name);
                    Field("month", b.Month);
                    Field("monthName", b.MonthName);
                    Field("day", b.Day);
                    Field("intercalary", b.Intercalary);
                    Field("weekday", b.Weekday);
                }

                // A case that carries a world: the calendars are found by the field being present, never by type.
                if (c["elements"] is JArray elements)
                {
                    founds++;
                    var found = string.Join(", ", OWCalendarLens.WorldCalendars(elements.Cast<JObject>()).Select(w => w.Id));
                    var wantFound = string.Join(", ", c["found"].Select(t => (string)t));
                    if (found != wantFound) misses.Add($"{name}: found [{found}], the profile finds [{wantFound}]");
                }
            }

            Assert.AreEqual(75, refusals + calendars, "the dist.89 fixture holds 75 cases");
            Assert.AreEqual(41, refusals, "the dist.89 fixture holds 41 refusals");
            // The steps counted too, as the TypeScript SDK's fixture test does since 4.76.1: a dropped step reads green
            // under a case count alone.
            Assert.AreEqual(209, steps, "the dist.89 fixture's calendars read 209 steps");
            Assert.AreEqual(1, founds, "the dist.89 fixture holds one case with a world's elements");
            Assert.IsEmpty(misses, $"{refusals} refusals, {calendars} calendars, {steps} steps:\n" + string.Join("\n", misses));
        }
    }
}
