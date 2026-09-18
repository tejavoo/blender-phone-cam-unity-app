using System.Collections.Generic;
using CamLinkPro.App;
using CamLinkPro.Networking;
using UnityEngine;
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

        void PulseHeart()
        {
            float t = Time.realtimeSinceStartup;
            float s = 1f + 0.18f * Mathf.Max(0f, Mathf.Sin(t * 3.2f));
            creditHeart.style.scale = new StyleScale(new Scale(new Vector2(s, s)));
        }

        void Update()
        {
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
