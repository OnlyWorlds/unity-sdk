// The world calendar kernel: pure arithmetic, formatting and parsing over the world's integer time line.
//
// Ported from @onlyworlds/sdk 4.23.0 src/calendar/calendar.ts (commit 60f08f3), function for function.
// Atlas owns the `atlas_calendar` field and its meaning (its calendar design, sections 5 and 5b); every
// other tool reads it and never writes it. A kernel change starts in the npm SDK and is ported here, so a
// date reads the same in every tool.
//
// The binding rules, restated where they bite:
// - The number line is the only truth. A calendar names it; it never owns it.
// - The absolute year index is ZERO-based: step 0 opens absolute year 0. All division is Euclidean floor
//   (negatives depend on it).
// - FormatStep(step) is total and O(config): it never walks history.
// - Intercalary days belong to no month and render via `format_intercalary`.
// - A KNOWN token with no applicable value renders empty (with separator collapse); an UNKNOWN token renders
//   literally.
// - ParseDate matches calendar words case-insensitively; bare numbers are ALWAYS the raw stored step.
//   Guaranteed: Format(Parse(Format(n))) == Format(n). Exact integer recovery is NOT promised.
// - A missing or malformed calendar reads as null, never an error.
//
// C# notes: steps and years are `long` (JS numbers are doubles; every value a valid calendar produces is a
// safe integer, so the arithmetic is identical). Whitespace classes and trim follow JavaScript's `\s`.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Js = OnlyWorlds.Sdk.OWCalendarJs;

namespace OnlyWorlds.Sdk
{
    // ---- Config shape (persisted in the `atlas_calendar` element field) ----

    /// <summary>One era of a calendar.</summary>
    public sealed class CalendarEra
    {
        public string Name { get; set; }
        public string Abbrev { get; set; }
        /// <summary>Up-era: domain start (inclusive). Down-era: the PIVOT (exclusive upper bound).</summary>
        public long Start { get; set; }
        /// <summary>Up-era: domain end (exclusive); defaults to the next era's start. Down-era: lower bound
        /// (inclusive); defaults to minus infinity. Null when absent.</summary>
        public long? End { get; set; }
        /// <summary>true (default): years number from Start. false: pure label, {year} shows the absolute year index.</summary>
        public bool Restart { get; set; } = true;
        /// <summary>First year number of a restart era: 0 or 1 (default 1).</summary>
        public long YearBase { get; set; } = 1;
        /// <summary>"up" (default, also when null) or "down" (BC / Dune B.G.: counts toward the pivot).</summary>
        public string Direction { get; set; }
        /// <summary>Closed eras format old dates but are never offered at date entry.</summary>
        public bool Closed { get; set; }

        /// <summary>True when the era counts down toward its pivot.</summary>
        public bool IsDown => Direction == "down";
    }

    /// <summary>The months of a year: names and lengths, same count.</summary>
    public sealed class CalendarMonths
    {
        public List<string> Names { get; set; } = new List<string>();
        /// <summary>Same length as Names.</summary>
        public List<long> Lengths { get; set; } = new List<long>();
    }

    /// <summary>The week.</summary>
    public sealed class CalendarWeek
    {
        public long Length { get; set; }
        public List<string> Names { get; set; } = new List<string>();
        /// <summary>"free" (default, also when null): runs across month boundaries, anchor step 0 = Names[0].
        /// "nested": weekday = (day_of_month - 1) mod Length; a ragged final week is legal.</summary>
        public string Mode { get; set; }
    }

    /// <summary>A day that belongs to no month (a festival day).</summary>
    public sealed class CalendarIntercalary
    {
        public string Name { get; set; }
        /// <summary>0 = before month 1; k = after month k. Requires months.</summary>
        public long AfterMonth { get; set; }
        /// <summary>Present only in years where FloorMod(absYear + Offset, N) == 0 (the leap rule). Null when every year.</summary>
        public long? EveryYears { get; set; }
        /// <summary>Version 2: divisors that flip the rule in order, each a multiple of the one before
        /// (<c>every_years: 4, except: [100, 400]</c>: every 4, not every 100, yet every 400). Null when absent.</summary>
        public List<long> Except { get; set; }
        /// <summary>Version 2: the rule reads year absYear + Offset (default 0; 1 for a calendar with no year 0).
        /// Null when absent.</summary>
        public long? Offset { get; set; }
        /// <summary>Counts in the free-running week (default true). The Shire lock = opting exactly Mid-year's Day
        /// and Overlithe out (364 = 52 x 7).</summary>
        public bool InWeek { get; set; } = true;
    }

    /// <summary>Coarse-calendar year length. Legal ONLY when months are absent.</summary>
    public sealed class CalendarYearBundle
    {
        public long Steps { get; set; }
    }

    /// <summary>The structures above the step.</summary>
    public sealed class CalendarBundles
    {
        public CalendarYearBundle Year { get; set; }
        public CalendarMonths Months { get; set; }
        public CalendarWeek Week { get; set; }
    }

    /// <summary>A calendar config, as persisted in `atlas_calendar`.</summary>
    public sealed class CalendarConfig
    {
        public long Version { get; set; }
        /// <summary>Immutable sentinel: the world's time_basic_unit at creation. Machinery only.</summary>
        public string Grain { get; set; }
        /// <summary>The culture's display word for one step ("Sol"). Defaults to Grain.</summary>
        public string StepName { get; set; }
        public List<CalendarEra> Eras { get; set; } = new List<CalendarEra>();
        public CalendarBundles Bundles { get; set; }
        public List<CalendarIntercalary> Intercalary { get; set; } = new List<CalendarIntercalary>();
        /// <summary>Regular-day template. Tokens: {day} {month} {weekday} {year} {era} {step}.</summary>
        public string Format { get; set; }
        /// <summary>Intercalary-day template; may also use {intercalary}.</summary>
        public string FormatIntercalary { get; set; }
    }

    /// <summary>A step, taken apart. Fields are null when the structure doesn't exist.</summary>
    public sealed class StepBreakdown
    {
        public long Step { get; set; }
        /// <summary>Absolute year index (zero-based from step 0).</summary>
        public long AbsYear { get; set; }
        /// <summary>The era-local display year (restart arithmetic applied), or AbsYear when no era / restart false.</summary>
        public long Year { get; set; }
        public CalendarEra Era { get; set; }
        /// <summary>1-based month index, or null (intercalary day / no months).</summary>
        public int? Month { get; set; }
        public string MonthName { get; set; }
        /// <summary>1-based day within the month, or null.</summary>
        public long? Day { get; set; }
        /// <summary>Intercalary day name when the step IS one, else null.</summary>
        public string Intercalary { get; set; }
        public string Weekday { get; set; }
    }

    /// <summary>
    /// The outcome of <see cref="OWCalendar.ParseDate"/>: a step, or an error with a reason. (The TS union
    /// ParseResult | ParseError is one class here: <see cref="IsError"/> is the TS isParseError.)
    /// </summary>
    public sealed class ParseResult
    {
        public long Step { get; private set; }
        /// <summary>The reason, or null on success.</summary>
        public string Error { get; private set; }
        public bool IsError => Error != null;

        public static ParseResult Ok(long step) => new ParseResult { Step = step };
        public static ParseResult Fail(string error) => new ParseResult { Error = error };
        public override string ToString() => IsError ? "error: " + Error : "step " + Step.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>An era's resolved domain on the number line: Lo inclusive, Hi exclusive (infinite when open).</summary>
    public sealed class EraDomain
    {
        public CalendarEra Era { get; set; }
        public double Lo { get; set; }
        public double Hi { get; set; }
    }

    /// <summary>The first step of one month in a given year.</summary>
    public sealed class MonthStart
    {
        public int Month { get; set; }
        public string Name { get; set; }
        public long Step { get; set; }
    }

    /// <summary>
    /// The calendar kernel. Pure functions over a <see cref="CalendarConfig"/>; read a config with
    /// <see cref="ReadConfig"/> (or <see cref="OWCalendarLens.ReadCalendarValue"/> for the raw field value).
    /// </summary>
    public static class OWCalendar
    {
        // ---- Euclidean division (the contract's floor semantics) ----

        /// <summary>Floor division (toward negative infinity), never C#'s truncation. b == 0 throws, as C# integer
        /// division does (JS returns Infinity); the kernel never divides by zero.</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if (a % b != 0 && ((a < 0) != (b < 0))) q -= 1;
            return q;
        }

        /// <summary>Euclidean-style modulo, literally the TS formula ((a % b) + b) % b.</summary>
        public static long FloorMod(long a, long b)
        {
            return ((a % b) + b) % b;
        }

        // ---- Validation (the builder rejects loudly; wild configs fall back) ----

