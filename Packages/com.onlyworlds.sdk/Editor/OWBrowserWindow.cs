using System.Collections.Generic;
using System.Linq;
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
        private const float TypePanelWidth = 172f; // fits "phenomenon (99)"; 150 cut it
        private const float ListPanelWidth = 260f;

        // One list, in the runtime assembly, guarded by a test. A second copy here drifted the
        // moment the standard grew a 23rd type, and the symptom -- "the browser does not show the
        // new type" -- sends the reader into UI code chasing a data problem.
        private static string[] ElementTypes => OWSync.ElementTypes;

        // Shown alphabetically (2026-10-09): in the standard's order the family colours sit in
        // four blocks, and the grouping reads as a claim the standard does not make. The colours
        // stay; the order scatters them.
        private static string[] _alphabetical;
        private static string[] TypesAlphabetical
        {
            get
            {
                if (_alphabetical == null || _alphabetical.Length != ElementTypes.Length)
                {
                    _alphabetical = (string[])ElementTypes.Clone();
                    System.Array.Sort(_alphabetical, System.StringComparer.Ordinal);
                }

                return _alphabetical;
            }
        }

        // The 22 type icons: Material Symbols glyphs (Apache 2.0) named in ow-presentation.json,
        // rendered white by tools/render_type_icons.py so the window can tint them.
        private const string IconFolder = "Packages/com.onlyworlds.sdk/Editor/Icons/";
        private static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        private const float RowHeight = 20f;
        private const float IconSize = 16f;

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

        // Every element this window has seen, by id: what turns a link field's id into a name
        // that opens. Filled from the cache and from each list loaded; an id it has not seen
        // stays an id, because guessing a name would be worse than showing the id.
        private readonly Dictionary<string, KnownElement> _known = new Dictionary<string, KnownElement>();
        private string _pendingId;

        private struct KnownElement
        {
            public string Type;
            public string Name;
        }

        [MenuItem("Window/OnlyWorlds/World Browser")]
        public static void Open()
        {
            var window = GetWindow<OWBrowserWindow>("OnlyWorlds");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true; // the rows' hover state
            if (_cache != null) IndexCache();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove) Repaint();

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

            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button("Open Folder...", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                {
                    OpenFolderWorld();
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

            // Colour carries the family, the icon and the label carry the type: four colours
            // cannot tell 22 types apart and were never meant to. No family legend (2026-10-09):
            // the colours stay, the grouping is not exposed.
            foreach (var type in TypesAlphabetical)
            {
                var count = _counts.TryGetValue(type, out var n) ? n.ToString() : null;
                if (Row(type, type == _selectedType, IconFor(type), OWPresentation.ColorFor(type, dark), count)
                    && type != _selectedType)
                {
                    _pendingId = null;
                    SelectType(type);
                }
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
                if (Row(name, selected) && !selected)
                {
                    _selectedElement = element;
                    _detailScroll = Vector2.zero;
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

            DrawHeader(_selectedElement);
            EditorGUILayout.Space(4f);

            foreach (var property in _selectedElement.Properties())
            {
                DrawField(property.Name, property.Value);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>The element's name beside its type icon, and what kind of thing it is.</summary>
        private void DrawHeader(JObject element)
        {
            var type = element["type"]?.ToString() ?? _selectedType;
            var kind = string.Join(" · ", new[] { type, element["supertype"]?.ToString(), element["subtype"]?.ToString() }
                .Where(s => !string.IsNullOrEmpty(s)));

            var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.label, GUILayout.Height(40f), GUILayout.ExpandWidth(true));
            var icon = IconFor(type);
            if (icon != null)
            {
                var old = GUI.color;
                GUI.color = OWPresentation.ColorFor(type, EditorGUIUtility.isProSkin);
                GUI.DrawTexture(new Rect(rect.x + 4f, rect.y + 6f, 28f, 28f), icon, ScaleMode.ScaleToFit);
                GUI.color = old;
            }

            var x = rect.x + (icon != null ? 40f : 4f);
            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, 20f), element["name"]?.ToString() ?? "(unnamed)", HeaderStyle);
            GUI.Label(new Rect(x, rect.y + 21f, rect.width - x, 16f), kind, EditorStyles.miniLabel);
        }

        private void DrawField(string name, JToken value)
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
                    var id = item.ToString();
                    if (_known.TryGetValue(id, out var linked))
                    {
                        DrawLink(id, linked);
                        continue;
                    }

                    EditorGUILayout.SelectableLabel(id,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                EditorGUI.indentLevel--;
                return;
            }

            var text = value.ToString();

            // An empty string is set, not unset, so it keeps its own mark; dimmed like "--" so a
            // column of empty fields does not read as a column of headings.
            if (value.Type == JTokenType.String && text.Length == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField(name, "(empty)");
                }

                return;
            }

            // A link: an id this window has seen, under any field but the element's own id.
            if (name != "id" && value.Type == JTokenType.String && _known.TryGetValue(text, out var target))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(name);
                DrawLink(text, target);
                EditorGUILayout.EndHorizontal();
                return;
            }
            if (text.Length > 60)
            {
                EditorGUILayout.LabelField(name, EditorStyles.miniBoldLabel);
                // Sized to the text at the detail panel's width: a fixed three lines cut long
                // descriptions off mid-sentence.
                var width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - TypePanelWidth - ListPanelWidth - 40f);
                var height = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(text), width);
                EditorGUILayout.SelectableLabel(text, EditorStyles.wordWrappedLabel, GUILayout.Height(height));
                return;
            }

            EditorGUILayout.LabelField(name, text);
        }

        // -- Drawing helpers --------------------------------------------------

        private static GUIStyle _headerStyle, _selectedLabel, _countLabel;

        private static GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

        private static GUIStyle SelectedLabel => _selectedLabel ??= new GUIStyle(EditorStyles.label)
        {
            normal = { textColor = Color.white },
        };

        private static GUIStyle CountLabel => _countLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
        };

        // Unity's own selection blue and a faint hover, per skin, so the rows read as an editor list.
        private static Color SelectedFill => EditorGUIUtility.isProSkin
            ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);

        private static Color HoverFill => EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.06f);

        /// <summary>A selectable list row: hover, selection, an optional tinted icon and a count. True on click.</summary>
        private static bool Row(string label, bool selected, Texture2D icon = null, Color? tint = null, string count = null)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.label,
                GUILayout.Height(RowHeight), GUILayout.ExpandWidth(true));
            var e = Event.current;
            var hover = rect.Contains(e.mousePosition);

            if (e.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(rect, SelectedFill);
                else if (hover) EditorGUI.DrawRect(rect, HoverFill);

                var x = rect.x + 6f;
                if (icon != null)
                {
                    var old = GUI.color;
                    GUI.color = tint ?? old;
                    GUI.DrawTexture(new Rect(x, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize), icon, ScaleMode.ScaleToFit);
                    GUI.color = old;
                    x += IconSize + 6f;
                }

                var countWidth = count != null ? 34f : 0f;
                var style = selected ? SelectedLabel : EditorStyles.label;
                style.Draw(new Rect(x, rect.y + 1f, rect.xMax - x - countWidth - 4f, rect.height - 2f), label, false, false, false, false);
                if (count != null)
                {
                    CountLabel.Draw(new Rect(rect.xMax - countWidth - 6f, rect.y, countWidth, rect.height), count, false, false, false, false);
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                e.Use();
                return true;
            }

            return false;
        }

        /// <summary>A linked element's name, with its type icon; a click opens it.</summary>
        private void DrawLink(string id, KnownElement target)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f); // a layout row ignores indentLevel; a list's items sit under their heading
            var icon = IconFor(target.Type);
            if (icon != null)
            {
                var r = GUILayoutUtility.GetRect(IconSize, IconSize, GUILayout.Width(IconSize), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                var old = GUI.color;
                GUI.color = OWPresentation.ColorFor(target.Type, EditorGUIUtility.isProSkin);
                GUI.DrawTexture(new Rect(r.x, r.y + (r.height - 14f) / 2f, 14f, 14f), icon, ScaleMode.ScaleToFit);
                GUI.color = old;
            }

            if (EditorGUILayout.LinkButton(string.IsNullOrEmpty(target.Name) ? id : target.Name))
            {
                OpenLinked(id);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static Texture2D IconFor(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            if (!Icons.TryGetValue(type, out var icon) || icon == null)
            {
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + type + ".png");
                Icons[type] = icon;
            }

            return icon;
        }

        // -- Links ------------------------------------------------------------

        private void Index(IEnumerable<JObject> elements, string type)
        {
            foreach (var element in elements)
            {
                var id = element["id"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                _known[id] = new KnownElement { Type = element["type"]?.ToString() ?? type, Name = element["name"]?.ToString() };
            }
        }

        private void IndexCache()
        {
            if (_cache == null) return;
            foreach (var type in ElementTypes)
            {
                Index(_cache.AllRaw(type).Select(JObject.Parse), type);
            }
        }

        /// <summary>Opens a linked element: its type's list, then the element itself.</summary>
        /// <remarks>
        /// Live, the type's list loads asynchronously, so the id waits in <c>_pendingId</c> until
        /// the load lands; from the cache it opens at once.
        /// </remarks>
        private void OpenLinked(string id)
        {
            if (!_known.TryGetValue(id, out var target)) return;
            _pendingId = id;
            _filter = string.Empty;
            GUI.FocusControl(null);
            if (target.Type != _selectedType) SelectType(target.Type);
            OpenPending();
        }

        private void OpenPending()
        {
            if (_pendingId == null) return;
            foreach (var element in _elements)
            {
                if (element["id"]?.ToString() != _pendingId) continue;
                _selectedElement = element;
                _detailScroll = Vector2.zero;
                _pendingId = null;
                break;
            }

            Repaint();
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
                        _known.Clear();
                        IndexCache();
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
                Index(_elements, type);
                OpenPending();
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
                    Index(loaded, type);
                    OpenPending();
                },
                error =>
                {
                    _busy = false;
                    _status = Describe(error);
                    Repaint();
                });
        }

        /// <summary>
        /// Opens a world folder from disk into the browser, read-only.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reading is not editing: nothing is created, moved or rewritten in the chosen folder. The
        /// reader is synchronous and local, so it does not go through <c>OWEditorAsync</c> -- there
        /// is no request to marshal and no main-thread hazard.
        /// </para>
        /// <para>
        /// Skipped files are surfaced in the status bar rather than swallowed. A world that quietly
        /// loses an element to one malformed file looks identical to a world that never had it, and
        /// the person who can act on that is the one looking at this window.
        /// </para>
        /// </remarks>
        private void OpenFolderWorld()
        {
            var path = EditorUtility.OpenFolderPanel("Open OnlyWorlds folder world", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var cache = ScriptableObject.CreateInstance<OWWorldCache>();
                var read = OWFolderLoader.LoadInto(cache, path);

                _cache = cache;
                _useCache = true;
                _worldId = read.WorldId;
                _worldName = string.IsNullOrEmpty(read.WorldName) ? "(unnamed world)" : read.WorldName;

                RecountFromCache();
                _known.Clear();
                IndexCache();
                _selectedType = null;
                _selectedElement = null;
                _elements.Clear();

                _status = $"Folder: {_worldName} -- {cache.Count} elements"
                          + (read.LegacyElements.Count > 0 ? $", {read.LegacyElements.Count} legacy" : string.Empty)
                          + (read.Skipped.Count > 0 ? $", {read.Skipped.Count} SKIPPED" : string.Empty)
                          + (read.DeclaresReadOnly ? " [frozen snapshot]" : string.Empty);

                foreach (var skip in read.Skipped)
                {
                    Debug.LogWarning($"[OnlyWorlds] Skipped {skip.Path} -- {skip.Reason}. Left on disk.");
                }
            }
            catch (OWFolderFormatException e)
            {
                _status = "Not a world folder.";
                EditorUtility.DisplayDialog("OnlyWorlds",
                    $"That folder could not be read as a world:\n\n{e.Message}", "OK");
            }

            Repaint();
        }

        /// <summary>Rebuilds the per-type counts from whatever the cache currently holds.</summary>
        private void RecountFromCache()
        {
            _counts.Clear();
            if (_cache == null) return;

            foreach (var type in ElementTypes)
            {
                var n = _cache.AllRaw(type).Count;
                if (n > 0) _counts[type] = n;
            }
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
                    IndexCache();

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
