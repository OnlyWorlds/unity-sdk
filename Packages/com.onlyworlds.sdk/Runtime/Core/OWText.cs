// The profiles' one comparison and one order, public: names compared and sorted as every profile reader here already
// compares and sorts them. Parity with @onlyworlds/sdk 4.180.0 (trimAndFold, byCodePoint).

using System;
using System.Collections.Generic;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A text trimmed and without regard to case, and two texts in code point order, as the extension profiles define
    /// them once. The profile readers of this SDK (the room, names, lens and charter readers among them) match and
    /// order by these.
    /// </summary>
    /// <remarks>
    /// Names as the profiles order them: by <c>TrimAndFold(name)</c> in <see cref="ByCodePoint"/> order, ties by
    /// <c>id</c> in the same order.
    /// </remarks>
    public static class OWText
    {
        /// <summary>
        /// <paramref name="text"/> trimmed, then folded. It trims Unicode's White_Space set at both ends (U+0085 and
        /// the ideographic space go, a byte order mark and a zero width space stay) and touches nothing inside, then
        /// applies simple case folding by Unicode 15.1's table (the long s folds to s, the sharp s stays). Neither
        /// <c>string.Trim()</c> nor <c>ToLowerInvariant()</c> is this: their sets are the runtime's own.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public static string TrimAndFold(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text), "TrimAndFold takes a string, not null");
            return OWProfileText.Fold(text);
        }

        /// <summary>
        /// A comparer that orders strings by code point: never by UTF-16 code unit, as
        /// <see cref="StringComparer.Ordinal"/> does (which puts a letter beyond U+FFFF before U+FF5A), and never by a
        /// culture's collation. A surrogate pair is one code point; a lone surrogate keeps its own value; when one
        /// string is a prefix of the other the shorter sorts first; null sorts before any string.
        /// </summary>
        public static IComparer<string> ByCodePoint => OWCodePointComparer.Instance;

        /// <summary><see cref="ByCodePoint"/> as a comparison: negative, zero or positive as <paramref name="a"/> sorts before, with or after <paramref name="b"/>.</summary>
        public static int CompareByCodePoint(string a, string b) => OWCodePointComparer.Instance.Compare(a, b);
    }
}
