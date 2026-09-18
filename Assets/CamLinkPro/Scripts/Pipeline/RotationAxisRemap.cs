namespace CamLinkPro.Pipeline
{
    /// <summary>Which pipeline rotation channel (Tilt=source X, Roll=source Y,
    /// Pan=source Z, in the pipeline's own fixed pre-remap order) is sent in
    /// which wire output slot (rot_x, rot_y, rot_z). Named as the order of
    /// source letters assigned to output slots 0,1,2 -- e.g. XZY sends Tilt on
    /// rot_x, Pan on rot_y, Roll on rot_z.
    ///
    /// Exists as an output-stage-only workaround: some Blender-side rig setups
    /// (a parent Empty carrying its own baked rotation, composing with the
    /// camera's driven rotation) shift which physical motion each Euler
    /// channel ends up representing once you read it back off the camera,
    /// without any way to fix that from this app short of remapping which
    /// channel we send where. Applied last, after offset/scale/flip -- those
    /// still operate in the pipeline's fixed Tilt/Roll/Pan order.</summary>
    public enum RotationAxisRemap
    {
        XYZ,
        XZY,
        YXZ,
        YZX,
        ZXY,
        ZYX,
    }
}
