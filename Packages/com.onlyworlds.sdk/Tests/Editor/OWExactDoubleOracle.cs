using System.Globalization;
using System.Numerics;
using System.Text;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// A double to decimal text and back, computed in exact integers and never by the runtime, so a test can tell what
    /// an exact writer (JSON.stringify, Python's repr) writes and what an exact reader reads on every host. The tests
    /// of <see cref="OWJson"/> hold it to this, never to the runtime's own parse or print: under Unity's Mono,
    /// <c>"R"</c> and <c>double.Parse</c> agree with each other and with nothing else, so a round trip through the
    /// runtime cannot see the fault.
    /// </summary>
    internal static class OWExactDoubleOracle
    {
        /// <summary>
        /// The shortest decimal that reads back to the same double, nearest the exact value (Burger and Dybvig's
        /// free-format digits), as <c>(digits, n)</c> with the value <c>0.digits x 10^n</c> and no trailing zero.
        /// </summary>
        public static (string Digits, int N) Shortest(double v)
        {
            long bits = System.BitConverter.DoubleToInt64Bits(v);
            if (v == 0) return ("0", 1);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            int biased = (int)((bits >> 52) & 0x7FF);
            if (biased == 0x7FF) throw new System.ArgumentException("not a finite number");

            // v = f * 2^e exactly; the bounds between v and its neighbours are half a step either side, the lower one
            // a quarter step when f is the first mantissa of its binade.
            BigInteger f = biased == 0 ? fraction : fraction | (1L << 52);
            int e = biased == 0 ? -1074 : biased - 1075;
            bool lowerCloser = biased > 1 && fraction == 0;
            bool even = ((long)(f & 1)) == 0; // a round-half-even reader gives the bounds to v when f is even
            BigInteger r, s, mPlus, mMinus;
            if (e >= 0)
            {
                BigInteger be = BigInteger.Pow(2, e);
                if (!lowerCloser) { r = f * be * 2; s = 2; mPlus = be; mMinus = be; }
                else { r = f * be * 4; s = 4; mPlus = be * 2; mMinus = be; }
            }
            else
            {
                if (!lowerCloser) { r = f * 2; s = BigInteger.Pow(2, -e) * 2; mPlus = 1; mMinus = 1; }
                else { r = f * 4; s = BigInteger.Pow(2, 1 - e) * 2; mPlus = 2; mMinus = 1; }
            }

            // Scale so that v = 0.d1d2... * 10^k. The estimate is corrected both ways, so the runtime's logarithm
            // cannot change the result.
            int k = (int)System.Math.Ceiling(System.Math.Log10(System.Math.Abs(v)) - 1e-10);
            if (k >= 0) s *= BigInteger.Pow(10, k);
            else { BigInteger p = BigInteger.Pow(10, -k); r *= p; mPlus *= p; mMinus *= p; }
            while (even ? r + mPlus >= s : r + mPlus > s) { s *= 10; k++; }
            while (even ? (r + mPlus) * 10 < s : (r + mPlus) * 10 <= s) { r *= 10; mPlus *= 10; mMinus *= 10; k--; }

            var digits = new StringBuilder(17);
            while (true)
            {
                r *= 10; mPlus *= 10; mMinus *= 10;
                BigInteger d = BigInteger.DivRem(r, s, out r);
                bool low = even ? r <= mMinus : r < mMinus;
                bool high = even ? r + mPlus >= s : r + mPlus > s;
                if (!low && !high) { digits.Append((char)('0' + (int)d)); continue; }
                if (low && high)
                {
                    int c = (r * 2).CompareTo(s);
                    if (c > 0 || (c == 0 && ((int)d & 1) == 1)) d += 1;
                }
                else if (high) d += 1;
                digits.Append((char)('0' + (int)d));
                break;
            }

            // Burger and Dybvig's termination never rounds a 9 up to 10 once k is fixed as above; if it ever did, the
            // oracle is wrong, and says so rather than spelling a ':'.
            string ds = digits.ToString();
            if (ds.IndexOf(':') >= 0) throw new System.InvalidOperationException("oracle carried a digit for " + bits);
            return (ds.TrimEnd('0'), k);
        }

        /// <summary>
        /// What <c>JSON.stringify</c> writes for a finite double: ECMAScript <c>Number::toString</c> over the shortest
        /// digits, both zeros as <c>0</c>.
        /// </summary>
        public static string EcmaScript(double v)
        {
            if (v == 0) return "0";
            var (digits, n) = Shortest(v);
            int k = digits.Length;
            string body;
            if (k <= n && n <= 21) body = digits + new string('0', n - k);
            else if (0 < n && n <= 21) body = digits.Substring(0, n) + "." + digits.Substring(n);
            else if (-6 < n && n <= 0) body = "0." + new string('0', -n) + digits;
            else
            {
                int e = n - 1;
                body = digits.Substring(0, 1) + (k > 1 ? "." + digits.Substring(1) : "") + "e" + (e >= 0 ? "+" : "-")
                    + System.Math.Abs(e).ToString(CultureInfo.InvariantCulture);
            }
            return v < 0 ? "-" + body : body;
        }

        /// <summary>
        /// The same shortest digits in Python's <c>repr</c> layout (<c>1e-07</c>, <c>1e+16</c>, <c>0.0001</c>,
        /// <c>1.0</c>): what keel writes. A second spelling for the reader to meet.
        /// </summary>
        public static string Python(double v)
        {
            if (v == 0) return System.BitConverter.DoubleToInt64Bits(v) < 0 ? "-0.0" : "0.0";
            var (digits, n) = Shortest(v);
            int k = digits.Length;
            int e = n - 1;
            string body;
            if (-4 <= e && e < 16)
            {
                if (n <= 0) body = "0." + new string('0', -n) + digits;
                else if (k <= n) body = digits + new string('0', n - k) + ".0";
                else body = digits.Substring(0, n) + "." + digits.Substring(n);
            }
            else
            {
                body = digits.Substring(0, 1) + (k > 1 ? "." + digits.Substring(1) : "") + "e" + (e >= 0 ? "+" : "-")
                    + (System.Math.Abs(e) < 10 ? "0" : "") + System.Math.Abs(e).ToString(CultureInfo.InvariantCulture);
            }
            return v < 0 ? "-" + body : body;
        }

        /// <summary>
        /// A JSON number (<c>-?digits(.digits)?([eE][+-]?digits)?</c>) to the nearest double, ties to even, exactly.
        /// Throws on anything else; a magnitude past the largest double is an error, never infinity.
        /// </summary>
        public static double Parse(string text)
        {
            if (text == null) throw new System.ArgumentNullException(nameof(text));
            int i = 0, n = text.Length;
            bool negative = false;
            if (i < n && text[i] == '-') { negative = true; i++; }
            BigInteger m = BigInteger.Zero;
            int scale = 0, digitsSeen = 0;
            while (i < n && text[i] >= '0' && text[i] <= '9') { m = m * 10 + (text[i] - '0'); i++; digitsSeen++; }
            if (i < n && text[i] == '.')
            {
                i++;
                int before = digitsSeen;
                while (i < n && text[i] >= '0' && text[i] <= '9') { m = m * 10 + (text[i] - '0'); i++; digitsSeen++; scale--; }
                if (digitsSeen == before) throw new System.FormatException("no digits after the point: " + text);
            }
            if (digitsSeen == 0) throw new System.FormatException("not a number: " + text);
            if (i < n && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                bool expNegative = false;
                if (i < n && (text[i] == '+' || text[i] == '-')) { expNegative = text[i] == '-'; i++; }
                int start = i;
                long x = 0;
                while (i < n && text[i] >= '0' && text[i] <= '9') { if (x < 100000) x = x * 10 + (text[i] - '0'); i++; }
                if (i == start) throw new System.FormatException("no digits in the exponent: " + text);
                scale += (int)(expNegative ? -x : x);
            }
            if (i != n) throw new System.FormatException("trailing text: " + text);
            if (m.IsZero) return negative ? -0.0 : 0.0;

            int magnitude = digitsSeen + scale;
            if (magnitude < -400) return negative ? -0.0 : 0.0;
            if (magnitude > 400) throw new System.OverflowException("past the largest double: " + text);

            BigInteger num = m, den = BigInteger.One;
            if (scale >= 0) num *= BigInteger.Pow(10, scale);
            else den = BigInteger.Pow(10, -scale);

            // value = num / den; find b with 2^52 <= value / 2^b < 2^53, then no lower than the subnormals' -1074.
            int b = BitLength(num) - BitLength(den) - 53;
            while (true)
            {
                if (Compare(num, den, b, 53) >= 0) { b++; continue; }
                if (Compare(num, den, b, 52) < 0) { b--; continue; }
                break;
            }
            if (b < -1074) b = -1074;

            BigInteger sn = num, sd = den;
            if (b >= 0) sd <<= b; else sn <<= -b;
            BigInteger q = BigInteger.DivRem(sn, sd, out BigInteger rem);
            int c = (rem * 2).CompareTo(sd);
            if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 53)) { q >>= 1; b++; }
            if (b + 52 > 1023) throw new System.OverflowException("past the largest double: " + text);

            long bits = b == -1074 ? (long)q : ((long)(b + 1075) << 52) | ((long)q & 0xFFFFFFFFFFFFFL);
            if (negative) bits |= long.MinValue;
            return System.BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>
        /// A check independent of <see cref="Parse"/>: is <paramref name="d"/> the double nearest the decimal
        /// <paramref name="text"/> (ties to the even significand)? Compares the exact rational value of the text with
        /// the midpoints to <paramref name="d"/>'s neighbours. For finite, non-zero results below the largest double.
        /// </summary>
        public static bool IsNearest(string text, double d)
        {
            bool negative = text.StartsWith("-");
            if ((System.BitConverter.DoubleToInt64Bits(d) < 0) != negative) return false;
            var (tn, td) = Rational(negative ? text.Substring(1) : text);
            long bits = System.BitConverter.DoubleToInt64Bits(System.Math.Abs(d));
            if (bits == 0 || bits >= 0x7FEFFFFFFFFFFFFFL) return false;

            // In units of 2^-1076: d is 4*f*2^(e+1074)... take every double as an integer times 2^-1075.
            BigInteger Units(long b)
            {
                long fr = b & 0xFFFFFFFFFFFFFL;
                int bi = (int)((b >> 52) & 0x7FF);
                BigInteger f = bi == 0 ? fr : fr | (1L << 52);
                int e = bi == 0 ? -1074 : bi - 1075;
                return f << (e + 1075); // d = f * 2^e = (f << (e + 1075)) * 2^-1075
            }

            // Midpoints in units of 2^-1076 (twice the 2^-1075 units).
            BigInteger lo = Units(bits - 1) + Units(bits);
            BigInteger hi = Units(bits) + Units(bits + 1);
            BigInteger scaledNum = tn << 1076; // text * 2^1076 = tn * 2^1076 / td
            int cLo = scaledNum.CompareTo(lo * td);
            int cHi = scaledNum.CompareTo(hi * td);
            bool even = (bits & 1) == 0;
            return even ? cLo >= 0 && cHi <= 0 : cLo > 0 && cHi < 0;
        }

        static (BigInteger Num, BigInteger Den) Rational(string text)
        {
            int ePos = text.IndexOfAny(new[] { 'e', 'E' });
            string mant = ePos < 0 ? text : text.Substring(0, ePos);
            int exp = ePos < 0 ? 0 : int.Parse(text.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            int dot = mant.IndexOf('.');
            if (dot >= 0) { exp -= mant.Length - dot - 1; mant = mant.Remove(dot, 1); }
            BigInteger m = BigInteger.Parse(mant, CultureInfo.InvariantCulture);
            return exp >= 0 ? (m * BigInteger.Pow(10, exp), BigInteger.One) : (m, BigInteger.Pow(10, -exp));
        }

        /// <summary>Compares num / den with 2^(b + p).</summary>
        static int Compare(BigInteger num, BigInteger den, int b, int p)
        {
            int t = b + p;
            return t >= 0 ? num.CompareTo(den << t) : (num << -t).CompareTo(den);
        }

        static int BitLength(BigInteger x)
        {
            byte[] bytes = x.ToByteArray(); // little-endian, two's complement; x > 0 here
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            int len = top * 8, last = bytes[top];
            while (last != 0) { len++; last >>= 1; }
            return len;
        }
    }
}
