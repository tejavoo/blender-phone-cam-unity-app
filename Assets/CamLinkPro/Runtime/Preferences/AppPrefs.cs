namespace CamLinkPro.Preferences
{
    /// Full preferences table (spec section 7). All PlayerPrefs-backed under
    /// "CamLinkPro.Prefs.*". Visibility toggles hide UI only — the function they
    /// control must remain reachable elsewhere (usually still adjustable from
    /// Settings itself).
    public static class AppPrefs
    {
        const string P = "CamLinkPro.Prefs.";

        public static readonly PrefBool DiagnosticsOverlayEnabled = new(P + nameof(DiagnosticsOverlayEnabled), false);
        public static readonly PrefBool KeepScreenAwake = new(P + nameof(KeepScreenAwake), true);
        public static readonly PrefBool ZoomSliderEnabled = new(P + nameof(ZoomSliderEnabled), false);
        public static readonly PrefFloat RecordCountdownSeconds = new(P + nameof(RecordCountdownSeconds), 2.0f);
        public static readonly PrefBool BigZoomSliderVisible = new(P + nameof(BigZoomSliderVisible), false);
        public static readonly PrefFloat ZoomSliderSensitivity = new(P + nameof(ZoomSliderSensitivity), 1.0f);
        public static readonly PrefBool StatusStripVisible = new(P + nameof(StatusStripVisible), true);
        public static readonly PrefBool RigPresetRowVisible = new(P + nameof(RigPresetRowVisible), true);
        public static readonly PrefBool FreezeAxisRowVisible = new(P + nameof(FreezeAxisRowVisible), true);
        public static readonly PrefBool BottomControlBarVisible = new(P + nameof(BottomControlBarVisible), true);
        public static readonly PrefBool RecordReadinessWarningVisible = new(P + nameof(RecordReadinessWarningVisible), true);
        public static readonly PrefBool TerminalReadoutVisible = new(P + nameof(TerminalReadoutVisible), true);
        public static readonly PrefFloat FontScale = new(P + nameof(FontScale), 1.0f);
        public static readonly PrefBool BoldTextEnabled = new(P + nameof(BoldTextEnabled), false);
        public static readonly PrefBool SafeAreaEnabled = new(P + nameof(SafeAreaEnabled), true);
        public static readonly PrefBool HudBackgroundEnabled = new(P + nameof(HudBackgroundEnabled), true);
        public static readonly PrefFloat HudBackgroundOpacity = new(P + nameof(HudBackgroundOpacity), 1.0f);

        // "release -> back to center" (true) vs "release -> leave where it is" (false).
        public static readonly PrefBool ZoomSliderSnapsToCenter = new(P + nameof(ZoomSliderSnapsToCenter), true);

        public static readonly PrefBool DiagnosticsHudVisible = new(P + nameof(DiagnosticsHudVisible), false);
        public static readonly PrefBool DiagShowPosition = new(P + nameof(DiagShowPosition), true);
        public static readonly PrefBool DiagShowRotation = new(P + nameof(DiagShowRotation), true);
        public static readonly PrefBool DiagShowTracking = new(P + nameof(DiagShowTracking), true);
        public static readonly PrefBool DiagShowPackets = new(P + nameof(DiagShowPackets), true);
        public static readonly PrefBool DiagShowArmed = new(P + nameof(DiagShowArmed), true);
        public static readonly PrefBool DiagShowCancelled = new(P + nameof(DiagShowCancelled), true);
        public static readonly PrefBool DiagShowRecordState = new(P + nameof(DiagShowRecordState), true);
    }
}
