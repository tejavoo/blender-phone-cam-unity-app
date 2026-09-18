using UnityEngine;

namespace CamLinkPro.Pipeline
{
    /// <summary>One pose sample ready to go out on the wire: position and rotation
    /// already in Blender's coordinate space and Euler convention.</summary>
    public struct BlenderPose
    {
        /// <summary>Position in metres, Blender space (right-handed, Z-up).</summary>
        public Vector3 Position;
        /// <summary>Rotation in degrees, Blender's XYZ Euler order (R = Rz * Ry * Rx).</summary>
        public Vector3 EulerDegrees;
    }

    /// <summary>
    /// Turns a raw AR pose into a Blender-ready pose, every frame, in this order:
    ///   1. Axis conversion (AR Y-up -> Blender Z-up)
    ///   2. Jump absorption (teleport detection)
    ///   3. Origin/heading anchor
    ///   4. Horizon levelling (optional)
    ///   5. Angle continuity (unwrapping)
    ///   6. Adaptive steadying (One Euro filter)
    ///   7. Dolly offset
    ///   8. Axis freezing
    ///
    /// This class has no MonoBehaviour or AR Foundation dependency -- it operates
    /// on plain Vector3/Quaternion values, so it can be driven by a real AR session
    /// or by a synthetic sequence of poses in a unit test identically. One instance
    /// per tracking session; call <see cref="Reset"/> on a fresh origin (user tapped
    /// "Reset origin") or a rig/preset change that should re-anchor.
    ///
    /// Orientation is carried as a rotation matrix throughout and only converted to
    /// Blender's XYZ Euler degrees at the very end, matching the convention a
    /// Blender camera object actually uses: it looks down its own local -Z axis,
    /// with +Y as up and +X as right.
    /// </summary>
    public sealed class PosePipeline
    {
        // A single frame at typical AR frame rates can't legitimately move the
        // phone this far by hand -- anything bigger is the tracker re-finding
        // itself after losing its fix on the room.
        public const float TeleportMetres = 0.35f;

        // "Finding the room" warm-up: no packets go out for this many frames
        // after a reset, origin change, or tracking recovering from lost/unreliable.
        public const int WarmupFrames = 20;

        public bool LevelHorizon = false;
        float steadiness = 0.4f;
        public float DollyOffsetMetres = 0f;

        AxisFreezeState freeze;

        // -- session state ----------------------------------------------------

        Vector3? origin;
        float headingYaw0;
        Vector3? prevRawBlenderPos;
        Vector3 prevLevelledRight = Vector3.right;
        bool haveLevelledRight;
        Vector3? prevEulerRad;
        int warmFrames;

        Vector3 holdPosition;
        Vector3 holdEulerRad;
        bool haveHold;
        bool recaptureHoldOnNextFrame;

        readonly OneEuroFilter[] filters = new OneEuroFilter[6];

        public PosePipeline()
        {
            for (int i = 0; i < 6; i++) filters[i] = new OneEuroFilter(OneEuroFilter.SteadinessToMinCutoff(steadiness), OneEuroFilter.FixedBeta);
        }

        /// <summary>True once warm-up has elapsed and packets should be sent.</summary>
        public bool IsWarmedUp => warmFrames >= WarmupFrames;

        public float Steadiness
        {
            get => steadiness;
            set
            {
                steadiness = Mathf.Clamp01(value);
                float minCutoff = OneEuroFilter.SteadinessToMinCutoff(steadiness);
                // Update the live filters' cutoff in place rather than replacing
                // them, so dragging the slider doesn't throw away filter history
                // (which would otherwise cause a small hitch on every tick).
                for (int i = 0; i < 6; i++) filters[i].MinCutoff = minCutoff;
            }
        }

        /// <summary>Re-anchors origin and heading, and restarts warm-up. Called on
        /// first use, "Reset origin", and reconnects.</summary>
        public void Reset()
        {
            origin = null;
            prevRawBlenderPos = null;
            prevEulerRad = null;
            haveLevelledRight = false;
            warmFrames = 0;
            haveHold = false;
            for (int i = 0; i < 6; i++) filters[i].Reset();
        }

        /// <summary>Call when the AR system reports tracking as lost/unreliable
        /// (not just noisy) so warm-up restarts once real tracking resumes, without
        /// discarding the origin/heading anchor the user already set.</summary>
        public void NoteTrackingUnreliable() => warmFrames = 0;

        public void SetRigPreset(RigPreset preset) => SetFreeze(AxisFreezeState.FromPreset(preset));

        /// <summary>Applies a new freeze set, re-capturing the hold values from the
        /// current live pose at this moment for any channel newly frozen -- so
        /// re-freezing (or switching presets) always holds the value at the moment
        /// of the change, never a stale one from an earlier freeze.</summary>
        public void SetFreeze(AxisFreezeState newFreeze)
        {
            freeze = newFreeze;
            recaptureHoldOnNextFrame = true;
        }

        public AxisFreezeState Freeze => freeze;

