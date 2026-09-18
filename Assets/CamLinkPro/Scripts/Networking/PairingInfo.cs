namespace CamLinkPro.Networking
{
    /// <summary>Everything needed to open both connections to Blender, decoded
    /// from a pairing QR or typed in manually.</summary>
    public struct PairingInfo
    {
        public string Ip;
        public int PosePort;
        public int VideoPort;
        public string Token;

        public bool IsValid =>
            !string.IsNullOrEmpty(Ip) &&
            PosePort > 0 && PosePort <= 65535 &&
            VideoPort > 0 && VideoPort <= 65535 &&
            !string.IsNullOrEmpty(Token);
    }
}
