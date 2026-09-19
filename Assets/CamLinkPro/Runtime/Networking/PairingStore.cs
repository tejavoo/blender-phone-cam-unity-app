using UnityEngine;

namespace CamLinkPro.Networking
{
    /// The single last-used pairing, standalone.
    public static class PairingStore
    {
        const string LastUsedPrefix = "CamLinkPro.Pairing.";

        public static PairingInfo? LoadLastUsed()
        {
            var ip = PlayerPrefs.GetString(LastUsedPrefix + "Ip", "");
            if (string.IsNullOrEmpty(ip))
                return null;

            var pairing = new PairingInfo(
                ip,
                PlayerPrefs.GetInt(LastUsedPrefix + "PosePort", 0),
                PlayerPrefs.GetInt(LastUsedPrefix + "VideoPort", 0),
                PlayerPrefs.GetString(LastUsedPrefix + "Token", ""));

            return pairing.IsValid ? pairing : null;
        }

        public static void SaveLastUsed(PairingInfo pairing)
        {
            PlayerPrefs.SetString(LastUsedPrefix + "Ip", pairing.Ip);
            PlayerPrefs.SetInt(LastUsedPrefix + "PosePort", pairing.PosePort);
            PlayerPrefs.SetInt(LastUsedPrefix + "VideoPort", pairing.VideoPort);
            PlayerPrefs.SetString(LastUsedPrefix + "Token", pairing.Token);
            PlayerPrefs.Save();
        }
    }
}
