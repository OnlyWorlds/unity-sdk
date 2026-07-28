using System;

namespace OnlyWorlds.Sdk
{
    /// <summary>What kind of credential a key string is, decided by its prefix.</summary>
    public enum OWKeyKind
    {
        /// <summary>Not recognisable as any known key shape.</summary>
        Unknown = 0,

        /// <summary><c>ow_w_</c> -- world key, read+write. Paired with a PIN.</summary>
        Write,

        /// <summary><c>ow_r_</c> -- read-only. No PIN; this is the share primitive.</summary>
        Read,

        /// <summary><c>ow_a_</c> -- account key, sent as a Bearer token.</summary>
        Account,

        /// <summary>A legacy 10-digit world key. Still honored.</summary>
        Legacy,
    }

    /// <summary>
    /// Classifies OnlyWorlds API keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are OPAQUE PREFIXED STRINGS. The prefix decides the auth shape and whether a PIN is
    /// required; nothing else about the string is our business.
    /// </para>
    /// <para>
    /// Deliberately NOT regex-validating the legacy 10-digit format beyond "ten digits". Validating
    /// a credential's interior shape means a server-side format change silently locks users out of
    /// a client that was, from its own point of view, working correctly. The server is the only
    /// authority on whether a key is good; we only need to know how to send it.
    /// </para>
    /// </remarks>
    public static class OWKey
    {
        public const string WritePrefix = "ow_w_";
        public const string ReadPrefix = "ow_r_";
        public const string AccountPrefix = "ow_a_";

        public static OWKeyKind DetectKind(string key)
        {
            if (string.IsNullOrEmpty(key)) return OWKeyKind.Unknown;

            if (key.StartsWith(WritePrefix, StringComparison.Ordinal)) return OWKeyKind.Write;
            if (key.StartsWith(ReadPrefix, StringComparison.Ordinal)) return OWKeyKind.Read;
            if (key.StartsWith(AccountPrefix, StringComparison.Ordinal)) return OWKeyKind.Account;

            if (key.Length == 10 && IsAllDigits(key)) return OWKeyKind.Legacy;

            return OWKeyKind.Unknown;
        }

        /// <summary>
        /// Account keys authenticate with a Bearer token; every other kind uses API-Key/API-Pin.
        /// </summary>
        public static bool UsesBearer(OWKeyKind kind) => kind == OWKeyKind.Account;

        /// <summary>
        /// Read keys are the share primitive and carry no PIN. Write and legacy keys need one.
        /// </summary>
        public static bool RequiresPin(OWKeyKind kind) =>
            kind == OWKeyKind.Write || kind == OWKeyKind.Legacy;

        public static bool CanWrite(OWKeyKind kind) =>
            kind == OWKeyKind.Write || kind == OWKeyKind.Legacy || kind == OWKeyKind.Account;

        private static bool IsAllDigits(string s)
        {
            foreach (var c in s)
            {
                if (c < '0' || c > '9') return false;
            }

            return true;
        }
    }
}
