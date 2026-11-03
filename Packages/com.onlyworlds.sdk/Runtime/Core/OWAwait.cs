namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Where the SDK's own awaits resume. Everywhere but a web player they leave the caller's context
    /// (<c>ConfigureAwait(false)</c>), as they always have. A web player has one thread and no thread pool that runs: a
    /// continuation that leaves Unity's context is queued to that pool and never resumes, so the first answer arrived and
    /// the call that awaited it never returned (Subs' web build, 2026-11-03: the transport answered 200 and
    /// <see cref="OWClient.GetAsync{T}"/> never came back). There, the awaits keep Unity's context and resume on the main
    /// thread, where the transport already runs.
    /// </summary>
    internal static class OWAwait
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>The <c>continueOnCapturedContext</c> every SDK await passes: true in a web player.</summary>
        internal const bool OnCaller = true;
#else
        /// <summary>The <c>continueOnCapturedContext</c> every SDK await passes: false off the web.</summary>
        internal const bool OnCaller = false;
#endif
    }
}
