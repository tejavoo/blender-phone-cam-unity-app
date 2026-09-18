namespace CamLinkPro.Networking
{
    /// <summary>Blender-side state, learned only from the additive `STATE
    /// &lt;token&gt;` status line (FIXED CONTRACT 2 extension, add-on side
    /// confirmed as: sent on change, and again whenever a new client joins,
    /// never on disconnect). <see cref="Unknown"/> covers both "not
    /// connected yet" and "connected to an older add-on that never sends
    /// this line" -- deliberately not treated as "not live", so nothing
    /// gates on it (see CamLinkSessionController/HudController) beyond
    /// showing it as informational.</summary>
    public enum BlenderLiveState
    {
        Unknown,
        Connected,
        LiveNoData,
        Live,
    }
}
