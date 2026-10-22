// The profiles' one comparison: "trimmed and without regard to case" (schema-dist profiles/room.md, dist.47, defined
// once for every profile that folds like the room profile's pairs).
//
// Ported from @onlyworlds/sdk src/room.ts (names-profile, commit 7447831): trimWhiteSpace and fold. The room profile's
// own matcher (isRoomElement) is not ported; the names profile is the first reader here that needs the fold.

namespace OnlyWorlds.Sdk
{
    /// <summary>Trimmed, and trimmed then folded, as the extension profiles define them.</summary>
    internal static class OWProfileText
    {
        /// <summary>
        /// Leading and trailing whitespace removed, nothing inside touched. Whitespace is Unicode's White_Space
        /// property exactly (PropList.txt, 25 code points), so a byte order mark (U+FEFF) and a zero width space
        /// (U+200B) stay, as the TypeScript SDK and the Python package trim. Not <c>string.Trim()</c>: its set is
        /// the runtime's own.
        /// </summary>
        internal static string TrimWhiteSpace(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var start = 0;
            var end = text.Length;
            while (start < end && IsWhiteSpace(text[start])) start++;
            while (end > start && IsWhiteSpace(text[end - 1])) end--;
            return start == 0 && end == text.Length ? text : text.Substring(start, end - start);
        }

        /// <summary>
        /// Trimmed, then simple-case-folded (<see cref="OWCaseFold"/>, the table the <c>.owworld</c> reader uses):
        /// two values are equal without regard to case when their folds are equal.
        /// </summary>
        internal static string Fold(string text) => OWCaseFold.Fold(TrimWhiteSpace(text));

        /// <summary>
        /// Unicode White_Space: U+0009 to U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000 to U+200A, U+2028, U+2029,
        /// U+202F, U+205F, U+3000. All in the Basic Multilingual Plane, so one UTF-16 code unit each.
        /// </summary>
        internal static bool IsWhiteSpace(char c)
        {
            switch ((int)c)
            {
                case 0x0009: case 0x000A: case 0x000B: case 0x000C: case 0x000D:
                case 0x0020: case 0x0085: case 0x00A0: case 0x1680:
                case 0x2028: case 0x2029: case 0x202F: case 0x205F: case 0x3000:
                    return true;
                default:
                    return c >= 0x2000 && c <= 0x200A;
            }
        }
    }
}
