using System.Collections.Generic;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// The fields the server owns, which a client must never send back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read body carries all five. Round-tripping one straight back into a PUT or PATCH is the
    /// natural thing to write and it is wrong: <c>world</c> alone returns 422, because world
    /// identity comes from the API key, not the payload.
    /// </para>
    /// <para>
    /// Mirrors <c>READ_ONLY_FIELDS</c> in the npm SDK's <c>client.ts</c> exactly. If that list ever
    /// grows, this one has to grow with it -- the two clients are speaking the same wire and a
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
        };

        public static bool IsReadOnly(string fieldName)
        {
            for (var i = 0; i < ReadOnlyFields.Count; i++)
            {
                if (ReadOnlyFields[i] == fieldName) return true;
            }

            return false;
        }
    }
}
