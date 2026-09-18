using System;
using UnityEngine;

namespace CamLinkPro.Pipeline
{
    /// <summary>
    /// Two independent corrections applied to the pipeline's output, independent
    /// of everything in <see cref="PosePipeline"/> itself -- neither is part of
    /// the spec'd pose pipeline; both are user calibration knobs applied as a
    /// separate step after PosePipeline.Process():
    ///
    ///   1. A "starting point" zero-offset (additive, "ROP"/"ROR" in the HUD):
    ///      captures whatever the raw pipeline output is at the moment it's set
    ///      and subtracts it from every frame after, for when the phone's resting
    ///      orientation/position doesn't match what the Blender rig considers
    ///      neutral. Position and rotation are captured independently. Applied
    ///      on the pipeline's own continuous (already-unwrapped) angle scale, so
    ///      there's no +-180 degree wraparound to handle here.
    ///   2. Per-axis scale and flip (multiplicative, "gain"): corrects a mismatch
    ///      against how a specific rig interprets incoming axes (a mirrored
    ///      setup, or a scene built at a different real-world scale).
    ///
    /// Order is offset first, then scale/flip -- "zero" means zero regardless of
    /// whatever gain is dialed in. Persisted to PlayerPrefs so it survives app
    /// restarts.
    /// </summary>
    public sealed class OutputCalibration
    {
        const string PrefPrefix = "CamLinkPro.Calibration.";

        public Vector3 PositionScale = Vector3.one;
        public bool[] PositionFlip = new bool[3];   // x, y, z
        public Vector3 RotationScale = Vector3.one;
        // X (Tilt) and Z (Pan) flipped by default -- matches what most rigs
        // with a Blender-side "Set Up Delta Rig" base tilt actually need.
        public bool[] RotationFlip = { true, false, true };   // x, y, z

        public Vector3 PositionOffset = Vector3.zero;
        public Vector3 RotationOffsetDeg = Vector3.zero;

        // Default XZY: sends Tilt on rot_x (unchanged), Pan on rot_y, Roll on
        // rot_z -- compensates for a Blender rig whose parent Empty already
        // carries its own ~90 degree baseline (see RotationAxisRemap.cs).
        public RotationAxisRemap RotationRemap = RotationAxisRemap.XZY;

        public bool HasPositionOffset => PositionOffset != Vector3.zero;
        public bool HasRotationOffset => RotationOffsetDeg != Vector3.zero;

        public Vector3 ApplyToPosition(Vector3 p)
        {
            Vector3 zeroed = p - PositionOffset;
            return new Vector3(
                zeroed.x * PositionScale.x * (PositionFlip[0] ? -1f : 1f),
                zeroed.y * PositionScale.y * (PositionFlip[1] ? -1f : 1f),
                zeroed.z * PositionScale.z * (PositionFlip[2] ? -1f : 1f));
        }

        public Vector3 ApplyToEulerDegrees(Vector3 e)
        {
            Vector3 zeroed = e - RotationOffsetDeg;
            Vector3 calibrated = new Vector3(
                zeroed.x * RotationScale.x * (RotationFlip[0] ? -1f : 1f),
                zeroed.y * RotationScale.y * (RotationFlip[1] ? -1f : 1f),
                zeroed.z * RotationScale.z * (RotationFlip[2] ? -1f : 1f));
            // Remap is the very last step -- offset/scale/flip above still
            // operate in the pipeline's fixed Tilt/Roll/Pan (x/y/z) order.
            return RemapRotationAxes(calibrated, RotationRemap);
        }

        /// <summary>Permutes (Tilt, Roll, Pan) -- i.e. source (x, y, z) -- into
        /// the wire's (rot_x, rot_y, rot_z) slots per <paramref name="remap"/>.
        /// The enum name spells out, in order, which source axis feeds output
        /// slot 0, 1, 2.</summary>
        public static Vector3 RemapRotationAxes(Vector3 source, RotationAxisRemap remap) => remap switch
        {
            RotationAxisRemap.XYZ => new Vector3(source.x, source.y, source.z),
            RotationAxisRemap.XZY => new Vector3(source.x, source.z, source.y),
            RotationAxisRemap.YXZ => new Vector3(source.y, source.x, source.z),
            RotationAxisRemap.YZX => new Vector3(source.y, source.z, source.x),
            RotationAxisRemap.ZXY => new Vector3(source.z, source.x, source.y),
            RotationAxisRemap.ZYX => new Vector3(source.z, source.y, source.x),
            _ => source,
        };

