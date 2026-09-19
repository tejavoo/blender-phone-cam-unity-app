using System.Collections;
using System.Collections.Generic;
using CamLinkPro.App;
using CamLinkPro.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace CamLinkPro.UI
{
    /// <summary>
    /// Phase 1 of the uGUI -> UI Toolkit migration: a fully working, visually
    /// faithful rebuild of the Landing screen (see the reviewed mockup,
    /// claude.ai/artifact/2sDLBzMfR4Hdf16u5V9tmW), driven by UI Toolkit
    /// instead of hand-built uGUI.
    ///
    /// Deliberately scoped to Landing only -- Scan QR, Settings, Calibration
    /// and the Recording HUD stay on the existing <see cref="HudController"/>
    /// uGUI Canvas for now (each is its own follow-up migration phase; the
    /// custom controls there -- the zoom rocker, the native Dropdown, the
    /// ScrollRect-based Settings list -- are a materially bigger lift than
    /// Landing's plain buttons/fields, and migrating everything at once in a
    /// single pass is how you end up with a broken app for days instead of a
    /// working one at every checkpoint).
    ///
    /// The seam with the old system: HudController's landingPanel still
    /// exists and still runs all its own logic (RefreshLandingConnectionUi,
    /// the recent-connections list, the Wi-Fi warning poll) exactly as
    /// before, but is made permanently invisible/non-interactive (a
    /// zero-alpha, non-blocking CanvasGroup) the moment it's built --
    /// this component is Landing's only visible, interactive form now.
    /// HudController.ShowScreen shows/hides this GameObject in lockstep with
    /// landingPanel so every existing "go back to Landing" call site
    /// (Home, Cancel, Unpair, ...) keeps working unmodified.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LandingScreenUITK : MonoBehaviour
    {
        [SerializeField] CamLinkSessionController controller;
        [SerializeField] HudController hud;

        UIDocument document;

        Label creditHeart;
        Label wifiWarningLabel;
        VisualElement recentConnectionsRow;

        VisualElement pairingGroup;
        VisualElement manualPanel;
        Label manualConfirmLabel;
        Label manualErrorLabel;
        TextField ipField, posePortField, videoPortField, tokenField;
        Button manualConnectButton;

        VisualElement connectedGroup;
        VisualElement statusDot;
        Label statusHeadlineLabel;
        Label statusDetailLabel;

        Button scanQrButton;
        Button enterManuallyButton;
        Button settingsButton;
        Button letsRecordButton;
        Button repairButton;

        static readonly Color ChipGreen = new Color(0.239f, 0.839f, 0.549f);
        static readonly Color ChipYellow = new Color(0.910f, 0.702f, 0.224f);
        static readonly Color ChipRed = new Color(0.898f, 0.282f, 0.302f);

        void Awake()
        {
            document = GetComponent<UIDocument>();
            var root = document.rootVisualElement;

            root.RegisterCallback<PointerDownEvent>(evt => Debug.Log($"CamLinkPro-UITK: PointerDownEvent at {evt.position}"));
            root.RegisterCallback<ClickEvent>(evt => Debug.Log($"CamLinkPro-UITK: ClickEvent on {evt.target}"));
            Debug.Log("CamLinkPro-UITK: panel = " + root.panel + " panel.visualTree = " + (root.panel != null ? root.panel.visualTree.ToString() : "null"));

            creditHeart = root.Q<Label>("heart-glyph");
            wifiWarningLabel = root.Q<Label>("wifi-warning-label");
            recentConnectionsRow = root.Q<VisualElement>("recent-connections-row");

            pairingGroup = root.Q<VisualElement>("pairing-group");
            manualPanel = root.Q<VisualElement>("manual-panel");
            manualConfirmLabel = root.Q<Label>("manual-confirm-label");
            manualErrorLabel = root.Q<Label>("manual-error-label");
            ipField = root.Q<TextField>("ip-field");
            posePortField = root.Q<TextField>("pose-port-field");
            videoPortField = root.Q<TextField>("video-port-field");
            tokenField = root.Q<TextField>("token-field");
            manualConnectButton = root.Q<Button>("manual-connect-button");

            connectedGroup = root.Q<VisualElement>("connected-group");
            statusDot = root.Q<VisualElement>("status-dot");
            statusHeadlineLabel = root.Q<Label>("status-headline-label");
            statusDetailLabel = root.Q<Label>("status-detail-label");

            scanQrButton = root.Q<Button>("scan-qr-button");
            enterManuallyButton = root.Q<Button>("enter-manually-button");
            settingsButton = root.Q<Button>("settings-button");
            letsRecordButton = root.Q<Button>("lets-record-button");
            repairButton = root.Q<Button>("repair-button");

            SetPlaceholder(ipField, "IP address");
            SetPlaceholder(posePortField, "Pose UDP port");
            SetPlaceholder(videoPortField, "Video/command TCP port");
            SetPlaceholder(tokenField, "Token");

            scanQrButton.clicked += () => hud?.NavigateToScanQr();
            enterManuallyButton.clicked += () =>
            {
                bool opening = manualPanel.ClassListContains("hidden");
                manualPanel.EnableInClassList("hidden", !opening);
                if (opening) SetManualConfirm(null);
            };
            manualConnectButton.clicked += SubmitManualEntry;
            settingsButton.clicked += () => hud?.NavigateToSettingsFromLanding();
            letsRecordButton.clicked += () => hud?.NavigateToRecordingHud();
            repairButton.clicked += () => controller?.Unpair();

            if (controller != null)
            {
                controller.OnPairedChanged += RefreshPairedState;
                controller.OnChannelStateChanged += _ => RefreshPairedState(controller.IsPaired);
                controller.OnBlenderLiveStateChanged += _ => RefreshPairedState(controller.IsPaired);
            }

            PrefillFromLastPairing();
            RefreshPairedState(controller != null && controller.IsPaired);

            creditHeart.schedule.Execute(PulseHeart).Every(33);
        }

        void OnEnable()
        {
            // ReconnectToEventSystemNextFrame() (toggling UIDocument.enabled)
            // is DISABLED for this test -- on-device diagnostics showed it
            // poisons the panel's Yoga layout tree: every element under the
            // recreated root (LandingUITK-container) reports NaN
            // layout/worldBound forever afterward, while panel.visualTree
            // itself (created before the toggle) stays valid. That's the
            // real explanation for "renders fine, never hit-tests" -- not
            // an EventSystem wiring gap. Testing without it.
            StartCoroutine(LogPanelStateOnceSettled());
            InputSystem.onEvent += LogRawPointerEvent;
        }

        void OnDisable() => InputSystem.onEvent -= LogRawPointerEvent;

        void LogRawPointerEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!(device is Touchscreen) && !(device is Mouse)) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            Debug.Log($"CamLinkPro-INPUT: raw event from {device.displayName} ({device.GetType().Name}) type={eventPtr.type}");
        }

        /// <summary>One-shot dump of the actual on-device panel/screen geometry
        /// a few frames after this screen becomes active, once layout has
        /// settled past the NaN-sized first frame -- so we can compare it
        /// against Screen.width/height/safeArea and catch a coordinate-space
        /// mismatch (a common real cause of "renders fine, never hit-tests"
        /// on device even when Editor raycasts succeed).</summary>
        IEnumerator LogPanelStateOnceSettled()
        {
            for (int i = 0; i < 30; i++) yield return null;
            foreach (var dev in InputSystem.devices)
                Debug.Log($"CamLinkPro-DEVICE: {dev.displayName} ({dev.GetType().Name}) added={dev.added} enabled={dev.enabled}");

            var es = EventSystem.current;
            Debug.Log($"CamLinkPro-MODULE: EventSystem.current={(es != null ? es.name : "null")} " +
                      $"currentInputModule={(es != null && es.currentInputModule != null ? es.currentInputModule.GetType().FullName : "null")}");
            if (es != null)
            {
                var uiModule = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if (uiModule != null)
                {
                    Debug.Log($"CamLinkPro-MODULE: uiModule.enabled={uiModule.enabled} actionsAsset={(uiModule.actionsAsset != null ? uiModule.actionsAsset.name : "null")} " +
                              $"pointAction={(uiModule.point != null ? uiModule.point.action?.name : "null")} " +
                              $"leftClickAction={(uiModule.leftClick != null ? uiModule.leftClick.action?.name : "null")}");
                    if (uiModule.point?.action != null)
                        Debug.Log($"CamLinkPro-MODULE: point.action.enabled={uiModule.point.action.enabled} bindings={uiModule.point.action.bindings.Count}");
                    if (uiModule.leftClick?.action != null)
                        Debug.Log($"CamLinkPro-MODULE: leftClick.action.enabled={uiModule.leftClick.action.enabled} bindings={uiModule.leftClick.action.bindings.Count}");
                    if (uiModule.actionsAsset != null)
                        Debug.Log($"CamLinkPro-MODULE: actionsAsset.enabled={uiModule.actionsAsset.enabled}");
                }
                else
                {
                    Debug.Log("CamLinkPro-MODULE: no InputSystemUIInputModule component found on EventSystem GO");
                }
                var raycasters = es.GetComponents<BaseRaycaster>();
                Debug.Log($"CamLinkPro-MODULE: raycasters on EventSystem GO = {raycasters.Length}");
            }
            var allRaycasters = Object.FindObjectsByType<BaseRaycaster>(FindObjectsSortMode.None);
            foreach (var r in allRaycasters)
            {
                Debug.Log($"CamLinkPro-MODULE: scene raycaster {r.GetType().Name} on {r.gameObject.name} enabled={r.enabled} activeInHierarchy={r.gameObject.activeInHierarchy}");
                if (r.GetType().Name == "PanelRaycaster") cachedPanelRaycaster = r;
            }
            Debug.Log($"CamLinkPro-MODULE: cachedPanelRaycaster = {(cachedPanelRaycaster != null ? cachedPanelRaycaster.name : "NULL")}");

            var panel = document != null ? document.rootVisualElement?.panel : null;
            Debug.Log($"CamLinkPro-GEOM: Screen=({Screen.width}x{Screen.height}) safeArea={Screen.safeArea} " +
                      $"orientation={Screen.orientation} dpi={Screen.dpi}");
            if (panel != null)
                Debug.Log($"CamLinkPro-GEOM: panel.visualTree.layout={panel.visualTree.layout} " +
                          $"panel.visualTree.worldBound={panel.visualTree.worldBound}");

            // Bisect the ancestor chain from scan-qr-button up to root -- one
            // of these is the first link where layout/worldBound goes NaN.
            VisualElement cur = scanQrButton;
            int depth = 0;
            while (cur != null && depth < 12)
            {
                Debug.Log($"CamLinkPro-GEOM: chain[{depth}] name={cur.name} type={cur.GetType().Name} " +
                          $"layout={cur.layout} worldBound={cur.worldBound} " +
                          $"style.scale={cur.resolvedStyle.scale} style.width={cur.resolvedStyle.width} style.height={cur.resolvedStyle.height} " +
                          $"classes=[{string.Join(",", cur.GetClasses())}]");
                cur = cur.parent;
                depth++;
            }

            // Sanity check: does a plain Label without the .btn transition
            // classes resolve fine, or is NaN universal past the root?
            if (creditHeart != null)
                Debug.Log($"CamLinkPro-GEOM: creditHeart(Label) layout={creditHeart.layout} worldBound={creditHeart.worldBound}");
        }

        /// <summary>Candidate fix for the runtime-panel input bug described in
        /// HudController.BuildLandingPanel's UiToolkitMigrationEnabled note:
        /// taps were silently swallowed even after the documented (if
        /// obsolete) EventSystem.SetUITookitEventSystemOverride() call.
        /// This tries the supported, non-obsolete alternative instead --
        /// toggling UIDocument.enabled forces it to tear down and rebuild
        /// its runtime-panel registration against EventSystem.current, one
        /// frame after this GameObject activates (so EventSystem is
        /// guaranteed alive first). Untested on-device; if taps still don't
        /// land after this, the uGUI landingPanel remains the real fallback
        /// -- see UiToolkitMigrationEnabled.</summary>
        IEnumerator ReconnectToEventSystemNextFrame()
        {
            yield return null;
            if (document != null && EventSystem.current != null)
            {
                document.enabled = false;
                document.enabled = true;
                Debug.Log("CamLinkPro-UITK: re-enabled UIDocument against EventSystem " + EventSystem.current.name);
            }
        }

        void PulseHeart()
        {
            float t = Time.realtimeSinceStartup;
            float s = 1f + 0.18f * Mathf.Max(0f, Mathf.Sin(t * 3.2f));
            creditHeart.style.scale = new StyleScale(new Scale(new Vector2(s, s)));
        }

        bool wasTouchPressedLastFrame;
        BaseRaycaster cachedPanelRaycaster;

        void Update()
        {
            // On-device diagnostic: bypass EventSystem/InputModule entirely --
            // when a raw touch press is detected, convert its position to
            // panel space ourselves and ask UI Toolkit's own Pick() whether
            // it finds anything there. If this finds scan-qr-button but no
            // PointerDownEvent ever fires, the bug is in EventSystem's
            // dispatch pipeline (module/raycaster wiring), not picking
            // itself; if Pick() ALSO fails on-device, it's a genuine
            // picking/coordinate-space bug specific to this platform.
            // Don't rely on Touchscreen.current -- adb-injected taps arrive
            // as a distinct "Virtual (FastTouchscreen)" device that may
            // never become "current". Scan every touchscreen device
            // directly instead.
            bool pressed = false;
            Vector2 screenPos = default;
            foreach (var dev in InputSystem.devices)
            {
                if (dev is Touchscreen tsd && tsd.primaryTouch.press.isPressed)
                {
                    pressed = true;
                    screenPos = tsd.primaryTouch.position.ReadValue();
                    break;
                }
            }
            if (pressed && !wasTouchPressedLastFrame && document != null)
            {
                var panel = document.rootVisualElement?.panel;
                if (panel != null)
                {
                    var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
                    var picked = panel.Pick(panelPos);
                    Debug.Log($"CamLinkPro-TOUCH: screenPos={screenPos} -> panelPos={panelPos} Pick()={(picked != null ? picked.name : "NOTHING")}");
                }

                // Exactly mirror what InputSystemUIInputModule does: build a
                // PointerEventData at this screen position and call the
                // actual PanelRaycaster.Raycast() the module calls -- not
                // panel.Pick() -- to see whether THIS specific call path
                // (as opposed to picking itself) is what's failing on-device.
                Debug.Log($"CamLinkPro-RAYCAST: cachedPanelRaycaster={(cachedPanelRaycaster != null ? "present" : "NULL")} EventSystem.current={(EventSystem.current != null ? "present" : "NULL")}");
                if (cachedPanelRaycaster != null && EventSystem.current != null)
                {
                    var ped = new PointerEventData(EventSystem.current) { position = screenPos };
                    var results = new System.Collections.Generic.List<RaycastResult>();
                    cachedPanelRaycaster.Raycast(ped, results);
                    Debug.Log($"CamLinkPro-RAYCAST: PanelRaycaster.Raycast at {screenPos} returned {results.Count} hits");
                    var allResults = new System.Collections.Generic.List<RaycastResult>();
                    EventSystem.current.RaycastAll(ped, allResults);
                    Debug.Log($"CamLinkPro-RAYCAST: EventSystem.RaycastAll returned {allResults.Count} hits");
                }
            }
            wasTouchPressedLastFrame = pressed;

            if (wifiWarningLabel == null || pairingGroup == null || pairingGroup.ClassListContains("hidden")) return;
            var reachability = Application.internetReachability;
            bool onLan = reachability == NetworkReachability.ReachableViaLocalAreaNetwork;
            wifiWarningLabel.text = reachability == NetworkReachability.ReachableViaCarrierDataNetwork
                ? "On mobile data, not Wi-Fi -- turn on Wi-Fi to pair"
                : "Wi-Fi is off -- turn it on to pair";
            wifiWarningLabel.EnableInClassList("hidden", onLan);
        }

        void PrefillFromLastPairing()
        {
            var info = PairingInfoStore.Load();
            SetRealValue(ipField, info.Ip ?? "", "IP address");
            SetRealValue(posePortField, info.PosePort > 0 ? info.PosePort.ToString() : "", "Pose UDP port");
            SetRealValue(videoPortField, info.VideoPort > 0 ? info.VideoPort.ToString() : "", "Video/command TCP port");
            SetRealValue(tokenField, info.Token ?? "", "Token");
            RebuildRecentConnections();
        }

        /// <summary>Called by HudController's QR-scan hand-off so a scanned
        /// code prefills these same fields for review, matching the old
        /// uGUI flow's behaviour exactly.</summary>
        public void PrefillFromScan(PairingInfo info)
        {
            SetRealValue(ipField, info.Ip ?? "", "IP address");
            SetRealValue(posePortField, info.PosePort > 0 ? info.PosePort.ToString() : "", "Pose UDP port");
            SetRealValue(videoPortField, info.VideoPort > 0 ? info.VideoPort.ToString() : "", "Video/command TCP port");
            SetRealValue(tokenField, info.Token ?? "", "Token");
            manualPanel.RemoveFromClassList("hidden");
            SetManualConfirm("QR scanned successfully -- review and connect below.");
        }

        static void SetRealValue(TextField field, string value, string placeholder)
        {
            if (string.IsNullOrEmpty(value))
            {
                field.SetValueWithoutNotify(placeholder);
                field.AddToClassList("text-field__placeholder-active");
            }
            else
            {
                field.SetValueWithoutNotify(value);
                field.RemoveFromClassList("text-field__placeholder-active");
            }
        }

        void RebuildRecentConnections()
        {
            recentConnectionsRow.Clear();
            List<PairingInfo> history = PairingHistoryStore.Load();
            recentConnectionsRow.EnableInClassList("hidden", history.Count == 0);
            foreach (var entry in history)
            {
                var info = entry; // capture
                var chip = new Button(() => controller?.Pair(info)) { text = $"{info.Ip}:{info.VideoPort}" };
                chip.AddToClassList("chip-pill");
                recentConnectionsRow.Add(chip);
            }
        }

        void SubmitManualEntry()
        {
            string ip = RealValue(ipField).Trim();
            string token = RealValue(tokenField).Trim();
            bool okPose = int.TryParse(RealValue(posePortField), out int posePort);
            bool okVideo = int.TryParse(RealValue(videoPortField), out int videoPort);
            var info = new PairingInfo { Ip = ip, PosePort = posePort, VideoPort = videoPort, Token = token };

            if (string.IsNullOrEmpty(ip) || !okPose || !okVideo || string.IsNullOrEmpty(token) || !info.IsValid)
            {
                SetManualError("Enter a valid IP, both ports (1-65535), and the token from the pairing QR.");
                return;
            }
            SetManualError(null);
            controller?.Pair(info);
        }

        void SetManualError(string message)
        {
            manualErrorLabel.text = message ?? "";
            manualErrorLabel.EnableInClassList("hidden", string.IsNullOrEmpty(message));
        }

        void SetManualConfirm(string message)
        {
            manualConfirmLabel.text = message ?? "";
            manualConfirmLabel.EnableInClassList("hidden", string.IsNullOrEmpty(message));
        }

        static string RealValue(TextField field) =>
            field.ClassListContains("text-field__placeholder-active") ? "" : field.value;

        static void SetPlaceholder(TextField field, string placeholder)
        {
            // UI Toolkit's TextField has no built-in placeholder -- fake it
            // the standard way: grey placeholder text shown only while the
            // field is genuinely empty (not just unfocused).
            field.value = placeholder;
            field.AddToClassList("text-field__placeholder-active");
            field.RegisterCallback<FocusInEvent>(_ =>
            {
                if (field.ClassListContains("text-field__placeholder-active"))
                {
                    field.SetValueWithoutNotify("");
                    field.RemoveFromClassList("text-field__placeholder-active");
                }
            });
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (string.IsNullOrEmpty(field.value))
                {
                    field.SetValueWithoutNotify(placeholder);
                    field.AddToClassList("text-field__placeholder-active");
                }
            });
        }

        void RefreshPairedState(bool paired)
        {
            pairingGroup.EnableInClassList("hidden", paired);
            connectedGroup.EnableInClassList("hidden", !paired);
            letsRecordButton.SetEnabled(paired);
            if (!paired) RebuildRecentConnections();

            if (!paired || controller == null) return;

            var p = controller.CurrentPairing;
            string headline;
            Color dotColor;
            if (controller.ChannelState == ChannelState.Connected)
            {
                headline = controller.BlenderLiveState switch
                {
                    BlenderLiveState.Live => $"Live -- driving camera ({p.Ip})",
                    BlenderLiveState.LiveNoData => $"Live, no pose data -- {p.Ip}",
                    BlenderLiveState.Connected => $"Connected to {p.Ip} -- Blender not live",
                    _ => $"Connected to {p.Ip}",
                };
                dotColor = controller.BlenderLiveState == BlenderLiveState.Live ? ChipGreen : ChipYellow;
            }
            else if (controller.ChannelState == ChannelState.Connecting)
            {
                headline = $"Paired to {p.Ip} -- connecting...";
                dotColor = ChipYellow;
            }
            else
            {
                headline = $"Paired to {p.Ip} -- not reachable";
                dotColor = ChipRed;
            }

            statusHeadlineLabel.text = headline;
            statusDetailLabel.text = $"pose:{p.PosePort}  video/cmd:{p.VideoPort}";
            statusDot.style.backgroundColor = dotColor;
        }
    }
}
