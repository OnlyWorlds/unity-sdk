using System;
using System.Collections.Generic;
using System.Text;
using OnlyWorlds.Sdk;
using UnityEngine;

/// <summary>
/// Reads element files nested 200, 201, 1,000 and 10,000 deep through the SDK's own folder reader in a player build,
/// and logs one line per depth, so a WebGL (IL2CPP) page shows whether the depth rule (read at 200, left out past it,
/// folder format 0.3.25) holds where the stack is the browser's.
/// </summary>
/// <remarks>
/// A test rig for the package, like <see cref="IL2CPPStripProbe"/>: scene-referenced so the linker keeps it. Each line
/// is logged before its read, so a page that dies on the stack still shows which depth it died at.
/// </remarks>
public class DepthProbe : MonoBehaviour
{
    const string World = "{\"id\":\"019a0000-0000-7000-8000-000000000001\",\"name\":\"Depth probe\"}";

    void Start()
    {
        foreach (var depth in new[] { 200, 201, 1000, 10000 })
        {
            Debug.Log($"DEPTHPROBE start {depth}");
            try
            {
                var entries = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("world.json", World),
                    new KeyValuePair<string, string>("elements/character/deep.json", Element(depth)),
                };
                var read = OWFolderReader.ReadEntries(entries);
                var reasons = new List<string>();
                foreach (var s in read.Skipped) reasons.Add(s.Reason);
                Debug.Log($"DEPTHPROBE depth={depth} read={read.Elements.Count} skipped={read.Skipped.Count} {string.Join("; ", reasons)}");
            }
            catch (Exception e)
            {
                Debug.Log($"DEPTHPROBE depth={depth} threw {e.GetType().Name}: {e.Message}");
            }
        }
        Debug.Log("DEPTHPROBE done");
    }

    /// <summary>An element whose own object is depth 1 and whose x_depth nests an array and an object in turn to
    /// <paramref name="depth"/>, with a plain value inside the deepest.</summary>
    static string Element(int depth)
    {
        var open = new StringBuilder();
        var close = new StringBuilder();
        open.Append("{\"id\":\"019a0000-0000-7000-8000-000000000011\",\"name\":\"Deep\",\"type\":\"character\",\"x_depth\":");
        for (var level = 2; level <= depth; level++)
        {
            var array = level % 2 == 0;
            open.Append(array ? "[" : "{\"k\":");
            close.Insert(0, array ? "]" : "}");
        }
        open.Append("1");
        return open.ToString() + close + "}";
    }
}
