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

        // ---- resolveCalendarLens ----
        // TS tells "never chosen" (undefined) from "explicitly none" (null). C# has one null, so "never chosen"
        // is its own call, ResolveLensNeverChosen; ResolveLens takes a choice, null meaning "no calendar".

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
    }
}
