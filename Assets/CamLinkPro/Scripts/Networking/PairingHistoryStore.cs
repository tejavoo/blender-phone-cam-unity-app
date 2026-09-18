using System.Collections.Generic;
using UnityEngine;

namespace CamLinkPro.Networking
{
    /// <summary>Keeps the last few distinct pairings (most recent first) so the
    /// Landing screen can offer one-tap reconnects when switching between more
    /// than one Blender setup, instead of only ever remembering the single most
    /// recent one. Persisted to PlayerPrefs as simple delimited strings --
    /// nothing here needs real JSON.</summary>
    public static class PairingHistoryStore
    {
        const string Key = "CamLinkPro.PairingHistory";
        const int MaxEntries = 4;
        const char EntrySeparator = '|';
        const char FieldSeparator = ',';

        public static List<PairingInfo> Load()
        {
            var list = new List<PairingInfo>();
            string raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return list;

            foreach (string entry in raw.Split(EntrySeparator))
            {
                string[] parts = entry.Split(FieldSeparator);
                if (parts.Length != 4) continue;
                if (!int.TryParse(parts[1], out int posePort)) continue;
                if (!int.TryParse(parts[2], out int videoPort)) continue;
                list.Add(new PairingInfo { Ip = parts[0], PosePort = posePort, VideoPort = videoPort, Token = parts[3] });
            }
            return list;
        }

        public static void Push(PairingInfo info)
        {
            if (!info.IsValid) return;

            var list = Load();
            list.RemoveAll(p => p.Ip == info.Ip && p.PosePort == info.PosePort && p.VideoPort == info.VideoPort);
            list.Insert(0, info);
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);

            var entries = new List<string>();
            foreach (var p in list)
                entries.Add($"{p.Ip}{FieldSeparator}{p.PosePort}{FieldSeparator}{p.VideoPort}{FieldSeparator}{p.Token}");

            PlayerPrefs.SetString(Key, string.Join(EntrySeparator.ToString(), entries));
            PlayerPrefs.Save();
        }
    }
}
