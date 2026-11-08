using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// <see cref="OWCalendar"/> and <see cref="OWCalendarLens"/> against the TypeScript SDK's own answers:
    /// <c>Fixtures/calendar/calendar_parity.json</c>, vendored byte for byte from the Godot SDK (commit <c>7a6c92c</c>,
    /// <c>tests/data/calendar_parity.json</c>), which made it with <c>codegen/generate_calendar_parity.mjs</c> from the built
    /// code of <c>@onlyworlds/sdk</c> 4.133.1 at commit <c>3cd810b</c>. It holds, for every calendar the profile fixture
    /// reads as one (named by case, so its value is read from <c>calendar.fixture.json</c> beside it) and for sixteen
    /// calendars written for the traps: year and mid labels, month starts, the year length, durations (a fraction carries
    /// its IEEE 754 bits), era years and about 15,000 parses; then 147 validations and 784 resolutions.
    /// </summary>
    /// <remarks>
    /// Every record is replayed. Where Unity answers otherwise on purpose, a <see cref="Known"/> entry names the
    /// difference and why, selects its records by their input, and records how many it selects; the replay asserts each
    /// of them still differs. The day Unity is changed to agree, that entry goes red and comes out. A record no Unity
    /// call can take (an options object, a number for an id) is a <see cref="Known"/> entry too, marked
    /// <see cref="Known.NoCall"/>, counted and never silently skipped. What the vectors cannot carry (npm has no answer
    /// to record) is pinned by hand at the end.
    /// </remarks>
    public class OWCalendarParityTests
    {
        /// <summary>The vectors' sha256 at godot-sdk 7a6c92c.</summary>
        private const string VectorsSha256 = "f756303bb7f4f53d51e750a8e43ecf56ddb179903dfb19e618ae8c0b93fa58ea";

        // ---- the documents ----

        private static JObject _vectors;
        private static Dictionary<string, JToken> _cases;

        private static string PathOf(string file)
        {
            var root = TestPackage.Root();
            Assert.IsNotNull(root, "the package's own path is how the vectors are found");
            var path = Path.Combine(root, "Tests", "Editor", "Fixtures", "calendar", file);
            Assert.IsTrue(File.Exists(path), path);
            return path;
        }

        private static JToken ReadJson(string text)
        {
            using (var reader = OWJson.Reader(text))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JToken.ReadFrom(reader);
            }
        }

        private static JObject Vectors => _vectors ?? (_vectors = (JObject)ReadJson(File.ReadAllText(PathOf("calendar_parity.json"))));

        /// <summary>The profile fixture's values, by case name.</summary>
        private static Dictionary<string, JToken> Cases
        {
            get
            {
                if (_cases != null) return _cases;
                var fixture = (JObject)ReadJson(File.ReadAllText(PathOf("calendar.fixture.json")));
                return _cases = fixture["cases"].Cast<JObject>().ToDictionary(c => (string)c["name"], c => c["value"]);
            }
        }

        private static string NameOf(JObject record) => (string)(record["case"] ?? record["name"]);

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        // ---- the replay ----

        /// <summary>One answer npm gave, and the Unity call that should give it.</summary>
        private sealed class Record
        {
            public string Calendar;
            public string Call;
            public JToken Want;
            public Func<JToken> Unity;
            /// <summary>The record's own input, for a <see cref="Known"/> to select on.</summary>
            public JToken Input;
        }

        /// <summary>A difference kept on purpose, or a record no Unity call takes.</summary>
        private sealed class Known
        {
            public string Name;
            public string Why;
            public int Count;
            public bool NoCall;
            public Func<Record, bool> Selects;
        }

        private static JToken Answer(Func<JToken> unity)
        {
            try { return unity(); }
            catch (Exception e) { return new JObject { ["unity threw"] = e.GetType().Name + ": " + e.Message }; }
        }

        private static bool Threw(JToken t) => t is JObject o && o["unity threw"] != null;

        private static string Show(JToken t) => t == null ? "undefined" : t.ToString(Formatting.None);

        private static void Replay(string what, IEnumerable<Record> records, int total, params Known[] known)
        {
            var problems = new List<string>();
            var hits = known.ToDictionary(k => k.Name, k => 0);
            int n = 0;
            foreach (var r in records)
            {
                n++;
                var k = known.FirstOrDefault(x => x.Selects(r));
                if (k != null) hits[k.Name]++;
                if (k != null && k.NoCall) continue;
                var got = Answer(r.Unity);
                var where = r.Calendar + ": " + r.Call;
                if (Threw(got)) problems.Add(where + " threw (" + got["unity threw"] + "); npm " + Show(r.Want));
                else if (k == null && !JToken.DeepEquals(got, r.Want))
                    problems.Add(where + ": npm " + Show(r.Want) + ", Unity " + Show(got));
                else if (k != null && JToken.DeepEquals(got, r.Want))
                    problems.Add(where + ": the known difference '" + k.Name + "' no longer differs (both " + Show(got) + "); drop it");
            }
            foreach (var k in known)
            {
                if (hits[k.Name] != k.Count)
                    problems.Add("the known difference '" + k.Name + "' selects " + hits[k.Name] + " records, recorded " + k.Count
                        + " (" + k.Why + ")");
            }
            Assert.AreEqual(total, n, what + ": the records in the vectors");
            if (problems.Count > 0)
                Assert.Fail(what + ": " + problems.Count + " problems\n" + string.Join("\n", problems.Take(60)));
        }

        /// <summary>Every calendar record of one kind, with the calendar read through Unity's own reader.</summary>
        private static IEnumerable<(string Name, CalendarConfig Cfg, JArray Row)> Rows(string key)
        {
            foreach (JObject c in Vectors["calendars"])
            {
                var name = NameOf(c);
                var cfg = OWCalendarLens.ReadCalendarValue(c["value"] ?? Cases[name]);
                Assert.IsNotNull(cfg, name + ": npm reads a calendar here, and so must Unity");
                foreach (JArray row in c[key]) yield return (name, cfg, row);
            }
        }

        private static bool On(Record r, params string[] calendars) => calendars.Contains(r.Calendar);

        // ---- the file ----

        [Test]
        public void TheVectors_AreTheGodotSdksBytes_MadeFromTheVendoredFixture()
        {
            Assert.AreEqual(VectorsSha256, Sha256(PathOf("calendar_parity.json")),
                "the vendored vectors are not godot-sdk 7a6c92c's: re-vendor them byte for byte (a checkout's line endings change the bytes)");
            Assert.AreEqual("@onlyworlds/sdk", (string)Vectors["npm"]["package"]);
            Assert.AreEqual("4.133.1", (string)Vectors["npm"]["version"]);
            Assert.AreEqual("3cd810b", (string)Vectors["npm"]["commit"]);
            Assert.AreEqual(Sha256(PathOf("calendar.fixture.json")), (string)Vectors["fixture_sha256"],
                "the vectors were made from another profile fixture than the one vendored here");
            Assert.AreEqual(34, Vectors["calendars"].Count(c => c["case"] != null), "every calendar of the fixture");
            Assert.AreEqual(50, Vectors["calendars"].Count());
        }

        // ---- labels, month starts, year length, era years ----

        /// <summary>
        /// True when the era holding the step has an abbrev that is not a string in the raw value: Unity's typed reader
        /// reads it as absent (<see cref="CalendarEra.Abbrev"/> is a string).
        /// </summary>
        private static bool AbbrevNotAStringAt(string calendar, CalendarConfig cfg, long step)
        {
            var era = OWCalendar.EraForStep(cfg, step);
            if (era == null) return false;
            var raw = (Vectors["calendars"].Cast<JObject>().First(c => NameOf(c) == calendar)["value"] ?? Cases[calendar])["eras"];
            var abbrev = raw?[cfg.Eras.IndexOf(era)]?["abbrev"];
            return abbrev != null && abbrev.Type != JTokenType.String && abbrev.Type != JTokenType.Null;
        }

        private static Known AbbrevNotAString() => new Known
        {
            Name = "abbrev-not-a-string",
            Why = "Unity's typed reader reads a non-string abbrev as absent and prints the era's name; npm prints String() of it "
                + "(nothing for a falsy one, nothing after a space for an empty list). npm is not consistent with itself there "
                + "(formatStep prints \"0\" where formatYearLabel prints nothing, and parseDate throws), so Unity keeps one "
                + "reading across its functions",
            Count = 60,
            Selects = r => (bool)r.Input[1],
        };

        private static IEnumerable<Record> LabelRecords(int column, string call, Func<CalendarConfig, long, string> unity) =>
            Rows("labels").Select(x => new Record
            {
                Calendar = x.Name, Call = call + "(" + x.Row[0] + ")", Want = x.Row[column],
                Input = new JArray(x.Row[0], AbbrevNotAStringAt(x.Name, x.Cfg, (long)x.Row[0])),
                Unity = () => unity(x.Cfg, (long)x.Row[0]),
            });

        [Test]
        public void FormatYearLabel_AnswersAsNpm()
        {
            Replay("formatYearLabel", LabelRecords(1, "FormatYearLabel", OWCalendar.FormatYearLabel), 3695, AbbrevNotAString());
        }

        [Test]
        public void FormatMidLabel_AnswersAsNpm()
        {
            // The three calendars with such an abbrev have no months: their mid label is the year label.
            Replay("formatMidLabel", LabelRecords(2, "FormatMidLabel", OWCalendar.FormatMidLabel), 3695, AbbrevNotAString());
        }

        [Test]
        public void MonthStartSteps_AnswerAsNpm()
        {
            Replay("monthStartSteps", Rows("month_starts").Select(x => new Record
            {
                Calendar = x.Name, Call = "MonthStartSteps(" + x.Row[0] + ")", Input = x.Row[0], Want = x.Row[1],
                Unity = () => new JArray(OWCalendar.MonthStartSteps(x.Cfg, (long)x.Row[0]).Select(m => new JArray(m.Month, m.Name, m.Step))),
            }), 1000);
        }

        [Test]
        public void ApproxYearLength_AnswersAsNpm()
        {
            var records = Vectors["calendars"].Cast<JObject>().Select(c =>
            {
                var cfg = OWCalendarLens.ReadCalendarValue(c["value"] ?? Cases[NameOf(c)]);
                return new Record
                {
                    Calendar = NameOf(c), Call = "ApproxYearLength()", Want = c["approx_year_length"],
                    Unity = () => OWCalendar.ApproxYearLength(cfg) is long l ? new JValue(l) : JValue.CreateNull(),
                };
            });
            Replay("approxYearLength", records, 50);
        }

        [Test]
        public void AbsYearForEraYear_AnswersAsNpm()
        {
            Replay("absYearForEraYear", Rows("era_years").Select(x => new Record
            {
                Calendar = x.Name, Call = "AbsYearForEraYear(era " + x.Row[0] + ", " + x.Row[1] + ")", Input = x.Row, Want = x.Row[2],
                Unity = () => OWCalendar.AbsYearForEraYear(x.Cfg, x.Cfg.Eras[(int)x.Row[0]], (long)x.Row[1]),
            }), 250);
        }

        // ---- durations ----

        /// <summary>The span a duration row was read at: its exact bits for a fraction.</summary>
        private static double SpanOf(JArray row) =>
            row.Count > 2 ? BitConverter.Int64BitsToDouble(Convert.ToInt64((string)row[2], 16)) : (double)row[0];

        [Test]
        public void FormatDuration_AnswersAsNpm()
        {
            Replay("formatDuration", Rows("durations").Select(x => new Record
            {
                Calendar = x.Name, Call = "FormatDuration(" + OWJsNumber.Format(SpanOf(x.Row)) + ")", Input = x.Row, Want = x.Row[1],
                Unity = () => OWCalendar.FormatDuration(x.Cfg, SpanOf(x.Row)),
            }), 2250);
        }

        [Test]
        public void FormatDuration_OfAWholeSpan_AnswersAsNpmThroughTheLongOverloadToo()
        {
            var whole = Rows("durations").Where(x => x.Row.Count == 2).ToList();
            Replay("formatDuration(long)", whole.Select(x => new Record
            {
                Calendar = x.Name, Call = "FormatDuration((long)" + x.Row[0] + ")", Input = x.Row, Want = x.Row[1],
                Unity = () => OWCalendar.FormatDuration(x.Cfg, (long)x.Row[0]),
            }), whole.Count);
            Assert.AreEqual(1480, whole.Count, "the whole spans (the other 770 are fractions)");
        }

        // ---- parse ----

        [Test]
        public void ParseDate_AnswersAsNpm()
        {
            Replay("parseDate", Rows("parse").Select(x => new Record
            {
                Calendar = x.Name,
                Call = "ParseDate(" + JsonConvert.ToString((string)x.Row[0]) + ", now " + Show(x.Row[1]) + ")",
                Input = x.Row, Want = x.Row[2],
                Unity = () =>
                {
                    var r = OWCalendar.ParseDate(x.Cfg, (string)x.Row[0], x.Row[1].Type == JTokenType.Null ? (long?)null : (long)x.Row[1]);
                    return r.IsError ? new JObject { ["error"] = r.Error } : new JObject { ["step"] = r.Step };
                },
            }), 15171, new Known
            {
                Name = "npm-throws-on-a-non-string-abbrev",
                Why = "npm's parseDate lowercases every era abbrev and throws a TypeError on one that is not a string; Unity's "
                    + "typed reader reads it as absent, so the date parses without it",
                Count = 159,
                Selects = r => r.Want["throws"] != null,
            });
        }

        // ---- validate ----

        /// <summary>
        /// Unity's answer where npm's <c>validateCalendarConfig</c> throws (a TypeError: a list it loops over is not
        /// iterable, or a key read on null). The Godot SDK answers the same.
        /// </summary>
        private static readonly Dictionary<string, string[]> ValidateWhereNpmThrows = new Dictionary<string, string[]>
        {
            ["refuse-eras-not-list"] = new[] { "eras must be a list" },
            ["refuse-eras-falsy"] = new[] { "eras must be a list" },
            ["refuse-eras-false"] = new[] { "eras must be a list" },
            ["refuse-intercalary-not-list"] = new[] { "intercalary must be a list" },
            ["null"] = new[] { "Unsupported calendar version: undefined", "Grain is required" },
            ["eras-an-object"] = new[] { "eras must be a list" },
            ["eras-a-number"] = new[] { "eras must be a list" },
            ["eras-true"] = new[] { "eras must be a list" },
            ["eras-entry-null"] = new[] { "Every era needs a name", "Era \"undefined\": start must be a number" },
            ["festival-an-object"] = new[] { "intercalary must be a list" },
            ["festival-entry-null"] = new[] { "Festival day \"undefined\": pick the month it follows" },
            ["festival-a-number"] = new[] { "intercalary must be a list" },
        };

        /// <summary>A validate row's value as npm read it: a fixture case's string value JSON-parsed first, as the
        /// generator did (a text JSON.parse refuses has no row).</summary>
        private static JToken ValueOf(JObject row)
        {
            if (row["name"] != null) return row["value"];
            var value = Cases[(string)row["case"]];
            return value.Type == JTokenType.String ? ReadJson((string)value) : value;
        }

        private static IEnumerable<Record> ValidateRecords(Func<JToken, Func<JToken>> unity) =>
            Vectors["validate"].Cast<JObject>().Select(row =>
            {
                var name = NameOf(row);
                var want = row["answer"];
                if (want is JObject thrown)
                {
                    Assert.IsTrue(ValidateWhereNpmThrows.ContainsKey(name), name + ": npm throws (" + thrown["throws"] + "); Unity's answer is not listed");
                    want = new JArray(ValidateWhereNpmThrows[name]);
                }
                var value = ValueOf(row);
                return new Record { Calendar = name, Call = "validate", Input = value, Want = want, Unity = unity(value) };
            });

        [Test]
        public void ValidateValue_AnswersAsNpm()
        {
            Assert.AreEqual(ValidateWhereNpmThrows.Count, Vectors["validate"].Count(r => r["answer"] is JObject), "every listed answer is a row where npm throws");
            Replay("ValidateValue", ValidateRecords(value => () => new JArray(OWCalendar.ValidateValue(value))), 147);
        }

        [Test]
        public void Validate_OfWhatTheReaderTakes_AnswersAsNpm()
        {
            // The typed Validate sees only what ReadConfig reads, so its rows are the values the reader takes.
            var taken = ValidateRecords(value => () => new JArray(OWCalendar.Validate(OWCalendar.ReadConfig(value))))
                .Where(r => OWCalendar.ReadConfig(r.Input) != null).ToList();
            Replay("Validate(CalendarConfig)", taken, 50, new Known
            {
                Name = "version-1-leap-keys",
                Why = "version 1 reads except and offset as unknown keys (the profile), so the typed reader drops them and the "
                    + "typed Validate cannot name them; ValidateValue reads the value itself and does",
                Count = 2,
                Selects = r => (long?)OWCalendarJs.Get(r.Input, "version") is long v && v == 1
                    && OWCalendarJs.Items(OWCalendarJs.Get(r.Input, "intercalary"))?.Any(ic =>
                        !OWCalendarJs.Nullish(OWCalendarJs.Get(ic, "except")) || !OWCalendarJs.Nullish(OWCalendarJs.Get(ic, "offset"))) == true,
            });
        }

        // ---- resolve ----

        /// <summary>JavaScript's truthiness of an option's value.</summary>
        private static bool Truthy(JToken t) => OWCalendarJs.Truthy(t);

        [Test]
        public void ResolveLens_AnswersAsNpm()
        {
            var sets = (JObject)Vectors["element_sets"];
            var records = Vectors["resolve"].Cast<JObject>().Select(row =>
            {
                var elements = ((JArray)sets[(string)row["set"]]).Cast<JObject>().ToList();
                var active = row["active"];
                var options = row["options"] as JObject;
                bool fallback = Truthy(options?["fallbackToOnly"]);
                return new Record
                {
                    Calendar = (string)row["set"],
                    Call = "resolve(active " + Show(active) + ", options " + Show(options) + ")",
                    Input = row, Want = row["answer"],
                    Unity = () =>
                    {
                        var c = active == null ? OWCalendarLens.ResolveLensNeverChosen(elements, fallback) : OWCalendarLens.ResolveLens(elements, (string)active);
                        return c == null ? JValue.CreateNull() : new JObject { ["id"] = c.Id, ["name"] = c.Name };
                    },
                };
            });
            Replay("resolveCalendar", records, 784,
                new Known
                {
                    Name = "no-options-object", NoCall = true, Count = 196,
                    Why = "Unity takes fallbackToOnly as a bool parameter, so a misspelt option is a compile error, never a runtime "
                        + "refusal; npm's TypeError on an unknown key has no Unity call to replay",
                    Selects = r => r.Input["options"] is JObject o && o.Properties().Any(p => p.Name != "fallbackToOnly"),
                },
                new Known
                {
                    Name = "a-number-for-an-id", NoCall = true, Count = 84,
                    Why = "ResolveLens takes the chosen id as a string; npm's number id (5, 0) has no Unity call to replay",
                    Selects = r => r.Input["active"] is JValue v && v.Type == JTokenType.Integer,
                },
                new Known
                {
                    Name = "an-id-is-text", Count = 2,
                    Why = "WorldCalendar.Id is a string, so an element whose id is the number 5 reads as \"5\" where npm hands the "
                        + "number back",
                    Selects = r => r.Want is JObject o && o["id"]?.Type == JTokenType.Integer,
                });
        }

        // ---- what the vectors cannot carry ----

        [Test]
        public void KnownDifference_YearBase_UnityReadsAnyWholeNumber()
        {
            // year_base is 0 or 1 (the profile, schema-dist v0.34.0-dist.109, with its fixture case refuse-year-base-five).
            // npm and the Godot SDK refuse anything else, so npm has no answer to record here; this reader still takes any
            // whole number, narrowed at the dist.109 repin. When it refuses, this goes red: drop it.
            var cfg = OWCalendarLens.ReadCalendarValue(JObject.Parse("{\"version\":1,\"grain\":\"year\",\"eras\":[{\"name\":\"Age\",\"start\":0,\"year_base\":5}]}"));
            Assert.IsNotNull(cfg, "year_base 5 reads as a calendar in Unity (npm: bare numbers)");
            Assert.AreEqual("5 Age", OWCalendar.FormatYearLabel(cfg, 0));
        }

        [Test]
        public void KnownDifference_ParseDate_ABareNumberPastJavaScriptsIsExact()
        {
            // npm's parseInt reads into a double: "9007199254740993" is step 9007199254740992 there. A step is a long here.
            var cfg = OWCalendarLens.ReadCalendarValue(JObject.Parse("{\"version\":1,\"grain\":\"day\"}"));
            Assert.AreEqual(9007199254740993L, OWCalendar.ParseDate(cfg, "9007199254740993").Step);
            Assert.AreEqual("unrecognized: \"9223372036854775808\"", OWCalendar.ParseDate(cfg, "9223372036854775808").Error,
                "past a long there is no step (npm: 9223372036854775808 as a double)");
        }

        [Test]
        public void KnownDifference_ParseDate_FinalSigma()
        {
            // JavaScript lowers a final capital sigma to final sigma (it reads the next character); ToLowerInvariant lowers
            // every capital sigma to σ. npm answers "σασ 5" unrecognized and "5 σας" step 4 (Node 22, sdk 3cd810b).
            var cfg = OWCalendarLens.ReadCalendarValue(JObject.Parse("{\"version\":1,\"grain\":\"year\",\"eras\":[{\"name\":\"ΣΑΣ\",\"start\":0}]}"));
            Assert.AreEqual(4, OWCalendar.ParseDate(cfg, "σασ 5").Step);
            Assert.AreEqual("unrecognized: \"σας\"", OWCalendar.ParseDate(cfg, "5 σας").Error);
            Assert.AreEqual(4, OWCalendar.ParseDate(cfg, "ΣΑΣ 5").Step, "the name as written reads in both");
        }

        [Test]
        public void KnownDifference_FormatDuration_AStepNameNotAString_ReadsTheGrain()
        {
            // npm throws a TypeError (it trims the value); Unity's reader reads a non-string step_name as absent, as Godot's does.
            var cfg = OWCalendarLens.ReadCalendarValue(JObject.Parse("{\"version\":1,\"grain\":\"day\",\"step_name\":5}"));
            Assert.AreEqual("3 days", OWCalendar.FormatDuration(cfg, 3L));
            Assert.AreEqual("2.5 days", OWCalendar.FormatDuration(cfg, 2.5));
        }

        [Test]
        public void KnownDifference_Validate_OfNoConfig()
        {
            // npm throws on null; the typed Validate answers as it always has, ValidateValue as the Godot SDK does.
            CollectionAssert.AreEqual(new[] { "Grain is required" }, OWCalendar.Validate((CalendarConfig)null));
            CollectionAssert.AreEqual(new[] { "Unsupported calendar version: undefined", "Grain is required" },
                OWCalendar.ValidateValue(JValue.CreateNull()));
        }

        // ---- the fixes, by hand ----

        [Test]
        public void FormatDuration_ManyYears_PrintsTheNumberAsJavaScriptDoes()
        {
            // 4.5e15 years printed as "4.5E+15" before; JavaScript writes whole numbers below 1e21 in full.
            var cfg = OWCalendarLens.ReadCalendarValue(JObject.Parse("{\"version\":1,\"grain\":\"season\",\"bundles\":{\"year\":{\"steps\":2}}}"));
            Assert.AreEqual("4500000000000000 years", OWCalendar.FormatDuration(cfg, 9000000000000000L));
            Assert.AreEqual("1e+21 years", OWCalendar.FormatDuration(cfg, 2e21));
        }

        [Test]
        public void Validate_GrainOfWhiteSpaceAlone_IsNoGrain()
        {
            // The profile's grain: a string with something left after the White_Space trim. A hand-built config could pass "   ".
            var cfg = new CalendarConfig { Version = 1, Grain = "   " };
            CollectionAssert.AreEqual(new[] { "Grain is required" }, OWCalendar.Validate(cfg));
            CollectionAssert.AreEqual(OWCalendar.ValidateValue(JObject.Parse("{\"version\":1,\"grain\":\"   \"}")), OWCalendar.Validate(cfg));
        }

        [Test]
        public void Validate_ADirectionNeitherUpNorDown_IsCheckedAsNeither()
        {
            // npm checks an up era's end only for direction absent, null or exactly "up"; "Up" is neither up nor down there.
            var cfg = new CalendarConfig { Version = 1, Grain = "year" };
            cfg.Eras.Add(new CalendarEra { Name = "S", Start = 10, End = 5, Direction = "Up" });
            CollectionAssert.IsEmpty(OWCalendar.Validate(cfg));
        }
    }
}
