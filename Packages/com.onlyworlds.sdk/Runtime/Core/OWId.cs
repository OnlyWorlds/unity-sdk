using System;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Element ids as the standard mints them: RFC 9562 UUIDv7 (48-bit Unix milliseconds, version 7,
    /// variant 10, 74 random bits).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the npm SDK's <c>mintUuid</c> (4.2.0 on), the Python package's <c>uuid7</c> and keel's own
    /// server-side minting (<c>core/ids.py</c>). v7 is a default, never a requirement: ids minted earlier as
    /// v4 stay valid forever (the schema's <c>id</c> description, dist serial 23).
    /// </para>
    /// <para>
    /// Ids this client mints a millisecond or more apart sort by creation, while the clock does not step
    /// backwards. A world also holds v4 ids and the old v1 server's seconds-first ids, so never order
    /// elements by id: <c>created_at</c> is creation order.
    /// </para>
    /// <para>
    /// The random bits come from <see cref="Guid.NewGuid"/>, so this runs wherever the SDK already did. Only
    /// the bytes of a v4 <see cref="Guid"/> that carry no version or variant bits are used.
    /// </para>
    /// </remarks>
    public static class OWId
    {
        /// <summary>A new UUIDv7 in its lowercase 8-4-4-4-12 form.</summary>
        public static string NewId() => NewId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /// <summary>A new UUIDv7 carrying <paramref name="unixMilliseconds"/> as its timestamp.</summary>
        public static string NewId(long unixMilliseconds)
        {
            // ToByteArray stores Data3 little-endian, so a v4 Guid's version nibble sits in byte 7 and its
            // variant bits in byte 8. Bytes 0-6 and 9-15 are wholly random; ten of them fill bytes 6-15 here.
            var r = Guid.NewGuid().ToByteArray();
            var b = new byte[16];

            for (var i = 0; i < 6; i++)
            {
                b[i] = (byte)(unixMilliseconds >> (8 * (5 - i)));
            }

            b[6] = (byte)(0x70 | (r[0] & 0x0f)); // version 7
            b[7] = r[1];
            b[8] = (byte)(0x80 | (r[2] & 0x3f)); // variant 10
            b[9] = r[3];
            b[10] = r[4];
            b[11] = r[5];
            b[12] = r[6];
            b[13] = r[9];
            b[14] = r[10];
            b[15] = r[11];

            var hex = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-"
                + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
        }
    }
}
