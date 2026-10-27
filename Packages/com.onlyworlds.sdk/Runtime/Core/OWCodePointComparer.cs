using System.Collections.Generic;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Orders strings by Unicode code point: the order the folder format's id tie-breaks and the lens profile's id tie
    /// are read in, and the order the reference readers sort in (the TypeScript SDK's and the Python package's).
    /// </summary>
    /// <remarks>
    /// It differs from <see cref="System.StringComparer.Ordinal"/>, which compares UTF-16 code units, only for strings
    /// with a character outside the Basic Multilingual Plane: a surrogate pair (U+D800-U+DFFF as code units) sorts above
    /// U+FFFF by code point, not below U+E000. The hex ids a world holds are ASCII, where the two orders agree.
    /// </remarks>
    internal sealed class OWCodePointComparer : IComparer<string>
    {
        /// <summary>The one instance; the comparer holds no state.</summary>
        internal static readonly OWCodePointComparer Instance = new OWCodePointComparer();

        private OWCodePointComparer()
        {
        }

        /// <summary>
        /// Compares two strings by their Unicode scalar values, code point by code point, following
        /// <see cref="OWCaseFold"/>'s walk: a surrogate pair is one code point, and a lone surrogate keeps its own
        /// UTF-16 value (it never throws). When one string is a prefix of the other, the shorter sorts first.
        /// </summary>
        public int Compare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                var ca = CodePointAt(a, i, out var wa);
                var cb = CodePointAt(b, j, out var wb);
                if (ca != cb) return ca < cb ? -1 : 1;
                i += wa;
                j += wb;
            }

            // One is a prefix of the other (or they are equal): the shorter sorts first.
            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>The code point at <paramref name="index"/> and its width in chars (1, or 2 for a pair); a lone surrogate is its own char value.</summary>
        private static int CodePointAt(string text, int index, out int width)
        {
            width = 1;
            int codePoint = text[index];
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                codePoint = char.ConvertToUtf32(text[index], text[index + 1]);
                width = 2;
            }

            return codePoint;
        }
    }
}
