namespace CamLinkPro.Pipeline
{
    /// <summary>The four one-tap rig presets. Each just sets a starting point for
    /// the six per-channel freeze locks -- the user can still hand-tune individual
    /// channels afterwards.</summary>
    public enum RigPreset
    {
        Handheld,
        Tripod,
        Dolly,
        Crane,
    }

    /// <summary>Which of the six pose channels are currently frozen. A frozen
    /// channel holds whatever value it had at the moment it was frozen -- the hold
    /// value itself lives in <see cref="PosePipeline"/>, not here; this struct is
    /// just the on/off state. Position channels are in Blender space (Z-up);
    /// rotation channels are named for what they mean to a camera operator, not
    /// their raw axis letter, since that mapping is an implementation detail.</summary>
    public struct AxisFreezeState
    {
        public bool PositionX;
        public bool PositionY;
        public bool PositionZ;
        public bool Pan;   // rotation about the up axis (Blender rot_z)
        public bool Tilt;  // rotation about the left/right axis (Blender rot_x)
        public bool Roll;  // rotation about the forward axis (Blender rot_y)

        public static AxisFreezeState FromPreset(RigPreset preset)
        {
            switch (preset)
            {
                case RigPreset.Tripod:
                    return new AxisFreezeState { PositionX = true, PositionY = true, PositionZ = true };
                case RigPreset.Dolly:
                    return new AxisFreezeState { PositionZ = true };
                case RigPreset.Crane:
                    return new AxisFreezeState { PositionX = true, PositionY = true };
                case RigPreset.Handheld:
                default:
                    return new AxisFreezeState();
            }
        }

        public bool Equals(AxisFreezeState other) =>
            PositionX == other.PositionX && PositionY == other.PositionY && PositionZ == other.PositionZ &&
            Pan == other.Pan && Tilt == other.Tilt && Roll == other.Roll;
    }
}
