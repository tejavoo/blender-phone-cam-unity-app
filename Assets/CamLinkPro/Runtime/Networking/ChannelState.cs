namespace CamLinkPro.Networking
{
    public enum ChannelState
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// Derived from the additive "STATE <token>" line. Unknown is the safe
    /// "no info yet" default and must never be treated as an error state.
    public enum BlenderLiveState
    {
        Unknown,
        Connected,
        LiveNoData,
        Live
    }
}
