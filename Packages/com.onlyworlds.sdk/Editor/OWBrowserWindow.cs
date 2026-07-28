using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Browse an OnlyWorlds world from inside Unity: types, elements, detail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The March plugin's three-panel design, kept wholesale. The March MISTAKE was not building a
    /// viewer -- it was building a viewer with no foundation underneath. This one sits on the
    /// client, the models and the cache, all of which are tested independently of it.
    /// </para>
    /// <para>
    /// Every network call goes through <see cref="OWEditorAsync"/>. Nothing here ever blocks the
    /// update loop -- see that type for the deadlock this avoids.
    /// </para>
    /// </remarks>
    public class OWBrowserWindow : EditorWindow
    {
        private const float TypePanelWidth = 150f;
        private const float ListPanelWidth = 260f;

        private static readonly string[] ElementTypes =
        {
            "ability", "character", "collective", "construct", "creature", "event", "family",
            "institution", "language", "law", "location", "map", "marker", "narrative", "object",
            "phenomenon", "pin", "relation", "species", "title", "trait", "zone",
        };

        private string _selectedType;
        private JObject _selectedElement;
        private readonly List<JObject> _elements = new List<JObject>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        private Vector2 _typeScroll, _listScroll, _detailScroll;
        private string _status = "Not connected.";
        private bool _busy;
        private string _filter = string.Empty;
        private string _worldName;
        private string _worldId;

        private OWWorldCache _cache;
        private bool _useCache;

        [MenuItem("Window/OnlyWorlds/World Browser")]
        public static void Open()
        {
            var window = GetWindow<OWBrowserWindow>("OnlyWorlds");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!OWEditorSettings.IsConfigured)
            {
                DrawSetupPrompt();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawTypePanel();
            DrawListPanel();
            DrawDetailPanel();
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(_busy || !OWEditorSettings.IsConfigured))
            {
                if (GUILayout.Button("Connect", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    Connect();
                }
            }

            using (new EditorGUI.DisabledScope(_busy || string.IsNullOrEmpty(_worldId)))
            {
                if (GUILayout.Button("Sync to Cache", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    SyncToCache();
                }
            }

            using (new EditorGUI.DisabledScope(_cache == null))
            {
                var wanted = GUILayout.Toggle(_useCache, "Offline", EditorStyles.toolbarButton, GUILayout.Width(58f));
                if (wanted != _useCache)
                {
                    _useCache = wanted;
                    if (_selectedType != null) SelectType(_selectedType);
                }
            }

            GUILayout.Space(8f);
            if (!string.IsNullOrEmpty(_worldName))
            {
                GUILayout.Label(_worldName, EditorStyles.miniBoldLabel);
            }

            if (_cache != null)
            {
                GUILayout.Label($"| cache: {_cache.Count} @ seq {_cache.Cursor}", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Settings", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                OWSettingsWindow.Open();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSetupPrompt()
        {
            EditorGUILayout.Space(20f);
            EditorGUILayout.HelpBox(
                OWEditorSettings.ValidationMessage ?? "Not configured.",
                MessageType.Info);

            if (GUILayout.Button("Open Settings", GUILayout.Height(28f)))
            {
                OWSettingsWindow.Open();
            }
        }

        private void DrawTypePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(TypePanelWidth));
            _typeScroll = EditorGUILayout.BeginScrollView(_typeScroll);

            var dark = EditorGUIUtility.isProSkin;

            foreach (var type in ElementTypes)
            {
                var label = _counts.TryGetValue(type, out var n) ? $"{type} ({n})" : type;
                var selected = type == _selectedType;

                EditorGUILayout.BeginHorizontal();

                // A family swatch, NOT a per-type colour. Colour carries the family; the icon and
                // label carry the type. Four families cannot distinguish 22 types and were never
                // meant to -- the label beside this swatch is doing that work.
                var swatch = GUILayoutUtility.GetRect(4f, EditorGUIUtility.singleLineHeight,
                    GUILayout.Width(4f), GUILayout.ExpandWidth(false));
                EditorGUI.DrawRect(swatch, OWPresentation.ColorFor(type, dark));

                if (GUILayout.Toggle(selected, label, EditorStyles.miniButton) && !selected)
                {
                    SelectType(type);
                }

                EditorGUILayout.EndHorizontal();
            }

            // The legend, in the palette's published order -- that order IS the CVD-safety
            // mechanism, so it is preserved rather than sorted alphabetically.
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Families", EditorStyles.miniBoldLabel);

            foreach (var family in OWPresentation.FamilyOrder)
            {
                EditorGUILayout.BeginHorizontal();
                var swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.ExpandWidth(false));
                EditorGUI.DrawRect(swatch, OWPresentation.ColorOfFamily(family)?.For(dark) ?? Color.grey);
                GUILayout.Label(family, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawListPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListPanelWidth));

            if (_selectedType == null)
            {
                EditorGUILayout.LabelField("Select a type.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            var shown = 0;
            foreach (var element in _elements)
            {
                var name = element["name"]?.ToString() ?? "(unnamed)";

                if (!string.IsNullOrEmpty(_filter) &&
                    name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                shown++;
                var selected = ReferenceEquals(element, _selectedElement);
                if (GUILayout.Toggle(selected, name, EditorStyles.miniButton) && !selected)
                {
                    _selectedElement = element;
                    GUI.FocusControl(null);
                }
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField(
                    _elements.Count == 0 ? "Nothing loaded." : "No matches.",
                    EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetailPanel()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedElement == null)
            {
                EditorGUILayout.LabelField("Select an element.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            EditorGUILayout.LabelField(
                _selectedElement["name"]?.ToString() ?? "(unnamed)",
                EditorStyles.boldLabel);

            EditorGUILayout.Space(4f);

            foreach (var property in _selectedElement.Properties())
            {
                DrawField(property.Name, property.Value);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static void DrawField(string name, JToken value)
        {
            // The whole reason SerializableNullable exists, surfaced in the UI: an unset field
            // must read as unset. Rendering null as "0" here would relearn the lie one layer up.
            if (value == null || value.Type == JTokenType.Null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField(name, "--");
                }

                return;
            }

            if (value.Type == JTokenType.Array)
            {
                var array = (JArray)value;
                if (array.Count == 0)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.LabelField(name, "(empty)");
                    }

                    return;
                }

                EditorGUILayout.LabelField($"{name} ({array.Count})", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                foreach (var item in array)
                {
                    EditorGUILayout.SelectableLabel(item.ToString(),
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                EditorGUI.indentLevel--;
                return;
            }

            var text = value.ToString();
            if (text.Length > 60)
            {
                EditorGUILayout.LabelField(name, EditorStyles.miniBoldLabel);
                EditorGUILayout.SelectableLabel(text, EditorStyles.wordWrappedLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight * 3f));
                return;
            }

            EditorGUILayout.LabelField(name, text);
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_status, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (_busy) GUILayout.Label("working...", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        // -- Actions ----------------------------------------------------------

        private void Connect()
        {
            _busy = true;
            _status = "Connecting...";
            Repaint();

            OWEditorAsync.Run(
                OWEditorSettings.CreateClient().GetWorldAsync(),
                world =>
                {
                    _busy = false;
                    _worldName = world["name"]?.ToString() ?? "(unnamed world)";
                    _worldId = world["id"]?.ToString();
                    _status = $"Connected to {_worldName}.";

                    // Pick up an existing cache for THIS world so Offline is available straight
                    // away rather than only after a sync in the same session.
                    if (!string.IsNullOrEmpty(_worldId))
                    {
                        _cache = OWCacheAsset.Find(OWWorldKey.FromApi(_worldId, OWEditorSettings.BaseUrl));
                        if (_cache != null) _status += $" Cache: {_cache.Count} elements.";
                    }

                    // No counts here, deliberately. Checked the wire: neither GET /world nor the
                    // list envelope carries a total -- only {data, has_more, next_cursor}. A count
                    // per type would mean walking all 22 types on connect, which for a large world
                    // is a lot of traffic to populate a label. Counts appear when a type is
                    // actually loaded, and the label says so.
                    Repaint();
                },
                error =>
                {
                    _busy = false;
                    _worldName = null;
                    _status = Describe(error);
                    Repaint();
                });
        }

        private void SelectType(string type)
        {
            _selectedType = type;
            _selectedElement = null;
            _elements.Clear();

            if (_useCache && _cache != null)
            {
                // No network at all. This is the point of the cache: a game, or a designer on a
                // train, reads the world without the API being reachable.
                foreach (var element in _cache.AllRaw(type))
                {
                    _elements.Add(JObject.Parse(element));
                }

                _counts[type] = _elements.Count;
                _status = $"{_elements.Count} {type} (from cache).";
                Repaint();
                return;
            }

            _busy = true;
            _status = $"Loading {type}...";
            Repaint();

            OWEditorAsync.Run(
                OWEditorSettings.CreateClient().ListAllAsync<JObject>(
                    type,
                    onPage: (pageNumber, soFar) =>
                    {
                        // Runs on whatever thread the paging loop is on -- marshal before touching
                        // the window, or Repaint throws.
                        OWMainThread.Run(() =>
                        {
                            _status = $"Loading {type}... {soFar} so far (page {pageNumber})";
                            Repaint();
                        });
                    }),
                loaded =>
                {
                    _busy = false;
                    _elements.Clear();
                    _elements.AddRange(loaded);
                    _counts[type] = loaded.Count;
                    _status = $"{loaded.Count} {type}.";
                    Repaint();
                },
                error =>
                {
                    _busy = false;
                    _status = Describe(error);
                    Repaint();
                });
        }

        private void SyncToCache()
        {
            var key = OWWorldKey.FromApi(_worldId, OWEditorSettings.BaseUrl);
            _cache = OWCacheAsset.LoadOrCreate(key, _worldName);

            _busy = true;
            _status = "Syncing...";
            Repaint();

            OWEditorAsync.Run(
                OWSync.IncrementalAsync(OWEditorSettings.CreateClient(), _cache),
                result =>
                {
                    _busy = false;

                    // Without SaveAssets the next domain reload discards everything just fetched,
                    // which looks exactly like the sync having failed.
                    OWCacheAsset.Save(_cache);

                    _status = result.WasRebaselined
                        ? $"Rebaselined: {result.Fetched} elements at seq {result.Cursor}."
                        : result.ChangedAnything
                            ? $"Synced: +{result.Fetched} / -{result.Removed} at seq {result.Cursor}."
                            : $"Already current at seq {result.Cursor}.";

                    if (!string.IsNullOrEmpty(result.RebaselineReason))
                    {
                        Debug.Log($"[OnlyWorlds] {result.RebaselineReason}");
                    }

                    // Refresh the open type from the new cache contents rather than leaving a
                    // stale list on screen next to a fresh cursor.
                    if (_useCache && _selectedType != null) SelectType(_selectedType);
                    Repaint();
                },
                error =>
                {
                    _busy = false;
                    _status = Describe(error);
                    Repaint();
                });
        }

        /// <summary>
        /// Turns an exception into something a user can act on.
        /// </summary>
        /// <remarks>
        /// The typed error carries a doc URL and a param -- surfacing "invalid_link on field
        /// location" beats "request failed", which sends a developer reading their own code for a
        /// problem the server already diagnosed.
        /// </remarks>
        private static string Describe(System.Exception error)
        {
            if (error is OWApiError api)
            {
                var text = $"API {api.StatusCode}";
                if (!string.IsNullOrEmpty(api.Code)) text += $" [{api.Code}]";
                if (!string.IsNullOrEmpty(api.Param)) text += $" on {api.Param}";
                if (api.IsAuthError) text += " -- check the key and PIN in Settings.";
                return text;
            }

            if (error is OWTransportError) return "Network unreachable.";
            return error.Message;
        }
    }
}
