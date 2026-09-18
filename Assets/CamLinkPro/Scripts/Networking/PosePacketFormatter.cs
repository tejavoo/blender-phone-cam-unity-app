using System.Globalization;
using UnityEngine;

namespace CamLinkPro.Networking
{
    /// <summary>Formats the FIXED CONTRACT 1 pose packet line. Pure function --
    /// no socket, no Unity lifecycle -- so the exact wire format can be unit
    /// tested independent of the UDP sender.</summary>
    public static class PosePacketFormatter
    {
        /// <summary>
        /// sequence_id,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,focal_length_mm,sensor_width_mm
        /// Nine comma-separated ASCII fields, no spaces, five decimal places on
        /// every float. sequenceId must already be monotonically increasing --
        /// this function just formats it.
        /// </summary>
        public static string Format(long sequenceId, Vector3 positionMetres, Vector3 eulerDegrees, float focalLengthMm, float sensorWidthMm)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F5},{2:F5},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5}",
                sequenceId,
                positionMetres.x, positionMetres.y, positionMetres.z,
                eulerDegrees.x, eulerDegrees.y, eulerDegrees.z,
                focalLengthMm, sensorWidthMm);
        }
    }
}
