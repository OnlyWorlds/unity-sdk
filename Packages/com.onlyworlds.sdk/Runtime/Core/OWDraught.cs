namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The draught's policy (npm's <c>OwDraughtPolicy</c>): what GET /character/{id}/context serves and how
    /// (<see cref="OWClient.DraughtAsync"/>). Each member left null (or empty) sends nothing, and keel's default holds.
    /// </summary>
    public struct OWDraughtPolicy
    {
        /// <summary>
        /// The sections to serve, in keel's names (<c>self</c>, <c>role</c>, <c>work</c>, <c>style</c>, <c>events</c>,
        /// <c>knowledge</c>, <c>names</c>, <c>sheet</c>, <c>recent</c>, <c>messages</c>, <c>room</c>, <c>proposed</c>,
        /// <c>roster</c>), sent joined by commas as <c>components</c>. Default every one but <c>messages</c>, <c>room</c>
        /// and <c>proposed</c>, each served only when named.
        /// </summary>
        public string[] Components;

        /// <summary><c>voice</c>: her slice, as she would read it (keel's default). <c>act</c>: through the caller's own view.</summary>
        public string Mode;

        /// <summary>The horizon in the world's time unit, sent as <c>as_of</c>; default the world's <c>time_range_current</c>. A filter on the elements' own dates, not time travel.</summary>
        public int? AsOf;

        /// <summary>Undated Events: <c>include</c> (keel's default, marked <c>dated: false</c>, after the dated) or <c>exclude</c>.</summary>
        public string Undated;

        /// <summary>For <c>recent</c>: a <c>/changes</c> cursor, or an ISO 8601 time (as <c>/changes</c>' <c>after</c>). Absent, <c>recent</c> is empty.</summary>
        public string Since;

        /// <summary>Items per section, 1 to 1000 (keel's default 50); <c>cut</c> says what was dropped.</summary>
        public int? Budget;

        /// <summary>The sheet's reach: 1 (keel's default) what her own element links to, 0 none.</summary>
        public int? Depth;

        /// <summary>
        /// True leaves out every empty section and an empty <c>cut</c>, and each element body's empty fields (<c>null</c>,
        /// <c>""</c>, <c>[]</c>, <c>{}</c>; <c>id</c>, <c>type</c> and <c>name</c> always stay); the frame (<c>world</c>,
        /// <c>calendar</c>, <c>as_of</c>, <c>now</c>, <c>mode</c>, <c>policy</c>) and every null stay, so a null is still an
        /// answer. An absent section then means nothing in it, never that it was not read. False is sent as given.
        /// </summary>
        public bool? Compact;

        /// <summary>
        /// A lens id: the packet adds <c>lenses</c>, one frame with that lens read for this key's own view. Beside
        /// <see cref="Depth"/> or <see cref="Budget"/> it is keel's 422 (<c>param</c> lens).
        /// </summary>
        public string Lens;
    }
}
