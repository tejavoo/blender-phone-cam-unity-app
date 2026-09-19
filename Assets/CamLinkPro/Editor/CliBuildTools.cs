using UnityEditor;
using UnityEngine;

namespace CamLinkPro.EditorTools
{
    /// Headless build entry points, invoked via -batchmode -executeMethod.
    /// See llms_complete_specs.txt section 9.7 for the reasoning behind this pattern.
    public static class CliBuildTools
    {
        public static void BuildAndroidDevelopment()
        {
            var scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path);
            var outputDir = "Builds/Android";
            System.IO.Directory.CreateDirectory(outputDir);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputDir + "/CamLinkPro.apk",
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });

            var summary = report.summary;
            Debug.Log($"[CliBuildTools] Build result: {summary.result}, size: {summary.totalSize} bytes, errors: {summary.totalErrors}");

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
