using System;
using UnityEditor;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Per-developer, per-project connection settings for the editor tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored in <see cref="EditorPrefs"/> -- per-machine, outside the project, never in version
    /// control. A key committed to a repo is a key leaked, and a ScriptableObject in
    /// <c>Assets/</c> is exactly how that happens by accident.
    /// </para>
    /// <para>
    /// Scoped to the project by its <see cref="PlayerSettings.productGUID"/>: EditorPrefs is shared
    /// by every project on the machine, so one unscoped key served every project, and a key rotated
    /// for one world went stale in all the others. Clones and worktrees of one project share its GUID,
    /// and so its key. Versions before this kept one machine-wide key; it is left in place and can be
    /// adopted per project (<see cref="AdoptMachineWide"/>), never read silently.
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
        private const string KeyName = "ApiKey";
        private const string PinName = "ApiPin";
        private const string BaseUrlName = "BaseUrl";

        public const string DefaultBaseUrl = "https://www.onlyworlds.com/api/v2";

        /// <summary>The EditorPrefs key for one setting of one project.</summary>
        public static string PrefKey(Guid project, string name) => $"{Prefix}{project:N}.{name}";

        /// <summary>The machine-wide EditorPrefs key that versions before per-project scoping used.</summary>
        public static string MachineWidePrefKey(string name) => Prefix + name;

        private static string Scoped(string name) => PrefKey(PlayerSettings.productGUID, name);

        public static string ApiKey
        {
            get => EditorPrefs.GetString(Scoped(KeyName), string.Empty);
            set => EditorPrefs.SetString(Scoped(KeyName), value ?? string.Empty);
        }

        public static string ApiPin
        {
            get => EditorPrefs.GetString(Scoped(PinName), string.Empty);
            set => EditorPrefs.SetString(Scoped(PinName), value ?? string.Empty);
        }

        public static string BaseUrl
        {
            get => EditorPrefs.GetString(Scoped(BaseUrlName), DefaultBaseUrl);
            set => EditorPrefs.SetString(Scoped(BaseUrlName), string.IsNullOrEmpty(value) ? DefaultBaseUrl : value);
        }

        public static OWKeyKind KeyKind => OWKey.DetectKind(ApiKey);

        /// <summary>True when there is enough to attempt a connection.</summary>
        /// <remarks>
        /// A PIN is never needed to connect: prefixed keys read without one, and a legacy key needs one
        /// only for writes or a private world. Demanding it here locked the demo keys out of the
        /// browser while they worked in code. A missing PIN surfaces as the server's own 401.
        /// </remarks>
        public static bool IsConfigured =>
            !string.IsNullOrEmpty(ApiKey) && KeyKind != OWKeyKind.Unknown;

        /// <summary>Why the current settings are unusable, or null when they are fine.</summary>
        public static string ValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(ApiKey)) return "No API key set for this project.";

                var kind = KeyKind;
                if (kind == OWKeyKind.Unknown)
                {
                    return "Key not recognised. Expected an ow_w_ / ow_r_ / ow_a_ prefix, "
                           + "or a 10-digit legacy key.";
                }

                return null;
            }
        }

        /// <summary>
        /// True when this project has no key of its own and an earlier version left a machine-wide one.
        /// </summary>
        public static bool CanAdoptMachineWide =>
            string.IsNullOrEmpty(ApiKey)
            && !string.IsNullOrEmpty(EditorPrefs.GetString(MachineWidePrefKey(KeyName), string.Empty));

        /// <summary>
        /// Copies the machine-wide settings an earlier version saved into this project. The
        /// machine-wide copy stays, for the other projects that have not adopted it yet.
        /// </summary>
        public static void AdoptMachineWide()
        {
            foreach (var name in new[] { KeyName, PinName, BaseUrlName })
            {
                var legacy = MachineWidePrefKey(name);
                if (EditorPrefs.HasKey(legacy))
                {
                    EditorPrefs.SetString(Scoped(name), EditorPrefs.GetString(legacy, string.Empty));
                }
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

        /// <summary>Clears this project's settings. Other projects and the machine-wide copy keep theirs.</summary>
        public static void Clear()
        {
            EditorPrefs.DeleteKey(Scoped(KeyName));
            EditorPrefs.DeleteKey(Scoped(PinName));
            EditorPrefs.DeleteKey(Scoped(BaseUrlName));
        }
    }
}
