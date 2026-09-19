using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using CamLinkPro;

namespace CamLinkPro.EditorTools
{
    public static class SceneBootstrap
    {
        const string AppPanelSettingsPath = "Assets/CamLinkPro/Resources/UI/AppPanelSettings.asset";

        public static void EnsureAppPanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(AppPanelSettingsPath);
            if (settings != null)
                return;

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            AssetDatabase.CreateAsset(settings, AppPanelSettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBootstrap] Created AppPanelSettings.asset");
        }

        const string AppScenePath = "Assets/CamLinkPro/Scenes/Main.unity";

        public static void CreateAppScene()
        {
            EnsureAppPanelSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // AR Session gotcha (llms_complete_specs.txt section 3): the ARSession
            // GameObject must also carry an ARInputManager, or the XR input
            // subsystem's device never actually starts — fails silently (pose reads
            // static/zero), not an exception.
            var arSessionGo = new GameObject("AR Session");
            arSessionGo.AddComponent<ARSession>();
            arSessionGo.AddComponent<ARInputManager>();

            // AR Foundation requires an XR Origin wrapping the AR camera to
            // actually drive the camera's transform from tracking data —
            // without it, ARCameraManager/ARSession can initialize the
            // subsystem but the camera transform (and, in testing, ARSession
            // .state itself) never progresses past initializing. This was
            // the root cause of pose data never reaching Blender.
            var xrOriginGo = new GameObject("XR Origin");
            var xrOrigin = xrOriginGo.AddComponent<XROrigin>();

            var cameraGo = new GameObject("AR Camera");
            cameraGo.transform.SetParent(xrOriginGo.transform);
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cameraGo.tag = "MainCamera";
            cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<ARCameraBackground>();
            xrOrigin.Camera = cam;

            // THE actual missing piece that kept pose from ever reaching
            // Blender: without a TrackedPoseDriver, ARCameraManager/ARSession
            // initialize the subsystem but nothing ever writes the tracked
            // pose onto this transform. Bound directly to HandheldARInputDevice
            // (bare, non-asset-backed InputActions — same pattern the old app
            // used) rather than an input action asset, and ignoreTrackingState
            // is required since that device exposes no trackingState control
            // for the driver to gate on.
            var poseDriver = cameraGo.AddComponent<TrackedPoseDriver>();
            poseDriver.positionInput = new InputActionProperty(
                new InputAction("ArDevicePosition", binding: "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3"));
            poseDriver.rotationInput = new InputActionProperty(
                new InputAction("ArDeviceRotation", binding: "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion"));
            poseDriver.ignoreTrackingState = true;
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            var appRootGo = new GameObject("AppShell");
            appRootGo.AddComponent<CamLinkPro.UI.AppShell>();

            System.IO.Directory.CreateDirectory("Assets/CamLinkPro/Scenes");
            EditorSceneManager.SaveScene(scene, AppScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(AppScenePath, true) };

            Debug.Log("[SceneBootstrap] Main app scene created and set as sole build scene.");
        }
    }
}
