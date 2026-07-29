using OnlyWorlds.Sdk;
using UnityEngine;

/// <summary>
/// Forces the SDK's serialization path into a stripped player build, and proves at runtime whether
/// it survived.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists.</b> The first Android IL2CPP build "succeeded" and proved nothing: the
/// sample scene referenced no SDK type, so the linker stripped the entire package and the converted
/// C++ contained no OnlyWorlds and no Newtonsoft at all. A green build over code that was never
/// included is the same class of non-result as a test suite reporting <c>total: 0 ... Passed</c>.
/// </para>
/// <para>
/// <b>What is actually at risk.</b> <see cref="SerializableNullable{T}"/>'s Newtonsoft converter is
/// resolved by REFLECTION over a closed generic type. IL2CPP's linker cannot see reflective use, so
/// without a preserved, statically-reachable reference the converter can be stripped from a device
/// build -- and then every unset nullable silently deserializes as <c>0</c>. Unset level becomes
/// level 0. That is exactly the null-to-zero collapse this whole type exists to prevent, appearing
/// only on device, never in the editor, and looking like real data.
/// </para>
/// <para>
/// This probe is deliberately in <c>Assets/</c> rather than the package: it is a test rig for the
/// package, not part of it, and it must be a scene-referenced MonoBehaviour or the linker is right
/// to remove it.
/// </para>
/// </remarks>
public class IL2CPPStripProbe : MonoBehaviour
{
    private void Start() => Debug.Log(RunProbe());

    /// <summary>
    /// Round-trips all three states through the real serializer and reports what came back.
    /// </summary>
    public static string RunProbe()
    {
        // A character carrying the three states that must stay distinct: explicitly null,
        // deliberately zero, and set to a real value.
        const string json = @"{""id"":""probe"",""name"":""Probe"",
                              ""charisma"":null,""level"":0,""height"":180}";

        var character = OWJson.Deserialize<OWCharacter>(json);

        var unsetHeld = !character.Charisma.HasValue;
        var zeroHeld = character.Level.HasValue && character.Level.Value == 0;
        var valueHeld = character.Height.HasValue && character.Height.Value == 180;

        // And back out again -- the write path is where a stripped converter does its real damage,
        // because an unset field silently becoming 0 on the wire overwrites the author's "never
        // set this" with a claim they never made.
        var backOut = OWJson.Serialize(character);
        var writesExplicitNull = backOut.Contains("\"charisma\":null");
        var keepsDeliberateZero = backOut.Contains("\"level\":0");

        var passed = unsetHeld && zeroHeld && valueHeld && writesExplicitNull && keepsDeliberateZero;

        return "[OW IL2CPP PROBE] " + (passed ? "PASS" : "FAIL")
               + " unset=" + unsetHeld
               + " zero=" + zeroHeld
               + " value=" + valueHeld
               + " writesNull=" + writesExplicitNull
               + " keepsZero=" + keepsDeliberateZero
               + " | " + backOut;
    }
}
