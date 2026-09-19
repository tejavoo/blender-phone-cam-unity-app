using System.Collections.Generic;
using CamLinkPro.App;
using CamLinkPro.Networking;
using CamLinkPro.Pairing;
using CamLinkPro.Pipeline;
using Coffee.UIEffects;
using com.convalise.UnityMaterialSymbols;
using Gilzoide.FlexUi;
using Gilzoide.FlexUi.Yoga;
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

        // Dark-navy palette matching the reviewed mockup
        // (claude.ai/artifact/2sDLBzMfR4Hdf16u5V9tmW) rather than the
        // original white-alpha-on-black scheme -- flat opaque surfaces for
        // buttons/cards (so they read the same whether there's an AR feed
        // behind them or not), translucent navy (not translucent black) for
        // the full-screen panels that intentionally let that feed show
        // through.
        static readonly Color PanelBg = new Color(0.043f, 0.051f, 0.063f, 0.55f);
        static readonly Color CardBg = new Color(0.078f, 0.090f, 0.110f, 0.97f);
        static readonly Color ButtonBg = new Color(0.114f, 0.129f, 0.161f, 0.97f);
        static readonly Color ButtonPressed = new Color(0.29f, 0.565f, 1f, 1f);
        // Same value as ButtonPressed -- separate name for solid accent-blue
        // CTA buttons (mockup's Scan QR / Let's Record / Record), which are
        // that color at rest, not just while pressed.
        static readonly Color AccentBg = new Color(0.29f, 0.565f, 1f, 1f);
        static readonly Color ButtonDisabled = new Color(0.114f, 0.129f, 0.161f, 0.5f);
        static readonly Color ButtonActiveBg = new Color(0.29f, 0.565f, 1f, 0.9f);
        static readonly Color DangerBg = new Color(0.478f, 0.161f, 0.176f, 0.95f);
        static readonly Color DangerPressed = new Color(0.898f, 0.282f, 0.302f, 1f);
        static readonly Color ChipGreen = new Color(0.239f, 0.839f, 0.549f, 1f);
        static readonly Color ChipYellow = new Color(0.910f, 0.702f, 0.224f, 1f);
        static readonly Color ChipRed = new Color(0.898f, 0.282f, 0.302f, 1f);


        // See the long comment in BuildLandingPanel() before flipping this.
        const bool UiToolkitMigrationEnabled = false;

        // -- screens --
        GameObject landingPanel;
        GameObject hudPanel;      // "Recording"
        GameObject settingsPanel;
        GameObject settingsGeneralTab, settingsCalibrationTab, settingsAppTab, settingsConnectionTab, settingsCameraTab;
        Button settingsGeneralTabButton, settingsCalibrationTabButton, settingsAppTabButton, settingsConnectionTabButton, settingsCameraTabButton;
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

        GameObject startingCameraBatteryRow;
        MaterialSymbol startingCameraBatteryIcon;
        Text startingCameraBatteryLabel;
        GameObject startingCameraTrackingRow;
        MaterialSymbol startingCameraTrackingIcon;
        Text startingCameraTrackingLabel;
        Image startingCameraSpinner;
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
        Image creditHeartImage;

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
        GameObject terminalReadoutBox;
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
        Toggle terminalReadoutVisibilityToggle;

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
        Text focalLengthReadoutText, sensorWidthReadoutText;
        readonly Text[] positionOffsetAxisTexts = new Text[3];
        readonly Text[] rotationOffsetAxisTexts = new Text[3];
        readonly Text[] settingsRotationOffsetLabels = new Text[3];
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
            if (startingCameraPanel != null && startingCameraPanel.activeSelf)
            {
                RefreshArWarmup();
                if (startingCameraSpinner != null)
                    startingCameraSpinner.rectTransform.Rotate(0f, 0f, -270f * Time.unscaledDeltaTime);
            }
            if (savedToastHideAtUnscaled >= 0f && Time.unscaledTime >= savedToastHideAtUnscaled)
            {
                savedToastPanel.SetActive(false);
                savedToastHideAtUnscaled = -1f;
            }
            RefreshQrScanTimeout();
            AnimateCreditHeart();
        }

        /// <summary>Continuous pump (scale pulse) + slow spin on the "Made
        /// with (heart) by Teja" credit line's heart glyph -- purely
        /// decorative, only runs while Landing is actually visible.</summary>
        void AnimateCreditHeart()
        {
            if (creditHeartImage == null || !landingPanel.activeSelf) return;
            float t = Time.unscaledTime;
            float pump = 1f + 0.22f * Mathf.Max(0f, Mathf.Sin(t * 3.2f));
            creditHeartImage.rectTransform.localScale = new Vector3(pump, pump, 1f);
            // Spins around the vertical axis through the heart's center and
            // its top cleft (where the two lobes meet) -- a flip/turn, not a
            // flat clock-hand spin around the screen-facing Z axis.
            creditHeartImage.rectTransform.localRotation = Quaternion.Euler(0f, t * 90f, 0f);
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
            if (terminalReadoutBox != null) terminalReadoutBox.SetActive(AppPreferences.TerminalReadoutVisible);
        }

        /// <summary>Read-only readout of the two Blender-camera values already
        /// on the wire today (focal_length_mm/sensor_width_mm in every pose
        /// packet) -- Settings -> Blender Camera. Polled only while Settings
        /// is open.</summary>
        void RefreshLiveCameraReadout()
        {
            if (liveCameraReadoutText == null || controller == null || !settingsPanel.activeSelf) return;
            bool live = controller.HasRawPose && controller.PoseSource != null;
            liveCameraReadoutText.text = live ? "Live" : "-- (no live pose yet)";
            focalLengthReadoutText.text = live
                ? $"{controller.Zoom.Resolve(controller.PoseSource.LiveFocalLengthMm):0.0}"
                : "--";
            sensorWidthReadoutText.text = live ? $"{AR.ZoomState.SensorWidthMm:0.0}" : "--";
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
            // See the UiToolkitMigrationEnabled note in BuildLandingPanel();
            // kept inactive (flag false) until the runtime-panel input
            // issue is confirmed resolved on-device.
            if (uiToolkitLandingRoot != null)
                uiToolkitLandingRoot.SetActive(UiToolkitMigrationEnabled && screen == landingPanel);
            // Dev-only pose readout -- gated by the Settings > Diagnostics
            // Overlay toggle (AppPreferences.DiagnosticsOverlayEnabled), not
            // just "on the HUD screen": it was previously showing for every
            // user regardless of that toggle, bleeding green debug text over
            // the top bar on every Recording screen visit.
            if (debugOverlayRoot != null) debugOverlayRoot.SetActive(screen == hudPanel && AppPreferences.DiagnosticsOverlayEnabled);
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
            // Below the TopBar's left-clustered nav/rig/freeze rows (~240px
            // tall), not overlapping them -- this only ever shows when the
            // Settings > Diagnostics Overlay toggle is on, but it used to
            // sit right at the top-left corner and bleed over that cluster.
            rt.anchoredPosition = new Vector2(10, -250);
            rt.sizeDelta = new Vector2(480, 170);
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
            // failed to fix it.
            //
            // Two more candidate fixes have since been added but NOT yet
            // confirmed working on-device (no way to test them from here):
            //   1. LandingPanelSettings' scale mode was Constant Physical
            //      Size (DPI-driven) -- switched to Scale With Screen Size,
            //      since bogus/zero DPI reporting is a known source of a
            //      panel that renders fine but never hit-tests.
            //   2. LandingScreenUITK now toggles UIDocument.enabled off/on
            //      one frame after activating (see
            //      ReconnectToEventSystemNextFrame), the supported
            //      non-obsolete way to force it to re-register its runtime
            //      panel against EventSystem.current.
            // To try them: flip this to true, deploy, and watch Console for
            // "CamLinkPro-UITK: PointerDownEvent" on tap. If taps still
            // don't land, flip back to false -- this uGUI screen (now
            // restyled to match the mockup directly, see BuildLandingPanel
            // below) is the real fallback, not a stopgap.
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

            var creditRow = CreateRow(landingPanel.transform, "CreditRow");
            var creditLeft = CreateLabel(creditRow.transform, "Made with", 26);
            creditLeft.color = new Color(1f, 1f, 1f, 0.9f);
            creditHeartImage = CreateHeartImage(creditRow.transform, 128, 46f, new Color(0.95f, 0.22f, 0.29f, 1f));
            var creditRight = CreateLabel(creditRow.transform, "by Teja", 26);
            creditRight.color = new Color(1f, 1f, 1f, 0.9f);

            // -- connected state: status + re-pair --
            landingConnectedGroup = new GameObject("ConnectedGroup", typeof(RectTransform));
            landingConnectedGroup.transform.SetParent(landingPanel.transform, false);
            var connectedLayout = landingConnectedGroup.AddComponent<VerticalLayoutGroup>();
            connectedLayout.childAlignment = TextAnchor.MiddleCenter;
            connectedLayout.spacing = 12;
            // A card, matching the mockup's connected-status card, not bare
            // chip+text floating on the background.
            var statusRow = CreateRow(landingConnectedGroup.transform, "StatusRow");
            var statusRowLayout = statusRow.GetComponent<HorizontalLayoutGroup>();
            statusRowLayout.padding = new RectOffset(24, 24, 16, 16);
            statusRowLayout.spacing = 14;
            var statusRowImg = statusRow.AddComponent<Image>();
            statusRowImg.color = CardBg;
            MakeRounded(statusRow, CardCornerRadiusPixels);
            landingConnectionChip = CreateChip(statusRow.transform, size: 10);
            landingStatusText = CreateLabel(statusRow.transform, "Connected", 20);
            // Plain accent-colored link, not a full button box -- matches
            // the mockup's "Re-pair" text link. Wrapped in CreateColumn, not
            // a direct child of landingConnectedGroup -- that VerticalLayoutGroup
            // defaults to childForceExpandWidth=true, which would otherwise
            // stretch this button's clickable area across the full screen
            // width even though textLink makes it visually invisible.
            var repairCol = CreateColumn(landingConnectedGroup.transform, "RepairColumn");
            CreateButton(repairCol.transform, "Re-pair", () => controller?.Unpair(), textLink: true, width: 120, height: 40, fontSize: 16);

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

            // Stacked, not side-by-side -- the mockup treats "Enter Manually"
            // as a secondary fallback underneath the primary CTA, not a peer
            // action next to it. Wrapped in CreateColumn so the two buttons
            // keep their own fixed widths instead of being stretched
            // edge-to-edge by landingPairingGroup's default
            // childForceExpandWidth=true (see the Re-pair comment above).
            var ctaColumn = CreateColumn(landingPairingGroup.transform, "CtaColumn");
            CreateButton(ctaColumn.transform, "Scan QR", OpenQrScan, primary: true, width: 260, height: 64, fontSize: 20);
            CreateButton(ctaColumn.transform, "Enter Manually", () =>
            {
                bool opening = !manualPanel.activeSelf;
                manualPanel.SetActive(opening);
                // Opened by hand, not via a QR hand-off -- the "QR scanned"
                // confirmation from a previous scan shouldn't linger here.
                if (opening && manualConfirmText != null) manualConfirmText.gameObject.SetActive(false);
            }, textLink: true, width: 170, height: 40, fontSize: 16);

            BuildManualPanel();
            manualEntry?.Prefill(PairingInfoStore.Load());
            manualPanel.SetActive(false);

            // Always visible on Landing regardless of paired state. Let's
            // Record is the primary CTA (accent blue) once paired -- the
            // disabled color still takes over automatically while not paired
            // (ButtonDisabled, via MakeColors), so this only changes how it
            // looks once it's actually usable.
            var bottomRow = CreateRow(landingPanel.transform, "LandingBottomRow");
            CreateButton(bottomRow.transform, "Settings", () => OpenSettingsFrom(landingPanel), width: 150, height: 52, fontSize: 16);
            letsRecordButton = CreateButton(bottomRow.transform, "Let's Record", OpenRecordingHud, primary: true, width: 190, height: 52, fontSize: 16);
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
                char? chipSymbol;
                if (controller.ChannelState == ChannelState.Connected)
                {
                    headline = controller.BlenderLiveState switch
                    {
                        Networking.BlenderLiveState.Live => $"Live -- driving camera ({p.Ip})",
                        Networking.BlenderLiveState.LiveNoData => $"Live, no pose data -- {p.Ip}",
                        Networking.BlenderLiveState.Connected => $"Connected to {p.Ip} -- Blender not live",
                        _ => $"Connected to {p.Ip}",
                    };
                    bool live = controller.BlenderLiveState == Networking.BlenderLiveState.Live;
                    chipColor = live ? ChipGreen : ChipYellow;
                    chipSymbol = live ? ChipSymbolGood : ChipSymbolDegraded;
                }
                else if (controller.ChannelState == ChannelState.Connecting)
                {
                    headline = $"Paired to {p.Ip} -- connecting...";
                    chipColor = ChipYellow;
                    chipSymbol = ChipSymbolDegraded;
                }
                else
                {
                    headline = $"Paired to {p.Ip} -- not reachable";
                    chipColor = ChipRed;
                    chipSymbol = ChipSymbolLost;
                }
                landingStatusText.text = $"{headline}\npose:{p.PosePort}  video/cmd:{p.VideoPort}";
                SetChipState(landingConnectionChip, chipColor, chipSymbol);
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

            // Top-right "camera active" pill, matching the mockup -- an
            // honest heads-up that the physical camera is live right now,
            // not just a title. Excluded from the VerticalLayoutGroup flow
            // (ignoreLayout) so it can sit absolutely positioned instead of
            // as another stacked row.
            var cameraActivePill = CreatePillRow(scanScreenPanel.transform, "CameraActivePill", spacing: 6f);
            var cameraActiveLe = cameraActivePill.AddComponent<LayoutElement>();
            cameraActiveLe.ignoreLayout = true;
            var cameraActiveRt = cameraActivePill.GetComponent<RectTransform>();
            cameraActiveRt.anchorMin = cameraActiveRt.anchorMax = new Vector2(1, 1);
            cameraActiveRt.pivot = new Vector2(1, 1);
            cameraActiveRt.anchoredPosition = new Vector2(-20, -20);
            var cameraActiveDot = CreateChip(cameraActivePill.transform, size: 6);
            cameraActiveDot.color = ChipYellow;
            var cameraActiveLabel = CreateLabel(cameraActivePill.transform, "CAMERA ACTIVE", 13);
            cameraActiveLabel.color = ChipYellow;

            var previewBox = CreatePanel(scanScreenPanel.transform, "PreviewBox", stretch: false);
            var boxRt = previewBox.GetComponent<RectTransform>();
            boxRt.sizeDelta = new Vector2(800, 800);
            var boxLe = previewBox.AddComponent<LayoutElement>();
            boxLe.preferredWidth = 800;
            boxLe.preferredHeight = 800;
            // Opt out of scanScreenPanel's default childForceExpandWidth=true --
            // without this the box (and every corner bracket anchored to its
            // corners) stretches wider than tall, breaking the square
            // viewfinder frame.
            boxLe.flexibleWidth = 0;
            boxLe.flexibleHeight = 0;

            var previewGO = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            previewGO.transform.SetParent(previewBox.transform, false);
            var previewRt = previewGO.GetComponent<RectTransform>();
            previewRt.anchorMin = Vector2.zero;
            previewRt.anchorMax = Vector2.one;
            previewRt.offsetMin = Vector2.zero;
            previewRt.offsetMax = Vector2.zero;
            var previewImage = previewGO.GetComponent<RawImage>();
            previewImage.color = Color.white;

            // Viewfinder corner brackets, matching the mockup's QR-scan frame
            // -- added after the RawImage so they draw on top of it, not
            // underneath (later siblings render on top).
            const float bracketArm = 42f, bracketThickness = 5f, bracketMargin = 18f;
            CreateCornerBracket(previewBox.transform, new Vector2(0, 1), bracketArm, bracketThickness, bracketMargin, AccentBg);
            CreateCornerBracket(previewBox.transform, new Vector2(1, 1), bracketArm, bracketThickness, bracketMargin, AccentBg);
            CreateCornerBracket(previewBox.transform, new Vector2(0, 0), bracketArm, bracketThickness, bracketMargin, AccentBg);
            CreateCornerBracket(previewBox.transform, new Vector2(1, 0), bracketArm, bracketThickness, bracketMargin, AccentBg);

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

            // Warning icon circle, matching the mockup's "!" badge above the
            // title instead of jumping straight to text.
            var iconGo = new GameObject("WarningIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(qrTimeoutOverlay.transform, false);
            var iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.minWidth = iconLe.minHeight = iconLe.preferredWidth = iconLe.preferredHeight = 64;
            iconLe.flexibleWidth = 0; // opt out of the parent VerticalLayoutGroup's default childForceExpandWidth=true
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.color = new Color(ChipYellow.r, ChipYellow.g, ChipYellow.b, 0.16f);
            MakeRounded(iconGo, RoundedSpriteTextureSize / 2);
            var iconLabel = CreateMaterialIcon(iconGo.transform, IconWarning, 32, ChipYellow);
            var iconLabelRt = iconLabel.GetComponent<RectTransform>();
            iconLabelRt.anchorMin = Vector2.zero;
            iconLabelRt.anchorMax = Vector2.one;
            iconLabelRt.offsetMin = Vector2.zero;
            iconLabelRt.offsetMax = Vector2.zero;

            CreateLabel(qrTimeoutOverlay.transform, "Couldn't find a pairing QR code", 24);
            var sub = CreateLabel(qrTimeoutOverlay.transform, "Make sure Blender's Cam Link Pro panel is showing its QR and this phone is on the same Wi-Fi network.", 16);
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            var subRt = sub.GetComponent<RectTransform>();
            var subLe = sub.gameObject.AddComponent<LayoutElement>();
            subLe.preferredWidth = 700;

            var row = CreateRow(qrTimeoutOverlay.transform, "QrTimeoutRow");
            CreateButton(row.transform, "Try Again", OpenQrScan, primary: true);
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
            if (startingCameraBatteryRow != null) startingCameraBatteryRow.SetActive(false);
            if (startingCameraTrackingRow != null) startingCameraTrackingRow.SetActive(false);
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

            if (startingCameraBatteryRow != null && SystemInfo.batteryLevel >= 0f)
            {
                startingCameraBatteryRow.SetActive(true);
                startingCameraBatteryLabel.text = $"Battery {SystemInfo.batteryLevel * 100f:0}%";
            }
            if (startingCameraTrackingRow != null)
            {
                startingCameraTrackingRow.SetActive(true);
                startingCameraTrackingLabel.text = tracking ? "AR tracking ready" : "Waiting for AR tracking...";
                startingCameraTrackingIcon.code = tracking ? IconCheckCircle : ''; // pending -- schedule/clock icon (verified against font cmap)
                startingCameraTrackingIcon.color = tracking ? ChipGreen : ChipYellow;
                startingCameraTrackingLabel.color = tracking ? ChipGreen : ChipYellow;
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
        /// TopBar's 240px band, not overlapping its nav/rig/freeze rows.</summary>
        void BuildDisconnectBanner()
        {
            disconnectBannerPanel = CreatePanel(hudPanel.transform, "DisconnectBanner", stretch: false);
            var rt = disconnectBannerPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -240);
            rt.sizeDelta = new Vector2(0, 48);
            var img = disconnectBannerPanel.GetComponent<Image>();
            img.color = new Color(0.35f, 0.08f, 0.08f, 0.92f);
            img.raycastTarget = false;

            var row = CreateRow(disconnectBannerPanel.transform, "DisconnectBannerRow");
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.spacing = 8;
            CreateMaterialIcon(row.transform, IconWarning, 20, ChipYellow);
            disconnectBannerText = CreateLabel(row.transform, "Blender disconnected -- recording may not be saving", 18);
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

            // Spinning accent arc, matching the mockup's loading spinner --
            // a partial-fill circle (the same rounded-sprite circle used
            // everywhere else) rotated continuously in Update() while this
            // panel is active.
            var spinnerGo = new GameObject("Spinner", typeof(RectTransform), typeof(Image));
            spinnerGo.transform.SetParent(startingCameraPanel.transform, false);
            var spinnerLe = spinnerGo.AddComponent<LayoutElement>();
            spinnerLe.minWidth = spinnerLe.minHeight = spinnerLe.preferredWidth = spinnerLe.preferredHeight = 64;
            spinnerLe.flexibleWidth = 0; // opt out of the parent VerticalLayoutGroup's default childForceExpandWidth=true
            startingCameraSpinner = spinnerGo.GetComponent<Image>();
            startingCameraSpinner.sprite = GetCircleSprite();
            startingCameraSpinner.type = Image.Type.Filled;
            startingCameraSpinner.fillMethod = Image.FillMethod.Radial360;
            startingCameraSpinner.fillAmount = 0.75f;
            startingCameraSpinner.color = AccentBg;
            // startingCameraPanel's VerticalLayoutGroup defaults to
            // childForceExpandWidth=true (stretching every direct child to
            // the panel's full width) -- preserveAspect keeps this a circle
            // instead of a squashed-wide ellipse.
            startingCameraSpinner.preserveAspect = true;

            CreateLabel(startingCameraPanel.transform, "Starting camera...", 26);
            CreateLabel(startingCameraPanel.transform, "Initializing AR tracking", 16);

            var checklistColumn = CreateColumn(startingCameraPanel.transform, "ChecklistColumn");
            (startingCameraBatteryRow, startingCameraBatteryIcon, startingCameraBatteryLabel) = CreateChecklistRow(checklistColumn.transform);
            (startingCameraTrackingRow, startingCameraTrackingIcon, startingCameraTrackingLabel) = CreateChecklistRow(checklistColumn.transform);

            BuildStorageFullPanel();
        }

        /// <summary>One row of the "Starting camera..." preflight checklist
        /// (mockup: "✓ Battery 82%" / "✓ AR tracking ready") -- a
        /// MaterialSymbol check icon plus a label, so readiness is shown
        /// with a real icon instead of the plain sentence the checklist
        /// used to be.</summary>
        (GameObject row, MaterialSymbol icon, Text label) CreateChecklistRow(Transform parent)
        {
            var row = CreateRow(parent, "ChecklistRow");
            var icon = CreateMaterialIcon(row.transform, IconCheckCircle, 18, ChipGreen);
            var label = CreateLabel(row.transform, "", 16);
            label.color = ChipGreen;
            return (row, icon, label);
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

            // Danger-colored warning icon circle, matching the mockup's "!"
            // badge above the title instead of jumping straight to text.
            var iconGo = new GameObject("WarningIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(storageFullPanel.transform, false);
            var iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.minWidth = iconLe.minHeight = iconLe.preferredWidth = iconLe.preferredHeight = 64;
            iconLe.flexibleWidth = 0; // opt out of the parent VerticalLayoutGroup's default childForceExpandWidth=true
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.color = new Color(DangerBg.r, DangerBg.g, DangerBg.b, 0.22f);
            MakeRounded(iconGo, RoundedSpriteTextureSize / 2);
            var iconLabel = CreateMaterialIcon(iconGo.transform, IconWarning, 32, ChipRed);
            var iconLabelRt = iconLabel.GetComponent<RectTransform>();
            iconLabelRt.anchorMin = Vector2.zero;
            iconLabelRt.anchorMax = Vector2.one;
            iconLabelRt.offsetMin = Vector2.zero;
            iconLabelRt.offsetMax = Vector2.zero;

            CreateLabel(storageFullPanel.transform, "Not enough storage to record", 26);
            storageFullText = CreateLabel(storageFullPanel.transform, "", 16);
            storageFullText.horizontalOverflow = HorizontalWrapMode.Wrap;
            storageFullText.alignment = TextAnchor.MiddleCenter;
            var textLe = storageFullText.gameObject.AddComponent<LayoutElement>();
            textLe.preferredWidth = 700;
            CreateButton(storageFullPanel.transform, "Dismiss", () => ShowScreen(landingPanel));
        }

        /// <summary>Compact, left-clustered top strip -- rebuilt to match the
        /// reviewed mockup (claude.ai/artifact/2sDLBzMfR4Hdf16u5V9tmW,
        /// RecordingIdle) instead of the old auto-flowing, center-aligned
        /// VerticalLayoutGroup stack, which centered every row in the middle
        /// of a 1920-wide screen (misaligned rig-preset row), rendered the
        /// status pills after the Settings button in a HorizontalLayoutGroup
        /// insertion-order row (instead of between Home and Settings), and
        /// reserved a flat, fixed 200px opaque band regardless of content.
        /// Uses Gilzoide.FlexUi.FlexLayout (real CSS-flexbox-style Yoga
        /// layout) for the three rows that actually need flex behaviour --
        /// the nav row's space-between (Home / pills / Settings) and the
        /// rig-preset/freeze-axis pill rows' left-aligned wrapping -- while
        /// each row's own internal content (buttons, toggles, pill cards)
        /// stays built from the existing Unity layout-group helpers.</summary>
        void BuildTopBar()
        {
            var topBar = CreatePanel(hudPanel.transform, "TopBar", stretch: false);
            var rt = topBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(0, 240);
            AddEdgeFadeBackground(topBar, fadeFromTop: true);
            var layout = topBar.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 16, 10);
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // -- Nav row: Home (left) / status pills (center) / Settings
            // (right) -- Yoga space-between instead of insertion-order flow,
            // so the pills land between the two nav buttons regardless of
            // build order.
            var navRow = CreateFlexRow(topBar.transform, "NavRow", Justify.SpaceBetween, Align.Center);
            var navRowLe = navRow.gameObject.AddComponent<LayoutElement>();
            navRowLe.minHeight = navRowLe.preferredHeight = 56;

            var homeBtn = CreateButton(navRow.transform, "< Home", CloseRecordingHud, width: 118, height: 52, fontSize: 16);
            AsFlexChild(homeBtn);

            // Split out from the nav buttons above (which must always stay
            // visible for navigation) so "Status Strip" visibility in
            // Settings -> App only hides the status indicators, never Home/
            // Settings themselves. Both pill rows share one flex-child
            // wrapper so Yoga treats "the pills" as a single centered block.
            var statusCluster = CreateRow(navRow.transform, "StatusCluster");
            AsFlexChild(statusCluster.transform);

            statusIndicatorsGroup = CreatePillRow(statusCluster.transform, "StatusIndicatorsGroup", spacing: 6f);
            connectionChip = CreateChip(statusIndicatorsGroup.transform, size: 7);
            // Text alongside the chip, not just its color -- a color-only
            // signal reads fine indoors but washes out in bright sunlight,
            // and doesn't distinguish anything for color-vision deficiency.
            connectionStateText = CreateLabel(statusIndicatorsGroup.transform, "Link:", 14);
            // Terminal-style readout (green monospace-ish on near-black),
            // matching the mockup's live-telemetry treatment -- gated by
            // its own HUD Visibility toggle (Settings -> App), same
            // hide-the-element-only convention as the rest of the strip.
            (terminalReadoutBox, connectionLatencyText) = CreateInlineTerminalValue(statusIndicatorsGroup.transform, "--", ChipGreen);
            calibrationBadge = CreateLabel(statusIndicatorsGroup.transform, "CAL", 12);
            calibrationBadge.color = ButtonActiveBg;
            calibrationBadge.gameObject.SetActive(false);

            // Blender's own state, learned only from the additive STATE
            // line -- its own pill card, next to the link pill, since it's
            // informational rather than navigation. Unknown (dim chip, "--")
            // covers both "just connected, first STATE line hasn't arrived
            // yet" and "an older add-on that predates this line" --
            // deliberately not red, since neither of those is actually an
            // error.
            blenderStatusRow = CreatePillRow(statusCluster.transform, "BlenderStatusRow", spacing: 6f);
            blenderLiveChip = CreateChip(blenderStatusRow.transform, size: 7);
            blenderLiveText = CreateLabel(blenderStatusRow.transform, "Blender: --", 14);
            poseSendingText = CreateLabel(blenderStatusRow.transform, "Pose: --", 14);

            var settingsBtn = CreateButton(navRow.transform, "Settings", () => OpenSettingsFrom(hudPanel), width: 118, height: 52, fontSize: 16);
            AsFlexChild(settingsBtn);

            // -- Rig-preset pills: small, left-aligned, wrapping row --
            // previously full-size (140x60) CreateButtons centered in the
            // middle of the screen by the old VerticalLayoutGroup.
            var rigFlex = CreateFlexRow(topBar.transform, "RigRow", Justify.FlexStart, Align.Center, gap: 12f);
            rigRow = rigFlex.gameObject;
            var rigRowLe = rigRow.AddComponent<LayoutElement>();
            rigRowLe.minHeight = rigRowLe.preferredHeight = 50;
            var presets = new[] { RigPreset.Handheld, RigPreset.Tripod, RigPreset.Dolly, RigPreset.Crane };
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                rigButtons[i] = CreateButton(rigRow.transform, preset.ToString(), () => ApplyPreset(preset), width: 126, height: 50, fontSize: 15);
                AsFlexChild(rigButtons[i]);
            }

            // -- Freeze-axis pills: compact toggle chips (no separate
            // checkbox+caption -- the pill itself highlights when armed),
            // left-aligned under the rig row, matching the mockup's small
            // "Pan-Y / Tilt-X / Roll-Z" strip instead of the old full-size
            // toggle-with-label-underneath control.
            var freezeFlex = CreateFlexRow(topBar.transform, "FreezeRow", Justify.FlexStart, Align.Center, gap: 10f);
            freezeRow = freezeFlex.gameObject;
            var freezeRowLe = freezeRow.AddComponent<LayoutElement>();
            freezeRowLe.minHeight = freezeRowLe.preferredHeight = 44;
            // Labelled with the underlying wire rotation channel too (which of
            // rot_x/y/z carries Tilt/Roll/Pan) -- placeholder text here, kept
            // truthful afterwards by RefreshRotationAxisLabels() since which
            // channel is which depends on the Rotation Axis Remap setting.
            string[] freezeLabels = { "X", "Y", "Z", "Pan-Z", "Tilt-X", "Roll-Y" };
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                freezeToggles[i] = CreateToggle(freezeRow.transform, freezeLabels[i], v => OnFreezeToggle(idx, v),
                    width: 76, height: 40, fontSize: 13, labelBelow: false);
                AsFlexChild(freezeToggles[i]);
            }
            // Order matches freezeToggles[3,4,5]: Pan, Tilt, Roll.
            freezeRotationLabels[0] = freezeToggles[3].GetComponentInChildren<Text>();
            freezeRotationLabels[1] = freezeToggles[4].GetComponentInChildren<Text>();
            freezeRotationLabels[2] = freezeToggles[5].GetComponentInChildren<Text>();
        }

        void RefreshConnectionChip(ChannelState state)
        {
            if (connectionChip == null) return;
            SetChipState(connectionChip, state switch
            {
                ChannelState.Connected => ChipGreen,
                ChannelState.Connecting => ChipYellow,
                _ => ChipRed,
            }, state switch
            {
                ChannelState.Connected => ChipSymbolGood,
                ChannelState.Connecting => ChipSymbolDegraded,
                _ => ChipSymbolLost,
            });
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
            if (blenderLiveChip != null) SetChipState(blenderLiveChip, state switch
            {
                BlenderLiveState.Live => ChipGreen,
                BlenderLiveState.LiveNoData => ChipYellow,
                BlenderLiveState.Connected => ChipYellow,
                _ => ButtonBg, // Unknown: no info yet, not an error -- avoid a false-alarm red
            }, state switch
            {
                BlenderLiveState.Live => ChipSymbolGood,
                BlenderLiveState.LiveNoData => ChipSymbolDegraded,
                BlenderLiveState.Connected => ChipSymbolDegraded,
                _ => (char?)null, // Unknown: no info yet -- no symbol, matches "not an error" intent above
            });
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
            // rendering below the visible screen entirely). Full width now --
            // RecordPanel/BigZoomSlider moved to the top-right (matching the
            // mockup's top:100px record card) so they no longer compete with
            // the bottom bar's right edge; the old 0.82-width anchor was
            // reserving dead space that's no longer needed.
            bottomBar = CreatePanel(hudPanel.transform, "BottomBar", stretch: false);
            var rt = bottomBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(0, 200);
            AddEdgeFadeBackground(bottomBar, fadeFromTop: false);
            var rowLayout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(28, 28, 14, 20);
            rowLayout.spacing = 24;
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

        /// <summary>Compact status/record card, top-right -- matches the
        /// mockup's ~148px-wide record panel sitting just under the top bar,
        /// instead of the old 260x350 card vertically centered on screen
        /// (which is what made it read as "floating with excess margins,"
        /// far from everything else in the HUD).</summary>
        void BuildRecordControls()
        {
            var panel = CreatePanel(hudPanel.transform, "RecordPanel", stretch: false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-24, -256);
            rt.sizeDelta = new Vector2(300, 0);
            AddSoftShadow(panel);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            recordStatusText = CreateLabel(panel.transform, "Idle", 22);
            CreateDivider(panel.transform);
            lockStartButton = CreateButton(panel.transform, "Lock Start", () => controller?.LockStart(), width: 264, height: 52, fontSize: 17);
            cancelButton = CreateButton(panel.transform, "Cancel", () => controller?.CancelCurrent(), width: 264, height: 52, fontSize: 17);
            recordButton = CreateButton(panel.transform, "Record", () => controller?.BeginRecordCountdown(), width: 264, height: 52, fontSize: 17);
            stopButton = CreateButton(panel.transform, "Stop", () => controller?.Stop(), width: 264, height: 52, fontSize: 17);

            recordReadinessWarningText = CreateLabel(panel.transform, "Blender not live yet", 13);
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
        /// separate, independent control. Top-anchored directly left of
        /// RecordPanel and sized to roughly match its card height, instead of
        /// the old full-screen-height rocker vertically centered on its own.</summary>
        void BuildBigZoomSlider()
        {
            bigZoomSliderRoot = CreatePanel(hudPanel.transform, "BigZoomSliderPanel", stretch: false);
            var rt = bigZoomSliderRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-336, -256);
            rt.sizeDelta = new Vector2(90, 320);
            var layout = bigZoomSliderRoot.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 14, 14);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandHeight = false;

            CreateLabel(bigZoomSliderRoot.transform, "Zoom", 16);
            bigZoomSlider = CreateVerticalRockerSlider(bigZoomSliderRoot.transform);
            bigZoomFocalText = CreateLabel(bigZoomSliderRoot.transform, "auto", 14);

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
            SetStateToggle(terminalReadoutVisibilityToggle, AppPreferences.TerminalReadoutVisible);
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
            if (controller == null || positionOffsetAxisTexts[0] == null) return;
            var c = controller.Calibration;

            for (int i = 0; i < 3; i++)
            {
                positionOffsetAxisTexts[i].text = c.PositionOffset[i].ToString("0.000");
                rotationOffsetAxisTexts[i].text = c.RotationOffsetDeg[i].ToString("0.0") + "°";
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
            if (settingsRotationOffsetLabels[0] != null) settingsRotationOffsetLabels[0].text = tiltLabel;
            if (settingsRotationOffsetLabels[1] != null) settingsRotationOffsetLabels[1].text = rollLabel;
            if (settingsRotationOffsetLabels[2] != null) settingsRotationOffsetLabels[2].text = panLabel;
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

            // Tab bar -- General / Calibration / App / Connection / Camera,
            // matching the reviewed mockup's Settings structure (previously
            // one long flat two-column list with no tabs).
            var tabRow = CreateRow(settingsPanel.transform, "SettingsTabRow");
            var tabRowLe = tabRow.AddComponent<LayoutElement>();
            tabRowLe.minHeight = 48;
            settingsGeneralTabButton = CreateButton(tabRow.transform, "General", () => ShowSettingsTab(0), width: 110, height: 44, fontSize: 15);
            settingsCalibrationTabButton = CreateButton(tabRow.transform, "Calibration", () => ShowSettingsTab(1), width: 160, height: 44, fontSize: 15);
            settingsAppTabButton = CreateButton(tabRow.transform, "App", () => ShowSettingsTab(2), width: 100, height: 44, fontSize: 15);
            settingsConnectionTabButton = CreateButton(tabRow.transform, "Connection", () => ShowSettingsTab(3), width: 150, height: 44, fontSize: 15);
            settingsCameraTabButton = CreateButton(tabRow.transform, "Camera", () => ShowSettingsTab(4), width: 130, height: 44, fontSize: 15);

            // Everything below scrolls -- a phone screen in landscape is short,
            // and this list only grows over time, so trusting it to always fit
            // unscrolled isn't safe.
            var scrollGo = new GameObject("SettingsScroll", typeof(RectTransform));
            scrollGo.transform.SetParent(settingsPanel.transform, false);
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1;
            var content = CreateScrollView(scrollGo.transform, "Scroll");

            // One column per tab, all siblings in the same scroll Content --
            // ShowSettingsTab(int) shows exactly one at a time. Single-column
            // (not the old two-column split) since each tab's content is now
            // short enough on its own not to need the width.
            settingsGeneralTab = CreateColumn(content, "GeneralTab");
            settingsCalibrationTab = CreateColumn(content, "CalibrationTab");
            settingsAppTab = CreateColumn(content, "AppTab");
            settingsConnectionTab = CreateColumn(content, "ConnectionTab");
            settingsCameraTab = CreateColumn(content, "CameraTab");
            var generalCol = settingsGeneralTab.transform;
            var leftCol = settingsCalibrationTab.transform;
            var rightCol = settingsAppTab.transform;

            CreateLabel(generalCol, "General", 20);
            var fontScaleLabel = CreateLabel(generalCol, $"Text Size -- {AppPreferences.FontScale * 100f:0}%", 16);
            CreateSlider(generalCol, "", AppPreferences.MinFontScale, AppPreferences.MaxFontScale, AppPreferences.FontScale, v =>
            {
                AppPreferences.FontScale = v;
                fontScaleLabel.text = $"Text Size -- {AppPreferences.FontScale * 100f:0}%";
                RefreshAllFontSizes();
            });
            CreateLabel(generalCol, "Sample text at this size", 16);

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

            CreateLabel(leftCol, "Starting-Point Calibration (ROP/ROR on Recording)", 20);

            // Two separate cards, not one merged block -- ROP and ROR are
            // two independent calibration captures (reviewed mockup note),
            // and showing them as one combined summary blurred that.
            var ropRorRow = CreateRow(leftCol, "RopRorRow");
            var ropCard = CreateCard(ropRorRow.transform, "Reset Origin -- Position (ROP)",
                "Captured camera position, used as the (0,0,0) origin for this take.");
            var ropAxesRow = CreateRow(ropCard.transform, "RopAxesRow");
            string[] posAxisNames = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
                (_, positionOffsetAxisTexts[i]) = CreateTerminalValue(ropAxesRow.transform, posAxisNames[i], ChipGreen);
            CreateButton(ropCard.transform, "Re-zero", () => { controller?.CapturePositionZero(); RefreshCalibrationOffsetLabels(); },
                textLink: true, width: 90, height: 32, fontSize: 14);

            var rorCard = CreateCard(ropRorRow.transform, "Reset Origin -- Rotation (ROR)",
                "Captured camera rotation, used as the level/forward reference for this take.");
            var rorAxesRow = CreateRow(rorCard.transform, "RorAxesRow");
            for (int i = 0; i < 3; i++)
                (settingsRotationOffsetLabels[i], rotationOffsetAxisTexts[i]) = CreateTerminalValue(rorAxesRow.transform, "", ChipGreen);
            CreateButton(rorCard.transform, "Re-zero", () => { controller?.CaptureRotationZero(); RefreshCalibrationOffsetLabels(); },
                textLink: true, width: 90, height: 32, fontSize: 14);

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

            var terminalReadoutVisRow = CreateRow(rightCol, "TerminalReadoutVisibilityRow");
            CreateLabel(terminalReadoutVisRow.transform, "Terminal Readout (link latency)", 16);
            terminalReadoutVisibilityToggle = CreateStateToggle(terminalReadoutVisRow.transform, v =>
            {
                AppPreferences.TerminalReadoutVisible = v;
                RefreshHudVisibility();
            });

            var cameraCol = settingsCameraTab.transform;
            CreateLabel(cameraCol, "Camera Settings", 20);
            var bigZoomVisRow = CreateRow(cameraCol, "BigZoomVisibilityRow");
            CreateLabel(bigZoomVisRow.transform, "Zoom Slider Visibility (big rocker beside Record)", 16);
            bigZoomVisibilityToggle = CreateStateToggle(bigZoomVisRow.transform, v =>
            {
                AppPreferences.BigZoomSliderVisible = v;
                RefreshBigZoomSliderVisibility();
            });

            var zoomSensitivityRow = CreateRow(cameraCol, "ZoomSensitivityRow");
            CreateLabel(zoomSensitivityRow.transform, "Zoom Slider Sensitivity (0.25-4x)", 16);
            zoomSensitivityField = CreateInputField(zoomSensitivityRow.transform, "1", narrow: true);
            zoomSensitivityField.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, out float v)) AppPreferences.ZoomSliderSensitivity = v;
                zoomSensitivityField.SetTextWithoutNotify(AppPreferences.ZoomSliderSensitivity.ToString("0.##"));
            });

            CreateLabel(cameraCol, "Blender Camera", 20);
            liveCameraReadoutText = CreateLabel(cameraCol, "-- (no live pose yet)", 14);
            liveCameraReadoutText.color = new Color(1f, 1f, 1f, 0.6f);
            var liveCamRow = CreateRow(cameraCol, "LiveCameraReadoutRow");
            (_, focalLengthReadoutText) = CreateTerminalValue(liveCamRow.transform, "FOCAL LENGTH (MM)", ChipGreen);
            (_, sensorWidthReadoutText) = CreateTerminalValue(liveCamRow.transform, "SENSOR WIDTH (MM)", ChipGreen);

            CreateLabel(cameraCol, "Sensor height, lens distortion profile, stream quality and frame rate -- reserved for when the add-on exposes them. Not wired yet.", 13);

            var connectionCol = settingsConnectionTab.transform;
            CreateLabel(connectionCol, "Connection", 20);
            settingsIpField = CreateInputField(connectionCol, "IP address");
            settingsPoseField = CreateInputField(connectionCol, "Pose UDP port");
            settingsVideoField = CreateInputField(connectionCol, "Video/command TCP port");
            settingsTokenField = CreateInputField(connectionCol, "Token");
            settingsConnectionError = CreateLabel(connectionCol, "", 16);
            settingsConnectionError.color = new Color(1f, 0.4f, 0.4f, 1f);
            settingsConnectionError.gameObject.SetActive(false);

            var connectionRow = CreateRow(connectionCol, "ConnectionRow");
            CreateButton(connectionRow.transform, "Reconnect", ApplyConnectionEdits);
            CreateButton(connectionRow.transform, "Unpair", () =>
            {
                ShowConfirmDialog("Unpair from Blender? This ends the current session.", () =>
                {
                    controller?.Unpair();
                    ShowScreen(landingPanel);
                });
            }, danger: true);

            // Bottom breathing room so the last row in each tab isn't flush
            // against the scroll view's edge.
            foreach (var col in new[] { generalCol, leftCol, rightCol, cameraCol, connectionCol })
            {
                var spacerLe = new GameObject("BottomSpacer", typeof(RectTransform)).AddComponent<LayoutElement>();
                spacerLe.transform.SetParent(col, false);
                spacerLe.minHeight = 40;
            }

            ShowSettingsTab(0);
        }

        /// <summary>Switches the visible Settings tab (0=General,
        /// 1=Calibration, 2=App, 3=Connection, 4=Camera) and restyles the
        /// tab buttons so the active one reads as selected (matches the
        /// mockup's active-tab treatment).</summary>
        void ShowSettingsTab(int index)
        {
            settingsGeneralTab.SetActive(index == 0);
            settingsCalibrationTab.SetActive(index == 1);
            settingsAppTab.SetActive(index == 2);
            settingsConnectionTab.SetActive(index == 3);
            settingsCameraTab.SetActive(index == 4);

            SetTabButtonActive(settingsGeneralTabButton, index == 0);
            SetTabButtonActive(settingsCalibrationTabButton, index == 1);
            SetTabButtonActive(settingsAppTabButton, index == 2);
            SetTabButtonActive(settingsConnectionTabButton, index == 3);
            SetTabButtonActive(settingsCameraTabButton, index == 4);
        }

        static void SetTabButtonActive(Button button, bool active)
        {
            if (button == null) return;
            // Set via the Button's own ColorBlock (normalColor), not a raw
            // Image.color assignment -- Selectable re-applies colors.normalColor
            // on its next state transition (pointer enter/exit, select/
            // deselect), which would silently stomp a direct Image.color set
            // the first time the user hovers/taps any button on this screen.
            Color idle = active ? AccentBg : ButtonBg;
            button.colors = MakeColors(idle, ButtonPressed);
            var img = button.GetComponent<Image>();
            if (img != null) img.color = idle;
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                label.color = active ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            }
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
                if (freezeToggles[i] == null) continue;
                freezeToggles[i].SetIsOnWithoutNotify(values[i]);
                SetPillToggleColor(freezeToggles[i], values[i]);
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
            // Full-screen panels intentionally stay a translucent overlay --
            // the Recording HUD's AR feed shows through them. Smaller
            // decorative boxes (dialogs, cards, sub-panels) are opaque
            // "surface" cards with rounded corners instead, matching the
            // mockup rather than a flat translucent rectangle.
            img.color = stretch ? PanelBg : CardBg;
            if (!stretch) MakeRounded(go, CardCornerRadiusPixels);
            return go;
        }

        static Sprite roundedRectSpriteCache;
        static Sprite circleSpriteCache;
        const int RoundedSpriteTextureSize = 64;
        const int CardCornerRadiusPixels = 20;
        const int ButtonCornerRadiusPixels = 16;

        /// <summary>Rounded corners via a plain 9-sliced white-alpha sprite on
        /// the standard UI/Default shader, not a custom shader -- a vendored
        /// SDF rounded-corner shader (kirevdokimov/Unity-UI-Rounded-Corners)
        /// was tried first and turned out unreliable on-device: correct in
        /// the Editor, but rendered with square corners after being added to
        /// Graphics Settings' Always Included Shaders (to survive stripping),
        /// then rendered fully invisible after that fix. Not worth more
        /// device-build iterations chasing a mobile GPU/shader-compilation
        /// quirk in third-party shader code when a plain sprite -- using the
        /// exact same rendering path every other Image in the app already
        /// uses -- achieves the identical visual result with zero risk.</summary>
        static void MakeRounded(GameObject go, int radiusPixels)
        {
            var img = go.GetComponent<Image>();
            if (img == null) return;
            img.sprite = radiusPixels * 2 >= RoundedSpriteTextureSize
                ? GetCircleSprite()
                : GetRoundedRectSprite();
            img.type = Image.Type.Sliced;
        }

        static Sprite GetRoundedRectSprite() => roundedRectSpriteCache ??= CreateRoundedRectSprite(ButtonCornerRadiusPixels);
        static Sprite GetCircleSprite() => circleSpriteCache ??= CreateRoundedRectSprite(RoundedSpriteTextureSize / 2);

        /// <summary>A white square with alpha 1 inside a rounded-rect shape
        /// (radiusPixels corner radius) and a soft ~1px anti-aliased edge,
        /// set up for 9-slice scaling so the corner radius stays a fixed
        /// pixel size regardless of the final Image's width/height --
        /// radiusPixels == half the texture size renders as a full circle.</summary>
        static Sprite CreateRoundedRectSprite(int radiusPixels)
        {
            int size = RoundedSpriteTextureSize;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                bool yInCornerBand = y < radiusPixels || y > size - 1 - radiusPixels;
                for (int x = 0; x < size; x++)
                {
                    bool xInCornerBand = x < radiusPixels || x > size - 1 - radiusPixels;
                    float alpha = 1f;
                    if (xInCornerBand && yInCornerBand)
                    {
                        float cx = x < radiusPixels ? radiusPixels : size - 1 - radiusPixels;
                        float cy = y < radiusPixels ? radiusPixels : size - 1 - radiusPixels;
                        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        alpha = Mathf.Clamp01(radiusPixels - dist + 0.5f);
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            float b = radiusPixels;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>Subtle drop shadow via Coffee UIEffect, for the flat
        /// "surface" buttons/cards to read as raised rather than pasted flat
        /// on the background.</summary>
        static void AddSoftShadow(GameObject go)
        {
            var effect = go.AddComponent<UIEffect>();
            effect.shadowMode = ShadowMode.Shadow;
            effect.shadowDistance = new Vector2(0f, -3f);
            effect.shadowColorAlpha = 0.35f;
            effect.shadowBlurIntensity = 0.5f;
        }

        static Sprite topFadeSpriteCache;
        static Sprite bottomFadeSpriteCache;

        /// <summary>A smooth vertical alpha-gradient sprite (plain
        /// Image.Type.Simple, not sliced -- it needs to stretch, not tile)
        /// used behind the HUD's top/bottom bars so they read as a soft
        /// fade over the AR feed, like the mockup's
        /// linear-gradient(...,rgba(0,0,0,.55),transparent) bars, instead of
        /// a flat translucent rectangle with a hard edge.</summary>
        static Sprite GetEdgeFadeSprite(bool fadeFromTop)
        {
            if (fadeFromTop && topFadeSpriteCache != null) return topFadeSpriteCache;
            if (!fadeFromTop && bottomFadeSpriteCache != null) return bottomFadeSpriteCache;

            const int size = 64;
            var tex = new Texture2D(1, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size];
            for (int y = 0; y < size; y++)
            {
                // Texture row 0 is the bottom in Unity. fadeFromTop bars
                // (TopBar) want full alpha at the top edge fading to 0
                // downward; fadeFromTop == false (BottomBar) is the mirror.
                float t = y / (float)(size - 1);
                float alpha = fadeFromTop ? t : 1f - t;
                alpha = Mathf.Pow(alpha, 1.4f); // slightly faster falloff, matches the mockup's tight fade band
                pixels[y] = new Color32(0, 0, 0, (byte)(alpha * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, size), new Vector2(0.5f, 0.5f));
            if (fadeFromTop) topFadeSpriteCache = sprite; else bottomFadeSpriteCache = sprite;
            return sprite;
        }

        static Image AddEdgeFadeBackground(GameObject panel, bool fadeFromTop)
        {
            var img = panel.GetComponent<Image>();
            if (img == null) img = panel.AddComponent<Image>();
            img.sprite = GetEdgeFadeSprite(fadeFromTop);
            img.type = Image.Type.Simple;
            img.color = Color.white; // alpha baked into the texture
            img.raycastTarget = false;
            return img;
        }

        /// <summary>One L-shaped viewfinder corner bracket (two thin bars),
        /// matching the ScanQR mockup's camera-preview frame. <paramref
        /// name="corner"/> is which corner of <paramref name="parent"/> to
        /// anchor to, e.g. (0,1) for top-left.</summary>
        static void CreateCornerBracket(Transform parent, Vector2 corner, float armLength, float thickness, float margin, Color color)
        {
            float signX = corner.x < 0.5f ? 1f : -1f;
            float signY = corner.y < 0.5f ? 1f : -1f;
            var offset = new Vector2(signX * margin, signY * margin);

            void MakeBar(Vector2 size)
            {
                var go = new GameObject("Bracket", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = corner;
                rt.sizeDelta = size;
                rt.anchoredPosition = offset;
                var img = go.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            MakeBar(new Vector2(armLength, thickness));
            MakeBar(new Vector2(thickness, armLength));
        }

        /// <summary>A thin horizontal divider line, e.g. between the
        /// RecordPanel's status label and its buttons -- matches the
        /// mockup's record card, which has a faint separator there.</summary>
        static void CreateDivider(Transform parent, float thickness = 2f)
        {
            var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = thickness;
            le.preferredHeight = thickness;
            le.flexibleWidth = 1;
        }

        /// <summary>A flex-positioned row: its direct children (each also
        /// needing its own FlexLayout component -- see <see cref="AsFlexChild"/>)
        /// are laid out by Yoga per <paramref name="justify"/>/<paramref name="align"/>
        /// instead of Unity's HorizontalLayoutGroup, e.g. for the TopBar's nav
        /// row (Home / status pills / Settings, space-between) and the
        /// rig-preset/freeze-axis pill rows (left-aligned, wrapping).</summary>
        static FlexLayout CreateFlexRow(Transform parent, string name, Justify justify, Align align,
            Wrap wrap = Wrap.NoWrap, float gap = 10f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var flex = go.AddComponent<FlexLayout>();
            flex.FlexDirection = FlexDirection.Row;
            flex.JustifyContent = justify;
            flex.AlignItems = align;
            flex.FlexWrap = wrap;
            flex.Width = YGValue.Percent(100);
            flex.GapColumn = gap;
            flex.GapRow = gap;
            return flex;
        }

        /// <summary>Registers an already-built child (a normal Unity-layout
        /// subtree -- a Button, a Toggle, a small HorizontalLayoutGroup row)
        /// as a sized leaf node in its ancestor FlexLayout's tree, using that
        /// child's own existing LayoutElement/preferred-size for measurement
        /// instead of duplicating its sizing in Yoga terms.</summary>
        static void AsFlexChild(Component uiElement) => uiElement.gameObject.AddComponent<FlexLayout>();

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

        /// <summary>CreateRow's vertical counterpart -- a shrink-to-content
        /// column, immune to an ancestor VerticalLayoutGroup's default
        /// childForceExpandWidth=true (which otherwise stretches every
        /// direct child, buttons included, to the full parent width -- fine
        /// for text labels but visibly wrong for a button, which then
        /// renders as an edge-to-edge bar instead of the size it was given).</summary>
        static GameObject CreateColumn(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        // Settings -> General -> Text Size registry: every label CreateLabel
        // builds (which covers button/toggle captions too, since they're
        // built from it) is tracked here with its ORIGINAL/base size, so
        // RefreshAllFontSizes() can rescale every already-visible label
        // live instead of only affecting screens built after the change.
        static readonly List<(Text text, int baseFontSize, LayoutElement layoutElement, int baseMinHeight)> scalableLabels = new();

        static Text CreateLabel(Transform parent, string text, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 40;
            int baseMinHeight = fontSize + 10;
            scalableLabels.Add((t, fontSize, le, baseMinHeight));
            ApplyFontScaleTo(t, fontSize, le, baseMinHeight, AppPreferences.FontScale);
            return t;
        }

        static void ApplyFontScaleTo(Text t, int baseFontSize, LayoutElement le, int baseMinHeight, float scale)
        {
            t.fontSize = Mathf.RoundToInt(baseFontSize * scale);
            le.minHeight = Mathf.RoundToInt(baseMinHeight * scale);
        }

        /// <summary>Rescales every label built so far to the current
        /// AppPreferences.FontScale -- called once at startup and again
        /// live whenever the General tab's Text Size control changes.</summary>
        static void RefreshAllFontSizes()
        {
            float scale = AppPreferences.FontScale;
            foreach (var (text, baseFontSize, le, baseMinHeight) in scalableLabels)
            {
                if (text == null) continue; // pruned lazily -- destroyed labels from a torn-down screen, if any
                ApplyFontScaleTo(text, baseFontSize, le, baseMinHeight, scale);
            }
        }

        /// <summary>A small heart-shaped icon for the credit line -- drawn as
        /// a procedural texture rather than a Unicode glyph (♥/❤) because
        /// Unity's built-in LegacyRuntime.ttf has no symbol/dingbat glyph
        /// coverage at all: both U+2665 and U+2764 silently rendered as
        /// nothing on-device, not a fallback box, just an empty gap. A
        /// generated sprite can't hit that failure mode.</summary>
        static Image CreateHeartImage(Transform parent, int pixelSize, float displaySize, Color color)
        {
            var tex = new Texture2D(pixelSize, pixelSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[pixelSize * pixelSize];
            byte r = (byte)(color.r * 255), g = (byte)(color.g * 255), b = (byte)(color.b * 255);
            for (int py = 0; py < pixelSize; py++)
            {
                // Texture row 0 is the bottom in Unity -- map so the heart's
                // point ends up at the bottom and its two lobes at the top.
                float ny = (py / (float)(pixelSize - 1)) * 2.5f - 1.1f;
                for (int px = 0; px < pixelSize; px++)
                {
                    float nx = (px / (float)(pixelSize - 1)) * 2.6f - 1.3f;
                    // Classic implicit heart curve: (x^2 + y^2 - 1)^3 - x^2*y^3 <= 0
                    float val = Mathf.Pow(nx * nx + ny * ny - 1f, 3f) - nx * nx * ny * ny * ny;
                    // Soft edge (a couple of texels wide) instead of a hard
                    // cutoff, so it doesn't look jagged -- tight enough at
                    // this resolution to keep the top cleft between the two
                    // lobes crisp rather than blurring it into a blob.
                    float alpha = Mathf.Clamp01(0.5f - val * 16f);
                    pixels[py * pixelSize + px] = new Color32(r, g, b, (byte)(alpha * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, pixelSize, pixelSize), new Vector2(0.5f, 0.5f));

            var go = new GameObject("HeartIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            // An Image always stretches its sprite to fill its RectTransform's
            // actual width/height -- unlike Text, which just overflows past its
            // rect -- so this row's HorizontalLayoutGroup not controlling child
            // sizes (childControlWidth/Height are false) meant the heart was
            // rendering at a fresh RectTransform's default 100x100, not the
            // intended size. LayoutElement.min* alone doesn't override that;
            // setting sizeDelta directly does.
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(displaySize, displaySize);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.minHeight = displaySize;
            // The row's HorizontalLayoutGroup has childControlWidth/Height on,
            // so it assigns each child its *preferred* size, not just clamps
            // to the min -- and Image's own preferred size (with
            // preserveAspect) is the sprite's native pixel size (128), not
            // this. minWidth/minHeight alone is a floor, not the assigned
            // size; preferred is what actually gets used here.
            le.preferredWidth = le.preferredHeight = displaySize;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white; // colour already baked into the texture
            img.preserveAspect = true;
            return img;
        }

        /// <summary>A small circular status dot (connection health chip).</summary>
        static Image CreateChip(Transform parent, float size = 14)
        {
            var go = new GameObject("ConnectionChip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = size;
            le.minHeight = size;
            var img = go.GetComponent<Image>();
            img.color = ChipRed;
            MakeRounded(go, RoundedSpriteTextureSize / 2); // full circle

            // Accessibility: status dots must carry a symbol in addition to
            // hue (reviewed mockup, Round 2 note) -- color alone doesn't
            // read outdoors in bright sunlight or for colour-vision
            // deficiency. A plain Unicode glyph (check mark/ballot X) was
            // tried first, but Unity's built-in LegacyRuntime.ttf has no
            // symbol/dingbat coverage at all (see CreateHeartImage's note --
            // the exact same silent-empty-glyph failure would've hit here),
            // so this uses a MaterialSymbol icon instead, same as every
            // other icon in this file. Lives as a child so SetChipState can
            // update color+icon together without changing CreateChip's
            // Image return type (existing call sites all expect an Image).
            // At very small chip sizes a flat 0.62 ratio shrinks the icon
            // into illegibility -- floor it so the symbol stays readable
            // even on the smallest (7px) HUD chips.
            float iconSize = Mathf.Max(size * 0.62f, 6f);
            var symbolIcon = CreateMaterialIcon(go.transform, IconCheckCircle, iconSize, new Color(0.05f, 0.06f, 0.08f, 0.92f));
            var symbolRect = (RectTransform)symbolIcon.transform;
            // CreateChip's own GameObject has no LayoutGroup, so the
            // LayoutElement CreateMaterialIcon set is inert here -- size the
            // icon directly via sizeDelta instead of relying on a layout
            // pass that will never happen for this child.
            symbolRect.anchorMin = symbolRect.anchorMax = new Vector2(0.5f, 0.5f);
            symbolRect.anchoredPosition = Vector2.zero;
            symbolRect.sizeDelta = new Vector2(iconSize, iconSize);
            symbolIcon.gameObject.SetActive(false); // hidden until SetChipState gives it a real code

            return img;
        }

        /// <summary>Sets a status chip's color AND its accessibility icon
        /// together (check_circle good / priority_high degraded / cancel
        /// lost -- null hides the icon for a neutral/no-info state) -- see
        /// the accessibility note in CreateChip. Use this instead of setting
        /// chip.color directly everywhere a chip represents paired/live
        /// connection health.</summary>
        static void SetChipState(Image chip, Color color, char? symbolCode)
        {
            if (chip == null) return;
            chip.color = color;
            var symbolIcon = chip.transform.GetComponentInChildren<MaterialSymbol>(true);
            if (symbolIcon == null) return;
            symbolIcon.gameObject.SetActive(symbolCode.HasValue);
            if (symbolCode.HasValue) symbolIcon.code = symbolCode.Value;
        }

        const char ChipSymbolGood = IconCheckCircle;
        const char ChipSymbolDegraded = '';   // priority_high
        const char ChipSymbolLost = '';       // cancel

        // Material Symbols codepoints, verified against the bundled
        // MaterialSymbols-Standard/Filled.ttf's actual cmap (several of
        // Google's codepoint aliases per icon all resolve to the same
        // glyph -- these are just the first/primary one for each).
        const char IconCheckCircle = '';
        const char IconWarning = '';
        const char IconError = '';

        /// <summary>Material Symbols icon sized to fill its layout box --
        /// replaces the hand-drawn "!" / plain-text checkmarks the mockup's
        /// warning/success icons used to be built from.</summary>
        static MaterialSymbol CreateMaterialIcon(Transform parent, char code, float size, Color color, bool fill = false)
        {
            var go = new GameObject("MaterialIcon", typeof(RectTransform), typeof(MaterialSymbol));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.minHeight = le.preferredWidth = le.preferredHeight = size;
            le.flexibleWidth = 0;
            var icon = go.GetComponent<MaterialSymbol>();
            // MaterialSymbol.Start() only applies MiddleCenter alignment/
            // overflow via its own Init() when base.text is still empty at
            // that point -- setting .symbol here (which sets base.text
            // immediately) skips that path, so set them explicitly instead
            // of relying on Start()'s timing.
            icon.alignment = TextAnchor.MiddleCenter;
            icon.horizontalOverflow = HorizontalWrapMode.Overflow;
            icon.verticalOverflow = VerticalWrapMode.Overflow;
            icon.supportRichText = false;
            icon.symbol = new MaterialSymbolData(code, fill);
            icon.color = color;
            icon.raycastTarget = false;
            return icon;
        }

        /// <summary>Wraps a status row (colored dot + mono-ish label(s)) in a
        /// small rounded "pill" card background, matching the mockup's
        /// status pills (background rgba(20,23,28,.75), rounded corners)
        /// instead of the chip/text floating with no shared backing.</summary>
        static GameObject CreatePillRow(Transform parent, string name, float spacing = 8f)
        {
            var go = CreateRow(parent, name);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 7, 7);
            layout.spacing = spacing;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.078f, 0.090f, 0.110f, 0.75f);
            MakeRounded(go, 14);
            return go;
        }

        static readonly Color TerminalBg = new Color(0.02f, 0.035f, 0.03f, 1f);

        /// <summary>A rounded card with a title and optional description,
        /// matching the mockup's ROP/ROR "Reset Origin" cards -- a shared
        /// wrapper so any two-card readout section looks the same.</summary>
        static GameObject CreateCard(Transform parent, string title, string description)
        {
            var card = CreateColumn(parent, "Card");
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperLeft;
            var img = card.AddComponent<Image>();
            img.color = CardBg;
            MakeRounded(card, CardCornerRadiusPixels);

            CreateLabel(card.transform, title, 18);
            if (!string.IsNullOrEmpty(description))
            {
                var desc = CreateLabel(card.transform, description, 13);
                desc.color = new Color(1f, 1f, 1f, 0.6f);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                desc.alignment = TextAnchor.UpperLeft;
                var descLe = desc.gameObject.AddComponent<LayoutElement>();
                descLe.preferredWidth = 380;
            }
            return card;
        }

        /// <summary>One "terminal" readout box -- green monospace on
        /// near-black (red for an error/out-of-range value), matching the
        /// mockup's live-telemetry treatment. Returns both the axis-label
        /// Text (some callers need to update it later, e.g. Tilt/Roll/Pan
        /// naming depends on the Rotation Axis Remap setting) and the value
        /// Text.</summary>
        static (Text label, Text value) CreateTerminalValue(Transform parent, string axisLabel, Color valueColor)
        {
            var col = CreateColumn(parent, "TerminalValue");
            var layout = col.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            var label = CreateLabel(col.transform, axisLabel, 12);
            label.color = new Color(1f, 1f, 1f, 0.55f);

            var box = new GameObject("TerminalBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(col.transform, false);
            box.GetComponent<Image>().color = TerminalBg;
            MakeRounded(box, 8);
            var boxLe = box.AddComponent<LayoutElement>();
            boxLe.minWidth = 92;
            boxLe.minHeight = 36;
            boxLe.flexibleWidth = 0;

            var value = CreateLabel(box.transform, "0.000", 15);
            value.color = valueColor;
            value.fontStyle = FontStyle.Bold;
            var valueRt = value.GetComponent<RectTransform>();
            valueRt.anchorMin = Vector2.zero;
            valueRt.anchorMax = Vector2.one;
            valueRt.offsetMin = Vector2.zero;
            valueRt.offsetMax = Vector2.zero;
            return (label, value);
        }

        /// <summary>A compact terminal-style value box with no axis-label
        /// heading, for dropping inline into an existing labeled row (e.g.
        /// "Link: <box>ms</box>") instead of CreateTerminalValue's own
        /// label+box column.</summary>
        static (GameObject box, Text value) CreateInlineTerminalValue(Transform parent, string initialText, Color valueColor, float minWidth = 56, int fontSize = 13)
        {
            var box = new GameObject("InlineTerminalBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(parent, false);
            box.GetComponent<Image>().color = TerminalBg;
            MakeRounded(box, 6);
            var boxLe = box.AddComponent<LayoutElement>();
            boxLe.minWidth = minWidth;
            boxLe.minHeight = fontSize + 12;
            boxLe.flexibleWidth = 0;

            var value = CreateLabel(box.transform, initialText, fontSize);
            value.color = valueColor;
            value.fontStyle = FontStyle.Bold;
            var valueRt = value.GetComponent<RectTransform>();
            valueRt.anchorMin = Vector2.zero;
            valueRt.anchorMax = Vector2.one;
            valueRt.offsetMin = new Vector2(6, 0);
            valueRt.offsetMax = new Vector2(-6, 0);
            return (box, value);
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

        /// <summary>primary: solid accent-blue CTA (mockup's Scan QR /
        /// Let's Record buttons) instead of the flat neutral surface color.
        /// textLink: no box at all -- transparent background, no rounding/
        /// shadow, accent-colored label -- for secondary actions the mockup
        /// draws as plain underlined links (Enter Manually, Re-pair) rather
        /// than full buttons.</summary>
        static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, bool danger = false,
            float width = 140, float height = 60, int fontSize = 20, bool primary = false, bool textLink = false)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            Color idleColor = textLink ? new Color(0f, 0f, 0f, 0f) : danger ? DangerBg : primary ? AccentBg : ButtonBg;
            img.color = idleColor;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.minHeight = height;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            Color pressedColor = textLink ? new Color(1f, 1f, 1f, 0.08f) : danger ? DangerPressed : ButtonPressed;
            btn.colors = MakeColors(idleColor, pressedColor);
            btn.onClick.AddListener(onClick);
            if (!textLink)
            {
                MakeRounded(go, ButtonCornerRadiusPixels);
                AddSoftShadow(go);
            }

            var labelText = CreateLabel(go.transform, label, fontSize);
            if (textLink) labelText.color = AccentBg;
            else if (primary) labelText.color = Color.white;
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
        static Toggle CreateToggle(Transform parent, string label, System.Action<bool> onChanged,
            float width = 90, float height = 50, int fontSize = 18, bool labelBelow = true)
        {
            var go = BuildToggleVisual(parent, $"Toggle_{label}", out Image bgImg, out Image checkImg, width, height);
            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.colors = MakeColors(ButtonBg, ButtonPressed);
            toggle.isOn = false;
            toggle.onValueChanged.AddListener(v => onChanged(v));

            var labelText = CreateLabel(go.transform, label, fontSize);
            var labelRt = labelText.GetComponent<RectTransform>();
            if (labelBelow)
            {
                labelRt.anchorMin = new Vector2(0, -0.5f);
                labelRt.anchorMax = new Vector2(1, 0);
            }
            else
            {
                // Compact "pill chip" style (freeze-axis row): the label sits
                // centered inside the pill itself instead of hanging below
                // it -- matches the mockup's small inline toggle chips
                // rather than a checkbox-with-caption control.
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;
                checkImg.gameObject.SetActive(false);
                toggle.graphic = null;
                toggle.onValueChanged.AddListener(v => SetPillToggleColor(toggle, v));
            }

            return toggle;
        }

        /// <summary>Recolors a compact "pill chip" toggle (see the
        /// labelBelow: false branch of <see cref="CreateToggle"/>) to reflect
        /// its current isOn state -- needed both from its own onValueChanged
        /// listener and anywhere state is restored via SetIsOnWithoutNotify
        /// (e.g. <see cref="ApplyPreset"/>), since the "without notify" call
        /// deliberately skips listeners.</summary>
        static void SetPillToggleColor(Toggle toggle, bool isOn)
        {
            if (toggle.targetGraphic is Image img) img.color = isOn ? ButtonActiveBg : ButtonBg;
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

        static GameObject BuildToggleVisual(Transform parent, string name, out Image bgImg, out Image checkImg,
            float width = 90, float height = 50)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.minHeight = height;

            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bgImg = bgGo.GetComponent<Image>();
            bgImg.color = ButtonBg;
            MakeRounded(bgGo, RoundedSpriteTextureSize / 2); // pill: radius = half the box height

            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(bgGo.transform, false);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.15f, 0.15f); checkRt.anchorMax = new Vector2(0.85f, 0.85f);
            checkRt.offsetMin = Vector2.zero; checkRt.offsetMax = Vector2.zero;
            checkImg = checkGo.GetComponent<Image>();
            checkImg.color = ButtonActiveBg;
            MakeRounded(checkGo, RoundedSpriteTextureSize / 2); // knob: full circle

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
            MakeRounded(bgGo, RoundedSpriteTextureSize / 2); // pill track

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
            MakeRounded(fillGo, RoundedSpriteTextureSize / 2); // pill fill

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
            MakeRounded(handleGo, RoundedSpriteTextureSize / 2); // circular knob

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
            MakeRounded(bgGo, RoundedSpriteTextureSize / 2); // pill track

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
            MakeRounded(fillGo, RoundedSpriteTextureSize / 2); // pill fill

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
            MakeRounded(handleGo, RoundedSpriteTextureSize / 2); // circular knob

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
            bgImg.color = ButtonBg;
            MakeRounded(go, ButtonCornerRadiusPixels);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10, 4); textRt.offsetMax = new Vector2(-10, -4);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = narrow ? 18 : 20;
            text.color = Color.white;
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
            phText.color = new Color(1f, 1f, 1f, 0.4f);
            phText.fontStyle = FontStyle.Italic;
            phText.text = placeholder;
            phText.alignment = narrow ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = phText;
            field.targetGraphic = bgImg;
            field.colors = MakeColors(ButtonBg, ButtonPressed);
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
