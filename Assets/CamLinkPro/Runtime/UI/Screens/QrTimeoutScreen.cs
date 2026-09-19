using UnityEngine.UIElements;

namespace CamLinkPro.UI.Screens
{
    public class QrTimeoutScreen : IScreenController
    {
        public void Mount(VisualElement root, AppShell shell)
        {
            root.Q<Button>("TryAgainButton").clicked += () => shell.Navigate(ScreenId.ScanQr);
            root.Q<Button>("EnterManuallyButton").clicked += () => shell.Navigate(ScreenId.Landing);
            root.Q<Button>("CancelButton").clicked += () => shell.Navigate(ScreenId.Landing);
        }

        public void Unmount() { }
    }
}
