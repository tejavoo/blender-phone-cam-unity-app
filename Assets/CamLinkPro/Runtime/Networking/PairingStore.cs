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

        /// Wipes the saved pairing outright (Unpair) -- unlike Re-pair, which
        /// just reopens "Enter Manually" prefilled with these same values,
        /// this is for when the values themselves are stale/wrong and
        /// shouldn't keep coming back as the default next time.
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(LastUsedPrefix + "Ip");
            PlayerPrefs.DeleteKey(LastUsedPrefix + "PosePort");
            PlayerPrefs.DeleteKey(LastUsedPrefix + "VideoPort");
            PlayerPrefs.DeleteKey(LastUsedPrefix + "Token");
            PlayerPrefs.Save();
        }
    }
}
