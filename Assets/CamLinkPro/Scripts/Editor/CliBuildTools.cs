using UnityEditor;
using UnityEngine;

namespace CamLinkPro.EditorTools
{
    /// <summary>Headless Android build entry point for command-line use
    /// (-executeMethod CamLinkPro.EditorTools.CliBuildTools.BuildAndroidDevelopment)
    /// while the Editor GUI is closed, so package resolution / compile
    /// errors surface without needing the interactive Editor or the AI
    /// Assistant MCP bridge.</summary>
    public static class CliBuildTools
    {
        public static void BuildAndroidDevelopment()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/CamLinkPro/Scenes/Main.unity" },
                locationPathName = "Builds/Android/camlinkpro_cli.apk",
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"CLI_BUILD_RESULT: {report.summary.result} errors={report.summary.totalErrors} size={report.summary.totalSize}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
