using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Editor
{
    /// <summary>
    /// Doubles through <see cref="OWJson"/>, both ways, held to an exact oracle (<see cref="OWExactDoubleOracle"/>)
    /// rather than to the runtime: what an exact writer (npm's <c>JSON.stringify</c>, keel's Python) sends must be read
    /// as the double it denotes, and what the SDK writes must be <c>JSON.stringify</c>'s bytes. A test that checks the
    /// SDK's write against the SDK's own read passes on a runtime whose formatting and parsing are wrong together, as
    /// Unity's Mono's are.
    /// </summary>
    public class OWJsonExactDoublesTests
    {
        // -- The numbers ------------------------------------------------------

        /// <summary>
        /// 19,990 finite doubles: every even draw a bearing in [0, 360), every odd one a random bit pattern (NaN and
        /// the infinities skipped). Seeded, so a failure names the same number on every run.
        /// </summary>
        private static readonly double[] Numbers = Generate();

        private static double[] Generate()
        {
            var list = new List<double>(20000);
            ulong x = 20261101UL;
            for (var i = 0; i < 20000; i++)
            {
                x += 0x9E3779B97F4A7C15UL;
                var z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                var v = i % 2 == 0 ? (z >> 11) * (1.0 / (1UL << 53)) * 360.0 : BitConverter.Int64BitsToDouble((long)z);
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                list.Add(v);
            }

            return list.ToArray();
        }

        /// <summary>The edges: every power of two, the subnormals' ends, the layout's thresholds, the pinned vectors.</summary>
        private static double[] Edges()
        {
            var list = new List<double>(PowersOfTwoFromBits());
            list.AddRange(new[]
            {
                double.Epsilon, BitConverter.Int64BitsToDouble(2), BitConverter.Int64BitsToDouble(0xFFFFFFFFFFFFFL),
                BitConverter.Int64BitsToDouble(0x10000000000000L), double.MaxValue, -double.MaxValue,
                1e21, 1e20, 123e18, 1e-7, 1.5e-7, 1e-6, 0.000001, 1e16, 1e-5, 1e-4, 9007199254740993d, 4.35,
                0.1, 0.2, 0.1 + 0.2, 1.0 / 3, 2.0 / 3, 51.6721044378159, 263.2953629956518, 305.2019929825519,
                BitConverter.Int64BitsToDouble(-8627924879639660951), BitConverter.Int64BitsToDouble(545863264117066279),
                BitConverter.Int64BitsToDouble(7359235075472980790), 1.5, -3.25, 359.99999999999994,
            });
            return list.ToArray();
        }

        // Math.Pow is the runtime's; a power of two is built from bits so the edge itself is never in doubt.
        private static double BitsOfPowerOfTwo(int e)
            => e >= -1022 ? BitConverter.Int64BitsToDouble((long)(e + 1023) << 52) : BitConverter.Int64BitsToDouble(1L << (e + 1074));

        private static double[] All() => Numbers.Concat(Edges()).ToArray();

        private static IEnumerable<double> PowersOfTwoFromBits()
        {
            for (var e = -1074; e <= 1023; e++) yield return BitsOfPowerOfTwo(e);
        }

        /// <summary>
        /// A number token's double. An integer token (an integral spelling below 1e21) is converted by the oracle from
        /// its exact text, so the runtime's own integer-to-double conversion is never what is measured.
        /// </summary>
        private static double AsDouble(JToken token)
            => token.Type == JTokenType.Integer ? OWExactDoubleOracle.Parse(token.ToString(Formatting.None)) : (double)token;

        private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        private static string Show(double v) => BitConverter.DoubleToInt64Bits(v) + " (" + OWExactDoubleOracle.EcmaScript(v) + ")";

        /// <summary>A body's one number, as written: <c>{"x":N}</c> to <c>N</c>.</summary>
        private static string Number(string body)
        {
            Assert.IsTrue(body.StartsWith("{\"x\":") && body.EndsWith("}"), body);
            return body.Substring(5, body.Length - 6);
        }

        private static void NoneWrong(List<string> wrong, int of, string what)
        {
            Assert.AreEqual(0, wrong.Count,
                $"{what}: {wrong.Count} of {of} wrong; first {string.Join(" | ", wrong.Take(5))}");
        }

        // -- The oracle itself ------------------------------------------------

        [Test]
        public void Oracle_ReadsItsOwnShortestDigitsBack_AndTheNearestCheckAgrees()
        {
            // The oracle is held to a second, independent check before anything is held to the oracle.
            // The nearest check is the costly one (rationals scaled by 2^1076), so it takes every edge and every tenth
            // of the random numbers.
            var wrong = new List<string>();
            var all = All();
            for (var i = 0; i < all.Length; i++)
            {
                var v = all[i];
                if (v == 0) continue;
                var text = OWExactDoubleOracle.EcmaScript(v);
                var back = OWExactDoubleOracle.Parse(text);
                if (!Same(back, v)) wrong.Add("parse " + Show(v));
                var checkNearest = i >= Numbers.Length || i % 10 == 0;
                if (checkNearest && Math.Abs(v) < double.MaxValue && !OWExactDoubleOracle.IsNearest(text, v)) wrong.Add("nearest " + Show(v));
                var python = OWExactDoubleOracle.Python(v);
                if (!Same(OWExactDoubleOracle.Parse(python), v)) wrong.Add("python " + python);
            }

            NoneWrong(wrong, all.Length, "oracle");

            // And the nearest check is not vacuous: a neighbour of the double a text denotes is never the nearest.
            foreach (var v in Numbers.Take(200))
            {
                var bits = BitConverter.DoubleToInt64Bits(v);
                var text = OWExactDoubleOracle.EcmaScript(v);
                Assert.IsFalse(OWExactDoubleOracle.IsNearest(text, BitConverter.Int64BitsToDouble(bits + 1)), "neighbour of " + text);
                Assert.IsFalse(OWExactDoubleOracle.IsNearest(text, BitConverter.Int64BitsToDouble(bits - 1)), "neighbour of " + text);
            }
            Assert.AreEqual("0.3333333333333333", OWExactDoubleOracle.EcmaScript(1.0 / 3));
            Assert.AreEqual("1e+21", OWExactDoubleOracle.EcmaScript(1e21));
            Assert.AreEqual("1e-7", OWExactDoubleOracle.EcmaScript(1e-7));
            Assert.AreEqual("5e-324", OWExactDoubleOracle.EcmaScript(double.Epsilon));
            Assert.AreEqual("1e-07", OWExactDoubleOracle.Python(1e-7));
            Assert.AreEqual("1.0", OWExactDoubleOracle.Python(1.0));
        }

        // -- Write ------------------------------------------------------------

        [Test]
        public void Serialize_WritesEveryDoubleAsJsonStringifyDoes()
        {
            var wrong = new List<string>();
            var all = All();
            foreach (var v in all)
            {
                var written = Number(OWJson.Serialize(new JObject { ["x"] = v }));
                var expected = OWExactDoubleOracle.Wire(v);
                if (written != expected) wrong.Add(written + " for " + expected);
            }

            NoneWrong(wrong, all.Length, "OWJson.Serialize against JSON.stringify's bytes");
        }

        [Test]
        public void Serialize_WritesNoStringThatAnExactReaderReadsAsAnotherDouble()
        {
            var wrong = new List<string>();
            foreach (var v in Numbers)
            {
                var written = Number(OWJson.Serialize(new JObject { ["x"] = v }));
                if (!Same(OWExactDoubleOracle.Parse(written), v)) wrong.Add(written + " for " + Show(v));
            }

            NoneWrong(wrong, Numbers.Length, "OWJson.Serialize read back exactly");
        }

        [Test]
        public void Serialize_PinnedVectors()
        {
            string One(object v) => Number(OWJson.Serialize(new JObject { ["x"] = JToken.FromObject(v) }));

            Assert.AreEqual("0.3333333333333333", One(1.0 / 3), "JSON.stringify's sixteen digits; Mono's \"R\" writes seventeen.");
            Assert.AreEqual("51.6721044378159", One(51.6721044378159));

            // The measured fault: one neighbour of 51.6721044378159 went out as "51.6721044378159" itself, which an
            // exact reader reads as the other double. Each neighbour gets its own shortest spelling.
            var bits = BitConverter.DoubleToInt64Bits(51.6721044378159);
            foreach (var neighbour in new[] { BitConverter.Int64BitsToDouble(bits - 1), BitConverter.Int64BitsToDouble(bits + 1) })
            {
                Assert.AreEqual(OWExactDoubleOracle.Wire(neighbour), One(neighbour));
                Assert.AreNotEqual("51.6721044378159", One(neighbour));
            }
            Assert.AreEqual("1.0", One(1.0), "An integral double keeps its point on the wire, as Newtonsoft always wrote it.");
            Assert.AreEqual("-3.0", One(-3.0));
            // Negative zero: the harness's Mono writes -0.0, Unity's Editor 0.0 (measured 2026-11-01; the sign is gone
            // before the writer sees it, cause not found). The same number to every reader; either spelling passes.
            CollectionAssert.Contains(new[] { "-0.0", "0.0" }, One(-0.0), "Negative zero keeps its point on the wire.");
            Assert.AreEqual("100000000000000000000.0", One(1e20));
            Assert.AreEqual("1e+21", One(1e21));
            Assert.AreEqual("0.000001", One(0.000001));
            Assert.AreEqual("1e-7", One(1e-7));
            Assert.AreEqual("1.5e-7", One(1.5e-7));
            Assert.AreEqual("0.30000000000000004", One(0.1 + 0.2));
            Assert.AreEqual("1.7976931348623157e+308", One(double.MaxValue));
            Assert.AreEqual("5e-324", One(double.Epsilon));
            Assert.AreEqual("-7.364551133699082e-269", One(BitConverter.Int64BitsToDouble(-8627924879639660951)));
            Assert.AreEqual("3.5668811172242183e-272", One(BitConverter.Int64BitsToDouble(545863264117066279)));
            Assert.AreEqual("9.164848815383977e+183", One(BitConverter.Int64BitsToDouble(7359235075472980790)));
            Assert.AreEqual("0.1", One(0.1f), "A float keeps its own shortest digits.");
        }

        [Test]
        public void Serialize_IntegersStringsAndNonFiniteValues_AreAsBefore()
        {
            Assert.AreEqual("{\"a\":1874,\"b\":-492,\"c\":\"1.0\",\"d\":123456789012345678901234567890}",
                OWJson.Serialize(OWJson.ParseObject("{\"a\":1874,\"b\":-492,\"c\":\"1.0\",\"d\":123456789012345678901234567890}")));
            Assert.AreEqual("{\"x\":\"NaN\"}", OWJson.Serialize(new JObject { ["x"] = double.NaN }),
                "Non-finite values keep Newtonsoft's spelling on the wire; only finite doubles changed.");
            Assert.AreEqual("{\"x\":\"Infinity\"}", OWJson.Serialize(new JObject { ["x"] = double.PositiveInfinity }));
            Assert.AreEqual("{\"x\":null}", OWJson.Serialize(new JObject { ["x"] = null }));
        }

        private sealed class Typed
        {
            public double W { get; set; }
            public double? N { get; set; }
            public float F { get; set; }
            public long L { get; set; }
        }

        [Test]
        public void Serialize_ATypedObject_WritesItsDoublesAsJsonStringifyDoes()
        {
            Assert.AreEqual("{\"W\":0.3333333333333333,\"N\":51.6721044378159,\"F\":0.1,\"L\":9007199254740993}",
                OWJson.Serialize(new Typed { W = 1.0 / 3, N = 51.6721044378159, F = 0.1f, L = 9007199254740993L }));
            Assert.AreEqual("{\"W\":1.0,\"N\":null,\"F\":0.0,\"L\":0}", OWJson.Serialize(new Typed { W = 1.0 }));
        }

        // -- Read -------------------------------------------------------------

        private static double ReadOne(string number) => AsDouble(OWJson.ParseObject("{\"x\":" + number + "}")["x"]);

        [Test]
        public void ParseObject_ReadsEveryShortestStringAsTheDoubleItDenotes()
        {
            var wrong = new List<string>();
            var all = All();
            foreach (var v in all)
            {
                if (v == 0) continue;
                var text = OWExactDoubleOracle.EcmaScript(v);
                var token = OWJson.ParseObject("{\"x\":" + text + "}")["x"];

                // An integral spelling below 1e21 reads as an integer token, kept exactly as written (a double's
                // conversion from it is the runtime's business, not the reader's); every other spelling is a double.
                if (token.Type == JTokenType.Integer)
                {
                    if (token.ToString(Formatting.None) != text) wrong.Add("integer " + text);
                }
                else if (!Same((double)token, v)) wrong.Add(text);
            }

            NoneWrong(wrong, all.Length, "OWJson.ParseObject of JSON.stringify's spelling");
        }

        [Test]
        public void ParseObject_ReadsPythonsAndSeventeenDigitSpellingsExactly()
        {
            var wrong = new List<string>();
            foreach (var v in Numbers)
            {
                var python = OWExactDoubleOracle.Python(v);
                if (!Same(ReadOne(python), v)) wrong.Add("py " + python);
                var g17 = v.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
                var expected = OWExactDoubleOracle.Parse(g17);
                if (!Same(ReadOne(g17), expected)) wrong.Add("g17 " + g17);
            }

            NoneWrong(wrong, Numbers.Length * 2, "OWJson.ParseObject of Python's and G17 spellings");
        }

        [Test]
        public void ParseObject_ReadsNonShortestDecimalsToTheNearestDouble()
        {
            // Decimals no writer would send, each the nearest-double question in its hardest form: just around a
            // midpoint, past seventeen digits, at the subnormal floor.
            var cases = new[]
            {
                "0.1000000000000000055511151231257827021181583404541015625", // 0.1 exactly
                "0.10000000000000000555111512312578270211815834045410156250001",
                "9007199254740993.0", // a tie between 2^53 and 2^53 + 2: to even
                "9007199254740993.0000000000000001",
                "2.4703282292062327e-324", // just below half the smallest subnormal: 0
                "2.4703282292062328e-324", // just above: the smallest subnormal
                "4.9406564584124654e-324",
                "2.2250738585072011e-308", // the subnormal/normal boundary's famous case
                "2.2250738585072012e-308",
                "1.7976931348623157e308",
                "1e-400",
                "123456789012345678901234567890.5",
                "0.000000000000000000000000000000000000000000001",
            };

            var wrong = new List<string>();
            foreach (var text in cases)
            {
                var expected = OWExactDoubleOracle.Parse(text);
                if (!Same(ReadOne(text), expected)) wrong.Add(text + " read as " + ReadOne(text).ToString("G17"));
            }

            NoneWrong(wrong, cases.Length, "nearest double");
        }

        [Test]
        public void ParseObject_ZerosIntegersAndTypes_AreAsBefore()
        {
            var o = OWJson.ParseObject(
                "{\"neg\":-0.0,\"zero\":0.0,\"i\":1874,\"big\":123456789012345678901234567890,\"long\":9007199254740993,"
                + "\"e\":1e5,\"s\":\"1.5\",\"t\":\"2026-09-04T20:36:14.605251+00:00\",\"nan\":NaN}");

            Assert.IsTrue(Same(-0.0, (double)o["neg"]), "-0.0 keeps its sign.");
            Assert.AreEqual(JTokenType.Float, o["zero"].Type);
            Assert.AreEqual(JTokenType.Integer, o["i"].Type);
            Assert.AreEqual(1874L, ((JValue)o["i"]).Value);
            Assert.AreEqual(JTokenType.Integer, o["big"].Type, "Large integers stay integers.");
            Assert.AreEqual(System.Numerics.BigInteger.Parse("123456789012345678901234567890"), ((JValue)o["big"]).Value);
            Assert.AreEqual(9007199254740993L, ((JValue)o["long"]).Value, "A long past 2^53 is not rounded through a double.");
            Assert.AreEqual(JTokenType.Float, o["e"].Type);
            Assert.AreEqual(100000d, (double)o["e"]);
            Assert.AreEqual(JTokenType.String, o["s"].Type);
            Assert.AreEqual(JTokenType.String, o["t"].Type);
            Assert.IsTrue(double.IsNaN((double)o["nan"]));
            Assert.Throws<JsonReaderException>(() => OWJson.ParseObject("{\"x\":1.5.5}"));
        }

        [Test]
        public void ParseObject_FindsEachNumber_AcrossLineBreaksStringsAndComments()
        {
            // The reader finds each number's text where Newtonsoft stopped reading it, so every way a line can end,
            // and a string or a comment that holds line breaks and digits of its own, is crossed here.
            var values = Numbers.Take(400).ToArray();
            var breaks = new[] { "\n", "\r\n", "\r", " ", "\t", "\n\n", "\r\r\n" };
            var text = new StringBuilder("{\"s\":\"a 1.5\\n line\",\r\n\"raw\":\"two\nlines\r\n9.75 \",\"xs\":[");
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(breaks[i % breaks.Length]);
                if (i % 50 == 7) text.Append("/* 3.25,\n 1e5 */");
                if (i % 50 == 21) text.Append("{\"y\":").Append(OWExactDoubleOracle.Python(values[i])).Append('}');
                else text.Append(OWExactDoubleOracle.EcmaScript(values[i]));
            }

            text.Append("]}");
            var xs = (JArray)OWJson.ParseObject(text.ToString())["xs"];
            var wrong = new List<string>();
            for (var i = 0; i < values.Length; i++)
            {
                var token = i % 50 == 21 ? xs[i]["y"] : xs[i];
                if (!Same(AsDouble(token), values[i])) wrong.Add(i + ": " + Show(values[i]) + " read as " + token);
            }

            NoneWrong(wrong, values.Length, "numbers across lines");
        }

        [Test]
        public void ParseObject_ReadsAWholeArrayOnOneLongLine()
        {
            // One line past every buffer Newtonsoft keeps (its buffer shifts as it reads).
            var body = "{\"xs\":[" + string.Join(",", Numbers.Select(OWExactDoubleOracle.EcmaScript)) + "]}";
            var xs = (JArray)OWJson.ParseObject(body)["xs"];
            var wrong = new List<string>();
            for (var i = 0; i < Numbers.Length; i++)
            {
                if (!Same(AsDouble(xs[i]), Numbers[i])) wrong.Add(i + ": " + Show(Numbers[i]));
            }

            NoneWrong(wrong, Numbers.Length, "one long line");
        }

        [Test]
        public void Deserialize_ReadsExtensionDoublesExactly_AndSerializeWritesThemBack()
        {
            var values = Numbers.Take(2000).ToArray();
            var body = "{\"id\":\"019a0000-0000-7000-8000-000000000001\",\"name\":\"K\",\"x_tally\":["
                + string.Join(",", values.Select(OWExactDoubleOracle.EcmaScript)) + "],\"x_bearing\":263.2953629956518}";

            var character = OWJson.Deserialize<OWCharacter>(body);
            var back = OWJson.ParseObject(OWJson.Serialize(character));
            var written = OWJson.Serialize(character);

            var wrong = new List<string>();
            var tally = (JArray)back["x_tally"];
            for (var i = 0; i < values.Length; i++)
            {
                if (!Same(AsDouble(tally[i]), values[i])) wrong.Add(i + ": " + Show(values[i]));
            }

            NoneWrong(wrong, values.Length, "an element's x_ doubles through Deserialize and Serialize");
            StringAssert.Contains("\"x_bearing\":263.2953629956518", written);
            StringAssert.Contains("\"x_tally\":[" + string.Join(",", values.Select(OWExactDoubleOracle.EcmaScript)) + "]", written,
                "An element read and written back carries JSON.stringify's bytes.");
        }

        [Test]
        public void Deserialize_ReadsTypedDoublesExactly()
        {
            var wrong = new List<string>();
            foreach (var v in Numbers.Take(4000))
            {
                var text = OWExactDoubleOracle.EcmaScript(v);
                if (!Same(OWJson.Deserialize<double>(text), v)) wrong.Add("double " + text);
                if (!Same(OWJson.Deserialize<Typed>("{\"W\":" + text + ",\"N\":" + text + "}").N.Value, v)) wrong.Add("typed " + text);
                if (!Same(OWJson.Deserialize<Dictionary<string, double>>("{\"a\":" + text + "}")["a"], v)) wrong.Add("dict " + text);
            }

            NoneWrong(wrong, 12000, "typed reads");
        }

        // -- The client -------------------------------------------------------

        private static OWClient Make(FakeTransport t) => new OWClient(new OWClientConfig
        {
            ApiKey = "ow_w_test",
            ApiPin = "0000",
            Transport = t,
            BaseUrl = "https://example.test/api/v2",
        });

        [Test]
        public async Task Client_SendsJsonStringifysBytes_AndReadsTheAnswerExactly()
        {
            var values = Numbers.Take(2000).ToArray();
            var expectedArray = "[" + string.Join(",", values.Select(OWExactDoubleOracle.EcmaScript)) + "]";
            var t = new FakeTransport
            {
                ResponseBody = "{\"id\":\"x\",\"name\":\"K\",\"x_tally\":" + expectedArray + "}",
            };

            var answer = await Make(t).PatchAsync<JObject>("character", "x", new JObject
            {
                ["name"] = "K",
                ["x_tally"] = new JArray(values.Cast<object>().ToArray()),
            });

            StringAssert.Contains("\"x_tally\":[" + string.Join(",", values.Select(OWExactDoubleOracle.Wire)) + "]", t.Last.Body, "the request body, byte for byte");

            var wrong = new List<string>();
            var tally = (JArray)answer["x_tally"];
            for (var i = 0; i < values.Length; i++)
            {
                if (!Same(AsDouble(tally[i]), values[i])) wrong.Add(i + ": " + Show(values[i]));
            }

            NoneWrong(wrong, values.Length, "the client's answer");
        }

        [Test]
        public void Element_UnityMirror_KeepsExtensionDoublesExactly()
        {
            var values = Numbers.Take(500).ToArray();
            var character = OWJson.Deserialize<OWCharacter>(
                "{\"id\":\"x\",\"name\":\"K\",\"x_tally\":[" + string.Join(",", values.Select(OWExactDoubleOracle.EcmaScript)) + "]}");

            var raw = character.Extensions.Single(f => f.Key == "x_tally").RawJson;
            Assert.AreEqual("[" + string.Join(",", values.Select(OWExactDoubleOracle.EcmaScript)) + "]", raw,
                "An extension's raw JSON is JSON.stringify's bytes.");

            var receiver = (UnityEngine.ISerializationCallbackReceiver)character;
            receiver.OnBeforeSerialize();
            receiver.OnAfterDeserialize();
            var after = (JArray)OWJson.ParseObject(OWJson.Serialize(character))["x_tally"];
            var wrong = new List<string>();
            for (var i = 0; i < values.Length; i++)
            {
                if (!Same(AsDouble(after[i]), values[i])) wrong.Add(i + ": " + Show(values[i]));
            }

            NoneWrong(wrong, values.Length, "through the Unity mirror");
        }
    }
}
