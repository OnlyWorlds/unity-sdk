namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The door a write came through, as keel records it on every element
    /// (<see cref="OWElement.ChangedVia"/>) and on a delete (<see cref="OWChange.DeletedVia"/>).
    /// </summary>
    /// <remarks>
    /// Mirrors the npm SDK's <c>OwWriteDoor</c>, <c>AGENT_DOORS</c> and <c>isAgentDoor</c> (4.26.0).
    /// <see cref="Seat"/>, <see cref="Mcp"/> and <see cref="Declared"/> mean agent-made; the rest name
    /// the surface a person's or keel's own write used.
    /// </remarks>
    public static class OWWriteDoor
    {
        /// <summary>An agent seat's key.</summary>
        public const string Seat = "seat";

        /// <summary>keel's MCP server.</summary>
        public const string Mcp = "mcp";

        /// <summary>A client that sent <c>X-OW-Agent</c> (see <see cref="OWClientConfig.Agent"/>).</summary>
        public const string Declared = "declared";

        public const string Api = "api";
        public const string V1 = "v1";

        /// <summary>The mobile app's sync.</summary>
        public const string Sync = "sync";

        /// <summary>keel's own writes.</summary>
        public const string Server = "server";

        /// <summary>The owner's own page on onlyworlds.com (a hide or an unhide made there).</summary>
        public const string Site = "site";

        /// <summary>Whether a write came through an agent's door; false for null and unknown values.</summary>
        public static bool IsAgent(string door) => door == Seat || door == Mcp || door == Declared;
    }
}
