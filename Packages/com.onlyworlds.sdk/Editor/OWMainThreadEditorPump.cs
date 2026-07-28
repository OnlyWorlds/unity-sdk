using UnityEditor;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Drives the main-thread queue in the editor, where there is no frame loop.
    /// </summary>
    /// <remarks>
    /// The runtime pump is a MonoBehaviour and only ticks in play mode, so editor tools --
    /// the browser window, the settings test -- would have their queued requests sit forever.
    /// <c>EditorApplication.update</c> is the editor's equivalent tick.
    ///
    /// Lives in the Editor assembly because a package's runtime assembly cannot reference
    /// <c>UnityEditor</c>. Same queue, different clock.
    /// </remarks>
    [InitializeOnLoad]
    internal static class OWMainThreadEditorPump
    {
        static OWMainThreadEditorPump()
        {
            // A static constructor under [InitializeOnLoad] runs on the main thread at editor
            // load and after every domain reload, which is exactly when the id needs recapturing.
            OWMainThread.Capture();
            OWMainThread.MarkPumpInstalled();

            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        private static void Pump() => OWMainThread.Pump();
    }
}
