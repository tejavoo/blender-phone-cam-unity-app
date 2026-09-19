using UnityEngine;

namespace CamLinkPro.App
{
    /// <summary>Small standalone app-level preferences that don't belong with the
    /// pose calibration (<see cref="Pipeline.OutputCalibration"/>) or pairing info
    /// (<see cref="Networking.PairingInfoStore"/>) -- things the Settings screen
    /// exposes as simple on/off toggles. Persisted to PlayerPrefs, loaded once at
    /// startup and cached, so reading them every frame (e.g. from Update()) is
    /// cheap.</summary>
    public static class AppPreferences
    {
        const string DiagnosticsKey = "CamLinkPro.Prefs.DiagnosticsOverlay";
        const string KeepAwakeKey = "CamLinkPro.Prefs.KeepScreenAwake";
        const string ZoomSliderKey = "CamLinkPro.Prefs.ZoomSlider";
        const string RecordCountdownKey = "CamLinkPro.Prefs.RecordCountdownSeconds";
        const string BigZoomSliderVisibleKey = "CamLinkPro.Prefs.BigZoomSliderVisible";
        const string ZoomSliderSensitivityKey = "CamLinkPro.Prefs.ZoomSliderSensitivity";
        const string StatusStripVisibleKey = "CamLinkPro.Prefs.StatusStripVisible";
        const string RigPresetRowVisibleKey = "CamLinkPro.Prefs.RigPresetRowVisible";
        const string FreezeAxisRowVisibleKey = "CamLinkPro.Prefs.FreezeAxisRowVisible";
        const string BottomControlBarVisibleKey = "CamLinkPro.Prefs.BottomControlBarVisible";
        const string RecordReadinessWarningVisibleKey = "CamLinkPro.Prefs.RecordReadinessWarningVisible";
        const string TerminalReadoutVisibleKey = "CamLinkPro.Prefs.TerminalReadoutVisible";
        const string FontScaleKey = "CamLinkPro.Prefs.FontScale";

        public const float MinRecordCountdownSeconds = 0f;
        public const float MaxRecordCountdownSeconds = 10f;
        const float DefaultRecordCountdownSeconds = 2f;

        public const float MinZoomSliderSensitivity = 0.25f;
        public const float MaxZoomSliderSensitivity = 4f;
        const float DefaultZoomSliderSensitivity = 1f;

        public const float MinFontScale = 0.85f;
        public const float MaxFontScale = 2f;
        const float DefaultFontScale = 1f;

        static bool? diagnosticsOverlayEnabled;
        static bool? keepScreenAwake;
        static bool? zoomSliderEnabled;
        static float? recordCountdownSeconds;
        static bool? bigZoomSliderVisible;
        static float? zoomSliderSensitivity;
        static bool? statusStripVisible;
        static bool? rigPresetRowVisible;
        static bool? freezeAxisRowVisible;
        static bool? bottomControlBarVisible;
        static bool? recordReadinessWarningVisible;
        static bool? terminalReadoutVisible;
        static float? fontScale;

