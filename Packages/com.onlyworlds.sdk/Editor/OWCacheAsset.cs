using System.IO;
using UnityEditor;
using UnityEngine;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Creates and finds cache assets on disk, keyed by world.
    /// </summary>
    /// <remarks>
    /// Asset paths derive from <see cref="OWWorldKey.ToAssetKey"/> -- (source, worldId) -- so a live
    /// world and a folder snapshot of it, or two snapshots of one world, land in different files.
    /// Naming by world id alone is the collision Temper demonstrated: two registry entries pointing
    /// at one slot, opening the wrong world with no warning.
    /// </remarks>
    public static class OWCacheAsset
    {
        public const string DefaultFolder = "Assets/OnlyWorlds";

        public static string PathFor(OWWorldKey key, string folder = DefaultFolder)
            => $"{folder}/World_{key.ToAssetKey()}.asset";

        /// <summary>Loads the cache for a world, or creates it if absent.</summary>
        public static OWWorldCache LoadOrCreate(OWWorldKey key, string worldName, string folder = DefaultFolder)
        {
            var path = PathFor(key, folder);

            var existing = AssetDatabase.LoadAssetAtPath<OWWorldCache>(path);
            if (existing != null) return existing;

            EnsureFolder(folder);

            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            cache.Initialize(key, worldName);

            AssetDatabase.CreateAsset(cache, path);
            AssetDatabase.SaveAssets();
            return cache;
        }

        /// <summary>Loads the cache for a world, or null.</summary>
        public static OWWorldCache Find(OWWorldKey key, string folder = DefaultFolder)
            => AssetDatabase.LoadAssetAtPath<OWWorldCache>(PathFor(key, folder));

        /// <summary>
        /// Flushes in-memory changes to disk.
        /// </summary>
        /// <remarks>
        /// <c>SetDirty</c> alone only marks the asset; without <c>SaveAssets</c> a domain reload
        /// discards everything a sync just fetched, which looks exactly like the sync failing.
        /// </remarks>
        public static void Save(OWWorldCache cache)
        {
            if (cache == null) return;
            EditorUtility.SetDirty(cache);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            // AssetDatabase.CreateFolder does not create intermediates, so walk the path.
            var parts = folder.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
