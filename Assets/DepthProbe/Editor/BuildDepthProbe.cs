using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the depth probe's scene as a WebGL player (IL2CPP), uncompressed so any static server can host it:
/// <c>Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod BuildDepthProbe.WebGL -- --out &lt;dir&gt;</c>.
/// Player settings it changes are put back after the build.
/// </summary>
public static class BuildDepthProbe
{
    const string ScenePath = "Assets/DepthProbe/DepthProbe.unity";

    public static void WebGL()
    {
        var args = Environment.GetCommandLineArgs();
        var output = Path.GetFullPath("Builds/depthprobe-webgl");
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--out") output = Path.GetFullPath(args[i + 1]);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Camera").AddComponent<Camera>();
        new GameObject("DepthProbe").AddComponent<DepthProbe>();
        EditorSceneManager.SaveScene(scene, ScenePath);

        var compression = PlayerSettings.WebGL.compressionFormat;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        try
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            Debug.Log($"BUILDDEPTHPROBE {report.summary.result} {report.summary.totalErrors} errors, {output}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = compression;
        }
    }
}
