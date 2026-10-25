using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The calendar profile's version 2 (schema-dist serial 68, <c>profiles/calendar.md</c>): an intercalary entry's leap
    /// keys <c>except</c> and <c>offset</c>, a version-1 reading of a version-2 value, and an up era's open end skipping
    /// down eras. Ported from the TypeScript SDK's <c>test/calendar-v2.test.mjs</c> (4.76.0), its LegendKeeper fixture
    /// vendored in <c>Tests/Editor/Fixtures/calendar/</c>: five of LegendKeeper's preset calendars as configs and, for each
    /// probed day, LegendKeeper's own reading of it (year, era, month, day). Gregorian (every 4, not every 100, yet every
    /// 400, no year 0) and Harptos (every 4, no year 0) need version 2. LegendKeeper adds a leap day to the end of its
    /// month; the profile names it as its own day, so a probe on that day matches when the step reads as the leap day.
    /// </summary>
    public class OWCalendarV2Tests
    {
        private static JObject _lk;

        /// <summary>
        /// The package's own resolved path; outside the Editor (no package manager to ask), the working directory's
        /// <c>Packages/com.onlyworlds.sdk</c>, as <c>OWRoomTests</c> finds its fixture.
        /// </summary>
        private static string PackageRoot()
        {
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OWCalendarV2Tests).Assembly);
                if (package != null) return package.resolvedPath;
            }
            catch (Exception)
            {
                // No Editor behind the call; try the working directory below.
            }

            var local = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.onlyworlds.sdk");
            return Directory.Exists(local) ? local : null;
        }

        private static JObject LegendKeeper
        {
            get
            {
                if (_lk != null) return _lk;
                var root = PackageRoot();
                Assert.IsNotNull(root, "the package's own path is how the fixture is found");
                var path = Path.Combine(root, "Tests", "Editor", "Fixtures", "calendar", "legendkeeper-calendars.json");
                Assert.IsTrue(File.Exists(path), path);
                return _lk = OWJson.ParseObject(File.ReadAllText(path));
            }
        }

        private static IEnumerable<JObject> Calendars => LegendKeeper["calendars"].Cast<JObject>();

        private static JObject Gregorian => (JObject)Calendars.First(c => (string)c["name"] == "Gregorian")["config"];

        private static CalendarConfig Read(JToken value)
        {
            var cfg = OWCalendar.ReadConfig(value);
            Assert.IsNotNull(cfg, "the config reads");
            return cfg;
        }

        public static IEnumerable<TestCaseData> CalendarNames()
        {
            foreach (var name in new[] { "Gregorian", "Harptos", "Eberron", "Exandria", "Greyhawk" })
                yield return new TestCaseData(name).SetName("LegendKeeper_" + name + "_EveryProbedDayReadsAsLegendKeeperReadsIt");
        }

        [TestCaseSource(nameof(CalendarNames))]
        public void LegendKeeper_EveryProbedDayReadsAsLegendKeeperReadsIt(string name)
        {
            var cal = Calendars.First(c => (string)c["name"] == name);
            var cfg = Read(cal["config"]);
            CollectionAssert.IsEmpty(OWCalendar.Validate(cfg));
            var probes = (JArray)cal["probes"];
            Assert.Greater(probes.Count, 70, "the probe is there");
            var leapDays = (JObject)cal["leap_days"];
            var misses = new List<string>();
            foreach (JArray p in probes)
            {
                long step = (long)p[0];
                string year = p[1].ToString(), era = (string)p[2], month = (string)p[3];
                long day = (long)p[4];
                var leap = leapDays.Properties().FirstOrDefault(l => (string)l.Value["month"] == month && day == (long)l.Value["length"] + 1);
                var want = leap != null ? $"{year}|{era}|{leap.Name}|1" : $"{year}|{era}|{month}|{day}";
                var b = OWCalendar.BreakdownStep(cfg, step);
                var got = b.Intercalary != null ? $"{b.Year}|{b.Era?.Name}|{b.Intercalary}|1" : $"{b.Year}|{b.Era?.Name}|{b.MonthName}|{b.Day}";
                if (got != want) misses.Add($"{step}: {got} for {want}");
            }
            CollectionAssert.IsEmpty(misses);
        }

        [Test]
        public void AVersion2Value_ReadsAsVersion1WouldWithoutItsLeapKeys_AndTheKeysChangeTheLeapYears()
        {
            var greg = Read(Gregorian);
            var leap = greg.Intercalary.First(i => i.EveryYears != null);
            // 1900 (index 1899) is not a leap year, 2000 (index 1999) is, 1999 (index 1998) is not: offset 1, except [100, 400].
            Assert.IsFalse(OWCalendar.IntercalaryInYear(greg, leap, 1899));
            Assert.IsTrue(OWCalendar.IntercalaryInYear(greg, leap, 1999));
            Assert.IsFalse(OWCalendar.IntercalaryInYear(greg, leap, 1998));
            Assert.IsTrue(OWCalendar.IntercalaryInYear(greg, leap, 2003));
            Assert.AreEqual(365, OWCalendar.YearLength(greg, 1899));
            Assert.AreEqual(366, OWCalendar.YearLength(greg, 1999));

            // The same keys under version 1 are unknown keys: ignored, the plain every-4 rule from index 0.
            var raw = (JObject)Gregorian.DeepClone();
            raw["version"] = 1;
            var v1 = Read(raw);
            var leap1 = v1.Intercalary.First(i => i.EveryYears != null);
            Assert.IsNull(leap1.Except, "a version-1 reader keeps no except");
            Assert.IsNull(leap1.Offset, "a version-1 reader keeps no offset");
            Assert.IsFalse(OWCalendar.IntercalaryInYear(v1, leap1, 1899));
            Assert.IsTrue(OWCalendar.IntercalaryInYear(v1, leap1, 1900));
            Assert.AreEqual(366, OWCalendar.YearLength(v1, 1900));

            // A writer is told the keys need version 2 (a config built by hand, since the reader drops them).
            var handBuilt = Read(Gregorian);
            handBuilt.Version = 1;
            StringAssert.Contains("version 2", string.Join(" ", OWCalendar.Validate(handBuilt)));
        }

        [Test]
        public void TheClosedFormYearStarts_EqualTheSumOfYearLengths_BothSidesOf0()
        {
            foreach (var cal in Calendars)
            {
                var name = (string)cal["name"];
                var cfg = Read(cal["config"]);
                long start = 0;
                for (long y = 0; y < 1300; y++)
                {
                    Assert.AreEqual(start, OWCalendar.YearStartStep(cfg, y), $"{name} year {y}");
                    start += OWCalendar.YearLength(cfg, y);
                }
                start = 0;
                for (long y = -1; y > -1300; y--)
                {
                    start -= OWCalendar.YearLength(cfg, y);
                    Assert.AreEqual(start, OWCalendar.YearStartStep(cfg, y), $"{name} year {y}");
                }
                // +-1e12 steps (about 2.7 billion years): the year search seeds on the mean year, except chain
                // included, and corrects at most 10,000 laps, so a seed that ignored the chain would land outside.
                foreach (var step in new long[] { -1_000_000_000_000, -500000, -146097, -1, 0, 1, 146097, 730484, 1000000, 1_000_000_000_000 })
                {
                    var y = OWCalendar.AbsoluteYear(cfg, step);
                    Assert.IsTrue(OWCalendar.YearStartStep(cfg, y) <= step && step < OWCalendar.YearStartStep(cfg, y + 1), $"{name} step {step}");
                }
            }
        }

        [Test]
        public void AVersion2Date_ParsesBackToItsStep()
        {
            var greg = Read(Gregorian);
            foreach (var step in new long[] { 0, 59, 365, 730484, 730484 + 59, -1, -366 })
            {
                var words = OWCalendar.FormatStep(greg, step);
                var back = OWCalendar.ParseDate(greg, words);
                Assert.IsFalse(OWCalendar.IsParseError(back), $"{words}: {back?.Error}");
                Assert.AreEqual(step, back.Step, words);
            }
        }

        [Test]
        public void Version2_RefusesALeapRuleItCannotRead_Version3IsNotACalendar()
        {
            JObject Base() => JObject.Parse("{ version: 2, grain: 'day', bundles: { months: { names: ['A', 'B'], lengths: [10, 10] } } }");
            JObject WithLeap(string extra)
            {
                var b = Base();
                var leap = JObject.Parse("{ name: 'L', after_month: 1, every_years: 4 }");
                foreach (var p in JObject.Parse(extra).Properties()) leap[p.Name] = p.Value;
                b["intercalary"] = new JArray(leap);
                return b;
            }

            foreach (var (extra, why) in new[]
            {
                ("{ except: [100, 250] }", "not a multiple of the one before"),
                ("{ except: [400, 100] }", "not ascending"),
                ("{ except: [4] }", "not above every_years (the profile's own refusal example)"),
                ("{ except: [6] }", "not a multiple of every_years"),
                ("{ except: 100 }", "not a list"),
                ("{ except: [100.5] }", "not whole"),
                ("{ offset: 0.5 }", "offset not whole"),
                ("{ offset: '1' }", "offset not a number"),
            })
            {
                Assert.IsNull(OWCalendar.ReadConfig(WithLeap(extra)), why);
            }

            var noPeriod = Base();
            noPeriod["intercalary"] = JArray.Parse("[{ name: 'L', after_month: 1, offset: 1 }]");
            Assert.IsNull(OWCalendar.ReadConfig(noPeriod), "offset without every_years");
            Assert.IsNotNull(OWCalendar.ReadConfig(WithLeap("{ except: [100, 400], offset: -3 }")), "a negative offset is whole");
            Assert.IsNotNull(OWCalendar.ReadConfig(WithLeap("{ except: null, offset: null }")), "null is absent");
            var v3 = Base();
            v3["version"] = 3;
            Assert.IsNull(OWCalendar.ReadConfig(v3));

            // The validator names a bad chain on a config built by hand (the reader never hands one back).
            var cfg = Read(Base());
            foreach (var (except, why) in new[]
            {
                (new List<long> { 100, 250 }, "not a multiple of the one before"),
                (new List<long> { 400, 100 }, "not ascending"),
                (new List<long> { 4 }, "not above every_years"),
                (new List<long> { 6 }, "not a multiple of every_years"),
            })
            {
                cfg.Intercalary = new List<CalendarIntercalary> { new CalendarIntercalary { Name = "L", AfterMonth = 1, EveryYears = 4, Except = except } };
                CollectionAssert.IsNotEmpty(OWCalendar.Validate(cfg), why);
            }
            cfg.Intercalary = new List<CalendarIntercalary> { new CalendarIntercalary { Name = "L", AfterMonth = 1, Offset = 1 } };
            StringAssert.Contains("need every_years", string.Join(" ", OWCalendar.Validate(cfg)));
        }

        [Test]
        public void AnUpEraWithNoEnd_RunsToTheNextUpEra_PastADownEraListedAfterIt()
        {
            const string after = "{ name: 'After', abbrev: 'AF', start: 0 }";
            const string before = "{ name: 'Before', abbrev: 'BF', start: 0, direction: 'down' }";
            CalendarConfig Eras(params string[] eras) =>
                Read(JObject.Parse("{ version: 1, grain: 'year', eras: [" + string.Join(", ", eras) + "] }"));

            var cfg = Eras(after, before);
            Assert.AreEqual("After", OWCalendar.EraForStep(cfg, 5)?.Name);
            Assert.AreEqual("6 AF", OWCalendar.FormatStep(cfg, 5));
            Assert.AreEqual("1 BF", OWCalendar.FormatStep(cfg, -1));
            // List order no longer decides it.
            Assert.AreEqual("6 AF", OWCalendar.FormatStep(Eras(before, after), 5));
            // A later up era still ends it.
            var later = Eras(after, before, "{ name: 'Third', abbrev: 'TH', start: 10 }");
            Assert.AreEqual("After", OWCalendar.EraForStep(later, 9)?.Name);
            Assert.AreEqual("Third", OWCalendar.EraForStep(later, 10)?.Name);
        }
    }
}
