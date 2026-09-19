using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace CamLinkPro.EditorTools
{
    /// Enables the ARCore loader for Android in XR Plug-in Management. Without
    /// this, ARSession never initializes a real subsystem on-device — it just
    /// silently sits in the None state (a different, project-config-level
    /// version of the same "fails silently, no exception" trap noted for
    /// ARInputManager in llms_complete_specs.txt section 3).
    public static class XrConfig
    {
        const string SettingsAssetPath = "Assets/CamLinkPro/Resources/XRGeneralSettings.asset";
        const string ArCoreLoaderTypeName = "UnityEngine.XR.ARCore.ARCoreLoader";

        public static void EnableArCoreForAndroid()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget settingsPerTarget))
            {
                settingsPerTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsAssetPath);
                if (settingsPerTarget == null)
                {
                    settingsPerTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(settingsPerTarget, SettingsAssetPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settingsPerTarget, true);
            }

            if (!settingsPerTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                settingsPerTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);

            var androidSettings = settingsPerTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings.Manager == null)
                settingsPerTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var manager = settingsPerTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var assigned = XRPackageMetadataStore.AssignLoader(manager, ArCoreLoaderTypeName, BuildTargetGroup.Android);

            AssetDatabase.SaveAssets();
            Debug.Log($"[XrConfig] ARCore loader assigned for Android: {assigned}");
        }
    }
}
