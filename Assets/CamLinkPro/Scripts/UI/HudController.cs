using System.Collections.Generic;
using CamLinkPro.App;
using CamLinkPro.Networking;
using CamLinkPro.Pairing;
using CamLinkPro.Pipeline;
using UnityEngine;
using UnityEngine.UI;

namespace CamLinkPro.UI
{
    /// <summary>
    /// Builds and drives the entire on-screen UI at runtime, across five screens:
    ///
    ///   Landing (home) -- title/banner, pairing (recent connections, scan, or
    ///     manual entry) or connection status + re-pair if already paired, a
    ///     Settings button, and "Let's Record" (enabled once paired) into the
    ///     Recording screen.
    ///   Scan QR -- its own screen so the device camera is only ever live while
    ///     actually scanning, never sitting on in the background on Landing.
    ///   Recording -- the live HUD: rig presets, freeze toggles, steadiness,
    ///     zoom, dolly, reset origin, opacity, record flow, connection health
    ///     chip + latency, video overlay. A Home button returns to Landing
    ///     without losing the connection.
    ///   Settings -- per-axis output calibration, horizon-level, app-level
    ///     toggles (diagnostics overlay, keep screen awake), and connection
    ///     details (editable, with Reconnect/Unpair-with-confirmation).
    ///     Reachable from either other screen; Back returns to whichever one
    ///     opened it.
    ///   Calibration -- reached via a button on Settings: per-axis (X/Y/Z,
    ///     Tilt-X/Roll-Y/Pan-Z) editable ROP/ROR starting-point offsets, with
    ///     a capture-current and clear action for each. Back always returns
    ///     to Settings.
    ///
    /// Built in code rather than hand-authored as a prefab so the whole UI stays
    /// in one reviewable, versioned place; every interactive control gives clear
    /// pressed/highlighted feedback.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] CamLinkSessionController controller;
        [SerializeField] QRPairingScanner qrScanner;
        [SerializeField] ManualPairingEntry manualEntry;

        /// <summary>The UI Toolkit Landing screen's GameObject (see
        /// <see cref="LandingScreenUITK"/>) -- shown/hidden in lockstep with
        /// the old (now permanently invisible) uGUI landingPanel, so this is
        /// the only piece of "Phase 1 migration" wiring every existing
        /// ShowScreen(landingPanel) call site needed.</summary>
        [SerializeField] GameObject uiToolkitLandingRoot;
        [SerializeField] LandingScreenUITK uiToolkitLandingScreen;

        static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color ButtonBg = new Color(1f, 1f, 1f, 0.15f);
        static readonly Color ButtonPressed = new Color(0.2f, 0.7f, 1f, 0.85f);
        static readonly Color ButtonDisabled = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color ButtonActiveBg = new Color(0.2f, 0.7f, 1f, 0.55f);
        static readonly Color DangerBg = new Color(0.8f, 0.25f, 0.2f, 0.55f);
        static readonly Color DangerPressed = new Color(1f, 0.35f, 0.25f, 0.9f);
        static readonly Color ChipGreen = new Color(0.25f, 0.85f, 0.35f, 1f);
        static readonly Color ChipYellow = new Color(0.95f, 0.8f, 0.2f, 1f);
        static readonly Color ChipRed = new Color(0.9f, 0.3f, 0.25f, 1f);

        // -- screens --
        GameObject landingPanel;
        GameObject hudPanel;      // "Recording"
        GameObject settingsPanel;
        GameObject calibrationPanel; // ROP/ROR per-axis, reachable from Settings
        GameObject scanScreenPanel;
        GameObject qrTimeoutOverlay;
        float qrScanStartedAtUnscaled;
        const float QrScanTimeoutSeconds = 25f;
        GameObject manualPanel;
        GameObject landingPairingGroup;
        GameObject landingConnectedGroup;
        GameObject recentConnectionsRow;
        GameObject debugOverlayRoot;
        GameObject confirmDialogPanel;
        GameObject startingCameraPanel;
        GameObject storageFullPanel;
        Text storageFullText;

        GameObject screenBeforeSettings;

        Text startingCameraChecklistText;
        float arWarmupStartedAt;
        const float ArWarmupMaxSeconds = 4f;

        GameObject countdownOverlayPanel;
        Text countdownBigText;

        GameObject savedToastPanel;
        Text savedToastText;
        float recordingStartedAtUnscaled;
        float savedToastHideAtUnscaled = -1f;
        const float SavedToastDurationSeconds = 3.5f;

        GameObject disconnectBannerPanel;
        Text disconnectBannerText;

        Text landingStatusText;
        Image landingConnectionChip;
        Button letsRecordButton;

        Text recordStatusText;
        Text zoomValueText;
        Text dollyValueText;
        GameObject zoomButtonsRow;
        GameObject zoomSliderRow;
        Slider zoomSlider;
        GameObject bigZoomSliderRoot;
        Slider bigZoomSlider;
        Text bigZoomFocalText;
        readonly Toggle[] freezeToggles = new Toggle[6];
        readonly Text[] freezeRotationLabels = new Text[3]; // Pan, Tilt, Roll -- matches freezeToggles[3,4,5]
        readonly Text[] calibRotationLabels = new Text[3];  // Tilt, Roll, Pan -- matches rotOffsetFields[0,1,2]
        Dropdown rotationRemapDropdown;
        readonly Button[] rigButtons = new Button[4];
        Button lockStartButton;
        Button recordButton;
        Button stopButton;
        Button cancelButton;

        GameObject statusIndicatorsGroup;
        Image connectionChip;
        Text connectionStateText;
        Text connectionLatencyText;
        Text calibrationBadge;
        GameObject blenderStatusRow;
        Image blenderLiveChip;
        Text blenderLiveText;
        Text poseSendingText;
        Text recordReadinessWarningText;
        GameObject rigRow;
        GameObject freezeRow;
        GameObject bottomBar;

        Toggle statusStripVisibilityToggle;
        Toggle rigPresetRowVisibilityToggle;
        Toggle freezeAxisRowVisibilityToggle;
        Toggle bottomBarVisibilityToggle;
        Toggle recordReadinessWarningVisibilityToggle;

        Text confirmDialogText;
        System.Action confirmDialogAction;

        InputField ipField, posePortField, videoPortField, tokenField;
        Text manualErrorText;
        Text manualConfirmText;
        Text wifiWarningText;

        // Settings screen fields
        readonly InputField[] posScaleFields = new InputField[3];
        readonly InputField[] rotScaleFields = new InputField[3];
        readonly Toggle[] posFlipToggles = new Toggle[3];
        readonly Toggle[] rotFlipToggles = new Toggle[3];
        Toggle horizonLevelToggle;
        Toggle diagnosticsToggle;
        Toggle keepAwakeToggle;
        Toggle zoomSliderToggle;
        InputField recordDelayField;
        Toggle bigZoomVisibilityToggle;
        InputField zoomSensitivityField;
        Text liveCameraReadoutText;
        Text positionOffsetText;
        Text rotationOffsetText;
        readonly InputField[] posOffsetFields = new InputField[3];
        readonly InputField[] rotOffsetFields = new InputField[3];
        InputField settingsIpField, settingsPoseField, settingsVideoField, settingsTokenField;
        Text settingsConnectionError;

        Text debugText;

        RecordUiState previousRecordState = RecordUiState.Live;

        void Awake()
        {
            // UI Toolkit's runtime panels (LandingScreenUITK, Phase 1 of the
            // uGUI -> UI Toolkit migration) read touch through the Input
            // System's Enhanced Touch API directly, which is off by default --
            // without this, taps land fine on the old uGUI screens (routed
            // through EventSystem/InputSystemUIInputModule, unaffected by
            // this flag) but silently do nothing on any UI Toolkit panel,
            // with no error of any kind.
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();

            // Belt-and-suspenders alongside the above: force uGUI's
            // EventSystem to actually create the PanelEventHandler/
            // PanelRaycaster bridge objects a runtime UI Toolkit panel needs
            // to receive EventSystem-routed input at all (see
            // UnityEngine.EventSystems.EventSystem.SetUITookitEventSystemOverride
            // -- obsolete API, but the only public entry point into this,
            // and the automatic default-config path wasn't creating them on
            // its own in this project).
#pragma warning disable CS0618
            var eventSystem = UnityEngine.EventSystems.EventSystem.current
                ?? FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null)
                UnityEngine.EventSystems.EventSystem.SetUITookitEventSystemOverride(eventSystem, sendEvents: true, createPanelGameObjectsOnStart: true);
#pragma warning restore CS0618

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = GetOrAddScaler();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            // Apply the persisted keep-awake preference immediately at startup
            // (the AppPreferences setter only re-applies it on a live toggle).
            Screen.sleepTimeout = AppPreferences.KeepScreenAwake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            BuildLandingPanel();
            BuildScanScreen();
            BuildHudPanel();
            BuildStartingCameraPanel();
            BuildSettingsPanel();
            BuildCalibrationScreen();
            BuildDebugOverlay();
            BuildConfirmDialog();

            // Lazy AR start: the camera (and the battery drain/heat that
            // comes with it) only runs while the Recording HUD is actually
            // open -- see OpenRecordingHud/CloseRecordingHud/SetArSessionEnabled.
            SetArSessionEnabled(false);
            RefreshHudVisibility();

            if (controller != null)
            {
                controller.OnPairedChanged += RefreshLandingConnectionUi;
                controller.OnRecordStateChanged += UpdateRecordUi;
                controller.OnCountdownTick += sec =>
                {
                    recordStatusText.text = $"Starting in {sec}...";
                    if (countdownBigText != null) countdownBigText.text = sec.ToString();
                };
                controller.OnChannelStateChanged += RefreshConnectionChip;
                controller.OnChannelStateChanged += RefreshDisconnectBanner;
                controller.OnChannelStateChanged += _ => RefreshLandingConnectionUi(controller.IsPaired);
                controller.OnBlenderLiveStateChanged += _ => RefreshLandingConnectionUi(controller.IsPaired);
                controller.OnBlenderLiveStateChanged += RefreshBlenderLiveChip;
                controller.OnBlenderLiveStateChanged += RefreshRecordReadinessWarning;
                controller.OnPingUpdated += ms => { if (connectionLatencyText != null) connectionLatencyText.text = $"{ms:0}ms"; };
            }

