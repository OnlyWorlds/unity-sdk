using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OnlyWorlds.Sdk
{
    /// <summary>What an export file read found: a folder read's result, plus the envelope's own stamps.</summary>
    public class OWEnvelopeReadResult : OWFolderReadResult
    {
        /// <summary>Always <see cref="OWEnvelopeReader.Format"/>: anything else is refused.</summary>
        public string Format;

        /// <summary>
        /// Which version of the OnlyWorlds standard the elements follow (<c>00.32.00</c>, the value of
        /// keel's <c>X-OW-Schema-Version</c> header; spec §8c). Information,
        /// never checked: the envelope's own shape is keyed by <see cref="Format"/> alone.
        /// </summary>
        public string SchemaVersion;

        /// <summary>ISO 8601, as sent: when the owner downloaded it.</summary>
        public string ExportedAt;
    }

    /// <summary>
    /// Reads the portal's one-file world export (<c>format: onlyworlds-world-export</c>, keel's
    /// <c>world_export</c>; spec §8c), the file an owner downloads from their world's page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is <c>{format, schema_version, exported_at, world{id, name, created_at, updated_at},
    /// elements{slug: [v2 body…]}}</c>: the owner's full view, links as ids under the schema's names,
    /// extension fields riding along. It carries no media (a Map's picture is only its
    /// <c>image_url</c>), no members and no time settings.
    /// </para>
    /// <para>
    /// The result is a folder read's, so a caller opens a file as it opens a folder. The same rules
    /// hold: an element without an id is skipped and reported, the body's own <c>type</c> beats the
    /// key it sits under, unknown keys are ignored, and the bodies are kept verbatim.
    /// </para>
    /// </remarks>
    public static class OWEnvelopeReader
    {
        public const string Format = "onlyworlds-world-export";

        /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
        /// <exception cref="OWFolderFormatException">The file is not an export: see <see cref="Parse"/>.</exception>
        public static OWEnvelopeReadResult Read(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException($"No export file at '{path}'.", path);

            return Parse(File.ReadAllText(path), path);
        }

        /// <param name="json">The file's text.</param>
        /// <param name="source">Where it came from, for each element's and skip's <c>Path</c> (optional).</param>
        /// <exception cref="OWFolderFormatException">
        /// Not JSON, not an object, <c>format</c> other than <see cref="Format"/>, or no world with an id:
        /// the failures that make the whole file unreadable. Everything else degrades to a skipped entry.
        /// </exception>
        public static OWEnvelopeReadResult Parse(string json, string source = null)
        {
            var where = source ?? "the export";

            JObject envelope;
            try
            {
                envelope = OWFolderWriter.Parse(json ?? "");
            }
            catch (Exception e)
            {
                throw new OWFolderFormatException($"{where} is not valid JSON.", e);
            }

            var format = envelope["format"]?.Type == JTokenType.String ? (string)envelope["format"] : null;
            if (format != Format)
            {
                throw new OWFolderFormatException(
                    $"{where} is not an OnlyWorlds world export (format is '{format ?? "missing"}', not '{Format}').");
            }

            var world = envelope["world"] as JObject;
            if (string.IsNullOrEmpty(world?["id"]?.ToString()))
            {
                throw new OWFolderFormatException($"{where} has no world id: a world without one cannot be keyed or compared.");
            }

            var result = new OWEnvelopeReadResult
            {
                World = world,
                Format = format,
                SchemaVersion = envelope["schema_version"]?.ToString(),
                ExportedAt = envelope["exported_at"]?.ToString(),
            };

            if (!(envelope["elements"] is JObject groups)) return result;

            foreach (var group in groups.Properties())
            {
                var slug = group.Name;
                if (!(group.Value is JArray list))
                {
                    result.Skipped.Add(new OWSkippedFile { Path = $"{where}#elements/{slug}", Reason = "not a list" });
                    continue;
                }

                for (var i = 0; i < list.Count; i++)
                {
                    var at = $"{where}#elements/{slug}/{i}";

                    if (!(list[i] is JObject body))
                    {
                        result.Skipped.Add(new OWSkippedFile { Path = at, Reason = "not an object" });
                        continue;
                    }

                    var id = body["id"]?.ToString();
                    if (string.IsNullOrEmpty(id))
                    {
                        result.Skipped.Add(new OWSkippedFile { Path = at, Reason = "no id in body" });
                        continue;
                    }

                    var declared = body["type"]?.ToString();
                    var inferred = string.IsNullOrEmpty(declared);

                    result.Elements.Add(new OWFolderElement
                    {
                        Id = id,
                        Type = inferred ? slug : declared,
                        TypeInferred = inferred,
                        Body = body,
                        Path = at,
                    });
                }
            }

            return result;
        }
    }

    /// <summary>Loads an export file into a cache.</summary>
    public static class OWEnvelopeLoader
    {
        /// <summary>
        /// Reads an export file and fills a cache with it, read-only: a downloaded file is a capture,
        /// with no api block to sync and no folder to write back to.
        /// </summary>
        /// <remarks>
        /// Keyed <c>(Folder, worldId, path)</c> as a folder is, so this file and a folder or another
        /// download of the same world never open as one another.
        /// </remarks>
        public static OWEnvelopeReadResult LoadInto(OWWorldCache cache, string path)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            var read = OWEnvelopeReader.Read(path);

            cache.Initialize(OWWorldKey.FromFolder(read.WorldId, path), read.WorldName, readOnly: true);
            cache.Clear();

            foreach (var element in read.Elements)
            {
                cache.Upsert(element.Id, element.Type,
                    OWFolderReader.WithSchemaLinkNames(element.Body).ToString(Newtonsoft.Json.Formatting.None));
            }

            return read;
        }
    }
}
