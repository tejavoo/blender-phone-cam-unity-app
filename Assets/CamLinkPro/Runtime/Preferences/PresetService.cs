using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using CamLinkPro.Calibration;

namespace CamLinkPro.Preferences
{
    /// Snapshots/restores the whole Settings surface (Calibration, App,
    /// Camera, Diagnostics — everything except the Connection tab, which is
    /// pairing data rather than a "preference") as a single named preset.
    /// "Defaults" is captured once, from whatever the app's values happen to
    /// be the first time this runs, rather than hardcoded — so it reflects
    /// this build's shipped configuration even as that configuration evolves.
    public static class PresetService
    {
        const string KeyPrefix = "CamLinkPro.Prefs.Preset.";
        const string DefaultsKey = KeyPrefix + "Defaults";
        public const int CustomSlotCount = 3;

        static readonly CalibrationSettings Calibration = new();

        // One entry per setting exposed anywhere in the Settings screens
        // (Connection excluded — that's pairing data, not a preference).
        static IEnumerable<(string Key, Func<string> Get, Action<string> Set)> Fields()
        {
            yield return Bool("FontScale_Bold", AppPrefs.BoldTextEnabled);
            yield return Float("FontScale", AppPrefs.FontScale);

            yield return Bool("PositionFlipX", Calibration.PositionFlipX);
            yield return Bool("PositionFlipY", Calibration.PositionFlipY);
            yield return Bool("PositionFlipZ", Calibration.PositionFlipZ);
            yield return Bool("RotationFlipX", Calibration.RotationFlipX);
            yield return Bool("RotationFlipY", Calibration.RotationFlipY);
            yield return Bool("RotationFlipZ", Calibration.RotationFlipZ);
            yield return Bool("LevelHorizon", Calibration.LevelHorizon);
            yield return ("RotationRemap",
                () => Calibration.RotationRemap.ToString(),
                raw => { if (Enum.TryParse<RotationRemap>(raw, out var v)) Calibration.RotationRemap = v; });

            yield return Bool("KeepScreenAwake", AppPrefs.KeepScreenAwake);
            yield return Bool("ZoomSliderEnabled", AppPrefs.ZoomSliderEnabled);
            yield return Bool("SafeAreaEnabled", AppPrefs.SafeAreaEnabled);
            yield return Float("RecordCountdownSeconds", AppPrefs.RecordCountdownSeconds);
            yield return Bool("StatusStripVisible", AppPrefs.StatusStripVisible);
            yield return Bool("RigPresetRowVisible", AppPrefs.RigPresetRowVisible);
            yield return Bool("FreezeAxisRowVisible", AppPrefs.FreezeAxisRowVisible);
            yield return Bool("BottomControlBarVisible", AppPrefs.BottomControlBarVisible);
            yield return Bool("RecordReadinessWarningVisible", AppPrefs.RecordReadinessWarningVisible);
            yield return Bool("TerminalReadoutVisible", AppPrefs.TerminalReadoutVisible);

            yield return Bool("BigZoomSliderVisible", AppPrefs.BigZoomSliderVisible);
            yield return Float("ZoomSliderSensitivity", AppPrefs.ZoomSliderSensitivity);
            yield return Bool("ZoomSliderSnapsToCenter", AppPrefs.ZoomSliderSnapsToCenter);
            yield return Bool("HudBackgroundEnabled", AppPrefs.HudBackgroundEnabled);
            yield return Float("HudBackgroundOpacity", AppPrefs.HudBackgroundOpacity);
            yield return Bool("LiveMonitorVisible", AppPrefs.LiveMonitorVisible);
            yield return Float("LiveMonitorOpacity", AppPrefs.LiveMonitorOpacity);
            yield return Float("SteadyAmount", AppPrefs.SteadyAmount);

            yield return Bool("DiagnosticsHudVisible", AppPrefs.DiagnosticsHudVisible);
            yield return Bool("DiagShowPosition", AppPrefs.DiagShowPosition);
            yield return Bool("DiagShowRotation", AppPrefs.DiagShowRotation);
            yield return Bool("DiagShowTracking", AppPrefs.DiagShowTracking);
            yield return Bool("DiagShowPackets", AppPrefs.DiagShowPackets);
            yield return Bool("DiagShowCancelled", AppPrefs.DiagShowCancelled);
            yield return Bool("DiagShowRecordState", AppPrefs.DiagShowRecordState);
        }

        static (string, Func<string>, Action<string>) Bool(string key, PrefBool pref) =>
            (key, () => pref.Value ? "1" : "0", raw => pref.Value = raw == "1");

        static (string, Func<string>, Action<string>) Float(string key, PrefFloat pref) =>
            (key, () => pref.Value.ToString(CultureInfo.InvariantCulture),
                raw => { if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) pref.Value = v; });

        public static void CaptureDefaultsIfMissing()
        {
            if (!PlayerPrefs.HasKey(DefaultsKey))
                Save(DefaultsKey);
        }

        public static void RestoreDefaults() => Load(DefaultsKey);

        public static bool HasCustomSlot(int slot) => PlayerPrefs.HasKey(SlotKey(slot));

        public static string GetSlotName(int slot) => PlayerPrefs.GetString(SlotNameKey(slot), $"Preset {slot + 1}");

        public static void SetSlotName(int slot, string name)
        {
            PlayerPrefs.SetString(SlotNameKey(slot), name);
            PlayerPrefs.Save();
        }

        public static void SaveToSlot(int slot) => Save(SlotKey(slot));

        public static void LoadFromSlot(int slot) => Load(SlotKey(slot));

        static string SlotKey(int slot) => KeyPrefix + "Custom" + slot;
        static string SlotNameKey(int slot) => KeyPrefix + "CustomName" + slot;

        static void Save(string key)
        {
            var sb = new StringBuilder();
            foreach (var (fieldKey, get, _) in Fields())
                sb.Append(fieldKey).Append('=').Append(get()).Append(';');
            PlayerPrefs.SetString(key, sb.ToString());
            PlayerPrefs.Save();
        }

        static void Load(string key)
        {
            if (!PlayerPrefs.HasKey(key))
                return;

            var map = PlayerPrefs.GetString(key, "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);

            foreach (var (fieldKey, _, set) in Fields())
                if (map.TryGetValue(fieldKey, out var raw))
                    set(raw);
        }
    }
}
