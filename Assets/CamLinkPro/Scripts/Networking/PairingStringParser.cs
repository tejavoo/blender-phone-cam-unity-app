using System;
using System.Text.RegularExpressions;

namespace CamLinkPro.Networking
{
    /// <summary>Parses the pairing string encoded in Blender's QR code:
    /// camlink://&lt;ip&gt;:&lt;pose_port&gt;:&lt;video_port&gt;?t=&lt;token&gt;
    /// Pure function, no Unity/engine dependency -- exercised directly by both the
    /// QR scanner and the manual-entry fallback.</summary>
    public static class PairingStringParser
    {
        // Matches: camlink://<ip>:<posePort>:<videoPort>?t=<token>
        // ip: dotted-quad (kept simple/permissive -- Blender is on the same LAN,
        // this doesn't need to reject every malformed IP shape, just recognise the
        // camlink:// scheme it actually produces).
        static readonly Regex Pattern = new Regex(
            @"^camlink://([^:/?]+):(\d{1,5}):(\d{1,5})\?t=([A-Za-z0-9_-]+)$",
            RegexOptions.Compiled);

        /// <summary>Tries to parse <paramref name="text"/> as a Cam Link Pro pairing
        /// string. Returns false (without throwing) for anything that doesn't match
        /// this exact shape -- a stray QR code in frame that isn't a pairing code
        /// should be silently ignored, not treated as an error.</summary>
        public static bool TryParse(string text, out PairingInfo info)
        {
            info = default;
            if (string.IsNullOrEmpty(text)) return false;

            Match m = Pattern.Match(text.Trim());
            if (!m.Success) return false;

            string ip = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[2].Value, out int posePort)) return false;
            if (!int.TryParse(m.Groups[3].Value, out int videoPort)) return false;
            string token = m.Groups[4].Value;

            if (posePort <= 0 || posePort > 65535) return false;
            if (videoPort <= 0 || videoPort > 65535) return false;

            info = new PairingInfo { Ip = ip, PosePort = posePort, VideoPort = videoPort, Token = token };
            return true;
        }
    }
}
