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
        Label _manualEntryError;
        VisualElement _pairedStatusDot;
        Label _pairedHeadline;
        Label _pairedSubline;
        Button _letsRecordButton;

        PairingInfo? _currentPairing;

        Button _connectButton;
        VideoCommandChannel _probeChannel;
        IVisualElementScheduledItem _probeTickItem;
        IVisualElementScheduledItem _probeTimeoutItem;
        bool _probeResolved;
        const int ProbeTimeoutMs = 4000;

        // Kept alive for as long as Landing shows the Paired card, purely to
        // know whether "Connected" is still true -- the one-shot probe
        // channel used to prove a pairing works is disposed the instant it
        // succeeds, so without this the pill would freeze on "Connected"
        // forever even after Blender disconnects. Reuses the same
        // PING/PONG-driven liveness + auto-reconnect VideoCommandChannel
        // already has, just for status display rather than commands.
        VideoCommandChannel _liveChannel;
        IVisualElementScheduledItem _liveTickItem;

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
            _manualEntryError = root.Q<Label>("ManualEntryError");
            _pairedStatusDot = root.Q("PairedStatusDot");
            _pairedHeadline = root.Q<Label>("PairedHeadline");
            _pairedSubline = root.Q<Label>("PairedSubline");
            _letsRecordButton = root.Q<Button>("LetsRecordButton");

            _connectButton = root.Q<Button>("ConnectButton");

            root.Q<Button>("ScanQrButton").clicked += () => _shell.Navigate(ScreenId.ScanQr);
            root.Q<Button>("EnterManuallyLink").clicked += () => OpenModal(prefill: null);
            root.Q<Button>("ModalCancelButton").clicked += CloseModal;
            _connectButton.clicked += OnConnectClicked;
            root.Q<Button>("RepairLink").clicked += () => SetPaired(null);
            root.Q<Button>("UnpairLink").clicked += OnUnpairClicked;
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
            if (_currentPairing.HasValue)
                StartLiveMonitor(_currentPairing.Value);
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
            StopLiveMonitor();
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
            // Deliberately NOT keyed off ChannelState.Connected: that fires
            // the instant the raw TCP handshake completes, before the AUTH
            // line is even sent, so a wrong token would still read as
            // "success" right up until the server got around to dropping
            // it. The addon only ever sends a line (STATE, on a client
            // successfully joining) to a client whose AUTH it accepted, so
            // waiting for any status line is what actually proves the
            // pairing -- not just that the port was open.
            _probeChannel.StatusLineReceived += _ => OnProbeSucceeded(pairing);
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

            // Not AbortProbe(): that disposes _probeChannel, but this exact
            // already-connected-and-AUTH'd channel is what we want to keep
            // running as the live monitor, not throw away and reconnect.
            _probeTickItem?.Pause();
            _probeTickItem = null;
            _probeTimeoutItem?.Pause();
            _probeTimeoutItem = null;
            _connectButton.SetEnabled(true);
            _connectButton.text = "Connect";
            _manualEntryModal.style.display = DisplayStyle.None;

            PairingStore.SaveLastUsed(pairing);
            StartLiveMonitor(pairing, reuse: _probeChannel);
            _probeChannel = null;
            // The reused channel's own Connected transition already fired
            // (and was missed) before OnLiveChannelStateChanged was wired up
            // above -- force the pill in sync now rather than wait for its
            // next state change, which may be a while if nothing changes.
            OnLiveChannelStateChanged(ChannelState.Connected);

            SetPaired(pairing);
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

        /// Unlike Re-pair (SetPaired(null) alone, which just drops back to
        /// the Unpaired screen while leaving the saved values in place so
        /// "Enter Manually" still offers them), Unpair also wipes the saved
        /// pairing itself -- for a pairing that's gone stale/wrong and
        /// shouldn't keep being offered as the default.
        void OnUnpairClicked()
        {
            PairingStore.Clear();
            SetPaired(null);
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
            {
                _pairedSubline.text = $"pose:{pairing.Value.PosePort} video/cmd:{pairing.Value.VideoPort}";
            }
            else
            {
                // Re-pair/Unpair dropping back to the Unpaired screen, or a
                // failed re-verify on app start -- either way there's no
                // "Connected" card left to keep a live status for.
                StopLiveMonitor();
            }

            RefreshWifiWarning();
        }

        /// Starts (or takes over) the persistent status channel backing the
        /// Paired card's "Connected"/"Reconnecting…" pill. Pass an
        /// already-running channel via `reuse` (the just-succeeded probe) to
        /// avoid a pointless disconnect/reconnect right after proving the
        /// pairing works.
        void StartLiveMonitor(PairingInfo pairing, VideoCommandChannel reuse = null)
        {
            StopLiveMonitor();

            _liveChannel = reuse ?? new VideoCommandChannel(pairing.Ip, pairing.VideoPort, pairing.Token);
            _liveChannel.StateChanged += OnLiveChannelStateChanged;
            _liveTickItem = _root.schedule.Execute(() => _liveChannel?.Pump()).Every(16);

            if (reuse == null)
                _liveChannel.Start();
        }

        void StopLiveMonitor()
        {
            _liveTickItem?.Pause();
            _liveTickItem = null;

            if (_liveChannel == null)
                return;
            _liveChannel.StateChanged -= OnLiveChannelStateChanged;
            _liveChannel.Dispose();
            _liveChannel = null;
        }

        /// The channel's own state briefly reads Connected right after the
        /// raw TCP handshake, before AUTH is even evaluated (see the comment
        /// on StartConnectionProbe) -- so on a reconnect after e.g. a token
        /// change, this pill can flash green for a moment before the server
        /// drops it and the channel's own retry loop takes over. Cosmetic
        /// only: it always settles on the right state within a few seconds,
        /// same as the retry/PING-timeout logic already backing it.
        void OnLiveChannelStateChanged(ChannelState state)
        {
            var connected = state == ChannelState.Connected;
            _pairedStatusDot.RemoveFromClassList(connected ? "status-dot--lost" : "status-dot--good");
            _pairedStatusDot.AddToClassList(connected ? "status-dot--good" : "status-dot--lost");
            _pairedHeadline.text = connected ? "Connected" : "Reconnecting…";
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
