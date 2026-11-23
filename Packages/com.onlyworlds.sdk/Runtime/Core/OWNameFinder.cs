// Element names found in prose: a world's names read once, then found in any text. Parity with @onlyworlds/sdk
// 4.194.0 (src/name-finder.ts, nameFinder) and the Python package's name_finder: a rule of the packages, not of the
// standard, held by one file of cases (Tests/Editor/Fixtures/name-finder/name-finder.cases.json, the same bytes as
// the TypeScript SDK's test/fixtures/name-finder.cases.json).

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>How <see cref="OWNameFinder"/> compares a name with the text. There is no default: a value left out is refused.</summary>
    public enum OWNameCase
    {
        /// <summary>A name as written: another case is another word.</summary>
        Exact = 1,

        /// <summary>
        /// By simple case folding (Unicode 15.1, the fold the <c>.owworld</c> reader uses): <c>CONSUL</c> holds the
        /// name <c>Consul</c>; the sharp s is never two letters, and a dotted capital I stays itself.
        /// </summary>
        Folded = 2,
    }

    /// <summary>One name <see cref="OWNameFinder{T}.Find"/> found in a text.</summary>
    /// <typeparam name="T">The kind of element the finder was made from.</typeparam>
    public sealed class OWNameFound<T>
    {
        internal OWNameFound(int start, int end, T element)
        {
            Start = start;
            End = end;
            Element = element;
        }

        /// <summary>
        /// The index of the name's first character in the text: a C# string index, so it counts UTF-16 code units, as
        /// <see cref="string.Substring(int, int)"/> and <see cref="OWAddressFound.Start"/> do (the TypeScript SDK counts
        /// the same units; the Python package counts code points). A character above the Basic Multilingual Plane
        /// before the name counts two.
        /// </summary>
        public int Start { get; }

        /// <summary>The index just past the name's last character, in the same units: <c>text.Substring(Start, End - Start)</c> is the name as the text writes it.</summary>
        public int End { get; }

        /// <summary>The element the name belongs to: the same object the finder was given.</summary>
        public T Element { get; }
    }

    /// <summary>
    /// A finder of a world's element names in prose, made once by <see cref="OWNameFinder.For(IEnumerable{JToken}, OWNameCase, Func{JToken, IEnumerable{object}})"/>
    /// (or another overload of <c>For</c>); <see cref="Find"/> is then called on each text. It finds and never writes:
    /// what a tool does with a found name (a mark for a reader, a suggestion, a link a person chooses to make) is the
    /// tool's.
    /// </summary>
    /// <typeparam name="T">The kind of element the finder was made from.</typeparam>
    public sealed class OWNameFinder<T>
    {
        private const int NameMin = 3;

        private sealed class Hit
        {
            internal Hit(string id, T element)
            {
                Id = id;
                Element = element;
            }

            internal readonly string Id;
            internal readonly T Element;
            internal bool Shared;
        }

        private sealed class Node
        {
            internal readonly Dictionary<int, Node> Next = new Dictionary<int, Node>();
            internal Hit Hit;
        }

        private readonly Node _root = new Node();
        private readonly bool _folded;

        internal OWNameFinder(IEnumerable<T> elements, OWNameCase nameCase, Func<T, string> idOf, Func<T, IEnumerable<object>> names)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements), "OWNameFinder: elements is a list of elements with an id and a name");
            if (nameCase != OWNameCase.Exact && nameCase != OWNameCase.Folded)
                throw new ArgumentException($"OWNameFinder: nameCase is OWNameCase.Exact or OWNameCase.Folded, and must be given; got {(int)nameCase}.", nameof(nameCase));
            if (idOf == null) throw new ArgumentNullException(nameof(idOf), "OWNameFinder: idOf is a function from an element to its id");
            if (names == null) throw new ArgumentNullException(nameof(names), "OWNameFinder: names is a function from an element to its names");
            _folded = nameCase == OWNameCase.Folded;

            var points = new List<int>();
            foreach (var element in elements)
            {
                var id = element == null ? null : idOf(element);
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("OWNameFinder: every element is an object with a string id.", nameof(elements));
                var given = names(element);
                if (given == null) throw new ArgumentException($"OWNameFinder: names answered null for the element {id}; answer an empty list for none.", nameof(names));
                foreach (var raw in given)
                {
                    var text = NameText(raw);
                    if (text == null) continue;
                    var name = OWProfileText.TrimWhiteSpace(text);
                    points.Clear();
                    for (var k = 0; k < name.Length; k += Width(name, k))
                    {
                        var p = CodePointAt(name, k);
                        points.Add(_folded ? OWCaseFold.Fold(p) : p);
                    }

                    if (points.Count < NameMin) continue;
                    var node = _root;
                    foreach (var p in points)
                    {
                        if (!node.Next.TryGetValue(p, out var child))
                        {
                            child = new Node();
                            node.Next.Add(p, child);
                        }

                        node = child;
                    }

                    if (node.Hit == null) node.Hit = new Hit(id, element);
                    else if (!string.Equals(node.Hit.Id, id, StringComparison.Ordinal)) node.Hit.Shared = true;
                }
            }
        }

        /// <summary>
        /// Every name of the finder's elements in <paramref name="text"/>, in order, none overlapping, each as an
        /// <see cref="OWNameFound{T}"/> (start, end, and the element that was passed in). A new list on each call.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>A whole name only: the code point on each side of it is not a letter (Unicode category L), a mark (M),
        /// a decimal digit (Nd) or an underscore, in any script. So <c>Kira's</c> and <c>Kira-led</c> hold the name
        /// <c>Kira</c>, and <c>Kiran</c> does not. Outside ASCII the category is the runtime's own
        /// (<see cref="CharUnicodeInfo.GetUnicodeCategory(string, int)"/>), as the TypeScript SDK reads JavaScript's and the
        /// Python package Python's, so a letter assigned in a newer Unicode than the runtime knows is no word character
        /// here. Unity 6's Mono reads Unicode 11.0's categories: beside a letter added since (one of CJK Extension G,
        /// say), a name is whole here and inside a word for the TypeScript SDK on a current Node. The same read
        /// decides where <see cref="OWAddress.FindLinks"/> takes a start glued to a word.</item>
        /// <item>A name is trimmed of Unicode's White_Space and is three code points at least; a shorter one is never
        /// looked for.</item>
        /// <item>Where several names start at one place, the longest whole one is taken (<c>Iron Gate</c> over
        /// <c>Iron</c>), and the search goes on after it.</item>
        /// <item>A name two elements share (by id) is never answered, and nothing inside it is: the text stays plain
        /// there. One element listed twice, or found by two of its own names, is one element.</item>
        /// <item>Nothing is read inside an address (what <see cref="OWAddress.FindLinks"/> finds) or a Markdown link
        /// <c>[label](target)</c> (a <c>[</c>, a label up to the first <c>]</c>, a <c>(</c> right after it, a target
        /// up to the first <c>)</c>), and no name is read across the edge of one.</item>
        /// <item>Text is compared as written: no Unicode normalisation (a letter and a combining mark are not the
        /// one-character letter), and a space in a name matches that one space.</item>
        /// </list>
        /// The work for one text is at most its length times the longest name's length, whatever the number of names,
        /// with no pattern that backtracks.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public IReadOnlyList<OWNameFound<T>> Find(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text), "OWNameFinder: text must be a string");
            var found = new List<OWNameFound<T>>();
            if (_root.Next.Count == 0) return found;
            var spans = PassedOver(text);
            var length = text.Length;
            var span = 0;
            var i = 0;
            while (i < length)
            {
                if (span < spans.Count && i >= spans[span].start)
                {
                    i = spans[span].end;
                    span++;
                    continue;
                }

                var width = Width(text, i);
                if (!WordBefore(text, i))
                {
                    var limit = span < spans.Count ? spans[span].start : length;
                    var node = _root;
                    var j = i;
                    Hit best = null;
                    var bestEnd = -1;
                    while (j < limit)
                    {
                        var c = CodePointAt(text, j);
                        if (!node.Next.TryGetValue(_folded ? OWCaseFold.Fold(c) : c, out node)) break;
                        j += Width(text, j);
                        if (node.Hit != null && (j >= length || !IsWordAt(text, j)))
                        {
                            best = node.Hit;
                            bestEnd = j;
                        }
                    }

                    if (best != null)
                    {
                        if (!best.Shared) found.Add(new OWNameFound<T>(i, bestEnd, best.Element));
                        i = bestEnd;
                        continue;
                    }
                }

                i += width;
            }

            return found;
        }

        /// <summary>A name's text: a string, or a JSON string token; anything else is no name.</summary>
        private static string NameText(object raw)
        {
            if (raw is string s) return s;
            if (raw is JValue v && v.Type == JTokenType.String) return (string)v.Value;
            return null;
        }

        /// <summary>The code point at <paramref name="i"/>: a surrogate pair read whole, a lone surrogate as its own value.</summary>
        private static int CodePointAt(string text, int i)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) return char.ConvertToUtf32(c, text[i + 1]);
            return c;
        }

        /// <summary>How many UTF-16 units the code point at <paramref name="i"/> takes: two for a surrogate pair, else one.</summary>
        private static int Width(string text, int i)
            => char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;

        /// <summary>Whether the code point that ends just before <paramref name="i"/> is a word character; false at the start of the text.</summary>
        private static bool WordBefore(string text, int i)
        {
            if (i <= 0) return false;
            if (i >= 2 && char.IsLowSurrogate(text[i - 1]) && char.IsHighSurrogate(text[i - 2])) return IsWordAt(text, i - 2);
            return IsWordAt(text, i - 1);
        }

        /// <summary>
        /// Whether the code point that starts at <paramref name="i"/> is a word character: an ASCII letter or digit or
        /// an underscore, or outside ASCII a letter, a mark or a decimal digit by the runtime's categories. A lone
        /// surrogate is none.
        /// </summary>
        private static bool IsWordAt(string text, int i)
        {
            var c = text[i];
            if (c < 0x80) return c >= '0' && c <= '9' || c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c == '_';
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
                case UnicodeCategory.DecimalDigitNumber:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Every Markdown link <c>[label](target)</c> in a text as <c>[start, end)</c>: a <c>[</c>, a label up to the
        /// first <c>]</c>, a <c>(</c> right after it, a target up to the first <c>)</c>. One pass: each bracket and
        /// parenthesis is searched for once.
        /// </summary>
        private static List<(int start, int end)> MarkdownLinks(string text)
        {
            var spans = new List<(int start, int end)>();
            var close = -2;
            var paren = -2;
            var open = text.IndexOf('[');
            while (open != -1)
            {
                if (close <= open) close = text.IndexOf(']', open + 1);
                if (close == -1) break;
                if (close + 1 >= text.Length || text[close + 1] != '(')
                {
                    open = text.IndexOf('[', open + 1);
                    continue;
                }

                if (paren < close + 2) paren = close + 2 <= text.Length ? text.IndexOf(')', close + 2) : -1;
                if (paren == -1) break;
                spans.Add((open, paren + 1));
                open = paren + 1 < text.Length ? text.IndexOf('[', paren + 1) : -1;
            }

            return spans;
        }

        /// <summary>The stretches no name is read in: every address and every Markdown link, in order, none overlapping.</summary>
        private static List<(int start, int end)> PassedOver(string text)
        {
            var spans = MarkdownLinks(text);
            foreach (var a in OWAddress.FindLinks(text)) spans.Add((a.Start, a.End));
            spans.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : a.end.CompareTo(b.end));
            var merged = new List<(int start, int end)>(spans.Count);
            foreach (var s in spans)
            {
                if (merged.Count > 0 && s.start < merged[merged.Count - 1].end)
                {
                    var last = merged[merged.Count - 1];
                    if (s.end > last.end) merged[merged.Count - 1] = (last.start, s.end);
                }
                else
                {
                    merged.Add(s);
                }
            }

            return merged;
        }
    }

    /// <summary>
    /// A world's element names found in prose: <c>For</c> reads the names once and answers an
    /// <see cref="OWNameFinder{T}"/>, whose <see cref="OWNameFinder{T}.Find"/> finds them in any text. The TypeScript SDK's
    /// <c>nameFinder</c> and the Python package's <c>name_finder</c>, on the same cases.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>nameCase</c> must be given, <see cref="OWNameCase.Exact"/> or <see cref="OWNameCase.Folded"/>: the enum has no
    /// zero value, so <c>default(OWNameCase)</c> is refused, and a tool never gets the other rule by leaving it out.
    /// </para>
    /// <para>
    /// <c>names</c> gives the names one element is found by; left null, an element is found by its <c>name</c> alone. A
    /// name is a <see cref="string"/> or a JSON string token (<see cref="JValue"/> of <see cref="JTokenType.String"/>);
    /// any other value (null, a number) is passed over. Nothing given is changed, and the names are read once, when
    /// the finder is made: an element added to the list afterwards is not looked for.
    /// </para>
    /// <code>
    /// var find = OWNameFinder.For(new JToken[]
    /// {
    ///     new JObject { ["id"] = "a1", ["name"] = "Iron Gate" },
    ///     new JObject { ["id"] = "b2", ["name"] = "Kira" },
    /// }, OWNameCase.Exact);
    /// var found = find.Find("Kira's watch ends at the Iron Gate.");
    /// // found[0]: Start 0, End 4, Element the b2 object; found[1]: Start 25, End 34, Element the a1 object
    /// </code>
    /// </remarks>
    public static class OWNameFinder
    {
        /// <summary>
        /// A finder of the names of elements as JSON (a world's rows as the API or a folder serves them): each one an
        /// object with a non-empty string <c>id</c>, found by its string <c>name</c> unless <paramref name="names"/>
        /// says otherwise. An answer's <see cref="OWNameFound{T}.Element"/> is the token that was passed in.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="nameCase"/> is neither value; an element is not an
        /// object with a non-empty string <c>id</c>; <paramref name="names"/> answers null.</exception>
        public static OWNameFinder<JToken> For(IEnumerable<JToken> elements, OWNameCase nameCase, Func<JToken, IEnumerable<object>> names = null)
            => new OWNameFinder<JToken>(elements, nameCase, JsonId, names ?? JsonName);

        /// <summary>
        /// A finder of the names of typed elements (<see cref="OWElement"/> and the generated models), each found by its
        /// <see cref="OWElement.Name"/> unless <paramref name="names"/> says otherwise. An answer's
        /// <see cref="OWNameFound{T}.Element"/> is the object that was passed in.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="nameCase"/> is neither value; an element is null or has
        /// no <see cref="OWElement.Id"/>; <paramref name="names"/> answers null.</exception>
        public static OWNameFinder<TElement> For<TElement>(IEnumerable<TElement> elements, OWNameCase nameCase, Func<TElement, IEnumerable<object>> names = null)
            where TElement : OWElement
            => new OWNameFinder<TElement>(elements, nameCase, e => e.Id, names ?? (e => new object[] { e.Name }));

        /// <summary>
        /// A finder of the names of elements of any kind of the caller's own: <paramref name="idOf"/> gives an element's
        /// id (two elements are one when their ids are equal, ordinally) and <paramref name="names"/> its names.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/>, <paramref name="idOf"/> or
        /// <paramref name="names"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="nameCase"/> is neither value; an element is null or its id
        /// is null or empty; <paramref name="names"/> answers null.</exception>
        public static OWNameFinder<TElement> For<TElement>(IEnumerable<TElement> elements, OWNameCase nameCase, Func<TElement, string> idOf,
            Func<TElement, IEnumerable<object>> names)
            => new OWNameFinder<TElement>(elements, nameCase, idOf, names);

        private static string JsonId(JToken element)
            => element is JObject o && o["id"] is JValue v && v.Type == JTokenType.String ? (string)v.Value : null;

        private static IEnumerable<object> JsonName(JToken element) => new object[] { element is JObject o ? o["name"] : null };
    }
}
