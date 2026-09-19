namespace CamLinkPro.Calibration
{
    public enum RigPreset { Handheld, Tripod, Dolly, Crane }

    /// 6 independent freeze toggles. Frozen axes stop updating and hold their
    /// last sent value. A preset only sets the initial defaults for these
    /// toggles — each can still be freeze-toggled by hand afterward.
    public struct AxisFreezeState
    {
        public bool PositionX;
        public bool PositionY;
        public bool PositionZ;
        public bool Pan;
        public bool Tilt;
        public bool Roll;

        public static AxisFreezeState FromPreset(RigPreset preset) => preset switch
        {
            RigPreset.Handheld => default,
            RigPreset.Tripod => new AxisFreezeState { PositionX = true, PositionY = true, PositionZ = true },
            RigPreset.Dolly => new AxisFreezeState { PositionZ = true },
            RigPreset.Crane => new AxisFreezeState { PositionX = true, PositionY = true },
            _ => default
        };
    }
}
