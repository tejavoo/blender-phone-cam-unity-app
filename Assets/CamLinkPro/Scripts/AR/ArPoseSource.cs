using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace CamLinkPro.AR
{
    /// <summary>
    /// Thin glue reading the current AR camera pose and tracking state each frame.
    /// Everything that turns this into a Blender-ready pose lives in
    /// <see cref="Pipeline.PosePipeline"/>, which knows nothing about AR Foundation --
    /// this component's only job is to hand it plain values.
    ///
    /// Attach to the AR Camera GameObject, alongside ARCameraManager and a
    /// TrackedPoseDriver (Input System) bound to &lt;HandheldARInputDevice&gt;
    /// (the phone-AR-specific device, not the VR-headset-oriented XRHMD). The AR
    /// Session GameObject must also carry an ARInputManager component -- without
    /// it, the XRInputSubsystem that creates that device never starts, and
    /// TrackedPoseDriver silently reads nothing forever.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ArPoseSource : MonoBehaviour
    {
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] ARSession session;

        // A TrackedPoseDriver wired programmatically to a bare (non-asset-backed)
        // InputAction, rather than an InputActionReference, isn't auto-enabled by
        // anything -- an InputAction that's never had Enable() called on it
        // silently reads as zero forever, even with a perfectly valid binding.
        // Enabling it here, every time this component wakes up, makes that not
        // depend on editor-only state or scene-save timing.
        UnityEngine.InputSystem.XR.TrackedPoseDriver trackedPoseDriver;

        Camera cam;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        /// <summary>Exposed so the QR scanner can pause the AR session (and
        /// with it, its hold on the physical camera) while it briefly opens
        /// its own separate WebCamTexture -- two simultaneous camera clients
        /// on Android leads to resource contention where the second feed can
        /// freeze/black out after a few seconds, and doesn't always recover
        /// even once that screen is closed.</summary>
        public ARSession Session => session;

        /// <summary>False while the AR system reports the pose as genuinely
        /// unreliable/lost (not just noisy) -- session not actively tracking.</summary>
        public bool TrackingReliable => ARSession.state == ARSessionState.SessionTracking;

        /// <summary>Focal length (mm) equivalent to the AR camera's current live
        /// field of view, against <see cref="ZoomState.SensorWidthMm"/>.</summary>
        public float LiveFocalLengthMm
        {
            get
            {
                if (cam == null) cam = GetComponent<Camera>();
                return ZoomState.FocalLengthFromVerticalFov(cam.fieldOfView, cam.aspect, ZoomState.SensorWidthMm);
            }
        }

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (cameraManager == null) cameraManager = GetComponent<ARCameraManager>();
            if (session == null) session = FindAnyObjectByType<ARSession>();

            trackedPoseDriver = GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            EnsureTrackedPoseActionsEnabled();
        }

        void OnEnable() => EnsureTrackedPoseActionsEnabled();

        void EnsureTrackedPoseActionsEnabled()
        {
            if (trackedPoseDriver == null) return;
            var posAction = trackedPoseDriver.positionInput.action;
            var rotAction = trackedPoseDriver.rotationInput.action;
            if (posAction != null && !posAction.enabled) posAction.Enable();
            if (rotAction != null && !rotAction.enabled) rotAction.Enable();
        }
    }
}
