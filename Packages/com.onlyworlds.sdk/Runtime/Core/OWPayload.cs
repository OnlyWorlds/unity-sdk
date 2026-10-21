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
    /// identity comes from the API key, not the payload. The sixth, <c>known_of</c> (keel PR 113),
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

        /// <summary>The stub marker's key (folder format §4.4, v0.3.10; keel PR 113).</summary>
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
    }
}
