using UnityEditor;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Per-developer connection settings for the editor tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored in <see cref="EditorPrefs"/> -- per-machine, outside the project, never in version
    /// control. A key committed to a repo is a key leaked, and a ScriptableObject in
    /// <c>Assets/</c> is exactly how that happens by accident.
    /// </para>
    /// <para>
    /// EditorPrefs is not a secret store -- it is plaintext in the registry or a plist. That is
    /// acceptable for a world key on a developer's own machine and NOT acceptable for anything
    /// else. Shipped game code takes its key from the game's own config, never from here; this type
    /// is Editor-only by assembly.
    /// </para>
    /// </remarks>
    public static class OWEditorSettings
    {
        private const string Prefix = "OnlyWorlds.Sdk.";
        private const string KeyPref = Prefix + "ApiKey";
        private const string PinPref = Prefix + "ApiPin";
        private const string BaseUrlPref = Prefix + "BaseUrl";

        public const string DefaultBaseUrl = "https://www.onlyworlds.com/api/v2";

        public static string ApiKey
        {
            get => EditorPrefs.GetString(KeyPref, string.Empty);
            set => EditorPrefs.SetString(KeyPref, value ?? string.Empty);
        }

        public static string ApiPin
        {
            get => EditorPrefs.GetString(PinPref, string.Empty);
            set => EditorPrefs.SetString(PinPref, value ?? string.Empty);
        }

        public static string BaseUrl
        {
            get => EditorPrefs.GetString(BaseUrlPref, DefaultBaseUrl);
            set => EditorPrefs.SetString(BaseUrlPref, string.IsNullOrEmpty(value) ? DefaultBaseUrl : value);
        }

        public static OWKeyKind KeyKind => OWKey.DetectKind(ApiKey);

        /// <summary>True when there is enough to attempt a connection.</summary>
        public static bool IsConfigured
        {
            get
            {
                if (string.IsNullOrEmpty(ApiKey)) return false;
                var kind = KeyKind;
                if (kind == OWKeyKind.Unknown) return false;
                return !OWKey.RequiresPin(kind) || !string.IsNullOrEmpty(ApiPin);
            }
        }

        /// <summary>Why the current settings are unusable, or null when they are fine.</summary>
        public static string ValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(ApiKey)) return "No API key set.";

                var kind = KeyKind;
                if (kind == OWKeyKind.Unknown)
                {
                    return "Key not recognised. Expected an ow_w_ / ow_r_ / ow_a_ prefix, "
                           + "or a 10-digit legacy key.";
                }

                if (OWKey.RequiresPin(kind) && string.IsNullOrEmpty(ApiPin))
                {
                    return $"A {kind} key requires a PIN.";
                }

                return null;
            }
        }

        public static OWClient CreateClient()
        {
            return new OWClient(new OWClientConfig
            {
                ApiKey = ApiKey,
                ApiPin = ApiPin,
                BaseUrl = BaseUrl,
                Transport = new UnityWebRequestTransport(),
            });
        }

        public static void Clear()
        {
            EditorPrefs.DeleteKey(KeyPref);
            EditorPrefs.DeleteKey(PinPref);
            EditorPrefs.DeleteKey(BaseUrlPref);
        }
    }
}