        /// <summary>
        /// Semantic validation: a list of messages, empty when the config is clean. Checks the TS makes on
        /// types (Number.isInteger, isFinite) are range checks here, since the C# fields are already integers.
        /// </summary>
        public static List<string> Validate(CalendarConfig cfg)
        {
            var errors = new List<string>();
            if (cfg == null) { errors.Add("Grain is required"); return errors; }
            if (cfg.Version != 1 && cfg.Version != 2) errors.Add("Unsupported calendar version: " + Str(cfg.Version));
            // The profile's grain: something left after the White_Space trim (the room profile's set).
            if (cfg.Grain == null || OWProfileText.TrimWhiteSpace(cfg.Grain).Length == 0) errors.Add("Grain is required");

            var months = cfg.Bundles?.Months;
            if (months != null)
            {
                if (months.Names == null || months.Lengths == null
                    || months.Names.Count == 0 || months.Names.Count != months.Lengths.Count)
                {
                    errors.Add("months.names and months.lengths must be same-length non-empty arrays");
                }
                else if (months.Lengths.Any(l => l < 1))
                {
                    errors.Add("Every month length must be a whole number, 1 or more");
                }
                if (cfg.Bundles?.Year != null) errors.Add("year.steps is legal only when no months are defined (year length is derived from months)");
            }
            else if (cfg.Bundles?.Year != null)
            {
                if (cfg.Bundles.Year.Steps < 1) errors.Add("year.steps must be a positive integer");
            }

            var week = cfg.Bundles?.Week;
            if (week != null)
            {
                if (week.Length < 1) errors.Add("The week length must be a whole number, 1 or more");
                else if (week.Names == null || week.Names.Count != week.Length) errors.Add("Give the week as many day names as its length");
            }

            var inter = cfg.Intercalary ?? new List<CalendarIntercalary>();
            if (inter.Count > 0 && months == null) errors.Add("Festival days need months");
            long monthCount = months?.Names?.Count ?? 0;
            foreach (var ic in inter)
            {
                if (ic.AfterMonth < 0 || ic.AfterMonth > monthCount)
                    errors.Add("Festival day \"" + ic.Name + "\": pick the month it follows");
                if (ic.EveryYears != null && ic.EveryYears.Value < 1)
                    errors.Add("Festival day \"" + ic.Name + "\": every N yrs must be a whole number, 1 or more");
                var rule = LeapRuleProblem(ic);
                if (rule != null) errors.Add("Festival day \"" + ic.Name + "\": " + rule);
                if (cfg.Version == 1 && (ic.Except != null || ic.Offset != null))
                    errors.Add("Festival day \"" + ic.Name + "\": except and offset need calendar version 2");
            }

            var eras = cfg.Eras ?? new List<CalendarEra>();
            foreach (var era in eras)
            {
                if (string.IsNullOrEmpty(era.Name)) errors.Add("Every era needs a name");
                // TS: "start must be a number" / "end must be a number": a long always is.
                // As TS: the up check for a direction absent or exactly "up", the down check for exactly "down"; any
                // other word is neither (EraForStep still reads it as up).
                if ((era.Direction ?? "up") == "up" && era.End != null && era.End.Value <= era.Start)
                    errors.Add("Era \"" + era.Name + "\": end must be after start");
                if (era.IsDown && era.End != null && era.End.Value >= era.Start)
                    errors.Add("Era \"" + era.Name + "\": a counting-down era must end below its start");
            }
            // Overlap check over resolved domains.
            var domains = ResolveEraDomains(eras);
            for (int i = 0; i < domains.Count; i++)
            {
                for (int j = i + 1; j < domains.Count; j++)
                {
                    var a = domains[i]; var b = domains[j];
                    if (a.Lo < b.Hi && b.Lo < a.Hi)
                        errors.Add("Eras \"" + a.Era.Name + "\" and \"" + b.Era.Name + "\" overlap");
                }
            }
            return errors;
        }

        /// <summary>
        /// Semantic validation of a raw <c>atlas_calendar</c> value, as the TypeScript SDK's
        /// <c>validateCalendarConfig</c> reads it: a list of messages, empty when the value is clean. Where
        /// <see cref="Validate(CalendarConfig)"/> sees only what <see cref="ReadConfig"/> could read, this reads the
        /// value itself, so it also names what makes the reader refuse it ("start must be a number", "except must be a
        /// list", "except and offset need calendar version 2" on a version-1 entry). A value is read as JavaScript
        /// reads it: a missing key is <c>undefined</c> in a message, a string's or a list's <c>length</c> is read as
        /// one, a string in place of a list is its characters, mixed values compare as JavaScript's <c>&lt;</c> does,
        /// and the eras are ordered for the overlap check as V8's sort orders them (exact for fewer than 64 eras).
        /// A JSON string is a string here, not a config: parse it first. Where the TypeScript SDK throws (a null value,
        /// <c>eras</c> or <c>intercalary</c> an object, a number or <c>true</c>, a null entry), this answers: a null value
        /// or entry reads as one with no keys, and <c>eras must be a list</c> or <c>intercalary must be a list</c> stands
        /// where the loop over it would start, with no check of that list.
        /// </summary>
        public static List<string> ValidateValue(JToken value)
        {
            var errors = new List<string>();
            var version = Js.Get(value, "version");
            if (!(Js.IsNumber(version) && (Js.Num(version) == 1 || Js.Num(version) == 2)))
                errors.Add("Unsupported calendar version: " + Js.Str(version));
            var grain = Js.Get(value, "grain");
            if (!(grain != null && grain.Type == JTokenType.String && OWProfileText.TrimWhiteSpace((string)grain).Length > 0))
                errors.Add("Grain is required");

            var bundles = Js.Get(value, "bundles");
            var months = Js.Get(bundles, "months");
            var year = Js.Get(bundles, "year");
            if (Js.Truthy(months))
            {
                var names = Js.Get(months, "names") as JArray;
                var lengths = Js.Get(months, "lengths") as JArray;
                if (names == null || lengths == null || names.Count == 0 || names.Count != lengths.Count)
                    errors.Add("months.names and months.lengths must be same-length non-empty arrays");
                else if (lengths.Any(l => !Js.IsInteger(l) || Js.Num(l) < 1))
                    errors.Add("Every month length must be a whole number, 1 or more");
                if (Js.Truthy(year)) errors.Add("year.steps is legal only when no months are defined (year length is derived from months)");
            }
            else if (Js.Truthy(year))
            {
                var steps = Js.Get(year, "steps");
                if (!Js.IsInteger(steps) || Js.Num(steps) < 1) errors.Add("year.steps must be a positive integer");
            }

            var week = Js.Get(bundles, "week");
            if (Js.Truthy(week))
            {
                var length = Js.Get(week, "length");
                var names = Js.Get(week, "names") as JArray;
                if (!Js.IsInteger(length) || Js.Num(length) < 1) errors.Add("The week length must be a whole number, 1 or more");
                else if (names == null || names.Count != Js.Num(length)) errors.Add("Give the week as many day names as its length");
            }

            var inter = Js.Get(value, "intercalary");
            if (Js.Nullish(inter)) inter = new JArray();
            if (Js.Less(new JValue(0L), Js.Get(inter, "length")) == true && !Js.Truthy(months)) errors.Add("Festival days need months");
            long monthCount = Js.Get(months, "names") is JArray monthNames ? monthNames.Count : 0;
            var entries = Js.Items(inter);
            if (entries == null)
            {
                errors.Add("intercalary must be a list");
                entries = new List<JToken>();
            }
            foreach (var ic in entries)
            {
                string name = Js.Str(Js.Get(ic, "name"));
                var after = Js.Get(ic, "after_month");
                if (!Js.IsInteger(after) || Js.Num(after) < 0 || Js.Num(after) > monthCount)
                    errors.Add("Festival day \"" + name + "\": pick the month it follows");
                var every = Js.Get(ic, "every_years");
                if (!Js.Nullish(every) && (!Js.IsInteger(every) || Js.Num(every) < 1))
                    errors.Add("Festival day \"" + name + "\": every N yrs must be a whole number, 1 or more");
                var rule = RawLeapRuleProblem(ic);
                if (rule != null) errors.Add("Festival day \"" + name + "\": " + rule);
                if (Js.IsNumber(version) && Js.Num(version) == 1
                    && (!Js.Nullish(Js.Get(ic, "except")) || !Js.Nullish(Js.Get(ic, "offset"))))
                    errors.Add("Festival day \"" + name + "\": except and offset need calendar version 2");
            }

            var eraValue = Js.Get(value, "eras");
            if (Js.Nullish(eraValue)) eraValue = new JArray();
            var eras = Js.Items(eraValue);
            if (eras == null)
            {
                errors.Add("eras must be a list");
                return errors;
            }
            foreach (var era in eras)
            {
                var nameValue = Js.Get(era, "name");
                string name = Js.Str(nameValue);
                var start = Js.Get(era, "start");
                var end = Js.Get(era, "end");
                var direction = Js.Get(era, "direction");
                if (!Js.Truthy(nameValue)) errors.Add("Every era needs a name");
                if (!Js.IsFinite(start)) errors.Add("Era \"" + name + "\": start must be a number");
                if (!Js.Nullish(end) && !Js.IsFinite(end)) errors.Add("Era \"" + name + "\": end must be a number");
                if (DirectionIs(direction, "up") && !Js.Nullish(end) && Js.Less(start, end) == false)
                    errors.Add("Era \"" + name + "\": end must be after start");
                if (DirectionIs(direction, "down") && !Js.Nullish(end) && Js.Less(end, start) == false)
                    errors.Add("Era \"" + name + "\": a counting-down era must end below its start");
            }
            var domains = RawEraDomains(eras);
            for (int i = 0; i < domains.Count; i++)
            {
                for (int j = i + 1; j < domains.Count; j++)
                {
                    var a = domains[i]; var b = domains[j];
                    if (Js.Less(a.Lo, b.Hi) == true && Js.Less(b.Lo, a.Hi) == true)
                        errors.Add("Eras \"" + Js.Str(Js.Get(a.Era, "name")) + "\" and \"" + Js.Str(Js.Get(b.Era, "name")) + "\" overlap");
                }
            }
            return errors;
        }

