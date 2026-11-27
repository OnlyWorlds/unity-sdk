using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Which calendars a world holds: ported one for one from @onlyworlds/sdk 4.23.0 test/calendar-lens.test.mjs
    /// (9 tests: worldCalendars, readCalendarValue, resolveCalendarLens), on plain JObject elements.
    /// </summary>
    public class OWCalendarLensTests
    {
        static JObject CONFIG => JObject.Parse(@"{
            version: 1, grain: 'year',
            eras: [{ name: 'Third Age', abbrev: 'TA', start: 0 }],
            format: '{year} {era}'
        }");

        static JObject El(string id, string type, string name, JToken calendar = null, string supertype = null)
        {
            var o = new JObject { ["id"] = id };
            if (type != null) o["type"] = type;
            if (name != null) o["name"] = name;
            if (supertype != null) o["supertype"] = supertype;
            if (calendar != null) o[OWCalendarLens.CalendarField] = calendar;
            return o;
        }

        static List<JObject> Elements => new List<JObject> { El("c1", "construct", "Imperial", CONFIG) };

        // ---- worldCalendars ----

        [Test]
        public void WorldCalendars_DetectsByAtlasCalendarPresence_NeverBySupertype()
        {
            var elements = new List<JObject>
            {
                El("c1", "construct", "Imperial Reckoning", CONFIG, supertype: "calendar"),
                // a lore-only "Calendar" Construct: no config, so no calendar
                El("c2", "construct", "The Old Calendar", supertype: "Calendar"),
                // a config on another type still counts: presence is the whole rule
                El("c3", "phenomenon", "Tide Count", CONFIG),
            };
            CollectionAssert.AreEqual(new[] { "c1", "c3" }, OWCalendarLens.WorldCalendars(elements).Select(c => c.Id).ToArray());
            Assert.AreEqual("atlas_calendar", OWCalendarLens.CalendarField);
        }

        [Test]
        public void WorldCalendars_AcceptsAJsonStringConfig_AndSkipsMalformedOnes()
        {
            var elements = new List<JObject>
            {
                El("s1", "construct", "Stringy", CONFIG.ToString(Formatting.None)),
                El("s2", "construct", "Broken", "{not json"),
                El("s3", "construct", "Wrong shape", new JObject { ["version"] = 9 }),
            };
            CollectionAssert.AreEqual(new[] { "s1" }, OWCalendarLens.WorldCalendars(elements).Select(c => c.Id).ToArray());
        }

        [Test]
        public void WorldCalendars_NamesAnUnnamedCalendar_AndTakesAnyIterable()
        {
            var found = OWCalendarLens.WorldCalendars(new HashSet<JObject> { El("u1", null, "", CONFIG) });
            Assert.AreEqual("(unnamed calendar)", found[0].Name);
        }

        // ---- readCalendarValue ----

        [Test]
        public void ReadCalendarValue_ReadsObjectsAndJsonStringsIdentically_NullForJunk()
        {
            Assert.AreEqual("year", OWCalendarLens.ReadCalendarValue(CONFIG)?.Grain);
            Assert.AreEqual("year", OWCalendarLens.ReadCalendarValue(CONFIG.ToString(Formatting.None))?.Grain);
            Assert.AreEqual("Third Age", OWCalendarLens.ReadCalendarValue(CONFIG.ToString(Formatting.None))?.Eras?[0].Name);
            Assert.IsNull(OWCalendarLens.ReadCalendarValue("{not json"));
            Assert.IsNull(OWCalendarLens.ReadCalendarValue(null));
            Assert.IsNull(OWCalendarLens.ReadCalendarValue(42));
        }

        // ---- resolveCalendarLens (deprecated since 0.58.0: these hold the old names to their answers) ----
        // TS tells "never chosen" (undefined) from "explicitly none" (null). C# has one null, so "never chosen"
        // is its own call, ResolveLensNeverChosen; ResolveLens takes a choice, null meaning "no calendar".
#pragma warning disable CS0618

        [Test]
        public void ResolveLens_ResolvesALiveId_AndDegradesDanglingOrNullToBareNumbers()
        {
            Assert.AreEqual("Imperial", OWCalendarLens.ResolveLens(Elements, "c1")?.Name);
            Assert.IsNull(OWCalendarLens.ResolveLens(Elements, "gone"));
            Assert.IsNull(OWCalendarLens.ResolveLens(Elements, null));
            Assert.IsNull(OWCalendarLens.ResolveLens(new List<JObject>(), "c1"));
        }

        [Test]
        public void ResolveLens_FallsBackToTheOnlyCalendarWhenNoneWasEverChosen()
        {
            Assert.AreEqual("Imperial", OWCalendarLens.ResolveLensNeverChosen(Elements, fallbackToOnly: true)?.Name);
        }

        [Test]
        public void ResolveLens_RespectsAnExplicitNoCalendarChoiceEvenWhenOneExists()
        {
            // TS: resolveCalendarLens(elements, null, { fallbackToOnly: true }). An explicit null choice goes
            // through ResolveLens, which has no fallback at all: the intent, ported.
            Assert.IsNull(OWCalendarLens.ResolveLens(Elements, null));
        }

        [Test]
        public void ResolveLens_DoesNotGuessWhenTheWorldHasMoreThanOneCalendar()
        {
            var two = Elements.Concat(new[] { El("c2", "construct", "Guild", CONFIG) }).ToList();
            Assert.IsNull(OWCalendarLens.ResolveLensNeverChosen(two, fallbackToOnly: true));
        }

        [Test]
        public void ResolveLens_IsOptIn_WithoutFallbackToOnlyAnUnchosenCalendarStaysBareNumbers()
        {
            Assert.IsNull(OWCalendarLens.ResolveLensNeverChosen(Elements));
        }
#pragma warning restore CS0618

        // ---- resolveCalendar, npm 5.0's name for resolveCalendarLens (the TypeScript SDK's src/calendar/index.ts) ----

        [Test]
        public void ResolveCalendar_ResolvesALiveId_AndDegradesDanglingOrNullToBareNumbers()
        {
            Assert.AreEqual("Imperial", OWCalendarLens.ResolveCalendar(Elements, "c1")?.Name);
            Assert.IsNull(OWCalendarLens.ResolveCalendar(Elements, "gone"));
            Assert.IsNull(OWCalendarLens.ResolveCalendar(Elements, null), "an explicit no-calendar choice, even with one calendar");
            Assert.IsNull(OWCalendarLens.ResolveCalendar(new List<JObject>(), "c1"));
            Assert.IsNull(OWCalendarLens.ResolveCalendar(null, "c1"));
        }

        [Test]
        public void ResolveCalendarNeverChosen_TakesTheOnlyCalendar_OnlyWhenAskedAndOnlyWhenThereIsOne()
        {
            Assert.AreEqual("Imperial", OWCalendarLens.ResolveCalendarNeverChosen(Elements, fallbackToOnly: true)?.Name);
            Assert.IsNull(OWCalendarLens.ResolveCalendarNeverChosen(Elements), "opt-in");
            var two = Elements.Concat(new[] { El("c2", "construct", "Guild", CONFIG) }).ToList();
            Assert.IsNull(OWCalendarLens.ResolveCalendarNeverChosen(two, fallbackToOnly: true), "never a guess");
        }

        [Test]
        public void ResolveCalendar_IdMatchesOnlyATextId()
        {
            var numbered = new List<JObject> { El("c1", "construct", "Imperial", CONFIG) };
            numbered[0]["id"] = 5;
            Assert.IsNull(OWCalendarLens.ResolveCalendar(numbered, "5"));
        }

        [Test]
        public void TheLensNames_AreDeprecatedInFavourOfTheCalendarNames_AsAWarning()
        {
            foreach (var (old, use) in new[] { ("ResolveLens", "ResolveCalendar"), ("ResolveLensNeverChosen", "ResolveCalendarNeverChosen") })
            {
                var obsolete = (System.ObsoleteAttribute)System.Attribute.GetCustomAttribute(
                    typeof(OWCalendarLens).GetMethod(old), typeof(System.ObsoleteAttribute), false);
                Assert.IsNotNull(obsolete, $"{old} is not marked [Obsolete]");
                Assert.IsFalse(obsolete.IsError, $"{old}: a warning, not an error");
                StringAssert.StartsWith($"Since 0.58.0. Leaves in 1.0. Use {use}(", obsolete.Message, old);
                Assert.IsNull(System.Attribute.GetCustomAttribute(typeof(OWCalendarLens).GetMethod(use), typeof(System.ObsoleteAttribute)), use);
            }
        }

        [Test]
        public void AValueWrittenAsAString_IsReadOnlyWhenItIsStrictJson()
        {
            // The profile (dist.32): a calendar written as a JSON string is strict JSON, as JSON.parse reads it.
            Assert.IsNotNull(OWCalendarLens.ReadCalendarValue(new JValue(" {\"version\": 1, \"grain\": \"year\", \"eras\": [{\"name\": \"A\", \"start\": -1.5e2}]}\n")));

            var loose = new[]
            {
                "{'version': 1, 'grain': 'year'}",
                "{version: 1, grain: \"year\"}",
                "{\"version\": 1, \"grain\": \"year\"} // a note",
                "{\"version\": 1, /* x */ \"grain\": \"year\"}",
                "{\"version\": 1, \"grain\": \"year\",}",
                "{\"version\": 0x1, \"grain\": \"year\"}",
                "{\"version\": 01, \"grain\": \"year\"}",
                "{\"version\": 1, \"grain\": \"year\", \"x\": NaN}",
                "{\"version\": 1, \"grain\": \"ye\tar\"}",
                "﻿{\"version\": 1, \"grain\": \"year\"}",
            };
            foreach (var text in loose)
                Assert.IsNull(OWCalendarLens.ReadCalendarValue(new JValue(text)), "loose JSON is not a calendar: " + text);
        }
    }
}
