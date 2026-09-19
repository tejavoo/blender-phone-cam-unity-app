using System.Text.RegularExpressions;

namespace CamLinkPro.Networking
{
    /// camlink://<ip>:<pose_port>:<video_port>?t=<token>
    public readonly struct PairingInfo
    {
        static readonly Regex QrPattern = new(
            @"^camlink://(?<ip>[^:]+):(?<pose>\d{1,5}):(?<video>\d{1,5})\?t=(?<token>[A-Za-z0-9_-]+)$",
            RegexOptions.Compiled);

        public string Ip { get; }
        public int PosePort { get; }
        public int VideoPort { get; }
        public string Token { get; }

        public PairingInfo(string ip, int posePort, int videoPort, string token)
        {
            Ip = ip;
            PosePort = posePort;
            VideoPort = videoPort;
            Token = token;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(Ip) && !string.IsNullOrEmpty(Token) &&
            PosePort is > 0 and <= 65535 && VideoPort is > 0 and <= 65535;

        public static bool TryParseQr(string payload, out PairingInfo pairing)
        {
            pairing = default;
            if (string.IsNullOrEmpty(payload))
                return false;

            var match = QrPattern.Match(payload);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["pose"].Value, out var posePort) ||
                !int.TryParse(match.Groups["video"].Value, out var videoPort))
                return false;

            pairing = new PairingInfo(match.Groups["ip"].Value, posePort, videoPort, match.Groups["token"].Value);
            return pairing.IsValid;
        }
    }
}
