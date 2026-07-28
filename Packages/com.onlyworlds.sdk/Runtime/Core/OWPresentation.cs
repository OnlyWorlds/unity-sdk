using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
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

        /// <summary>Colour for a family, or null if unknown.</summary>
        public static OWFamilyColor? ColorOfFamily(string family)
        {
            EnsureLoaded();
            if (family != null && _colorByFamily.TryGetValue(family, out var c)) return c;
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
        /// crashes on an unknown type is worse than one that draws it plainly.
        /// </remarks>
        public static Color ColorFor(string typeSlug, bool darkSurface)
        {
            var color = ColorOfType(typeSlug);
            return color?.For(darkSurface) ?? new Color(0.6f, 0.6f, 0.6f);
        }

        private static void EnsureLoaded()
        {
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
            catch (Exception e)
            {
                Debug.LogError($"[OnlyWorlds] Could not parse presentation defaults: {e.Message}");
            }
        }

        private static void Parse(JObject root)
        {
            if (root["types"] is JObject types)
            {
                foreach (var pair in types)
                {
                    var entry = pair.Value as JObject;
                    if (entry == null) continue;

                    var family = entry["family"]?.ToString();
                    var icon = entry["icon"]?.ToString();

                    if (!string.IsNullOrEmpty(family)) _familyByType[pair.Key] = family;
                    if (!string.IsNullOrEmpty(icon)) _iconByType[pair.Key] = icon;
                }
            }

            var colors = root["colors"] as JObject;
            if (colors == null) return;

            if (colors["order"] is JArray order)
            {
                var list = new List<string>(order.Count);
                foreach (var item in order) list.Add(item.ToString());
                _familyOrder = list.ToArray();
            }

            if (colors["families"] is JObject families)
            {
                foreach (var pair in families)
                {
                    var entry = pair.Value as JObject;
                    if (entry == null) continue;

                    // Both modes are required. Taking one variant and reusing it for both surfaces
                    // is a surface-specific CHOICE, not reading the palette -- the pairs were
                    // measured against near-white and near-black separately.
                    if (TryParseHex(entry["light"]?.ToString(), out var light) &&
                        TryParseHex(entry["dark"]?.ToString(), out var dark))
                    {
                        _colorByFamily[pair.Key] = new OWFamilyColor { Light = light, Dark = dark };
                    }
                }
            }
        }

        private static bool TryParseHex(string hex, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(hex)) return false;
            return ColorUtility.TryParseHtmlString(hex, out color);
        }

        /// <summary>Forces a reload. For tests and for a re-vendored sidecar.</summary>
        public static void Reload() => _loaded = false;
    }
}
