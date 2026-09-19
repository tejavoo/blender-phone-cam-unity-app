using UnityEngine.UIElements;

namespace CamLinkPro.UI
{
    /// One controller per screen. AppShell instantiates the screen's UXML into
    /// a container and calls Mount(root); the controller wires its own
    /// elements and calls back into AppShell.Navigate(...) to move screens.
    public interface IScreenController
    {
        void Mount(VisualElement root, AppShell shell);
        void Unmount();
    }
}
