// A stored calendar does not set what one date label costs. The shared cases (Fixtures/calendar/calendar-cost.cases.json,
// the same bytes as the TypeScript SDK's test/fixtures/calendar-cost.cases.json and the Python package's copy, its
// sha256 pinned here): a long run of white space in a template or a name, a long run of marks at a label's edge, and a
// month of millions of days, each answered with its words inside the bound. Ported from @onlyworlds/sdk 4.201.2
// (69bd5bb), test/calendar-cost.test.mjs; then what the cases cannot hold, each answered as the list of days answered.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    public class OWCalendarCostTests
    {
        /// <summary>The cases file's sha256, as the TypeScript and Python suites hold it.</summary>
        private const string CasesSha256 = "1e326d378cbdc6db91deba2219ed07cf67ad72211653bd25219f963fc81ebc0f";

        private const int CaseCount = 8;

        private static JObject _fixture;

        private static string FixturePath
        {
            get
            {
                var root = TestPackage.Root();
                return root == null ? null : Path.Combine(root, "Tests", "Editor", "Fixtures", "calendar", "calendar-cost.cases.json");
            }
        }

        private static JObject Fixture
        {
            get
            {
                if (_fixture != null) return _fixture;
                var path = FixturePath;
                return path == null || !File.Exists(path) ? null : _fixture = JObject.Parse(File.ReadAllText(path));
            }
        }

        /// <summary>One test per shared case, named after it.</summary>
        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = Fixture;
            if (fixture == null)
            {
                yield return new TestCaseData(string.Empty).SetName("CalendarCost_FixtureNotFound");
                yield break;
            }

            foreach (var c in fixture["cases"].Cast<JObject>())
            {
                var name = (string)c["name"];
                var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) && ch < 0x80 ? ch : '_').ToArray());
                yield return new TestCaseData(name).SetName("CalendarCost_" + safe);
            }
        }

        /// <summary>The cases' shorthand undone: <c>{"$repeat": [[piece, times], ...]}</c> is the pieces joined, each repeated.</summary>
        private static JToken Expand(JToken v)
        {
            if (v is JArray a) return new JArray(a.Select(Expand));
            if (v is JObject o)
            {
                if (o["$repeat"] is JArray parts)
                {
                    var text = new StringBuilder();
                    foreach (JArray p in parts)
                        for (var i = 0; i < (int)p[1]; i++) text.Append((string)p[0]);
                    return new JValue(text.ToString());
                }

                var copy = new JObject();
                foreach (var prop in o.Properties()) copy[prop.Name] = Expand(prop.Value);
                return copy;
            }

            return v.DeepClone();
        }

        /// <summary>A parse in the cases' shape: <c>{"step": n}</c>, or <c>{"error": why}</c>.</summary>
        private static JObject ParseShape(ParseResult r) =>
            OWCalendar.IsParseError(r) ? new JObject { ["error"] = r?.Error } : new JObject { ["step"] = r.Step };

        [Test]
        public void TheCasesFile_IsTheSharedBytes()
        {
            var path = FixturePath;
            Assert.IsTrue(path != null && File.Exists(path), "the vendored calendar cost cases were not found: " + path);
            using var sha = SHA256.Create();
            var hash = System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(CasesSha256, hash,
                "the vendored cases are not the packages' bytes: re-vendor them (a checkout's line endings change the bytes)");
            Assert.AreEqual(CaseCount, Fixture["cases"].Count(), $"the cases file holds {CaseCount} cases");
            Assert.AreEqual(50, (int)Fixture["underMs"]);
        }

        /// <summary>
        /// Before the first timed call the kernel's code is compiled once, on a small calendar of its own, so a
        /// case measures what its calendar costs and not the runtime's first compile of the formatter.
        /// </summary>
        [OneTimeSetUp]
        public void CompileTheKernelOnce()
        {
            var warm = OWCalendar.ReadConfig(JObject.Parse(
                "{\"version\":1,\"grain\":\"day\",\"bundles\":{\"months\":{\"names\":[\"A\"],\"lengths\":[3]},\"week\":{\"length\":2,\"names\":[\"x\",\"y\"]}},\"intercalary\":[{\"name\":\"P\",\"after_month\":0,\"in_week\":false}],\"eras\":[{\"name\":\"E\",\"start\":0}]}"));
            var label = OWCalendar.FormatStep(warm, 2);
            OWCalendar.WeekdayForStep(warm, 2);
            OWCalendar.ParseDate(warm, label);
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryCase_AnswersItsWords_InsideTheBound(string name)
        {
            Assert.IsNotNull(Fixture, "the vendored calendar cost cases were not found");
            var c = Fixture["cases"].Cast<JObject>().Single(x => (string)x["name"] == name);
            var cfg = OWCalendar.ReadConfig(Expand(c["cfg"]));
            Assert.IsNotNull(cfg, "the case is a calendar the reader accepts");
            var step = (long)c["step"];
            var call = (string)c["call"];
            var watch = Stopwatch.StartNew();
            JToken got;
            if (call == "format") got = new JValue(OWCalendar.FormatStep(cfg, step));
            else if (call == "weekday") got = new JValue(OWCalendar.WeekdayForStep(cfg, step));
            else
            {
                var label = OWCalendar.FormatStep(cfg, step);
                got = new JArray(label, ParseShape(OWCalendar.ParseDate(cfg, label)));
            }

            watch.Stop();
            var ms = watch.Elapsed.TotalMilliseconds;
            TestContext.WriteLine($"{name}: {ms:F1} ms");
            var want = Expand(c["want"]);
            Assert.IsTrue(JToken.DeepEquals(want, got), $"{name}: answered {Short(got)}, the cases say {Short(want)}");
            var underMs = (int)Fixture["underMs"];
            Assert.Less(ms, underMs, $"{name}: {ms:F0} ms");
        }

        private static string Short(JToken t)
        {
            var s = t.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length <= 200 ? s : s.Substring(0, 200) + $"... ({s.Length} chars)";
        }

        /// <summary>
        /// A step word with a long run of white space inside, which the shared cases do not hold: this package trims as
        /// JavaScript trims with a regular expression of its own (TypeScript's <c>trim</c> is the runtime's), and a run that
        /// does not reach the end must not cost what the run's length squared costs.
        /// </summary>
        [Test]
        public void AStepWordWithALongRunOfWhiteSpaceInside_IsTrimmedInsideTheBound()
        {
            var name = "a" + new string(' ', 20000) + "b";
            var cfg = OWCalendar.ReadConfig(new JObject { ["version"] = 1, ["grain"] = "day", ["step_name"] = "  " + name + "\u3000" });
            Assert.IsNotNull(cfg);
            var watch = Stopwatch.StartNew();
            var got = OWCalendar.FormatDuration(cfg, 5L);
            watch.Stop();
            TestContext.WriteLine($"step word: {watch.Elapsed.TotalMilliseconds:F1} ms");
            Assert.AreEqual("5 " + name + "s", got);
            Assert.Less(watch.Elapsed.TotalMilliseconds, (int)Fixture["underMs"]);
        }

        // -- What the cases cannot hold: every answer as before ----------------------------

        /// <summary>A small fixed generator, so the made inputs are the same on every runtime.</summary>
        private sealed class Lcg
        {
            private ulong _s;
            public Lcg(ulong seed) { _s = seed; }
            public int Next(int n)
            {
                _s = _s * 6364136223846793005UL + 1442695040888963407UL;
                return (int)((_s >> 33) % (ulong)n);
            }
        }

        // The renderer's collapse as it was before 4.201.2, held here as the oracle: the regular expressions verbatim.
        private const string JsWs = @"[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]";
        private static readonly System.Text.RegularExpressions.Regex OldToken = new System.Text.RegularExpressions.Regex(@"\{([a-z_]+)\}");
        private static readonly System.Text.RegularExpressions.Regex OldCollapse = new System.Text.RegularExpressions.Regex(JsWs + @"*[,;\u00b7]?" + JsWs + "*\ue000" + JsWs + @"*[,;\u00b7]?" + JsWs + "*");
        private static readonly System.Text.RegularExpressions.Regex OldMultiWs = new System.Text.RegularExpressions.Regex(JsWs + "{2,}");
        private static readonly System.Text.RegularExpressions.Regex OldTrim = new System.Text.RegularExpressions.Regex(@"\A" + JsWs + "+|" + JsWs + @"+\z");
        private static readonly System.Text.RegularExpressions.Regex OldEdges = new System.Text.RegularExpressions.Regex(@"\A[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff,;\u00b7]+|[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff,;\u00b7]+\z");

        private static string OldRender(string template, IDictionary<string, string> values)
        {
            var known = new[] { "day", "month", "weekday", "year", "era", "step", "intercalary" };
            var output = OldToken.Replace(template, m =>
            {
                var token = m.Groups[1].Value;
                if (System.Array.IndexOf(known, token) < 0) return m.Value;
                values.TryGetValue(token, out var v);
                return string.IsNullOrEmpty(v) ? "\ue000" : v;
            });
            output = OldCollapse.Replace(output, " ");
            output = OldMultiWs.Replace(output, " ");
            output = OldTrim.Replace(output, "");
            return OldEdges.Replace(output, "");
        }

        /// <summary>
        /// Every made template formats as the regular expressions formatted it: runs of white space of JavaScript's kinds
        /// (and U+0085, which is not one), marks on both sides of an empty token, two empty tokens side by side, a token at
        /// either edge, and an unknown token, which stays as written. 4,000 templates of up to twelve pieces; each also
        /// trims as the trim's regular expression trimmed it.
        /// </summary>
        [Test]
        public void EveryMadeTemplate_FormatsAsTheRegularExpressionsDid()
        {
            var pieces = new[] { " ", "  ", ",", ";", "\u00b7", ", ", " ,", "\u3000", "\ufeff", "\u0085", "\u00a0", "\t", "x", "Yr",
                "{era}", "{day}", "{weekday}", "{year}", "{bogus}", "{", "}" };
            var values = new Dictionary<string, string> { ["year"] = "370", ["step"] = "370" };
            var rng = new Lcg(20261124);
            var wrong = new List<string>();
            int withMarker = 0;
            for (var t = 0; t < 4000; t++)
            {
                var template = new StringBuilder();
                var count = rng.Next(13);
                for (var i = 0; i < count; i++) template.Append(pieces[rng.Next(pieces.Length)]);
                var text = template.ToString();
                if (text.Contains("{era}") || text.Contains("{day}") || text.Contains("{weekday}")) withMarker++;
                var cfg = OWCalendar.ReadConfig(new JObject { ["version"] = 1, ["grain"] = "year", ["format"] = text });
                Assert.IsNotNull(cfg, text);
                var want = OldRender(text, values);
                var got = OWCalendar.FormatStep(cfg, 370);
                if (want != got && wrong.Count < 10) wrong.Add($"{Escape(text)}: {Escape(got)}, before {Escape(want)}");
                var trimmed = OWCalendar.JsTrim(text);
                if (trimmed != OldTrim.Replace(text, "") && wrong.Count < 10) wrong.Add($"{Escape(text)} trims to {Escape(trimmed)}");
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong));
            Assert.Greater(withMarker, 2000, "most templates hold an empty token");
        }

        private static string Escape(string s) =>
            "\"" + string.Concat(s.Select(ch => ch < 0x20 || ch > 0x7e ? $"\\u{(int)ch:x4}" : ch.ToString())) + "\"";

        // The year as it was before 4.201.2, held here as the oracle: a list of every slot, in step order.
        private sealed class OldSlot
        {
            public int Month;
            public long Day;
            public CalendarIntercalary Intercalary;
        }

        private static List<OldSlot> OldYearSlots(CalendarConfig cfg, long y)
        {
            var slots = new List<OldSlot>();
            var months = cfg.Bundles?.Months;
            if (months == null) return slots;
            var inter = cfg.Intercalary ?? new List<CalendarIntercalary>();
            void Push(long after)
            {
                foreach (var ic in inter)
                    if (ic.AfterMonth == after && OWCalendar.IntercalaryInYear(cfg, ic, y)) slots.Add(new OldSlot { Intercalary = ic });
            }
            Push(0);
            for (var m = 1; m <= months.Names.Count; m++)
            {
                var len = m - 1 < months.Lengths.Count ? months.Lengths[m - 1] : 0;
                for (long d = 1; d <= len; d++) slots.Add(new OldSlot { Month = m, Day = d });
                Push(m);
            }
            return slots;
        }

        /// <summary>The weekday as the list answered it: the closed forms replaced by a walk over the years.</summary>
        private static string OldWeekday(CalendarConfig cfg, long step)
        {
            var week = cfg.Bundles?.Week;
            if (week == null || week.Length < 1 || week.Names == null || week.Names.Count != week.Length) return null;
            var y = OWCalendar.AbsoluteYear(cfg, step);
            var offset = step - OWCalendar.YearStartStep(cfg, y);
            var slots = cfg.Bundles?.Months != null ? OldYearSlots(cfg, y) : null;
            var slot = slots != null && offset >= 0 && offset < slots.Count ? slots[(int)offset] : null;
            if (week.Mode == "nested")
                return slot == null || slot.Intercalary != null ? null : week.Names[(int)OWCalendar.FloorMod(slot.Day - 1, week.Length)];
            if (slot?.Intercalary != null && !slot.Intercalary.InWeek) return null;
            var inter = cfg.Intercalary ?? new List<CalendarIntercalary>();
            long before = y * inter.Count(i => !i.InWeek && i.EveryYears == null);
            foreach (var ic in inter.Where(i => !i.InWeek && i.EveryYears != null))
            {
                if (y >= 0) { for (long k = 0; k < y; k++) if (OWCalendar.IntercalaryInYear(cfg, ic, k)) before++; }
                else { for (long k = y; k < 0; k++) if (OWCalendar.IntercalaryInYear(cfg, ic, k)) before--; }
            }
            long inYear = 0;
            if (slots != null)
                for (long i = 0; i < offset && i < slots.Count; i++)
                    if (slots[(int)i].Intercalary != null && !slots[(int)i].Intercalary.InWeek) inYear++;
            return week.Names[(int)OWCalendar.FloorMod(step - before - inYear, week.Length)];
        }

        private static IEnumerable<(string name, CalendarConfig cfg, bool readable)> MadeCalendars()
        {
            string[] json =
            {
                // Three months, a festival before the first out of the week, a leap day after the second, one every other year out of the week.
                "{\"version\":1,\"grain\":\"day\",\"bundles\":{\"months\":{\"names\":[\"Ash\",\"Birch\",\"Cedar\"],\"lengths\":[3,2,4]},\"week\":{\"length\":3,\"names\":[\"Mo\",\"Tu\",\"We\"]}},\"intercalary\":[{\"name\":\"Dawnfeast\",\"after_month\":0,\"in_week\":false},{\"name\":\"Leapday\",\"after_month\":2,\"every_years\":4},{\"name\":\"Gapday\",\"after_month\":3,\"every_years\":2,\"in_week\":false}],\"eras\":[{\"name\":\"Before\",\"abbrev\":\"BF\",\"start\":0,\"direction\":\"down\"},{\"name\":\"After\",\"abbrev\":\"AF\",\"start\":0}]}",
                // The same months with a nested week of four, a ragged last week, and two festivals after one month.
                "{\"version\":1,\"grain\":\"day\",\"bundles\":{\"months\":{\"names\":[\"Ash\",\"Birch\",\"Cedar\"],\"lengths\":[3,5,4]},\"week\":{\"length\":4,\"names\":[\"a\",\"b\",\"c\",\"d\"],\"mode\":\"nested\"}},\"intercalary\":[{\"name\":\"Firstrest\",\"after_month\":1},{\"name\":\"Secondrest\",\"after_month\":1,\"in_week\":false}]}",
                // Version 2: a leap day every 4, not every 100, yet every 400, read with an offset, out of the week.
                "{\"version\":2,\"grain\":\"day\",\"bundles\":{\"months\":{\"names\":[\"Frost\",\"Thaw\"],\"lengths\":[2,3]},\"week\":{\"length\":2,\"names\":[\"x\",\"y\"]}},\"intercalary\":[{\"name\":\"Turn\",\"after_month\":2,\"every_years\":4,\"except\":[100,400],\"offset\":1,\"in_week\":false}],\"eras\":[{\"name\":\"Reckoning\",\"abbrev\":\"R\",\"start\":-20,\"year_base\":0}]}",
                // One month and a long run of days, a festival at each end.
                "{\"version\":1,\"grain\":\"day\",\"bundles\":{\"months\":{\"names\":[\"Long\"],\"lengths\":[40]},\"week\":{\"length\":7,\"names\":[\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\"]}},\"intercalary\":[{\"name\":\"Opening\",\"after_month\":0,\"in_week\":false},{\"name\":\"Closing\",\"after_month\":1,\"every_years\":3}]}",
            };
            for (var i = 0; i < json.Length; i++)
            {
                var cfg = OWCalendar.ReadConfig(JObject.Parse(json[i]));
                Assert.IsNotNull(cfg, "made calendar " + i);
                yield return ("made " + i, cfg, true);
            }

            // Built by hand, past what the reader takes: fewer lengths than names, a month of 0 days and one of -2, and a
            // festival after a month the year does not have (it counts in the year's length and holds no slot).
            CalendarConfig Hand(List<long> lengths, long after) => new CalendarConfig
            {
                Version = 1,
                Grain = "day",
                Bundles = new CalendarBundles
                {
                    Months = new CalendarMonths { Names = new List<string> { "Ash", "Birch", "Cedar", "Dusk" }, Lengths = lengths },
                    Week = new CalendarWeek { Length = 3, Names = new List<string> { "p", "q", "r" } },
                },
                Intercalary = new List<CalendarIntercalary>
                {
                    new CalendarIntercalary { Name = "Rest", AfterMonth = 2, InWeek = false },
                    new CalendarIntercalary { Name = "Ghost", AfterMonth = after, InWeek = false },
                },
            };
            yield return ("by hand, fewer lengths", Hand(new List<long> { 3, 0, 2 }, 9), false);
            yield return ("by hand, a negative length", Hand(new List<long> { 3, -2, 4, 1 }, 4), false);
        }

        /// <summary>
        /// Every step of the made calendars, from 40 before step 0 to 80 after it, takes apart, falls on a weekday and
        /// formats as the list of days answered; on the calendars the reader takes, each label reads back to its step.
        /// </summary>
        [Test]
        public void EveryStepOfTheMadeCalendars_AnswersAsTheListOfDaysDid()
        {
            var wrong = new List<string>();
            int intercalaryDays = 0, skippedWeekdays = 0;
            foreach (var (name, cfg, readable) in MadeCalendars())
            {
                for (long step = -40; step <= 80; step++)
                {
                    var y = OWCalendar.AbsoluteYear(cfg, step);
                    var offset = step - OWCalendar.YearStartStep(cfg, y);
                    var slots = OldYearSlots(cfg, y);
                    var slot = offset >= 0 && offset < slots.Count ? slots[(int)offset] : null;
                    var b = OWCalendar.BreakdownStep(cfg, step);
                    var want = slot == null ? "none" : slot.Intercalary != null ? "i:" + slot.Intercalary.Name : $"{slot.Month}/{slot.Day}";
                    var got = b.Intercalary != null ? "i:" + b.Intercalary : b.Month != null ? $"{b.Month}/{b.Day}" : "none";
                    if (slot?.Intercalary != null) intercalaryDays++;
                    if (want != got) wrong.Add($"{name}, step {step}: {got}, the list {want}");
                    var weekday = OldWeekday(cfg, step);
                    if (weekday == null && slot?.Intercalary != null) skippedWeekdays++;
                    if (b.Weekday != weekday || OWCalendar.WeekdayForStep(cfg, step) != weekday)
                        wrong.Add($"{name}, step {step}: weekday {b.Weekday}, the list {weekday}");
                    if (!readable) continue;
                    var label = OWCalendar.FormatStep(cfg, step);
                    var back = OWCalendar.ParseDate(cfg, label);
                    if (OWCalendar.IsParseError(back) || back.Step != step)
                        wrong.Add($"{name}, step {step}: \"{label}\" reads back as {(OWCalendar.IsParseError(back) ? back.Error : back.Step.ToString())}");
                }
            }

            Assert.IsEmpty(wrong, $"{wrong.Count} answers moved:\n" + string.Join("\n", wrong.Take(20)));
            Assert.Greater(intercalaryDays, 50, "the steps cross many festival days");
            Assert.Greater(skippedWeekdays, 20, "and many outside the week");
        }

        /// <summary>
        /// A day the year does not hold is refused as the list refused it: a day past its month, day 0, a month with no
        /// days, and a leap day in a year without it.
        /// </summary>
        [Test]
        public void ADayTheYearDoesNotHold_IsRefusedAsBefore()
        {
            var cfg = MadeCalendars().First().cfg;
            Assert.AreEqual("Leapday does not occur in that year", OWCalendar.ParseDate(cfg, "Leapday 2 AF").Error, "year 1 has no leap day");
            Assert.AreEqual(OWCalendar.YearStartStep(cfg, 4) + 1 + 3 + 2, OWCalendar.ParseDate(cfg, "Leapday 5 AF").Step, "year 4 has");
            var hand = MadeCalendars().Single(c => c.name == "by hand, fewer lengths").cfg;
            Assert.AreEqual("that day does not exist in that month", OWCalendar.ParseDate(hand, "Birch 0").Error, "a month of 0 days");
            var negative = MadeCalendars().Single(c => c.name == "by hand, a negative length").cfg;
            Assert.AreEqual("that day does not exist in that month", OWCalendar.ParseDate(negative, "Birch 0").Error, "a month of -2 days");
            Assert.AreEqual(OWCalendar.YearStartStep(hand, 2) + 3, OWCalendar.ParseDate(hand, "Rest 2").Step);
            Assert.AreEqual(OWCalendar.YearStartStep(negative, 1) + 3 + 1 + 4 + 1, OWCalendar.ParseDate(negative, "Ghost 1").Step, "after the fourth month");
        }
    }
}
