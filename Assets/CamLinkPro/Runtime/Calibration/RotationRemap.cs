using UnityEngine;

namespace CamLinkPro.Calibration
{
    /// Permutes which semantic rotation axis (Tilt=local X, Roll=local Y, Pan=local Z)
    /// lands in which wire slot (rot_x, rot_y, rot_z).
    public enum RotationRemap
    {
        XYZ,
        XZY,
        YXZ,
        YZX,
        ZXY,
        ZYX
    }

    public static class RotationRemapExtensions
    {
        /// Reorders (tiltDeg, rollDeg, panDeg) into wire-slot order per the remap.
        public static Vector3 Apply(this RotationRemap remap, float tiltDeg, float rollDeg, float panDeg)
        {
            return remap switch
            {
                RotationRemap.XYZ => new Vector3(tiltDeg, rollDeg, panDeg),
                RotationRemap.XZY => new Vector3(tiltDeg, panDeg, rollDeg),
                RotationRemap.YXZ => new Vector3(rollDeg, tiltDeg, panDeg),
                RotationRemap.YZX => new Vector3(rollDeg, panDeg, tiltDeg),
                RotationRemap.ZXY => new Vector3(panDeg, tiltDeg, rollDeg),
                RotationRemap.ZYX => new Vector3(panDeg, rollDeg, tiltDeg),
                _ => new Vector3(tiltDeg, panDeg, rollDeg)
            };
        }

        /// Which wire slot letter (X/Y/Z) a semantic axis currently lands on,
        /// for HUD labels like "Tilt-X" that must relabel live when the remap
        /// changes (spec section 4.4's freeze-axis row).
        public static char WireSlotFor(this RotationRemap remap, char semantic)
        {
            var (tilt, roll, pan) = semantic switch
            {
                'T' => (1f, 0f, 0f),
                'R' => (0f, 1f, 0f),
                _ => (0f, 0f, 1f)
            };
            var wire = remap.Apply(tilt, roll, pan);
            if (Mathf.Abs(wire.x) > 0.5f) return 'X';
            if (Mathf.Abs(wire.y) > 0.5f) return 'Y';
            return 'Z';
        }
    }
}
