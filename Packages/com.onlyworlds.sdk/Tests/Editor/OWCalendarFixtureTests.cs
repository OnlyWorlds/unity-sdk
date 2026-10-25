using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The <c>atlas_calendar</c> profile's conformance fixture (schema-dist <c>profiles/calendar.fixture.json</c>, tag
    /// <c>v0.33.0-dist.69</c>, the profile's version 2: 64 cases, 34 of them refusals), vendored byte for byte. It is
    /// generated from the TypeScript SDK's kernel (4.76.0) against the profile page: every reader refuses the same values and reads the same words, or one tool
    /// shows a calendar where another shows numbers. When the dist moves it, re-vendor and update the hash below.
    /// </summary>
    public class OWCalendarFixtureTests
    {
        /// <summary>The fixture's sha256 as dist.69's MANIFEST.json records it.</summary>
        private const string DistSha256 = "768318b7406099b0870d530e2409214145d9e4cd09a06299590056f2e95ce8d8";

        /// <summary>
        /// The package's own resolved path; outside the Editor (no package manager to ask), the working directory's
        /// <c>Packages/com.onlyworlds.sdk</c>, as <c>OWRoomTests</c> finds its fixture.
        /// </summary>
        private static string PackageRoot()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWCalendarFixtureTests).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (System.Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }

        private static string FixturePath
        {
            get
            {
                var root = PackageRoot();
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
                "the vendored fixture is not dist.69's: re-vendor it from schema-dist (a checkout's line endings change the bytes)");
        }

        [Test]
        public void EveryCase_ReadsAsTheProfileSays()
        {
            var fixture = JObject.Parse(File.ReadAllText(FixturePath));
            var misses = new List<string>();
            int refusals = 0, calendars = 0, steps = 0;
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
            }

            Assert.AreEqual(64, refusals + calendars, "the dist.69 fixture holds 64 cases");
            Assert.Greater(steps, 0, "a fixture whose calendars read no steps checks nothing");
            Assert.IsEmpty(misses, $"{refusals} refusals, {calendars} calendars, {steps} steps:\n" + string.Join("\n", misses));
        }
    }
}
