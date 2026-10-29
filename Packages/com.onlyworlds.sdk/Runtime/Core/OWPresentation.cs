using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// A colour word the presentation sidecar does not list in <c>colors.order</c>: on a type, as a colour's key, or asked
    /// of <see cref="OWPresentation.ColorOfFamily"/>. Or a word it lists whose colour is missing (no
    /// <c>colors.families</c> entry, no <c>light</c> or <c>dark</c>) or not a <c>#rrggbb</c> hex. The reader fails,
    /// naming the word (and the hex), and never draws a default colour (the lens profile's Nodes bullet and the
    /// sidecar's <c>_meta.groups</c> line, schema-dist serial 87).
    /// </summary>
    /// <remarks>
    /// A sidecar that fails this way fails every later read of it the same way (<see cref="OWPresentation.FamilyOf"/>,
    /// <see cref="OWPresentation.IconOf"/>, <see cref="OWPresentation.ColorFor"/>, a graph node), until it is re-vendored
    /// and <see cref="OWPresentation.Reload"/> runs. Most often it is a copy that kept an older serial's words (before
    /// serial 86 the groups were <c>agents</c>, <c>world</c>, <c>abstract</c> and <c>temporal</c>).
    /// </remarks>
    public sealed class OWPresentationException : Exception
    {
        internal OWPresentationException(string word, string type, IReadOnlyList<string> order)
            : base((type == null
                       ? $"[OnlyWorlds] the colour word '{word}' is not in colors.order"
                       : $"[OnlyWorlds] presentation.json gives the type '{type}' the colour word '{word}', which is not in colors.order")
                   + $" ({(order.Count == 0 ? "none listed" : string.Join(", ", order))}). A reader never draws a default colour: "
                   + "re-vendor Runtime/Resources/ow-presentation.json from the pinned schema-dist.")
        {
            Word = word;
            Type = type;
        }

        /// <summary>A word in <c>colors.order</c> whose colour is missing or not a hex: <paramref name="reason"/> says which.</summary>
        internal OWPresentationException(string word, string reason)
            : base($"[OnlyWorlds] presentation.json: the colour word '{word}' is in colors.order but {reason}. A reader never "
                   + "draws a default colour: re-vendor Runtime/Resources/ow-presentation.json from the pinned schema-dist.")
        {
            Word = word;
        }

        /// <summary>The word that is not in <c>colors.order</c>.</summary>
        public string Word { get; }

        /// <summary>The type slug the sidecar gave the word to; null for a colour's key or a word asked for.</summary>
        public string Type { get; }
    }

    /// <summary>A family's colour in both surface modes.</summary>
    public struct OWFamilyColor
    {
        public Color Light;
        public Color Dark;

        /// <summary>Picks the variant for the surface being drawn on.</summary>
        public Color For(bool darkSurface) => darkSurface ? Dark : Light;
    }

    /// <summary>
    /// Presentation defaults: which family a type belongs to, its icon, and family colours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from a vendored copy of schema-dist's <c>presentation.json</c>, never hardcoded.
    /// Four hardcoded copies of this palette already exist across the ecosystem; this SDK was going
    /// to be the fifth and the first with no argument for existing. The sidecar grew a
    /// <c>colors</c> block instead (dist v0.30.0-dist.6).
    /// </para>
    /// <para>
    /// **These are DEFAULTS a tool may override.** Atlas remaps wholesale for dark mode, and that is
    /// legitimate. A default a tool may replace is a different thing from a value a tool must invent.
    /// </para>
    /// <para>
    /// THE GOVERNING RULE: **colour carries the FAMILY; the icon carries the TYPE.** Dark-mode pairs
    /// land in the 6-8 deltaE floor band, so icon and label are REQUIRED alongside colour, never
    /// optional. A viewer distinguishing 22 types by colour alone cannot work and was never meant to.
    /// </para>
    /// </remarks>
    public static class OWPresentation
    {
        private const string ResourcePath = "ow-presentation";

        private static Dictionary<string, string> _familyByType;
        private static Dictionary<string, string> _iconByType;
        private static Dictionary<string, OWFamilyColor> _colorByFamily;
        private static string[] _familyOrder = Array.Empty<string>();
        private static bool _loaded;
        private static OWPresentationException _failure;

        /// <summary>
        /// Family names in their published order.
        /// </summary>
        /// <remarks>
        /// **The order is not cosmetic** -- it is the CVD-safety mechanism of the source palette.
        /// Preserve it when rendering legends and pickers.
        /// </remarks>
        public static IReadOnlyList<string> FamilyOrder
        {
            get { EnsureLoaded(); return _familyOrder; }
        }

        /// <summary>The family a type belongs to, or null if the type is unknown.</summary>
        public static string FamilyOf(string typeSlug)
        {
            EnsureLoaded();
            return typeSlug != null && _familyByType.TryGetValue(typeSlug, out var f) ? f : null;
        }

        /// <summary>
        /// The Material Symbols icon name for a type, or null.
        /// </summary>
        /// <remarks>
        /// The icon is what actually distinguishes 22 types; colour only distinguishes four
        /// families. A viewer that renders colour without icon and label has thrown away the
        /// discriminating signal and kept the decorative one.
        /// </remarks>
        public static string IconOf(string typeSlug)
        {
            EnsureLoaded();
            return typeSlug != null && _iconByType.TryGetValue(typeSlug, out var i) ? i : null;
        }

        /// <summary>Colour for a family, or null for no family (a null word) or when the sidecar did not load.</summary>
        /// <exception cref="OWPresentationException">The word is not in <c>colors.order</c>.</exception>
        public static OWFamilyColor? ColorOfFamily(string family)
        {
            EnsureLoaded();
            if (family == null) return null;
            if (Array.IndexOf(_familyOrder, family) < 0) throw new OWPresentationException(family, null, _familyOrder);
            if (_colorByFamily.TryGetValue(family, out var c)) return c;
            return null;
        }

        /// <summary>Colour for a type, resolved through its family.</summary>
        public static OWFamilyColor? ColorOfType(string typeSlug) => ColorOfFamily(FamilyOf(typeSlug));

        /// <summary>
        /// Convenience: the colour to draw a type in, with a neutral fallback.
        /// </summary>
        /// <remarks>
        /// Falls back to grey rather than throwing. An unrecognised type is a normal condition --
        /// the standard can add one before this SDK ships an updated sidecar -- and a viewer that
        /// crashes on an unknown type is worse than one that draws it plainly. A colour word not in
        /// <c>colors.order</c> is not that condition: the sidecar is wrong, and it throws
        /// (<see cref="OWPresentationException"/>) rather than draw grey.
        /// </remarks>
        public static Color ColorFor(string typeSlug, bool darkSurface)
        {
            var color = ColorOfType(typeSlug);
            return color?.For(darkSurface) ?? new Color(0.6f, 0.6f, 0.6f);
        }

        private static void EnsureLoaded()
        {
            if (_failure != null) throw _failure;
            if (_loaded) return;
            _loaded = true;

            _familyByType = new Dictionary<string, string>();
            _iconByType = new Dictionary<string, string>();
            _colorByFamily = new Dictionary<string, OWFamilyColor>();

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"[OnlyWorlds] {ResourcePath}.json not found in Resources. Types will render "
                    + "without family colours or icons.");
                return;
            }

            try
            {
                Parse(JObject.Parse(asset.text));
            }
            catch (OWPresentationException e)
            {
                // Loud and kept: a word not in colors.order never degrades to a default colour.
                _failure = e;
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[OnlyWorlds] Could not parse presentation defaults: {e.Message}");
            }
        }

        /// <summary>
        /// Reads the order first, so every word the sidecar uses is checked against it as it is read: a type's word and
        /// a colour's key. One not in the order throws <see cref="OWPresentationException"/>, naming it.
        /// </summary>
        private static void Parse(JObject root)
        {
            var colors = root["colors"] as JObject;

            if (colors?["order"] is JArray order)
            {
                var list = new List<string>(order.Count);
                foreach (var item in order) list.Add(item.ToString());
                _familyOrder = list.ToArray();
            }

            if (root["types"] is JObject types)
            {
                foreach (var pair in types)
                {
                    var entry = pair.Value as JObject;
                    if (entry == null) continue;

                    var family = entry["family"]?.ToString();
                    var icon = entry["icon"]?.ToString();

                    if (!string.IsNullOrEmpty(family))
                    {
                        if (Array.IndexOf(_familyOrder, family) < 0)
                            throw new OWPresentationException(family, pair.Key, _familyOrder);
                        _familyByType[pair.Key] = family;
                    }
                    if (!string.IsNullOrEmpty(icon)) _iconByType[pair.Key] = icon;
                }
            }

            if (colors == null) return;

            var families = colors["families"] as JObject;
            if (families != null)
            {
                foreach (var pair in families)
                {
                    if (Array.IndexOf(_familyOrder, pair.Key) < 0)
                        throw new OWPresentationException(pair.Key, null, _familyOrder);
                }
            }

            // Every word in the order has both modes, each a #rrggbb hex, checked before any is parsed (the parse needs
            // the engine), so a known type is never drawn in a default grey. Both modes are required: taking one variant
            // and reusing it for both surfaces is a surface-specific CHOICE, not reading the palette -- the pairs were
            // measured against near-white and near-black separately.
            foreach (var word in _familyOrder)
            {
                if (!(families?[word] is JObject entry))
                    throw new OWPresentationException(word, "it has no entry in colors.families");
                foreach (var mode in Modes)
                {
                    var hex = entry[mode] is JValue v && v.Type == JTokenType.String ? (string)v : null;
                    if (hex == null) throw new OWPresentationException(word, $"it has no {mode} colour in colors.families");
                    if (!IsHex(hex)) throw new OWPresentationException(word, $"its {mode} colour '{hex}' is not a #rrggbb hex");
                }
            }

            foreach (var word in _familyOrder)
            {
                var entry = (JObject)families[word];
                _colorByFamily[word] = new OWFamilyColor
                {
                    Light = ParseHex(word, "light", (string)entry["light"]),
                    Dark = ParseHex(word, "dark", (string)entry["dark"]),
                };
            }
        }

        private static readonly string[] Modes = { "light", "dark" };

        /// <summary>A <c>#</c> and six hex digits, the sidecar's shape for a colour.</summary>
        private static bool IsHex(string hex)
        {
            if (hex.Length != 7 || hex[0] != '#') return false;
            for (var i = 1; i < 7; i++)
            {
                var c = hex[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        private static Color ParseHex(string word, string mode, string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var color))
                throw new OWPresentationException(word, $"its {mode} colour '{hex}' does not parse");
            return color;
        }

        /// <summary>Forces a reload, a failed load's <see cref="OWPresentationException"/> included. For tests and for a re-vendored sidecar.</summary>
        public static void Reload()
        {
            _loaded = false;
            _failure = null;
        }

        /// <summary>
        /// Reads the sidecar from its text, as the resource is read: for a test run outside the Editor, where
        /// <c>Resources</c> loads nothing. The type table is read first; the colours need the engine's parser.
        /// </summary>
        internal static void LoadText(string json)
        {
            _loaded = true;
            _familyByType = new Dictionary<string, string>();
            _iconByType = new Dictionary<string, string>();
            _colorByFamily = new Dictionary<string, OWFamilyColor>();
            _familyOrder = Array.Empty<string>();
            _failure = null;
            try
            {
                Parse(JObject.Parse(json));
            }
            catch (OWPresentationException e)
            {
                _failure = e;
                throw;
            }
        }
    }
}
