using UnityEngine;
using UnityEngine.UIElements;

namespace CamLinkPro.UI.Screens
{
    public class ArPreflightScreen : IScreenController
    {
        const int TimeoutMs = 4000;
        const int PollIntervalMs = 100;

        VisualElement _root;
        AppShell _shell;
        IVisualElementScheduledItem _pollItem;
        IVisualElementScheduledItem _timeoutItem;
        IVisualElementScheduledItem _loadingTickItem;
        bool _advanced;

        VisualElement _lensInner;
        VisualElement _barTrack;
        VisualElement _barFill;
        float _startTime;

        public void Mount(VisualElement root, AppShell shell)
        {
            _root = root;
            _shell = shell;
            _advanced = false;

            shell.ArSession.EnableSession();

            var batteryRow = root.Q("BatteryRow");
            if (UnityEngine.SystemInfo.batteryStatus != UnityEngine.BatteryStatus.Unknown)
                batteryRow.style.display = DisplayStyle.Flex;

            _lensInner = root.Q("CameraLensInner");
            _lensInner.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            _barTrack = root.Q("LoadingBarTrack");
            _barFill = root.Q("LoadingBarFill");
            _startTime = Time.unscaledTime;
            _loadingTickItem = root.schedule.Execute(AnimateLoading).Every(16);

            _pollItem = root.schedule.Execute(CheckTracking).Every(PollIntervalMs);
            _timeoutItem = root.schedule.Execute(() => Advance()).StartingIn(TimeoutMs);
        }

        /// A camera "shutter" breathing pulse on the lens, plus the loading
        /// bar's fill sliding back and forth — both driven by simple sine
        /// waves since there's no real progress fraction to report (AR
        /// tracking either becomes reliable or the fixed timeout elapses).
        void AnimateLoading()
        {
            var t = Time.unscaledTime - _startTime;

            var pulse = 0.75f + Mathf.Abs(Mathf.Sin(t * 3f)) * 0.5f;
            _lensInner.style.scale = new Scale(new Vector2(pulse, pulse));

            var trackWidth = _barTrack.resolvedStyle.width;
            var fillWidth = _barFill.resolvedStyle.width;
            if (trackWidth > 0f && !float.IsNaN(trackWidth))
            {
                var range = Mathf.Max(0f, trackWidth - fillWidth);
                var ping = (Mathf.Sin(t * 1.8f) + 1f) * 0.5f;
                _barFill.style.left = ping * range;
            }
        }

        void CheckTracking()
        {
            if (!_shell.ArSession.TrackingReliable)
                return;

            var icon = _root.Q<Label>("TrackingIcon");
            icon.text = "✓";
            icon.style.color = new StyleColor(new UnityEngine.Color(61 / 255f, 214 / 255f, 140 / 255f));
            Advance();
        }

        void Advance()
        {
            if (_advanced)
                return;
            _advanced = true;
            _shell.Navigate(ScreenId.RecordingHud);
        }

        public void Unmount()
        {
            _pollItem?.Pause();
            _timeoutItem?.Pause();
            _loadingTickItem?.Pause();
        }
    }
}
