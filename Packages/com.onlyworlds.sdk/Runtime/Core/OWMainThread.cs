using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Marshals work onto Unity's main thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of the Unity API, including <c>UnityWebRequest.SendWebRequest()</c>, may only be called
    /// from the main thread. That is fine for a single request -- the caller is usually already
    /// there -- but a multi-request flow breaks: after the first <c>await</c>, the continuation
    /// resumes on a thread-pool thread, and every subsequent request is issued from the wrong
    /// thread.
    /// </para>
    /// <para>
    /// This was a real, shipped-to-the-user bug (2026-07-28): types small enough to fit one page
    /// loaded fine; a 172-element type needing two pages failed on page two and reported "network
    /// unreachable". The contract tests could not see it, because a fake transport has no thread
    /// affinity. Only running it against a real world found it.
    /// </para>
    /// </remarks>
    public static class OWMainThread
    {
        private static readonly Queue<Action> Pending = new Queue<Action>();
        private static int _mainThreadId = -1;
        private static bool _pumpInstalled;

        /// <summary>True when the calling thread is Unity's main thread.</summary>
        public static bool IsMainThread =>
            _mainThreadId != -1 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        /// Captures the main thread and installs the pump. Runs automatically at load.
        /// </summary>
        /// <remarks>
        /// <c>RuntimeInitializeOnLoadMethod</c> covers play mode and builds. The editor path is
        /// installed by <c>OWMainThreadEditorPump</c> in the Editor assembly, because a package's
        /// runtime assembly cannot reference <c>EditorApplication</c>.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeRuntime()
        {
            Capture();
            if (_pumpInstalled) return;

            var pump = new GameObject("[OnlyWorlds Pump]") { hideFlags = HideFlags.HideAndDontSave };
            pump.AddComponent<OWMainThreadPump>();
            UnityEngine.Object.DontDestroyOnLoad(pump);
            _pumpInstalled = true;
        }

        /// <summary>Records the current thread as the main thread. Idempotent.</summary>
        public static void Capture()
        {
            if (_mainThreadId == -1) _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>Queues an action to run on the main thread.</summary>
        /// <exception cref="InvalidOperationException">
        /// No pump is installed, so the action could never run.
        /// </exception>
        /// <remarks>
        /// Throwing beats queueing here. Without a pump the action sits in <c>Pending</c> forever,
        /// its <c>TaskCompletionSource</c> never completes and never faults, and the caller awaits
        /// with an empty console -- a hang with no diagnostic, which is the worst failure profile
        /// available. Reachable in batch mode, in a headless/server build, and anywhere
        /// <c>RuntimeInitializeOnLoadMethod</c> never ran. A caller that gets an exception can at
        /// least see why; a caller that gets silence cannot.
        /// </remarks>
        public static void Run(Action action)
        {
            if (action == null) return;

            if (!_pumpInstalled)
            {
                throw new InvalidOperationException(
                    "OnlyWorlds: no main-thread pump is installed, so this work could never run. "
                    + "The editor installs one automatically, and a player build installs one at "
                    + "load. In batch mode or a headless context, call OWMainThread.Capture() from "
                    + "the main thread and drive OWMainThread.Pump() yourself.");
            }

            lock (Pending)
            {
                Pending.Enqueue(action);
            }
        }

        /// <summary>
        /// Drains the queue. Called by whichever pump is driving -- editor or runtime.
        /// </summary>
        public static void Pump()
        {
            while (true)
            {
                Action next;
                lock (Pending)
                {
                    if (Pending.Count == 0) return;
                    next = Pending.Dequeue();
                }

                try
                {
                    next();
                }
                catch (Exception e)
                {
                    // One bad action must not stop the queue -- a stalled pump means every
                    // subsequent request hangs forever with no diagnostic.
                    Debug.LogError($"[OnlyWorlds] Queued main-thread action threw: {e}");
                }
            }
        }

        /// <summary>
        /// Marks the pump as installed, so the runtime path does not add a second one.
        /// </summary>
        /// <remarks>
        /// Public rather than internal because the editor pump lives in a different assembly --
        /// <c>internal</c> is per-assembly in C#, so the Editor assembly could not call it.
        /// </remarks>
        public static void MarkPumpInstalled() => _pumpInstalled = true;
    }

    /// <summary>Drives <see cref="OWMainThread.Pump"/> each frame at runtime.</summary>
    [AddComponentMenu("")]
    internal class OWMainThreadPump : MonoBehaviour
    {
        private void Update() => OWMainThread.Pump();
    }
}