        /// <summary>
        /// Processes one raw AR frame.
        /// </summary>
        /// <param name="arPosition">Raw AR position, right-handed Y-up, metres.</param>
        /// <param name="arRotation">Raw AR orientation, same space as arPosition.</param>
        /// <param name="dt">Seconds since the previous call.</param>
        /// <param name="trackingReliable">False while the AR system itself reports
        /// the pose as unreliable/emulated (genuinely lost, not just noisy).</param>
        /// <param name="pose">The Blender-ready pose, valid only when this method
        /// returns true.</param>
        /// <returns>False while warming up or while tracking is unreliable -- the
        /// caller should not send a packet for those frames.</returns>
        public bool Process(Vector3 arPosition, Quaternion arRotation, float dt, bool trackingReliable, out BlenderPose pose)
        {
            pose = default;

            if (!trackingReliable)
            {
                NoteTrackingUnreliable();
                return false;
            }

            // -- 1. Axis conversion: AR (right-handed, Y-up) -> Blender (right-handed,
            // Z-up). A fixed 90 degree rotation about X, applied to both position and
            // orientation -- the whole rigid body is rotated by the same fixed
            // quaternion, which is what keeps the two consistent with each other.
            Vector3 rawBlenderPos = AxisConvert.YupToZup(arPosition);
            Matrix4x4 blenderRot = Matrix4x4.Rotate(AxisConvert.RotationYupToZup * arRotation);

            // -- 2. Jump absorption: an implausible single-frame move is treated as
            // a tracking artifact. Shift the origin by exactly the jump so the shot
            // doesn't visibly move -- the jump becomes invisible, not corrected after.
            if (prevRawBlenderPos.HasValue && origin.HasValue)
            {
                float delta = Vector3.Distance(rawBlenderPos, prevRawBlenderPos.Value);
                if (delta > TeleportMetres)
                {
                    origin = origin.Value + (rawBlenderPos - prevRawBlenderPos.Value);
                }
            }
            prevRawBlenderPos = rawBlenderPos;

            // -- 3. Origin and heading anchor: first good frame after a reset defines
            // Blender's (0,0,0) and which compass heading counts as +Y.
            if (!origin.HasValue)
            {
                origin = rawBlenderPos;
                headingYaw0 = HeadingFromMatrix(blenderRot);
                prevEulerRad = null;
                haveLevelledRight = false;
            }

            float yaw = -headingYaw0;
            Matrix4x4 yawed = Matrix4x4.Rotate(Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.forward)) * blenderRot;

            // -- 4. Horizon levelling (toggleable): reconstruct orientation with
            // "up" forced to world-up, discarding roll, to avoid the XYZ Euler
            // instability at 90 degrees of roll -- exactly the orientation a phone
            // is in during a normal landscape grip.
            if (LevelHorizon)
            {
                yawed = LevelHorizonMatrix(yawed, haveLevelledRight ? prevLevelledRight : (Vector3?)null, out prevLevelledRight);
                haveLevelledRight = true;
            }

            Vector3 eulerRad = MatrixToEulerXYZ(yawed);

            // -- 5. Angle continuity: keep each axis a continuous number across the
            // +-180 degree wrap rather than snapping.
            eulerRad = UnwrapAngles(prevEulerRad, eulerRad);
            prevEulerRad = eulerRad;

            Vector3 position = RotateAroundUp(rawBlenderPos - origin.Value, yaw);

            // -- 7. Dolly offset: push/pull along the camera's own current forward
            // direction. A Blender camera looks down its local -Z, so that's the
            // forward direction here too.
            if (Mathf.Abs(DollyOffsetMetres) > 1e-6f)
            {
                Vector3 fwd = EulerXYZToMatrix(eulerRad).MultiplyVector(new Vector3(0f, 0f, -1f));
                position += fwd * DollyOffsetMetres;
            }

            // -- 8. Axis freezing: a frozen channel holds exactly the value it had
            // at the moment it was frozen.
            if (!haveHold || recaptureHoldOnNextFrame)
            {
                holdPosition = position;
                holdEulerRad = eulerRad;
                haveHold = true;
                recaptureHoldOnNextFrame = false;
            }

            if (freeze.PositionX) position.x = holdPosition.x;
            if (freeze.PositionY) position.y = holdPosition.y;
            if (freeze.PositionZ) position.z = holdPosition.z;
            if (freeze.Tilt) eulerRad.x = holdEulerRad.x;
            if (freeze.Roll) eulerRad.y = holdEulerRad.y;
            if (freeze.Pan) eulerRad.z = holdEulerRad.z;

            // -- 6. Adaptive steadying: applied last of the "shaping" steps so the
            // filter sees the final, unwrapped, freeze-applied signal -- freezing a
            // channel should make it perfectly still, not filtered-toward-still.
            if (dt > 0f && dt < 0.5f)
            {
                position.x = filters[0].Filter(position.x, dt);
                position.y = filters[1].Filter(position.y, dt);
                position.z = filters[2].Filter(position.z, dt);
                eulerRad.x = filters[3].Filter(eulerRad.x, dt);
                eulerRad.y = filters[4].Filter(eulerRad.y, dt);
                eulerRad.z = filters[5].Filter(eulerRad.z, dt);
            }

