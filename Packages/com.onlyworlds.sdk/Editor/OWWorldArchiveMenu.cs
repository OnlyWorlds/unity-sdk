using System;
using System.IO;
using UnityEditor;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// <c>Assets/OnlyWorlds/Pack World Folder...</c>: a world folder anywhere on disk, packed into an <c>.owworld</c>
    /// inside the project, then imported.
    /// </summary>
    /// <remarks>
    /// The folder stays where it is and is never written to: Atlas, the Python package or a git repository owns it,
    /// and the folder format allows one writer per folder. Re-pack to refresh; the import follows the file.
    /// </remarks>
    public static class OWWorldArchiveMenu
    {
        [MenuItem("Assets/OnlyWorlds/Pack World Folder...")]
        public static void PackWorldFolder()
        {
            var folder = EditorUtility.OpenFolderPanel("Pack an OnlyWorlds world folder", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(folder)) return;

            string defaultName;
            try
            {
                defaultName = OWWorldArchive.DefaultArchiveName(folder);
            }
            catch (OWFolderFormatException e)
            {
                EditorUtility.DisplayDialog("OnlyWorlds", $"That folder is not a world folder:\n\n{e.Message}", "OK");
                return;
            }

            var target = EditorUtility.SaveFilePanelInProject(
                "Save the world archive", defaultName, OWWorldArchive.Extension,
                "Where in the project the world archive goes. The folder itself is not changed.");
            if (string.IsNullOrEmpty(target)) return;

            try
            {
                OWWorldArchive.Pack(folder, Path.GetFullPath(target));
            }
            catch (Exception e) when (e is OWFolderFormatException || e is IOException || e is ArgumentException
                                      || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("OnlyWorlds", $"The world could not be packed:\n\n{e.Message}", "OK");
                return;
            }

            AssetDatabase.ImportAsset(target);
            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(target));
        }
    }
}
