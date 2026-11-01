using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Doubles to JSON text and back, exactly, on every runtime: written as ECMAScript's <c>Number::toString</c> writes
    /// them (what <c>JSON.stringify</c> sends), read to the nearest double, ties to even. Computed in integer
    /// arithmetic, never by the runtime's own formatting or parsing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why not the runtime: under Unity's Mono, <c>double.ToString("R")</c> (which Newtonsoft writes with) and
    /// <c>double.Parse</c> (which Newtonsoft reads with) agree with each other and with no exact reader or writer.
    /// Measured over 19,990 doubles: <c>"R"</c> wrote 15 strings that an exact reader reads as another double
    /// (<c>51.6721044378159</c> among them), and the parse misread 63 of the shortest strings an exact writer sends
    /// (<c>263.2953629956518</c> among them). A round trip through the runtime alone cannot see either.
    /// </para>
    /// <para>
    /// The writer's digits were the folder writer's (spec §5) before they were shared: the folder and the wire now
    /// spell a number one way.
    /// </para>
    /// </remarks>
    internal static class OWJsNumber
    {
        // -- Write -------------------------------------------------------------

        /// <summary>A finite double as <c>JSON.stringify</c> writes it (both zeros as <c>0</c>).</summary>
        public static string Format(double value)
        {
            // Covers -0 too: ECMAScript writes both zeros as "0".
            if (value == 0d) return "0";

            // The common case, and exact: every integral double below 2^53 is a whole long.
            if (Math.Abs(value) < 9007199254740992d && value == Math.Floor(value))
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            return Layout(ShortestDigits(value));
        }

        /// <summary>
        /// A finite float, by its own shortest digits (<c>0.1f</c> is <c>0.1</c>, not the double it widens to,
        /// <c>0.10000000149011612</c>), laid out by the same rule.
        /// </summary>
        public static string Format(float value) => value == 0f ? "0" : Layout(ShortestDigits(value));

        private static (string Digits, int Exponent, bool Negative) ShortestDigits(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var biased = (int)((bits >> 52) & 0x7FF);
            var fraction = bits & 0xFFFFFFFFFFFFFL;

            return biased == 0
                ? Shortest(fraction, -1074, false, 17, bits < 0)
                : Shortest(fraction | (1L << 52), biased - 1075, fraction == 0 && biased > 1, 17, bits < 0);
        }

        private static (string Digits, int Exponent, bool Negative) ShortestDigits(float value)
        {
            var bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            var biased = (bits >> 23) & 0xFF;
            var fraction = bits & 0x7FFFFF;

            return biased == 0
                ? Shortest(fraction, -149, false, 9, bits < 0)
                : Shortest(fraction | (1L << 23), biased - 150, fraction == 0 && biased > 1, 9, bits < 0);
        }

        /// <summary>
        /// The fewest decimal digits that read back as <c>m * 2^e</c>, and the closest such
        /// digits when several qualify -- exact, in integer arithmetic.
        /// </summary>
        /// <param name="m">The binary significand, positive.</param>
        /// <param name="e">Its binary exponent.</param>
        /// <param name="lowerGapHalved">
        /// True at a power of two (other than the smallest normal), where the next value DOWN is
        /// half as far away as the next value up, so the reading-back interval is lopsided.
        /// </param>
        /// <param name="maxDigits">17 for a double, 9 for a float: always enough to round-trip.</param>
        /// <param name="negative">The sign, carried through.</param>
        /// <remarks>
        /// Not <c>ToString("E" + p)</c> plus a parse: measured against Python's shortest repr
        /// over 45,024 doubles, Unity's Mono misrounds both its formatting and its parsing at
        /// extreme exponents (362 wrong spellings, e.g. <c>7.364551133699083e-269</c> for the
        /// correct <c>...082</c>). A candidate reads back as this value exactly when it lies inside
        /// the half-gaps to the neighbouring values, inclusive when <paramref name="m"/> is even
        /// (a tie parses to the even significand), and that is decided here without any float.
        /// </remarks>
        private static (string Digits, int Exponent, bool Negative) Shortest(
            long m, int e, bool lowerGapHalved, int maxDigits, bool negative)
        {
            // Everything in units of 2^(e-2): the value is 4m, the half-gap up is 2, and the
            // half-gap down is 2, or 1 at a power of two.
            var t = e - 2;
            var x = new BigInteger(m) * 4;
            BigInteger gapUp = 2, gapDown = lowerGapHalved ? 1 : 2;
            var inclusive = (m & 1) == 0;

            // The value as the fraction xNum / xDen.
            var xNum = t >= 0 ? x << t : x;
            var xDen = t >= 0 ? BigInteger.One : BigInteger.One << -t;

            // n such that 10^(n-1) <= value < 10^n: estimated, then made exact.
            var n = (int)Math.Floor(Math.Log10(m) + e * Math.Log10(2)) + 1;
            while (Compare(xNum, xDen, n - 1) < 0) n--;
            while (Compare(xNum, xDen, n) >= 0) n++;

            // The closest k-digit decimal that reads back as the value, or null when none does.
            BigInteger? Candidate(int k)
            {
                var q = n - k;
                var scale = PowerOfTen(Math.Abs(q));

                // The value over 10^q, as yNum / yDen, and the two k-digit candidates around it.
                var yNum = q >= 0 ? xNum : xNum * scale;
                var yDen = q >= 0 ? xDen * scale : xDen;
                var below = BigInteger.DivRem(yNum, yDen, out var remainder);

                // One denominator for the comparison: value, candidate and gap unit, all times C.
                var valueC = q >= 0 ? xNum : xNum * scale;
                var unitC = (t >= 0 ? BigInteger.One << t : BigInteger.One) * (q >= 0 ? BigInteger.One : scale);

                BigInteger? best = null;
                var bestDistance = BigInteger.Zero;

                foreach (var s in remainder.IsZero ? new[] { below } : new[] { below, below + 1 })
                {
                    var candidateC = s * (q >= 0 ? scale : BigInteger.One) * xDen;
                    var diff = candidateC - valueC;
                    var limit = (diff.Sign < 0 ? gapDown : gapUp) * unitC;
                    var distance = BigInteger.Abs(diff);
                    if (inclusive ? distance > limit : distance >= limit) continue;

                    // Closest wins; on an exact tie ECMAScript takes the even candidate.
                    if (best == null || distance < bestDistance || (distance == bestDistance && s.IsEven))
                    {
                        best = s;
                        bestDistance = distance;
                    }
                }

                return best;
            }

            // The fewest digits, by bisection: whether some k-digit decimal reads back only grows with k (a k-digit
            // one, times ten, lies between the value and the (k+1)-digit decimal on its side, so that one reads back
            // too), and maxDigits always does. Five candidates instead of up to seventeen.
            int lo = 1, hi = maxDigits, foundAt = 0;
            BigInteger? found = null;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                var c = Candidate(mid);
                if (c == null)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                    found = c;
                    foundAt = mid;
                }
            }

            // lo is the fewest; its candidate is already in hand unless no smaller count was ever tried there.
            var bestDigits = foundAt == lo ? found : Candidate(lo);
            if (bestDigits == null)
            {
                throw new InvalidOperationException(
                    $"No {maxDigits}-digit decimal reads back as {m} x 2^{e}; the search is wrong, not the value.");
            }

            // A round-up can carry into a new digit (9.99 -> 10); the length says where.
            var text = bestDigits.Value.ToString(CultureInfo.InvariantCulture);
            return (text.TrimEnd('0'), n - lo + text.Length, negative);
        }

        /// <summary>Sign of <c>num/den - 10^p</c>.</summary>
        private static int Compare(BigInteger num, BigInteger den, int p)
        {
            var power = PowerOfTen(Math.Abs(p));
            return p >= 0 ? num.CompareTo(power * den) : (num * power).CompareTo(den);
        }

        /// <summary>10^p, from a table built once for every power a double or its reading needs.</summary>
        private static BigInteger PowerOfTen(int p) => p < PowersOfTen.Table.Length ? PowersOfTen.Table[p] : BigInteger.Pow(10, p);

        // A nested type, so the table is built on first use, once, by the runtime's type initialisation (thread-safe).
        private static class PowersOfTen
        {
            public static readonly BigInteger[] Table = Build();

            private static BigInteger[] Build()
            {
                var table = new BigInteger[420];
                table[0] = BigInteger.One;
                for (var i = 1; i < table.Length; i++) table[i] = table[i - 1] * 10;
                return table;
            }
        }

        /// <summary>ECMAScript <c>Number::toString</c>, steps 6 to 10, for a finite non-zero value.</summary>
        private static string Layout((string Digits, int Exponent, bool Negative) number)
        {
            var (digits, n, negative) = number;
            var k = digits.Length;
            string body;

            if (k <= n && n <= 21)
            {
                body = digits + new string('0', n - k);
            }
            else if (0 < n && n <= 21)
            {
                body = digits.Substring(0, n) + "." + digits.Substring(n);
            }
            else if (-6 < n && n <= 0)
            {
                body = "0." + new string('0', -n) + digits;
            }
            else
            {
                var e = n - 1;
                body = digits.Substring(0, 1)
                    + (k > 1 ? "." + digits.Substring(1) : string.Empty)
                    + "e" + (e >= 0 ? "+" : "-")
                    + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
            }

            return negative ? "-" + body : body;
        }

        // -- Read --------------------------------------------------------------

        /// <summary>
        /// A JSON number (<c>-?digits(.digits)?([eE][+-]?digits)?</c>, the text from <paramref name="start"/> for
        /// <paramref name="length"/> characters) to the nearest double, ties to even, exactly. False for anything that
        /// is not a JSON number, and for a magnitude past the largest double (JSON has no infinity).
        /// </summary>
        public static bool TryParse(string text, int start, int length, out double value)
        {
            value = 0;
            int i = start, n = start + length;
            var negative = false;
            if (i < n && text[i] == '-') { negative = true; i++; }

            // The digits as one integer m, and the power of ten they are scaled by.
            var digitsStart = i;
            while (i < n && text[i] >= '0' && text[i] <= '9') i++;
            var intDigits = i - digitsStart;
            if (intDigits == 0) return false;
            if (intDigits > 1 && text[digitsStart] == '0') return false; // JSON has no leading zeros

            var fracStart = -1;
            var fracDigits = 0;
            if (i < n && text[i] == '.')
            {
                i++;
                fracStart = i;
                while (i < n && text[i] >= '0' && text[i] <= '9') i++;
                fracDigits = i - fracStart;
                if (fracDigits == 0) return false;
            }

            long exponent = 0;
            if (i < n && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                var expNegative = false;
                if (i < n && (text[i] == '+' || text[i] == '-')) { expNegative = text[i] == '-'; i++; }
                var expStart = i;
                while (i < n && text[i] >= '0' && text[i] <= '9')
                {
                    if (exponent < 100000) exponent = exponent * 10 + (text[i] - '0');
                    i++;
                }

                if (i == expStart) return false;
                if (expNegative) exponent = -exponent;
            }

            if (i != n) return false;

            var m = BigInteger.Zero;
            var significant = 0; // digits from the first non-zero one on
            for (var j = digitsStart; j < digitsStart + intDigits; j++) Accumulate(text[j], ref m, ref significant);
            for (var j = 0; j < fracDigits; j++) Accumulate(text[fracStart + j], ref m, ref significant);
            if (m.IsZero)
            {
                value = negative ? -0.0 : 0.0;
                return true;
            }

            var scale = exponent - fracDigits;

            // Past these the answer is decided: 10^(magnitude-1) <= value < 10^magnitude.
            var magnitude = significant + scale;
            if (magnitude < -400)
            {
                value = negative ? -0.0 : 0.0;
                return true;
            }

            if (magnitude > 400) return false;

            BigInteger num = m, den = BigInteger.One;
            if (scale >= 0) num *= PowerOfTen((int)scale);
            else den = PowerOfTen((int)-scale);

            // value = num / den; find b with 2^52 <= value / 2^b < 2^53, then no lower than the subnormals' -1074.
            var b = BitLength(num) - BitLength(den) - 53;
            while (true)
            {
                if (ComparePow2(num, den, b + 53) >= 0) { b++; continue; }
                if (ComparePow2(num, den, b + 52) < 0) { b--; continue; }
                break;
            }

            if (b < -1074) b = -1074;

            BigInteger sn = num, sd = den;
            if (b >= 0) sd <<= b;
            else sn <<= -b;
            var q = BigInteger.DivRem(sn, sd, out var rem);
            var c = (rem * 2).CompareTo(sd);
            if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 53)) { q >>= 1; b++; }
            if (b + 52 > 1023) return false;

            // At b = -1074 the bits are q itself (q = 2^52 is the smallest normal); above, the biased exponent is b + 1075.
            var bits = b == -1074 ? (long)q : ((long)(b + 1075) << 52) | ((long)q & 0xFFFFFFFFFFFFFL);
            if (negative) bits |= long.MinValue;
            value = BitConverter.Int64BitsToDouble(bits);
            return true;
        }

        private static void Accumulate(char digit, ref BigInteger m, ref int significant)
        {
            m = m * 10 + (digit - '0');
            if (significant > 0 || digit != '0') significant++;
        }

        /// <summary>Compares num / den with 2^t.</summary>
        private static int ComparePow2(BigInteger num, BigInteger den, int t)
            => t >= 0 ? num.CompareTo(den << t) : (num << -t).CompareTo(den);

        private static int BitLength(BigInteger x)
        {
            var bytes = x.ToByteArray(); // little-endian, two's complement; x > 0 here
            var top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            int len = top * 8, last = bytes[top];
            while (last != 0) { len++; last >>= 1; }
            return len;
        }
    }

    /// <summary>
    /// A <see cref="JsonTextWriter"/> that writes every finite double and float as <c>JSON.stringify</c> writes it
    /// (<see cref="OWJsNumber.Format(double)"/>) and changes nothing else: integers, strings and escaping are
    /// Newtonsoft's, and a NaN or an infinity is written as the writer's <see cref="JsonWriter.FloatFormatHandling"/>
    /// says (<see cref="WriteNonFinite"/>).
    /// </summary>
    /// <remarks>
    /// Newtonsoft's own spelling is <c>"R"</c> plus a <c>.0</c> on an integral value, and <c>E+XX</c> past its
    /// thresholds, so on Mono it writes seventeen digits where sixteen read back (<c>0.33333333333333331</c>) and some
    /// strings that read as a neighbouring double. A decimal is left as Newtonsoft writes it (its digits are exact).
    /// </remarks>
    internal class OWJsNumberWriter : JsonTextWriter
    {
        public OWJsNumberWriter(TextWriter writer) : base(writer) { }

        public override void WriteValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) WriteNonFinite(value);
            else WriteRawValue(OWJsNumber.Format(value));
        }

        public override void WriteValue(double? value)
        {
            if (value.HasValue) WriteValue(value.Value);
            else WriteNull();
        }

        public override void WriteValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) WriteNonFinite(value);
            else WriteRawValue(OWJsNumber.Format(value));
        }

        public override void WriteValue(float? value)
        {
            if (value.HasValue) WriteValue(value.Value);
            else WriteNull();
        }

        /// <summary>A NaN or an infinity: Newtonsoft's own handling here.</summary>
        protected virtual void WriteNonFinite(double value) => base.WriteValue(value);

        /// <summary>A float NaN or infinity: Newtonsoft's own handling here.</summary>
        protected virtual void WriteNonFinite(float value) => base.WriteValue(value);
    }

    /// <summary>
    /// A <see cref="JsonTextReader"/> that reads every JSON number with a fraction or an exponent to the nearest
    /// double, ties to even, exactly (<see cref="OWJsNumber.TryParse"/>), and changes nothing else: integers, large
    /// integers, strings and every error are Newtonsoft's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Newtonsoft 13.0.2 (Unity's <c>com.unity.nuget.newtonsoft-json</c> 3.2.2) reads such a number with
    /// <c>double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture)</c>, the runtime's own parser, and
    /// Mono's misreads ordinary numbers. It has no hook for the number's text, so this reader finds the text itself:
    /// where Newtonsoft stopped (its <see cref="JsonTextReader.LineNumber"/> and
    /// <see cref="JsonTextReader.LinePosition"/>, mapped to an offset by the same line breaks Newtonsoft counts, CR,
    /// LF and CRLF, inside strings and comments too), back over the number's characters. It then serves the exact
    /// double as <see cref="Value"/>, which is what <c>JObject.Load</c> and the serializer read.
    /// </para>
    /// <para>
    /// It falls back to Newtonsoft's value whenever it is not sure: a text that is not a strict JSON number (a quoted
    /// number read as a double, <c>.5</c>, a NaN), a magnitude past the largest double, or an exact value more than a
    /// few hundred thousand steps from Newtonsoft's (which would mean the text found is not this token's). So the
    /// worst it can do is what Newtonsoft did.
    /// </para>
    /// </remarks>
    internal sealed class OWExactReader : JsonTextReader
    {
        private readonly string _text;
        private List<int> _lineStarts;
        private object _exact;

        public OWExactReader(string text) : base(new StringReader(text ?? throw new ArgumentNullException(nameof(text))))
        {
            _text = text;
        }

        /// <summary>The exact double for a number token that has one; Newtonsoft's value for every other token.</summary>
        public override object Value => _exact ?? base.Value;

        public override bool Read()
        {
            _exact = null;
            var read = base.Read();
            Capture();
            return read;
        }

        public override double? ReadAsDouble()
        {
            _exact = null;
            var read = base.ReadAsDouble();
            return Capture() is double exact ? exact : read;
        }

        // Every other read clears the last token's exact value first, so it is never served for the wrong token.
        public override int? ReadAsInt32() { _exact = null; return base.ReadAsInt32(); }

        public override string ReadAsString() { _exact = null; return base.ReadAsString(); }

        public override byte[] ReadAsBytes() { _exact = null; return base.ReadAsBytes(); }

        public override decimal? ReadAsDecimal() { _exact = null; return base.ReadAsDecimal(); }

        public override DateTime? ReadAsDateTime() { _exact = null; return base.ReadAsDateTime(); }

        public override DateTimeOffset? ReadAsDateTimeOffset() { _exact = null; return base.ReadAsDateTimeOffset(); }

        public override bool? ReadAsBoolean() { _exact = null; return base.ReadAsBoolean(); }

        private object Capture()
        {
            if (TokenType != JsonToken.Float || !(base.Value is double read)) return null;
            if (double.IsNaN(read) || double.IsInfinity(read)) return null;

            var end = Offset();
            if (end <= 0 || end > _text.Length) return null;
            var start = end;
            while (start > 0 && IsNumberChar(_text[start - 1])) start--;
            if (start == end) return null;

            if (!OWJsNumber.TryParse(_text, start, end - start, out var exact) || !Near(exact, read)) return null;
            _exact = exact;
            return _exact;
        }

        private static bool IsNumberChar(char c) => (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E';

        /// <summary>
        /// Within 2^20 steps of each other (a relative 2e-10 for a normal double), or both zero. Mono's misreads are
        /// a step or two; a number found in the wrong place is not this close.
        /// </summary>
        private static bool Near(double exact, double read)
        {
            if (exact == 0 && read == 0) return true;
            var a = BitConverter.DoubleToInt64Bits(exact);
            var b = BitConverter.DoubleToInt64Bits(read);
            if ((a < 0) != (b < 0)) return Math.Abs(a & long.MaxValue) + Math.Abs(b & long.MaxValue) <= 1 << 20;
            return Math.Abs((a & long.MaxValue) - (b & long.MaxValue)) <= 1 << 20;
        }

        /// <summary>Where Newtonsoft's reading stopped, as an offset into the text.</summary>
        private int Offset()
        {
            var line = LineNumber;
            if (line <= 0) return -1;
            if (_lineStarts == null) _lineStarts = LineStarts(_text);
            if (line > _lineStarts.Count) return -1;
            return _lineStarts[line - 1] + LinePosition;
        }

        /// <summary>Each line's first offset, breaking where Newtonsoft breaks: LF, CR, and CRLF as one.</summary>
        private static List<int> LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\n')
                {
                    starts.Add(i + 1);
                }
                else if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    starts.Add(i + 1);
                }
            }

            return starts;
        }
    }
}
