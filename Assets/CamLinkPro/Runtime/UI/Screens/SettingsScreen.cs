using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using CamLinkPro.Calibration;
using CamLinkPro.Networking;
using CamLinkPro.Preferences;

namespace CamLinkPro.UI.Screens
{
    public class SettingsScreen : IScreenController
    {
        static readonly CalibrationSettings Calibration = new();

        VisualElement _root;
        AppShell _shell;
        readonly Dictionary<string, VisualElement> _panels = new();
        readonly Dictionary<string, Button> _tabs = new();

        public void Mount(VisualElement root, AppShell shell)
        {
            _root = root;
            _shell = shell;

            root.Q<Button>("BackButton").clicked += () => _shell.Navigate(_shell.SettingsReturnTo);

            _panels["General"] = root.Q("PanelGeneral");
            _panels["Calibration"] = root.Q("PanelCalibration");
            _panels["App"] = root.Q("PanelApp");
            _panels["Connection"] = root.Q("PanelConnection");
            _panels["Camera"] = root.Q("PanelCamera");
            _panels["Diagnostics"] = root.Q("PanelDiagnostics");

            _tabs["General"] = root.Q<Button>("TabGeneral");
            _tabs["Calibration"] = root.Q<Button>("TabCalibration");
            _tabs["App"] = root.Q<Button>("TabApp");
            _tabs["Connection"] = root.Q<Button>("TabConnection");
            _tabs["Camera"] = root.Q<Button>("TabCamera");
            _tabs["Diagnostics"] = root.Q<Button>("TabDiagnostics");

            foreach (var (name, button) in _tabs)
                button.clicked += () => ShowTab(name);
            ShowTab("General");

            WireGeneralTab();
            WireCalibrationTab();
            WireAppTab();
            WireConnectionTab();
            WireCameraTab();
            WireDiagnosticsTab();
        }

        public void Unmount() { }

        void ShowTab(string name)
        {
            foreach (var (key, panel) in _panels)
                panel.style.display = key == name ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var (key, button) in _tabs)
                button.EnableInClassList("chip--selected", key == name);
        }

        void WireGeneralTab()
        {
            var slider = _root.Q<Slider>("TextSizeSlider");
            slider.value = AppPrefs.FontScale.Value;
            slider.RegisterValueChangedCallback(evt =>
            {
                AppPrefs.FontScale.Value = evt.newValue;
                _shell.ReapplyTheme();
            });

            var boldToggle = _root.Q<Toggle>("BoldTextToggle");
            boldToggle.value = AppPrefs.BoldTextEnabled.Value;
            boldToggle.RegisterValueChangedCallback(evt =>
            {
                AppPrefs.BoldTextEnabled.Value = evt.newValue;
                _shell.ReapplyTheme();
            });
        }

        void WireCalibrationTab()
        {
            BindToggle("PositionFlipXToggle", Calibration.PositionFlipX);
            BindToggle("PositionFlipYToggle", Calibration.PositionFlipY);
            BindToggle("PositionFlipZToggle", Calibration.PositionFlipZ);
            BindToggle("RotationFlipXToggle", Calibration.RotationFlipX);
            BindToggle("RotationFlipYToggle", Calibration.RotationFlipY);
            BindToggle("RotationFlipZToggle", Calibration.RotationFlipZ);
            BindToggle("LevelHorizonToggle", Calibration.LevelHorizon);

            var remapDropdown = _root.Q<DropdownField>("RotationRemapDropdown");
            remapDropdown.choices = System.Enum.GetNames(typeof(RotationRemap)).ToList();
            remapDropdown.value = Calibration.RotationRemap.ToString();
            remapDropdown.RegisterValueChangedCallback(evt =>
                Calibration.RotationRemap = System.Enum.Parse<RotationRemap>(evt.newValue));

            RefreshRopRorReadouts();
            _root.Q<Button>("RezeroPositionButton").clicked += () =>
            {
                Calibration.ClearPositionZero();
                RefreshRopRorReadouts();
            };
            _root.Q<Button>("RezeroRotationButton").clicked += () =>
            {
                Calibration.ClearRotationZero();
                RefreshRopRorReadouts();
            };
        }

        void RefreshRopRorReadouts()
        {
            var pos = Calibration.PositionOffset;
            _root.Q<Label>("RopX").text = pos.x.ToString("F3");
            _root.Q<Label>("RopY").text = pos.y.ToString("F3");
            _root.Q<Label>("RopZ").text = pos.z.ToString("F3");

            var rot = Calibration.RotationOffsetDeg;
            _root.Q<Label>("RorTilt").text = rot.x.ToString("F3");
            _root.Q<Label>("RorRoll").text = rot.y.ToString("F3");
            _root.Q<Label>("RorPan").text = rot.z.ToString("F3");
        }

        void WireAppTab()
        {
            BindToggle("KeepScreenAwakeToggle", AppPrefs.KeepScreenAwake);
            BindToggle("ZoomSliderToggle", AppPrefs.ZoomSliderEnabled);

            var safeAreaToggle = _root.Q<Toggle>("SafeAreaToggle");
            safeAreaToggle.value = AppPrefs.SafeAreaEnabled.Value;
            safeAreaToggle.RegisterValueChangedCallback(evt =>
            {
                AppPrefs.SafeAreaEnabled.Value = evt.newValue;
                _shell.ReapplySafeArea();
            });
            BindToggle("StatusStripVisibleToggle", AppPrefs.StatusStripVisible);
            BindToggle("RigPresetRowVisibleToggle", AppPrefs.RigPresetRowVisible);
            BindToggle("FreezeAxisRowVisibleToggle", AppPrefs.FreezeAxisRowVisible);
            BindToggle("BottomControlBarVisibleToggle", AppPrefs.BottomControlBarVisible);
            BindToggle("RecordReadinessWarningVisibleToggle", AppPrefs.RecordReadinessWarningVisible);
            BindToggle("TerminalReadoutVisibleToggle", AppPrefs.TerminalReadoutVisible);

            var delayField = _root.Q<TextField>("RecordDelayField");
            delayField.value = AppPrefs.RecordCountdownSeconds.Value.ToString("F1");
            delayField.RegisterValueChangedCallback(evt =>
            {
                if (float.TryParse(evt.newValue, out var seconds))
                    AppPrefs.RecordCountdownSeconds.Value = UnityEngine.Mathf.Clamp(seconds, 0f, 10f);
            });
        }

