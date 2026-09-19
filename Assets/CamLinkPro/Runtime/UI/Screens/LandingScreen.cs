using UnityEngine;
using UnityEngine.UIElements;
using CamLinkPro.Networking;

namespace CamLinkPro.UI.Screens
{
    public class LandingScreen : IScreenController
    {
        VisualElement _root;
        AppShell _shell;

        VisualElement _wifiWarning;
        VisualElement _unpairedGroup;
        VisualElement _pairedGroup;
        VisualElement _manualEntryModal;
        VisualElement _aboutModal;
        VisualElement _recentChips;
        Label _manualEntryError;
        Label _pairedSubline;
        Button _letsRecordButton;

        PairingInfo? _currentPairing;

        Button _connectButton;
        VideoCommandChannel _probeChannel;
        IVisualElementScheduledItem _probeTickItem;
        IVisualElementScheduledItem _probeTimeoutItem;
        bool _probeResolved;
        const int ProbeTimeoutMs = 4000;

        IVisualElementScheduledItem _heartTick;
        float _heartStartTime;
        const float HeartBeatPeriod = 1.1f;
        const float HeartFlipPeriod = 4.0f;
        const string GitHubUrl = "https://github.com/tejavoo";

        public void Mount(VisualElement root, AppShell shell)
        {
            _root = root;
            _shell = shell;

            _wifiWarning = root.Q("WifiWarning");
            _unpairedGroup = root.Q("UnpairedGroup");
            _pairedGroup = root.Q("PairedGroup");
            _manualEntryModal = root.Q("ManualEntryModal");
            _aboutModal = root.Q("AboutModal");
            _recentChips = root.Q("RecentChips");
            _manualEntryError = root.Q<Label>("ManualEntryError");
            _pairedSubline = root.Q<Label>("PairedSubline");
            _letsRecordButton = root.Q<Button>("LetsRecordButton");

            _connectButton = root.Q<Button>("ConnectButton");

            root.Q<Button>("ScanQrButton").clicked += () => _shell.Navigate(ScreenId.ScanQr);
            root.Q<Button>("EnterManuallyLink").clicked += () => OpenModal(prefill: null);
            root.Q<Button>("ModalCancelButton").clicked += CloseModal;
            _connectButton.clicked += OnConnectClicked;
            root.Q<Button>("RepairLink").clicked += () => SetPaired(null);
            root.Q<Button>("SettingsButton").clicked += () =>
            {
                _shell.SettingsReturnTo = ScreenId.Landing;
                _shell.Navigate(ScreenId.SettingsGeneral);
            };
            _letsRecordButton.clicked += OnLetsRecordClicked;

            var creditLink = root.Q<Label>("CreditLink");
            creditLink.pickingMode = PickingMode.Position;
            creditLink.RegisterCallback<PointerUpEvent>(_ => _aboutModal.style.display = DisplayStyle.Flex);

            var githubLink = root.Q<Label>("AboutGithubLink");
            githubLink.pickingMode = PickingMode.Position;
            githubLink.RegisterCallback<PointerUpEvent>(_ => Application.OpenURL(GitHubUrl));

            root.Q<Button>("AboutCloseButton").clicked += () => _aboutModal.style.display = DisplayStyle.None;

            var heart = root.Q<Label>("HeartIcon");
            heart.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            _heartStartTime = Time.unscaledTime;
            _heartTick = root.schedule.Execute(() => AnimateHeart(heart)).Every(16);

            _currentPairing = PairingStore.LoadLastUsed();
            SetPaired(_currentPairing);
            RefreshRecentChips();
            RefreshWifiWarning();
            ConsumePendingScan();
        }

