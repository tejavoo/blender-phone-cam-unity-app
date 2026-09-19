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
        bool _advanced;

        public void Mount(VisualElement root, AppShell shell)
        {
            _root = root;
            _shell = shell;
            _advanced = false;

            shell.ArSession.EnableSession();

            var batteryRow = root.Q("BatteryRow");
            if (UnityEngine.SystemInfo.batteryStatus != UnityEngine.BatteryStatus.Unknown)
                batteryRow.style.display = DisplayStyle.Flex;

            _pollItem = root.schedule.Execute(CheckTracking).Every(PollIntervalMs);
            _timeoutItem = root.schedule.Execute(() => Advance()).StartingIn(TimeoutMs);
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
        }
    }
}
