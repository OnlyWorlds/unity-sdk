using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OnlyWorlds.Sdk;
using UnityEngine;

/// <summary>
/// Writes a world folder under <see cref="Application.persistentDataPath"/> through the SDK's own folder writer and
/// reads it back on the next load, so a WebGL page shows whether a folder a web game wrote survives a reload. Each
/// load reads first, then writes the next generation: one more element, and the world's <c>x_gen</c> raised by one.
/// </summary>
/// <remarks>
/// A test rig for the package, like <see cref="DepthProbe"/>: scene-referenced so the linker keeps it, one line logged
/// per step so a page that dies still shows where.
/// </remarks>
public class ReloadProbe : MonoBehaviour
{
    const string WorldId = "019a0000-0000-7000-8000-000000000101";

    void Start()
    {
        var folder = Path.Combine(Application.persistentDataPath, "reload-probe-world");
        Debug.Log($"RELOADPROBE start path={folder}");
        var gen = 0;
        try
        {
            if (Directory.Exists(folder))
            {
                var read = OWFolderReader.Read(folder);
                gen = read.World?["x_gen"]?.Value<int>() ?? -1;
                Debug.Log($"RELOADPROBE found gen={gen} elements={read.Elements.Count} skipped={read.Skipped.Count}");
            }
            else
            {
                Debug.Log("RELOADPROBE found none");
            }
        }
        catch (Exception e)
        {
            Debug.Log($"RELOADPROBE read threw {e.GetType().Name}: {e.Message}");
        }

        try
        {
            var next = Math.Max(gen, 0) + 1;
            OWFolderWriter.WriteWorld(folder, new JObject { ["id"] = WorldId, ["name"] = "Reload probe", ["x_gen"] = next });
            var bodies = new List<JObject>();
            for (var i = 1; i <= next; i++)
                bodies.Add(new JObject
                {
                    ["id"] = $"019a0000-0000-7000-8000-{100 + i:D12}",
                    ["name"] = $"Probe {i}",
                    ["type"] = "character",
                });
            OWFolderWriter.Write(folder, bodies);
            var back = OWFolderReader.Read(folder);
            Debug.Log($"RELOADPROBE wrote gen={next} elements={back.Elements.Count}");
        }
        catch (Exception e)
        {
            Debug.Log($"RELOADPROBE write threw {e.GetType().Name}: {e.Message}");
        }
        Debug.Log("RELOADPROBE done");
    }
}
