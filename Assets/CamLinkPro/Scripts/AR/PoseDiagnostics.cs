using System.Collections;
using System.Collections.Generic;
using System.Text;
using CamLinkPro.App;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace CamLinkPro.AR
{
    /// <summary>
    /// Optional, verbose diagnostics for the AR pose pipeline -- dumps every
    /// plausible pose source (XR subsystem states, all connected Input System
    /// devices, the TrackedPoseDriver's bound actions, legacy InputTracking) to
    /// logcat, plus a running timeline of ARSession state transitions. This is
    /// exactly what pinned down a real bug once (a missing ARInputManager
    /// component silently leaving XRInputSubsystem stopped forever), so it stays
    /// in the app rather than being deleted -- just gated behind the
    /// Settings screen's "Diagnostics Overlay" toggle so it isn't spamming
    /// logcat by default.
    /// </summary>
    [RequireComponent(typeof(ArPoseSource))]
    public sealed class PoseDiagnostics : MonoBehaviour
    {
        UnityEngine.InputSystem.XR.TrackedPoseDriver trackedPoseDriver;
        bool everEnabled;

        void Awake()
        {
            trackedPoseDriver = GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        }

        void OnEnable() => ARSession.stateChanged += OnSessionStateChanged;
        void OnDisable() => ARSession.stateChanged -= OnSessionStateChanged;

        void Update()
        {
            // Fire the first full dump a few seconds after diagnostics are first
            // turned on (giving ARCore time to reach SessionTracking), whether
            // that's at app start or from a mid-session Settings toggle.
            if (AppPreferences.DiagnosticsOverlayEnabled && !everEnabled)
            {
                everEnabled = true;
                StartCoroutine(DumpAfterDelay(6f));
            }
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            if (!AppPreferences.DiagnosticsOverlayEnabled) return;
            Debug.Log($"CamLinkPro: ARSession state -> {args.state} at t={Time.realtimeSinceStartup:F1}s, notTrackingReason={ARSession.notTrackingReason}");
        }

        /// <summary>Dumps immediately, e.g. wired to a button on the Settings
        /// screen, without waiting for the startup delay.</summary>
        public void DumpNow() => StartCoroutine(DumpAfterDelay(0f));

        IEnumerator DumpAfterDelay(float seconds)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
            if (!AppPreferences.DiagnosticsOverlayEnabled) yield break;

            var sb = new StringBuilder();
            sb.AppendLine("=== CamLinkPro pose diagnostics ===");
            sb.AppendLine($"ARSession.state: {ARSession.state}");

            var xrSettings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            if (xrSettings == null || xrSettings.Manager == null)
            {
                sb.AppendLine("XRGeneralSettings.Instance or Manager is null.");
            }
            else
            {
                var manager = xrSettings.Manager;
                sb.AppendLine($"XR Manager isInitializationComplete: {manager.isInitializationComplete}");
                sb.AppendLine($"XR Manager activeLoader: {(manager.activeLoader == null ? "null" : manager.activeLoader.GetType().FullName)}");
                if (manager.activeLoader != null)
                {
                    var inputSub = manager.activeLoader.GetLoadedSubsystem<UnityEngine.XR.XRInputSubsystem>();
                    var sessionSub = manager.activeLoader.GetLoadedSubsystem<UnityEngine.XR.ARSubsystems.XRSessionSubsystem>();
                    var cameraSub = manager.activeLoader.GetLoadedSubsystem<UnityEngine.XR.ARSubsystems.XRCameraSubsystem>();
                    sb.AppendLine($"XRInputSubsystem: {(inputSub == null ? "NULL (not loaded)" : $"running={inputSub.running}")}");
                    sb.AppendLine($"XRSessionSubsystem: {(sessionSub == null ? "NULL" : $"running={sessionSub.running}, trackingState={sessionSub.trackingState}")}");
                    sb.AppendLine($"XRCameraSubsystem: {(cameraSub == null ? "NULL" : $"running={cameraSub.running}")}");
                }
            }

            sb.AppendLine($"-- Input System devices ({UnityEngine.InputSystem.InputSystem.devices.Count}) --");
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
                sb.AppendLine($"  {d.displayName} | layout={d.layout} | added={d.added}");

            if (trackedPoseDriver != null)
            {
                var posAction = trackedPoseDriver.positionInput.action;
                var rotAction = trackedPoseDriver.rotationInput.action;
                sb.AppendLine($"-- TrackedPoseDriver -- posAction enabled={posAction?.enabled}, controls={posAction?.controls.Count}, value={(posAction != null && posAction.controls.Count > 0 ? posAction.ReadValue<Vector3>().ToString() : "n/a")}");
                sb.AppendLine($"                       rotAction enabled={rotAction?.enabled}, controls={rotAction?.controls.Count}");
            }
            else
            {
                sb.AppendLine("No TrackedPoseDriver component found.");
            }

            Vector3 legacyPos = UnityEngine.XR.InputTracking.GetLocalPosition(UnityEngine.XR.XRNode.CenterEye);
            Quaternion legacyRot = UnityEngine.XR.InputTracking.GetLocalRotation(UnityEngine.XR.XRNode.CenterEye);
            sb.AppendLine($"Legacy InputTracking CenterEye: pos={legacyPos}, rot={legacyRot.eulerAngles}");

            var nodeStates = new List<UnityEngine.XR.XRNodeState>();
            UnityEngine.XR.InputTracking.GetNodeStates(nodeStates);
            sb.AppendLine($"-- XRNodeState list ({nodeStates.Count}) --");
            foreach (var n in nodeStates)
            {
                n.TryGetPosition(out var np);
                n.TryGetRotation(out var nr);
                sb.AppendLine($"  node={n.nodeType} tracked={n.tracked} pos={np} rot={nr.eulerAngles}");
            }

            sb.AppendLine($"transform.position (this AR Camera): {transform.position}");
            sb.AppendLine("=== end diagnostics ===");

            Debug.Log(sb.ToString());
        }
    }
}
