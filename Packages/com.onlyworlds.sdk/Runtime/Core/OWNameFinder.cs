// Element names found in prose: a world's names read once, then found in any text. Parity with @onlyworlds/sdk
// 4.198.0 (src/name-finder.ts, nameFinder) and the Python package's name_finder: a rule of the packages, not of the
// standard, held by one file of cases (Tests/Editor/Fixtures/name-finder/name-finder.cases.json, version 2, the same
// bytes as the TypeScript SDK's test/fixtures/name-finder.cases.json).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
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
    /// A name two or more elements hold (<see cref="OWNameFinder{T}.Shared"/>): <see cref="OWNameFinder{T}.Find"/>
    /// answers nothing for it, and this says that it is there.
    /// </summary>
    /// <typeparam name="T">The kind of element the finder was made from.</typeparam>
    public sealed class OWNameShared<T>
    {
        internal OWNameShared(string name, IReadOnlyList<T> elements)
        {
            Name = name;
            Elements = elements;
        }

        /// <summary>The name as the finder compares it: trimmed of White_Space, and folded when the finder is <see cref="OWNameCase.Folded"/>.</summary>
        public string Name { get; }

        /// <summary>The elements that hold it, two at least, in the order they were given: the same objects the finder was given. Read-only.</summary>
        public IReadOnlyList<T> Elements { get; }
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

        /// <summary>
        /// The mark the matcher reads between two characters wherever a name may end: before a character that is not a
        /// word character, and at the end of the text. No code point is negative.
        /// </summary>
        private const int EndMark = -1;

        /// <summary>One name, as compared, and every element that holds it.</summary>
        private sealed class Hit
        {
            internal Hit(int length, string name, string id, T element)
            {
                Length = length;
                Name = name;
                FirstId = id;
                Elements.Add(element);
            }

            /// <summary>The name's length in code points.</summary>
            internal readonly int Length;

            internal readonly string Name;
            internal readonly string FirstId;
            internal readonly List<T> Elements = new List<T>(1);

            /// <summary>The ids of <see cref="Elements"/>, kept once a second element holds the name.</summary>
            internal HashSet<string> Ids;
        }

        /// <summary>
        /// A state of the matcher. The names are held backwards, each from its last character to its first, with the
        /// end mark before every character that is not a word character and once at the name's end: so a text read
        /// from its end towards its start, with the same mark laid wherever a name may end, passes each character once
        /// (Aho and Corasick's failure links), and the state after a character says which whole names start there.
        /// </summary>
        private sealed class State
        {
            internal readonly Dictionary<int, State> Next = new Dictionary<int, State>();
            internal State Fail;

            /// <summary>The name that is this state, read backwards.</summary>
            internal Hit Hit;

            /// <summary>The longest name that ends, read backwards, at this state: <see cref="Hit"/>, or the failure state's.</summary>
            internal Hit Longest;
        }

        private readonly State _root = new State();
        private readonly bool _folded;
        private readonly int _names;

        internal OWNameFinder(IEnumerable<T> elements, OWNameCase nameCase, Func<T, string> idOf, Func<T, IEnumerable<object>> names)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements), "OWNameFinder: elements is a list of elements with an id and a name");
            if (nameCase != OWNameCase.Exact && nameCase != OWNameCase.Folded)
                throw new ArgumentException($"OWNameFinder: nameCase is OWNameCase.Exact or OWNameCase.Folded, and must be given; got {(int)nameCase}.", nameof(nameCase));
            if (idOf == null) throw new ArgumentNullException(nameof(idOf), "OWNameFinder: idOf is a function from an element to its id");
            if (names == null) throw new ArgumentNullException(nameof(names), "OWNameFinder: names is a function from an element to its names");
            _folded = nameCase == OWNameCase.Folded;

            var hits = new List<Hit>();
            var points = new List<int>();
            var tokens = new List<int>();
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

                    // The name backwards: the end mark, then each character from the last to the first, and the mark
                    // again after (so, in the text, before) every character but the first that is not a word character.
                    tokens.Clear();
                    tokens.Add(EndMark);
                    for (var k = points.Count - 1; k >= 0; k--)
                    {
                        tokens.Add(points[k]);
                        if (k > 0 && !OWNameFinder.IsWord(points[k])) tokens.Add(EndMark);
                    }

                    var state = _root;
                    foreach (var token in tokens)
                    {
                        if (!state.Next.TryGetValue(token, out var child))
                        {
                            child = new State();
                            state.Next.Add(token, child);
                        }

                        state = child;
                    }

                    var hit = state.Hit;
                    if (hit == null)
                    {
                        state.Hit = new Hit(points.Count, Spelled(points), id, element);
                        hits.Add(state.Hit);
                    }
                    else if (hit.Ids == null)
                    {
                        if (!string.Equals(hit.FirstId, id, StringComparison.Ordinal))
                        {
                            hit.Ids = new HashSet<string>(StringComparer.Ordinal) { hit.FirstId, id };
                            hit.Elements.Add(element);
                        }
                    }
                    else if (hit.Ids.Add(id))
                    {
                        hit.Elements.Add(element);
                    }
                }
            }

            _names = hits.Count;

            // The failure links, level by level: where the matcher goes on when the text leaves the name it was reading.
            var level = new List<State>();
            foreach (var child in _root.Next.Values)
            {
                child.Fail = _root;
                child.Longest = child.Hit;
                level.Add(child);
            }

            while (level.Count > 0)
            {
                var deeper = new List<State>();
                foreach (var state in level)
                {
                    foreach (var pair in state.Next)
                    {
                        var child = pair.Value;
                        var back = state.Fail;
                        State to = null;
                        while (back != null && !back.Next.TryGetValue(pair.Key, out to)) back = back.Fail;
                        child.Fail = back == null ? _root : to;
                        child.Longest = child.Hit ?? child.Fail.Longest;
                        deeper.Add(child);
                    }
                }

                level = deeper;
            }

            var shared = new List<OWNameShared<T>>();
            foreach (var hit in hits)
            {
                if (hit.Elements.Count > 1)
                    shared.Add(new OWNameShared<T>(hit.Name, new ReadOnlyCollection<T>(hit.Elements.ToArray())));
            }

            Shared = new ReadOnlyCollection<OWNameShared<T>>(shared);
        }

        /// <summary>
        /// Every name two or more elements hold, in the order each name was first met, each as an
        /// <see cref="OWNameShared{T}"/> (the name as compared, and the elements that hold it in the order given).
        /// <see cref="Find"/> never answers one of these, so a tool reads this list to tell a writer why a name stays
        /// plain, or to test that no two elements of a world share one. A name too short to be looked for is not on
        /// it. The list and its entries are read-only, and made once, with the finder.
        /// </summary>
        public IReadOnlyList<OWNameShared<T>> Shared { get; }

        /// <summary>
        /// Every name of the finder's elements in <paramref name="text"/>, in order, none overlapping, each as an
        /// <see cref="OWNameFound{T}"/> (start, end, and the element that was passed in). A new list on each call.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>A whole name only: the code point on each side of it is not a letter (Unicode category L), a mark (M),
        /// a decimal digit (Nd) or an underscore, in any script. So <c>Kira's</c> and <c>Kira-led</c> hold the name
        /// <c>Kira</c>, and <c>Kiran</c> does not. Outside ASCII the category is the runtime's own
        /// (<see cref="CharUnicodeInfo.GetUnicodeCategory(char)"/>), as the TypeScript SDK reads JavaScript's and the
        /// Python package Python's, so a letter assigned in a newer Unicode than the runtime knows is no word character
        /// here. Unity 6's Mono reads Unicode 11.0's categories: beside a letter added since (one of CJK Extension G,
        /// say), a name is whole here and inside a word for the TypeScript SDK on a current Node. A capital assigned
        /// since then whose fold is an older letter (U+A7C4 to U+A7C6) is no word character here while its fold is
        /// one, so a folded name holding one inside it is found where the text writes the fold, not the capital. The
        /// same read decides where <see cref="OWAddress.FindLinks"/> takes a start glued to a word.</item>
        /// <item>A name is trimmed of Unicode's White_Space and is three code points at least; a shorter one is never
        /// looked for.</item>
        /// <item>Where several names start at one place, the longest whole one is taken (<c>Iron Gate</c> over
        /// <c>Iron</c>), and the search goes on after it.</item>
        /// <item>A name two elements share (by id) is never answered, and nothing inside it is: the text stays plain
        /// there (<see cref="Shared"/> lists it). One element listed twice, or found by two of its own names, is one
        /// element.</item>
        /// <item>Nothing is read inside an address (what <see cref="OWAddress.FindLinks"/> finds), a Markdown link
        /// <c>[label](target)</c> (a <c>[</c>, a label up to the first <c>]</c>, a <c>(</c> right after it, a target
        /// up to the first <c>)</c>), a bare web address (<c>http://</c> or <c>https://</c> in any ASCII case, to the
        /// next white space or <c>&lt;</c>) or an autolink (<c>&lt;scheme:…&gt;</c>), and no name is read across the
        /// edge of one. <see cref="Find(string, bool)"/> with markdown true also passes over Markdown's code, HTML and
        /// reference definitions.</item>
        /// <item>Text is compared as written: no Unicode normalisation (a letter and a combining mark are not the
        /// one-character letter), and a space in a name matches that one space.</item>
        /// </list>
        /// The work for one text grows with its length alone, whatever the names and however many: the text is read
        /// once, from its end to its start, and no character is read again for another name. Reading the names, when
        /// the finder is made, grows with their total length.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public IReadOnlyList<OWNameFound<T>> Find(string text) => Find(text, false);

        /// <summary>
        /// <see cref="Find(string)"/>, told whether the text is Markdown. With <paramref name="markdown"/> true, nothing
        /// is read inside an inline code span, a fenced code block, an HTML tag or comment, or a reference definition's
        /// line either; false answers as <see cref="Find(string)"/>, so a rich-text node's literal backticks stay prose.
        /// An indented code block, a fence inside a quote or a list, and a definition's title on the next line are read
        /// as prose. The TypeScript SDK's <c>find(text, { markdown: true })</c> (4.208.0).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public IReadOnlyList<OWNameFound<T>> Find(string text, bool markdown)
        {
            if (text == null) throw new ArgumentNullException(nameof(text), "OWNameFinder: text must be a string");
            var found = new List<OWNameFound<T>>();
            if (_names == 0) return found;
            var spans = PassedOver(text, markdown);
            var length = text.Length;

            // Where a whole name starts and the longest one that does, as the backward pass meets them: last place first.
            var starts = new List<int>();
            var longest = new List<Hit>();
            // Whether every character from that start to the stretch's end is one string index wide.
            var narrow = new List<bool>();
            var from = 0;
            for (var span = 0; span <= spans.Count; span++)
            {
                var to = span < spans.Count ? spans[span].start : length;
                if (to > from)
                {
                    starts.Clear();
                    longest.Clear();
                    narrow.Clear();
                    var pairs = false;
                    var state = _root;
                    var k = to;
                    // Whether a name may end at k: the text ends there, or the character at k is not a word character.
                    var mayEnd = k >= length || !OWNameFinder.IsWord(CodePointAt(text, k));
                    while (k > from)
                    {
                        if (mayEnd) state = Step(state, EndMark);
                        int c = text[k - 1];
                        k--;
                        if (char.IsLowSurrogate((char)c) && k > from && char.IsHighSurrogate(text[k - 1]))
                        {
                            k--;
                            c = char.ConvertToUtf32(text[k], (char)c);
                            pairs = true;
                        }

                        mayEnd = !OWNameFinder.IsWord(c);
                        // From the root only the end mark leads anywhere: every name starts, read backwards, with it.
                        if (state != _root) state = Step(state, _folded ? OWCaseFold.Fold(c) : c);
                        if (state.Longest != null && !WordBefore(text, k))
                        {
                            starts.Add(k);
                            longest.Add(state.Longest);
                            narrow.Add(!pairs);
                        }
                    }

                    // Forwards over what was met: the first place a name starts takes its longest name, and the next
                    // name starts at or after that one's end.
                    var free = from;
                    for (var r = starts.Count - 1; r >= 0; r--)
                    {
                        var start = starts[r];
                        if (start < free) continue;
                        var hit = longest[r];
                        var end = start;
                        if (narrow[r]) end += hit.Length;
                        else
                            for (var n = hit.Length; n > 0; n--)
                                end += Width(text, end);
                        if (hit.Elements.Count == 1) found.Add(new OWNameFound<T>(start, end, hit.Elements[0]));
                        free = end;
                    }
                }

                if (span < spans.Count) from = spans[span].end;
            }

            return found;
        }

        /// <summary>The state after <paramref name="token"/>: the first state on the failure chain that has it, else the root.</summary>
        private State Step(State from, int token)
        {
            for (var state = from; state != null; state = state.Fail)
                if (state.Next.TryGetValue(token, out var to)) return to;
            return _root;
        }

        /// <summary>A name's code points as a string (a lone surrogate kept as its own unit).</summary>
        private static string Spelled(List<int> points)
        {
            var name = new StringBuilder(points.Count);
            foreach (var p in points)
            {
                if (p < 0x10000) name.Append((char)p);
                else name.Append(char.ConvertFromUtf32(p));
            }

            return name.ToString();
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
            if (i >= 2 && char.IsLowSurrogate(text[i - 1]) && char.IsHighSurrogate(text[i - 2])) return OWNameFinder.IsWord(CodePointAt(text, i - 2));
            return OWNameFinder.IsWord(text[i - 1]);
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

        /// <summary>Unicode's White_Space code points, all in the BMP: the end of a bare web address and of an autolink.</summary>
        private static bool IsWhiteSpace(int c) =>
            (c >= 0x09 && c <= 0x0d) || c == 0x20 || c == 0x85 || c == 0xa0 || c == 0x1680
            || (c >= 0x2000 && c <= 0x200a) || c == 0x2028 || c == 0x2029 || c == 0x202f || c == 0x205f || c == 0x3000;

        private static bool IsAsciiLetter(int c) => (c >= 0x41 && c <= 0x5a) || (c >= 0x61 && c <= 0x7a);

        /// <summary>The UTF-16 unit at <paramref name="i"/>, or -1 past the text's end.</summary>
        private static int At(string text, int i) => i >= 0 && i < text.Length ? text[i] : -1;

        private static int IndexFrom(string text, char c, int from) => from >= text.Length ? -1 : text.IndexOf(c, from);

        private static int IndexFrom(string text, string s, int from) =>
            from > text.Length ? -1 : text.IndexOf(s, from, StringComparison.Ordinal);

        /// <summary>
        /// Every bare web address and every autolink, in any text. A bare address starts at <c>http://</c> or
        /// <c>https://</c> (the scheme in any ASCII case) and runs to just before the first white space or <c>&lt;</c>.
        /// An autolink is <c>&lt;</c>, a scheme (an ASCII letter, then 1 to 31 of letters, digits, <c>+</c>, <c>.</c> or
        /// <c>-</c>), <c>:</c>, then anything but white space, a control character, <c>&lt;</c> or <c>&gt;</c>, up to the
        /// closing <c>&gt;</c>. The TypeScript SDK's webAddresses (4.208.0).
        /// </summary>
        private static List<(int start, int end)> WebAddresses(string text)
        {
            var spans = new List<(int start, int end)>();
            var i0 = 0;
            while (i0 < text.Length)
            {
                var at = SchemeAt(text, i0);
                if (at == -1) break;
                var end = at + (At(text, at + 4) == 's' || At(text, at + 4) == 'S' ? 8 : 7);
                while (end < text.Length && !IsWhiteSpace(text[end]) && text[end] != '<') end++;
                spans.Add((at, end));
                i0 = end;
            }

            var open = text.IndexOf('<');
            while (open != -1)
            {
                var i = open + 1;
                if (IsAsciiLetter(At(text, i)))
                {
                    var n = 1;
                    i++;
                    while (i < text.Length && n < 32 && IsSchemeChar(text[i])) { i++; n++; }
                    if (n >= 2 && At(text, i) == ':')
                    {
                        i++;
                        var c = At(text, i);
                        while (i < text.Length && c != '>' && c != '<' && c >= 0x20 && c != 0x7f && !IsWhiteSpace(c)) c = At(text, ++i);
                        if (i < text.Length && c == '>')
                        {
                            spans.Add((open, i + 1));
                            open = IndexFrom(text, '<', i + 1);
                            continue;
                        }
                    }
                }

                open = IndexFrom(text, '<', open + 1);
            }

            return spans;
        }

        private static bool IsSchemeChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '.' || c == '-';

        /// <summary>The first place at or after <paramref name="from"/> where <c>http://</c> or <c>https://</c> starts,
        /// the letters in ASCII case alone; -1 when none does.</summary>
        private static int SchemeAt(string text, int from)
        {
            for (var i = from; i + 7 <= text.Length; i++)
            {
                if ((text[i] | 0x20) != 'h' || (text[i + 1] | 0x20) != 't' || (text[i + 2] | 0x20) != 't' || (text[i + 3] | 0x20) != 'p') continue;
                var j = i + 4;
                if ((text[j] | 0x20) == 's') j++;
                if (j + 3 <= text.Length && text[j] == ':' && text[j + 1] == '/' && text[j + 2] == '/') return i;
            }

            return -1;
        }

        /// <summary>A line's fence, after at most three spaces: a run of three or more backticks or tildes, or null.</summary>
        private static (int ch, int length, int after)? FenceOf(string text, int start, int end)
        {
            var i = start;
            while (i < end && i - start < 3 && text[i] == ' ') i++;
            var ch = At(text, i);
            if (ch != '`' && ch != '~') return null;
            var j = i;
            while (j < end && text[j] == ch) j++;
            return j - i >= 3 ? (ch, j - i, j) : ((int, int, int)?)null;
        }

        /// <summary>
        /// The Markdown stretches no name is read in (markdown true). Fenced code blocks first: a line whose fence opens
        /// one (a backtick fence holds no backtick in the rest of its line) runs to a later line holding only a fence of
        /// the same character, at least as long, and spaces or tabs after it, or to the end of the text. Outside them: an
        /// inline code span (a run of backticks to the next run of exactly as many; a run with none is prose), an HTML
        /// comment (<c>&lt;!--</c> to the first <c>--&gt;</c>), an HTML tag (<c>&lt;</c>, an optional <c>/</c>, an ASCII
        /// letter, to the first <c>&gt;</c>), and a reference definition's line (after at most three spaces, <c>[</c>, a
        /// label, and the line's first <c>]</c> followed by <c>:</c>). The TypeScript SDK's markdownStretches (4.208.0).
        /// </summary>
        private static List<(int start, int end)> MarkdownStretches(string text)
        {
            var spans = new List<(int start, int end)>();
            var lines = new List<(int start, int end)>();
            for (var start = 0; ;)
            {
                var nl = IndexFrom(text, '\n', start);
                if (nl == -1) { lines.Add((start, text.Length)); break; }
                lines.Add((start, nl));
                start = nl + 1;
            }

            var outside = new List<(int start, int end)>();
            var segStart = 0;
            var bracket = -2; // the next ] at or after the place last asked about, searched for once per place passed
            for (var k = 0; k < lines.Count; k++)
            {
                var (ls, le) = lines[k];
                var open = FenceOf(text, ls, le);
                var tick = open != null && open.Value.ch == '`' ? IndexFrom(text, '`', open.Value.after) : -1;
                if (open == null || (tick != -1 && tick < le))
                {
                    var i = ls;
                    while (i < le && i - ls < 3 && text[i] == ' ') i++;
                    if (At(text, i) == '[')
                    {
                        if (bracket != -1 && bracket < i + 1) bracket = IndexFrom(text, ']', i + 1);
                        var close = bracket;
                        if (close != -1 && close < le && close > i + 1 && At(text, close + 1) == ':') spans.Add((ls, le));
                    }

                    continue;
                }

                var m = k + 1;
                for (; m < lines.Count; m++)
                {
                    var (cs, ce) = lines[m];
                    var shut = FenceOf(text, cs, ce);
                    if (shut == null || shut.Value.ch != open.Value.ch || shut.Value.length < open.Value.length) continue;
                    var j = shut.Value.after;
                    while (j < ce && (text[j] == ' ' || text[j] == '\t' || text[j] == '\r')) j++;
                    if (j == ce) break;
                }

                var blockEnd = m < lines.Count ? lines[m].end : text.Length;
                if (ls > segStart) outside.Add((segStart, ls));
                spans.Add((ls, blockEnd));
                segStart = blockEnd;
                k = m;
            }

            if (segStart < text.Length) outside.Add((segStart, text.Length));
            foreach (var (s, e) in outside)
            {
                // Inline code spans: every maximal backtick run, then each opening run to the next run of its length.
                var runs = new List<(int at, int n)>();
                var t = IndexFrom(text, '`', s);
                while (t != -1 && t < e)
                {
                    var j = t;
                    while (j < e && text[j] == '`') j++;
                    runs.Add((t, j - t));
                    t = IndexFrom(text, '`', j);
                }

                var byLength = new Dictionary<int, List<int>>();
                for (var idx = 0; idx < runs.Count; idx++)
                {
                    if (!byLength.TryGetValue(runs[idx].n, out var l)) byLength[runs[idx].n] = l = new List<int>();
                    l.Add(idx);
                }

                var cursor = new Dictionary<int, int>();
                var r = 0;
                while (r < runs.Count)
                {
                    var (at, n) = runs[r];
                    var list = byLength[n];
                    cursor.TryGetValue(n, out var c);
                    while (c < list.Count && list[c] <= r) c++;
                    cursor[n] = c;
                    if (c < list.Count)
                    {
                        spans.Add((at, runs[list[c]].at + n));
                        r = list[c] + 1;
                    }
                    else
                    {
                        r++;
                    }
                }

                // HTML comments and tags.
                var gt = -2;
                var commentEnd = -2;
                var lt = IndexFrom(text, '<', s);
                while (lt != -1 && lt < e)
                {
                    if (string.CompareOrdinal(text, lt, "<!--", 0, 4) == 0 && lt + 4 <= text.Length)
                    {
                        if (commentEnd != -1 && commentEnd < lt + 4) commentEnd = IndexFrom(text, "-->", lt + 4);
                        if (commentEnd != -1 && commentEnd + 3 <= e)
                        {
                            spans.Add((lt, commentEnd + 3));
                            lt = IndexFrom(text, '<', commentEnd + 3);
                            continue;
                        }
                    }
                    else
                    {
                        var name = At(text, lt + 1) == '/' ? lt + 2 : lt + 1;
                        if (IsAsciiLetter(At(text, name)))
                        {
                            if (gt != -1 && gt < name) gt = IndexFrom(text, '>', name);
                            if (gt != -1 && gt < e)
                            {
                                spans.Add((lt, gt + 1));
                                lt = IndexFrom(text, '<', gt + 1);
                                continue;
                            }
                        }
                    }

                    lt = IndexFrom(text, '<', lt + 1);
                }
            }

            return spans;
        }

        /// <summary>
        /// The stretches no name is read in, in order, none overlapping: every address, every Markdown link, every bare
        /// web address and autolink, and with <paramref name="markdown"/> the Markdown stretches too.
        /// </summary>
        private static List<(int start, int end)> PassedOver(string text, bool markdown)
        {
            var spans = MarkdownLinks(text);
            foreach (var a in OWAddress.FindLinks(text)) spans.Add((a.Start, a.End));
            spans.AddRange(WebAddresses(text));
            if (markdown) spans.AddRange(MarkdownStretches(text));
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

        /// <summary>
        /// Whether a code point is a word character: an ASCII letter or digit or an underscore, or outside ASCII a
        /// letter, a mark or a decimal digit by the runtime's categories. A lone surrogate is none.
        /// </summary>
        internal static bool IsWord(int codePoint)
        {
            if (codePoint < 0x80)
                return codePoint >= '0' && codePoint <= '9' || codePoint >= 'A' && codePoint <= 'Z' || codePoint >= 'a' && codePoint <= 'z' || codePoint == '_';
            var category = codePoint < 0x10000
                ? CharUnicodeInfo.GetUnicodeCategory((char)codePoint)
                : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0);
            switch (category)
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
    }
}
