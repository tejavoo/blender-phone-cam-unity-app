using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;

namespace CamLinkPro.Ar
{
    /// Owns the AR camera session lifecycle. The session must be enabled only
    /// when entering the Recording HUD flow (right before the "Starting
    /// camera..." preflight screen) and disabled on every path back out (Home,
    /// Settings, QR-scan cancel, Landing) — an always-on AR session drains
    /// battery/heat and fights the QR scanner for the camera resource.
    ///
    /// Gotcha (see llms_complete_specs.txt section 3): if AR tracking never
    /// becomes ready with no other symptom, check that the ARSession GameObject
    /// also has whatever input-device-starting component this AR Foundation
    /// version needs — it fails silently (pose reads static/zero) rather than
    /// throwing.
    public sealed class ArSessionController : MonoBehaviour
    {
        // Found at runtime rather than serialized-field-assigned — avoids
        // editor-scene cross-reference authoring issues.
        ARSession arSession;
        ARCameraManager arCameraManager;
        TrackedPoseDriver poseDriver;

        void FindComponents()
        {
            if (arSession == null)
                arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
            if (arCameraManager == null)
                arCameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
            if (poseDriver == null && arCameraManager != null)
                poseDriver = arCameraManager.GetComponent<TrackedPoseDriver>();
        }

        public bool TrackingReliable
        {
            get
            {
                FindComponents();
                return arSession != null && ARSession.state == ARSessionState.SessionTracking;
            }
        }

        public Transform CameraTransform => arCameraManager != null ? arCameraManager.transform : null;

        public void EnableSession()
        {
            FindComponents();
            if (arSession != null)
                arSession.enabled = true;
            EnsurePoseActionsEnabled();
        }

        // A TrackedPoseDriver wired programmatically to a bare (non-asset-backed)
        // InputAction isn't auto-enabled by anything — an InputAction that's
        // never had Enable() called on it silently reads as zero forever, even
        // with a perfectly valid binding. This is the exact bug that kept pose
        // from ever reaching Blender; confirmed against the old app's fix.
        void EnsurePoseActionsEnabled()
        {
            if (poseDriver == null)
                return;

            var posAction = poseDriver.positionInput.action;
            var rotAction = poseDriver.rotationInput.action;
            if (posAction != null && !posAction.enabled)
                posAction.Enable();
            if (rotAction != null && !rotAction.enabled)
                rotAction.Enable();
        }

        public void DisableSession()
        {
            FindComponents();
            if (arSession != null)
                arSession.enabled = false;
        }

        void Awake()
        {
            // Start disabled — a caller must explicitly EnableSession() when
            // entering the Recording HUD flow.
            DisableSession();
        }

        public float GetVerticalFovDeg()
        {
            if (arCameraManager != null && arCameraManager.TryGetIntrinsics(out var intrinsics))
            {
                var verticalFovRad = 2f * Mathf.Atan(intrinsics.resolution.y / (2f * intrinsics.focalLength.y));
                return verticalFovRad * Mathf.Rad2Deg;
            }

            var cam = arCameraManager != null ? arCameraManager.GetComponent<Camera>() : null;
            return cam != null ? cam.fieldOfView : 60f;
        }
    }
}
