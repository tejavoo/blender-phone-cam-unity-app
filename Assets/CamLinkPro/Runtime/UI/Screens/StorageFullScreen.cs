using UnityEngine;
using UnityEngine.UIElements;

namespace CamLinkPro.UI.Screens
{
    public class StorageFullScreen : IScreenController
    {
        public void Mount(VisualElement root, AppShell shell)
        {
            var freeBytes = (long)0;
            try { freeBytes = new System.IO.DriveInfo(Application.persistentDataPath.Substring(0, 1)).AvailableFreeSpace; }
            catch { /* platform without drive info — leave 0, still shows the red readout */ }

            var freeMb = freeBytes / (1024 * 1024);
            root.Q<Label>("FreeSpaceReadout").text = $"{freeMb} MB free";
            root.Q<Button>("DismissButton").clicked += () => shell.Navigate(ScreenId.Landing);
        }

        public void Unmount() { }
    }
}
