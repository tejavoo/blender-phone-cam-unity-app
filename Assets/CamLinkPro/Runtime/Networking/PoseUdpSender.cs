using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CamLinkPro.Networking
{
    /// Fire-and-forget UDP pose channel. One packet per sample, no ACK/retry.
    /// sequence_id = microseconds since this sender was constructed, monotonic.
    public sealed class PoseUdpSender : IDisposable
    {
        readonly UdpClient _client;
        readonly IPEndPoint _endpoint;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public PoseUdpSender(string ip, int port)
        {
            _client = new UdpClient();
            _endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
        }

        public long NextSequenceId() => _stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;

        public int PacketsSent { get; private set; }
        public string LastError { get; private set; }

        public void Send(PoseSample sample)
        {
            try
            {
                var bytes = Encoding.ASCII.GetBytes(sample.ToWireFormat());
                _client.Send(bytes, bytes.Length, _endpoint);
                PacketsSent++;
            }
            catch (Exception ex)
            {
                // Fire-and-forget by design, but a silently-swallowed exception
                // here would look identical to "nothing is being tracked" from
                // the outside — surface it via LastError instead of letting it
                // vanish (this runs inside a UI Toolkit scheduled callback,
                // where an uncaught exception can quietly stop that callback).
                LastError = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        public void Dispose() => _client.Dispose();
    }
}