        /// <summary>The TypeScript <c>leapRuleProblem</c> on a raw entry: why its version-2 keys cannot be read, or null.</summary>
        static string RawLeapRuleProblem(JToken ic)
        {
            var ex = Js.Get(ic, "except");
            var off = Js.Get(ic, "offset");
            if (Js.Nullish(ex) && Js.Nullish(off)) return null;
            var every = Js.Get(ic, "every_years");
            if (Js.Nullish(every)) return "except and offset need every_years";
            if (!Js.Nullish(off) && !Js.IsSafeInt(off)) return "offset must be a whole number";
            if (!Js.Nullish(ex))
            {
                if (!(ex is JArray list)) return "except must be a list";
                var prev = every;
                foreach (var d in list)
                {
                    // d <= prev, and d % prev !== 0 (JavaScript's remainder: NaN for a zero divisor).
                    if (!Js.IsPositiveInt(d) || Js.Less(prev, d) == false || !(Js.ToNumber(d) % Js.ToNumber(prev) == 0))
                        return "each except must be a multiple of the number before it, and larger";
                    prev = d;
                }
            }
            return null;
        }

        /// <summary><c>(direction ?? 'up') === word</c>.</summary>
        static bool DirectionIs(JToken direction, string word) =>
            Js.Nullish(direction) ? word == "up" : direction.Type == JTokenType.String && (string)direction == word;

        sealed class RawDomain
        {
            public JToken Era;
            public JToken Lo;
            public JToken Hi;
        }

        /// <summary>
        /// The TypeScript <c>resolveEraDomains</c> over raw eras: sorted by V8's sort with <c>(a, b) =&gt; a.start - b.start</c>,
        /// an up era's open end the next up era's start, a down era's open end minus infinity; the values as given, so the
        /// overlap check compares them as JavaScript does.
        /// </summary>
        static List<RawDomain> RawEraDomains(List<JToken> eras)
        {
            var sorted = Js.V8Sort(eras, (a, b) => Js.ToNumber(Js.Get(a, "start")) - Js.ToNumber(Js.Get(b, "start")));
            var output = new List<RawDomain>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var era = sorted[i];
                var start = Js.Get(era, "start");
                var end = Js.Get(era, "end");
                if (DirectionIs(Js.Get(era, "direction"), "down"))
                {
                    output.Add(new RawDomain { Era = era, Lo = Js.Nullish(end) ? new JValue(double.NegativeInfinity) : end, Hi = start });
                    continue;
                }
                JToken hi = end;
                if (Js.Nullish(hi))
                {
                    var nextUp = sorted.Skip(i + 1).FirstOrDefault(e => DirectionIs(Js.Get(e, "direction"), "up"));
                    var nextStart = nextUp == null ? null : Js.Get(nextUp, "start");
                    hi = Js.Nullish(nextStart) ? new JValue(double.PositiveInfinity) : nextStart;
                }
                output.Add(new RawDomain { Era = era, Lo = start, Hi = hi });
            }
            return output;
        }

        /// <summary>
        /// Defensive reader for wild configs (hand-edited files, other tools). Returns null when the value is not
        /// a structurally usable calendar: callers fall back to bare numbers, never a broken axis. Structural
        /// sanity only; <see cref="Validate"/> covers semantics (a config with overlaps still formats).
        /// Every divisor and step size must be a positive integer, or the year arithmetic could loop forever.
        /// Stricter than the TS in one way: values the TS reader lets through but then misbehaves or throws on
        /// (a non-integral era start/end/year_base, a non-string month or weekday name) read as null here.
        /// </summary>
        public static CalendarConfig ReadConfig(JToken value)
        {
            try { return ReadConfigCore(value); }
            catch (Exception) { return null; } // never throw: an unusable value is no calendar
        }

        static CalendarConfig ReadConfigCore(JToken value)
        {
            if (!(value is JObject o)) return null;
            if (!TryNumber(o["version"], out double version) || (version != 1 && version != 2)) return null;
            // The profile's grain is a string with something left after trimming the room profile's White_Space set.
            if (!(o["grain"] is JValue g) || g.Type != JTokenType.String
                || OWProfileText.TrimWhiteSpace((string)g).Length == 0) return null;

            var cfg = new CalendarConfig
            {
                Version = (long)version,
                Grain = (string)g,
                StepName = OptString(o["step_name"]),
                Format = OptString(o["format"]),
                FormatIntercalary = OptString(o["format_intercalary"]),
            };

            // bundles: anything but an object carries no structure (JS: `cfg.bundles?.months` is undefined).
            if (o["bundles"] is JObject bo)
            {
                var bundles = new CalendarBundles();
                var mt = bo["months"];
                if (!IsFalsy(mt))
                {
                    if (!(mt is JObject mo) || !(mo["names"] is JArray names) || !(mo["lengths"] is JArray lengths)
                        || names.Count != lengths.Count || names.Count == 0) return null;
                    var m = new CalendarMonths();
                    foreach (var n in names)
                    {
                        if (n.Type != JTokenType.String) return null;
                        m.Names.Add((string)n);
                    }
                    foreach (var l in lengths)
                    {
                        if (!IsPositiveInt(l, out long len)) return null;
                        m.Lengths.Add(len);
                    }
                    bundles.Months = m;
                }
                var yt = bo["year"];
                if (!IsNullish(yt))
                {
                    if (!(yt is JObject yo) || !IsPositiveInt(yo["steps"], out long steps)) return null;
                    bundles.Year = new CalendarYearBundle { Steps = steps };
                }
                var wt = bo["week"];
                if (!IsNullish(wt))
                {
                    if (!(wt is JObject wo) || !IsPositiveInt(wo["length"], out long wlen) || !(wo["names"] is JArray wnames)) return null;
                    var w = new CalendarWeek { Length = wlen, Mode = OptString(wo["mode"]) };
                    foreach (var n in wnames)
                    {
                        if (n.Type != JTokenType.String) return null;
                        w.Names.Add((string)n);
                    }
                    bundles.Week = w;
                }
                cfg.Bundles = bundles;
            }

            // Absent or null: no eras. Any other non-list (false, 0, "") is not a calendar (the profile, dist.31); the
            // TypeScript kernel throws on those, where the page says refuse.
            var et = o["eras"];
            if (!IsNullish(et))
            {
                if (!(et is JArray eras)) return null;
                foreach (var e in eras)
                {
                    if (!(e is JObject eo)) return null;
                    if (!(eo["name"] is JValue en) || en.Type != JTokenType.String) return null;
                    // Whole numbers a double holds exactly (Number.isSafeInteger), as the TypeScript reader takes them.
                    if (!IsSafeInt(eo["start"], out long start)) return null;
                    var era = new CalendarEra
                    {
                        Name = (string)en,
                        Abbrev = OptString(eo["abbrev"]),
                        Start = start,
                        Restart = !(eo["restart"] is JValue r && r.Type == JTokenType.Boolean && !(bool)r),
                        Direction = OptString(eo["direction"]),
                        Closed = IsTruthy(eo["closed"]),
                    };
                    var end = eo["end"];
                    if (!IsNullish(end))
                    {
                        if (!IsSafeInt(end, out long endValue)) return null;
                        era.End = endValue;
                    }
                    var yb = eo["year_base"];
                    if (!IsNullish(yb))
                    {
                        if (!IsSafeInt(yb, out long baseValue)) return null;
                        era.YearBase = baseValue;
                    }
                    cfg.Eras.Add(era);
                }
            }

            var it = o["intercalary"];
            if (!IsNullish(it))
            {
                if (!(it is JArray inter)) return null;
                foreach (var i in inter)
                {
                    if (!(i is JObject io)) return null;
                    if (!(io["name"] is JValue iname) || iname.Type != JTokenType.String) return null;
                    if (!TryInteger(io["after_month"], out long after)) return null;
                    // A day placed after a month the year does not have (past the last, below the first) has no
                    // position, and a reader never invents one (the profile, serial 89); with no months there is no
                    // place between months at all, so any intercalary day refuses, 0 included.
                    long monthCount = cfg.Bundles?.Months?.Names.Count ?? 0;
                    if (monthCount == 0 || after < 0 || after > monthCount) return null;
                    var ic = new CalendarIntercalary
                    {
                        Name = (string)iname,
                        AfterMonth = after,
                        InWeek = !(io["in_week"] is JValue iw && iw.Type == JTokenType.Boolean && !(bool)iw),
                    };
                    var ev = io["every_years"];
                    if (!IsNullish(ev))
                    {
                        if (!IsPositiveInt(ev, out long every)) return null;
                        ic.EveryYears = every;
                    }
                    // Version 2's leap keys (version 1 ignores them, as it ignores any unknown key); null reads as
                    // absent, and a chain LeapRuleProblem refuses is no calendar.
                    var ex = io["except"];
                    var off = io["offset"];
                    if (cfg.Version == 2 && (!IsNullish(ex) || !IsNullish(off)))
                    {
                        if (ic.EveryYears == null) return null;
                        if (!IsNullish(off))
                        {
                            if (!IsSafeInt(off, out long offset)) return null;
                            ic.Offset = offset;
                        }
                        if (!IsNullish(ex))
                        {
                            if (!(ex is JArray exList)) return null;
                            ic.Except = new List<long>();
                            foreach (var d in exList)
                            {
                                if (!IsPositiveInt(d, out long divisor)) return null;
                                ic.Except.Add(divisor);
                            }
                            if (LeapRuleProblem(ic) != null) return null;
                        }
                    }
                    cfg.Intercalary.Add(ic);
                }
            }
            return cfg;
        }

