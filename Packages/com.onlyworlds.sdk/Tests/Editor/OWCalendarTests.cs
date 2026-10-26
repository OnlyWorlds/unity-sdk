using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// The calendar kernel's contract tests, ported one for one from @onlyworlds/sdk 4.23.0
    /// test/calendar-kernel.test.mjs (48 tests, same inputs, same expected outputs), plus a real world's calendar
    /// (Moppetopia's Treaty Reckoning).
    /// Fixtures: Kirrowmoss (irregular months, 3 eras incl. closed, free 6-day week), Vessach (year-grain, era
    /// gap), Dune B.G./A.G. (down-era astride 0), Harptos (intercalary festivals + Shieldmeet leap + nested
    /// tendays), Shire (the weekday lock via per-day in_week).
    /// </summary>
    public class OWCalendarTests
    {
        // ---- Fixtures (JSON, read through the defensive reader as a consumer would) ----

        const string KirrowmossJson = @"{
            version: 1, grain: 'day',
            eras: [
                { name: 'Kingsreckoning', abbrev: 'KR', start: -131400, end: -3600, restart: true, closed: true },
                { name: 'Occupation Count', abbrev: 'OC', start: -3600, restart: true, closed: true },
                { name: 'Freehold Tally', abbrev: 'FT', start: 0, restart: true }
            ],
            bundles: {
                months: {
                    names: ['Sowmarch', 'Thistlewane', 'Greening', 'Suncrest', 'Longhay', 'Ashfall',
                            'Rootdeep', 'Frostwake', 'Bellmoot', 'Hearthtide', 'Graytide', 'Coldmark'],
                    lengths: [30, 28, 32, 30, 31, 29, 30, 28, 32, 30, 29, 31]
                },
                week: { length: 6, names: ['Ondel', 'Tresk', 'Mireth', 'Aldun', 'Volk', 'Hearth'], mode: 'free' }
            },
            format: '{day} {month}, Year {year} {era}'
        }";

        const string VessachJson = @"{
            version: 1, grain: 'year',
            eras: [
                { name: 'First Age', abbrev: 'FA', start: -20000, end: -10000, restart: true, year_base: 0 },
                { name: 'Third Age', abbrev: 'TA', start: 0, restart: true, year_base: 0 }
            ],
            format: '{year} {era}'
        }";

        const string DuneJson = @"{
            version: 1, grain: 'year',
            eras: [
                { name: 'Before Guild', abbrev: 'B.G.', start: 0, direction: 'down', restart: true },
                { name: 'After Guild', abbrev: 'A.G.', start: 0, restart: true, year_base: 0 }
            ],
            format: '{year} {era}'
        }";

        const string HarptosJson = @"{
            version: 1, grain: 'day',
            eras: [{ name: 'Dalereckoning', abbrev: 'DR', start: 0, restart: true, year_base: 0 }],
            bundles: {
                months: {
                    names: ['Hammer', 'Alturiak', 'Ches', 'Tarsakh', 'Mirtul', 'Kythorn',
                            'Flamerule', 'Eleasis', 'Eleint', 'Marpenoth', 'Uktar', 'Nightal'],
                    lengths: [30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30]
                },
                week: { length: 10, names: ['1st', '2nd', '3rd', '4th', '5th', '6th', '7th', '8th', '9th', '10th'], mode: 'nested' }
            },
            intercalary: [
                { name: 'Midwinter', after_month: 1 },
                { name: 'Greengrass', after_month: 3 },
                { name: 'Midsummer', after_month: 7 },
                { name: 'Shieldmeet', after_month: 7, every_years: 4 },
                { name: 'Highharvestide', after_month: 9 },
                { name: 'Feast of the Moon', after_month: 11 }
            ],
            format: '{day} {month}, {year} {era}',
            format_intercalary: '{intercalary}, {year} {era}'
        }";

        const string ShireJson = @"{
            version: 1, grain: 'day',
            eras: [{ name: 'Shire Reckoning', abbrev: 'S.R.', start: 0, restart: true }],
            bundles: {
                months: {
                    names: ['Afteryule', 'Solmath', 'Rethe', 'Astron', 'Thrimidge', 'Forelithe',
                            'Afterlithe', 'Wedmath', 'Halimath', 'Winterfilth', 'Blotmath', 'Foreyule'],
                    lengths: [30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30]
                },
                week: { length: 7, names: ['Sterday', 'Sunday', 'Monday', 'Trewsday', 'Hevensday', 'Mersday', 'Highday'], mode: 'free' }
            },
            intercalary: [
                { name: '2 Yule', after_month: 0 },
                { name: '1 Lithe', after_month: 6 },
                { name: ""Mid-year's Day"", after_month: 6, in_week: false },
                { name: 'Overlithe', after_month: 6, every_years: 4, in_week: false },
                { name: '2 Lithe', after_month: 6 },
                { name: '1 Yule', after_month: 12 }
            ],
            format: '{day} {month}, S.R. {year}',
            format_intercalary: '{intercalary}, S.R. {year}'
        }";

        // Minimal: eras + format on a year-grain world must feel complete.
        const string MinimalJson = @"{
            version: 1, grain: 'year',
            eras: [{ name: 'Third Age', abbrev: 'TA', start: 0, restart: true, year_base: 0 }],
            format: '{year} {era}'
        }";

        // Moppetopia's Treaty Reckoning, its atlas_calendar copied verbatim (a real world, written by Atlas).
        const string TreatyReckoningJson = @"{
            ""eras"": [
                { ""name"": ""Before the Treaty"", ""start"": 250, ""abbrev"": ""BT"", ""direction"": ""down"" },
                { ""name"": ""Years since Soft Landings"", ""start"": 250, ""abbrev"": ""YSL"", ""year_base"": 0 }
            ],
            ""grain"": ""Year"",
            ""format"": ""Year {year} {era}"",
            ""version"": 1
        }";

        static CalendarConfig Read(string json)
        {
            var cfg = OWCalendar.ReadConfig(JObject.Parse(json));
            Assert.IsNotNull(cfg, "fixture failed the defensive read");
            return cfg;
        }

        /// <summary>The JS spread { ...base, key: value }.</summary>
        static CalendarConfig With(string json, string key, JToken value)
        {
            var o = JObject.Parse(json);
            o[key] = value;
            return Read(o.ToString());
        }

        static CalendarConfig KIRROWMOSS => Read(KirrowmossJson);
        static CalendarConfig VESSACH => Read(VessachJson);
        static CalendarConfig DUNE => Read(DuneJson);
        static CalendarConfig HARPTOS => Read(HarptosJson);
        static CalendarConfig SHIRE => Read(ShireJson);
        static CalendarConfig MINIMAL => Read(MinimalJson);

        static string[] AllFixtureJson => new[] { KirrowmossJson, VessachJson, DuneJson, HarptosJson, ShireJson, MinimalJson };

        static long Step(ParseResult r)
        {
            Assert.IsFalse(OWCalendar.IsParseError(r), "parse failed: " + r);
            return r.Step;
        }

        // ---- Euclidean division (contract rule 1: floor, never truncation) ----

        [Test]
        public void FloorDivision_FloorsTowardNegativeInfinity()
        {
            Assert.AreEqual(-2, OWCalendar.FloorDiv(-400, 360));
            Assert.AreEqual(1, OWCalendar.FloorDiv(400, 360));
            Assert.AreEqual(-1, OWCalendar.FloorDiv(-360, 360));
        }

        [Test]
        public void FloorDivision_FloorModStaysPeriodicForNegatives()
        {
            Assert.AreEqual(3, OWCalendar.FloorMod(-1, 4));
            Assert.AreEqual(0, OWCalendar.FloorMod(-4, 4));
            Assert.AreEqual(0, OWCalendar.FloorMod(-8, 4));
        }

        // ---- Absolute year index (rule 1: zero-based, step 0 opens year 0) ----

        [Test]
        public void AbsoluteYear_IsZeroBasedWithEuclideanFloor()
        {
            var k = KIRROWMOSS;
            Assert.AreEqual(0, OWCalendar.AbsoluteYear(k, 0));
            Assert.AreEqual(0, OWCalendar.AbsoluteYear(k, 359));
            Assert.AreEqual(1, OWCalendar.AbsoluteYear(k, 360));
            Assert.AreEqual(-1, OWCalendar.AbsoluteYear(k, -1));
            Assert.AreEqual(-1, OWCalendar.AbsoluteYear(k, -360));
            Assert.AreEqual(-2, OWCalendar.AbsoluteYear(k, -361));
        }

        [Test]
        public void AbsoluteYear_YearGrainTheStepIsTheYear()
        {
            Assert.AreEqual(10191, OWCalendar.AbsoluteYear(DUNE, 10191));
            Assert.AreEqual(-108, OWCalendar.AbsoluteYear(DUNE, -108));
        }

        // ---- Leap arithmetic (rule 6 + astride year 0) ----

        [Test]
        public void LeapYears_FireOnFloorModZero_IncludingNegativesAndYearZero()
        {
            Assert.IsTrue(OWCalendar.IsLeapYear(0, 4));
            Assert.IsTrue(OWCalendar.IsLeapYear(4, 4));
            Assert.IsTrue(OWCalendar.IsLeapYear(-4, 4));
            Assert.IsFalse(OWCalendar.IsLeapYear(-1, 4));
            Assert.IsFalse(OWCalendar.IsLeapYear(3, 4));
        }

        [Test]
        public void LeapYears_YearLengthsAndStartsAccountForLeapDaysAstrideYearZero()
        {
            var h = HARPTOS;
            // Harptos: 360 month days + 5 fixed festivals = 365; Shieldmeet years 366.
            Assert.AreEqual(366, OWCalendar.YearLength(h, 0));
            Assert.AreEqual(365, OWCalendar.YearLength(h, 1));
            Assert.AreEqual(366, OWCalendar.YearLength(h, 4));
            Assert.AreEqual(366, OWCalendar.YearLength(h, -4));
            // yearStart(0) is 0 by construction; year 1 starts after leap year 0.
            Assert.AreEqual(0, OWCalendar.YearStartStep(h, 0));
            Assert.AreEqual(366, OWCalendar.YearStartStep(h, 1));
            Assert.AreEqual(731, OWCalendar.YearStartStep(h, 2));
            // Negative side: year -1 (365) and year -4 (leap, 366).
            Assert.AreEqual(-365, OWCalendar.YearStartStep(h, -1));
            Assert.AreEqual(-365 * 3 - 366, OWCalendar.YearStartStep(h, -4));
            // Consistency: AbsoluteYear inverts YearStartStep everywhere.
            foreach (long y in new long[] { -5, -4, -1, 0, 1, 3, 4, 100 })
            {
                Assert.AreEqual(y, OWCalendar.AbsoluteYear(h, OWCalendar.YearStartStep(h, y)));
                Assert.AreEqual(y, OWCalendar.AbsoluteYear(h, OWCalendar.YearStartStep(h, y + 1) - 1));
            }
        }

        // ---- Era domains, gaps, down-eras (rules 3 + 4) ----

        [Test]
        public void Eras_SelectByDomain_StartToEndOrNextStart()
        {
            var k = KIRROWMOSS;
            Assert.AreEqual("FT", OWCalendar.EraForStep(k, 0)?.Abbrev); // boundary: latest start wins
            Assert.AreEqual("OC", OWCalendar.EraForStep(k, -1)?.Abbrev);
            Assert.AreEqual("OC", OWCalendar.EraForStep(k, -3600)?.Abbrev);
            Assert.AreEqual("KR", OWCalendar.EraForStep(k, -3601)?.Abbrev);
        }

        [Test]
        public void Eras_AnExplicitEndCreatesARealInteriorGap()
        {
            var v = VESSACH;
            Assert.AreEqual("FA", OWCalendar.EraForStep(v, -20000)?.Abbrev);
            Assert.AreEqual("FA", OWCalendar.EraForStep(v, -10001)?.Abbrev);
            Assert.IsNull(OWCalendar.EraForStep(v, -10000)); // the gap
            Assert.IsNull(OWCalendar.EraForStep(v, -5000));
            Assert.AreEqual("TA", OWCalendar.EraForStep(v, 0)?.Abbrev);
        }

        [Test]
        public void Eras_GapAndPreEraFormatting_EraEmptyYearAbsolute()
        {
            Assert.AreEqual("-5000", OWCalendar.FormatStep(VESSACH, -5000));
            Assert.AreEqual("-30000", OWCalendar.FormatStep(VESSACH, -30000)); // before the first era
        }

        [Test]
        public void Eras_DownEraStartIsThePivot_TheUpEraOwnsTheSharedStep()
        {
            Assert.AreEqual("A.G.", OWCalendar.EraForStep(DUNE, 0)?.Abbrev);
            Assert.AreEqual("B.G.", OWCalendar.EraForStep(DUNE, -1)?.Abbrev);
        }

        [Test]
        public void Eras_DownEraYearFormula_NoYearZeroOnTheDownSide()
        {
            var d = DUNE;
            Assert.AreEqual("1 B.G.", OWCalendar.FormatStep(d, -1));
            Assert.AreEqual("108 B.G.", OWCalendar.FormatStep(d, -108));
            Assert.AreEqual("0 A.G.", OWCalendar.FormatStep(d, 0));
            Assert.AreEqual("10191 A.G.", OWCalendar.FormatStep(d, 10191));
        }

        [Test]
        public void Eras_YearBasePicksZeroOrOneBasedNumberingPerEra()
        {
            // Kirrowmoss: 1-based (default). Step 0 = Year 1 FT.
            Assert.AreEqual("1 Sowmarch, Year 1 FT", OWCalendar.FormatStep(KIRROWMOSS, 0));
            // Vessach: 0-based. Step 0 = 0 TA; step 1204 = 1204 TA; step -19912 = 88 FA.
            var v = VESSACH;
            Assert.AreEqual("0 TA", OWCalendar.FormatStep(v, 0));
            Assert.AreEqual("1204 TA", OWCalendar.FormatStep(v, 1204));
            Assert.AreEqual("88 FA", OWCalendar.FormatStep(v, -19912));
        }

        [Test]
        public void Eras_RestartFalseShowsTheAbsoluteYearIndexUnderTheEraLabel()
        {
            var cfg = Read(@"{ version: 1, grain: 'year',
                eras: [{ name: 'Common Era', abbrev: 'CE', start: -500, restart: false }],
                format: '{year} {era}' }");
            Assert.AreEqual("454 CE", OWCalendar.FormatStep(cfg, 454));
            Assert.AreEqual("-100 CE", OWCalendar.FormatStep(cfg, -100));
        }

        // ---- The gate-verified Kirrowmoss vector + round trips ----

        [Test]
        public void Kirrowmoss_TheGateVerifiedVector_Step7603()
        {
            // 21 complete years x 360 + Sowmarch 30 + day-offset 13.
            Assert.AreEqual("14 Thistlewane, Year 22 FT", OWCalendar.FormatStep(KIRROWMOSS, 7603));
            var r = OWCalendar.ParseDate(KIRROWMOSS, "14 Thistlewane, Year 22 FT");
            Assert.IsFalse(OWCalendar.IsParseError(r));
            Assert.AreEqual(7603, r.Step);
        }

        [Test]
        public void Kirrowmoss_IrregularMonthBoundariesLandCorrectly()
        {
            var b = OWCalendar.BreakdownStep(KIRROWMOSS, 30); // day after 30-day Sowmarch
            Assert.AreEqual("Thistlewane", b.MonthName);
            Assert.AreEqual(1, b.Day);
            var c = OWCalendar.BreakdownStep(KIRROWMOSS, 30 + 28); // Thistlewane has 28
            Assert.AreEqual("Greening", c.MonthName);
            Assert.AreEqual(1, c.Day);
        }

        [Test]
        public void Kirrowmoss_FreeWeekAnchorsAtStepZeroAndRunsAcrossMonthBoundaries()
        {
            var k = KIRROWMOSS;
            Assert.AreEqual("Ondel", OWCalendar.WeekdayForStep(k, 0));
            Assert.AreEqual("Hearth", OWCalendar.WeekdayForStep(k, 5));
            Assert.AreEqual("Ondel", OWCalendar.WeekdayForStep(k, 6));
            Assert.AreEqual("Ondel", OWCalendar.WeekdayForStep(k, 30)); // 30 mod 6 = 0: crosses months without reset
            Assert.AreEqual("Hearth", OWCalendar.WeekdayForStep(k, -1)); // negative steps stay periodic
        }

        // ---- Intercalary days (rule 8) ----

        [Test]
        public void Harptos_FestivalDaysRenderThroughFormatIntercalaryByName()
        {
            // Year 0 (leap): Hammer 30 days, then Midwinter at offset 30.
            Assert.AreEqual("Midwinter, 0 DR", OWCalendar.FormatStep(HARPTOS, 30));
            var b = OWCalendar.BreakdownStep(HARPTOS, 30);
            Assert.AreEqual("Midwinter", b.Intercalary);
            Assert.IsNull(b.Month);
            Assert.IsNull(b.Day);
        }

        [Test]
        public void Harptos_ShieldmeetExistsOnlyInLeapYears_RightAfterMidsummer()
        {
            var h = HARPTOS;
            // Offset of Midsummer in any year: 7 months x 30 + Midwinter + Greengrass = 212.
            long y1372 = OWCalendar.YearStartStep(h, 1372); // 1372 mod 4 = 0: leap
            Assert.AreEqual("Midsummer, 1372 DR", OWCalendar.FormatStep(h, y1372 + 212));
            Assert.AreEqual("Shieldmeet, 1372 DR", OWCalendar.FormatStep(h, y1372 + 213));
            long y1373 = OWCalendar.YearStartStep(h, 1373); // not leap: no Shieldmeet
            Assert.AreEqual("1 Eleasis, 1373 DR", OWCalendar.FormatStep(h, y1373 + 213));
        }

        [Test]
        public void Harptos_ParsesAnIntercalaryDate_AndRejectsItInANonLeapYear()
        {
            var h = HARPTOS;
            var ok = OWCalendar.ParseDate(h, "Shieldmeet 1372");
            Assert.IsFalse(OWCalendar.IsParseError(ok));
            Assert.AreEqual("Shieldmeet, 1372 DR", OWCalendar.FormatStep(h, ok.Step));
            var bad = OWCalendar.ParseDate(h, "Shieldmeet 1373");
            Assert.IsTrue(OWCalendar.IsParseError(bad));
        }

        [Test]
        public void Harptos_NestedTendays_WeekdayFromDayOfMonth_IntercalaryDaysHaveNone()
        {
            var h = HARPTOS;
            Assert.AreEqual("1st", OWCalendar.WeekdayForStep(h, 0)); // 1 Hammer
            Assert.AreEqual("10th", OWCalendar.WeekdayForStep(h, 9));
            Assert.AreEqual("1st", OWCalendar.WeekdayForStep(h, 10)); // tenday resets
            Assert.IsNull(OWCalendar.WeekdayForStep(h, 30)); // Midwinter: no tenday slot
        }

        // ---- The Shire weekday lock (rule 7: per-day in_week) ----

        [Test]
        public void Shire_Counts364WeekDaysPerYear_EveryDateKeepsItsWeekdayAcrossYears()
        {
            // 360 month days + 2 Yule + 1&2 Lithe (in week) = 364 = 52 x 7; Mid-year's Day + Overlithe are out.
            var shire = SHIRE;
            string DateWeekday(long year, string month, long day)
            {
                var r = OWCalendar.ParseDate(shire, day + " " + month + " " + year);
                if (OWCalendar.IsParseError(r)) Assert.Fail(r.Error);
                return OWCalendar.WeekdayForStep(shire, r.Step);
            }
            string w1 = DateWeekday(1, "Afteryule", 1);
            foreach (long y in new long[] { 2, 3, 4, 5, 9, 100, 1401 }) // 4, 100: leap years included
                Assert.AreEqual(w1, DateWeekday(y, "Afteryule", 1));
            string mid = DateWeekday(1, "Halimath", 22); // Bilbo's birthday
            foreach (long y in new long[] { 2, 4, 5, 1401 })
                Assert.AreEqual(mid, DateWeekday(y, "Halimath", 22));
        }

        [Test]
        public void Shire_TheOutOfWeekDaysThemselvesCarryNoWeekday()
        {
            var r = OWCalendar.ParseDate(SHIRE, "Mid-year's Day 1401");
            Assert.IsFalse(OWCalendar.IsParseError(r));
            Assert.IsNull(OWCalendar.WeekdayForStep(SHIRE, r.Step));
        }

        [Test]
        public void Shire_InWeekIntercalaryDaysDoCarryWeekdays()
        {
            var r = OWCalendar.ParseDate(SHIRE, "2 Yule 1401");
            Assert.IsFalse(OWCalendar.IsParseError(r));
            Assert.IsNotNull(OWCalendar.WeekdayForStep(SHIRE, r.Step));
        }

        // ---- Format token rules (rule 8) ----

        [Test]
        public void FormatTokens_UnknownRenderLiterally_KnownInapplicableRenderEmptyWithCollapse()
        {
            var cfg = With(MinimalJson, "format", "{day} {month}, {year} {era} {frob}");
            Assert.AreEqual("454 TA {frob}", OWCalendar.FormatStep(cfg, 454));
        }

        [Test]
        public void FormatTokens_AMonthlessCalendarWithAMonthfulFormatCollapsesCleanly()
        {
            var cfg = With(MinimalJson, "format", "{day} {month}, {year} {era}");
            Assert.AreEqual("454 TA", OWCalendar.FormatStep(cfg, 454));
        }

        [Test]
        public void FormatTokens_StepAlwaysExposesTheRawInteger()
        {
            var cfg = With(MinimalJson, "format", "{year} {era} (step {step})");
            Assert.AreEqual("454 TA (step 454)", OWCalendar.FormatStep(cfg, 454));
        }

        [Test]
        public void FormatTokens_DefaultsExistWhenFormatIsAbsent()
        {
            var o = JObject.Parse(MinimalJson);
            var noFmt = new JObject { ["version"] = 1, ["grain"] = "year", ["eras"] = o["eras"].DeepClone() };
            Assert.AreEqual("454 TA", OWCalendar.FormatStep(Read(noFmt.ToString()), 454));
        }

        // ---- Parser rules (rule 9) ----

        [Test]
        public void Parser_BareNumbersAreAlwaysTheRawStoredStep()
        {
            var r = OWCalendar.ParseDate(KIRROWMOSS, "454");
            Assert.IsTrue(!OWCalendar.IsParseError(r) && r.Step == 454);
            var n = OWCalendar.ParseDate(KIRROWMOSS, "-480");
            Assert.IsTrue(!OWCalendar.IsParseError(n) && n.Step == -480);
        }

        [Test]
        public void Parser_MatchesCalendarWordsCaseInsensitively()
        {
            var r = OWCalendar.ParseDate(KIRROWMOSS, "14 thistlewane, year 22 ft");
            Assert.IsTrue(!OWCalendar.IsParseError(r) && r.Step == 7603);
        }

        [Test]
        public void Parser_PartialDatesSnapToThePeriodStart()
        {
            var k = KIRROWMOSS;
            var yearOnly = OWCalendar.ParseDate(k, "Year 22 FT");
            Assert.IsTrue(!OWCalendar.IsParseError(yearOnly) && yearOnly.Step == 21 * 360);
            var monthOnly = OWCalendar.ParseDate(k, "Thistlewane 22"); // 22 fits as a day -> needs now
            Assert.IsTrue(OWCalendar.IsParseError(monthOnly)); // no now set: never guess
            var monthYear = OWCalendar.ParseDate(k, "Thistlewane 400"); // 400 can't be a day -> year 400
            Assert.IsTrue(!OWCalendar.IsParseError(monthYear) && OWCalendar.FormatStep(k, monthYear.Step) == "1 Thistlewane, Year 400 FT");
        }

        [Test]
        public void Parser_AMonthDayWithNoYearResolvesAgainstTheYearContainingTheNow()
        {
            var r = OWCalendar.ParseDate(KIRROWMOSS, "Thistlewane 14", now: 7900); // now inside year 22 FT
            Assert.IsTrue(!OWCalendar.IsParseError(r) && r.Step == 7603);
        }

        [Test]
        public void Parser_EraLessYearsPreferTheEraContainingTheNow_ElseTheLatestOpenEra()
        {
            // Both KR and OC are closed; FT is the only open era.
            var r = OWCalendar.ParseDate(KIRROWMOSS, "Year 12");
            Assert.IsTrue(!OWCalendar.IsParseError(r) && r.Step == 11 * 360);
            // With a now inside OC, the OC reading wins.
            var oc = OWCalendar.ParseDate(KIRROWMOSS, "Year 2", now: -3000);
            Assert.IsTrue(!OWCalendar.IsParseError(oc) && oc.Step == -3600 + 360);
        }

        [Test]
        public void Parser_DownEraYearsParseBackAcrossThePivot()
        {
            var r = OWCalendar.ParseDate(DUNE, "108 B.G.");
            Assert.IsTrue(!OWCalendar.IsParseError(r) && r.Step == -108);
        }

        [Test]
        public void Parser_TheStabilityProperty_FormatParseFormatEqualsFormat()
        {
            foreach (var json in new[] { KirrowmossJson, HarptosJson, ShireJson, DuneJson, VessachJson })
            {
                var cfg = Read(json);
                foreach (long n in new long[] { -3601, -108, -1, 0, 30, 213, 7603, 500596 })
                {
                    string f = OWCalendar.FormatStep(cfg, n);
                    var p = OWCalendar.ParseDate(cfg, f, now: 0);
                    if (OWCalendar.IsParseError(p))
                        Assert.Fail("parse failed on \"" + f + "\" (" + cfg.Eras.FirstOrDefault()?.Name + "): " + p.Error);
                    Assert.AreEqual(f, OWCalendar.FormatStep(cfg, p.Step));
                }
            }
        }

        [Test]
        public void Parser_AMidYearDownPivotRoundTrips_EveryStep()
        {
            // npm 4.42.3's parity vector (sdk 67df160): formatStep then parseDate gives the step back on both
            // sides of a down era's pivot, at the year's start and mid-year, for every step from -55 to 65.
            foreach (var pivot in new[] { 0, 5 })
            {
                var cfg = Read(@"{ version: 1, grain: 'day', bundles: { months: { names: ['Frost', 'Thaw'], lengths: [10, 10] } },
                    eras: [ { name: 'Before', abbrev: 'BG', start: " + pivot + @", direction: 'down' },
                            { name: 'After', abbrev: 'AG', start: " + pivot + " } ] }");
                for (long step = -55; step <= 65; step++)
                {
                    string f = OWCalendar.FormatStep(cfg, step);
                    var r = OWCalendar.ParseDate(cfg, f);
                    Assert.IsFalse(OWCalendar.IsParseError(r), "pivot " + pivot + ", step " + step + ": \"" + f + "\" did not parse");
                    Assert.AreEqual(step, r.Step, "pivot " + pivot + ": \"" + f + "\"");
                }
            }
        }

        [Test]
        public void Parser_ADateTheTypedEraDoesNotHold_IsRefused()
        {
            // npm 4.42.4's vector (sdk a119342, from Temper's case): a typed era names the date's era.
            var cfg = Read(@"{ version: 1, grain: 'day', bundles: { months: { names: ['Frost', 'Thaw'], lengths: [10, 10] } },
                eras: [ { name: 'Before Gate', abbrev: 'BG', start: 5, direction: 'down' }, { name: 'After Gate', abbrev: 'AG', start: 5 } ] }");
            // year 1 BG holds 1 to 5 Frost only; the absolute reading landed in AG (step 39) before
            Assert.IsTrue(OWCalendar.IsParseError(OWCalendar.ParseDate(cfg, "10 Thaw, 1 BG")));
            // there is no year 0 BG; the absolute reading landed on 3 Frost, 1 BG (step 2) before
            Assert.IsTrue(OWCalendar.IsParseError(OWCalendar.ParseDate(cfg, "3 Frost, 0 BG")));
            Assert.AreEqual(2, OWCalendar.ParseDate(cfg, "3 Frost, 1 BG").Step);
            Assert.AreEqual(19, OWCalendar.ParseDate(cfg, "10 Thaw, 1 AG").Step);
        }

        [Test]
        public void Parser_RejectsGarbageWithAReason_NeverAGuess()
        {
            Assert.IsTrue(OWCalendar.IsParseError(OWCalendar.ParseDate(KIRROWMOSS, "the fifth of never")));
            Assert.IsTrue(OWCalendar.IsParseError(OWCalendar.ParseDate(KIRROWMOSS, "")));
        }

        // ---- Validation + defensive read ----

        [Test]
        public void Validation_RejectsYearStepsAlongsideMonths()
        {
            var o = JObject.Parse(KirrowmossJson);
            o["bundles"]["year"] = new JObject { ["steps"] = 360 };
            var bad = Read(o.ToString());
            Assert.IsTrue(OWCalendar.Validate(bad).Any(e => e.Contains("year.steps")));
        }

        [Test]
        public void Validation_RejectsOverlappingEraDomainsLoudly()
        {
            var bad = Read(@"{ version: 1, grain: 'year',
                eras: [ { name: 'A', start: 0, end: 100 }, { name: 'B', start: 50 } ] }");
            Assert.IsTrue(OWCalendar.Validate(bad).Any(e => e.Contains("overlap")));
        }

        [Test]
        public void Validation_EraGapsAreLegalAndSilent()
        {
            CollectionAssert.IsEmpty(OWCalendar.Validate(VESSACH));
        }

        [Test]
        public void Validation_RejectsIntercalaryWithoutMonths()
        {
            var bad = Read(@"{ version: 1, grain: 'year', intercalary: [{ name: 'Floating Day', after_month: 0 }] }");
            Assert.IsTrue(OWCalendar.Validate(bad).Any(e => e.Contains("Festival days need months")));
        }

        [Test]
        public void Validation_AllFixtureCalendarsValidateClean()
        {
            foreach (var json in AllFixtureJson)
                CollectionAssert.IsEmpty(OWCalendar.Validate(Read(json)), json);
        }

        // ---- Surface helpers (2B sweep) ----

        [Test]
        public void SurfaceHelpers_FormatYearLabel_CompactForTicks_BareAbsoluteYearOutsideEras()
        {
            Assert.AreEqual("22 FT", OWCalendar.FormatYearLabel(KIRROWMOSS, 7603));
            Assert.AreEqual("108 B.G.", OWCalendar.FormatYearLabel(DUNE, -108));
            Assert.AreEqual("-5000", OWCalendar.FormatYearLabel(VESSACH, -5000)); // the era gap
        }

        [Test]
        public void SurfaceHelpers_ApproxYearLength_FromMonthsAndFixedIntercalary_OrYearSteps_ElseNull()
        {
            Assert.AreEqual(360, OWCalendar.ApproxYearLength(KIRROWMOSS));
            Assert.AreEqual(365, OWCalendar.ApproxYearLength(HARPTOS)); // 360 + 5 fixed festivals; Shieldmeet excluded
            Assert.IsNull(OWCalendar.ApproxYearLength(MINIMAL));
        }

        [Test]
        public void SurfaceHelpers_FormatDuration_YearsWhenTheSpanReachesAYear_ElseStepWords()
        {
            var k = KIRROWMOSS;
            Assert.AreEqual("19 years", OWCalendar.FormatDuration(k, 360 * 19));
            Assert.AreEqual("1.5 years", OWCalendar.FormatDuration(k, 540));
            Assert.AreEqual("14 days", OWCalendar.FormatDuration(k, 14));
            Assert.AreEqual("1 day", OWCalendar.FormatDuration(k, 1));
            // Year-grain worlds: the step word IS "year".
            Assert.AreEqual("19 years", OWCalendar.FormatDuration(MINIMAL, 19));
            // A renamed step speaks the culture's word.
            var sol = Read(@"{ version: 1, grain: 'day', step_name: 'sol' }");
            Assert.AreEqual("14 sols", OWCalendar.FormatDuration(sol, 14));
        }

        // ---- Defensive read (wild configs fall back, never a broken axis) ----

        [Test]
        public void DefensiveRead_ReturnsNullOnGarbage()
        {
            Assert.IsNull(OWCalendar.ReadConfig(null));
            Assert.IsNull(OWCalendar.ReadConfig(JValue.CreateNull())); // JS null
            Assert.IsNull(OWCalendar.ReadConfig("a string"));
            Assert.IsNull(OWCalendar.ReadConfig(JObject.Parse("{ version: 3, grain: 'day' }")));
            Assert.IsNull(OWCalendar.ReadConfig(JObject.Parse("{ version: 1 }")));
            Assert.IsNull(OWCalendar.ReadConfig(JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['a'], lengths: [1, 2] } } }")));
            Assert.IsNull(OWCalendar.ReadConfig(JObject.Parse("{ version: 1, grain: 'day', eras: [{ name: 'X' }] }")));
        }

        [Test]
        public void DefensiveRead_AcceptsEveryFixture()
        {
            // JS: readCalendarConfig(JSON.parse(JSON.stringify(cfg))): a strict-JSON round trip.
            foreach (var json in AllFixtureJson)
            {
                string strict = JObject.Parse(json).ToString(Newtonsoft.Json.Formatting.None);
                Assert.IsNotNull(OWCalendar.ReadConfig(JObject.Parse(strict)), strict);
            }
        }

        // ---- Malformed step sizes (S367 critic K1): a bad step size used to pass the reader and hang ----

        [Test]
        public void MalformedStepSizes_TheReaderRejectsNonPositiveFractionalOrNonFiniteDivisorsAndStepSizes()
        {
            var bad = new List<JObject>
            {
                JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['Frost', 'Thaw'], lengths: [-30, 10] } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['Frost'], lengths: [0] } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['Frost'], lengths: [2.5] } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['Frost'], lengths: ['30'] } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { year: { steps: -5 } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { year: { steps: 0 } } }"),
                new JObject { ["version"] = 1, ["grain"] = "day",
                    ["bundles"] = new JObject { ["year"] = new JObject { ["steps"] = double.PositiveInfinity } } },
                JObject.Parse("{ version: 1, grain: 'day', bundles: { year: {} } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { week: { length: 0, names: [] } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { week: { length: 3, names: 'abc' } } }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['a'], lengths: [30] } }, intercalary: [{ name: 'Leap', after_month: 1, every_years: 0 }] }"),
                JObject.Parse("{ version: 1, grain: 'day', bundles: { months: { names: ['a'], lengths: [30] } }, intercalary: [{ name: 'Leap', after_month: 1, every_years: -4 }] }"),
            };
            foreach (var cfg in bad)
                Assert.IsNull(OWCalendar.ReadConfig(cfg), cfg.ToString(Newtonsoft.Json.Formatting.None));
        }

        [Test]
        public void MalformedStepSizes_AbsoluteYearAndFormatStepTerminateOnAConfigThatSlipsTheReader()
        {
            // JS builds a getter tripwire that throws after a million reads. In C# a config slips the reader only
            // when built by hand, so the test is that these calls return at all (the harness and Unity's runner
            // time out a hang); a long is always finite.
            var negMonth = new CalendarConfig
            {
                Version = 1, Grain = "day",
                Bundles = new CalendarBundles
                {
                    Months = new CalendarMonths { Names = new List<string> { "Frost", "Thaw" }, Lengths = new List<long> { -30, 10 } },
                },
            };
            OWCalendar.AbsoluteYear(negMonth, 100);
            Assert.IsInstanceOf<string>(OWCalendar.FormatStep(negMonth, 100));
            var negYear = new CalendarConfig
            {
                Version = 1, Grain = "day",
                Bundles = new CalendarBundles { Year = new CalendarYearBundle { Steps = -5 } },
            };
            OWCalendar.AbsoluteYear(negYear, 100);
            Assert.IsInstanceOf<string>(OWCalendar.FormatStep(negYear, 100));
        }

        [Test]
        public void MalformedStepSizes_TheBoundNeverBitesAValidCalendarFarFromZero()
        {
            var h = HARPTOS;
            foreach (long step in new long[] { 0, 1, -1, 1_000_000_000, -1_000_000_000, 1_000_000_000_000, -1_000_000_000_000 })
            {
                long y = OWCalendar.AbsoluteYear(h, step);
                Assert.LessOrEqual(OWCalendar.YearStartStep(h, y), step);
                Assert.Greater(OWCalendar.YearStartStep(h, y + 1), step);
            }
            // One step per month, a festival every year: the old seed was off by half the step count.
            var dense = Read(@"{ version: 1, grain: 'day',
                bundles: { months: { names: ['a'], lengths: [1] } },
                intercalary: [{ name: 'F', after_month: 1, every_years: 1 }, { name: 'G', after_month: 0, every_years: 3 }] }");
            foreach (long step in new long[] { 0, 7, -7, 5_000_000, -5_000_000 })
            {
                long y = OWCalendar.AbsoluteYear(dense, step);
                Assert.LessOrEqual(OWCalendar.YearStartStep(dense, y), step);
                Assert.Greater(OWCalendar.YearStartStep(dense, y + 1), step);
            }
        }

        // ---- A real world: Moppetopia's Treaty Reckoning (not in the JS suite) ----

        [Test]
        public void TreatyReckoning_Year453ReadsYear203YSL()
        {
            var cfg = Read(TreatyReckoningJson);
            CollectionAssert.IsEmpty(OWCalendar.Validate(cfg));
            Assert.AreEqual("Year 203 YSL", OWCalendar.FormatStep(cfg, 453));
            Assert.AreEqual("203 YSL", OWCalendar.FormatYearLabel(cfg, 453));
            Assert.AreEqual("Year 0 YSL", OWCalendar.FormatStep(cfg, 250)); // the pivot belongs to the up-era
            Assert.AreEqual("Year 1 BT", OWCalendar.FormatStep(cfg, 249)); // no year zero on the down side
            Assert.AreEqual(453, Step(OWCalendar.ParseDate(cfg, "Year 203 YSL")));
            Assert.AreEqual(249, Step(OWCalendar.ParseDate(cfg, "Year 1 BT")));
        }

        // ---- The profile's own words (schema-dist profiles/calendar.md, dist.31) ----

        [Test]
        public void Profile_ErasOrIntercalaryThatIsNotAList_IsNotACalendar()
        {
            const string year = @"{""version"": 1, ""grain"": ""year""}";
            foreach (var key in new[] { "eras", "intercalary" })
            foreach (var value in new JToken[] { new JValue(false), new JValue(0), new JValue("") })
            {
                var o = JObject.Parse(year);
                o[key] = value;
                Assert.IsNull(OWCalendar.ReadConfig(o), $"{key}: {value.ToString(Newtonsoft.Json.Formatting.None)} is not a list");
            }
            var nulls = JObject.Parse(year);
            nulls["eras"] = JValue.CreateNull();
            nulls["intercalary"] = JValue.CreateNull();
            Assert.IsNotNull(OWCalendar.ReadConfig(nulls), "null reads as absent");
        }

        [Test]
        public void Profile_AtEqualStarts_TheLaterOfOneDirectionWins()
        {
            // TS 4.42.2: latest start, then up over down, then the later in the list. "Down" is exactly
            // "down": a missing, null or other-spelled direction counts up.
            foreach (var b in new[] { @"""direction"": ""up""", @"""direction"": ""Up""", @"""direction"": null", @"""x"": 0" })
            {
                var cfg = Read(@"{""version"": 1, ""grain"": ""year"", ""eras"": [
                    { ""name"": ""A"", ""start"": 0, ""end"": 10 },
                    { ""name"": ""B"", ""start"": 0, " + b + " } ] }");
                Assert.AreEqual("4 B", OWCalendar.FormatStep(cfg, 3), "two up eras at one start: the later in the list (" + b + ")");
            }

            var downs = Read(@"{""version"": 1, ""grain"": ""year"", ""eras"": [
                { ""name"": ""A"", ""start"": 0, ""direction"": ""down"" },
                { ""name"": ""B"", ""start"": 0, ""direction"": ""down"" } ] }");
            Assert.AreEqual("3 B", OWCalendar.FormatStep(downs, -3), "two down eras at one start: the later in the list");
        }

        [Test]
        public void Profile_ADownEraCountsFromTheYearBeforeItsPivot_NoYearZeroMidYear()
        {
            // TS 4.42.2: counted from the year holding the step just before the pivot, which is year_base.
            var cfg = Read(@"{""version"": 1, ""grain"": ""day"",
                ""bundles"": { ""months"": { ""names"": [""Frost"", ""Thaw""], ""lengths"": [10, 10] } },
                ""eras"": [ { ""name"": ""Before"", ""abbrev"": ""BG"", ""start"": 5, ""direction"": ""down"" },
                            { ""name"": ""After"", ""abbrev"": ""AG"", ""start"": 5 } ] }");
            Assert.AreEqual(1, OWCalendar.BreakdownStep(cfg, 2).Year, "a mid-year pivot's own year is year 1, not 0");
            Assert.AreEqual(2, OWCalendar.BreakdownStep(cfg, -18).Year, "the year before it is year 2");

            var boundary = Read(@"{""version"": 1, ""grain"": ""year"", ""eras"": [
                { ""name"": ""Before"", ""start"": 0, ""direction"": ""down"" }, { ""name"": ""After"", ""start"": 0 } ] }");
            Assert.AreEqual(1, OWCalendar.BreakdownStep(boundary, -1).Year, "a pivot on a year's first step reads as it always did");

            // And a date read aloud parses back to its own step, on both sides of the pivot and on a boundary pivot.
            foreach (var step in new long[] { 2, 4, -18, -1, 5, 7, 25 })
                Assert.AreEqual(step, Step(OWCalendar.ParseDate(cfg, OWCalendar.FormatStep(cfg, step))), "round trip at " + step);
            foreach (var step in new long[] { -1, -5, 0, 3 })
                Assert.AreEqual(step, Step(OWCalendar.ParseDate(boundary, OWCalendar.FormatStep(boundary, step))), "boundary round trip at " + step);
        }
    }
}
