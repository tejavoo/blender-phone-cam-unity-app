using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CamLinkPro.EditorTools
{
    /// One-time headless configuration entry point, run via -executeMethod.
    public static class ProjectBootstrap
    {
        public static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Teja";
            PlayerSettings.productName = "Cam Link Pro";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.camlinkpro.mobilev2");

            // UI Toolkit runtime panels have known attach/render issues under Vulkan
            // on Android in several Unity versions; force OpenGLES3 instead.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;

            // New Input System exclusively. activeInputHandler is project-wide (not
            // per-platform); it must be set directly in ProjectSettings.asset since
            // there is no public per-target PlayerSettings API for it.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBootstrap] Player settings configured.");
        }
    }
}
