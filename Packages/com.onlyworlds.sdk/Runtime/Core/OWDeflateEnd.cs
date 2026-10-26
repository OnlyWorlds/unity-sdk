using System;
using System.IO;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Where a raw deflate stream (RFC 1951) ends: it walks the blocks and decodes every symbol, as an inflater does,
    /// but copies nothing, and counts the bytes up to the one holding the final block's end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mono's <see cref="System.IO.Compression.DeflateStream"/> says neither where a stream ended nor whether it ended
    /// at all: given the bytes of an entry it stops at the stream's end and ignores what follows, and at the end of
    /// its input it stops as if the stream were whole. Folder spec §4.7's size bullet (OW-14, Skeld, 2026-10-24)
    /// speaks of every size an entry declares, compressed and uncompressed alike: a reader refuses an archive whose
    /// stream it cannot read to its declared end. <see cref="OWWorldArchive"/> runs this walk over each deflated
    /// entry's raw bytes, after the inflater has read it and its size and CRC-32 hold.
    /// </para>
    /// <para>
    /// The walk finds the end; it is not a second inflater. It refuses only what stops it from finding the end: a
    /// reserved block type, a stored block whose length fails its complement, a code table that is over-subscribed
    /// or misses the bits it is given, a length or distance symbol the format has no meaning for, a repeat with
    /// nothing to repeat or past the code lengths, and input that runs out first. A distance reaching before the
    /// stream's start or a block with no end-of-block code is left to the inflater (which refuses the first) and to
    /// the bytes running out (the second).
    /// </para>
    /// </remarks>
    internal sealed class OWDeflateEnd
    {
        /// <summary>
        /// The count of bytes a raw deflate stream takes from <paramref name="input"/>'s position: the last of them
        /// holds the final block's end-of-block code (or ends a final stored block), and its higher bits are padding.
        /// Reads at most <paramref name="limit"/> bytes.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The stream is broken (see the remarks), or it has not ended when <paramref name="limit"/> bytes are read:
        /// it never ends inside them.
        /// </exception>
        /// <exception cref="EndOfStreamException">The input holds fewer than <paramref name="limit"/> bytes and the stream needs more.</exception>
        internal static long Measure(Stream input, long limit)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            return new OWDeflateEnd(input, limit).Walk();
        }

        /// <summary><see cref="Measure(Stream, long)"/> over <paramref name="count"/> bytes of an array.</summary>
        internal static long Measure(byte[] bytes, int offset, int count)
        {
            using (var input = new MemoryStream(bytes, offset, count, writable: false)) return Measure(input, count);
        }

        // -- The bits -----------------------------------------------------------------------------------------------

        private readonly Stream _input;
        private long _left;                 // bytes not yet read from the input, of the limit
        private readonly byte[] _buffer;    // the size of the stream, up to 8 KiB: most entries are a few hundred bytes
        private int _at, _end;              // the unread part of _buffer
        private ulong _bits;                // bits taken from the input and not yet used, the next one lowest
        private int _count;                 // how many of _bits are real
        private long _taken;                // bytes moved into _bits

        private OWDeflateEnd(Stream input, long limit)
        {
            _input = input;
            _left = limit;
            _buffer = new byte[(int)Math.Max(1, Math.Min(8192, limit))];
        }

        /// <summary>Tops <see cref="_bits"/> up to at least 57 real bits, or to every byte the limit leaves.</summary>
        private void Fill()
        {
            while (_count <= 56)
            {
                if (_at == _end)
                {
                    if (_left == 0) return;
                    var got = _input.Read(_buffer, 0, (int)Math.Min(_buffer.Length, _left));
                    if (got <= 0) throw new EndOfStreamException($"The input ends {_left} bytes short of the deflate stream's limit.");
                    _left -= got;
                    _at = 0;
                    _end = got;
                }

                _bits |= (ulong)_buffer[_at++] << _count;
                _count += 8;
                _taken++;
            }
        }

        /// <summary>The next <paramref name="n"/> bits (0..16), the first one lowest.</summary>
        private int Take(int n)
        {
            if (_count < n)
            {
                Fill();
                if (_count < n) throw RunsOut();
            }

            var value = (int)(_bits & ((1UL << n) - 1));
            _bits >>= n;
            _count -= n;
            return value;
        }

        private InvalidDataException RunsOut()
            => new InvalidDataException($"the deflate stream has not ended when its {_taken} bytes run out");

        private static InvalidDataException Broken(string why) => new InvalidDataException("the deflate stream is broken: " + why);

        // -- The blocks ---------------------------------------------------------------------------------------------

        private long Walk()
        {
            bool last;
            do
            {
                last = Take(1) == 1;
                switch (Take(2))
                {
                    case 0: Stored(); break;
                    case 1: Codes(FixedLiterals, FixedDistances); break;
                    case 2: Dynamic(); break;
                    default: throw Broken("a block of the reserved type 3");
                }
            } while (!last);

            // Every bit used, rounded up to the byte that holds the last of them.
            return _taken - _count / 8;
        }

        private void Stored()
        {
            Take(_count % 8); // to the byte's end: the bits read so far always end on one
            var length = Take(16);
            if ((Take(16) ^ 0xFFFF) != length) throw Broken("a stored block's length fails its complement");

            // The whole bytes still in _bits come first (after the alignment it holds only whole bytes), then the
            // buffer is skipped through, refilled as it empties.
            while (length > 0)
            {
                if (_count >= 8)
                {
                    Take(8);
                    length--;
                    continue;
                }

                if (_at == _end)
                {
                    Fill();
                    if (_count == 0) throw RunsOut();
                    continue;
                }

                var skip = Math.Min(length, _end - _at);
                _at += skip;
                _taken += skip;
                length -= skip;
            }
        }

        /// <summary>One block's symbols, up to and including its end-of-block code.</summary>
        private void Codes(Huffman literals, Huffman distances)
        {
            while (true)
            {
                var symbol = Decode(literals);
                if (symbol < 256) continue;
                if (symbol == 256) return;

                symbol -= 257;
                if (symbol >= 29) throw Broken($"the length symbol {symbol + 257}");
                Take(LengthExtra[symbol]);

                var distance = Decode(distances);
                if (distance >= 30) throw Broken($"the distance symbol {distance}");
                Take(DistanceExtra[distance]);
            }
        }

        private void Dynamic()
        {
            var literalCount = Take(5) + 257;
            var distanceCount = Take(5) + 1;
            var lengthCodes = Take(4) + 4;

            var codeLengths = new byte[19];
            for (var i = 0; i < lengthCodes; i++) codeLengths[CodeLengthOrder[i]] = (byte)Take(3);
            var lengthCode = new Huffman(codeLengths, 19);

            var lengths = new byte[literalCount + distanceCount];
            for (var i = 0; i < lengths.Length;)
            {
                var symbol = Decode(lengthCode);
                if (symbol < 16)
                {
                    lengths[i++] = (byte)symbol;
                    continue;
                }

                byte repeated = 0;
                int times;
                if (symbol == 16)
                {
                    if (i == 0) throw Broken("a repeat with no code length before it");
                    repeated = lengths[i - 1];
                    times = 3 + Take(2);
                }
                else if (symbol == 17) times = 3 + Take(3);
                else times = 11 + Take(7);

                if (i + times > lengths.Length) throw Broken("a repeat past the code lengths");
                while (times-- > 0) lengths[i++] = repeated;
            }

            var literals = new Huffman(lengths, 0, literalCount);
            var distances = new Huffman(lengths, literalCount, distanceCount);
            Codes(literals, distances);
        }

        // -- Huffman codes ------------------------------------------------------------------------------------------

        /// <summary>
        /// The next symbol of <paramref name="code"/>. Past the input's end the bits read as zeros, which decide
        /// nothing: a code is prefix-free, so a symbol whose bits are all real is the stream's own, and one that needs
        /// a bit past the end means the stream runs out. Bits that begin no code (the zeros past the end counted) are
        /// broken: a refusal either way, only the words differ.
        /// </summary>
        private int Decode(Huffman code)
        {
            if (_count < MaxBits) Fill();
            var peek = (int)(_bits & ((1UL << MaxBits) - 1));

            int length, symbol;
            var fast = code.Fast[peek & FastMask];
            if (fast != 0)
            {
                length = fast >> 16;
                symbol = fast & 0xFFFF;
            }
            else if (!code.Slow(peek, out length, out symbol))
            {
                throw Broken("bits that no code of the block holds");
            }

            if (length > _count) throw RunsOut();
            _bits >>= length;
            _count -= length;
            return symbol;
        }

        private const int MaxBits = 15;
        private const int FastBits = 9;
        private const int FastMask = (1 << FastBits) - 1;

        /// <summary>
        /// A canonical Huffman code (RFC 1951 3.2.2): a table for codes of up to <see cref="FastBits"/> bits, indexed
        /// by the bits as they arrive (the code reversed), and the counts and symbols that decode a longer one a bit at a time.
        /// </summary>
        private sealed class Huffman
        {
            /// <summary>For each <see cref="FastBits"/> next bits: the symbol and its length (length &lt;&lt; 16), or 0.</summary>
            public readonly int[] Fast = new int[1 << FastBits];

            private readonly int[] _counts = new int[MaxBits + 1];
            private readonly int[] _symbols;

            public Huffman(byte[] lengths, int count) : this(lengths, 0, count)
            {
            }

            /// <exception cref="InvalidDataException">The lengths over-subscribe the code.</exception>
            public Huffman(byte[] lengths, int start, int count)
            {
                for (var s = 0; s < count; s++) _counts[lengths[start + s]]++;
                _counts[0] = 0;

                // A code may be incomplete (one distance code of one bit, or no distance code, is the usual case); one
                // whose lengths ask for more codes than there are cannot be decoded.
                var left = 1;
                for (var length = 1; length <= MaxBits; length++)
                {
                    left = (left << 1) - _counts[length];
                    if (left < 0) throw Broken("an over-subscribed code");
                }

                var offsets = new int[MaxBits + 2];
                for (var length = 1; length <= MaxBits; length++) offsets[length + 1] = offsets[length] + _counts[length];
                _symbols = new int[offsets[MaxBits + 1]];

                var next = 0; // the canonical code, MSB first, of the current length
                var at = new int[MaxBits + 1];
                for (var length = 1; length <= MaxBits; length++)
                {
                    at[length] = next;
                    next = (next + _counts[length]) << 1;
                }

                for (var s = 0; s < count; s++)
                {
                    var length = lengths[start + s];
                    if (length == 0) continue;
                    _symbols[offsets[length]++] = s;

                    var code = at[length]++;
                    if (length > FastBits) continue;
                    var reversed = Reverse(code, length);
                    for (var i = reversed; i < Fast.Length; i += 1 << length) Fast[i] = (length << 16) | s;
                }
            }

            /// <summary>Decodes a code longer than the table's, from <paramref name="peek"/>'s bits, first lowest.</summary>
            public bool Slow(int peek, out int length, out int symbol)
            {
                int code = 0, first = 0, index = 0;
                for (length = 1; length <= MaxBits; length++)
                {
                    code |= (peek >> (length - 1)) & 1;
                    var count = _counts[length];
                    if (code - first < count)
                    {
                        symbol = _symbols[index + code - first];
                        return true;
                    }

                    index += count;
                    first = (first + count) << 1;
                    code <<= 1;
                }

                symbol = 0;
                return false;
            }

            private static int Reverse(int code, int length)
            {
                var reversed = 0;
                for (var i = 0; i < length; i++)
                {
                    reversed = (reversed << 1) | (code & 1);
                    code >>= 1;
                }

                return reversed;
            }
        }

        // -- RFC 1951's tables ----------------------------------------------------------------------------------------

        private static readonly int[] LengthExtra =
            { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };

        private static readonly int[] DistanceExtra =
            { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };

        private static readonly int[] CodeLengthOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        private static readonly Huffman FixedLiterals = MakeFixedLiterals();

        // All 32 five-bit codes, as RFC 1951 3.2.6 lays them out: 30 and 31 are codes with no meaning (refused when used).
        private static readonly Huffman FixedDistances = new Huffman(Repeat(5, 32), 32);

        private static Huffman MakeFixedLiterals()
        {
            var lengths = new byte[288];
            for (var s = 0; s < 288; s++) lengths[s] = (byte)(s < 144 ? 8 : s < 256 ? 9 : s < 280 ? 7 : 8);
            return new Huffman(lengths, 288);
        }

        private static byte[] Repeat(byte value, int count)
        {
            var bytes = new byte[count];
            for (var i = 0; i < count; i++) bytes[i] = value;
            return bytes;
        }
    }
}
