using UnityEngine;

namespace CamLinkPro.Networking
{
    /// <summary>Persists the last-used pairing info (IP/ports/token) to
    /// PlayerPrefs, so the manual-entry screen can prefill from whatever was last
    /// scanned or typed -- either to reconnect after a drop, or to hand-correct a
    /// single field (e.g. the video port) without retyping everything else.</summary>
    public static class PairingInfoStore
    {
        const string Prefix = "CamLinkPro.Pairing.";

        public static void Save(PairingInfo info)
        {
            PlayerPrefs.SetString(Prefix + "Ip", info.Ip ?? "");
            PlayerPrefs.SetInt(Prefix + "PosePort", info.PosePort);
            PlayerPrefs.SetInt(Prefix + "VideoPort", info.VideoPort);
            PlayerPrefs.SetString(Prefix + "Token", info.Token ?? "");
            PlayerPrefs.Save();
        }

        public static PairingInfo Load()
        {
            return new PairingInfo
            {
                Ip = PlayerPrefs.GetString(Prefix + "Ip", ""),
                PosePort = PlayerPrefs.GetInt(Prefix + "PosePort", 0),
                VideoPort = PlayerPrefs.GetInt(Prefix + "VideoPort", 0),
                Token = PlayerPrefs.GetString(Prefix + "Token", ""),
            };
        }
    }
}
