using UnityEngine;

namespace CamLinkPro.App
{
    /// <summary>Real free-space check on the volume the app actually writes
    /// to (Blender does the recording, but Blender's own temp/output paths
    /// are on the same phone storage this app's data lives on for any given
    /// user's setup often enough that a low-space phone is worth flagging
    /// before a take starts). Unity has no built-in cross-platform free-space
    /// API; on Android this reads it for real via android.os.StatFs. Off
    /// Android (Editor, any future non-Android build) it honestly reports
    /// "unknown" rather than faking a number.</summary>
    public static class DeviceStorage
    {
        /// <summary>Free bytes on the partition backing
        /// <see cref="Application.persistentDataPath"/>, or -1 if this
        /// platform doesn't support the check.</summary>
        public static long GetFreeBytes()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var statFs = new AndroidJavaObject("android.os.StatFs", Application.persistentDataPath);
                long blockSize = statFs.Call<long>("getBlockSizeLong");
                long availableBlocks = statFs.Call<long>("getAvailableBlocksLong");
                return blockSize * availableBlocks;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"CamLinkPro: couldn't read free storage ({e.GetType().Name}: {e.Message}).");
                return -1;
            }
#else
            return -1;
#endif
        }

        public const long LowStorageThresholdBytes = 500L * 1024 * 1024; // 500 MB

        public static string FormatMegabytes(long bytes) => $"{bytes / (1024f * 1024f):0} MB";
    }
}
