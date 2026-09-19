using UnityEngine;
using CamLinkPro.Preferences;

namespace CamLinkPro.Calibration
{
    public sealed class CalibrationSettings
    {
        const string P = "CamLinkPro.Calibration.";

        public readonly PrefFloat PositionScaleX = new(P + "PositionScaleX", 1f);
        public readonly PrefFloat PositionScaleY = new(P + "PositionScaleY", 1f);
        public readonly PrefFloat PositionScaleZ = new(P + "PositionScaleZ", 1f);
        public readonly PrefBool PositionFlipX = new(P + "PositionFlipX", false);
        public readonly PrefBool PositionFlipY = new(P + "PositionFlipY", false);
        public readonly PrefBool PositionFlipZ = new(P + "PositionFlipZ", false);

        public readonly PrefFloat RotationScaleX = new(P + "RotationScaleX", 1f);
        public readonly PrefFloat RotationScaleY = new(P + "RotationScaleY", 1f);
        public readonly PrefFloat RotationScaleZ = new(P + "RotationScaleZ", 1f);
        // Default: Tilt-X and Pan-Z flipped, Roll-Y not.
        public readonly PrefBool RotationFlipX = new(P + "RotationFlipX", true);
        public readonly PrefBool RotationFlipY = new(P + "RotationFlipY", false);
        public readonly PrefBool RotationFlipZ = new(P + "RotationFlipZ", true);

        public readonly PrefFloat PositionOffsetX = new(P + "PositionOffsetX", 0f);
        public readonly PrefFloat PositionOffsetY = new(P + "PositionOffsetY", 0f);
        public readonly PrefFloat PositionOffsetZ = new(P + "PositionOffsetZ", 0f);

        public readonly PrefFloat RotationOffsetDegX = new(P + "RotationOffsetDegX", 0f);
        public readonly PrefFloat RotationOffsetDegY = new(P + "RotationOffsetDegY", 0f);
        public readonly PrefFloat RotationOffsetDegZ = new(P + "RotationOffsetDegZ", 0f);

        public readonly PrefString RotationRemapName = new(P + "RotationRemap", nameof(Calibration.RotationRemap.XZY));
        public readonly PrefBool LevelHorizon = new(P + "LevelHorizon", false);

        public RotationRemap RotationRemap
        {
            get => System.Enum.TryParse<RotationRemap>(RotationRemapName.Value, out var r) ? r : Calibration.RotationRemap.XZY;
            set => RotationRemapName.Value = value.ToString();
        }

        public Vector3 PositionOffset => new(PositionOffsetX.Value, PositionOffsetY.Value, PositionOffsetZ.Value);
        public Vector3 RotationOffsetDeg => new(RotationOffsetDegX.Value, RotationOffsetDegY.Value, RotationOffsetDegZ.Value);

        /// Input is already Blender-space (Pipeline.PosePipeline does the fixed
        /// Unity Y-up -> Blender Z-up conversion upstream, matching the old
        /// app's layering: axis conversion lives in the pipeline, not here).
        /// Exact apply order, do not reorder: offset subtracted first, then
        /// scale x flip, then — rotation only — the remap permutation applied
        /// last, just before the packet is written to the wire.
        public Vector3 ApplyToPosition(Vector3 blenderSpacePosition)
        {
            var offset = blenderSpacePosition - PositionOffset;
            return new Vector3(
                offset.x * PositionScaleX.Value * (PositionFlipX.Value ? -1f : 1f),
                offset.y * PositionScaleY.Value * (PositionFlipY.Value ? -1f : 1f),
                offset.z * PositionScaleZ.Value * (PositionFlipZ.Value ? -1f : 1f));
        }

        /// rawRotationDeg is (tiltDeg, rollDeg, panDeg) — phone-local (X, Y, Z) before remap.
        public Vector3 ApplyToRotation(Vector3 rawRotationDeg)
        {
            var offset = rawRotationDeg - RotationOffsetDeg;
            var scaledFlipped = new Vector3(
                offset.x * RotationScaleX.Value * (RotationFlipX.Value ? -1f : 1f),
                offset.y * RotationScaleY.Value * (RotationFlipY.Value ? -1f : 1f),
                offset.z * RotationScaleZ.Value * (RotationFlipZ.Value ? -1f : 1f));
            return RotationRemap.Apply(scaledFlipped.x, scaledFlipped.y, scaledFlipped.z);
        }

        /// Expects Blender-space input (run raw Unity position through
        /// UnityToBlenderAxes first) — the offset is stored in the same
        /// space ApplyToPosition subtracts it in.
        public void CapturePositionZero(Vector3 blenderSpacePosition)
        {
            PositionOffsetX.Value = blenderSpacePosition.x;
            PositionOffsetY.Value = blenderSpacePosition.y;
            PositionOffsetZ.Value = blenderSpacePosition.z;
        }

        public void ClearPositionZero()
        {
            PositionOffsetX.Value = 0f;
            PositionOffsetY.Value = 0f;
            PositionOffsetZ.Value = 0f;
        }

        public void CaptureRotationZero(Vector3 rawRotationDeg)
        {
            RotationOffsetDegX.Value = rawRotationDeg.x;
            RotationOffsetDegY.Value = rawRotationDeg.y;
            RotationOffsetDegZ.Value = rawRotationDeg.z;
        }

        public void ClearRotationZero()
        {
            RotationOffsetDegX.Value = 0f;
            RotationOffsetDegY.Value = 0f;
            RotationOffsetDegZ.Value = 0f;
        }
    }
}
