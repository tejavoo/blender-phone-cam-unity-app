using UnityEngine;

namespace CamLinkPro.Calibration
{
    public sealed class ZoomState
    {
        public enum Mode { Auto, Manual }

        public const float SensorWidthMm = 36f;
        public const float MinFocalLengthMm = 10f;
        public const float MaxFocalLengthMm = 300f;

        public Mode CurrentMode { get; set; } = Mode.Auto;
        public float ManualFocalLengthMm { get; private set; } = 50f;

        public void SetManualFocalLength(float mm) =>
            ManualFocalLengthMm = Mathf.Clamp(mm, MinFocalLengthMm, MaxFocalLengthMm);

        /// Pure-math conversion: vertical FOV + aspect -> horizontal FOV -> 35mm-equivalent focal length.
        public static float FocalLengthFromVerticalFov(float verticalFovDeg, float aspectRatio)
        {
            var verticalFovRad = verticalFovDeg * Mathf.Deg2Rad;
            var horizontalFovRad = 2f * Mathf.Atan(Mathf.Tan(verticalFovRad / 2f) * aspectRatio);
            return SensorWidthMm / (2f * Mathf.Tan(horizontalFovRad / 2f));
        }

        public float Resolve(float liveVerticalFovDeg, float aspectRatio) => CurrentMode switch
        {
            Mode.Manual => ManualFocalLengthMm,
            _ => FocalLengthFromVerticalFov(liveVerticalFovDeg, aspectRatio)
        };
    }
}
