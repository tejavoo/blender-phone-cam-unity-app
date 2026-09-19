using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CamLinkPro.Networking
{
    /// Last-used pairing (standalone) plus a rolling history of the last 4
    /// distinct pairings, most-recent-first, shown as quick-connect chips.
    public static class PairingStore
    {
        const string LastUsedPrefix = "CamLinkPro.Pairing.";
        const string HistoryKey = "CamLinkPro.PairingHistory";
        const int HistoryLimit = 4;

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

            PushHistory(pairing);
        }

        static string Label(PairingInfo p) => $"{p.Ip}:{p.VideoPort}";

        public static List<PairingInfo> LoadHistory()
        {
            var raw = PlayerPrefs.GetString(HistoryKey, "");
            if (string.IsNullOrEmpty(raw))
                return new List<PairingInfo>();

            var result = new List<PairingInfo>();
            foreach (var entry in raw.Split('|'))
            {
                var parts = entry.Split(',');
                if (parts.Length != 4)
                    continue;
                if (!int.TryParse(parts[1], out var pose) || !int.TryParse(parts[2], out var video))
                    continue;
                result.Add(new PairingInfo(parts[0], pose, video, parts[3]));
            }
            return result;
        }

        static void PushHistory(PairingInfo pairing)
        {
            var history = LoadHistory();
            history.RemoveAll(p => Label(p) == Label(pairing));
            history.Insert(0, pairing);
            if (history.Count > HistoryLimit)
                history.RemoveRange(HistoryLimit, history.Count - HistoryLimit);

            var raw = string.Join("|", history.Select(p => $"{p.Ip},{p.PosePort},{p.VideoPort},{p.Token}"));
            PlayerPrefs.SetString(HistoryKey, raw);
            PlayerPrefs.Save();
        }
    }
}