            pose = new BlenderPose { Position = position, EulerDegrees = eulerRad * Mathf.Rad2Deg };

            if (warmFrames < WarmupFrames)
            {
                warmFrames++;
                return false;
            }
            return true;
        }

        // -- pure static math --------------------------------------------------

        /// <summary>Heading angle (radians) of the camera's forward direction,
        /// projected onto the Blender ground (XY) plane. Forward is -column2 of
        /// the rotation matrix (Blender's -Z-forward camera convention).</summary>
        internal static float HeadingFromMatrix(Matrix4x4 m) => Mathf.Atan2(m.m02, -m.m12);

        static Vector3 RotateAroundUp(Vector3 p, float angleRad)
        {
            float c = Mathf.Cos(angleRad), s = Mathf.Sin(angleRad);
            return new Vector3(p.x * c - p.y * s, p.x * s + p.y * c, p.z);
        }

        internal static Matrix4x4 LevelHorizonMatrix(Matrix4x4 m, Vector3? fallbackRight, out Vector3 right)
        {
            // Forward (the direction the camera looks) is -column2, per Blender's
            // -Z-forward camera convention.
            Vector3 forward = new Vector3(-m.m02, -m.m12, -m.m22);
            Vector3 worldUp = Vector3.forward; // Blender's up is +Z, held in the z-slot of our (x,y,z)-as-Blender-tuple Vector3.

            Vector3 r = Vector3.Cross(forward, worldUp);
            float n = r.magnitude;
            if (n < 1e-4f)
            {
                // Forward is pointing straight up/down -- can't derive a horizontal
                // right vector from it; keep whatever we had last frame instead of
                // snapping to an arbitrary axis.
                r = fallbackRight ?? Vector3.right;
            }
            else
            {
                r /= n;
            }

            Vector3 u = Vector3.Cross(r, forward);
            right = r;

            var result = Matrix4x4.identity;
            result.SetColumn(0, r);
            result.SetColumn(1, u);
            result.SetColumn(2, -forward);
            return result;
        }

        /// <summary>Decomposes a rotation matrix into Blender's XYZ Euler angles
        /// (radians), matching R = Rz(z) * Ry(y) * Rx(x).</summary>
        internal static Vector3 MatrixToEulerXYZ(Matrix4x4 m)
        {
            float sy = Mathf.Sqrt(m.m00 * m.m00 + m.m10 * m.m10);
            if (sy > 1e-6f)
            {
                float x = Mathf.Atan2(m.m21, m.m22);
                float y = Mathf.Atan2(-m.m20, sy);
                float z = Mathf.Atan2(m.m10, m.m00);
                return new Vector3(x, y, z);
            }
            else
            {
                // Gimbal lock at y = +-90 degrees: x and z become coupled. Pick x
                // from the remaining matrix entries and leave z to whatever the
                // continuity step preserves.
                float x = Mathf.Atan2(-m.m12, m.m11);
                float y = Mathf.Atan2(-m.m20, sy);
                return new Vector3(x, y, 0f);
            }
        }

        internal static Matrix4x4 EulerXYZToMatrix(Vector3 eulerRad)
        {
            float ca = Mathf.Cos(eulerRad.x), sa = Mathf.Sin(eulerRad.x);
            float cb = Mathf.Cos(eulerRad.y), sb = Mathf.Sin(eulerRad.y);
            float cg = Mathf.Cos(eulerRad.z), sg = Mathf.Sin(eulerRad.z);

            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(cb * cg, cg * sb * sa - ca * sg, cg * sb * ca + sa * sg, 0));
            m.SetRow(1, new Vector4(cb * sg, sg * sb * sa + ca * cg, sg * sb * ca - sa * cg, 0));
            m.SetRow(2, new Vector4(-sb, cb * sa, cb * ca, 0));
            return m;
        }

        internal static Vector3 UnwrapAngles(Vector3? prev, Vector3 next)
        {
            if (!prev.HasValue) return next;
            Vector3 result = next;
            for (int i = 0; i < 3; i++)
            {
                float d = result[i] - prev.Value[i];
                while (d > Mathf.PI) { result[i] -= 2f * Mathf.PI; d -= 2f * Mathf.PI; }
                while (d < -Mathf.PI) { result[i] += 2f * Mathf.PI; d += 2f * Mathf.PI; }
            }
            return result;
        }
    }

    /// <summary>The AR-to-Blender axis conversion, isolated as its own pure static
    /// helper since it's used identically to convert both the position vector and
    /// the orientation quaternion.</summary>
    public static class AxisConvert
    {
        /// <summary>A fixed 90 degree rotation about X mapping right-handed Y-up
        /// (AR) into right-handed Z-up (Blender): (x, y, z) -> (x, -z, y).</summary>
        public static Vector3 YupToZup(Vector3 v) => new Vector3(v.x, -v.z, v.y);

        /// <summary>The same 90 degree rotation about X, as a quaternion, for
        /// composing with the incoming orientation: newRotation = RotationYupToZup * oldRotation.</summary>
        public static readonly Quaternion RotationYupToZup = Quaternion.AngleAxis(90f, Vector3.right);
    }
}
