using UnityEngine;

namespace CamLinkPro.UI
{
    /// Thin wrapper so haptic calls read as an intentional cue at each call
    /// site (record/stop/cancel) instead of a bare Handheld.Vibrate() with no
    /// context. Android-only, matching the rest of the app's platform scope.
    public static class HapticFeedback
    {
        public static void Trigger()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }
}
