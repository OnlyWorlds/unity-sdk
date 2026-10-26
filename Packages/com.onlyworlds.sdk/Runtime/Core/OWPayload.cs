using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The fields the server owns, which a client must never send back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read body carries the first five. Round-tripping one straight back into a PUT or PATCH is
    /// the natural thing to write and it is wrong: <c>world</c> alone returns 422, because world
    /// identity comes from the API key, not the payload. The sixth, <c>known_of</c>,
    /// marks a stub (<see cref="IsKnownOf"/>); keel's write path refuses it at any value, <c>false</c>
    /// included (422 <c>known_of_not_writable</c>), so a stray one is stripped like the rest, and a
    /// body where it is <c>true</c> is refused before anything is sent.
    /// </para>
    /// <para>
    /// Mirrors <c>READ_ONLY_FIELDS</c> in the npm SDK's <c>client.ts</c> exactly, as of 4.42.3. If that
    /// list ever grows, this one has to grow with it -- the two clients are speaking the same wire and a
    /// divergence here means one of them starts getting 422s the other does not.
    /// </para>
    /// </remarks>
    public static class OWPayload
    {
        public static readonly IReadOnlyList<string> ReadOnlyFields = new[]
        {
            "world",
            "type",
            "created_at",
            "updated_at",
            "change_seq",
            KnownOf,
        };

        /// <summary>
        /// The world's own keys <c>GET /world</c> serves and <c>PATCH /world</c> refuses (a 422 for each), stripped by
        /// <see cref="OWClient.PatchWorldAsync"/> on top of <see cref="ReadOnlyFields"/> so a read body can be sent back.
        /// </summary>
        /// <remarks>
        /// <c>id</c> (keel's World marks it read-only); <c>slug</c> and <c>public_page</c> and the owner's
        /// switches <c>public_read</c>, <c>agent_narrator_rule</c> and <c>agent_writes_proposed</c>, each set on
        /// the world page; and <c>origin</c>, the world's first import, which is written as the plain keys
        /// <c>source</c> and <c>producer</c> and never as itself; and <c>counts</c> (read-only in keel's World), the element
        /// counts a keyless read serves, absent with a key. <c>owner_character</c> is not here: PATCH takes it. The same
        /// list as the npm SDK's <c>WORLD_READ_ONLY_FIELDS</c> (4.65.0) and the Python package's (0.34.0), plus
        /// <c>counts</c>, which neither strips yet. Kept apart from <see cref="ReadOnlyFields"/> so an element write never
        /// loses a field of these names.
        /// </remarks>
        public static readonly IReadOnlyList<string> WorldReadOnlyFields = new[]
        {
            "id",
            "slug",
            "public_page",
            "public_read",
            "agent_narrator_rule",
            "agent_writes_proposed",
            "origin",
            "counts",
        };

        /// <summary>The stub marker's key (folder format §4.4, v0.3.10).</summary>
        public const string KnownOf = "known_of";

        public static bool IsReadOnly(string fieldName)
        {
            for (var i = 0; i < ReadOnlyFields.Count; i++)
            {
                if (ReadOnlyFields[i] == fieldName) return true;
            }

            return false;
        }

        /// <summary>
        /// True for a stub the reader only knows of (<c>known_of: true</c>), never a whole element.
        /// </summary>
        /// <remarks>
        /// keel hands a guest or player key a stub for an element it reaches only through its own
        /// Character's sheet: <c>id</c>, <c>type</c>, <c>name</c>, <c>description</c>, <c>supertype</c>,
        /// <c>subtype</c>, <c>image_url</c>, the three server stamps and the type's public fields with
        /// their values, every other field unset. Those unset fields are unknown, not empty and not a
        /// deletion: never merge them over a whole copy, and never write a stub back (the client refuses
        /// to). Only the JSON boolean <c>true</c> counts; absent, <c>false</c> or anything else is a whole
        /// element, as the npm SDK's <c>isKnownOf</c> and the Python package's <c>is_known_of</c> read it.
        /// </remarks>
        public static bool IsKnownOf(JObject body)
        {
            var marker = body?[KnownOf];
            return marker != null && marker.Type == JTokenType.Boolean && (bool)marker;
        }

        /// <summary>The hidden mark on an element (folder format §4.8).</summary>
        public const string Hidden = "hidden";

        /// <summary>
        /// True for an element whose <c>hidden</c> is there with any value but <c>null</c> and <c>false</c>: the mark
        /// (<c>true</c>, what its source or its owner hid) or a doubt (<c>"true"</c>, <c>1</c>), which counts as the mark, so
        /// a doubt is never handed on. Absent, <c>null</c> and <c>false</c> are not hidden.
        /// </summary>
        /// <remarks>
        /// Folder format §4.8 (RULED 2026-10-23, Skeld): a folder carries no viewer identity, so the mark means
        /// hidden from whoever the world is handed to, never from the person who opened it. A reader that shows a world to
        /// its holder (a viewer, this SDK's folder readers handing elements to their own caller) shows the element with
        /// the word hidden on it; a reader that hands a world on (a pack, a context packet, a share) leaves it out unless
        /// asked and counts what it left out. A writer keeps the key byte for byte. One rule for both kinds of reader, so
        /// the word a viewer shows predicts what a share leaves out: the Python package's <c>folder.is_hidden</c> (0.43.0)
        /// and the npm SDK's world-file writer (4.70.0) read it the same way (Kael h58).
        /// </remarks>
        public static bool IsHidden(JObject body) => IsHiddenMark(body?[Hidden]);

        /// <summary><see cref="IsHidden"/>'s rule on the mark's own value.</summary>
        internal static bool IsHiddenMark(JToken mark)
            => mark != null && mark.Type != JTokenType.Null && !(mark.Type == JTokenType.Boolean && !(bool)mark);

        /// <summary>The review profile's mark on a new element written as proposed (schema-dist <c>profiles/review.md</c>).</summary>
        public const string ReviewProposed = "x_review_proposed";

        /// <summary>
        /// True for a proposed element, one whose <c>x_review_proposed</c> is an object: not canon until a keeper settles
        /// it (<see cref="OWClient.ReviewNewElementAsync"/>). Absent, <c>null</c> or any other value is settled.
        /// </summary>
        /// <remarks>
        /// The review profile's rules 1 and 4, and keel's own <c>has_mark</c> (<c>proposals/marked.py</c>): the rule of the
        /// review queue and the review route, and of a reader that shows a world to its people, which shows such an
        /// element with its mark visible, apart from settled ones. A reader that hands a world on (a pack, a context
        /// packet for an AI) reads the wider <see cref="IsProposedOrInDoubt"/> instead.
        /// </remarks>
        public static bool IsProposed(JObject body) => IsAMark(body?[ReviewProposed]);

        /// <summary>
        /// True when <c>x_review_proposed</c> is there with any value but <c>null</c>: a mark (an object) or a doubt (a
        /// string, a number, a list). The rule of a reader that hands a world on, which leaves such an element out
        /// unless asked, so a doubt never reaches an AI as canon.
        /// </summary>
        /// <remarks>
        /// keel's <c>doubt_or_mark</c> (<c>proposals/marked.py</c>, as amended on 2026-10-23), which its
        /// draught and MCP reads use, and the Python package's <c>pack.is_proposed</c> (0.36.0). Wider than
        /// <see cref="IsProposed"/>, which the queue and the review route keep.
        /// </remarks>
        public static bool IsProposedOrInDoubt(JObject body) => IsMarkOrDoubt(body?[ReviewProposed]);

        /// <summary><see cref="IsProposed"/>'s rule on the mark's own value.</summary>
        internal static bool IsAMark(JToken mark) => mark != null && mark.Type == JTokenType.Object;

        /// <summary><see cref="IsProposedOrInDoubt"/>'s rule on the mark's own value.</summary>
        internal static bool IsMarkOrDoubt(JToken mark) => mark != null && mark.Type != JTokenType.Null;
    }
}
