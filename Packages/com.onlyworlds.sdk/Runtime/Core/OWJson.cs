using Newtonsoft.Json;

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

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

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
