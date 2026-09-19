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
        VisualElement _recentChips;
        Label _manualEntryError;
        Label _pairedSubline;
        Button _letsRecordButton;

        PairingInfo? _currentPairing;

        public void Mount(VisualElement root, AppShell shell)
        {
            _root = root;
            _shell = shell;

            _wifiWarning = root.Q("WifiWarning");
            _unpairedGroup = root.Q("UnpairedGroup");
            _pairedGroup = root.Q("PairedGroup");
            _manualEntryModal = root.Q("ManualEntryModal");
            _recentChips = root.Q("RecentChips");
            _manualEntryError = root.Q<Label>("ManualEntryError");
            _pairedSubline = root.Q<Label>("PairedSubline");
            _letsRecordButton = root.Q<Button>("LetsRecordButton");

            root.Q<Button>("ScanQrButton").clicked += () => _shell.Navigate(ScreenId.ScanQr);
            root.Q<Button>("EnterManuallyLink").clicked += () => OpenModal(prefill: null);
            root.Q<Button>("ModalCancelButton").clicked += CloseModal;
            root.Q<Button>("ConnectButton").clicked += OnConnectClicked;
            root.Q<Button>("RepairLink").clicked += () => SetPaired(null);
            root.Q<Button>("SettingsButton").clicked += () =>
            {
                _shell.SettingsReturnTo = ScreenId.Landing;
                _shell.Navigate(ScreenId.SettingsGeneral);
            };
            _letsRecordButton.clicked += OnLetsRecordClicked;

            _currentPairing = PairingStore.LoadLastUsed();
            SetPaired(_currentPairing);
            RefreshRecentChips();
            RefreshWifiWarning();
            ConsumePendingScan();
        }

        void ConsumePendingScan()
        {
            if (!PendingScan.Result.HasValue)
                return;

            var pairing = PendingScan.Result.Value;
            PendingScan.Result = null;
            OpenModal(pairing);
        }

        public void Unmount() { }

        void OpenModal(PairingInfo? prefill)
        {
            _manualEntryModal.style.display = DisplayStyle.Flex;
            _manualEntryError.style.display = DisplayStyle.None;
            _root.Q("QrScanConfirmation").style.display = prefill.HasValue ? DisplayStyle.Flex : DisplayStyle.None;

            _root.Q<TextField>("IpField").value = prefill?.Ip ?? _currentPairing?.Ip ?? "";
            _root.Q<TextField>("PosePortField").value = (prefill?.PosePort ?? _currentPairing?.PosePort)?.ToString() ?? "";
            _root.Q<TextField>("VideoPortField").value = (prefill?.VideoPort ?? _currentPairing?.VideoPort)?.ToString() ?? "";
            _root.Q<TextField>("TokenField").value = prefill?.Token ?? _currentPairing?.Token ?? "";
        }

        void CloseModal() => _manualEntryModal.style.display = DisplayStyle.None;

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

            CloseModal();
            PairingStore.SaveLastUsed(pairing);
            SetPaired(pairing);
            RefreshRecentChips();
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
                var chip = new Button(() => { PairingStore.SaveLastUsed(pairing); SetPaired(pairing); })
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
