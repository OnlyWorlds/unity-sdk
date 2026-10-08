using System.Threading.Tasks;
using OnlyWorlds.Sdk;
using UnityEngine;

namespace OnlyWorlds.Samples
{
    /// <summary>
    /// Reads a world at runtime and logs what it finds.
    /// </summary>
    /// <remarks>
    /// Drop this on a GameObject, fill in a key, press play. It demonstrates the three things that
    /// are easy to get wrong: nullable fields, link resolution, and reading from a cache instead of
    /// the network.
    /// </remarks>
    public class OWQuickStart : MonoBehaviour
    {
        [Header("Credentials")]
        [Tooltip("Starts as the demo key for Moppetopia, a public sample world (read-only, no account). " +
                 "For your own world: an ow_r_ key reads, an ow_w_ key also writes.")]
        [SerializeField] private string _apiKey = "0000000001";

        [Tooltip("Only for writes: an agent seat's ow_s_ secret, or the account PIN. Reads need none.")]
        [SerializeField] private string _apiPin;

        [Header("Cache")]
        [Tooltip("Optional. When set, reads come from here and no network call is made.")]
        [SerializeField] private OWWorldCache _cache;

        private async void Start()
        {
            if (_cache != null)
            {
                ReadFromCache();
                return;
            }

            await ReadFromApi();
        }

        /// <summary>The shipping path: no network, no keys, no latency.</summary>
        private void ReadFromCache()
        {
            var characters = _cache.All<OWCharacter>("character");
            Debug.Log($"[QuickStart] {characters.Count} characters from cache '{_cache.WorldName}'.");

            foreach (var character in characters)
            {
                Describe(character);
            }
        }

        private async Task ReadFromApi()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning("[QuickStart] Set an API key, or assign a cache asset.");
                return;
            }

            var client = new OWClient(new OWClientConfig
            {
                ApiKey = _apiKey,
                ApiPin = _apiPin,
                Transport = new UnityWebRequestTransport(),
            });

            try
            {
                var world = await client.GetWorldAsync();
                Debug.Log($"[QuickStart] Connected to '{world["name"]}'.");

                var characters = await client.ListAllAsync<OWCharacter>("character");
                Debug.Log($"[QuickStart] {characters.Count} characters.");

                foreach (var character in characters)
                {
                    Describe(character);
                }
            }
            catch (OWApiError e)
            {
                // The typed error carries what went wrong and where to read about it -- far more
                // actionable than a status code alone.
                Debug.LogError($"[QuickStart] API {e.StatusCode} [{e.Code}] on '{e.Param}'. {e.DocUrl}");
            }
            catch (OWTransportError e)
            {
                // Distinct from an API rejection: the request never got an answer.
                Debug.LogError($"[QuickStart] Network unreachable. {e.Message}");
            }
        }

        private static void Describe(OWCharacter character)
        {
            // THE THING TO COPY. A nullable is not an int -- reading one means deciding what unset
            // means HERE. Level 0 and level-unknown are different claims, and the type refuses to
            // let you conflate them by accident.
            var level = character.Level.HasValue
                ? $"level {character.Level.Value}"
                : "level unknown";

            // Links are bare UUIDs; the cache is the resolver. With no cache, you hold ids.
            var friends = character.Friends.Count;

            Debug.Log($"  {character.Name} -- {level}, {friends} friend link(s).");
        }
    }
}