            RefreshLandingConnectionUi(controller != null && controller.IsPaired);
            RefreshRotationAxisLabels();
            // Events only fire on change, and BlenderLiveState already
            // starts at its default (Unknown) -- without this, the chip
            // would sit at CreateChip's own default red until the first
            // real transition, misrepresenting "no info yet" as an error.
            RefreshBlenderLiveChip(BlenderLiveState.Unknown);
            RefreshRecordReadinessWarning(BlenderLiveState.Unknown);
            ShowScreen(landingPanel);
        }

        // Full-deflection rate for the big zoom rocker, at sensitivity 1x --
        // close to what the +/- repeat buttons already work out to (2mm every
        // ~0.08s once repeating), just continuous instead of stepped.
        const float BigZoomRockerFullRateMmPerSecond = 25f;
        const float BigZoomRockerDeadzone = 0.03f;

        void Update()
        {
            RefreshDebugOverlay();
            RefreshCalibrationBadge();
            RefreshPoseSendingLabel();
            RefreshWifiWarning();
            DriveBigZoomSlider();
            RefreshLiveCameraReadout();
            if (startingCameraPanel != null && startingCameraPanel.activeSelf) RefreshArWarmup();
            if (savedToastHideAtUnscaled >= 0f && Time.unscaledTime >= savedToastHideAtUnscaled)
            {
                savedToastPanel.SetActive(false);
                savedToastHideAtUnscaled = -1f;
            }
            RefreshQrScanTimeout();
        }

        void RefreshQrScanTimeout()
        {
            if (scanScreenPanel == null || !scanScreenPanel.activeSelf) return;
            if (qrTimeoutOverlay == null || qrTimeoutOverlay.activeSelf) return;
            if (Time.unscaledTime - qrScanStartedAtUnscaled < QrScanTimeoutSeconds) return;
            qrScanner?.Close();
            qrTimeoutOverlay.SetActive(true);
        }

        /// <summary>Blender is always reached over the local network, so
        /// mobile data is just as dead-on-arrival as no connection at all --
        /// only actual Wi-Fi/Ethernet clears the warning. Only checked while
        /// Landing's pairing group is what's showing (cheap property read
        /// either way, but no point running it elsewhere).</summary>
        void RefreshWifiWarning()
        {
            if (wifiWarningText == null || landingPairingGroup == null || !landingPairingGroup.activeInHierarchy) return;
            var reachability = Application.internetReachability;
            bool onLan = reachability == NetworkReachability.ReachableViaLocalAreaNetwork;
            wifiWarningText.text = reachability == NetworkReachability.ReachableViaCarrierDataNetwork
                ? "On mobile data, not Wi-Fi -- turn on Wi-Fi to pair"
                : "Wi-Fi is off -- turn it on to pair";
            if (wifiWarningText.gameObject.activeSelf != !onLan) wifiWarningText.gameObject.SetActive(!onLan);
        }

        /// <summary>Polled every frame rather than driven off Slider's
        /// onValueChanged -- that event only fires when the value *changes*,
        /// but a rocker held stationary away from center still needs to keep
        /// zooming for as long as it's held there.</summary>
        void DriveBigZoomSlider()
        {
            if (bigZoomSlider == null || !bigZoomSliderRoot.activeInHierarchy || controller == null) return;

            float push = bigZoomSlider.value;
            if (Mathf.Abs(push) >= BigZoomRockerDeadzone)
            {
                float deltaMm = push * AppPreferences.ZoomSliderSensitivity * BigZoomRockerFullRateMmPerSecond * Time.unscaledDeltaTime;
                controller.NudgeZoom(deltaMm);
                RefreshZoomLabel();
            }

            if (bigZoomFocalText != null) bigZoomFocalText.text = zoomValueText != null ? zoomValueText.text : "";
        }

        void RefreshCalibrationBadge()
        {
            if (calibrationBadge == null || controller == null || !hudPanel.activeSelf) return;
            bool active = controller.Calibration.HasPositionOffset || controller.Calibration.HasRotationOffset;
            if (calibrationBadge.gameObject.activeSelf != active) calibrationBadge.gameObject.SetActive(active);
        }

        /// <summary>Applies the Settings -> App -> HUD Visibility toggles to
        /// the actual HUD rows. Purely cosmetic -- every one of these keeps
        /// working from Settings/its own code path even hidden here, so
        /// turning an element off can't strand anyone.</summary>
        void RefreshHudVisibility()
        {
            if (statusIndicatorsGroup != null) statusIndicatorsGroup.SetActive(AppPreferences.StatusStripVisible);
            if (blenderStatusRow != null) blenderStatusRow.SetActive(AppPreferences.StatusStripVisible);
            if (rigRow != null) rigRow.SetActive(AppPreferences.RigPresetRowVisible);
            if (freezeRow != null) freezeRow.SetActive(AppPreferences.FreezeAxisRowVisible);
            if (bottomBar != null) bottomBar.SetActive(AppPreferences.BottomControlBarVisible);
        }

        /// <summary>Read-only readout of the two Blender-camera values already
        /// on the wire today (focal_length_mm/sensor_width_mm in every pose
        /// packet) -- Settings -> Blender Camera. Polled only while Settings
        /// is open.</summary>
        void RefreshLiveCameraReadout()
        {
            if (liveCameraReadoutText == null || controller == null || !settingsPanel.activeSelf) return;
            liveCameraReadoutText.text = controller.HasRawPose && controller.PoseSource != null
                ? $"{controller.Zoom.Resolve(controller.PoseSource.LiveFocalLengthMm):0.#}mm / {AR.ZoomState.SensorWidthMm:0.#}mm"
                : "-- (no live pose yet)";
        }

        void ShowScreen(GameObject screen)
        {
            landingPanel.SetActive(screen == landingPanel);
            hudPanel.SetActive(screen == hudPanel);
            settingsPanel.SetActive(screen == settingsPanel);
            calibrationPanel.SetActive(screen == calibrationPanel);
            scanScreenPanel.SetActive(screen == scanScreenPanel);
            if (startingCameraPanel != null) startingCameraPanel.SetActive(screen == startingCameraPanel);
            if (storageFullPanel != null) storageFullPanel.SetActive(screen == storageFullPanel);
            // Parked -- see the UiToolkitMigrationEnabled note in
            // BuildLandingPanel(). Kept permanently inactive until the
            // runtime-panel input issue is actually resolved.
            if (uiToolkitLandingRoot != null) uiToolkitLandingRoot.SetActive(false);
            // The always-on pose readout only matters once you're actually
            // shooting -- keep it off the other screens to reduce clutter.
            if (debugOverlayRoot != null) debugOverlayRoot.SetActive(screen == hudPanel);
            if (screen == hudPanel)
            {
                RefreshZoomControlMode();
                RefreshBigZoomSliderVisibility();
            }
        }

        // -- Bridge for the UI Toolkit Landing screen (LandingScreenUITK) --
        // the old uGUI Landing panel this HudController still builds stays
        // permanently invisible (see uiToolkitLandingRoot below); these three
        // just forward to the exact same navigation the old uGUI Landing
        // buttons already used, so migrating Landing's *visuals* didn't need
        // to touch any of the *destination* screens' logic at all.
        internal void NavigateToScanQr() => OpenQrScan();
        internal void NavigateToSettingsFromLanding() => OpenSettingsFrom(landingPanel);
        internal void NavigateToRecordingHud() => OpenRecordingHud();

        void OpenSettingsFrom(GameObject returnScreen)
        {
            screenBeforeSettings = returnScreen;
            RefreshSettingsFromLiveState();
            ShowScreen(settingsPanel);
        }

        void OpenCalibrationScreen()
        {
            RefreshCalibrationOffsetLabels();
            ShowScreen(calibrationPanel);
        }

        CanvasScaler GetOrAddScaler()
        {
            var s = GetComponent<CanvasScaler>();
            if (s == null) s = gameObject.AddComponent<CanvasScaler>();
            return s;
        }

        // -- confirm dialog -----------------------------------------------------

        void BuildConfirmDialog()
        {
            confirmDialogPanel = CreatePanel(transform, "ConfirmDialog", stretch: true);
            confirmDialogPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            var box = CreatePanel(confirmDialogPanel.transform, "Box", stretch: false);
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(560, 220);
            box.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.98f);
            var boxLayout = box.AddComponent<VerticalLayoutGroup>();
            boxLayout.childAlignment = TextAnchor.MiddleCenter;
            boxLayout.spacing = 18;
            boxLayout.padding = new RectOffset(24, 24, 24, 24);

            confirmDialogText = CreateLabel(box.transform, "Are you sure?", 22);
            confirmDialogText.horizontalOverflow = HorizontalWrapMode.Wrap;

            var row = CreateRow(box.transform, "ConfirmRow");
            CreateButton(row.transform, "Cancel", () => confirmDialogPanel.SetActive(false));
            CreateButton(row.transform, "Confirm", () =>
            {
                confirmDialogPanel.SetActive(false);
                confirmDialogAction?.Invoke();
            }, danger: true);

            confirmDialogPanel.SetActive(false);
        }

        void ShowConfirmDialog(string message, System.Action onConfirm)
        {
            confirmDialogText.text = message;
            confirmDialogAction = onConfirm;
            confirmDialogPanel.transform.SetAsLastSibling();
            confirmDialogPanel.SetActive(true);
        }

        // -- debug overlay ----------------------------------------------------------
        // Always-on, lightweight pose readout (distinct from the verbose logcat
        // diagnostics gated behind Settings) -- per the spec's own build-order
        // advice ("verify the numbers look sane in an on-screen debug readout").

        void BuildDebugOverlay()
        {
            debugOverlayRoot = new GameObject("DebugOverlay", typeof(RectTransform), typeof(Image));
            debugOverlayRoot.transform.SetParent(transform, false);
            var rt = debugOverlayRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -10);
            rt.sizeDelta = new Vector2(560, 190);
            debugOverlayRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(debugOverlayRoot.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8, 8);
            textRt.offsetMax = new Vector2(-8, -8);

            debugText = textGo.GetComponent<Text>();
            debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            debugText.fontSize = 16;
            debugText.color = Color.green;
            debugText.alignment = TextAnchor.UpperLeft;
            debugText.horizontalOverflow = HorizontalWrapMode.Overflow;
            debugText.verticalOverflow = VerticalWrapMode.Overflow;
            debugText.text = "waiting for controller...";

            debugOverlayRoot.SetActive(false);
        }

        void RefreshDebugOverlay()
        {
            if (debugText == null || !debugOverlayRoot.activeSelf) return;
            if (controller == null) { debugText.text = "No CamLinkSessionController wired."; return; }

            var poseSource = controller.PoseSource;
            string sessionState = UnityEngine.XR.ARFoundation.ARSession.state.ToString();

            if (poseSource == null)
            {
                debugText.text =
                    $"Paired: {controller.IsPaired}\n" +
                    $"ARSession.state: {sessionState}\n" +
                    "No ArPoseSource wired to SessionController -- pose will never be sent.";
                return;
            }

            Vector3 p = poseSource.Position;
            Vector3 e = poseSource.Rotation.eulerAngles;
            Vector3 blenderRot = controller.LastRawPose.EulerDegrees;

            debugText.text =
                $"Paired: {controller.IsPaired}   Record: {controller.RecordState}\n" +
                $"ARSession.state: {sessionState}\n" +
                $"TrackingReliable: {poseSource.TrackingReliable}   WarmedUp: {controller.Pipeline.IsWarmedUp}\n" +
                $"Raw AR pos: ({p.x:F3}, {p.y:F3}, {p.z:F3})\n" +
                $"Raw AR rot: ({e.x:F1}, {e.y:F1}, {e.z:F1})\n" +
                $"Blender rot -- Tilt-X:{blenderRot.x:F1}  Roll-Y:{blenderRot.y:F1}  Pan-Z:{blenderRot.z:F1}\n" +
                $"Last seq: {controller.LastSequenceId}   LastSendOk: {controller.LastSendOk}";
        }

        // -- Landing panel ----------------------------------------------------------

        void BuildLandingPanel()
        {
            landingPanel = CreatePanel(transform, "LandingPanel", stretch: true);

            // Phase 1 of the UI Toolkit migration (LandingScreenUITK,
            // Assets/CamLinkPro/UIToolkit/Landing.uxml) rendered beautifully
            // on-device -- a real visual improvement over this uGUI screen,
            // matching the reviewed mockup much more closely -- but every
            // tap on it is silently swallowed: no PointerDownEvent, no
            // click, nothing, even on a full-screen catch-all handler.
            // EnhancedTouchSupport.Enable() and the documented (if obsolete)
            // EventSystem.SetUITookitEventSystemOverride() override both
            // failed to fix it; the actual root cause needs Unity-support-
            // level investigation, not more guessing. Until it's solved,
            // UiToolkitMigrationEnabled stays false so this uGUI screen --
            // which works -- keeps running the real Landing UI. The new
            // GameObject (LandingUITK) is left in the scene, inactive, so
            // the next session can pick this up without rebuilding it.
            const bool UiToolkitMigrationEnabled = false;
            if (UiToolkitMigrationEnabled)
            {
                var invisibility = landingPanel.AddComponent<CanvasGroup>();
                invisibility.alpha = 0f;
                invisibility.interactable = false;
                invisibility.blocksRaycasts = false;
            }

            var layout = landingPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 20;
            layout.padding = new RectOffset(40, 40, 40, 40);
            var fitter = landingPanel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            CreateLabel(landingPanel.transform, "Cam Link Pro", 40);
            var banner = CreateLabel(landingPanel.transform, "Made with Love - Teja", 16);
            banner.fontStyle = FontStyle.Italic;
            banner.color = new Color(1f, 1f, 1f, 0.7f);

            // -- connected state: status + re-pair --
            landingConnectedGroup = new GameObject("ConnectedGroup", typeof(RectTransform));
            landingConnectedGroup.transform.SetParent(landingPanel.transform, false);
            var connectedLayout = landingConnectedGroup.AddComponent<VerticalLayoutGroup>();
            connectedLayout.childAlignment = TextAnchor.MiddleCenter;
            connectedLayout.spacing = 12;
            var statusRow = CreateRow(landingConnectedGroup.transform, "StatusRow");
            landingConnectionChip = CreateChip(statusRow.transform);
            landingStatusText = CreateLabel(statusRow.transform, "Connected", 20);
            CreateButton(landingConnectedGroup.transform, "Re-pair", () => controller?.Unpair());

            // -- not-connected state: recent connections / scan / manual pairing --
            landingPairingGroup = new GameObject("PairingGroup", typeof(RectTransform));
            landingPairingGroup.transform.SetParent(landingPanel.transform, false);
            var pairingLayout = landingPairingGroup.AddComponent<VerticalLayoutGroup>();
            pairingLayout.childAlignment = TextAnchor.MiddleCenter;
            pairingLayout.spacing = 16;

            CreateLabel(landingPairingGroup.transform, "Pair with the Blender add-on", 22);

            // Proactive, not reactive -- catches the exact "why won't it
            // pair" confusion a dead Wi-Fi radio causes, instead of only
            // surfacing as a generic failure after the user already tried.
            wifiWarningText = CreateLabel(landingPairingGroup.transform, "Wi-Fi is off -- turn it on to pair", 16);
            wifiWarningText.color = ChipRed;
            wifiWarningText.gameObject.SetActive(false);

            recentConnectionsRow = CreateRow(landingPairingGroup.transform, "RecentConnectionsRow");

            var modeRow = CreateRow(landingPairingGroup.transform, "ModeRow");
            CreateButton(modeRow.transform, "Scan QR", OpenQrScan);
            CreateButton(modeRow.transform, "Enter Manually", () =>
            {
                bool opening = !manualPanel.activeSelf;
                manualPanel.SetActive(opening);
                // Opened by hand, not via a QR hand-off -- the "QR scanned"
                // confirmation from a previous scan shouldn't linger here.
                if (opening && manualConfirmText != null) manualConfirmText.gameObject.SetActive(false);
            });

            BuildManualPanel();
            manualEntry?.Prefill(PairingInfoStore.Load());
            manualPanel.SetActive(false);

            // Always visible on Landing regardless of paired state.
            var bottomRow = CreateRow(landingPanel.transform, "LandingBottomRow");
            CreateButton(bottomRow.transform, "Settings", () => OpenSettingsFrom(landingPanel));
            letsRecordButton = CreateButton(bottomRow.transform, "Let's Record", OpenRecordingHud);
        }

        void RefreshLandingConnectionUi(bool paired)
        {
            if (landingConnectedGroup == null) return; // not built yet (early callback ordering)
            landingConnectedGroup.SetActive(paired);
            landingPairingGroup.SetActive(!paired);
            if (letsRecordButton != null) letsRecordButton.interactable = paired;

            if (paired && controller != null && landingStatusText != null)
            {
                var p = controller.CurrentPairing;
                // IsPaired only means "valid pairing info is configured" --
                // it flips true the instant Pair() is called, before the
                // sockets have proven anything reaches the other end. The
                // actual live state is ChannelState (TCP handshake) plus,
                // once that's Connected, BlenderLiveState (learned only from
                // the additive STATE line -- Unknown if the add-on hasn't
                // sent one yet, e.g. an older version that predates it, in
                // which case we say nothing more than "Connected").
                string headline;
                Color chipColor;
                if (controller.ChannelState == ChannelState.Connected)
                {
                    headline = controller.BlenderLiveState switch
                    {
                        Networking.BlenderLiveState.Live => $"Live -- driving camera ({p.Ip})",
                        Networking.BlenderLiveState.LiveNoData => $"Live, no pose data -- {p.Ip}",
                        Networking.BlenderLiveState.Connected => $"Connected to {p.Ip} -- Blender not live",
                        _ => $"Connected to {p.Ip}",
                    };
                    chipColor = controller.BlenderLiveState == Networking.BlenderLiveState.Live ? ChipGreen : ChipYellow;
                }
                else if (controller.ChannelState == ChannelState.Connecting)
                {
                    headline = $"Paired to {p.Ip} -- connecting...";
                    chipColor = ChipYellow;
                }
                else
                {
                    headline = $"Paired to {p.Ip} -- not reachable";
                    chipColor = ChipRed;
                }
                landingStatusText.text = $"{headline}\npose:{p.PosePort}  video/cmd:{p.VideoPort}";
                if (landingConnectionChip != null) landingConnectionChip.color = chipColor;
            }

            if (!paired) RebuildRecentConnectionsRow();
        }

        void RebuildRecentConnectionsRow()
        {
            if (recentConnectionsRow == null) return;
            for (int i = recentConnectionsRow.transform.childCount - 1; i >= 0; i--)
                Destroy(recentConnectionsRow.transform.GetChild(i).gameObject);

            List<PairingInfo> history = PairingHistoryStore.Load();
            if (history.Count == 0)
            {
                recentConnectionsRow.SetActive(false);
                return;
            }
            recentConnectionsRow.SetActive(true);

            foreach (var entry in history)
            {
                var info = entry; // capture
                CreateButton(recentConnectionsRow.transform, $"{info.Ip}:{info.VideoPort}", () => controller?.Pair(info));
            }
        }

        void BuildManualPanel()
        {
            manualPanel = CreatePanel(landingPairingGroup.transform, "ManualPanel", stretch: false);
            var rt = manualPanel.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(520, 400);
            var layout = manualPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 10;
            layout.padding = new RectOffset(20, 20, 20, 20);

            CreateLabel(manualPanel.transform, "Review and connect", 20);

            manualConfirmText = CreateLabel(manualPanel.transform, "", 16);
            manualConfirmText.color = ChipGreen;
            manualConfirmText.gameObject.SetActive(false);

            ipField = CreateInputField(manualPanel.transform, "IP address (e.g. 192.168.1.42)");
            posePortField = CreateInputField(manualPanel.transform, "Pose UDP port");
            videoPortField = CreateInputField(manualPanel.transform, "Video/command TCP port");
            tokenField = CreateInputField(manualPanel.transform, "Token (from QR / add-on panel)");
            manualErrorText = CreateLabel(manualPanel.transform, "", 16);
            manualErrorText.color = new Color(1f, 0.4f, 0.4f, 1f);
            manualErrorText.gameObject.SetActive(false);

            if (manualEntry != null)
            {
                SetPrivateField(manualEntry, "ipField", ipField);
                SetPrivateField(manualEntry, "posePortField", posePortField);
                SetPrivateField(manualEntry, "videoPortField", videoPortField);
                SetPrivateField(manualEntry, "tokenField", tokenField);
                SetPrivateField(manualEntry, "errorText", manualErrorText);
                manualEntry.OnPaired += info => controller?.Pair(info);
            }

            CreateButton(manualPanel.transform, "Connect", () =>
            {
                if (manualConfirmText != null) manualConfirmText.gameObject.SetActive(false);
                manualEntry?.SubmitFromFields();
            });
        }

        // -- Scan QR screen -----------------------------------------------------
        // Its own screen, deliberately: the device camera should only ever be
        // live while this screen is open, never sitting on in the background on
        // Landing.

        void BuildScanScreen()
        {
            scanScreenPanel = CreatePanel(transform, "ScanScreenPanel", stretch: true);
            var layout = scanScreenPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 16;
            layout.padding = new RectOffset(30, 30, 30, 30);

            CreateLabel(scanScreenPanel.transform, "Scan QR", 28);

            var previewBox = CreatePanel(scanScreenPanel.transform, "PreviewBox", stretch: false);
            var boxRt = previewBox.GetComponent<RectTransform>();
            boxRt.sizeDelta = new Vector2(800, 800);
            var boxLe = previewBox.AddComponent<LayoutElement>();
            boxLe.preferredWidth = 800;
            boxLe.preferredHeight = 800;

            var previewGO = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            previewGO.transform.SetParent(previewBox.transform, false);
            var previewRt = previewGO.GetComponent<RectTransform>();
            previewRt.anchorMin = Vector2.zero;
            previewRt.anchorMax = Vector2.one;
            previewRt.offsetMin = Vector2.zero;
            previewRt.offsetMax = Vector2.zero;
            var previewImage = previewGO.GetComponent<RawImage>();
            previewImage.color = Color.white;

            if (qrScanner != null)
            {
                var field = typeof(QRPairingScanner).GetField("previewImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(qrScanner, previewImage);

                // A scanned QR doesn't connect immediately -- it hands off to the
                // manual-entry screen on Landing, prefilled, so a field (most
                // often the video port, if it doesn't match what's actually
                // running in Blender) can be corrected before actually opening
                // the connections.
                qrScanner.OnPaired += info =>
                {
                    if (qrTimeoutOverlay != null) qrTimeoutOverlay.SetActive(false);
                    // Back to Landing, which -- under lazy AR start -- stays
                    // camera-off, same as Cancel below. QRPairingScanner
                    // releasing its own WebCamTexture on Close() is what
                    // actually frees the physical camera; nothing here needs
                    // to hand it back to the AR session anymore.
                    SetArSessionEnabled(false);
                    manualEntry?.Prefill(info);
                    ShowScreen(landingPanel);
                    manualPanel.SetActive(true);
                    if (manualConfirmText != null)
                    {
                        manualConfirmText.text = "QR scanned successfully -- review and connect below.";
                        manualConfirmText.gameObject.SetActive(true);
                    }
                    uiToolkitLandingScreen?.PrefillFromScan(info);
                };
            }

            CreateLabel(scanScreenPanel.transform, "Point the camera at the pairing QR shown by Blender.", 18);
            CreateButton(scanScreenPanel.transform, "Cancel", CancelQrScan);

            BuildQrTimeoutOverlay();
        }

        /// <summary>Opens Scan QR -- shared by Landing's "Scan QR" button and
        /// the timeout overlay's "Try Again", so both start the same clean
        /// state (timer reset, overlay hidden).</summary>
        void OpenQrScan()
        {
            ShowScreen(scanScreenPanel);
            if (qrTimeoutOverlay != null) qrTimeoutOverlay.SetActive(false);
            qrScanStartedAtUnscaled = Time.unscaledTime;
            // Already off by default (lazy AR start -- see
            // SetArSessionEnabled) whenever Landing is showing, but explicit
            // here too in case a future entry point into Scan QR ever isn't
            // from Landing.
            SetArSessionEnabled(false);
            qrScanner?.Open();
        }

        void CancelQrScan()
        {
            qrScanner?.Close();
            SetArSessionEnabled(false);
            if (qrTimeoutOverlay != null) qrTimeoutOverlay.SetActive(false);
            ShowScreen(landingPanel);
        }

        /// <summary>A QR scan that never finds a valid code (Blender's panel
        /// isn't open, wrong Wi-Fi network, bad lighting) previously just left
        /// the camera preview running forever with only a manual Cancel as a
        /// way out. Now it times out into an explicit, friendly dead end with
        /// the same three ways forward the mockup review called for: retry,
        /// fall back to manual entry, or give up.</summary>
        void BuildQrTimeoutOverlay()
        {
            // A child of the Canvas root, NOT of scanScreenPanel -- that panel
            // has its own VerticalLayoutGroup for its title/preview/subtext/
            // Cancel stack, which would otherwise sweep a "stretch" overlay
            // into that stacking flow (positioned/sized as just another list
            // row) instead of actually covering the screen.
            qrTimeoutOverlay = CreatePanel(transform, "QrTimeoutOverlay", stretch: true);
            qrTimeoutOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);
            var layout = qrTimeoutOverlay.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 14;

            CreateLabel(qrTimeoutOverlay.transform, "Couldn't find a pairing QR code", 24);
            var sub = CreateLabel(qrTimeoutOverlay.transform, "Make sure Blender's Cam Link Pro panel is showing its QR and this phone is on the same Wi-Fi network.", 16);
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            var subRt = sub.GetComponent<RectTransform>();
            var subLe = sub.gameObject.AddComponent<LayoutElement>();
            subLe.preferredWidth = 700;

            var row = CreateRow(qrTimeoutOverlay.transform, "QrTimeoutRow");
            CreateButton(row.transform, "Try Again", OpenQrScan);
            CreateButton(row.transform, "Enter Manually", () =>
            {
                qrScanner?.Close();
                SetArSessionEnabled(false);
                qrTimeoutOverlay.SetActive(false);
                ShowScreen(landingPanel);
                manualPanel.SetActive(true);
            });
            CreateButton(row.transform, "Cancel", CancelQrScan);

            qrTimeoutOverlay.SetActive(false);
        }

        /// <summary>Central on/off switch for the AR camera session. Used for
        /// two different reasons: (1) releasing its hold on the physical
        /// camera while QRPairingScanner's own separate WebCamTexture is open
        /// -- two simultaneous camera clients on Android is exactly the setup
        /// that made the QR preview freeze/go black after a few seconds with
        /// no way to recover short of restarting the app, since disabling the
        /// RawImage alone doesn't resolve contention over the camera hardware
        /// itself; and (2) the lazy-start policy below -- the AR camera (and
        /// the battery drain/heat that comes with it) now only runs while the
        /// Recording HUD is actually open, not from app launch.</summary>
        void SetArSessionEnabled(bool enabled)
        {
            var session = controller != null ? controller.PoseSource?.Session : null;
            if (session != null) session.enabled = enabled;
        }

        /// <summary>Entry point for "Let's Record" -- starts the AR session
        /// (previously off; see <see cref="SetArSessionEnabled"/>) and shows a
        /// brief loading screen while it warms up, instead of cutting straight
        /// to a HUD whose pose feed isn't live yet.</summary>
        void OpenRecordingHud()
        {
            long freeBytes = DeviceStorage.GetFreeBytes();
            if (freeBytes >= 0 && freeBytes < DeviceStorage.LowStorageThresholdBytes)
            {
                if (storageFullText != null)
                    storageFullText.text = $"Free at least 500 MB on this device and try again.\n\nFree space: {DeviceStorage.FormatMegabytes(freeBytes)}";
                ShowScreen(storageFullPanel);
                return;
            }

            SetArSessionEnabled(true);
            arWarmupStartedAt = Time.unscaledTime;
            if (startingCameraChecklistText != null) startingCameraChecklistText.text = "";
            ShowScreen(startingCameraPanel);
        }

        /// <summary>Home button out of the Recording HUD -- stops the AR
        /// session again so it's never left running in the background while
        /// the app just sits on Landing or Settings.</summary>
        void CloseRecordingHud()
        {
            SetArSessionEnabled(false);
            ShowScreen(landingPanel);
        }

        /// <summary>Polled while <see cref="startingCameraPanel"/> is showing --
        /// advances to the HUD once AR tracking is actually up, or after
        /// <see cref="ArWarmupMaxSeconds"/> regardless, so a phone that's slow
        /// (or never) reaching SessionTracking can't strand the user on a
        /// loading screen forever.</summary>
        void RefreshArWarmup()
        {
            bool tracking = controller != null && controller.PoseSource != null && controller.PoseSource.TrackingReliable;
            float elapsed = Time.unscaledTime - arWarmupStartedAt;

            if (startingCameraChecklistText != null)
            {
                string batteryLine = SystemInfo.batteryLevel >= 0f
                    ? $"Battery {SystemInfo.batteryLevel * 100f:0}%"
                    : null;
                string trackingLine = tracking ? "AR tracking ready" : "Waiting for AR tracking...";
                startingCameraChecklistText.text = batteryLine != null ? $"{batteryLine}\n{trackingLine}" : trackingLine;
            }

            if (tracking || elapsed >= ArWarmupMaxSeconds) ShowScreen(hudPanel);
        }

        // -- Recording (HUD) panel --------------------------------------------------

        void BuildHudPanel()
        {
            hudPanel = CreatePanel(transform, "HudPanel", stretch: true);
            var hudImage = hudPanel.GetComponent<Image>();
            if (hudImage != null) hudImage.enabled = false; // transparent container, video shows through

            BuildTopBar();
            BuildBottomBar();
            BuildRecordControls();
            BuildBigZoomSlider();
            BuildCountdownOverlay();
            BuildSavedToast();
            BuildDisconnectBanner();
        }

        /// <summary>Real, signal-driven banner (off <see cref="ChannelState"/>,
        /// not a mock) -- the TCP command channel can drop mid-take without
        /// the local RecordUiState machine ever hearing REC_OFF, which would
        /// otherwise leave the HUD quietly claiming "Recording" while nothing
        /// is actually reaching Blender any more. Positioned just below
        /// TopBar's fixed 200px band, not overlapping its nav/status rows.</summary>
        void BuildDisconnectBanner()
        {
            disconnectBannerPanel = CreatePanel(hudPanel.transform, "DisconnectBanner", stretch: false);
            var rt = disconnectBannerPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -200);
            rt.sizeDelta = new Vector2(0, 48);
            var img = disconnectBannerPanel.GetComponent<Image>();
            img.color = new Color(0.35f, 0.08f, 0.08f, 0.92f);
            img.raycastTarget = false;

            disconnectBannerText = CreateLabel(disconnectBannerPanel.transform, "Blender disconnected -- recording may not be saving", 18);
            disconnectBannerText.color = Color.white;
            disconnectBannerText.raycastTarget = false;

            disconnectBannerPanel.SetActive(false);
        }

        void RefreshDisconnectBanner(ChannelState state)
        {
            if (disconnectBannerPanel == null || controller == null) return;
            disconnectBannerPanel.SetActive(state != ChannelState.Connected && controller.RecordState == RecordUiState.Recording);
        }

        /// <summary>Small top-center confirmation shown right after a
        /// Recording -> Live transition (see <see cref="ShowSavedToast"/>) --
        /// closes the "did that actually save?" gap a silent return to Idle
        /// left open.</summary>
        void BuildSavedToast()
        {
            savedToastPanel = CreatePanel(hudPanel.transform, "SavedToast", stretch: false);
            var rt = savedToastPanel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -80);
            rt.sizeDelta = new Vector2(440, 64);
            var img = savedToastPanel.GetComponent<Image>();
            img.color = new Color(0.04f, 0.09f, 0.06f, 0.92f);
            img.raycastTarget = false;

            savedToastText = CreateLabel(savedToastPanel.transform, "", 18);
            savedToastText.color = ChipGreen;
            savedToastText.raycastTarget = false;

            savedToastPanel.SetActive(false);
        }

        /// <summary>Big center-screen flash during the pre-roll countdown,
        /// replacing the old small corner "Starting in Xs..." text (still
        /// updated too, for the debug-overlay-adjacent record status label) --
        /// built last so it's the topmost sibling and draws over everything
        /// else in the HUD. Doesn't intercept touches (raycastTarget off on
        /// both pieces): Cancel, on the record panel underneath, must stay
        /// reachable during the countdown it's meant to interrupt.</summary>
        void BuildCountdownOverlay()
        {
            countdownOverlayPanel = CreatePanel(hudPanel.transform, "CountdownOverlay", stretch: true);
            var overlayImage = countdownOverlayPanel.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.45f);
            overlayImage.raycastTarget = false;

            countdownBigText = CreateLabel(countdownOverlayPanel.transform, "", 140);
            countdownBigText.raycastTarget = false;
            var bigRt = countdownBigText.GetComponent<RectTransform>();
            bigRt.anchorMin = bigRt.anchorMax = new Vector2(0.5f, 0.5f);
            bigRt.sizeDelta = new Vector2(420, 220);

            countdownOverlayPanel.SetActive(false);
        }

        /// <summary>Shown between "Let's Record" and the HUD actually
        /// appearing, while the just-started AR session warms up -- see
        /// <see cref="OpenRecordingHud"/>/<see cref="RefreshArWarmup"/>. A
        /// separate top-level screen (like landingPanel/hudPanel), not a HUD
        /// overlay, since the HUD's own pose-driven content has nothing
        /// valid to show yet at this point.</summary>
        void BuildStartingCameraPanel()
        {
            startingCameraPanel = CreatePanel(transform, "StartingCameraPanel", stretch: true);
            var layout = startingCameraPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 14;

            CreateLabel(startingCameraPanel.transform, "Starting camera...", 26);
            CreateLabel(startingCameraPanel.transform, "Initializing AR tracking", 16);
            startingCameraChecklistText = CreateLabel(startingCameraPanel.transform, "", 16);
            startingCameraChecklistText.color = ChipGreen;

            BuildStorageFullPanel();
        }

        /// <summary>Blocks "Let's Record" outright (real check -- see
        /// <see cref="DeviceStorage"/> -- not a placeholder) when the device
        /// is critically low on space, instead of letting a take start that's
        /// likely to fail partway through.</summary>
        void BuildStorageFullPanel()
        {
            storageFullPanel = CreatePanel(transform, "StorageFullPanel", stretch: true);
            var layout = storageFullPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 16;

            CreateLabel(storageFullPanel.transform, "Not enough storage to record", 26);
            storageFullText = CreateLabel(storageFullPanel.transform, "", 16);
            storageFullText.horizontalOverflow = HorizontalWrapMode.Wrap;
            storageFullText.alignment = TextAnchor.MiddleCenter;
            var textLe = storageFullText.gameObject.AddComponent<LayoutElement>();
            textLe.preferredWidth = 700;
            CreateButton(storageFullPanel.transform, "Dismiss", () => ShowScreen(landingPanel));
        }

        void BuildTopBar()
        {
            var topBar = CreatePanel(hudPanel.transform, "TopBar", stretch: false);
            var rt = topBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(0, 200);
            var layout = topBar.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 10, 10);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperCenter;

            var navRow = CreateRow(topBar.transform, "NavRow");
            CreateButton(navRow.transform, "< Home", CloseRecordingHud);
            CreateButton(navRow.transform, "Settings", () => OpenSettingsFrom(hudPanel));

            // Split out from the nav buttons above (which must always stay
            // visible for navigation) so "Status Strip" visibility in
            // Settings -> App only hides the status indicators, never Home/
            // Settings themselves.
            statusIndicatorsGroup = CreateRow(navRow.transform, "StatusIndicatorsGroup");
            connectionChip = CreateChip(statusIndicatorsGroup.transform);
            // Text alongside the chip, not just its color -- a color-only
            // signal reads fine indoors but washes out in bright sunlight,
            // and doesn't distinguish anything for color-vision deficiency.
            connectionStateText = CreateLabel(statusIndicatorsGroup.transform, "Link: --", 16);
            connectionLatencyText = CreateLabel(statusIndicatorsGroup.transform, "--", 16);
            calibrationBadge = CreateLabel(statusIndicatorsGroup.transform, "CAL", 14);
            calibrationBadge.color = ButtonActiveBg;
            calibrationBadge.gameObject.SetActive(false);

            // Blender's own state, learned only from the additive STATE
            // line -- separate row from NavRow since it's informational,
            // not navigation. Unknown (dim chip, "--") covers both "just
            // connected, first STATE line hasn't arrived yet" and "an older
            // add-on that predates this line" -- deliberately not red,
            // since neither of those is actually an error.
            blenderStatusRow = CreateRow(topBar.transform, "BlenderStatusRow");
            blenderLiveChip = CreateChip(blenderStatusRow.transform);
            blenderLiveText = CreateLabel(blenderStatusRow.transform, "Blender: --", 16);
            poseSendingText = CreateLabel(blenderStatusRow.transform, "Pose: --", 16);

            rigRow = CreateRow(topBar.transform, "RigRow");
            var presets = new[] { RigPreset.Handheld, RigPreset.Tripod, RigPreset.Dolly, RigPreset.Crane };
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                rigButtons[i] = CreateButton(rigRow.transform, preset.ToString(), () => ApplyPreset(preset));
            }

            freezeRow = CreateRow(topBar.transform, "FreezeRow");
            // Labelled with the underlying wire rotation channel too (which of
            // rot_x/y/z carries Tilt/Roll/Pan) -- placeholder text here, kept
            // truthful afterwards by RefreshRotationAxisLabels() since which
            // channel is which depends on the Rotation Axis Remap setting.
            string[] freezeLabels = { "X", "Y", "Z", "Pan-Z", "Tilt-X", "Roll-Y" };
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                freezeToggles[i] = CreateToggle(freezeRow.transform, freezeLabels[i], v => OnFreezeToggle(idx, v));
            }
            // Order matches freezeToggles[3,4,5]: Pan, Tilt, Roll.
            freezeRotationLabels[0] = freezeToggles[3].GetComponentInChildren<Text>();
            freezeRotationLabels[1] = freezeToggles[4].GetComponentInChildren<Text>();
            freezeRotationLabels[2] = freezeToggles[5].GetComponentInChildren<Text>();
        }

        void RefreshConnectionChip(ChannelState state)
        {
            if (connectionChip == null) return;
            connectionChip.color = state switch
            {
                ChannelState.Connected => ChipGreen,
                ChannelState.Connecting => ChipYellow,
                _ => ChipRed,
            };
            if (connectionStateText != null) connectionStateText.text = state switch
            {
                ChannelState.Connected => "Link: Connected",
                ChannelState.Connecting => "Link: Connecting",
                _ => "Link: Disconnected",
            };
            if (state != ChannelState.Connected && connectionLatencyText != null) connectionLatencyText.text = "--";
        }

        void RefreshBlenderLiveChip(BlenderLiveState state)
        {
            if (blenderLiveChip != null) blenderLiveChip.color = state switch
            {
                BlenderLiveState.Live => ChipGreen,
                BlenderLiveState.LiveNoData => ChipYellow,
                BlenderLiveState.Connected => ChipYellow,
                _ => ButtonBg, // Unknown: no info yet, not an error -- avoid a false-alarm red
            };
            if (blenderLiveText != null) blenderLiveText.text = state switch
            {
                BlenderLiveState.Live => "Blender: Live",
                BlenderLiveState.LiveNoData => "Blender: Live (no pose data)",
                BlenderLiveState.Connected => "Blender: Not live",
                _ => "Blender: --",
            };
        }

        /// <summary>Polled every frame (LastSendOk isn't event-driven) --
        /// the phone's own honest half of Blender's "Pose: Receiving/No
        /// data" readout: whether packets are actually leaving this device,
        /// independent of whether Blender is parsing them.</summary>
        void RefreshPoseSendingLabel()
        {
            if (poseSendingText == null || controller == null || !hudPanel.activeSelf) return;
            poseSendingText.text = controller.LastSendOk ? "Pose: Sending" : "Pose: Not sending";
            poseSendingText.color = controller.LastSendOk ? Color.white : new Color(1f, 0.5f, 0.5f, 1f);
        }

        /// <summary>Shown only when we positively know Blender is Connected
        /// but not Live -- Unknown (older add-on, or not connected at all)
        /// deliberately shows nothing, since Record already degrades
        /// gracefully via the Starting -> Cancel escape hatch either way;
        /// this is purely an early, honest heads-up when we actually know.</summary>
        void RefreshRecordReadinessWarning(BlenderLiveState state)
        {
            if (recordReadinessWarningText != null)
                recordReadinessWarningText.gameObject.SetActive(state == BlenderLiveState.Connected && AppPreferences.RecordReadinessWarningVisible);
            RefreshRecordButtonInteractable();
        }

        /// <summary>Gates Record on both the phone's own arm state and
        /// Blender's actual live status. Without the second half, "Lock Start"
        /// arming (which Blender can send unconditionally) let a user reach an
        /// interactable Record button while Blender's add-on wasn't live yet --
        /// pressing it sent START, which the add-on silently drops when
        /// `is_live` is false (no ARMED/REC_ON-style error comes back), so the
        /// button just looked broken: countdown, "Starting...", and nothing
        /// ever recorded. Only the *known* not-live case (BlenderLiveState ==
        /// Connected) blocks; Unknown -- an older add-on that never sends the
        /// additive STATE line, or no info yet -- stays permissive so this
        /// can't regress the original fixed-contract behaviour.</summary>
        void RefreshRecordButtonInteractable()
        {
            if (recordButton == null || controller == null) return;
            bool knownNotLive = controller.BlenderLiveState == BlenderLiveState.Connected;
            recordButton.interactable = controller.RecordState == RecordUiState.Armed && !knownNotLive;
        }

        void BuildBottomBar()
        {
            // Four compact columns side by side, using a landscape phone's
            // abundant width instead of stacking every control into one tall
            // column that runs off the bottom of a short screen -- that's
            // exactly what was happening before (ROP/ROR and part of Dolly were
            // rendering below the visible screen entirely).
            bottomBar = CreatePanel(hudPanel.transform, "BottomBar", stretch: false);
            var rt = bottomBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0.82f, 0); // leaves the right ~18% clear of RecordPanel's column
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 10);
            rt.sizeDelta = new Vector2(0, 230);
            var rowLayout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(20, 20, 12, 12);
            rowLayout.spacing = 20;
            rowLayout.childAlignment = TextAnchor.UpperCenter;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            Transform MakeColumn(string name)
            {
                var col = new GameObject(name, typeof(RectTransform));
                col.transform.SetParent(bottomBar.transform, false);
                var colLayout = col.AddComponent<VerticalLayoutGroup>();
                colLayout.spacing = 6;
                colLayout.childAlignment = TextAnchor.UpperCenter;
                return col.transform;
            }

            var monitorCol = MakeColumn("MonitorColumn");
            CreateSlider(monitorCol, "Steady", 0f, 1f, 0.4f, v => controller?.SetSteadiness(v));
            CreateSlider(monitorCol, "Opacity", 0f, 1f, 0.6f, v => controller?.SetOpacity(v));

            var zoomCol = MakeColumn("ZoomColumn");
            var zoomHeaderRow = CreateRow(zoomCol, "ZoomHeaderRow");
            CreateLabel(zoomHeaderRow.transform, "Zoom", 18);
            zoomValueText = CreateLabel(zoomHeaderRow.transform, "auto", 18);
            CreateButton(zoomHeaderRow.transform, "Live", () => { controller?.SetZoomAuto(); RefreshZoomLabel(); });

            // Two interchangeable control styles for the same thing -- which one
            // is visible is an opt-in Settings toggle (ZoomSliderEnabled); both
            // are built up front and just swapped by RefreshZoomControlMode().
            zoomButtonsRow = CreateRow(zoomCol, "ZoomButtonsRow");
            CreateRepeatButton(zoomButtonsRow.transform, "-", () => { controller?.NudgeZoom(-2f); RefreshZoomLabel(); });
            CreateRepeatButton(zoomButtonsRow.transform, "+", () => { controller?.NudgeZoom(2f); RefreshZoomLabel(); });

            zoomSliderRow = CreateRow(zoomCol, "ZoomSliderRow");
            zoomSlider = CreateSlider(zoomSliderRow.transform, "", AR.ZoomState.MinFocalLengthMm, AR.ZoomState.MaxFocalLengthMm, 35f,
                v => { controller?.SetZoomFocalLength(v); RefreshZoomLabel(); });

            var dollyCol = MakeColumn("DollyColumn");
            var dollyHeaderRow = CreateRow(dollyCol, "DollyHeaderRow");
            CreateLabel(dollyHeaderRow.transform, "Dolly", 18);
            dollyValueText = CreateLabel(dollyHeaderRow.transform, "0.0m", 18);
            var dollyButtonsRow = CreateRow(dollyCol, "DollyButtonsRow");
            CreateRepeatButton(dollyButtonsRow.transform, "-", () => { controller?.NudgeDolly(-1f); RefreshDollyLabel(); });
            CreateRepeatButton(dollyButtonsRow.transform, "+", () => { controller?.NudgeDolly(1f); RefreshDollyLabel(); });
            CreateLongPressButton(dollyCol, "Reset Origin",
                onTap: () => controller?.ResetOrigin(),
                onLongPress: () => { controller?.ClearAllZeroOffsets(); Handheld.Vibrate(); });

            var zeroCol = MakeColumn("ZeroColumn");
            CreateLabel(zeroCol, "Zero Start", 18);
            var zeroButtonsRow = CreateRow(zeroCol, "ZeroButtonsRow");
            CreateButton(zeroButtonsRow.transform, "ROP", () => controller?.CapturePositionZero());
            CreateButton(zeroButtonsRow.transform, "ROR", () => controller?.CaptureRotationZero());
        }

        /// <summary>Swaps between the +/- buttons and the slider for optical
        /// zoom based on the Settings toggle, and refreshes the slider's
        /// position to match the current state without re-triggering a manual
        /// zoom switch just from becoming visible.</summary>
        void RefreshZoomControlMode()
        {
            if (zoomButtonsRow == null) return;
            bool useSlider = AppPreferences.ZoomSliderEnabled;
            zoomButtonsRow.SetActive(!useSlider);
            zoomSliderRow.SetActive(useSlider);
            if (useSlider && controller != null) zoomSlider.SetValueWithoutNotify(controller.Zoom.ManualFocalLengthMm);
        }

        void BuildRecordControls()
        {
            var panel = CreatePanel(hudPanel.transform, "RecordPanel", stretch: false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-30, 0);
            rt.sizeDelta = new Vector2(260, 350);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(15, 15, 15, 15);
            layout.childAlignment = TextAnchor.MiddleCenter;

            recordStatusText = CreateLabel(panel.transform, "Idle", 24);
            lockStartButton = CreateButton(panel.transform, "Lock Start", () => controller?.LockStart());
            cancelButton = CreateButton(panel.transform, "Cancel", () => controller?.CancelCurrent());
            recordButton = CreateButton(panel.transform, "Record", () => controller?.BeginRecordCountdown());
            stopButton = CreateButton(panel.transform, "Stop", () => controller?.Stop());

            recordReadinessWarningText = CreateLabel(panel.transform, "Blender not live yet", 14);
            recordReadinessWarningText.color = ChipYellow;
            recordReadinessWarningText.gameObject.SetActive(false);

            UpdateRecordUi(RecordUiState.Live);
        }

        /// <summary>A big camcorder-style zoom rocker, just left of the
        /// Live/Record panel -- push up/down and hold to zoom continuously at
        /// a rate proportional to how far it's pushed (scaled by the Settings
        /// sensitivity), release and it snaps back to center. Opt-in via
        /// Settings -> Camera Settings -> Zoom Slider Visibility; the compact
        /// absolute-position slider in the bottom bar's Zoom column is a
        /// separate, independent control.</summary>
        void BuildBigZoomSlider()
        {
            bigZoomSliderRoot = CreatePanel(hudPanel.transform, "BigZoomSliderPanel", stretch: false);
            var rt = bigZoomSliderRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-310, 0);
            rt.sizeDelta = new Vector2(100, 480);
            var layout = bigZoomSliderRoot.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 14, 14);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandHeight = false;

            CreateLabel(bigZoomSliderRoot.transform, "Zoom", 18);
            bigZoomSlider = CreateVerticalRockerSlider(bigZoomSliderRoot.transform);
            bigZoomFocalText = CreateLabel(bigZoomSliderRoot.transform, "auto", 16);

            bigZoomSliderRoot.SetActive(false); // RefreshBigZoomSliderVisibility() sets the real state
        }

        /// <summary>Shows/hides the big zoom rocker per the Settings toggle --
        /// called whenever the HUD becomes active and whenever that toggle
        /// changes while Settings is open.</summary>
        void RefreshBigZoomSliderVisibility()
        {
            if (bigZoomSliderRoot != null) bigZoomSliderRoot.SetActive(AppPreferences.BigZoomSliderVisible);
        }

        // -- Settings panel ---------------------------------------------------------

        void RefreshSettingsFromLiveState()
        {
            if (controller == null) return;
            for (int i = 0; i < 3; i++)
            {
                posScaleFields[i].SetTextWithoutNotify(controller.Calibration.PositionScale[i].ToString("0.###"));
                rotScaleFields[i].SetTextWithoutNotify(controller.Calibration.RotationScale[i].ToString("0.###"));
                posFlipToggles[i].SetIsOnWithoutNotify(controller.Calibration.PositionFlip[i]);
                rotFlipToggles[i].SetIsOnWithoutNotify(controller.Calibration.RotationFlip[i]);
            }
            SetStateToggle(horizonLevelToggle, controller.Pipeline.LevelHorizon);
            SetStateToggle(diagnosticsToggle, AppPreferences.DiagnosticsOverlayEnabled);
            SetStateToggle(keepAwakeToggle, AppPreferences.KeepScreenAwake);
            SetStateToggle(zoomSliderToggle, AppPreferences.ZoomSliderEnabled);
            recordDelayField.SetTextWithoutNotify(AppPreferences.RecordCountdownSeconds.ToString("0.#"));
            SetStateToggle(bigZoomVisibilityToggle, AppPreferences.BigZoomSliderVisible);
            zoomSensitivityField.SetTextWithoutNotify(AppPreferences.ZoomSliderSensitivity.ToString("0.##"));
            SetStateToggle(statusStripVisibilityToggle, AppPreferences.StatusStripVisible);
            SetStateToggle(rigPresetRowVisibilityToggle, AppPreferences.RigPresetRowVisible);
            SetStateToggle(freezeAxisRowVisibilityToggle, AppPreferences.FreezeAxisRowVisible);
            SetStateToggle(bottomBarVisibilityToggle, AppPreferences.BottomControlBarVisible);
            SetStateToggle(recordReadinessWarningVisibilityToggle, AppPreferences.RecordReadinessWarningVisible);
            if (rotationRemapDropdown != null) rotationRemapDropdown.SetValueWithoutNotify((int)controller.Calibration.RotationRemap);
            RefreshRotationAxisLabels();
            RefreshCalibrationOffsetLabels();

            var p = controller.CurrentPairing;
            settingsIpField.text = p.Ip ?? "";
            settingsPoseField.text = p.PosePort > 0 ? p.PosePort.ToString() : "";
            settingsVideoField.text = p.VideoPort > 0 ? p.VideoPort.ToString() : "";
            settingsTokenField.text = p.Token ?? "";
        }

        void RefreshCalibrationOffsetLabels()
        {
            if (controller == null || positionOffsetText == null) return;
            var c = controller.Calibration;
            var (tiltLabel, rollLabel, panLabel) = ComputeRotationAxisLabels();
            positionOffsetText.text = c.HasPositionOffset
                ? $"Position zero: ({c.PositionOffset.x:F2}, {c.PositionOffset.y:F2}, {c.PositionOffset.z:F2})"
                : "Position zero: none";
            rotationOffsetText.text = c.HasRotationOffset
                ? $"Rotation zero -- {tiltLabel}:{c.RotationOffsetDeg.x:F1}  {rollLabel}:{c.RotationOffsetDeg.y:F1}  {panLabel}:{c.RotationOffsetDeg.z:F1}"
                : "Rotation zero: none";

            for (int i = 0; i < 3; i++)
            {
                posOffsetFields[i].SetTextWithoutNotify(c.PositionOffset[i].ToString("0.###"));
                rotOffsetFields[i].SetTextWithoutNotify(c.RotationOffsetDeg[i].ToString("0.###"));
            }
        }

        /// <summary>Which wire channel (X/Y/Z) each semantic rotation name
        /// (Tilt, Roll, Pan) currently lands on, per the Rotation Axis Remap
        /// setting -- so every place that names these channels stays truthful
        /// instead of assuming the old fixed Tilt-X/Roll-Y/Pan-Z mapping.</summary>
        (string tilt, string roll, string pan) ComputeRotationAxisLabels()
        {
            if (controller == null) return ("Tilt-X", "Roll-Y", "Pan-Z");
            OutputCalibration.GetOutputSlots(controller.Calibration.RotationRemap, out int tiltSlot, out int rollSlot, out int panSlot);
            string[] letters = { "X", "Y", "Z" };
            return ($"Tilt-{letters[tiltSlot]}", $"Roll-{letters[rollSlot]}", $"Pan-{letters[panSlot]}");
        }

        /// <summary>Re-labels every on-screen Tilt/Roll/Pan name (Recording
        /// HUD freeze toggles, Calibration screen ROR fields) to reflect the
        /// current Rotation Axis Remap setting.</summary>
        void RefreshRotationAxisLabels()
        {
            var (tiltLabel, rollLabel, panLabel) = ComputeRotationAxisLabels();
            if (freezeRotationLabels[0] != null) freezeRotationLabels[0].text = panLabel;
            if (freezeRotationLabels[1] != null) freezeRotationLabels[1].text = tiltLabel;
            if (freezeRotationLabels[2] != null) freezeRotationLabels[2].text = rollLabel;
            if (calibRotationLabels[0] != null) calibRotationLabels[0].text = tiltLabel;
            if (calibRotationLabels[1] != null) calibRotationLabels[1].text = rollLabel;
            if (calibRotationLabels[2] != null) calibRotationLabels[2].text = panLabel;
        }

        void BuildSettingsPanel()
        {
            settingsPanel = CreatePanel(transform, "SettingsPanel", stretch: true);
            var outerLayout = settingsPanel.AddComponent<VerticalLayoutGroup>();
            outerLayout.childAlignment = TextAnchor.UpperCenter;
            outerLayout.padding = new RectOffset(30, 30, 20, 20);
            outerLayout.spacing = 10;
            // childControlHeight must be on for the scroll area's flexibleHeight=1
            // (below) to actually claim the remaining vertical space -- Unity's
            // default (off) silently ignores flexibleHeight, which is what was
            // collapsing the whole scrollable body to zero height.
            outerLayout.childControlWidth = true;
            outerLayout.childControlHeight = true;
            outerLayout.childForceExpandWidth = true;
            outerLayout.childForceExpandHeight = false;

            var topRow = CreateRow(settingsPanel.transform, "SettingsTopRow");
            var topRowLe = topRow.AddComponent<LayoutElement>();
            topRowLe.minHeight = 60;
            CreateButton(topRow.transform, "< Back", () => ShowScreen(screenBeforeSettings != null ? screenBeforeSettings : landingPanel));
            CreateLabel(topRow.transform, "Settings", 28);

            // Everything below scrolls -- a phone screen in landscape is short,
            // and this list only grows over time, so trusting it to always fit
            // unscrolled isn't safe.
            var scrollGo = new GameObject("SettingsScroll", typeof(RectTransform));
            scrollGo.transform.SetParent(settingsPanel.transform, false);
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1;
            var content = CreateScrollView(scrollGo.transform, "Scroll");

            // Two columns side by side -- uses a landscape phone's abundant
            // width instead of stacking everything into one tall column.
            var (leftCol, rightCol) = CreateTwoColumns(content, "Columns");

            CreateLabel(leftCol, "Pose Calibration (gain)", 20);
            CreateAxisRow(leftCol, "Position Scale", posScaleFields, (axis, text) =>
            {
                if (controller != null && float.TryParse(text, out float v))
                {
                    var s = controller.Calibration.PositionScale;
                    s[axis] = v;
                    controller.Calibration.PositionScale = s;
                    controller.Calibration.Save();
                }
            });
            CreateAxisFlipRow(leftCol, "Position Flip", posFlipToggles, (axis, on) =>
            {
                if (controller == null) return;
                controller.Calibration.PositionFlip[axis] = on;
                controller.Calibration.Save();
            });
            CreateAxisRow(leftCol, "Rotation Scale", rotScaleFields, (axis, text) =>
            {
                if (controller != null && float.TryParse(text, out float v))
                {
                    var s = controller.Calibration.RotationScale;
                    s[axis] = v;
                    controller.Calibration.RotationScale = s;
                    controller.Calibration.Save();
                }
            });
            CreateAxisFlipRow(leftCol, "Rotation Flip", rotFlipToggles, (axis, on) =>
            {
                if (controller == null) return;
                controller.Calibration.RotationFlip[axis] = on;
                controller.Calibration.Save();
            });

            var remapRow = CreateRow(leftCol, "RotationRemapRow");
            CreateLabel(remapRow.transform, "Rotation Axis Remap (which channel sends where)", 16);
            rotationRemapDropdown = CreateDropdown(remapRow.transform,
                new[] { "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX" },
                (int)RotationAxisRemap.XZY,
                v =>
                {
                    if (controller == null) return;
                    controller.Calibration.RotationRemap = (RotationAxisRemap)v;
                    controller.Calibration.Save();
                    RefreshRotationAxisLabels();
                });

            var horizonRow = CreateRow(leftCol, "HorizonRow");
            CreateLabel(horizonRow.transform, "Horizon Level (fixes roll instability near 90 deg)", 16);
            horizonLevelToggle = CreateStateToggle(horizonRow.transform, v => controller?.SetLevelHorizon(v));

            CreateLabel(leftCol, "Starting-Point Calibration (offset, ROP/ROR on Recording)", 20);
            positionOffsetText = CreateLabel(leftCol, "Position zero: none", 16);
            CreateButton(leftCol, "Re-zero (ROP)", () => { controller?.CapturePositionZero(); RefreshCalibrationOffsetLabels(); });
            rotationOffsetText = CreateLabel(leftCol, "Rotation zero: none", 16);
            CreateButton(leftCol, "Re-zero (ROR)", () => { controller?.CaptureRotationZero(); RefreshCalibrationOffsetLabels(); });
            CreateButton(leftCol, "Customize ROP / ROR per axis ->", OpenCalibrationScreen);

            CreateLabel(rightCol, "App", 20);
            var diagRow = CreateRow(rightCol, "DiagnosticsRow");
            CreateLabel(diagRow.transform, "Diagnostics Overlay (verbose logcat dump)", 16);
            diagnosticsToggle = CreateStateToggle(diagRow.transform, v => AppPreferences.DiagnosticsOverlayEnabled = v);

            var awakeRow = CreateRow(rightCol, "AwakeRow");
            CreateLabel(awakeRow.transform, "Keep Screen Awake", 16);
            keepAwakeToggle = CreateStateToggle(awakeRow.transform, v => AppPreferences.KeepScreenAwake = v);

            var zoomSliderRow2 = CreateRow(rightCol, "ZoomSliderToggleRow");
            CreateLabel(zoomSliderRow2.transform, "Zoom Slider (instead of +/- buttons)", 16);
            zoomSliderToggle = CreateStateToggle(zoomSliderRow2.transform, v => AppPreferences.ZoomSliderEnabled = v);

            var recordDelayRow = CreateRow(rightCol, "RecordDelayRow");
            CreateLabel(recordDelayRow.transform, "Record Delay (seconds, after pressing Record)", 16);
            recordDelayField = CreateInputField(recordDelayRow.transform, "2", narrow: true);
            recordDelayField.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, out float v)) AppPreferences.RecordCountdownSeconds = v;
                recordDelayField.SetTextWithoutNotify(AppPreferences.RecordCountdownSeconds.ToString("0.#"));
            });

            CreateLabel(rightCol, "HUD Visibility (hides the element only -- never the function)", 20);
            var statusStripVisRow = CreateRow(rightCol, "StatusStripVisibilityRow");
            CreateLabel(statusStripVisRow.transform, "Status Strip (link / blender / pose)", 16);
            statusStripVisibilityToggle = CreateStateToggle(statusStripVisRow.transform, v =>
            {
                AppPreferences.StatusStripVisible = v;
                RefreshHudVisibility();
            });

            var rigRowVisRow = CreateRow(rightCol, "RigPresetRowVisibilityRow");
            CreateLabel(rigRowVisRow.transform, "Rig Preset Row (Handheld/Tripod/Dolly/Crane)", 16);
            rigPresetRowVisibilityToggle = CreateStateToggle(rigRowVisRow.transform, v =>
            {
                AppPreferences.RigPresetRowVisible = v;
                RefreshHudVisibility();
            });

            var freezeRowVisRow = CreateRow(rightCol, "FreezeAxisRowVisibilityRow");
            CreateLabel(freezeRowVisRow.transform, "Freeze-Axis Row (Pan/Tilt/Roll toggles)", 16);
            freezeAxisRowVisibilityToggle = CreateStateToggle(freezeRowVisRow.transform, v =>
            {
                AppPreferences.FreezeAxisRowVisible = v;
                RefreshHudVisibility();
            });

            var bottomBarVisRow = CreateRow(rightCol, "BottomBarVisibilityRow");
            CreateLabel(bottomBarVisRow.transform, "Bottom Control Bar (Steady/Opacity/Zoom/Dolly/Zero Start)", 16);
            bottomBarVisibilityToggle = CreateStateToggle(bottomBarVisRow.transform, v =>
            {
                AppPreferences.BottomControlBarVisible = v;
                RefreshHudVisibility();
            });

            var readinessWarnVisRow = CreateRow(rightCol, "ReadinessWarningVisibilityRow");
            CreateLabel(readinessWarnVisRow.transform, "Record-Readiness Warning Banner", 16);
            recordReadinessWarningVisibilityToggle = CreateStateToggle(readinessWarnVisRow.transform, v =>
            {
                AppPreferences.RecordReadinessWarningVisible = v;
                RefreshRecordReadinessWarning(controller != null ? controller.BlenderLiveState : BlenderLiveState.Unknown);
            });

            CreateLabel(rightCol, "Camera Settings", 20);
            var bigZoomVisRow = CreateRow(rightCol, "BigZoomVisibilityRow");
            CreateLabel(bigZoomVisRow.transform, "Zoom Slider Visibility (big rocker beside Record)", 16);
            bigZoomVisibilityToggle = CreateStateToggle(bigZoomVisRow.transform, v =>
            {
                AppPreferences.BigZoomSliderVisible = v;
                RefreshBigZoomSliderVisibility();
            });

            var zoomSensitivityRow = CreateRow(rightCol, "ZoomSensitivityRow");
            CreateLabel(zoomSensitivityRow.transform, "Zoom Slider Sensitivity (0.25-4x)", 16);
            zoomSensitivityField = CreateInputField(zoomSensitivityRow.transform, "1", narrow: true);
            zoomSensitivityField.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, out float v)) AppPreferences.ZoomSliderSensitivity = v;
                zoomSensitivityField.SetTextWithoutNotify(AppPreferences.ZoomSliderSensitivity.ToString("0.##"));
            });

            CreateLabel(rightCol, "Blender Camera", 20);
            var liveCamRow = CreateRow(rightCol, "LiveCameraReadoutRow");
            CreateLabel(liveCamRow.transform, "Live from Blender (focal length / sensor width)", 14);
            liveCameraReadoutText = CreateLabel(liveCamRow.transform, "--", 16);
            liveCameraReadoutText.color = ChipGreen;

            CreateLabel(rightCol, "Sensor height, lens distortion profile, stream quality and frame rate -- reserved for when the add-on exposes them. Not wired yet.", 13);

            CreateLabel(rightCol, "Connection", 20);
            settingsIpField = CreateInputField(rightCol, "IP address");
            settingsPoseField = CreateInputField(rightCol, "Pose UDP port");
            settingsVideoField = CreateInputField(rightCol, "Video/command TCP port");
            settingsTokenField = CreateInputField(rightCol, "Token");
            settingsConnectionError = CreateLabel(rightCol, "", 16);
            settingsConnectionError.color = new Color(1f, 0.4f, 0.4f, 1f);
            settingsConnectionError.gameObject.SetActive(false);

            var connectionRow = CreateRow(rightCol, "ConnectionRow");
            CreateButton(connectionRow.transform, "Reconnect", ApplyConnectionEdits);
            CreateButton(connectionRow.transform, "Unpair", () =>
            {
                ShowConfirmDialog("Unpair from Blender? This ends the current session.", () =>
                {
                    controller?.Unpair();
                    ShowScreen(landingPanel);
                });
            }, danger: true);

            // Bottom breathing room so the last row isn't flush against the
            // scroll view's edge.
            var spacerLe = new GameObject("BottomSpacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            spacerLe.transform.SetParent(rightCol, false);
            spacerLe.minHeight = 40;
        }

        /// <summary>Dedicated screen (reached via a button on Settings) for
        /// customizing the starting-point calibration (ROP/ROR) per axis, for
        /// both position and rotation -- separate from the compact Settings
        /// summary since editing six individual axis values needs real room,
        /// not an inline accordion.</summary>
        void BuildCalibrationScreen()
        {
            calibrationPanel = CreatePanel(transform, "CalibrationPanel", stretch: true);
            var outerLayout = calibrationPanel.AddComponent<VerticalLayoutGroup>();
            outerLayout.childAlignment = TextAnchor.UpperCenter;
            outerLayout.padding = new RectOffset(30, 30, 20, 20);
            outerLayout.spacing = 10;
            outerLayout.childControlWidth = true;
            outerLayout.childControlHeight = true;
            outerLayout.childForceExpandWidth = true;
            outerLayout.childForceExpandHeight = false;

            var topRow = CreateRow(calibrationPanel.transform, "CalibrationTopRow");
            var topRowLe = topRow.AddComponent<LayoutElement>();
            topRowLe.minHeight = 60;
            CreateButton(topRow.transform, "< Back", () => ShowScreen(settingsPanel));
            CreateLabel(topRow.transform, "Starting-Point Calibration", 28);

            var scrollGo = new GameObject("CalibrationScroll", typeof(RectTransform));
            scrollGo.transform.SetParent(calibrationPanel.transform, false);
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1;
            var content = CreateScrollView(scrollGo.transform, "Scroll");

            var (leftCol, rightCol) = CreateTwoColumns(content, "Columns");

            CreateLabel(leftCol, "Position (ROP)", 22);
            CreateLabel(leftCol, "Delta subtracted from raw position before it's sent.", 14);
            string[] posAxisLabels = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                var row = CreateRow(leftCol, $"PosOffsetRow_{posAxisLabels[i]}");
                CreateLabel(row.transform, posAxisLabels[i], 18);
                var field = CreateInputField(row.transform, "0", narrow: true);
                field.onEndEdit.AddListener(text =>
                {
                    if (controller == null || !float.TryParse(text, out float v)) return;
                    var o = controller.Calibration.PositionOffset;
                    o[axis] = v;
                    controller.Calibration.PositionOffset = o;
                    controller.Calibration.Save();
                    RefreshCalibrationOffsetLabels();
                });
                posOffsetFields[i] = field;
            }
            var posButtonsRow = CreateRow(leftCol, "PosOffsetButtons");
            CreateButton(posButtonsRow.transform, "Capture Current (ROP)", () => { controller?.CapturePositionZero(); RefreshCalibrationOffsetLabels(); });
            CreateButton(posButtonsRow.transform, "Clear", () => { controller?.ClearPositionZero(); RefreshCalibrationOffsetLabels(); }, danger: true);

            CreateLabel(rightCol, "Rotation (ROR)", 22);
            CreateLabel(rightCol, "Delta subtracted from raw rotation before it's sent.", 14);
            // Placeholder text -- kept truthful by RefreshRotationAxisLabels()
            // since which wire channel each of these is depends on the
            // Rotation Axis Remap setting in Settings.
            string[] rotAxisLabels = { "Tilt-X", "Roll-Y", "Pan-Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                var row = CreateRow(rightCol, $"RotOffsetRow_{rotAxisLabels[i]}");
                calibRotationLabels[i] = CreateLabel(row.transform, rotAxisLabels[i], 18);
                var field = CreateInputField(row.transform, "0", narrow: true);
                field.onEndEdit.AddListener(text =>
                {
                    if (controller == null || !float.TryParse(text, out float v)) return;
                    var o = controller.Calibration.RotationOffsetDeg;
                    o[axis] = v;
                    controller.Calibration.RotationOffsetDeg = o;
                    controller.Calibration.Save();
                    RefreshCalibrationOffsetLabels();
                });
                rotOffsetFields[i] = field;
            }
            var rotButtonsRow = CreateRow(rightCol, "RotOffsetButtons");
            CreateButton(rotButtonsRow.transform, "Capture Current (ROR)", () => { controller?.CaptureRotationZero(); RefreshCalibrationOffsetLabels(); });
            CreateButton(rotButtonsRow.transform, "Clear", () => { controller?.ClearRotationZero(); RefreshCalibrationOffsetLabels(); }, danger: true);

            var spacerLe = new GameObject("BottomSpacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            spacerLe.transform.SetParent(rightCol, false);
            spacerLe.minHeight = 40;
        }

        void ApplyConnectionEdits()
        {
            string ip = settingsIpField.text.Trim();
            string token = settingsTokenField.text.Trim();
            bool okPose = int.TryParse(settingsPoseField.text, out int posePort);
            bool okVideo = int.TryParse(settingsVideoField.text, out int videoPort);
            var info = new PairingInfo { Ip = ip, PosePort = posePort, VideoPort = videoPort, Token = token };

            if (!info.IsValid)
            {
                settingsConnectionError.text = "Enter a valid IP, both ports (1-65535), and token.";
                settingsConnectionError.gameObject.SetActive(true);
                return;
            }
            settingsConnectionError.gameObject.SetActive(false);
            controller?.Reconnect(info);
        }

        // -- callbacks ------------------------------------------------------------

        void ApplyPreset(RigPreset preset)
        {
            controller?.ApplyRigPreset(preset);
            var f = AxisFreezeState.FromPreset(preset);
            bool[] values = { f.PositionX, f.PositionY, f.PositionZ, f.Pan, f.Tilt, f.Roll };
            for (int i = 0; i < 6; i++)
            {
                if (freezeToggles[i] != null) freezeToggles[i].SetIsOnWithoutNotify(values[i]);
            }
        }

        void OnFreezeToggle(int index, bool value)
        {
            var f = new AxisFreezeState
            {
                PositionX = freezeToggles[0].isOn,
                PositionY = freezeToggles[1].isOn,
                PositionZ = freezeToggles[2].isOn,
                Pan = freezeToggles[3].isOn,
                Tilt = freezeToggles[4].isOn,
                Roll = freezeToggles[5].isOn,
            };
            controller?.SetFreeze(f);
        }

        void RefreshZoomLabel()
        {
            if (controller == null || zoomValueText == null) return;
            zoomValueText.text = controller.Zoom.Mode == CamLinkPro.AR.ZoomMode.Auto
                ? "auto"
                : $"{controller.Zoom.ManualFocalLengthMm:0}mm";
        }

        void RefreshDollyLabel()
        {
            if (controller == null || dollyValueText == null) return;
            dollyValueText.text = $"{controller.Pipeline.DollyOffsetMetres:0.0}m";
        }

        void UpdateRecordUi(RecordUiState state)
        {
            if (recordStatusText == null) return;
            recordStatusText.text = state switch
            {
                // Displayed as "Idle" now, not "Live" -- "Live" is a
                // Blender-side concept (BlenderLiveState) shown elsewhere on
                // this same screen; using the same word for two different
                // things here would be actively misleading.
                RecordUiState.Live => "Idle",
                RecordUiState.Armed => "Armed",
                RecordUiState.CountingDown => "Starting...",
                RecordUiState.Starting => "Starting...",
                RecordUiState.Recording => "Recording",
                _ => state.ToString(),
            };

            if (lockStartButton != null) lockStartButton.interactable = state == RecordUiState.Live;
            RefreshRecordButtonInteractable();
            if (stopButton != null) stopButton.interactable = state == RecordUiState.Recording;
            if (countdownOverlayPanel != null) countdownOverlayPanel.SetActive(state == RecordUiState.CountingDown);
            if (controller != null) RefreshDisconnectBanner(controller.ChannelState);
            // Cancel works from Armed (un-arm locally), CountingDown (abort the
            // pre-roll before START is sent), and Starting (escape hatch if
            // Blender's REC_ON confirmation never arrives after START was sent
            // -- without this, a slow/unresponsive add-on left the whole record
            // flow stuck on "Starting..." with Cancel greyed out and no way
            // back short of force-quitting).
            if (cancelButton != null) cancelButton.interactable = state == RecordUiState.Armed || state == RecordUiState.CountingDown || state == RecordUiState.Starting;

            // A short haptic tick on the transitions that actually mean
            // something changed for real (armed, recording started, or
            // recording stopped) -- not on the local-only CountingDown/Starting
            // sub-states, which would just buzz twice in quick succession.
            if ((state == RecordUiState.Armed || state == RecordUiState.Recording) ||
                (state == RecordUiState.Live && previousRecordState == RecordUiState.Recording))
            {
                Handheld.Vibrate();
            }
            if (state == RecordUiState.Recording) recordingStartedAtUnscaled = Time.unscaledTime;
            if (state == RecordUiState.Live && previousRecordState == RecordUiState.Recording)
                ShowSavedToast(Time.unscaledTime - recordingStartedAtUnscaled);
            previousRecordState = state;
        }

        /// <summary>Confirms a stop actually happened, since the wire contract
        /// only ever gives a bare REC_OFF -- no filename, no duration -- and a
        /// silent return to Idle left it genuinely ambiguous whether the take
        /// saved. The duration shown is measured locally (REC_ON to REC_OFF,
        /// on this phone's clock), which is the only duration this side of the
        /// wire actually has; it's not claiming to be Blender's exact saved
        /// file length.</summary>
        void ShowSavedToast(float elapsedSeconds)
        {
            if (savedToastPanel == null) return;
            int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(elapsedSeconds));
            savedToastText.text = $"Recording stopped -- {totalSeconds / 60}:{totalSeconds % 60:00}";
            savedToastPanel.SetActive(true);
            savedToastHideAtUnscaled = Time.unscaledTime + SavedToastDurationSeconds;
        }

        // -- small uGUI builder helpers --------------------------------------------

        static GameObject CreatePanel(Transform parent, string name, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            var img = go.GetComponent<Image>();
            img.color = PanelBg;
            return go;
        }

        static GameObject CreateRow(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        static Text CreateLabel(Transform parent, string text, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 40;
            le.minHeight = fontSize + 10;
            return t;
        }

        /// <summary>A small circular status dot (connection health chip).</summary>
        static Image CreateChip(Transform parent)
        {
            var go = new GameObject("ConnectionChip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 22;
            le.minHeight = 22;
            var img = go.GetComponent<Image>();
            img.color = ChipRed;
            return img;
        }

        /// <summary>Explicit, clearly-different colours per interaction state so
        /// every button/toggle gives immediate visual feedback when pressed --
        /// relying on Unity's own default ColorBlock wasn't visible enough
        /// against this dark theme.</summary>
        static ColorBlock MakeColors(Color normal, Color pressed)
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = normal;
            c.highlightedColor = Color.Lerp(normal, pressed, 0.35f);
            c.pressedColor = pressed;
            c.selectedColor = normal;
            c.disabledColor = ButtonDisabled;
            c.fadeDuration = 0.05f;
            c.colorMultiplier = 1f;
            return c;
        }

        static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, bool danger = false)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = danger ? DangerBg : ButtonBg;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140, 60);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 140;
            le.minHeight = 60;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = MakeColors(danger ? DangerBg : ButtonBg, danger ? DangerPressed : ButtonPressed);
            btn.onClick.AddListener(onClick);

            var labelText = CreateLabel(go.transform, label, 20);
            var labelRt = labelText.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            return btn;
        }

        static RepeatButton CreateRepeatButton(Transform parent, string label, System.Action onFire)
        {
            var button = CreateButton(parent, label, () => { });
            var repeat = button.gameObject.AddComponent<RepeatButton>();
            repeat.OnFire += () => onFire();
            return repeat;
        }

        static LongPressButton CreateLongPressButton(Transform parent, string label, System.Action onTap, System.Action onLongPress)
        {
            // Built via the normal button (for its visual pressed/highlighted
            // feedback) with onClick left unbound -- LongPressButton owns the
            // actual tap-vs-hold decision instead.
            var button = CreateButton(parent, label, () => { });
            var longPress = button.gameObject.AddComponent<LongPressButton>();
            longPress.OnTap += onTap;
            longPress.OnLongPress += onLongPress;
            return longPress;
        }

        /// <summary>A toggle with a fixed, descriptive label beside it (X, Y, Z,
        /// Pan, Tilt, Roll, etc.) -- the label never changes; only the checkmark
        /// state does.</summary>
        static Toggle CreateToggle(Transform parent, string label, System.Action<bool> onChanged)
        {
            var go = BuildToggleVisual(parent, $"Toggle_{label}", out Image bgImg, out Image checkImg);
            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.colors = MakeColors(ButtonBg, ButtonPressed);
            toggle.isOn = false;
            toggle.onValueChanged.AddListener(v => onChanged(v));

            var labelText = CreateLabel(go.transform, label, 18);
            var labelRt = labelText.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0, -0.5f);
            labelRt.anchorMax = new Vector2(1, 0);

            return toggle;
        }

        /// <summary>A toggle whose own inline text reflects its current state
        /// ("On"/"Off") -- for standalone settings switches, where a static
        /// "On" label regardless of state read as broken/unresponsive.</summary>
        static Toggle CreateStateToggle(Transform parent, System.Action<bool> onChanged)
        {
            var go = BuildToggleVisual(parent, "Toggle_State", out Image bgImg, out Image checkImg);
            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.colors = MakeColors(ButtonBg, ButtonPressed);

            var labelText = CreateLabel(go.transform, "Off", 16);
            var labelRt = labelText.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            toggle.isOn = false;
            toggle.onValueChanged.AddListener(v =>
            {
                labelText.text = v ? "On" : "Off";
                onChanged(v);
            });

            return toggle;
        }

        static void SetStateToggle(Toggle toggle, bool value)
        {
            toggle.SetIsOnWithoutNotify(value);
            var label = toggle.GetComponentInChildren<Text>();
            if (label != null) label.text = value ? "On" : "Off";
        }

        static GameObject BuildToggleVisual(Transform parent, string name, out Image bgImg, out Image checkImg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 90;
            le.minHeight = 50;

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bgImg = bgGo.GetComponent<Image>();
            bgImg.color = ButtonBg;

            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(bgGo.transform, false);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.15f, 0.15f); checkRt.anchorMax = new Vector2(0.85f, 0.85f);
            checkRt.offsetMin = Vector2.zero; checkRt.offsetMax = Vector2.zero;
            checkImg = checkGo.GetComponent<Image>();
            checkImg.color = ButtonActiveBg;

            return go;
        }

        /// <summary>A label followed by three small numeric fields (X/Y/Z),
        /// calling back with the axis index and the raw text on each edit.</summary>
        static void CreateAxisRow(Transform parent, string label, InputField[] targets, System.Action<int, string> onEdit)
        {
            var row = CreateRow(parent, $"Axis_{label}");
            CreateLabel(row.transform, label, 18);
            string[] axisNames = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                CreateLabel(row.transform, axisNames[i], 14);
                var field = CreateInputField(row.transform, "1.0", narrow: true);
                field.text = "1";
                field.onEndEdit.AddListener(text => onEdit(axis, text));
                targets[i] = field;
            }
        }

        static void CreateAxisFlipRow(Transform parent, string label, Toggle[] targets, System.Action<int, bool> onChanged)
        {
            var row = CreateRow(parent, $"Flip_{label}");
            CreateLabel(row.transform, label, 18);
            string[] axisNames = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                targets[i] = CreateToggle(row.transform, axisNames[i], v => onChanged(axis, v));
            }
        }

        static Slider CreateSlider(Transform parent, string label, float min, float max, float initial, System.Action<float> onChanged)
        {
            var row = CreateRow(parent, $"Slider_{label}");
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childForceExpandWidth = false;

            CreateLabel(row.transform, label, 18);

            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(row.transform, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 260;
            le.minHeight = 30;

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.25f); bgRt.anchorMax = new Vector2(1, 0.75f);
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bgGo.GetComponent<Image>().color = ButtonBg;

            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(go.transform, false);
            var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0, 0.25f); fillAreaRt.anchorMax = new Vector2(1, 0.75f);
            fillAreaRt.offsetMin = new Vector2(5, 0); fillAreaRt.offsetMax = new Vector2(-5, 0);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(0, 1); fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            fillGo.GetComponent<Image>().color = ButtonActiveBg;

            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(go.transform, false);
            var handleAreaRt = handleAreaGo.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero; handleAreaRt.anchorMax = Vector2.one; handleAreaRt.offsetMin = Vector2.zero; handleAreaRt.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24, 0);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.color = Color.white;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.colors = MakeColors(Color.white, ButtonPressed);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = initial;
            slider.onValueChanged.AddListener(v => onChanged(v));

            return slider;
        }

        /// <summary>A tall vertical slider spanning -1..1, centered at 0, with
        /// a <see cref="ZoomRockerSlider"/> that snaps it back to center on
        /// release -- the "push and hold to zoom continuously" rocker, as
        /// opposed to <see cref="CreateSlider"/>'s absolute left-to-right
        /// position-is-the-value control.</summary>
        static Slider CreateVerticalRockerSlider(Transform parent)
        {
            var go = new GameObject("RockerSlider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = 1;
            le.preferredWidth = 70;

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.25f, 0f); bgRt.anchorMax = new Vector2(0.75f, 1f);
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bgGo.GetComponent<Image>().color = ButtonBg;

            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(go.transform, false);
            var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0.25f, 0f); fillAreaRt.anchorMax = new Vector2(0.75f, 1f);
            fillAreaRt.offsetMin = new Vector2(0, 5); fillAreaRt.offsetMax = new Vector2(0, -5);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(1, 0); fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            fillGo.GetComponent<Image>().color = ButtonActiveBg;

            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(go.transform, false);
            var handleAreaRt = handleAreaGo.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero; handleAreaRt.anchorMax = Vector2.one; handleAreaRt.offsetMin = Vector2.zero; handleAreaRt.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(0, 32);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.color = Color.white;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.colors = MakeColors(Color.white, ButtonPressed);
            slider.direction = Slider.Direction.BottomToTop;
            slider.minValue = -1f;
            slider.maxValue = 1f;
            slider.value = 0f;

            go.AddComponent<ZoomRockerSlider>();

            return slider;
        }

        static InputField CreateInputField(Transform parent, string placeholder, bool narrow = false)
        {
            var go = new GameObject($"Input_{placeholder}", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = narrow ? 90 : 420;
            le.minHeight = 50;
            var bgImg = go.GetComponent<Image>();
            bgImg.color = new Color(1, 1, 1, 0.9f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10, 4); textRt.offsetMax = new Vector2(-10, -4);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = narrow ? 18 : 20;
            text.color = Color.black;
            text.alignment = narrow ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            text.supportRichText = false;

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            placeholderGo.transform.SetParent(go.transform, false);
            var phRt = placeholderGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(10, 4); phRt.offsetMax = new Vector2(-10, -4);
            var phText = placeholderGo.GetComponent<Text>();
            phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            phText.fontSize = narrow ? 18 : 20;
            phText.color = new Color(0, 0, 0, 0.4f);
            phText.fontStyle = FontStyle.Italic;
            phText.text = placeholder;
            phText.alignment = narrow ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = phText;
            field.targetGraphic = bgImg;
            field.colors = MakeColors(new Color(1, 1, 1, 0.9f), new Color(0.85f, 0.93f, 1f, 1f));
            if (narrow) field.contentType = InputField.ContentType.DecimalNumber;

            return field;
        }

        /// <summary>A native uGUI Dropdown, built by hand since nothing in
        /// this app is hand-authored as a prefab. Uses RectMask2D for the
        /// popup list's clipping rather than the stencil Mask+Image pattern --
        /// that combination rendered fine in the Editor but reproduced blank
        /// on the real device (see the Settings/Calibration scroll-view fix),
        /// so every clipped list in this app now avoids it from the start.</summary>
        static Dropdown CreateDropdown(Transform parent, string[] options, int initialIndex, System.Action<int> onChanged)
        {
            var go = new GameObject("Dropdown", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 160;
            le.minHeight = 50;
            var bgImg = go.GetComponent<Image>();
            bgImg.color = ButtonBg;

            var dropdown = go.GetComponent<Dropdown>();
            dropdown.targetGraphic = bgImg;
            dropdown.colors = MakeColors(ButtonBg, ButtonPressed);

            var captionText = CreateLabel(go.transform, "", 18);
            var captionRt = captionText.GetComponent<RectTransform>();
            captionRt.anchorMin = Vector2.zero; captionRt.anchorMax = Vector2.one;
            captionRt.offsetMin = new Vector2(10, 2); captionRt.offsetMax = new Vector2(-10, -2);
            dropdown.captionText = captionText;

            const float itemHeight = 32f;
            float listHeight = itemHeight * options.Length;

            var templateGo = new GameObject("Template", typeof(RectTransform), typeof(Image));
            templateGo.transform.SetParent(go.transform, false);
            var templateRt = templateGo.GetComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0, 0);
            templateRt.anchorMax = new Vector2(1, 0);
            templateRt.pivot = new Vector2(0.5f, 1f);
            templateRt.anchoredPosition = new Vector2(0, -4);
            templateRt.sizeDelta = new Vector2(0, listHeight);
            templateGo.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.06f, 0.98f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(templateGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero; viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero; viewportRt.offsetMax = Vector2.zero;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, listHeight);

            var itemGo = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            itemGo.transform.SetParent(contentGo.transform, false);
            var itemRt = itemGo.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0, 1); itemRt.anchorMax = new Vector2(1, 1);
            itemRt.pivot = new Vector2(0.5f, 1f);
            itemRt.sizeDelta = new Vector2(0, itemHeight);

            var itemBgGo = new GameObject("Item Background", typeof(RectTransform), typeof(Image));
            itemBgGo.transform.SetParent(itemGo.transform, false);
            var itemBgRt = itemBgGo.GetComponent<RectTransform>();
            itemBgRt.anchorMin = Vector2.zero; itemBgRt.anchorMax = Vector2.one;
            itemBgRt.offsetMin = Vector2.zero; itemBgRt.offsetMax = Vector2.zero;
            var itemBgImg = itemBgGo.GetComponent<Image>();
            itemBgImg.color = ButtonBg;

            var itemCheckGo = new GameObject("Item Checkmark", typeof(RectTransform), typeof(Image));
            itemCheckGo.transform.SetParent(itemGo.transform, false);
            var itemCheckRt = itemCheckGo.GetComponent<RectTransform>();
            itemCheckRt.anchorMin = new Vector2(0, 0.5f); itemCheckRt.anchorMax = new Vector2(0, 0.5f);
            itemCheckRt.sizeDelta = new Vector2(16, 16);
            itemCheckRt.anchoredPosition = new Vector2(14, 0);
            var itemCheckImg = itemCheckGo.GetComponent<Image>();
            itemCheckImg.color = ButtonActiveBg;

            var itemLabelText = CreateLabel(itemGo.transform, "", 16);
            itemLabelText.alignment = TextAnchor.MiddleLeft;
            var itemLabelRt = itemLabelText.GetComponent<RectTransform>();
            itemLabelRt.anchorMin = Vector2.zero; itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(30, 1); itemLabelRt.offsetMax = new Vector2(-8, -1);

            var itemToggle = itemGo.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBgImg;
            itemToggle.graphic = itemCheckImg;
            itemToggle.colors = MakeColors(ButtonBg, ButtonPressed);
            itemToggle.isOn = true;

            dropdown.itemText = itemLabelText;
            dropdown.template = templateRt;
            templateGo.SetActive(false);

            dropdown.options.Clear();
            foreach (var opt in options) dropdown.options.Add(new Dropdown.OptionData(opt));
            dropdown.SetValueWithoutNotify(initialIndex);
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(v => onChanged(v));

            return dropdown;
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        /// <summary>A vertically-scrolling area (Viewport + Content, masked and
        /// clamped) filling its parent. Returns the Content transform to parent
        /// children under -- Content grows to fit them (ContentSizeFitter) and
        /// the ScrollRect clips/scrolls whatever doesn't fit in the visible area.
        /// Any screen with an unbounded amount of content (Settings, in
        /// particular) needs this rather than trusting everything to fit --
        /// phone screens are short in landscape and content only grows over
        /// time.</summary>
        static Transform CreateScrollView(Transform parent, string name)
        {
            var scrollGo = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            // RectMask2D rather than Mask+Image: a stencil-based Mask needs its
            // (invisible) Graphic to actually render every frame to establish
            // the stencil buffer, and a near-zero-alpha Image is exactly what
            // some mobile GPU drivers cull as a batching optimization -- which
            // silently drops the whole masked subtree. That reproduced clean
            // in the Editor but blanked the entire scroll body on the real
            // device. RectMask2D clips by rect overlap alone, no Graphic or
            // stencil buffer involved.
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            var contentFitter = contentGo.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // ContentSizeFitter can only compute a size from children when the
            // same GameObject also has a LayoutGroup -- without this, Content
            // silently stays at its initial zero height/width and everything
            // inside the scroll view renders invisible (this was the "Settings
            // isn't rendering all the options" bug).
            var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            // Must be true, not just for Content to size its single child --
            // with this off, LayoutGroup reads the child's raw *current* rect
            // instead of properly querying its preferred size, which caches
            // whatever stale/default size the child had before it resolved
            // its own layout this pass (this was the actual reason Content
            // stayed stuck at a 100px default height even after the earlier
            // fix added this LayoutGroup).
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRt;
            scrollRect.content = contentRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            return contentGo.transform;
        }

        /// <summary>Two side-by-side vertical columns -- lets a screen use a
        /// landscape phone's abundant width instead of stacking everything into
        /// one tall column that only fits by scrolling past most of it.</summary>
        static (Transform left, Transform right) CreateTwoColumns(Transform parent, string name)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 40;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = row.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var left = new GameObject("Left", typeof(RectTransform));
            left.transform.SetParent(row.transform, false);
            var leftLayout = left.AddComponent<VerticalLayoutGroup>();
            leftLayout.spacing = 14;
            leftLayout.childAlignment = TextAnchor.UpperCenter;
            var leftFitter = left.AddComponent<ContentSizeFitter>();
            leftFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var right = new GameObject("Right", typeof(RectTransform));
            right.transform.SetParent(row.transform, false);
            var rightLayout = right.AddComponent<VerticalLayoutGroup>();
            rightLayout.spacing = 14;
            rightLayout.childAlignment = TextAnchor.UpperCenter;
            var rightFitter = right.AddComponent<ContentSizeFitter>();
            rightFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return (left.transform, right.transform);
        }
    }
}
