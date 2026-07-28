using System;

namespace OnlyWorlds.Sdk
{
    /// <summary>Where a cached world came from.</summary>
    public enum OWSourceKind
    {
        /// <summary>The live v2 API.</summary>
        Api = 0,

        /// <summary>An on-disk folder export -- a snapshot, typically frozen.</summary>
        Folder,
    }

    /// <summary>
    /// Identity for a cached world: <c>(source, worldId)</c>. Never the world id alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RULED, folder-format spec v0.3 section 4.3: <i>a consumer that can hold more than one world
    /// at a time MUST key its own storage by (source, world id), never by world id alone.</i>
    /// </para>
    /// <para>
    /// This is not defensive design, it is a demonstrated bug. Temper wrote two folder snapshots of
    /// the same world -- chapter 1 and chapter 12, same <c>world.json</c> id, different content --
    /// and Atlas opened the wrong one. No warning, no error, a correct-looking UI showing the wrong
    /// world. The registry keyed on world id, so both entries pointed at the same slot.
    /// </para>
    /// <para>
    /// Unity makes this trap especially easy to fall into: the world id is the natural asset name,
    /// it is what the API returns, and it reads like a primary key. It is not one the moment you
    /// hold a live world and a snapshot of it at the same time -- which is exactly the SDK's
    /// intended use.
    /// </para>
    /// <para>
    /// Snapshots mint a FRESH world id (spec v0.3.1), so the collision is narrower than it was.
    /// The key stays on its own merits regardless: a live API world and a folder export of that
    /// same world are two different things to hold, and one of them must never be written to.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct OWWorldKey : IEquatable<OWWorldKey>
    {
        [UnityEngine.SerializeField] private OWSourceKind _source;
        [UnityEngine.SerializeField] private string _worldId;

        /// <summary>
        /// Distinguishes two sources of the same kind -- a base URL, or a folder path.
        /// </summary>
        [UnityEngine.SerializeField] private string _origin;

        public OWSourceKind Source => _source;
        public string WorldId => _worldId;
        public string Origin => _origin;

        public OWWorldKey(OWSourceKind source, string worldId, string origin = null)
        {
            _source = source;
            _worldId = worldId ?? string.Empty;
            _origin = origin ?? string.Empty;
        }

        public static OWWorldKey FromApi(string worldId, string baseUrl = null)
            => new OWWorldKey(OWSourceKind.Api, worldId, baseUrl);

        public static OWWorldKey FromFolder(string worldId, string folderPath)
            => new OWWorldKey(OWSourceKind.Folder, worldId, folderPath);

        public bool IsValid => !string.IsNullOrEmpty(_worldId);

        public bool Equals(OWWorldKey other)
            => _source == other._source
               && string.Equals(_worldId, other._worldId, StringComparison.Ordinal)
               && string.Equals(_origin, other._origin, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is OWWorldKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)_source;
                hash = (hash * 397) ^ (_worldId?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (_origin?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static bool operator ==(OWWorldKey a, OWWorldKey b) => a.Equals(b);

        public static bool operator !=(OWWorldKey a, OWWorldKey b) => !a.Equals(b);

        /// <summary>A filesystem-safe identity, for asset names and folder paths.</summary>
        public string ToAssetKey()
        {
            var origin = string.IsNullOrEmpty(_origin) ? "default" : Sanitize(_origin);
            return $"{_source.ToString().ToLowerInvariant()}__{Sanitize(_worldId)}__{origin}";
        }

        public override string ToString() => $"{_source}:{_worldId}" +
                                             (string.IsNullOrEmpty(_origin) ? "" : $" @ {_origin}");

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "none";

            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            }

            // A long origin (a URL or a deep path) would blow past filesystem name limits, and the
            // tail is the distinguishing part -- keep that rather than the protocol prefix.
            var result = sb.ToString();
            return result.Length <= 48 ? result : result.Substring(result.Length - 48);
        }
    }
}
