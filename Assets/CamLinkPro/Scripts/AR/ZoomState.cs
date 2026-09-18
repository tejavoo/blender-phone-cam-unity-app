using UnityEngine;

namespace CamLinkPro.AR
{
    public enum ZoomMode
    {
        /// <summary>Focal length tracks the AR system's actual field of view, so
        /// the Blender lens matches the phone's real field of view.</summary>
        Auto,
        /// <summary>Focal length is locked to a fixed value the user set with the
        /// zoom +/- control.</summary>
        Manual,
    }

    /// <summary>Optical zoom state -- distinct from <see cref="Pipeline.PosePipeline.DollyOffsetMetres"/>,
    /// which moves the camera instead of changing its lens. Pure logic, no Unity
    /// lifecycle dependency beyond Mathf/UnityEngine.</summary>
    public sealed class ZoomState
    {
        public const float MinFocalLengthMm = 10f;
        public const float MaxFocalLengthMm = 300f;

        /// <summary>The sensor width sent alongside focal_length_mm in every pose
        /// packet. Only the ratio of the two matters for matching a field of view,
        /// so this is really just a normalisation constant -- 36mm (the common
        /// "full-frame equivalent" convention) is picked so a manually set focal
        /// length reads as a familiar cinema-lens number in Blender too.</summary>
        public const float SensorWidthMm = 36f;

        public ZoomMode Mode { get; private set; } = ZoomMode.Auto;
        public float ManualFocalLengthMm { get; private set; } = 35f;

        /// <summary>Presses of the zoom +/- control switch into manual mode (you
        /// can't "adjust" a value that's continuously being overwritten by live
        /// FOV) and nudge the locked focal length, clamped to the supported range.</summary>
        public void Nudge(float deltaMm)
        {
            Mode = ZoomMode.Manual;
            ManualFocalLengthMm = Mathf.Clamp(ManualFocalLengthMm + deltaMm, MinFocalLengthMm, MaxFocalLengthMm);
        }

        public void SetAuto() => Mode = ZoomMode.Auto;

        public void SetManualFocalLength(float mm)
        {
            Mode = ZoomMode.Manual;
            ManualFocalLengthMm = Mathf.Clamp(mm, MinFocalLengthMm, MaxFocalLengthMm);
        }

        /// <summary>Which focal length to actually transmit this frame.</summary>
        public float Resolve(float liveFovBasedFocalLengthMm) =>
            Mode == ZoomMode.Auto ? liveFovBasedFocalLengthMm : ManualFocalLengthMm;

        /// <summary>Converts the AR camera's live vertical field of view into an
        /// equivalent focal length against <see cref="SensorWidthMm"/>, so the
        /// transmitted (focal_length_mm, sensor_width_mm) pair reproduces the same
        /// field of view on the Blender side.</summary>
        public static float FocalLengthFromVerticalFov(float verticalFovDegrees, float aspect, float sensorWidthMm)
        {
            float vFovRad = verticalFovDegrees * Mathf.Deg2Rad;
            float hFovRad = 2f * Mathf.Atan(Mathf.Tan(vFovRad * 0.5f) * aspect);
            float denom = Mathf.Tan(hFovRad * 0.5f);
            if (denom < 1e-6f) return MaxFocalLengthMm;
            return Mathf.Clamp((sensorWidthMm * 0.5f) / denom, MinFocalLengthMm, MaxFocalLengthMm);
        }
    }
}
