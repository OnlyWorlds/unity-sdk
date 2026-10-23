using UnityEditor;
using UnityEngine;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>Connection settings for the OnlyWorlds editor tools.</summary>
    public class OWSettingsWindow : EditorWindow
    {
        private string _key;
        private string _pin;
        private string _baseUrl;
        private string _testResult;
        private bool _busy;
        private bool _revealKey;

        public static void Open()
        {
            var window = GetWindow<OWSettingsWindow>(true, "OnlyWorlds Settings");
            window.minSize = new Vector2(430f, 280f);
            window.maxSize = new Vector2(430f, 280f);
            window.Load();
            window.ShowUtility();
        }

        private void Load()
        {
            _key = OWEditorSettings.ApiKey;
            _pin = OWEditorSettings.ApiPin;
            _baseUrl = OWEditorSettings.BaseUrl;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Credentials", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            // Masked by default: these windows get screen-shared and screenshotted, and a key on
            // screen is a key leaked.
            _key = _revealKey
                ? EditorGUILayout.TextField("API Key", _key)
                : EditorGUILayout.PasswordField("API Key", _key);
            _revealKey = GUILayout.Toggle(_revealKey, "show", EditorStyles.miniButton, GUILayout.Width(46f));
            EditorGUILayout.EndHorizontal();

            var kind = OWKey.DetectKind(_key);

            using (new EditorGUI.DisabledScope(!OWKey.RequiresPin(kind)))
            {
                _pin = EditorGUILayout.PasswordField("API PIN", _pin);
            }

            if (!string.IsNullOrEmpty(_key))
            {
                var note = kind == OWKeyKind.Read
                    ? "Read-only key: no PIN, and writes will be refused."
                    : kind == OWKeyKind.Legacy
                        ? "Legacy key: reads a public world without a PIN; writes and private worlds need one."
                        : kind == OWKeyKind.Write
                            ? "Write key: reads without a PIN; writes need one (a seat's secret, or the account PIN)."
                            : $"Detected: {kind} key.";
                // Wrapped: the window is a fixed 430 wide, and the write key's note ran past its edge.
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUIUtility.labelWidth + 3f);
                GUILayout.Label(note, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6f);
            _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Stored for this project in EditorPrefs, on this machine -- never written into the "
                + "project or version control. Shipped game code should take its key from the game's "
                + "own config, not from here.",
                MessageType.None);

            if (OWEditorSettings.CanAdoptMachineWide && string.IsNullOrEmpty(_key))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("An earlier version saved a key for every project.",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("Use it here", EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    OWEditorSettings.AdoptMachineWide();
                    Load();
                    _testResult = "Saved.";
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save"))
            {
                OWEditorSettings.ApiKey = _key;
                OWEditorSettings.ApiPin = _pin;
                OWEditorSettings.BaseUrl = _baseUrl;
                _testResult = "Saved.";
            }

            using (new EditorGUI.DisabledScope(_busy || string.IsNullOrEmpty(_key)))
            {
                if (GUILayout.Button("Test Connection"))
                {
                    TestConnection();
                }
            }

            if (GUILayout.Button("Clear"))
            {
                OWEditorSettings.Clear();
                Load();
                _testResult = "Cleared.";
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_testResult))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(_testResult,
                    _testResult.StartsWith("Connected") || _testResult == "Saved."
                        ? MessageType.Info
                        : MessageType.Warning);
            }
        }

        private void TestConnection()
        {
            // Test what is on screen, not what was last saved -- otherwise a user edits the key,
            // hits Test, and gets a result for the old one.
            var client = new OWClient(new OWClientConfig
            {
                ApiKey = _key,
                ApiPin = _pin,
                BaseUrl = _baseUrl,
                Transport = new UnityWebRequestTransport(),
            });

            _busy = true;
            _testResult = "Testing...";
            Repaint();

            // A key that takes a PIN is tested through GET /me, the one read the client sends the PIN on (keel #314):
            // a world read goes without it now, and would report a wrong PIN as connected.
            var viaMe = OWKey.RequiresPin(client.KeyKind);

            OWEditorAsync.Run(
                viaMe ? client.GetMeAsync() : client.GetWorldAsync(),
                answer =>
                {
                    _busy = false;
                    _testResult = $"Connected: {(viaMe ? answer["world"]?["name"] : answer["name"])}";
                    Repaint();
                },
                error =>
                {
                    _busy = false;
                    _testResult = error is OWApiError api
                        ? $"Failed: {api.StatusCode} {api.Code}"
                        : $"Failed: {error.Message}";
                    Repaint();
                });
        }
    }
}
