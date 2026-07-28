using System;
using System.Threading.Tasks;
using UnityEditor;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Runs a Task from editor code without ever blocking the update loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists because of a deadlock that froze this editor on 2026-07-28.
    /// <c>UnityWebRequest.SendWebRequest()</c> raises its completion callback FROM the update loop.
    /// Any editor code that blocks that loop while waiting -- <c>.Result</c>, <c>.Wait()</c>, or a
    /// synchronous test awaiting the task -- prevents the very pump that would complete it. The
    /// editor hangs until the request times out, with no diagnostic.
    /// </para>
    /// <para>
    /// So: never block. Fire the task, return to the loop immediately, and deliver the result via
    /// callbacks on the main thread. Every await in this SDK's editor surface goes through here.
    /// </para>
    /// </remarks>
    public static class OWEditorAsync
    {
        /// <summary>
        /// Runs <paramref name="task"/> and delivers its outcome on the main thread.
        /// </summary>
        /// <remarks>
        /// Both callbacks are marshalled through <see cref="EditorApplication.delayCall"/> because
        /// a continuation may resume on a background thread, and touching Unity objects or repainting
        /// a window from there throws.
        /// </remarks>
        public static void Run<T>(Task<T> task, Action<T> onSuccess, Action<Exception> onError = null)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            task.ContinueWith(t =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (t.IsFaulted)
                    {
                        var error = t.Exception?.GetBaseException() ?? new Exception("Unknown failure.");
                        if (onError != null) onError(error);
                        else UnityEngine.Debug.LogError($"[OnlyWorlds] {error.Message}");
                        return;
                    }

                    if (t.IsCanceled) return;

                    try
                    {
                        onSuccess?.Invoke(t.Result);
                    }
                    catch (Exception e)
                    {
                        // A throwing success handler must not be reported as a request failure --
                        // that sends the reader looking at the network for a UI bug.
                        UnityEngine.Debug.LogError($"[OnlyWorlds] Handler threw after a successful request: {e}");
                    }
                };
            }, TaskScheduler.Default);
        }

        /// <summary>Task-returning overload for calls with no meaningful result.</summary>
        public static void Run(Task task, Action onSuccess, Action<Exception> onError = null)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            task.ContinueWith(t =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (t.IsFaulted)
                    {
                        var error = t.Exception?.GetBaseException() ?? new Exception("Unknown failure.");
                        if (onError != null) onError(error);
                        else UnityEngine.Debug.LogError($"[OnlyWorlds] {error.Message}");
                        return;
                    }

                    if (t.IsCanceled) return;
                    onSuccess?.Invoke();
                };
            }, TaskScheduler.Default);
        }
    }
}
