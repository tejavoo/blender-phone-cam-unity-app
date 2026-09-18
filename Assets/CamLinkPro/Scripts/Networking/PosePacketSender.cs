using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CamLinkPro.Networking
{
    /// <summary>
    /// FIXED CONTRACT 1: one UDP packet per pose update, fire-and-forget, no
    /// acknowledgement, no retry. Sequence id is derived from a monotonically
    /// increasing clock (microseconds since this sender was created) rather than
    /// a counter that could restart at 0, per contract.
    ///
    /// This never blocks the caller on network state -- a UdpClient.Send is a
    /// single non-blocking datagram write, and any exception (e.g. no route to
    /// host, socket not yet configured) is caught and logged rather than thrown,
    /// so a bad network never stalls the pose-sending path.
    /// </summary>
    public sealed class PosePacketSender : IDisposable
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        UdpClient client;
        string targetIp;
        int targetPort;
        bool configured;

        public void Configure(string ip, int port)
        {
            targetIp = ip;
            targetPort = port;
            client?.Dispose();
            client = new UdpClient();
            configured = true;
        }

        /// <summary>Sends one packet. Returns the sequence id used and whether the
        /// underlying socket write succeeded -- purely for an on-screen debug
        /// readout; callers shouldn't otherwise act on the return value, since
        /// this channel is fire-and-forget by design.</summary>
        public (long sequenceId, bool ok) Send(Vector3 positionMetres, Vector3 eulerDegrees, float focalLengthMm, float sensorWidthMm)
        {
            if (!configured || client == null) return (-1, false);

            long sequenceId = clock.ElapsedTicks * 1_000_000L / Stopwatch.Frequency; // microseconds, monotonic, never resets

            string line = PosePacketFormatter.Format(sequenceId, positionMetres, eulerDegrees, focalLengthMm, sensorWidthMm);
            byte[] bytes = Encoding.ASCII.GetBytes(line);

            try
            {
                client.Send(bytes, bytes.Length, targetIp, targetPort);
                return (sequenceId, true);
            }
            catch (Exception e)
            {
                // Fire-and-forget by design -- log and keep going, never throw
                // back into the caller's per-frame update loop.
                Debug.LogWarning($"CamLinkPro: pose packet send failed ({e.GetType().Name}: {e.Message})");
                return (sequenceId, false);
            }
        }

        public void Dispose()
        {
            client?.Dispose();
            client = null;
            configured = false;
        }
    }
}