        /// <summary>
        /// Why an entry's version-2 leap keys can't be read, or null. <c>except</c> must be a list of whole numbers,
        /// each above and a multiple of the one before (the first of <c>every_years</c>), so "the last divisor that
        /// matches" has one meaning and the closed-form count holds; neither key without <c>every_years</c>. The
        /// offset's whole-number check is the reader's (a long always is one).
        /// </summary>
        static string LeapRuleProblem(CalendarIntercalary ic)
        {
            if (ic.Except == null && ic.Offset == null) return null;
            if (ic.EveryYears == null) return "except and offset need every_years";
            if (ic.Except != null)
            {
                long prev = ic.EveryYears.Value;
                foreach (var d in ic.Except)
                {
                    if (d < 1 || prev < 1 || d <= prev || d % prev != 0)
                        return "each except must be a multiple of the number before it, and larger";
                    prev = d;
                }
            }
            return null;
        }

        // ---- Year arithmetic ----

        /// <summary>A leap entry's rule as read: period, the except chain and the offset (both version 2 only).</summary>
        struct LeapRule
        {
            public long N;
            public List<long> Except;
            public long Offset;
        }

        static readonly List<long> NoExcept = new List<long>();

        static LeapRule RuleOf(CalendarConfig cfg, CalendarIntercalary ic)
        {
            bool v2 = cfg.Version == 2;
            return new LeapRule
            {
                N = ic.EveryYears.Value,
                Except = v2 && ic.Except != null ? ic.Except : NoExcept,
                Offset = v2 && ic.Offset != null ? ic.Offset.Value : 0,
            };
        }

        static List<LeapRule> LeapRules(CalendarConfig cfg, IEnumerable<CalendarIntercalary> inter) =>
            inter.Where(i => i.EveryYears != null).Select(i => RuleOf(cfg, i)).ToList();

        struct YearShapeInfo
        {
            /// <summary>Base year length: months + non-leap intercalary (or year.steps, or 1).</summary>
            public long BaseLen;
            /// <summary>Leap entries' rules (one extra step each in the years a rule selects).</summary>
            public List<LeapRule> Rules;
        }

        static List<CalendarIntercalary> Inter(CalendarConfig cfg) => cfg.Intercalary ?? new List<CalendarIntercalary>();
        static List<CalendarEra> ErasOf(CalendarConfig cfg) => cfg.Eras ?? new List<CalendarEra>();

        static YearShapeInfo YearShape(CalendarConfig cfg)
        {
            var months = cfg.Bundles?.Months;
            var inter = Inter(cfg);
            var rules = LeapRules(cfg, inter);
            if (months != null)
            {
                long sum = 0;
                foreach (var l in months.Lengths) sum += l;
                long b = sum + inter.Count(i => i.EveryYears == null);
                return new YearShapeInfo { BaseLen = b, Rules = rules };
            }
            return new YearShapeInfo { BaseLen = cfg.Bundles?.Year?.Steps ?? 1, Rules = rules };
        }

        /// <summary>True when the rule selects absolute year y: FloorMod(y + offset, n) == 0, flipped by each except
        /// dividing it.</summary>
        static bool RuleHolds(LeapRule r, long y)
        {
            if (r.N == 0) return false; // a zero period only slips in by hand (the reader refuses it); TS: NaN, never === 0
            long Y = y + r.Offset;
            bool on = FloorMod(Y, r.N) == 0;
            foreach (var d in r.Except) if (d != 0 && FloorMod(Y, d) == 0) on = !on;
            return on;
        }

        /// <summary>How many k in [a, b) have FloorMod(k + offset, m) == 0.</summary>
        static long MultiplesIn(long a, long b, long m, long offset) =>
            m == 0 ? 0 : FloorDiv(b - 1 + offset, m) - FloorDiv(a - 1 + offset, m);

        /// <summary>
        /// Signed count of the years k the rule selects with 0 &lt;= k &lt; y, minus those with y &lt;= k &lt; 0.
        /// Closed form: each except is a multiple of the divisor before it (the reader refuses any other list), so
        /// its years are a subset of the last term's and the terms alternate.
        /// </summary>
        static long LeapYearsBefore(long y, LeapRule r)
        {
            long a = y >= 0 ? 0 : y, b = y >= 0 ? y : 0, sign = y >= 0 ? 1 : -1;
            long count = MultiplesIn(a, b, r.N, r.Offset);
            long s = -1;
            foreach (var d in r.Except)
            {
                count += s * MultiplesIn(a, b, d, r.Offset);
                s = -s;
            }
            return sign * count;
        }

        /// <summary>True when absolute year y contains a leap entry with period N (the version-1 rule).</summary>
        public static bool IsLeapYear(long y, long N)
        {
            if (N == 0) return false; // TS: floorMod(y, 0) is NaN, never === 0
            return FloorMod(y, N) == 0;
        }

        /// <summary>True when the intercalary day falls in absolute year y: always without <c>every_years</c>,
        /// else by its rule (version 2's except and offset included).</summary>
        public static bool IntercalaryInYear(CalendarConfig cfg, CalendarIntercalary ic, long y) =>
            ic.EveryYears == null || RuleHolds(RuleOf(cfg, ic), y);

        /// <summary>First step of absolute year y. YearStartStep(0) == 0 by construction.</summary>
        public static long YearStartStep(CalendarConfig cfg, long y)
        {
            var shape = YearShape(cfg);
            long start = y * shape.BaseLen;
            foreach (var r in shape.Rules) start += LeapYearsBefore(y, r);
            return start;
        }

        /// <summary>Length in steps of absolute year y.</summary>
        public static long YearLength(CalendarConfig cfg, long y)
        {
            var shape = YearShape(cfg);
            long len = shape.BaseLen;
            foreach (var r in shape.Rules) if (RuleHolds(r, y)) len += 1;
            return len;
        }

        /// <summary>A rule's mean leap days per year: 1/n - 1/e1 + 1/e2 - ...</summary>
        static double MeanLeap(LeapRule r)
        {
            if (r.N == 0) return double.PositiveInfinity; // as TS's 1 / 0
            double m = 1.0 / r.N;
            double s = -1;
            foreach (var d in r.Except)
            {
                m += s / d;
                s = -s;
            }
            return m;
        }

        /// <summary>Correction laps allowed per direction. A valid config needs about (leap periods + 2); the cap
        /// only stops a malformed one from hanging.</summary>
        const int MaxYearCorrection = 10_000;

        /// <summary>Absolute year index of a step. Zero-based; closed-form seed plus bounded correction.</summary>
        public static long AbsoluteYear(CalendarConfig cfg, long step)
        {
            var shape = YearShape(cfg);
            // Seed on the MEAN year length (leap days included) so the correction stays a few laps at any
            // distance from zero.
            double meanLen = shape.BaseLen;
            foreach (var r in shape.Rules) meanLen += MeanLeap(r);
            double seed = Math.Floor(step / meanLen);
            // Non-finite seed (a zero-length year): TS returns the non-finite value itself; a long cannot hold
            // it, so 0. Only a hand-built config that bypasses ReadConfig gets here.
            if (double.IsNaN(seed) || double.IsInfinity(seed) || seed > 9.2e18 || seed < -9.2e18) return 0;
            long y = (long)seed;
            // Bounded laps: a config that slips the reader must not hang.
            for (int i = 0; i < MaxYearCorrection && step < YearStartStep(cfg, y); i++) y -= 1;
            for (int i = 0; i < MaxYearCorrection && step >= YearStartStep(cfg, y + 1); i++) y += 1;
            return y;
        }

