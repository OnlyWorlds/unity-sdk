using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Imports an <c>.owworld</c> (a world folder, zipped) as a read-only <see cref="OWWorldCache"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The archive is the one declared input, so Unity re-imports it whenever it changes and never writes a
    /// <c>.meta</c> into anyone's world folder. The cache is the asset: drag it onto a component's
    /// <c>OWWorldCache</c> field as with any cache.
    /// </para>
    /// <para>
    /// Thin on purpose: every decision (where the world sits in the zip, the refusals, the key, read-only) is in
    /// <see cref="OWWorldArchive"/>, where it is tested without the Editor. A refusal is an import error naming
    /// why, and no asset; a skipped element file is a warning, and the rest of the world still imports.
    /// </para>
    /// <para>
    /// Known gap: the archive's <c>media/</c> is not imported (the cache holds elements only).
    /// </para>
    /// </remarks>
    [ScriptedImporter(1, OWWorldArchive.Extension)]
    public class OWWorldArchiveImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            // The key's origin is the asset's GUID: in the committed .meta, so the same on every clone, and unmoved
            // by a move or a rename. Unity assigns it before the first import.
            var guid = AssetDatabase.AssetPathToGUID(ctx.assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                ctx.LogImportError($"{ctx.assetPath}: not imported. Unity gave the asset no GUID to key the world by.");
                return;
            }

            var cache = ScriptableObject.CreateInstance<OWWorldCache>();
            OWFolderReadResult read;
            try
            {
                // Read from the resolved file: a Packages/ path is virtual.
                read = OWWorldArchive.LoadInto(cache, Path.GetFullPath(ctx.assetPath), guid);
            }
            catch (System.Exception e) when (e is OWFolderFormatException || e is IOException
                                             || e is System.ArgumentException)
            {
                DestroyImmediate(cache);
                ctx.LogImportError($"{ctx.assetPath}: not imported. {e.Message}");
                return;
            }

            foreach (var skip in read.Skipped)
            {
                ctx.LogImportWarning($"{ctx.assetPath}: skipped {skip.Path} -- {skip.Reason}.");
            }

            cache.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("world", cache);
            ctx.SetMainObject(cache);
        }
    }
}