        void WireConnectionTab()
        {
            var pairing = PairingStore.LoadLastUsed();
            _root.Q<TextField>("ConnIpField").value = pairing?.Ip ?? "";
            _root.Q<TextField>("ConnPosePortField").value = pairing?.PosePort.ToString() ?? "";
            _root.Q<TextField>("ConnVideoPortField").value = pairing?.VideoPort.ToString() ?? "";
            _root.Q<TextField>("ConnTokenField").value = pairing?.Token ?? "";

            var error = _root.Q<Label>("ConnError");

            _root.Q<Button>("ReconnectButton").clicked += () =>
            {
                var ip = _root.Q<TextField>("ConnIpField").value;
                var token = _root.Q<TextField>("ConnTokenField").value;
                if (!int.TryParse(_root.Q<TextField>("ConnPosePortField").value, out var pose) ||
                    !int.TryParse(_root.Q<TextField>("ConnVideoPortField").value, out var video))
                {
                    error.text = "Ports must be numbers 1-65535";
                    error.style.display = DisplayStyle.Flex;
                    return;
                }

                var newPairing = new PairingInfo(ip, pose, video, token);
                if (!newPairing.IsValid)
                {
                    error.text = "Fill in IP, ports and token";
                    error.style.display = DisplayStyle.Flex;
                    return;
                }

                error.style.display = DisplayStyle.None;
                PairingStore.SaveLastUsed(newPairing);
            };

            _root.Q<Button>("UnpairButton").clicked += () =>
            {
                // Confirmation modal is a follow-up; unpair immediately for now.
                UnityEngine.PlayerPrefs.DeleteKey("CamLinkPro.Pairing.Ip");
                UnityEngine.PlayerPrefs.Save();
                _root.Q<TextField>("ConnIpField").value = "";
                _root.Q<TextField>("ConnTokenField").value = "";
            };
        }

        void WireCameraTab()
        {
            BindToggle("ZoomSliderVisibleToggle", AppPrefs.BigZoomSliderVisible);

            var sensitivityField = _root.Q<TextField>("ZoomSensitivityField");
            sensitivityField.value = AppPrefs.ZoomSliderSensitivity.Value.ToString("F2");
            sensitivityField.RegisterValueChangedCallback(evt =>
            {
                if (float.TryParse(evt.newValue, out var v))
                    AppPrefs.ZoomSliderSensitivity.Value = UnityEngine.Mathf.Clamp(v, 0.25f, 4f);
            });

            _root.Q<Label>("SensorWidthReadout").text = ZoomState.SensorWidthMm.ToString("F1");

            var releaseToggle = _root.Q<Toggle>("ZoomSliderReleaseToggle");
            releaseToggle.value = AppPrefs.ZoomSliderSnapsToCenter.Value;
            UpdateZoomReleaseLabel(releaseToggle);
            releaseToggle.RegisterValueChangedCallback(evt =>
            {
                AppPrefs.ZoomSliderSnapsToCenter.Value = evt.newValue;
                UpdateZoomReleaseLabel(releaseToggle);
            });

            BindToggle("HudBackgroundToggle", AppPrefs.HudBackgroundEnabled);
            var opacitySlider = _root.Q<Slider>("HudBackgroundOpacitySlider");
            var opacityValue = _root.Q<Label>("HudBackgroundOpacityValue");
            opacitySlider.value = AppPrefs.HudBackgroundOpacity.Value;
            opacityValue.text = opacitySlider.value.ToString("F1");
            opacitySlider.RegisterValueChangedCallback(evt =>
            {
                AppPrefs.HudBackgroundOpacity.Value = evt.newValue;
                opacityValue.text = evt.newValue.ToString("F1");
            });
        }

        static void UpdateZoomReleaseLabel(Toggle toggle) =>
            toggle.label = toggle.value ? "Release: Back to Center" : "Release: Leave Where It Is";

        void WireDiagnosticsTab()
        {
            BindToggle("DiagVisibleToggle", AppPrefs.DiagnosticsHudVisible);
            BindToggle("DiagShowPositionToggle", AppPrefs.DiagShowPosition);
            BindToggle("DiagShowRotationToggle", AppPrefs.DiagShowRotation);
            BindToggle("DiagShowTrackingToggle", AppPrefs.DiagShowTracking);
            BindToggle("DiagShowPacketsToggle", AppPrefs.DiagShowPackets);
            BindToggle("DiagShowArmedToggle", AppPrefs.DiagShowArmed);
            BindToggle("DiagShowCancelledToggle", AppPrefs.DiagShowCancelled);
            BindToggle("DiagShowRecordStateToggle", AppPrefs.DiagShowRecordState);
        }

        void BindToggle(string name, PrefBool pref)
        {
            var toggle = _root.Q<Toggle>(name);
            toggle.value = pref.Value;
            toggle.RegisterValueChangedCallback(evt => pref.Value = evt.newValue);
        }
    }
}