        // ---- Era arithmetic ----

        /// <summary>Each era's domain. Up-era: [start, end ?? next start ?? +inf). Down-era: [end ?? -inf, start):
        /// counts toward the pivot; the pivot belongs to the next (up) era. Sorted by start (stable).</summary>
        public static List<EraDomain> ResolveEraDomains(IList<CalendarEra> eras)
        {
            var sorted = (eras ?? new List<CalendarEra>()).OrderBy(e => e.Start).ToList(); // OrderBy is stable, as JS sort is
            var output = new List<EraDomain>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var era = sorted[i];
                if (era.IsDown)
                {
                    output.Add(new EraDomain { Era = era, Lo = era.End.HasValue ? era.End.Value : double.NegativeInfinity, Hi = era.Start });
                }
                else
                {
                    // An open end runs to the next UP era's start: a down era holds no step at or above its pivot,
                    // so it never ends an up era (before TS 4.76.0 an up era at 0 listed before a down era with
                    // pivot 0 ended where it began, decided by list order).
                    // "Up" as TS reads it here: direction absent or exactly "up" (any other word is neither).
                    var nextUp = sorted.Skip(i + 1).FirstOrDefault(e => (e.Direction ?? "up") == "up");
                    double hi = era.End.HasValue ? era.End.Value
                        : (nextUp != null ? nextUp.Start : double.PositiveInfinity);
                    output.Add(new EraDomain { Era = era, Lo = era.Start, Hi = hi });
                }
            }
            return output;
        }

        /// <summary>The era owning a step: domain containment; wild overlaps resolve to the latest start (up wins
        /// ties). Null outside every era.</summary>
        public static CalendarEra EraForStep(CalendarConfig cfg, long step)
        {
            var eras = ErasOf(cfg);
            if (eras.Count == 0) return null;
            // As TS 4.42.2 resolves it (the profile, calendar.md): the latest start, then an up era over a down one,
            // then the later era in the list. Domains come in list order within one start (a stable sort), so on a
            // tie the candidate replaces the holder when it is up, or when both are down. "Down" is exactly "down";
            // any other direction, missing or null counts up.
            CalendarEra best = null;
            foreach (var d in ResolveEraDomains(eras))
            {
                if (!(step >= d.Lo && step < d.Hi)) continue;
                if (best == null || d.Era.Start > best.Start || (d.Era.Start == best.Start && (!d.Era.IsDown || best.IsDown))) best = d.Era;
            }
            return best;
        }

        /// <summary>The display year for a step under an era (or the absolute year without one).</summary>
        public static long DisplayYear(CalendarConfig cfg, long step, CalendarEra era)
        {
            long abs = AbsoluteYear(cfg, step);
            if (era == null || !era.Restart) return abs;
            long b = era.YearBase;
            if (era.IsDown)
            {
                // Counted from the year holding the step just before the pivot, which is year `b` (step -1 with pivot
                // 0 and base 1 => year 1: no year zero on the down side). A mid-year pivot read its own year's earlier
                // steps as year b - 1 before TS 4.42.2; a pivot on a year's first step reads as it always did.
                return AbsoluteYear(cfg, era.Start - 1) - abs + b;
            }
            return abs - AbsoluteYear(cfg, era.Start) + b;
        }

        // ---- The year's day sequence (months + intercalary interleave) ----

        sealed class DaySlot
        {
            public bool IsIntercalary;
            public int Month;     // 1-based, month slots
            public long Day;      // 1-based within month
            public CalendarIntercalary Intercalary;
        }

        /// <summary>The slots of absolute year y, in step order. O(config).</summary>
        static List<DaySlot> YearSlots(CalendarConfig cfg, long y)
        {
            var slots = new List<DaySlot>();
            var months = cfg.Bundles?.Months;
            if (months == null) return slots;
            var inter = Inter(cfg);
            void Push(long afterMonth)
            {
                foreach (var ic in inter)
                    if (ic.AfterMonth == afterMonth && IntercalaryInYear(cfg, ic, y))
                        slots.Add(new DaySlot { IsIntercalary = true, Intercalary = ic });
            }
            Push(0);
            for (int m = 1; m <= months.Names.Count; m++)
            {
                long len = m - 1 < months.Lengths.Count ? months.Lengths[m - 1] : 0;
                for (long d = 1; d <= len; d++) slots.Add(new DaySlot { Month = m, Day = d });
                Push(m);
            }
            return slots;
        }

        // ---- Weekday ----

        /// <summary>Weekday name for a step, or null (no week / nested outside months / skipped intercalary day).</summary>
        public static string WeekdayForStep(CalendarConfig cfg, long step) => WeekdayForStep(cfg, step, null);

        static string WeekdayForStep(CalendarConfig cfg, long step, StepPosition pre)
        {
            var week = cfg.Bundles?.Week;
            if (week == null || week.Length < 1 || week.Names == null || week.Names.Count != week.Length) return null;
            var pos = pre ?? PositionOf(cfg, step);
            if (week.Mode == "nested")
            {
                if (pos.Slot == null || pos.Slot.IsIntercalary) return null;
                return week.Names[(int)FloorMod(pos.Slot.Day - 1, week.Length)];
            }
            // Free mode: counted steps exclude in_week:false intercalary days. Anchor: counted 0 -> Names[0].
            if (pos.Slot != null && pos.Slot.IsIntercalary && !pos.Slot.Intercalary.InWeek) return null;
            var skipped = Inter(cfg).Where(i => !i.InWeek).ToList();
            long y = pos.AbsYear;
            long skippedBeforeYear = y * skipped.Count(i => i.EveryYears == null);
            foreach (var r in LeapRules(cfg, skipped)) skippedBeforeYear += LeapYearsBefore(y, r);
            long skippedInYear = 0;
            if (pos.Slots != null)
            {
                // TS reads slots[i] unguarded; past the end it would throw. Since serial 89 the reader refuses the one
                // value that got there (an after_month past the last month); a config built by hand still can.
                for (long i = 0; i < pos.Offset && i < pos.Slots.Count; i++)
                {
                    var s = pos.Slots[(int)i];
                    if (s.IsIntercalary && !s.Intercalary.InWeek) skippedInYear += 1;
                }
            }
            long counted = step - skippedBeforeYear - skippedInYear;
            return week.Names[(int)FloorMod(counted, week.Length)];
        }

        // ---- Breakdown + format ----

        sealed class StepPosition
        {
            public long AbsYear;
            public long Offset;           // steps into the year
            public DaySlot Slot;          // null when no months
            public List<DaySlot> Slots;   // null when no months
        }

        static StepPosition PositionOf(CalendarConfig cfg, long step)
        {
            long y = AbsoluteYear(cfg, step);
            long offset = step - YearStartStep(cfg, y);
            if (cfg.Bundles?.Months == null) return new StepPosition { AbsYear = y, Offset = offset };
            var slots = YearSlots(cfg, y);
            var slot = offset >= 0 && offset < slots.Count ? slots[(int)offset] : null;
            return new StepPosition { AbsYear = y, Offset = offset, Slot = slot, Slots = slots };
        }

        /// <summary>A step, taken apart: absolute and display year, era, month, day, intercalary day, weekday.</summary>
        public static StepBreakdown BreakdownStep(CalendarConfig cfg, long step)
        {
            var pos = PositionOf(cfg, step);
            var era = EraForStep(cfg, step);
            var months = cfg.Bundles?.Months;
            var slot = pos.Slot;
            bool isMonth = slot != null && !slot.IsIntercalary;
            return new StepBreakdown
            {
                Step = step,
                AbsYear = pos.AbsYear,
                Year = DisplayYear(cfg, step, era),
                Era = era,
                Month = isMonth ? slot.Month : (int?)null,
                MonthName = isMonth && months != null ? months.Names[slot.Month - 1] : null,
                Day = isMonth ? slot.Day : (long?)null,
                Intercalary = slot != null && slot.IsIntercalary ? slot.Intercalary.Name : null,
                Weekday = WeekdayForStep(cfg, step, pos),
            };
        }

        static readonly string[] KnownTokens = { "day", "month", "weekday", "year", "era", "step", "intercalary" };

        const string DefaultFormatMonths = "{day} {month}, {year} {era}";
        const string DefaultFormatPlain = "{year} {era}";
        const string DefaultFormatIntercalary = "{intercalary}, {year} {era}";

        /// <summary>Placeholder for a KNOWN token that resolved to nothing, so the collapse pass can absorb it
        /// along with any separators hugging it. U+E000 (private use), deliberately never NUL.</summary>
        const string EmptyKnown = "";

        // JavaScript's \s, exactly (.NET's \s differs: it has U+0085 and lacks U+FEFF).
        const string WsChars = @"\t\n\v\f\r    -     　﻿";
        const string Ws = "[" + WsChars + "]";
        static readonly Regex TokenRx = new Regex(@"\{([a-z_]+)\}");
        static readonly Regex CollapseRx = new Regex(Ws + @"*[,;·]?" + Ws + @"*" + Ws + @"*[,;·]?" + Ws + "*");
        static readonly Regex MultiWsRx = new Regex(Ws + "{2,}");
        static readonly Regex AnyWsRx = new Regex(Ws + "+");
        static readonly Regex TrimRx = new Regex(@"\A" + Ws + "+|" + Ws + @"+\z");
        static readonly Regex EdgeSepRx = new Regex(@"\A[" + WsChars + @",;·]+|[" + WsChars + @",;·]+\z");
        static readonly Regex IntRx = new Regex(@"\A-?[0-9]+\z"); // JS \d is ASCII; .NET \d is not

        /// <summary>JavaScript's String.prototype.trim.</summary>
        internal static string JsTrim(string s) => TrimRx.Replace(s, "");

        /// <summary>Substitute and collapse. KNOWN tokens with no value render empty and adjacent separators
        /// collapse; UNKNOWN tokens render literally (visible, never swallowed).</summary>
        static string RenderTemplate(string template, Dictionary<string, string> values)
        {
            string output = TokenRx.Replace(template, match =>
            {
                string token = match.Groups[1].Value;
                if (Array.IndexOf(KnownTokens, token) < 0) return match.Value; // unknown: literal
                values.TryGetValue(token, out string v);
                return string.IsNullOrEmpty(v) ? EmptyKnown : v;
            });
            // Collapse around empty-known markers: absorb adjacent separators/spaces.
            output = CollapseRx.Replace(output, " ");
            output = MultiWsRx.Replace(output, " ");
            output = JsTrim(output);
            output = EdgeSepRx.Replace(output, "");
            return output;
        }

        /// <summary>Format a step. Total: every integer formats to a string under every readable config.</summary>
        public static string FormatStep(CalendarConfig cfg, long step)
        {
            var b = BreakdownStep(cfg, step);
            var values = new Dictionary<string, string>
            {
                ["day"] = b.Day != null ? Str(b.Day.Value) : null,
                ["month"] = b.MonthName,
                ["weekday"] = b.Weekday,
                ["year"] = Str(b.Year),
                ["era"] = b.Era != null ? (b.Era.Abbrev ?? b.Era.Name) : null,
                ["step"] = Str(step),
                ["intercalary"] = b.Intercalary,
            };
            string template = b.Intercalary != null
                ? (cfg.FormatIntercalary ?? DefaultFormatIntercalary)
                : (cfg.Format ?? (cfg.Bundles?.Months != null ? DefaultFormatMonths : DefaultFormatPlain));
            return RenderTemplate(template, values);
        }

        /// <summary>
        /// Compact year-level label: "{year} {era-abbrev}", the axis-tick wording when zoomed wide. Outside every
        /// era: the absolute year index alone.
        /// </summary>
        public static string FormatYearLabel(CalendarConfig cfg, long step)
        {
            var era = EraForStep(cfg, step);
            long year = DisplayYear(cfg, step, era);
            string eraLabel = era != null ? (era.Abbrev ?? era.Name) : "";
            return !string.IsNullOrEmpty(eraLabel) ? Str(year) + " " + eraLabel : Str(year);
        }

        /// <summary>
        /// The base year length in steps (leap days excluded), or null when the calendar has no year structure
        /// above the step. Consumers use it as a zoom threshold (ticks tighter than a year: full dates).
        /// </summary>
        public static long? ApproxYearLength(CalendarConfig cfg)
        {
            if (cfg.Bundles?.Months != null) return FixedYearLength(cfg);
            return cfg.Bundles?.Year?.Steps;
        }

        static long FixedYearLength(CalendarConfig cfg)
        {
            long sum = 0;
            foreach (var l in cfg.Bundles.Months.Lengths) sum += l;
            return sum + Inter(cfg).Count(i => i.EveryYears == null);
        }

        /// <summary>
        /// A duration in steps, spoken: "19 years" when the calendar has year structure and the span reaches it,
        /// else "12 days" in the step word. Uses the BASE year length (leap days are noise at duration
        /// granularity); one decimal below 10 years, whole above.
        /// </summary>
        public static string FormatDuration(CalendarConfig cfg, long steps)
        {
            long n = Math.Abs(steps);
            string unit = StepWord(cfg);
            long? yearLen = DurationYearLength(cfg, unit);
            if (yearLen != null && yearLen.Value > 1 && n >= yearLen.Value) return YearsSpoken((double)n / yearLen.Value);
            // TS rounds to two decimals; a long step count is already whole.
            return Str(n) + " " + (n == 1 ? unit : unit + "s");
        }

        /// <summary>
        /// A duration of a span that need not be whole (the TypeScript <c>formatDuration</c> takes any number): as the
        /// <see cref="FormatDuration(CalendarConfig, long)"/> overload, with the step count rounded to two decimals as
        /// JavaScript's <c>Math.round</c> rounds ("2.5 days", "0.13 days" for 0.125) and every number written as
        /// JavaScript writes it. The sign is dropped.
        /// </summary>
        public static string FormatDuration(CalendarConfig cfg, double steps)
        {
            double n = Math.Abs(steps);
            string unit = StepWord(cfg);
            long? yearLen = DurationYearLength(cfg, unit);
            if (yearLen != null && yearLen.Value > 1 && n >= yearLen.Value) return YearsSpoken(n / yearLen.Value);
            double shown = JsRound(n * 100) / 100;
            return JsNumber(shown) + " " + (shown == 1 ? unit : unit + "s");
        }

        /// <summary>The step word: step_name, else the grain, trimmed as JavaScript trims, else "step".</summary>
        static string StepWord(CalendarConfig cfg)
        {
            string unit = JsTrim(cfg.StepName ?? cfg.Grain ?? "step");
            return unit.Length == 0 ? "step" : unit;
        }

        /// <summary>The year a duration counts in: the base year, else year.steps, else 1 when the step word is "year".</summary>
        static long? DurationYearLength(CalendarConfig cfg, string unit) =>
            cfg.Bundles?.Months != null
                ? FixedYearLength(cfg)
                : cfg.Bundles?.Year?.Steps ?? (unit.ToLowerInvariant() == "year" ? 1 : (long?)null);

        static string YearsSpoken(double years)
        {
            double rounded = years >= 10 ? JsRound(years) : JsRound(years * 10) / 10;
            return JsNumber(rounded) + " " + (rounded == 1 ? "year" : "years");
        }

        /// <summary>JavaScript's Math.round: the nearest whole number, a half toward plus infinity.</summary>
        static double JsRound(double x)
        {
            double r = Math.Floor(x);
            return x - r >= 0.5 ? r + 1 : r;
        }

        /// <summary>
        /// The first step of each month in absolute year y, in order. O(config): walks month lengths and
        /// intercalary placement, never the year's days. Empty when the calendar has no months.
        /// </summary>
        public static List<MonthStart> MonthStartSteps(CalendarConfig cfg, long y)
        {
            var output = new List<MonthStart>();
            var months = cfg.Bundles?.Months;
            if (months == null) return output;
            var inter = Inter(cfg);
            bool Active(CalendarIntercalary ic) => IntercalaryInYear(cfg, ic, y);
            long offset = inter.Count(i => i.AfterMonth == 0 && Active(i));
            long start = YearStartStep(cfg, y);
            for (int m = 1; m <= months.Names.Count; m++)
            {
                output.Add(new MonthStart { Month = m, Name = months.Names[m - 1], Step = start + offset });
                offset += months.Lengths[m - 1] + inter.Count(i => i.AfterMonth == m && Active(i));
            }
            return output;
        }

        /// <summary>
        /// Mid-rung label for dense axis ticks: "11 Greening 454": day, month, display year, NO era word (the
        /// era bands carry the era). Intercalary steps read "Hearthmoot 454". Monthless calendars fall back to
        /// the year label.
        /// </summary>
        public static string FormatMidLabel(CalendarConfig cfg, long step)
        {
            var b = BreakdownStep(cfg, step);
            if (b.Intercalary != null) return b.Intercalary + " " + Str(b.Year);
            if (b.MonthName != null && b.Day != null) return Str(b.Day.Value) + " " + b.MonthName + " " + Str(b.Year);
            return FormatYearLabel(cfg, step);
        }

        // ---- Parse ----

        /// <summary>Absolute year for an era-local display year (inverse of DisplayYear).</summary>
        public static long AbsYearForEraYear(CalendarConfig cfg, CalendarEra era, long Y)
        {
            if (!era.Restart) return Y;
            long b = era.YearBase;
            // The inverse of DisplayYear's down count (from the year holding the step before the pivot, TS 4.42.2). The
            // old inverse, AbsoluteYear(start) - (Y - b + 1), read a mid-year pivot's down dates one year early.
            if (era.IsDown) return AbsoluteYear(cfg, era.Start - 1) - (Y - b);
            return AbsoluteYear(cfg, era.Start) + (Y - b);
        }

        /// <summary>Preferred era when the input names none: the era holding the now, else the latest-started
        /// open era, else the latest-started.</summary>
        static CalendarEra DefaultEra(CalendarConfig cfg, long? now)
        {
            var eras = ErasOf(cfg);
            if (eras.Count == 0) return null;
            if (now != null)
            {
                var holder = EraForStep(cfg, now.Value);
                if (holder != null) return holder;
            }
            var open = eras.Where(e => !e.Closed).ToList();
            var pool = open.Count > 0 ? open : eras;
            return pool.OrderByDescending(e => e.Start).First(); // stable, as JS sort is
        }

        static string Normalize(string s)
        {
            string t = JsLower(s).Replace(',', ' ').Replace('.', ' ');
            return " " + JsTrim(AnyWsRx.Replace(t, " ")) + " ";
        }

        /// <summary>
        /// JavaScript's toLowerCase where it differs from ToLowerInvariant on a name a calendar can hold: U+0130 (capital
        /// I with dot above) lowers to "i" and U+0307, two code units. Final sigma is not reproduced (it needs Unicode's
        /// Cased and Case_Ignorable properties): ToLowerInvariant lowers every capital sigma to the medial form.
        /// </summary>
        static string JsLower(string s) => s.Replace("\u0130", "i\u0307").ToLowerInvariant();

        /// <summary>
        /// Parse a written date to a step.
        /// Accepted shapes (calendar words case-insensitive):
        ///   "-454" / "454"                      -> the raw stored step, always
        ///   "454 AE" / "Year 12 FT"             -> year (+ era)
        ///   "Frostwane 454" / "12 Frostwane 454" (+ era)
        ///   "Shieldmeet 1372" (+ era)           -> intercalary day
        ///   "Frostwane 12" (no year)            -> resolved against the now's year
        /// Partial dates snap to the period's first step. A missing year with no now is an error (never guess).
        /// P(F(n)) == n is NOT promised; F(P(F(n))) == F(n) is.
        /// </summary>
        public static ParseResult ParseDate(CalendarConfig cfg, string input, long? now = null)
        {
            string raw = JsTrim(input ?? "");
            if (raw.Length == 0) return ParseResult.Fail("empty date");

            // Bare number = the raw stored step, always (contract rule 9).
            if (IntRx.IsMatch(raw))
            {
                // TS parseInt keeps any magnitude as a double; past a long there is no step to give.
                return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long bare)
                    ? ParseResult.Ok(bare) : ParseResult.Fail("unrecognized: \"" + raw + "\"");
            }

            var months = cfg.Bundles?.Months;
            var inter = Inter(cfg);

            CalendarEra era = null;
            int? monthIndex = null;
            CalendarIntercalary intercalary = null;
            // Eat NAMES from the normalized string BEFORE extracting numbers: some calendar names legitimately
            // contain digits ("2 Yule"). Input and candidate keys normalize identically (case-insensitive,
            // punctuation-tolerant: "S.R." matches "s r").
            string rest = Normalize(raw).Replace(" year ", " ");

            bool Eat(string name)
            {
                string needle = Normalize(name);
                int at = rest.IndexOf(needle, StringComparison.Ordinal);
                if (at < 0) return false;
                rest = rest.Substring(0, at) + " " + rest.Substring(at + needle.Length); // first occurrence only
                return true;
            }

            // Eras first (longest names first so "After Empire" beats "Empire").
            var eraCandidates = ErasOf(cfg)
                .SelectMany(e => string.IsNullOrEmpty(e.Abbrev)
                    ? new[] { (key: e.Name, e) }
                    : new[] { (key: e.Name, e), (key: e.Abbrev, e) })
                .OrderByDescending(c => c.key.Length) // stable, as JS sort is
                .ToList();
            foreach (var c in eraCandidates) if (Eat(c.key)) { era = c.e; break; }

            foreach (var c in inter.OrderByDescending(i => i.Name.Length)) if (Eat(c.Name)) { intercalary = c; break; }

            if (intercalary == null && months != null)
            {
                for (int m = 1; m <= months.Names.Count; m++)
                {
                    if (Eat(months.Names[m - 1])) { monthIndex = m; break; }
                }
            }

            // What remains must be numbers only.
            var numbers = new List<long>();
            foreach (var p in AnyWsRx.Split(JsTrim(rest)).Where(x => x.Length > 0))
            {
                if (IntRx.IsMatch(p) && long.TryParse(p, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long num)) numbers.Add(num);
                else return ParseResult.Fail("unrecognized: \"" + p + "\"");
            }

            // Distribute numbers: with a month, [day?, year?]; with an intercalary, [year?]; else [year].
            long? day = null;
            long? year = null;
            if (monthIndex != null)
            {
                long len = months != null ? months.Lengths[monthIndex.Value - 1] : 0;
                if (numbers.Count == 2)
                {
                    // Day-first and day-second both occur ("12 Frostwane 454" / "Frostwane 12, 454"): the day is
                    // the one that fits the month; ties resolve day-first.
                    long a = numbers[0], b = numbers[1];
                    if (a >= 1 && a <= len) { day = a; year = b; }
                    else if (b >= 1 && b <= len) { day = b; year = a; }
                    else return ParseResult.Fail("no day in " + months?.Names[monthIndex.Value - 1] + " matches " + Str(a) + "/" + Str(b));
                }
                else if (numbers.Count == 1)
                {
                    // One number beside a month: a plausible day resolves against the now's year; otherwise it is
                    // the year (snap to the month's first day).
                    long n = numbers[0];
                    if (n >= 1 && n <= len) { day = n; year = null; } else { year = n; }
                }
                else if (numbers.Count != 0)
                {
                    return ParseResult.Fail("too many numbers");
                }
            }
            else if (intercalary != null)
            {
                if (numbers.Count > 1) return ParseResult.Fail("too many numbers");
                year = numbers.Count > 0 ? numbers[0] : (long?)null;
            }
            else
            {
                if (numbers.Count != 1) return ParseResult.Fail("expected a year");
                year = numbers[0];
            }

            // Resolve to a step for a given absolute year.
            ParseResult ResolveInYear(long abs)
            {
                long start = YearStartStep(cfg, abs);
                if (monthIndex == null && intercalary == null) return ParseResult.Ok(start);
                var slots = YearSlots(cfg, abs);
                if (intercalary != null)
                {
                    int ii = slots.FindIndex(s => s.IsIntercalary && ReferenceEquals(s.Intercalary, intercalary));
                    if (ii == -1) return ParseResult.Fail(intercalary.Name + " does not occur in that year");
                    return ParseResult.Ok(start + ii);
                }
                long wanted = day ?? 1;
                int idx = slots.FindIndex(s => !s.IsIntercalary && s.Month == monthIndex.Value && s.Day == wanted);
                if (idx == -1) return ParseResult.Fail("that day does not exist in that month");
                return ParseResult.Ok(start + idx);
            }

            if (year == null)
            {
                if (now == null) return ParseResult.Fail("add a year (the world clock has no now set)");
                return ResolveInYear(AbsoluteYear(cfg, now.Value));
            }

            var target = era ?? DefaultEra(cfg, now);
            if (target == null) return ResolveInYear(year.Value);

            // Era arithmetic first; if the resulting step falls OUTSIDE the era's own domain (e.g. "S.R. -10":
            // format shows absolute years outside era domains, often under a literal era word in the template),
            // fall back to the absolute-year reading so Format(Parse(Format(n))) stays stable.
            var viaEra = ResolveInYear(AbsYearForEraYear(cfg, target, year.Value));
            if (viaEra.IsError) return viaEra;
            if (ReferenceEquals(EraForStep(cfg, viaEra.Step), target)) return viaEra;
            var viaAbs = ResolveInYear(year.Value);
            // A typed era names the date's era: an absolute reading that lands in an era is a date the input never
            // named ("10 Thaw, 1 BG" read as "10 Thaw, 2 AG" when year 1 BG holds only 1 to 5 Frost). Refused; the
            // fallback stays for a step in no era, where format prints the absolute year (npm 4.42.4, Temper).
            if (era != null && !viaAbs.IsError && EraForStep(cfg, viaAbs.Step) != null)
                return ParseResult.Fail("not a date in " + era.Name);
            return viaAbs;
        }

        /// <summary>True when a parse failed (the TS type guard isParseError).</summary>
        public static bool IsParseError(ParseResult r) => r == null || r.IsError;

        // ---- helpers ----

        static string Str(long v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>A double the way JavaScript's String() writes it: the shortest digits that read back (Mono's "R" is
        /// not that), whole numbers below 1e21 in full.</summary>
        static string JsNumber(double d) => Js.Number(d);

        static bool IsNullish(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;

        /// <summary>JavaScript falsiness: null/undefined, false, 0, NaN, "".</summary>
        static bool IsFalsy(JToken t)
        {
            if (IsNullish(t)) return true;
            switch (t.Type)
            {
                case JTokenType.Boolean: return !(bool)t;
                case JTokenType.String: return ((string)t).Length == 0;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return TryNumber(t, out double d) && (d == 0 || double.IsNaN(d));
                default: return false;
            }
        }

        static bool IsTruthy(JToken t) => !IsFalsy(t);

        static string OptString(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        static bool TryNumber(JToken t, out double d)
        {
            d = 0;
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return false;
            var v = ((JValue)t).Value;
            d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Number.isInteger, within a long's range.</summary>
        static bool TryInteger(JToken t, out long v)
        {
            v = 0;
            if (!TryNumber(t, out double d) || double.IsNaN(d) || double.IsInfinity(d) || d != Math.Floor(d)) return false;
            if (d > 9.2e18 || d < -9.2e18) return false;
            v = (long)d;
            return true;
        }

        /// <summary>Number.isSafeInteger(n).</summary>
        static bool IsSafeInt(JToken t, out long v)
        {
            if (!TryInteger(t, out v)) return false;
            return v >= -9007199254740991L && v <= 9007199254740991L;
        }

        /// <summary>Number.isSafeInteger(n) and n >= 1.</summary>
        static bool IsPositiveInt(JToken t, out long v)
        {
            if (!TryInteger(t, out v)) return false;
            return v >= 1 && v <= 9007199254740991L;
        }
    }

    /// <summary>
    /// JavaScript's reading of a JSON value, for <see cref="OWCalendar.ValidateValue"/>: a C# null is
    /// <c>undefined</c> (a key that is not there), a JSON null is <c>null</c>.
    /// </summary>
    internal static class OWCalendarJs
    {
        const double MaxSafe = 9007199254740991;

        public static bool Undefined(JToken v) => v == null || v.Type == JTokenType.Undefined;

        /// <summary><c>v == null</c>: null or undefined.</summary>
        public static bool Nullish(JToken v) => Undefined(v) || v.Type == JTokenType.Null;

        /// <summary>
        /// <c>o.key</c> on a JSON value: an object's own key, a string's (UTF-16 code units) or a list's <c>length</c>,
        /// anything else undefined (where JavaScript reads a key of null it throws: undefined here).
        /// </summary>
        public static JToken Get(JToken o, string key)
        {
            if (o is JObject obj) return obj.TryGetValue(key, out var t) ? t : null;
            if (key == "length")
            {
                if (o is JArray a) return new JValue((long)a.Count);
                if (o != null && o.Type == JTokenType.String) return new JValue((long)((string)o).Length);
            }
            return null;
        }

        /// <summary>What a for-of reads: a list's items, a string's characters (code points), or null where JavaScript
        /// throws (the value is not iterable).</summary>
        public static List<JToken> Items(JToken v)
        {
            if (v is JArray a) return a.ToList();
            if (v == null || v.Type != JTokenType.String) return null;
            var s = (string)v;
            var output = new List<JToken>();
            for (int i = 0; i < s.Length; i++)
            {
                int n = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                output.Add(new JValue(s.Substring(i, n)));
                i += n - 1;
            }
            return output;
        }

        public static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);

        /// <summary>A number token's double (a JavaScript number is one).</summary>
        public static double Num(JToken v)
        {
            switch (((JValue)v).Value)
            {
                case double d: return d;
                case long l: return l;
                case int i: return i;
                case System.Numerics.BigInteger b: return (double)b;
                case decimal m: return (double)m;
                case float f: return f;
                default: return Convert.ToDouble(((JValue)v).Value, CultureInfo.InvariantCulture);
            }
        }

        public static bool IsFinite(JToken v) => IsNumber(v) && !double.IsNaN(Num(v)) && !double.IsInfinity(Num(v));

        /// <summary>Number.isInteger.</summary>
        public static bool IsInteger(JToken v) => IsFinite(v) && Math.Floor(Num(v)) == Num(v);

        /// <summary>Number.isSafeInteger.</summary>
        public static bool IsSafeInt(JToken v) => IsInteger(v) && Math.Abs(Num(v)) <= MaxSafe;

        public static bool IsPositiveInt(JToken v) => IsSafeInt(v) && Num(v) >= 1;

        /// <summary>JavaScript's truthiness: undefined, null, false, 0, NaN and "" are false; a list or object is true.</summary>
        public static bool Truthy(JToken v)
        {
            if (Nullish(v)) return false;
            switch (v.Type)
            {
                case JTokenType.Boolean: return (bool)v;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return Num(v) != 0 && !double.IsNaN(Num(v));
                case JTokenType.String: return ((string)v).Length > 0;
                default: return true;
            }
        }

        /// <summary>JavaScript's String(v): a number as Number::toString writes it, a list its items joined by commas
        /// (null and undefined as nothing), an object <c>[object Object]</c>.</summary>
        public static string Str(JToken v)
        {
            if (Undefined(v)) return "undefined";
            switch (v.Type)
            {
                case JTokenType.Null: return "null";
                case JTokenType.Boolean: return (bool)v ? "true" : "false";
                case JTokenType.Integer:
                case JTokenType.Float:
                    return Number(Num(v));
                case JTokenType.String: return (string)v;
                case JTokenType.Array: return string.Join(",", ((JArray)v).Select(x => Nullish(x) ? "" : Str(x)));
                case JTokenType.Object: return "[object Object]";
                default: return v.ToString();
            }
        }

        /// <summary>Number::toString.</summary>
        public static string Number(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            return OWJsNumber.Format(d);
        }

        /// <summary>JavaScript's ToNumber on a JSON value (a list or object through its String()).</summary>
        public static double ToNumber(JToken v)
        {
            if (Undefined(v)) return double.NaN;
            switch (v.Type)
            {
                case JTokenType.Null: return 0;
                case JTokenType.Boolean: return (bool)v ? 1 : 0;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return Num(v);
                case JTokenType.String: return StringToNumber((string)v);
                default: return StringToNumber(Str(v));
            }
        }

        static readonly Regex DecimalRx = new Regex(@"\A[+-]?(Infinity|([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?)\z");
        static readonly Regex RadixRx = new Regex(@"\A0(?:[xX]([0-9a-fA-F]+)|[oO]([0-7]+)|[bB]([01]+))\z");

        /// <summary>StringToNumber: trimmed as trim() trims, empty is 0, a decimal (with Infinity), or 0x, 0o, 0b.</summary>
        static double StringToNumber(string s)
        {
            string t = OWCalendar.JsTrim(s);
            if (t.Length == 0) return 0;
            if (DecimalRx.IsMatch(t))
            {
                if (t.EndsWith("Infinity", StringComparison.Ordinal)) return t[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
                return double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            var m = RadixRx.Match(t);
            if (!m.Success) return double.NaN;
            int radix = m.Groups[1].Success ? 16 : m.Groups[2].Success ? 8 : 2;
            string digits = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value;
            double n = 0;
            foreach (char c in digits) n = n * radix + Convert.ToInt32(c.ToString(), 16);
            return n;
        }

        /// <summary>
        /// JavaScript's <c>a &lt; b</c> (IsLessThan): true, false, or null where it is undefined (a NaN). Two strings compare by
        /// UTF-16 code units. So <c>a &lt;= b</c> is <c>Less(b, a) == false</c>.
        /// </summary>
        public static bool? Less(JToken a, JToken b)
        {
            bool aText = IsText(a), bText = IsText(b);
            if (aText && bText) return string.CompareOrdinal(Str(a), Str(b)) < 0;
            double x = ToNumber(a), y = ToNumber(b);
            if (double.IsNaN(x) || double.IsNaN(y)) return null;
            return x < y;
        }

        /// <summary>A value whose ToPrimitive is a string: a string, a list or an object.</summary>
        static bool IsText(JToken v) => !Undefined(v) && (v.Type == JTokenType.String || v.Type == JTokenType.Array || v.Type == JTokenType.Object);

        /// <summary>
        /// V8's Array.prototype.sort for fewer than 64 items (its TimSort is then one run: the leading run found, a strictly
        /// descending one reversed, then binary insertion), with a comparator that may answer NaN (read as "not less").
        /// Past 63 items the same insertion, which a consistent comparator sorts as V8 does.
        /// </summary>
        public static List<JToken> V8Sort(List<JToken> items, Func<JToken, JToken, double> compare)
        {
            var a = new List<JToken>(items);
            int n = a.Count;
            if (n < 2) return a;
            int run = 2;
            bool descending = compare(a[1], a[0]) < 0;
            for (int i = 2; i < n; i++)
            {
                double order = compare(a[i], a[i - 1]);
                if ((descending && order >= 0) || (!descending && order < 0)) break;
                run++;
            }
            if (descending) a.Reverse(0, run);
            for (int start = run; start < n; start++)
            {
                var pivot = a[start];
                int left = 0, right = start;
                while (left < right)
                {
                    int mid = left + ((right - left) >> 1);
                    if (compare(pivot, a[mid]) < 0) right = mid;
                    else left = mid + 1;
                }
                for (int p = start; p > left; p--) a[p] = a[p - 1];
                a[left] = pivot;
            }
            return a;
        }
    }
}
