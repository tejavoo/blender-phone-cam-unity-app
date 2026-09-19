using System.Globalization;

namespace CamLinkPro.Networking
{
    /// One wire-format pose packet: "sequence_id,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,focal_length_mm,sensor_width_mm"
    public readonly struct PoseSample
    {
        public long SequenceId { get; }
        public float PosX { get; }
        public float PosY { get; }
        public float PosZ { get; }
        public float RotX { get; }
        public float RotY { get; }
        public float RotZ { get; }
        public float FocalLengthMm { get; }
        public float SensorWidthMm { get; }

        public PoseSample(long sequenceId, float posX, float posY, float posZ,
            float rotX, float rotY, float rotZ, float focalLengthMm, float sensorWidthMm)
        {
            SequenceId = sequenceId;
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            RotX = rotX;
            RotY = rotY;
            RotZ = rotZ;
            FocalLengthMm = focalLengthMm;
            SensorWidthMm = sensorWidthMm;
        }

        public string ToWireFormat()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F5},{2:F5},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5}",
                SequenceId, PosX, PosY, PosZ, RotX, RotY, RotZ, FocalLengthMm, SensorWidthMm);
        }
    }
}
