using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OnlyWorlds.Sdk
{
    /// <summary>
    /// Element ids as the standard mints them: RFC 9562 UUIDv7 (48-bit Unix milliseconds, version 7,
    /// variant 10, 74 random bits); under a caller's Idempotency-Key, a UUIDv5 derived from it (<see cref="Keyed"/>).
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

            return Format(b);
        }

        /// <summary>The Python package's <c>_KEYED_IDS</c> namespace (<c>onlyworlds/http.py</c>), the npm SDK's <c>KEYED_IDS</c>.</summary>
        public const string KeyedNamespace = "0199b6a0-6f0e-7c2a-9d1e-3b5c7a8e4f21";

        /// <summary>
        /// The id for an element with none, written at <paramref name="index"/> under the caller's
        /// <paramref name="idempotencyKey"/>: the RFC 9562 UUIDv5 of <c>key + "\n" + index</c> in
        /// <see cref="KeyedNamespace"/>.
        /// </summary>
        /// <remarks>
        /// The same key with the same elements then sends the same body, so keel replays the first answer
        /// instead of refusing a second body (409 <c>idempotency_error</c>). The Python package
        /// (<c>_minted_id</c>) and the npm SDK (<c>keyedId</c>, 4.27.1 on) derive it the same way, so all
        /// three clients mint one id per key. A single create is index 0; a bulk item is its position.
        /// </remarks>
        public static string Keyed(string idempotencyKey, int index)
        {
            if (string.IsNullOrEmpty(idempotencyKey)) throw new ArgumentException("A keyed id needs a key.", nameof(idempotencyKey));

            var name = Encoding.UTF8.GetBytes(idempotencyKey + "\n" + index.ToString(CultureInfo.InvariantCulture));
            var input = new byte[16 + name.Length];
            var ns = KeyedNamespace.Replace("-", "");
            for (var i = 0; i < 16; i++)
            {
                input[i] = Convert.ToByte(ns.Substring(2 * i, 2), 16);
            }

            Buffer.BlockCopy(name, 0, input, 16, name.Length);

            byte[] hash;
            using (var sha1 = SHA1.Create())
            {
                hash = sha1.ComputeHash(input);
            }

            var b = new byte[16];
            Array.Copy(hash, b, 16);
            b[6] = (byte)(0x50 | (b[6] & 0x0f)); // version 5
            b[8] = (byte)(0x80 | (b[8] & 0x3f)); // variant 10
            return Format(b);
        }

        /// <summary>The id a write mints for an element with none: <see cref="Keyed"/> under a caller's key, else <see cref="NewId()"/>.</summary>
        internal static string Mint(string idempotencyKey, int index)
            => string.IsNullOrEmpty(idempotencyKey) ? NewId() : Keyed(idempotencyKey, index);

        static string Format(byte[] b)
        {
            var hex = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-"
                + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
        }
    }
}
