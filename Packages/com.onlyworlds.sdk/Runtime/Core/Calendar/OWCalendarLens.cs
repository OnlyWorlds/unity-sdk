// World calendars: which calendars a world holds, and the tolerant read of the field.
//
// Ported from @onlyworlds/sdk 4.23.0 src/calendar/index.ts (commit 60f08f3), which adapts Atlas's
// calendar-lens to any element shape; here the element shape is a JObject. A kernel change starts in the npm
// SDK and is ported here.
//
// The field: a calendar is an element (a Construct by convention) carrying `atlas_calendar`. Atlas owns the
// field, its meaning and its writes (its calendar design, sections 5 and 5b); every other tool reads it and
// never writes it. Detection keys on the field's presence, never on the supertype. Everything degrades to bare
// numbers: a missing, dangling or malformed calendar is null, never an error.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>A usable calendar found in a world.</summary>
    public sealed class WorldCalendar
    {
        /// <summary>The calendar element's id.</summary>
        public string Id { get; set; }
        /// <summary>The element's name: the calendar's display name.</summary>
        public string Name { get; set; }
        public CalendarConfig Config { get; set; }
    }

    /// <summary>Which calendars a world holds, over elements as JObjects.</summary>
    public static class OWCalendarLens
    {
        /// <summary>Atlas's field for a calendar's config (owned by Atlas: read it, never write it).</summary>
        public const string CalendarField = "atlas_calendar";

        /// <summary>The supertype Atlas gives a calendar Construct: convention and display only.</summary>
        public const string CalendarSupertype = "calendar";

        /// <summary>
        /// The tolerant `atlas_calendar` read: an object or a JSON string (another tool may serialize it; Atlas
        /// writes objects). Null for anything unusable. Read the field only through this: a reader that took
        /// objects alone would blank a string config. Pass the field's value (not the element).
        /// </summary>
        public static CalendarConfig ReadCalendarValue(JToken raw)
        {
            if (raw == null || raw.Type == JTokenType.Null || raw.Type == JTokenType.Undefined) return null;
            JToken value = raw;
            if (raw.Type == JTokenType.String)
            {
                value = ParseJson((string)raw);
                if (value == null) return null;
            }
            return OWCalendar.ReadConfig(value);
        }

        /// <summary>The calendar an element carries in its `atlas_calendar` field, or null.</summary>
        public static CalendarConfig ReadElementCalendar(JObject element)
        {
            return element == null ? null : ReadCalendarValue(element[CalendarField]);
        }

        /// <summary>
        /// Every usable calendar in the world: the elements with a valid `atlas_calendar`. A malformed config is
        /// skipped: a broken calendar is invisible, not an error.
        /// </summary>
        public static List<WorldCalendar> WorldCalendars(IEnumerable<JObject> elements)
        {
            var output = new List<WorldCalendar>();
            if (elements == null) return output;
            foreach (var el in elements)
            {
                if (el == null) continue;
                var config = ReadCalendarValue(el[CalendarField]);
                if (config == null) continue;
                string name = Text(el["name"]);
                output.Add(new WorldCalendar
                {
                    Id = Text(el["id"]),
                    Name = string.IsNullOrEmpty(name) ? "(unnamed calendar)" : name,
                    Config = config,
                });
            }
            return output;
        }

        /// <summary>
        /// Resolve a CHOSEN calendar id against the elements. A null id (an explicit "no calendar"), a dangling
        /// id or an invalid config resolve to null: bare numbers. (TS resolveCalendarLens with a string or null
        /// activeId; for "never chosen", the TS `undefined`, use <see cref="ResolveLensNeverChosen"/>.)
        /// </summary>
        public static WorldCalendar ResolveLens(IEnumerable<JObject> elements, string activeId)
        {
            if (string.IsNullOrEmpty(activeId)) return null;
            foreach (var c in WorldCalendars(elements))
                if (c.Id == activeId) return c;
            return null;
        }

        /// <summary>
        /// Resolve when no calendar was ever chosen (the TS `activeId === undefined`). With
        /// <paramref name="fallbackToOnly"/> and exactly one calendar in the world, use it; with several, nothing
        /// is guessed; without the flag, bare numbers.
        /// </summary>
        public static WorldCalendar ResolveLensNeverChosen(IEnumerable<JObject> elements, bool fallbackToOnly = false)
        {
            if (!fallbackToOnly) return null;
            var all = WorldCalendars(elements);
            return all.Count == 1 ? all[0] : null;
        }

        static string Text(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined) return null;
            return t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
        }

        /// <summary>JSON.parse, or null when the text is not JSON. Dates stay strings (Newtonsoft would turn
        /// them into DateTime), and trailing content is refused as JSON.parse refuses it.</summary>
        static JToken ParseJson(string text)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                {
                    var token = JToken.ReadFrom(reader);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment) return null;
                    }
                    return token;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
