using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Where a deflate stream ends (<see cref="OWDeflateEnd"/>), and the reader refusing an entry whose stream does
    /// not end in the last byte of the compressed size it declares (folder spec §4.7's size bullet, OW-14; Skeld,
    /// 2026-10-24).
    /// </summary>
    /// <remarks>
    /// Most streams here are written bit by bit, so each test holds exactly the block it names; each valid one is also
    /// inflated by the runtime's own <see cref="DeflateStream"/>, so the test's writer is held to the format too. The
    /// conformance set's two cases, <c>fail-OW-14-deflate-early-end</c> and <c>fail-OW-14-deflate-no-end</c>, are made
    /// again here as their generator makes them (the files themselves run in <c>OWWorldArchiveConformanceTests</c>).
    /// </remarks>
    public class OWDeflateEndTests
    {
        // -- Each block type ends where it ends -------------------------------------------------------------------

        [Test]
        public void Measure_AFinalStoredBlock_EndsWithItsLastByte()
        {
            var stream = Stored(true, "stored");
            Assert.AreEqual("stored", Inflated(stream));
            Assert.AreEqual(stream.Length, Measure(stream));
        }

        [Test]
        public void Measure_AFixedBlock_WithLiteralsAndAMatch()
        {
            var b = new Bits();
            b.Put(1, 1).Put(1, 2);
            foreach (var c in "abc") Literal(b, c);
            b.Code(1, 7);                 // 257: length 3
            b.Code(2, 5);                 // distance symbol 2: distance 3
            b.Code(0, 7);                 // 256: end of block
            var stream = b.Done();

            Assert.AreEqual("abcabc", Inflated(stream));
            Assert.AreEqual(stream.Length, Measure(stream));
        }

        [Test]
        public void Measure_AnEmptyFile_AsZlibDeflatesOne_IsTwoBytes()
        {
            var stream = new byte[] { 0x03, 0x00 }; // a final fixed block holding only its end code, then padding
            Assert.AreEqual("", Inflated(stream));
            Assert.AreEqual(2, Measure(stream));
        }

        [Test]
        public void Measure_ADynamicBlock_WithEveryKindOfCodeLengthRepeat()
        {
            var stream = DynamicSample(out var expected);
            Assert.AreEqual(expected, Inflated(stream));
            Assert.AreEqual(stream.Length, Measure(stream));
        }

        [Test]
        public void Measure_AStoredBlockThenAFixedOne_EndsAtTheFinalBlock()
        {
            var b = new Bits();
            b.Bytes(Stored(false, "xyz"));
            b.Put(1, 1).Put(1, 2);
            Literal(b, 'a');
            b.Code(0, 7);
            var stream = b.Done();

            Assert.AreEqual("xyza", Inflated(stream));
            Assert.AreEqual(stream.Length, Measure(stream));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Measure_TheRuntimesOwDeflate_OverManyBlocks(bool incompressible)
        {
            // Mono's deflater (zlib) writes dynamic blocks for text and stored ones for noise, many of each at this size.
            var random = new Random(7);
            var body = new byte[300_000];
            if (incompressible) random.NextBytes(body);
            else
            {
                var text = new StringBuilder();
                while (text.Length < body.Length) text.Append("{\"id\": \"0695db83-0000-7000-8000-").Append(random.Next(100000)).Append("\", \"name\": \"N\"}\n");
                body = Encoding.UTF8.GetBytes(text.ToString(0, body.Length));
            }

            var stream = Deflated(body);
            Assert.AreEqual(incompressible ? 0 : 2, (stream[0] >> 1) & 3, "the first block is of the kind the case names");
            Assert.AreEqual(stream.Length, Measure(stream));
        }

        // -- Where the end falls --------------------------------------------------------------------------------

        [Test]
        public void Measure_AStreamEndingInsideItsLastByte_CountsThatByte_WhateverItsPaddingBits()
        {
            var b = new Bits();
            b.Put(1, 1).Put(1, 2);
            foreach (var c in "pad") Literal(b, c);
            b.Code(0, 7);
            Assert.AreNotEqual(0, b.Count % 8, "the end-of-block code ends inside a byte");
            var stream = b.Done(padWithOnes: true);

            Assert.AreEqual("pad", Inflated(stream));
            Assert.AreEqual(stream.Length, Measure(stream));
        }

        [Test]
        public void Measure_AStreamEndingEarly_GivesWhereItEnds_NotTheLimit()
        {
            var whole = Deflated(Encoding.UTF8.GetBytes(new string('x', 500) + "tail"));
            var held = whole.Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
            Assert.AreEqual(whole.Length, OWDeflateEnd.Measure(held, 0, held.Length));
        }

        [Test]
        public void Measure_AStreamThatNeverEnds_IsRefused()
        {
            var e = Assert.Throws<InvalidDataException>(() => Measure(Endless("no end of block here")));
            StringAssert.Contains("has not ended", e.Message);
        }

        [TestCase(new byte[0])]          // no stream at all (a deflated empty file is 03 00)
        [TestCase(new byte[] { 0x01, 5 })] // a stored block's length cut in half
        public void Measure_AStreamCutInsideAHeader_IsRefused(byte[] stream)
            => StringAssert.Contains("has not ended", Assert.Throws<InvalidDataException>(() => Measure(stream)).Message);

        [Test]
        public void Measure_AStoredBlockLongerThanTheLimit_IsRefused()
        {
            var stream = new byte[] { 0x01, 10, 0, 0xF5, 0xFF, 1, 2, 3 }; // declares 10 bytes, holds 3
            StringAssert.Contains("has not ended", Assert.Throws<InvalidDataException>(() => Measure(stream)).Message);
        }

        [Test]
        public void Measure_AnInputShorterThanTheLimit_IsAnEndOfStream()
        {
            var endless = Endless("short");
            using (var input = new MemoryStream(endless))
                Assert.Throws<EndOfStreamException>(() => OWDeflateEnd.Measure(input, endless.Length + 10));
        }

        // -- What stops the walk ----------------------------------------------------------------------------------

        [Test]
        public void Measure_ABlockOfTheReservedType_IsRefused()
            => AssertBroken(new byte[] { 0x07, 0, 0, 0 }, "reserved type");

        [Test]
        public void Measure_AStoredLengthThatFailsItsComplement_IsRefused()
            => AssertBroken(new byte[] { 0x01, 5, 0, 0, 0, 1, 2, 3, 4, 5 }, "complement");

        [Test]
        public void Measure_ALengthSymbolPast285_IsRefused()
        {
            var b = new Bits();
            b.Put(1, 1).Put(1, 2);
            Literal(b, 'a');
            b.Code(0xC0 + 6, 8); // 286
            AssertBroken(b.Done().Concat(new byte[4]).ToArray(), "length symbol 286");
        }

        [Test]
        public void Measure_ADistanceSymbolPast29_IsRefused()
        {
            var b = new Bits();
            b.Put(1, 1).Put(1, 2);
            Literal(b, 'a');
            b.Code(1, 7);  // 257
            b.Code(30, 5); // distance symbol 30
            AssertBroken(b.Done().Concat(new byte[4]).ToArray(), "distance symbol 30");
        }

        [Test]
        public void Measure_ARepeatWithNothingBeforeIt_IsRefused()
        {
            var b = DynamicHeader(true, 257, 1);
            LengthSymbol(b, 16);
            b.Put(0, 2);
            AssertBroken(b.Done().Concat(new byte[4]).ToArray(), "a repeat with no code length");
        }

        [Test]
        public void Measure_ARepeatPastTheCodeLengths_IsRefused()
        {
            var b = DynamicHeader(true, 257, 1); // 258 lengths
            LengthSymbol(b, 18);
            b.Put(127, 7); // 138 zeros
            LengthSymbol(b, 18);
            b.Put(127, 7); // 138 more: 276
            AssertBroken(b.Done().Concat(new byte[4]).ToArray(), "past the code lengths");
        }

        [Test]
        public void Measure_AnOverSubscribedCode_IsRefused()
        {
            var literals = new byte[257];
            literals['a'] = literals['b'] = literals['c'] = 1; // three one-bit codes
            literals[256] = 1;
            var b = DynamicHeader(true, 257, 1);
            WriteLengths(b, literals.Concat(new byte[] { 1 }).ToArray());
            AssertBroken(b.Done().Concat(new byte[4]).ToArray(), "over-subscribed");
        }

        [Test]
        public void Measure_BitsNoCodeHolds_AreRefused()
        {
            // 'a' is 0 and the end is 10, so 11 is no code (an incomplete code, decodable until it is used).
            var literals = new byte[257];
            literals['a'] = 1;
            literals[256] = 2;
            var b = DynamicHeader(true, 257, 1);
            WriteLengths(b, literals.Concat(new byte[] { 1 }).ToArray());
            b.Code(0, 1);  // 'a'
            b.Code(3, 2);  // no code
            AssertBroken(b.Done(padWithOnes: true).Concat(new byte[] { 0xFF, 0xFF, 0xFF }).ToArray(), "no code");
        }

        // -- The reader (OW-14) -----------------------------------------------------------------------------------

        private string _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "ow-deflate-end-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_temp != null && Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
        }

        private const string WorldJson = "{\n  \"id\": \"0695db52-0000-7000-8000-0000000000aa\",\n  \"name\": \"W\"\n}\n";

        private static string Element(string id, string name) => "{\n  \"id\": \"" + id + "\",\n  \"name\": \"" + name + "\"\n}\n";

        [Test]
        public void Read_AWholeStreamAtItsDeclaredEnd_Reads()
        {
            // The control for the two cases below: the same zip, the same element, the stream whole.
            var body = Element("0695db83-0000-7000-8000-00000000abe5", "Early");
            var archive = Zip("whole.owworld", ("w/elements/character/early.json", body, LiteralStream(body), false));
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count);
        }

        [Test]
        public void Read_RefusesAStreamEndingBeforeItsDeclaredCompressedSize_AsTheSetsDeflateEarlyEnd()
        {
            // fail-OW-14-deflate-early-end: a whole stream, then FF FF FF FF inside the declared compressed size.
            var body = Element("0695db83-0000-7000-8000-00000000abe5", "Early");
            var payload = LiteralStream(body).Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
            var archive = Zip("early-end.owworld", ("w/elements/character/early.json", body, payload, false));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, e.Reason);
            StringAssert.Contains($"ends 4 bytes before the {payload.Length} compressed bytes it declares", e.Message);
        }

        [Test]
        public void Read_RefusesAStreamThatNeverEnds_AsTheSetsDeflateNoEnd()
        {
            // fail-OW-14-deflate-no-end: every byte a literal in one final fixed block, no end-of-block code; the sizes
            // and the CRC honest, so the inflater gives every declared byte and stops.
            var body = Element("0695db83-0000-7000-8000-00000000abe6", "Endless");
            var payload = Endless(body);
            var archive = Zip("no-end.owworld", ("w/elements/character/endless.json", body, payload, false));

            var e = Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive));
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable, e.Reason);
            StringAssert.Contains($"cannot be read to the {payload.Length} compressed bytes it declares", e.Message);
            StringAssert.Contains("has not ended", e.Message);
        }

        [Test]
        public void Read_RefusesAnEarlyEndInAFileTheWorldNeverReads()
        {
            // Every entry is checked, as a CRC is: media/ is inflated and let go, and still refused.
            const string body = "png bytes";
            var payload = LiteralStream(body).Concat(new byte[] { 0x00 }).ToArray();
            var archive = Zip("media.owworld", ("w/media/a.png", body, payload, false));
            Assert.AreEqual(OWWorldArchiveReason.NotOpenable,
                Assert.Throws<OWFolderFormatException>(() => OWWorldArchive.Read(archive)).Reason);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Read_FindsTheLocalHeaderThroughAZip64ExtraField(bool sizesToo)
        {
            // A central record whose offset field is 0xFFFFFFFF keeps the offset in its zip64 extra field, after the
            // two sizes when those fields are 0xFFFFFFFF too (APPNOTE 4.5.3). Read at the wrong place, the walk refuses.
            var body = Element("0695db83-0000-7000-8000-0000000000e1", "One");
            var archive = Zip("zip64.owworld", ("w/elements/character/one.json", body, LiteralStream(body), true), sizesToo);
            Assert.AreEqual(1, OWWorldArchive.Read(archive).Elements.Count);
        }

        [Test]
        public void Read_AWorldThisSdkPacked_Reads()
        {
            // The runtime's own deflater, through Pack: every entry ends where it declares.
            var folder = Path.Combine(_temp, "packed-world");
            Directory.CreateDirectory(Path.Combine(folder, "elements", "character"));
            File.WriteAllText(Path.Combine(folder, "world.json"), WorldJson);
            for (var i = 0; i < 40; i++)
            {
                var id = $"0695db83-0000-7000-8000-{i:x12}";
                File.WriteAllText(Path.Combine(folder, "elements", "character", $"c{i}--{id.Substring(28)}.json"),
                    Element(id, "Character " + new string('x', i * 37)));
            }

            var archive = Path.Combine(_temp, "packed.owworld");
            OWWorldArchive.Pack(folder, archive);
            Assert.AreEqual(40, OWWorldArchive.Read(archive).Elements.Count);
        }

        // -- Writing streams ------------------------------------------------------------------------------------

        private static long Measure(byte[] stream) => OWDeflateEnd.Measure(stream, 0, stream.Length);

        private static void AssertBroken(byte[] stream, string why)
        {
            var e = Assert.Throws<InvalidDataException>(() => Measure(stream));
            StringAssert.Contains(why, e.Message);
        }

        private static string Inflated(byte[] stream)
        {
            using (var input = new DeflateStream(new MemoryStream(stream), CompressionMode.Decompress))
            using (var reader = new StreamReader(input, new UTF8Encoding(false))) return reader.ReadToEnd();
        }

        private static byte[] Deflated(byte[] body)
        {
            var packed = new MemoryStream();
            using (var deflater = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) deflater.Write(body, 0, body.Length);
            return packed.ToArray();
        }

        /// <summary>One stored block (RFC 1951 3.2.4), byte-aligned from its start.</summary>
        private static byte[] Stored(bool last, string text)
        {
            var data = Encoding.ASCII.GetBytes(text);
            return new[] { (byte)(last ? 1 : 0), (byte)data.Length, (byte)(data.Length >> 8), (byte)~data.Length, (byte)(~data.Length >> 8) }
                .Concat(data).ToArray();
        }

        /// <summary>The set's <c>deflate_literal</c>: one final fixed block, every byte a literal, then the end.</summary>
        private static byte[] LiteralStream(string text)
        {
            var b = new Bits();
            b.Put(1, 1).Put(1, 2);
            foreach (var x in Encoding.UTF8.GetBytes(text)) Literal(b, x);
            b.Code(0, 7);
            return b.Done();
        }

        /// <summary>The set's no-end case: <see cref="LiteralStream"/> without its end-of-block code.</summary>
        private static byte[] Endless(string text)
        {
            var b = new Bits();
            b.Put(1, 1).Put(1, 2);
            foreach (var x in Encoding.UTF8.GetBytes(text)) Literal(b, x);
            Assert.AreNotEqual(7, (8 - b.Count % 8) % 8, "seven zero padding bits would spell the end-of-block code");
            return b.Done();
        }

        /// <summary>A fixed-Huffman literal (RFC 1951 3.2.6).</summary>
        private static void Literal(Bits b, int value)
        {
            if (value < 144) b.Code(0x30 + value, 8);
            else b.Code(0x190 + value - 144, 9);
        }

        /// <summary>
        /// A dynamic block's header with a code-length code of sixteen four-bit codes: lengths 0..12 and the three
        /// repeats 16, 17, 18 (a complete code), so any length up to 12 can be sent.
        /// </summary>
        private static Bits DynamicHeader(bool last, int literals, int distances)
        {
            var b = new Bits();
            b.Put(last ? 1 : 0, 1).Put(2, 2).Put(literals - 257, 5).Put(distances - 1, 5).Put(19 - 4, 4);
            foreach (var symbol in new[] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 })
                b.Put(LengthAlphabet.Contains(symbol) ? 4 : 0, 3);
            return b;
        }

        private static readonly int[] LengthAlphabet = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 17, 18 };

        /// <summary>A code-length symbol in <see cref="DynamicHeader"/>'s code: its canonical code is its place in the alphabet.</summary>
        private static void LengthSymbol(Bits b, int symbol) => b.Code(Array.IndexOf(LengthAlphabet, symbol), 4);

        /// <summary>Code lengths, run-length coded as zlib does: 18 and 17 for zeros, 16 to repeat the length before.</summary>
        private static void WriteLengths(Bits b, byte[] lengths)
        {
            for (var i = 0; i < lengths.Length;)
            {
                var run = 1;
                while (i + run < lengths.Length && lengths[i + run] == lengths[i]) run++;
                if (lengths[i] == 0 && run >= 11)
                {
                    run = Math.Min(run, 138);
                    LengthSymbol(b, 18);
                    b.Put(run - 11, 7);
                }
                else if (lengths[i] == 0 && run >= 3)
                {
                    run = Math.Min(run, 10);
                    LengthSymbol(b, 17);
                    b.Put(run - 3, 3);
                }
                else if (i > 0 && lengths[i - 1] == lengths[i] && run >= 3)
                {
                    run = Math.Min(run, 6);
                    LengthSymbol(b, 16);
                    b.Put(run - 3, 2);
                }
                else
                {
                    run = 1;
                    LengthSymbol(b, lengths[i]);
                }

                i += run;
            }
        }

        /// <summary>RFC 1951 3.2.2's canonical codes for <paramref name="lengths"/>.</summary>
        private static int[] Canonical(byte[] lengths)
        {
            var count = new int[16];
            foreach (var length in lengths) if (length > 0) count[length]++;
            var next = new int[16];
            for (int bits = 1, code = 0; bits < 16; bits++)
            {
                code = (code + count[bits - 1]) << 1;
                next[bits] = code;
            }

            return lengths.Select(length => length == 0 ? 0 : next[length]++).ToArray();
        }

        /// <summary>
        /// One dynamic block whose lengths use all three repeats: a..g and k four bits each (16 repeats the 4), 256 two
        /// bits, 257 and 258 three bits; distances 1 and 2 one bit each; the zeros sent with 18, the three before k with 17.
        /// </summary>
        private static byte[] DynamicSample(out string expected)
        {
            var literals = new byte[259];
            for (var c = 'a'; c <= 'g'; c++) literals[c] = 4;
            literals['k'] = 4;
            literals[256] = 2;
            literals[257] = literals[258] = 3;
            var distances = new byte[] { 1, 1 };
            var lit = Canonical(literals);
            var dist = Canonical(distances);

            var b = DynamicHeader(true, literals.Length, distances.Length);
            WriteLengths(b, literals.Concat(distances).ToArray());
            foreach (var c in "abc") b.Code(lit[c], 4);
            b.Code(lit[257], 3);   // length 3
            b.Code(dist[1], 1);    // distance 2
            b.Code(lit[258], 3);   // length 4
            b.Code(dist[0], 1);    // distance 1
            b.Code(lit['k'], 4);
            b.Code(lit[256], 2);
            expected = "abc" + "bcb" + "bbbb" + "k";
            return b.Done();
        }

        /// <summary>Bits written first-lowest into bytes, as deflate packs them.</summary>
        private sealed class Bits
        {
            private readonly List<byte> _bytes = new List<byte>();
            private int _held, _count;

            /// <summary>Bits written so far.</summary>
            public int Count => _bytes.Count * 8 + _count;

            /// <summary><paramref name="count"/> bits of <paramref name="value"/>, lowest first (a header field, extra bits).</summary>
            public Bits Put(int value, int count)
            {
                for (var i = 0; i < count; i++)
                {
                    _held |= ((value >> i) & 1) << _count;
                    if (++_count == 8) Flush();
                }

                return this;
            }

            /// <summary>A Huffman code, its highest bit first.</summary>
            public Bits Code(int code, int length)
            {
                for (var i = length - 1; i >= 0; i--) Put((code >> i) & 1, 1);
                return this;
            }

            /// <summary>Whole bytes, at a byte boundary.</summary>
            public Bits Bytes(byte[] bytes)
            {
                Assert.AreEqual(0, _count, "bytes go in at a byte boundary");
                _bytes.AddRange(bytes);
                return this;
            }

            public byte[] Done(bool padWithOnes = false)
            {
                if (_count > 0)
                {
                    if (padWithOnes) _held |= 0xFF << _count;
                    _count = 7;
                    Put(0, 1);
                }

                return _bytes.ToArray();
            }

            private void Flush()
            {
                _bytes.Add((byte)_held);
                _held = 0;
                _count = 0;
            }
        }

        // -- Writing zips ---------------------------------------------------------------------------------------

        /// <summary>
        /// The world <c>w/</c> (its <c>world.json</c> stored) plus the given entries, each deflated with exactly the
        /// payload given and declaring that payload's length, the body's length and the body's CRC-32 (all honest but
        /// the stream). <paramref name="zip64"/> on an entry moves its central record's offset into a zip64 extra field;
        /// <paramref name="sizesToo"/> moves its two sizes there as well.
        /// </summary>
        private string Zip(string fileName, (string name, string body, byte[] payload, bool zip64) entry, bool sizesToo = false)
        {
            var files = new List<(string name, byte[] body, byte[] payload, int method, bool zip64)>
            {
                ("w/world.json", Encoding.UTF8.GetBytes(WorldJson), Encoding.UTF8.GetBytes(WorldJson), 0, false),
                (entry.name, Encoding.UTF8.GetBytes(entry.body), entry.payload, 8, entry.zip64),
            };

            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            var offsets = new List<long>();
            foreach (var f in files)
            {
                offsets.Add(output.Position);
                var name = Encoding.UTF8.GetBytes(f.name);
                w.Write(0x04034b50u);
                w.Write((ushort)20); w.Write((ushort)0x0800); w.Write((ushort)f.method);
                w.Write((ushort)0); w.Write((ushort)0x0021);
                w.Write(Crc32(f.body)); w.Write((uint)f.payload.Length); w.Write((uint)f.body.Length);
                w.Write((ushort)name.Length); w.Write((ushort)0);
                w.Write(name);
                w.Write(f.payload);
            }

            var directory = output.Position;
            for (var i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var name = Encoding.UTF8.GetBytes(f.name);
                var big = f.zip64 && sizesToo;
                var extra = new MemoryStream();
                if (f.zip64)
                {
                    var x = new BinaryWriter(extra);
                    x.Write((ushort)1); x.Write((ushort)(big ? 24 : 8));
                    if (big) { x.Write((ulong)f.body.Length); x.Write((ulong)f.payload.Length); }
                    x.Write((ulong)offsets[i]);
                }

                w.Write(0x02014b50u);
                w.Write((ushort)45); w.Write((ushort)45); w.Write((ushort)0x0800); w.Write((ushort)f.method);
                w.Write((ushort)0); w.Write((ushort)0x0021);
                w.Write(Crc32(f.body));
                w.Write(big ? uint.MaxValue : (uint)f.payload.Length);
                w.Write(big ? uint.MaxValue : (uint)f.body.Length);
                w.Write((ushort)name.Length); w.Write((ushort)extra.Length); w.Write((ushort)0);
                w.Write((ushort)0); w.Write((ushort)0); w.Write(0u);
                w.Write(f.zip64 ? uint.MaxValue : (uint)offsets[i]);
                w.Write(name);
                w.Write(extra.ToArray());
            }

            var size = output.Position - directory;
            w.Write(0x06054b50u);
            w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)files.Count); w.Write((ushort)files.Count);
            w.Write((uint)size); w.Write((uint)directory);
            w.Write((ushort)0);
            w.Flush();

            var path = Path.Combine(_temp, fileName);
            File.WriteAllBytes(path, output.ToArray());
            return path;
        }

        private static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var x in bytes)
            {
                crc ^= x;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }

            return ~crc;
        }
    }
}
