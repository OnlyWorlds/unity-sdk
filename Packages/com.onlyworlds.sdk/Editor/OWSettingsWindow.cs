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
            window.minSize = new Vector2(430f, 260f);
            window.maxSize = new Vector2(430f, 260f);
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
                    ? "Read-only key -- no PIN needed, and writes will be refused."
                    : $"Detected: {kind} key.";
                EditorGUILayout.LabelField(" ", note, EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6f);
            _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Stored per-machine in EditorPrefs -- never written into the project or version "
                + "control. Shipped game code should take its key from the game's own config, not "
                + "from here.",
                MessageType.None);

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

            OWEditorAsync.Run(
                client.GetWorldAsync(),
                world =>
                {
                    _busy = false;
                    _testResult = $"Connected: {world["name"]}";
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
