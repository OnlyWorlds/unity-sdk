using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The SDK's single JSON configuration. Every read and write goes through these settings.
    /// </summary>
    public static class OWJson
    {
        /// <summary>
        /// Serializer settings for the OnlyWorlds v2 wire format.
        /// </summary>
        /// <remarks>
        /// <c>NullValueHandling.Include</c> is deliberate and load-bearing: an unset nullable field
        /// must reach the wire as explicit <c>null</c>. Switching this to Ignore would turn every
        /// "the author unset this" into "leave it alone", silently.
        /// </remarks>
        public static JsonSerializerSettings Settings => BuildSettings();

        /// <summary>
        /// A fresh settings instance per call. Deliberately NOT a shared mutable static.
        /// </summary>
        /// <remarks>
        /// A static settings object with a mutable Converters collection is a live hazard: any
        /// consumer inserting a converter at index 0 wins over ours, and an int converter placed
        /// there turns unset into <c>{"level":0}</c> -- the exact null-to-zero collapse this whole
        /// type exists to prevent, silently, for every caller in the process.
        /// </remarks>
        private static JsonSerializerSettings BuildSettings() => new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,

            // Pinned explicitly. DefaultValueHandling.Ignore would OMIT unset fields entirely --
            // the converter never even runs -- and omission on a destructive PATCH means the
            // user's deliberate clear is silently dropped. Pinning NullValueHandling alone does
            // not protect the write path; this is the other half of the same guarantee.
            DefaultValueHandling = DefaultValueHandling.Include,

            // MissingMemberHandling stays at its default (Ignore) ONLY because unmodeled fields are
            // captured by the extensions bag via [JsonExtensionData] on the element base -- they are
            // not dropped. The March build set Ignore with no extensions bag, which silently
            // destroyed other tools' x_* state on every write-back. If the extensions bag is ever
            // removed, this line becomes a data-loss bug.
            MissingMemberHandling = MissingMemberHandling.Ignore,

            DateParseHandling = DateParseHandling.None,
            Converters = { new SerializableNullableConverterFactory() },
        };

        /// <summary>
        /// Serializes with <see cref="Settings"/>, as <c>JsonConvert.SerializeObject</c> does, except that every
        /// finite double and float is written as <c>JSON.stringify</c> writes it.
        /// </summary>
        /// <remarks>
        /// Newtonsoft's own spelling is the runtime's <c>"R"</c>, and under Unity's Mono that writes seventeen digits
        /// where sixteen read back (<c>0.33333333333333331</c>) and some strings that an exact reader (keel, npm) reads
        /// as the neighbouring double (<c>51.672104437815896</c> for <c>51.6721044378159</c>). An integral double is
        /// written without <c>.0</c> (<c>1</c>) and both zeros as <c>0</c>, as <c>JSON.stringify</c> writes them;
        /// integers, strings, NaN and the infinities are written as before.
        /// </remarks>
        public static string Serialize(object value)
        {
            var serializer = JsonSerializer.CreateDefault(Settings);
            var buffer = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var writer = new OWJsNumberWriter(buffer))
            {
                writer.Formatting = serializer.Formatting;
                serializer.Serialize(writer, value, null);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// A token as compact JSON text, as <c>token.ToString(Formatting.None)</c> writes it, except that every finite
        /// double and float is written as <c>JSON.stringify</c> writes it. Every request body, and every text the SDK
        /// keeps and reads back (the cache, an extension's raw JSON), is written through this.
        /// </summary>
        /// <remarks>
        /// Writer and reader go together: once <see cref="ParseObject"/> reads exactly, a text written with the
        /// runtime's <c>"R"</c> would no longer always read back as the double it came from.
        /// </remarks>
        internal static string Write(JToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            var buffer = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new OWJsNumberWriter(buffer) { Formatting = Formatting.None })
            {
                token.WriteTo(writer);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Parses JSON text into a <see cref="JObject"/> <b>without coercing any value</b>. Use this,
        /// never <c>JObject.Parse</c>, for anything that can be written back or cached as text.
        /// </summary>
        /// <remarks>
        /// <c>JObject.Parse</c> defaults <c>DateParseHandling</c> to <c>DateTime</c>: an ISO-8601
        /// string such as <c>"2026-09-04T20:36:14.605251+00:00"</c> comes back as a DateTime token
        /// and re-serializes in the machine's local zone (<c>…T22:36:14.605251+02:00</c> here).
        /// Same instant, different bytes, and a whole-repository diff whose content depends on
        /// which machine ran the write. Measured 2026-09-07 across all 4,769 files of a real world.
        /// Large integers stay integers (<c>FloatParseHandling.Double</c>) for the same reason.
        /// <para>
        /// A number with a fraction or an exponent is read to the nearest double, ties to even, exactly, on every
        /// runtime (<see cref="OWExactReader"/>): Newtonsoft reads it with the runtime's <c>double.TryParse</c>, and
        /// Unity's Mono misreads ordinary numbers that keel and npm write (<c>263.2953629956518</c>, one step off).
        /// </para>
        /// </remarks>
        public static JObject ParseObject(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            using (var reader = new OWExactReader(text))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                return JObject.Load(reader);
            }
        }

        /// <summary>
        /// Deserializes with <see cref="Settings"/>, as <c>JsonConvert.DeserializeObject</c> does (content after the
        /// value is refused), reading every double exactly as <see cref="ParseObject"/> does: an element's
        /// <c>x_</c> doubles as much as a typed field.
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var serializer = JsonSerializer.CreateDefault(Settings);
            serializer.CheckAdditionalContent = true;
            using (var reader = new OWExactReader(json))
            {
                return serializer.Deserialize<T>(reader);
            }
        }

        /// <summary>
        /// <c>JToken.Parse</c> with its own settings (an ISO-8601 string still becomes a date token, as it did at the
        /// one caller), reading every double exactly.
        /// </summary>
        internal static JToken ParseToken(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            using (var reader = new OWExactReader(text))
            {
                var token = JToken.ReadFrom(reader);

                // Any content here other than a comment throws in the reader, as in JToken.Parse.
                while (reader.Read())
                {
                }

                return token;
            }
        }

        /// <summary>A reader over <paramref name="text"/> that reads every double exactly, for a caller with its own settings.</summary>
        internal static JsonTextReader Reader(string text) => new OWExactReader(text);

        /// <summary>
        /// A live, statically-typed reference to the closed generic forms the schema uses, so the
        /// IL2CPP linker cannot strip them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This must be a REACHABLE static field, not an uncalled method. An uncalled
        /// <c>internal static void AotHint()</c> -- the first attempt -- roots nothing; the linker
        /// removes it along with everything it referenced. A static field initialized in a type
        /// that is itself constructed on every serialize call is genuinely reachable.
        /// </para>
        /// <para>
        /// The schema's 70 nullable scalars are all integers today. A nullable float or bool would
        /// need its own entry here, and would fail ONLY on an IL2CPP build (iOS/Android/WebGL) --
        /// never in the editor, which is what makes it worth pinning deliberately.
        /// </para>
        /// <para>
        /// UNVALIDATED against a real IL2CPP build. Verify before shipping to a stripped platform.
        /// </para>
        /// </remarks>
        [UnityEngine.Scripting.Preserve]
        private static readonly SerializableNullableConverter<int> AotIntConverter =
            new SerializableNullableConverter<int>();

        /// <summary>Prevents the AOT anchor from being considered unused.</summary>
        [UnityEngine.Scripting.Preserve]
        internal static object AotAnchor => AotIntConverter;
    }
}