        /// Pulses like a heartbeat (a quick "lub" thump followed by a softer
        /// "dub") while slowly spinning around its own vertical axis. The
        /// spin is a horizontal-flip illusion (scaleX crossing zero) rather
        /// than a true 3D rotation, since this is a flat UI Toolkit label.
        void AnimateHeart(VisualElement heart)
        {
            var t = Time.unscaledTime - _heartStartTime;

            var cycle = (t % HeartBeatPeriod) / HeartBeatPeriod;
            var lub = GaussianPulse(cycle, 0.00f, 0.10f);
            var dub = GaussianPulse(cycle, 0.16f, 0.12f) * 0.7f;
            var beatScale = 1f + Mathf.Max(lub, dub) * 0.3f;

            var flip = Mathf.Cos(t / HeartFlipPeriod * Mathf.PI * 2f);

            heart.style.scale = new Scale(new Vector2(flip * beatScale, beatScale));
        }

        static float GaussianPulse(float cycle, float center, float width)
        {
            var d = (cycle - center) / width;
            return Mathf.Exp(-d * d * 4f);
        }

        void ConsumePendingScan()
        {
            if (!PendingScan.Result.HasValue)
                return;

            var pairing = PendingScan.Result.Value;
            PendingScan.Result = null;
            OpenModal(pairing);
        }

        public void Unmount()
        {
            _heartTick?.Pause();
            AbortProbe();
        }

        void OpenModal(PairingInfo? prefill)
        {
            _manualEntryModal.style.display = DisplayStyle.Flex;
            _manualEntryError.style.display = DisplayStyle.None;
            _root.Q("QrScanConfirmation").style.display = prefill.HasValue ? DisplayStyle.Flex : DisplayStyle.None;

            // Falls back to the last successfully-connected pairing on disk
            // (not just _currentPairing) so "Enter Manually" still shows the
            // last recorded values after Re-pair has cleared the in-memory
            // paired state.
            var lastRecorded = _currentPairing ?? PairingStore.LoadLastUsed();

            _root.Q<TextField>("IpField").value = prefill?.Ip ?? lastRecorded?.Ip ?? "";
            _root.Q<TextField>("PosePortField").value = (prefill?.PosePort ?? lastRecorded?.PosePort)?.ToString() ?? "";
            _root.Q<TextField>("VideoPortField").value = (prefill?.VideoPort ?? lastRecorded?.VideoPort)?.ToString() ?? "";
            _root.Q<TextField>("TokenField").value = prefill?.Token ?? lastRecorded?.Token ?? "";
        }

        void CloseModal()
        {
            _manualEntryModal.style.display = DisplayStyle.None;
            AbortProbe();
        }

        void OnConnectClicked()
        {
            var ip = _root.Q<TextField>("IpField").value;
            var posePortText = _root.Q<TextField>("PosePortField").value;
            var videoPortText = _root.Q<TextField>("VideoPortField").value;
            var token = _root.Q<TextField>("TokenField").value;

            if (!int.TryParse(posePortText, out var posePort) || !int.TryParse(videoPortText, out var videoPort))
            {
                ShowManualEntryError("Ports must be numbers 1-65535");
                return;
            }

            var pairing = new PairingInfo(ip, posePort, videoPort, token);
            if (!pairing.IsValid)
            {
                ShowManualEntryError("Fill in IP, ports and token");
                return;
            }

            StartConnectionProbe(pairing);
        }

        /// Actually attempts to reach the endpoint (a real TCP connect, not
        /// just "do the fields look well-formed") before ever showing
        /// "Connected" — a plain field-validity check let a wrong/unreachable
        /// IP report success, which was the whole bug.
        void StartConnectionProbe(PairingInfo pairing)
        {
            AbortProbe();
            _probeResolved = false;

            _connectButton.SetEnabled(false);
            _connectButton.text = "Connecting…";
            _manualEntryError.style.display = DisplayStyle.None;

            _probeChannel = new VideoCommandChannel(pairing.Ip, pairing.VideoPort, pairing.Token);
            _probeChannel.StateChanged += state =>
            {
                if (state == ChannelState.Connected)
                    OnProbeSucceeded(pairing);
            };
            _probeChannel.Error += _ => OnProbeFailed(pairing);
            _probeChannel.Start();

            _probeTickItem = _root.schedule.Execute(() => _probeChannel?.Pump()).Every(16);
            _probeTimeoutItem = _root.schedule.Execute(() => OnProbeFailed(pairing)).StartingIn(ProbeTimeoutMs);
        }

