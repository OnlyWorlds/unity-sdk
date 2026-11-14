// The address profile: one element (or a world) named by one triple, the world's id, the type word and the element's
// id, in three spellings, with an optional moment and lens (schema-dist profiles/address.md, serial 77, ruled
// 2026-10-26). OWAddress.ElementAddress writes it, OWAddress.ParseLink reads it: the TypeScript SDK's and the Python
// package's elementAddress and parseOwLink.
//
// Written from the profile and its conformance fixture, profiles/address.fixture.json, which is this file's own test
// (OWAddressTests), as it is the siblings'. The type words are the generated OWElementTypes.Slugs, never a list here;
// the trim is the room profile's (OWProfileText).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Why an address is refused, by the profile's own words. A reader checks them in this order and the first that
    /// fails names the refusal, so every reader refuses a string for the same reason.
    /// </summary>
    public static class OWAddressRefusal
    {
        /// <summary>No <c>https://www.onlyworlds.com/w/</c> or <c>ow://</c> prefix: another host, <c>http</c>, the bare host, a member's page, a bare id.</summary>
        public const string NotAnAddress = "not_an_address";

        /// <summary>The prefix or a type word, in another case.</summary>
        public const string Uppercase = "uppercase";

        /// <summary>The wrong number of path segments, an empty one (a trailing slash, an empty id), or a type with no id.</summary>
        public const string Segments = "segments";

        /// <summary>The world segment is not an id (or, in the human form only, a slug).</summary>
        public const string BadWorld = "bad_world";

        /// <summary>A word that is not one of the 22 type words.</summary>
        public const string UnknownType = "unknown_type";

        /// <summary>The element id is not a UUID in the hyphenated 8-4-4-4-12 form.</summary>
        public const string BadId = "bad_id";

        /// <summary>The moment is not a step: a whole number in the 32-bit range, written plainly.</summary>
        public const string BadMoment = "bad_moment";

        /// <summary>
        /// A <c>lens</c> key's value that is not an id or is empty, the key twice, or the key's four letters in another
        /// case (<c>LENS</c>, <c>Lens</c>). A key is compared as written, nothing trimmed: one with a space in it is another
        /// key, ignored like any other. The id the key holds is read in either case.
        /// </summary>
        public const string BadLens = "bad_lens";

        /// <summary>A writer's: no world where one is needed (the human form, a world's own text form).</summary>
        public const string NoWorld = "no_world";

        /// <summary>A writer's: a form other than <c>human</c> or <c>text</c>.</summary>
        public const string BadForm = "bad_form";

        /// <summary>Every refusal, in the order a parser checks them (the last two are a writer's only).</summary>
        public static readonly string[] All =
        {
            NotAnAddress, Uppercase, Segments, BadWorld, UnknownType, BadId, BadMoment, BadLens, NoWorld, BadForm,
        };
    }

    /// <summary>A writer's refusal: <see cref="Reason"/> is one of <see cref="OWAddressRefusal"/>'s words.</summary>
    public sealed class OWAddressException : ArgumentException
    {
        public OWAddressException(string reason)
            : base("Not an address the profile writes: " + reason)
        {
            Reason = reason;
        }

        /// <summary>The refusal, as <see cref="OWAddressRefusal"/> spells it.</summary>
        public string Reason { get; }
    }

    /// <summary>What <see cref="OWAddress.ParseLink"/> reads from one address.</summary>
    public sealed class OWAddressReading
    {
        internal OWAddressReading(string form, string world, bool worldIsSlug, string type, string id, string lens, int? moment)
        {
            Form = form;
            World = world;
            WorldIsSlug = worldIsSlug;
            Type = type;
            Id = id;
            Lens = lens;
            Moment = moment;
        }

        /// <summary><see cref="OWAddress.Human"/> (the site's address) or <see cref="OWAddress.Text"/> (<c>ow://</c>).</summary>
        public string Form { get; }

        /// <summary>
        /// The world's id in lowercase, or its slug as written when <see cref="WorldIsSlug"/>; null in the text form
        /// inside a world (<c>ow://&lt;type&gt;/&lt;id&gt;</c>).
        /// </summary>
        public string World { get; }

        /// <summary>The human address named its world by slug: read, never canonical, and never written back.</summary>
        public bool WorldIsSlug { get; }

        /// <summary>One of the 22 type words; null for a world's own address.</summary>
        public string Type { get; }

        /// <summary>The element's id in lowercase; null for a world's own address.</summary>
        public string Id { get; }

        /// <summary>The <c>lens</c> query key's element id in lowercase, or null. It never widens what a reader may see.</summary>
        public string Lens { get; }

        /// <summary>
        /// The step after <c>@</c>, in the world's <c>time_basic_unit</c>, or null. A tool that cannot show a moment may
        /// ignore it: the address is the same address.
        /// </summary>
        public int? Moment { get; }

        /// <summary>True when the string as received is exactly what a writer writes for this reading.</summary>
        public bool Canonical { get; internal set; }

        /// <summary>A world's own address: no type and no id.</summary>
        public bool IsWorld => Id == null;
    }

    /// <summary>What <see cref="OWAddress.ReadLinkAt"/> finds: the address read, and the index just past it in the text.</summary>
    public sealed class OWAddressAt
    {
        internal OWAddressAt(OWAddressReading reading, int end)
        {
            Reading = reading;
            End = end;
        }

        /// <summary>What <see cref="OWAddress.ParseLink"/> reads from <c>text.Substring(index, End - index)</c>.</summary>
        public OWAddressReading Reading { get; }

        /// <summary>
        /// The index just past the address: the text's first character that is not part of it, or its length. A C#
        /// string index, so it counts UTF-16 code units, as <see cref="string.Substring(int, int)"/> and
        /// <see cref="string.IndexOf(string)"/> do.
        /// </summary>
        public int End { get; }
    }

    /// <summary>
    /// The address of an element, or of a world, in the profile's three spellings, read and written alike by every
    /// package (schema-dist <c>profiles/address.md</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>https://www.onlyworlds.com/w/&lt;world id&gt;/&lt;type&gt;/&lt;id&gt;</c> is a link for a person;
    /// <c>ow://&lt;type&gt;/&lt;id&gt;</c> names an element of the same world in a text field, and
    /// <c>ow://&lt;world id&gt;/&lt;type&gt;/&lt;id&gt;</c> one of another world. A world alone is
    /// <c>https://www.onlyworlds.com/w/&lt;world id&gt;</c> or <c>ow://&lt;world id&gt;</c>. Any of them may end in
    /// <c>@&lt;step&gt;</c> (a moment) and <c>?lens=&lt;id&gt;</c>, the moment first.
    /// </para>
    /// <para>
    /// A field that holds an address is public text: never resolve, fetch or unfurl it on a server. A reader shows one
    /// wording, "private or missing", for a world it cannot read and an element that is not there. An address is not a
    /// foreign key, not an identity key and not a promise that its target exists.
    /// </para>
    /// <para>
    /// Static, null for none, in the house style of <see cref="OWJoin.ParseLink"/>; <see cref="TryParseLink"/> also says
    /// why. <see cref="ParseLink"/> reads one string whole; <see cref="ReadLinkAt"/> reads an address found inside other
    /// text (a sentence, a line of a report) from the index where its prefix starts, and says where it ends.
    /// </para>
    /// </remarks>
    public static class OWAddress
    {
        /// <summary>The human form: the site's page for the element.</summary>
        public const string Human = "human";

        /// <summary>The text form, <c>ow://</c>, inside a world or across worlds.</summary>
        public const string Text = "text";

        /// <summary>The human form's prefix, the only host and scheme a reader reads.</summary>
        public const string HumanPrefix = "https://www.onlyworlds.com/w/";

        /// <summary>The text form's prefix.</summary>
        public const string TextPrefix = "ow://";

        /// <summary>The one query key the profile reads.</summary>
        public const string LensKey = "lens";

        /// <summary>The keys of an address as JSON: its parts, and a reading's two flags.</summary>
        internal static readonly string[] JsonKeys = { "form", "world", "type", "id", "lens", "moment", "world_is_slug", "canonical" };

        /// <summary>The reading of <paramref name="text"/>, or null when the profile refuses it.</summary>
        public static OWAddressReading ParseLink(string text) => TryParseLink(text, out var reading, out _) ? reading : null;

        /// <summary>
        /// Reads <paramref name="text"/> as the profile's reader does. True with the <paramref name="reading"/>; false
        /// with the <paramref name="refusal"/> (one of <see cref="OWAddressRefusal"/>'s words) and a null reading.
        /// </summary>
        public static bool TryParseLink(string text, out OWAddressReading reading, out string refusal)
        {
            reading = Read(text, out refusal);
            return reading != null;
        }

        /// <summary>
        /// The address a writer writes: lowercase throughout, the world's id (never its slug), the moment before the
        /// lens, nothing else. A world's own address takes no <paramref name="type"/> and no <paramref name="id"/>.
        /// </summary>
        /// <exception cref="OWAddressException">The parts are refused; <see cref="OWAddressException.Reason"/> says why.</exception>
        public static string ElementAddress(string form, string world, string type = null, string id = null,
            string lens = null, int? moment = null)
        {
            var refusal = Write(form, world, type, id, lens, moment, out var written);
            if (refusal != null) throw new OWAddressException(refusal);
            return written;
        }

        /// <summary>The canonical address for a reading. A reading by slug is refused (<see cref="OWAddressRefusal.BadWorld"/>).</summary>
        /// <exception cref="OWAddressException">The reading cannot be written.</exception>
        public static string ElementAddress(OWAddressReading reading)
        {
            if (reading == null) throw new ArgumentNullException(nameof(reading));
            return ElementAddress(reading.Form, reading.World, reading.Type, reading.Id, reading.Lens, reading.Moment);
        }

        /// <summary>
        /// The address for parts given as JSON, in the shape the TypeScript SDK and the Python package take and the
        /// fixture writes: <c>form</c>, <c>world</c>, <c>type</c>, <c>id</c>, <c>lens</c>, <c>moment</c>. A moment
        /// that is not a whole JSON number within the 32-bit range (a fraction, a string, a boolean) is refused as
        /// <see cref="OWAddressRefusal.BadMoment"/>; <c>3.0</c> is the step 3. Only a JSON null or a missing key is
        /// absent: an empty string is present and refuses as that part would (<c>world: ""</c> as
        /// <see cref="OWAddressRefusal.BadWorld"/>). Refusals come in the writer's order: <c>bad_form</c>,
        /// <c>segments</c>, <c>no_world</c>, then the reader's. A key outside those six (and a reading's
        /// <c>world_is_slug</c> and <c>canonical</c>, which are ignored) is not an address's part.
        /// </summary>
        /// <exception cref="OWAddressException">The parts are refused.</exception>
        /// <exception cref="ArgumentException">A key the shape does not hold (a misspelling, another case).</exception>
        public static string ElementAddress(JObject address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            foreach (var property in address.Properties())
            {
                if (Array.IndexOf(JsonKeys, property.Name) < 0)
                    throw new ArgumentException("Not a key of an address: '" + property.Name + "'. The keys are "
                                                + string.Join(", ", JsonKeys) + ".", nameof(address));
            }

            var form = Part(address, "form");
            var world = Part(address, "world");
            var type = Part(address, "type");
            var id = Part(address, "id");
            var lens = Part(address, "lens");

            int? moment = null;
            string momentRefusal = null;
            if (address.TryGetValue("moment", out var m) && m.Type != JTokenType.Null)
            {
                if (TryJsonStep(m, out var step)) moment = step;
                else momentRefusal = OWAddressRefusal.BadMoment;
            }

            var refusal = Write(form, world, type, id, lens, moment, out var written, momentRefusal);
            if (refusal != null) throw new OWAddressException(refusal);
            return written;
        }

        // -- An address in prose ----------------------------------------------------------------

        /// <summary>The query that continues an address found in prose; no other key does.</summary>
        private const string LensQuery = "?" + LensKey + "=";

        /// <summary>An id is 36 characters: a lens found in prose ends where its id ends.</summary>
        private const int IdLength = 36;

        /// <summary>The longest type word.</summary>
        private static readonly int TypeWordMax = OWElementTypes.Slugs.Max(t => t.Length);

        /// <summary>No path the parser reads is longer: a world's id, the longest type word and an element's id, two slashes.</summary>
        private static readonly int PathMax = IdLength + 1 + TypeWordMax + 1 + IdLength;

        /// <summary>No element path after a world is longer: a slash, the longest type word, a slash and an element's id.</summary>
        private static readonly int ElementPathMax = 1 + TypeWordMax + 1 + IdLength;

        /// <summary>
        /// The address that starts exactly at <paramref name="index"/> in a text, found in prose, and where it ends; null
        /// when the text there is not an address (the address page's "An address in prose", serial 123; the TypeScript
        /// SDK's <c>readOwLinkAt</c> and the Python package's <c>read_ow_link_at</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both forms, <c>ow://</c> and <c>https://www.onlyworlds.com/w/</c>, the prefix as written (lowercase). In the
        /// site's form the run after <c>/w/</c> is read whole, once: it ends at the first character outside the slug's
        /// alphabet (lowercase letters, digits, hyphens) or the id's (hex in either case); exactly an id's form is the id,
        /// any other run <see cref="ParseLink"/> reads as a slug is the slug, else there is no address. A run followed by
        /// a letter of any script, a combining mark or an underscore is cut inside a word and is no address
        /// (<c>/w/mopPetopia.</c>, <c>/w/hyper_ion.</c>); the character after it is read as a whole code point, so a
        /// letter above U+FFFF (two UTF-16 units) cuts as any letter does.
        /// </para>
        /// <para>
        /// After the world, or in the text form from the prefix, the path is the longest one <see cref="ParseLink"/>
        /// reads. After it the address continues only with <c>@</c> and a moment (its whole run of an optional minus and
        /// digits, taken only when it reads as a step) and then <c>?lens=</c> and a lens (an id, which ends where its 36
        /// characters end). Any other query key or a fragment is not part of an address found in prose: the address ends
        /// before its <c>?</c>, <c>&amp;</c> or <c>#</c>. What follows is prose, a full stop included.
        /// <see cref="ParseLink"/> itself still reads a string whole: <c>ow://object/&lt;id&gt;.</c> handed to it is refused.
        /// </para>
        /// <para>
        /// An address begins at the start of the text or after a character that does not glue it to a word: after a
        /// letter of any script, a combining mark or an underscore the start is inside a word and this answers null
        /// (<c>elbow://object/&lt;id&gt;</c>); a hyphen, a digit, white space or punctuation before it does not glue
        /// (<c>x-ow://</c>, <c>3ow://</c>, <c>(ow://</c>). The same set read from both ends; the character before is read
        /// as a whole code point (staged: ruled 2026-11-14, publishes with a later serial).
        /// </para>
        /// <para>
        /// <paramref name="index"/> and <see cref="OWAddressAt.End"/> are C# string indexes (UTF-16 code units), so they
        /// match <see cref="string.IndexOf(string)"/> and <see cref="string.Substring(int, int)"/>. An index past the end
        /// reads nothing.
        /// </para>
        /// <code>
        /// var line = "Built the gate: ow://object/01a1d4ef-0000-7000-8000-000000000084.";
        /// var found = OWAddress.ReadLinkAt(line, 16);
        /// // found.End == 64: line[64] is the full stop; found.Reading.Id == "01a1d4ef-0000-7000-8000-000000000084"
        /// </code>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is below 0.</exception>
        public static OWAddressAt ReadLinkAt(string text, int index = 0)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), index, "an index is a whole number from 0");

            // A start glued to a word is prose, the mirror of the cut after a run (staged: ruled 2026-11-14, publishes
            // with a later serial). The character before is read whole: a letter above U+FFFF is two UTF-16 units.
            if (index > 0 && index <= text.Length && IsInAWord(text, CodePointStart(text, index - 1))) return null;

            var human = StartsAt(text, index, HumanPrefix);
            int start;
            if (human) start = index + HumanPrefix.Length;
            else if (StartsAt(text, index, TextPrefix)) start = index + TextPrefix.Length;
            else return null;

            var end = -1;
            var floor = start;
            var run = start;
            if (human)
            {
                // The site's form: the run after /w/ is read whole, once, with no order between an id and a slug.
                var w = start;
                while (w < text.Length && IsWorldRunChar(text[w])) w++;
                if (IsInAWord(text, w)) return null;
                var world = text.Substring(start, w - start);
                if (!IsUuid(world) && !IsSlug(world)) return null;
                end = floor = run = w;
                while (run < text.Length && run - w <= ElementPathMax && IsPathChar(text[run])) run++;
            }
            else
            {
                while (run < text.Length && run - start <= PathMax && IsPathChar(text[run])) run++;
            }

            for (var p = run; p > floor; p--)
            {
                if (Parses(text, index, p))
                {
                    end = p;
                    break;
                }
            }

            if (end == -1) return null;

            // A moment: its whole run of an optional minus and digits, taken only when it reads as a step.
            if (end < text.Length && text[end] == '@')
            {
                var stepEnd = end + 1;
                if (stepEnd < text.Length && text[stepEnd] == '-') stepEnd++;
                var digits = stepEnd;
                while (stepEnd < text.Length && text[stepEnd] >= '0' && text[stepEnd] <= '9') stepEnd++;
                if (stepEnd > digits && Parses(text, index, stepEnd)) end = stepEnd;
            }

            // A lens: an id, which ends where its 36 characters end.
            if (StartsAt(text, end, LensQuery))
            {
                var lensEnd = end + LensQuery.Length + IdLength;
                if (lensEnd <= text.Length && IsUuid(text.Substring(end + LensQuery.Length, IdLength)) && Parses(text, index, lensEnd))
                    end = lensEnd;
            }

            return new OWAddressAt(ParseLink(text.Substring(index, end - index)), end);
        }

        /// <summary>Where the code point holding the unit at <paramref name="i"/> starts: one unit back when it is the low half of a pair.</summary>
        private static int CodePointStart(string text, int i)
            => i > 0 && char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]) ? i - 1 : i;

        /// <summary>Whether <paramref name="prefix"/> is written at <paramref name="index"/> of the text, ordinally.</summary>
        private static bool StartsAt(string text, int index, string prefix)
            => index <= text.Length - prefix.Length && string.CompareOrdinal(text, index, prefix, 0, prefix.Length) == 0;

        /// <summary>Whether the text from <paramref name="index"/> to <paramref name="end"/> parses whole as an address.</summary>
        private static bool Parses(string text, int index, int end) => Read(text.Substring(index, end - index), out _) != null;

        /// <summary>The characters a path segment can hold (an id, a slug, a type word in any case) and the slash between them.</summary>
        private static bool IsPathChar(char c) => c >= '0' && c <= '9' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c == '/' || c == '-';

        /// <summary>The run after <c>/w/</c>: the slug's alphabet (lowercase letters, digits, hyphens) and the id's (hex in either case).</summary>
        private static bool IsWorldRunChar(char c) => c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'F' || c == '-';

        /// <summary>
        /// Whether the code point that starts at <paramref name="i"/> glues a run into a word: a letter of any script, a
        /// combining mark (Unicode categories L and M) or an underscore. A surrogate pair is read whole, as one code point.
        /// </summary>
        private static bool IsInAWord(string text, int i)
        {
            if (i < 0 || i >= text.Length) return false;
            if (text[i] == '_') return true;
            switch (CharUnicodeInfo.GetUnicodeCategory(text, i))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return true;
                default:
                    return false;
            }
        }

        // -- Reading ----------------------------------------------------------------------------

        private static OWAddressReading Read(string text, out string refusal)
        {
            refusal = null;

            // 1. Trim White_Space at both ends.
            var s = OWProfileText.TrimWhiteSpace(text ?? string.Empty);

            // 2. The prefix, as written; in another case it is uppercase.
            string form;
            if (s.StartsWith(HumanPrefix, StringComparison.Ordinal)) form = Human;
            else if (s.StartsWith(TextPrefix, StringComparison.Ordinal)) form = Text;
            else
            {
                refusal = StartsWithAsciiIgnoringCase(s, HumanPrefix) || StartsWithAsciiIgnoringCase(s, TextPrefix)
                    ? OWAddressRefusal.Uppercase
                    : OWAddressRefusal.NotAnAddress;
                return null;
            }

            var rest = s.Substring(form == Human ? HumanPrefix.Length : TextPrefix.Length);

            // 3. Drop a fragment, split off the query at the first ?, split the path on /, and the last segment at its
            // first @ into the id and the moment.
            var hash = rest.IndexOf('#');
            if (hash >= 0) rest = rest.Substring(0, hash);
            string query = null;
            var q = rest.IndexOf('?');
            if (q >= 0)
            {
                query = rest.Substring(q + 1);
                rest = rest.Substring(0, q);
            }

            var segments = rest.Split('/');
            string moment = null;
            var last = segments[segments.Length - 1];
            var at = last.IndexOf('@');
            if (at >= 0)
            {
                moment = last.Substring(at + 1);
                segments[segments.Length - 1] = last.Substring(0, at);
            }

            // 4. Segments: the human form one or three, a text form one, two or three, none empty; a lone type word is
            // a type with no id.
            var count = segments.Length;
            var countOk = form == Human ? count == 1 || count == 3 : count >= 1 && count <= 3;
            if (!countOk || segments.Any(x => x.Length == 0) || count == 1 && IsTypeWord(segments[0]))
            {
                refusal = OWAddressRefusal.Segments;
                return null;
            }

            string world = null, type = null, id = null;
            var isSlug = false;
            if (count == 1) world = segments[0];
            else if (count == 2)
            {
                type = segments[0];
                id = segments[1];
            }
            else
            {
                world = segments[0];
                type = segments[1];
                id = segments[2];
            }

            // 5. The world: an id, or in the human form only a slug.
            if (world != null)
            {
                if (IsUuid(world)) world = world.ToLowerInvariant();
                else if (form == Human && IsSlug(world)) isSlug = true;
                else
                {
                    refusal = OWAddressRefusal.BadWorld;
                    return null;
                }
            }

            // 6. The type word; 7. the element id.
            if (type != null)
            {
                refusal = TypeRefusal(type);
                if (refusal != null) return null;
                if (!IsUuid(id))
                {
                    refusal = OWAddressRefusal.BadId;
                    return null;
                }

                id = id.ToLowerInvariant();
            }

            // 8. The moment.
            int? step = null;
            if (moment != null)
            {
                if (!TryStep(moment, out var value))
                {
                    refusal = OWAddressRefusal.BadMoment;
                    return null;
                }

                step = value;
            }

            // 9. The lens; any other query key is ignored (and the address is not canonical).
            string lens = null;
            if (query != null)
            {
                var lenses = 0;
                foreach (var pair in query.Split('&'))
                {
                    var eq = pair.IndexOf('=');
                    var key = eq >= 0 ? pair.Substring(0, eq) : pair;
                    var value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;
                    if (key == LensKey)
                    {
                        lenses++;
                        lens = value;
                    }
                    else if (EqualsAsciiIgnoringCase(key, LensKey))
                    {
                        refusal = OWAddressRefusal.BadLens;
                        return null;
                    }
                }

                if (lenses > 1 || lenses == 1 && !IsUuid(lens))
                {
                    refusal = OWAddressRefusal.BadLens;
                    return null;
                }

                lens = lens?.ToLowerInvariant();
            }

            var reading = new OWAddressReading(form, world, isSlug, type, id, lens, step);
            // Canonical: the string as received (untrimmed) is what a writer writes for this reading; a slug never is.
            reading.Canonical = !isSlug
                                && Write(form, world, type, id, lens, step, out var written) == null
                                && string.Equals(text, written, StringComparison.Ordinal);
            return reading;
        }

        // -- Writing ----------------------------------------------------------------------------

        /// <summary>The refusal for these parts, or null with the address in <paramref name="written"/>.</summary>
        private static string Write(string form, string world, string type, string id, string lens, int? moment,
            out string written, string momentRefusal = null)
        {
            written = null;
            if (form != Human && form != Text) return OWAddressRefusal.BadForm;
            if (type == null != (id == null)) return OWAddressRefusal.Segments;
            if (world == null && (form == Human || type == null)) return OWAddressRefusal.NoWorld;
            if (world != null && !IsUuid(world)) return OWAddressRefusal.BadWorld;
            if (type != null)
            {
                var typeRefusal = TypeRefusal(type);
                if (typeRefusal != null) return typeRefusal;
                if (!IsUuid(id)) return OWAddressRefusal.BadId;
            }

            if (momentRefusal != null) return momentRefusal;
            if (lens != null && !IsUuid(lens)) return OWAddressRefusal.BadLens;

            var path = new List<string>(3);
            if (world != null) path.Add(world.ToLowerInvariant());
            if (type != null)
            {
                path.Add(type);
                path.Add(id.ToLowerInvariant());
            }

            written = (form == Human ? HumanPrefix : TextPrefix)
                      + string.Join("/", path)
                      + (moment.HasValue ? "@" + moment.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)
                      + (lens != null ? "?" + LensKey + "=" + lens.ToLowerInvariant() : string.Empty);
            return null;
        }

        /// <summary>
        /// A part of a JSON address: a string, or null when missing or JSON null. An empty string is present, and
        /// refuses as that part would.
        /// </summary>
        internal static string Part(JObject address, string key)
        {
            if (!address.TryGetValue(key, out var token) || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String) return key == "form" ? string.Empty : token.ToString();
            return (string)token;
        }

        /// <summary>
        /// A JSON moment as a step: an integer, or a number with a zero fraction (<c>3.0</c>, as JavaScript has one
        /// number), within the 32-bit range.
        /// </summary>
        private static bool TryJsonStep(JToken token, out int step)
        {
            step = 0;
            decimal n;
            if (token.Type == JTokenType.Integer)
            {
                var raw = ((JValue)token).Value;
                if (raw is BigInteger) return false; // past 64 bits, so past 32
                n = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
            }
            else if (token.Type == JTokenType.Float)
            {
                var d = token.Value<double>();
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d || d < int.MinValue || d > int.MaxValue)
                    return false;
                n = (decimal)d;
            }
            else return false;

            if (n < int.MinValue || n > int.MaxValue) return false;
            step = (int)n;
            return true;
        }

        // -- The grammar's pieces ---------------------------------------------------------------

        private static bool IsTypeWord(string word) => Array.IndexOf(OWElementTypes.Slugs, word) >= 0;

        /// <summary>Null for one of the 22 words; uppercase for one in another case; else unknown.</summary>
        private static string TypeRefusal(string word)
        {
            if (IsTypeWord(word)) return null;
            return OWElementTypes.Slugs.Any(slug => EqualsAsciiIgnoringCase(slug, word))
                ? OWAddressRefusal.Uppercase
                : OWAddressRefusal.UnknownType;
        }

        /// <summary>A UUID in the hyphenated 8-4-4-4-12 form, any version, its hex in either case. Nothing else.</summary>
        internal static bool IsUuid(string s)
        {
            if (s == null || s.Length != 36) return false;
            for (var i = 0; i < 36; i++)
            {
                var c = s[i];
                if (i == 8 || i == 13 || i == 18 || i == 23)
                {
                    if (c != '-') return false;
                }
                else if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A world's slug: 3 to 40 lowercase letters, numbers and single hyphens, no hyphen at either end, never a type
        /// word (the site reserves them).
        /// </summary>
        private static bool IsSlug(string s)
        {
            if (s.Length < 3 || s.Length > 40 || s[0] == '-' || s[s.Length - 1] == '-') return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '-')
                {
                    if (s[i - 1] == '-') return false;
                }
                else if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9'))
                {
                    return false;
                }
            }

            return !IsTypeWord(s);
        }

        /// <summary>
        /// A step: decimal, an optional minus, no plus, no leading zero, no <c>-0</c>, within the 32-bit range.
        /// </summary>
        private static bool TryStep(string s, out int value)
        {
            value = 0;
            var digits = s.StartsWith("-", StringComparison.Ordinal) ? s.Substring(1) : s;
            if (digits.Length == 0 || digits.Length > 10) return false;
            foreach (var c in digits)
            {
                if (c < '0' || c > '9') return false;
            }

            if (digits[0] == '0' && (digits.Length > 1 || s.Length > 1)) return false;
            var n = long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (n < int.MinValue || n > int.MaxValue) return false;
            value = (int)n;
            return true;
        }

        private static bool StartsWithAsciiIgnoringCase(string s, string prefix)
            => s.Length >= prefix.Length && EqualsAsciiIgnoringCase(s.Substring(0, prefix.Length), prefix);

        /// <summary>Equal when ASCII letters are folded, and nothing else is: no culture, no Unicode case mapping.</summary>
        private static bool EqualsAsciiIgnoringCase(string a, string b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (Lower(a[i]) != Lower(b[i])) return false;
            }

            return true;
        }

        private static char Lower(char c) => c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
    }
}