        /// <summary>Verbose pose/XR-subsystem diagnostics to logcat -- off by
        /// default (it's genuinely noisy), but this is exactly what pinned down
        /// the ARInputManager bug, so it stays available as an opt-in for the
        /// next time something like that needs diagnosing without a rebuild
        /// treasure hunt.</summary>
        public static bool DiagnosticsOverlayEnabled
        {
            get
            {
                diagnosticsOverlayEnabled ??= PlayerPrefs.GetInt(DiagnosticsKey, 0) != 0;
                return diagnosticsOverlayEnabled.Value;
            }
            set
            {
                diagnosticsOverlayEnabled = value;
                PlayerPrefs.SetInt(DiagnosticsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Keeps the display on (no auto-dim/lock) while the app is in
        /// the foreground -- on by default, since a session dying mid-shot
        /// because the screen locked is a much worse failure than a bit of extra
        /// battery drain.</summary>
        public static bool KeepScreenAwake
        {
            get
            {
                keepScreenAwake ??= PlayerPrefs.GetInt(KeepAwakeKey, 1) != 0;
                return keepScreenAwake.Value;
            }
            set
            {
                keepScreenAwake = value;
                PlayerPrefs.SetInt(KeepAwakeKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        /// <summary>Shows a slider for optical zoom on the Recording HUD instead
        /// of the default +/- repeat buttons. Off by default -- the +/- buttons
        /// are what's already built and tested; this is an opt-in alternative
        /// for finer control.</summary>
        public static bool ZoomSliderEnabled
        {
            get
            {
                zoomSliderEnabled ??= PlayerPrefs.GetInt(ZoomSliderKey, 0) != 0;
                return zoomSliderEnabled.Value;
            }
            set
            {
                zoomSliderEnabled = value;
                PlayerPrefs.SetInt(ZoomSliderKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>How long the on-screen countdown runs after pressing Record
        /// before START is actually sent to Blender -- 2 seconds by default,
        /// customizable in Settings for setups that need more (or less) time to
        /// get out of frame or settle the shot.</summary>
        public static float RecordCountdownSeconds
        {
            get
            {
                recordCountdownSeconds ??= Mathf.Clamp(
                    PlayerPrefs.GetFloat(RecordCountdownKey, DefaultRecordCountdownSeconds),
                    MinRecordCountdownSeconds, MaxRecordCountdownSeconds);
                return recordCountdownSeconds.Value;
            }
            set
            {
                float clamped = Mathf.Clamp(value, MinRecordCountdownSeconds, MaxRecordCountdownSeconds);
                recordCountdownSeconds = clamped;
                PlayerPrefs.SetFloat(RecordCountdownKey, clamped);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Whether the big zoom rocker slider (beside the Live/Record
        /// panel on the Recording HUD) is shown at all -- off by default, same
        /// convention as every other optional HUD element in this app.</summary>
        public static bool BigZoomSliderVisible
        {
            get
            {
                bigZoomSliderVisible ??= PlayerPrefs.GetInt(BigZoomSliderVisibleKey, 0) != 0;
                return bigZoomSliderVisible.Value;
            }
            set
            {
                bigZoomSliderVisible = value;
                PlayerPrefs.SetInt(BigZoomSliderVisibleKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Multiplier on how fast the big zoom rocker changes focal
        /// length for a given push distance from its centered rest position.</summary>
        public static float ZoomSliderSensitivity
        {
            get
            {
                zoomSliderSensitivity ??= Mathf.Clamp(
                    PlayerPrefs.GetFloat(ZoomSliderSensitivityKey, DefaultZoomSliderSensitivity),
                    MinZoomSliderSensitivity, MaxZoomSliderSensitivity);
                return zoomSliderSensitivity.Value;
            }
            set
            {
                float clamped = Mathf.Clamp(value, MinZoomSliderSensitivity, MaxZoomSliderSensitivity);
                zoomSliderSensitivity = clamped;
                PlayerPrefs.SetFloat(ZoomSliderSensitivityKey, clamped);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Per-element HUD visibility toggles (Settings -> App -> HUD
        /// Visibility) -- each hides only the on-screen element, never the
        /// underlying function, so turning one off can't strand a user (e.g.
        /// freeze toggles still work from Settings even with the row hidden).
        /// All default on except the zoom rocker (<see cref="BigZoomSliderVisible"/>,
        /// already off by default above) -- this is purely decluttering, not a
        /// safety-relevant default.</summary>
        public static bool StatusStripVisible
        {
            get { statusStripVisible ??= PlayerPrefs.GetInt(StatusStripVisibleKey, 1) != 0; return statusStripVisible.Value; }
            set { statusStripVisible = value; PlayerPrefs.SetInt(StatusStripVisibleKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool RigPresetRowVisible
        {
            get { rigPresetRowVisible ??= PlayerPrefs.GetInt(RigPresetRowVisibleKey, 1) != 0; return rigPresetRowVisible.Value; }
            set { rigPresetRowVisible = value; PlayerPrefs.SetInt(RigPresetRowVisibleKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool FreezeAxisRowVisible
        {
            get { freezeAxisRowVisible ??= PlayerPrefs.GetInt(FreezeAxisRowVisibleKey, 1) != 0; return freezeAxisRowVisible.Value; }
            set { freezeAxisRowVisible = value; PlayerPrefs.SetInt(FreezeAxisRowVisibleKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool BottomControlBarVisible
        {
            get { bottomControlBarVisible ??= PlayerPrefs.GetInt(BottomControlBarVisibleKey, 1) != 0; return bottomControlBarVisible.Value; }
            set { bottomControlBarVisible = value; PlayerPrefs.SetInt(BottomControlBarVisibleKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool RecordReadinessWarningVisible
        {
            get { recordReadinessWarningVisible ??= PlayerPrefs.GetInt(RecordReadinessWarningVisibleKey, 1) != 0; return recordReadinessWarningVisible.Value; }
            set { recordReadinessWarningVisible = value; PlayerPrefs.SetInt(RecordReadinessWarningVisibleKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The Recording HUD's live "terminal" readout (link
        /// latency, in the green monospace box) -- on by default, same
        /// hide-the-element-only convention as every other HUD Visibility
        /// toggle.</summary>
        public static bool TerminalReadoutVisible
        {
            get { terminalReadoutVisible ??= PlayerPrefs.GetInt(TerminalReadoutVisibleKey, 1) != 0; return terminalReadoutVisible.Value; }
            set { terminalReadoutVisible = value; PlayerPrefs.SetInt(TerminalReadoutVisibleKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>App-wide text size multiplier (Settings -> General) --
        /// 1.0 is the original design size. Applied to every label built via
        /// HudController.CreateLabel (which covers button/toggle captions
        /// too, since those are built from it) through a live registry, so
        /// changing it updates already-visible text immediately instead of
        /// only affecting screens built after the change.</summary>
        public static float FontScale
        {
            get
            {
                fontScale ??= Mathf.Clamp(PlayerPrefs.GetFloat(FontScaleKey, DefaultFontScale), MinFontScale, MaxFontScale);
                return fontScale.Value;
            }
            set
            {
                float clamped = Mathf.Clamp(value, MinFontScale, MaxFontScale);
                fontScale = clamped;
                PlayerPrefs.SetFloat(FontScaleKey, clamped);
                PlayerPrefs.Save();
            }
        }
    }
}