        /// <summary>Given a remap, returns which output slot (0=X, 1=Y, 2=Z)
        /// each semantic channel (Tilt, Roll, Pan) currently lands on -- used
        /// purely to keep on-screen axis labels ("Tilt-X", "Pan-Y", ...)
        /// truthful about where each channel actually ends up after remapping.</summary>
        public static void GetOutputSlots(RotationAxisRemap remap, out int tiltSlot, out int rollSlot, out int panSlot)
        {
            int[] sourceForSlot = remap switch
            {
                RotationAxisRemap.XYZ => new[] { 0, 1, 2 },
                RotationAxisRemap.XZY => new[] { 0, 2, 1 },
                RotationAxisRemap.YXZ => new[] { 1, 0, 2 },
                RotationAxisRemap.YZX => new[] { 1, 2, 0 },
                RotationAxisRemap.ZXY => new[] { 2, 0, 1 },
                RotationAxisRemap.ZYX => new[] { 2, 1, 0 },
                _ => new[] { 0, 1, 2 },
            };
            tiltSlot = Array.IndexOf(sourceForSlot, 0);
            rollSlot = Array.IndexOf(sourceForSlot, 1);
            panSlot = Array.IndexOf(sourceForSlot, 2);
        }

        public void CapturePositionOffset(Vector3 currentRawPosition)
        {
            PositionOffset = currentRawPosition;
            Save();
        }

        public void CaptureRotationOffset(Vector3 currentRawEulerDegrees)
        {
            RotationOffsetDeg = currentRawEulerDegrees;
            Save();
        }

        public void ClearPositionOffset()
        {
            PositionOffset = Vector3.zero;
            Save();
        }

        public void ClearRotationOffset()
        {
            RotationOffsetDeg = Vector3.zero;
            Save();
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(PrefPrefix + "PosScaleX", PositionScale.x);
            PlayerPrefs.SetFloat(PrefPrefix + "PosScaleY", PositionScale.y);
            PlayerPrefs.SetFloat(PrefPrefix + "PosScaleZ", PositionScale.z);
            PlayerPrefs.SetFloat(PrefPrefix + "RotScaleX", RotationScale.x);
            PlayerPrefs.SetFloat(PrefPrefix + "RotScaleY", RotationScale.y);
            PlayerPrefs.SetFloat(PrefPrefix + "RotScaleZ", RotationScale.z);
            for (int i = 0; i < 3; i++)
            {
                PlayerPrefs.SetInt(PrefPrefix + "PosFlip" + i, PositionFlip[i] ? 1 : 0);
                PlayerPrefs.SetInt(PrefPrefix + "RotFlip" + i, RotationFlip[i] ? 1 : 0);
            }
            PlayerPrefs.SetFloat(PrefPrefix + "PosOffX", PositionOffset.x);
            PlayerPrefs.SetFloat(PrefPrefix + "PosOffY", PositionOffset.y);
            PlayerPrefs.SetFloat(PrefPrefix + "PosOffZ", PositionOffset.z);
            PlayerPrefs.SetFloat(PrefPrefix + "RotOffX", RotationOffsetDeg.x);
            PlayerPrefs.SetFloat(PrefPrefix + "RotOffY", RotationOffsetDeg.y);
            PlayerPrefs.SetFloat(PrefPrefix + "RotOffZ", RotationOffsetDeg.z);
            PlayerPrefs.SetInt(PrefPrefix + "RotRemap", (int)RotationRemap);
            PlayerPrefs.Save();
        }

        public static OutputCalibration Load()
        {
            var c = new OutputCalibration
            {
                PositionScale = new Vector3(
                    PlayerPrefs.GetFloat(PrefPrefix + "PosScaleX", 1f),
                    PlayerPrefs.GetFloat(PrefPrefix + "PosScaleY", 1f),
                    PlayerPrefs.GetFloat(PrefPrefix + "PosScaleZ", 1f)),
                RotationScale = new Vector3(
                    PlayerPrefs.GetFloat(PrefPrefix + "RotScaleX", 1f),
                    PlayerPrefs.GetFloat(PrefPrefix + "RotScaleY", 1f),
                    PlayerPrefs.GetFloat(PrefPrefix + "RotScaleZ", 1f)),
                PositionOffset = new Vector3(
                    PlayerPrefs.GetFloat(PrefPrefix + "PosOffX", 0f),
                    PlayerPrefs.GetFloat(PrefPrefix + "PosOffY", 0f),
                    PlayerPrefs.GetFloat(PrefPrefix + "PosOffZ", 0f)),
                RotationOffsetDeg = new Vector3(
                    PlayerPrefs.GetFloat(PrefPrefix + "RotOffX", 0f),
                    PlayerPrefs.GetFloat(PrefPrefix + "RotOffY", 0f),
                    PlayerPrefs.GetFloat(PrefPrefix + "RotOffZ", 0f)),
                RotationRemap = (RotationAxisRemap)PlayerPrefs.GetInt(PrefPrefix + "RotRemap", (int)RotationAxisRemap.XZY),
            };
            // X (Tilt) and Z (Pan) flip default to true, Y (Roll) to false.
            bool[] rotFlipDefaults = { true, false, true };
            for (int i = 0; i < 3; i++)
            {
                c.PositionFlip[i] = PlayerPrefs.GetInt(PrefPrefix + "PosFlip" + i, 0) != 0;
                c.RotationFlip[i] = PlayerPrefs.GetInt(PrefPrefix + "RotFlip" + i, rotFlipDefaults[i] ? 1 : 0) != 0;
            }
            return c;
        }
    }
}