        void OnProbeSucceeded(PairingInfo pairing)
        {
            if (_probeResolved)
                return;
            _probeResolved = true;
            AbortProbe();

            CloseModal();
            PairingStore.SaveLastUsed(pairing);
            SetPaired(pairing);
            RefreshRecentChips();
        }

        void OnProbeFailed(PairingInfo pairing)
        {
            if (_probeResolved)
                return;
            _probeResolved = true;
            AbortProbe();

            ShowManualEntryError($"Could not reach {pairing.Ip}:{pairing.VideoPort} — check the details and try again");
        }

        /// Stops and discards any in-flight connection probe (a fresh one is
        /// created per attempt) and restores the Connect button.
        void AbortProbe()
        {
            _probeTickItem?.Pause();
            _probeTimeoutItem?.Pause();
            _probeChannel?.Dispose();
            _probeChannel = null;

            _connectButton.SetEnabled(true);
            _connectButton.text = "Connect";
        }

        void ShowManualEntryError(string message)
        {
            _manualEntryError.text = message;
            _manualEntryError.style.display = DisplayStyle.Flex;
        }

        void SetPaired(PairingInfo? pairing)
        {
            _currentPairing = pairing;
            var isPaired = pairing.HasValue;

            _unpairedGroup.style.display = isPaired ? DisplayStyle.None : DisplayStyle.Flex;
            _pairedGroup.style.display = isPaired ? DisplayStyle.Flex : DisplayStyle.None;

            _letsRecordButton.SetEnabled(isPaired);
            _letsRecordButton.EnableInClassList("button-primary", isPaired);
            _letsRecordButton.EnableInClassList("button-secondary", !isPaired);

            if (isPaired)
                _pairedSubline.text = $"pose:{pairing.Value.PosePort} video/cmd:{pairing.Value.VideoPort}";

            RefreshWifiWarning();
        }

        void RefreshRecentChips()
        {
            _recentChips.Clear();
            foreach (var pairing in PairingStore.LoadHistory())
            {
                var chip = new Button(() =>
                {
                    // Route through the same verified-connect path as manual
                    // entry — a stale recent pairing might not be reachable
                    // any more, and the modal is where that gets surfaced.
                    OpenModal(pairing);
                    StartConnectionProbe(pairing);
                })
                {
                    text = $"{pairing.Ip}:{pairing.VideoPort}"
                };
                chip.AddToClassList("chip");
                chip.AddToClassList("text-caption");
                chip.style.marginBottom = 8;
                _recentChips.Add(chip);
            }
        }

        void RefreshWifiWarning()
        {
            var unpairedVisible = _unpairedGroup.style.display == DisplayStyle.Flex;
            var onLan = Application.internetReachability != NetworkReachability.NotReachable;
            _wifiWarning.style.display = (unpairedVisible && !onLan) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        const long MinFreeBytes = 500L * 1024 * 1024;

        void OnLetsRecordClicked()
        {
            if (!_currentPairing.HasValue)
                return;

            if (HasEnoughStorage())
                _shell.Navigate(ScreenId.ArPreflight);
            else
                _shell.Navigate(ScreenId.StorageFull);
        }

        static bool HasEnoughStorage()
        {
            try
            {
                var path = Application.persistentDataPath;
                return new System.IO.DriveInfo(path.Substring(0, 1)).AvailableFreeSpace >= MinFreeBytes;
            }
            catch
            {
                // Platform without DriveInfo support (e.g. some Android configurations) —
                // don't block recording on a check we can't perform.
                return true;
            }
        }
    }
}
