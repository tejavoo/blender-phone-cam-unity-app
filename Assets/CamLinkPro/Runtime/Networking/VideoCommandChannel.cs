using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CamLinkPro.Networking
{
    /// Single persistent TCP socket carrying commands out and status/video in.
    /// All socket I/O runs on a background thread; call Pump() once per frame
    /// from the main thread to dispatch queued events.
    public sealed class VideoCommandChannel : IDisposable
    {
        const int ReconnectBackoffMs = 1500;
        const int MaxFrameBytes = 16 * 1024 * 1024;

        // A dead-but-not-closed TCP connection (WiFi drop without a clean
        // FIN/RST, a Blender-side stall) otherwise leaves client.Connected
        // reporting true indefinitely -- the "connected" pill would sit
        // green over a link that isn't actually moving anything. PING/PONG
        // is already part of the wire contract (the addon answers it); this
        // is what actually uses it to detect a stale connection and force a
        // reconnect instead of trusting the raw socket state.
        const int PingIntervalMs = 3000;
        const int PongTimeoutMs = 8000;

        readonly string _ip;
        readonly int _port;
        readonly string _token;
        readonly ConcurrentQueue<Action> _mainThreadEvents = new();
        readonly ConcurrentQueue<string> _outgoingCommands = new();
        readonly object _latestFrameLock = new();

        Thread _worker;
        volatile bool _running;
        byte[] _latestFrame;
        int _lastPongTickMs;

        public event Action<ChannelState> StateChanged;
        public event Action<BlenderLiveState> BlenderStateChanged;
        public event Action<string> StatusLineReceived;
        public event Action<byte[]> FrameReceived;
        public event Action<string> Error;

        public ChannelState State { get; private set; } = ChannelState.Disconnected;
        public BlenderLiveState BlenderState { get; private set; } = BlenderLiveState.Unknown;

        public VideoCommandChannel(string ip, int port, string token)
        {
            _ip = ip;
            _port = port;
            _token = token;
        }

        public void Start()
        {
            if (_running)
                return;
            _running = true;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "CamLinkPro-VideoCommandChannel" };
            _worker.Start();
        }

        public void Stop()
        {
            _running = false;
            _worker?.Join(TimeSpan.FromSeconds(2));
        }

        public void SendCommand(string command) => _outgoingCommands.Enqueue(command);

        /// Call once per frame from the main thread.
        public void Pump()
        {
            while (_mainThreadEvents.TryDequeue(out var action))
                action();
        }

        void WorkerLoop()
        {
            while (_running)
            {
                using var client = new TcpClient();
                try
                {
                    SetState(ChannelState.Connecting);
                    client.Connect(_ip, _port);
                    SetState(ChannelState.Connected);

                    using var stream = client.GetStream();
                    WriteLine(stream, $"AUTH {_token}");

                    RunConnectedLoop(client, stream);
                }
                catch (Exception ex)
                {
                    _mainThreadEvents.Enqueue(() => Error?.Invoke($"video/command channel error ({ex.GetType().Name}: {ex.Message})"));
                }

                SetState(ChannelState.Disconnected);
                SetBlenderState(BlenderLiveState.Unknown);

                if (_running)
                    Thread.Sleep(ReconnectBackoffMs);
            }
        }

        void RunConnectedLoop(TcpClient client, NetworkStream stream)
        {
            var readBuffer = new byte[4096];
            var textLine = new MemoryStream();
            var lastPingSent = Environment.TickCount;
            _lastPongTickMs = Environment.TickCount; // grace period before the first PONG

            while (_running && client.Connected)
            {
                FlushOutgoingCommands(stream);

                var now = Environment.TickCount;
                if (now - lastPingSent >= PingIntervalMs)
                {
                    WriteLine(stream, "PING");
                    lastPingSent = now;
                }
                if (now - _lastPongTickMs > PongTimeoutMs)
                    break; // no PONG in too long -- treat as dead, force a reconnect

                if (!stream.DataAvailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                var firstByte = new byte[1];
                if (stream.Read(firstByte, 0, 1) == 0)
                    break;

                if (firstByte[0] == 0)
                {
                    var frame = ReadLengthPrefixedFrame(stream, firstByte[0]);
                    if (frame == null)
                        break;

                    lock (_latestFrameLock)
                        _latestFrame = frame;

                    _mainThreadEvents.Enqueue(() =>
                    {
                        byte[] toDeliver;
                        lock (_latestFrameLock)
                        {
                            toDeliver = _latestFrame;
                            _latestFrame = null;
                        }
                        if (toDeliver != null)
                            FrameReceived?.Invoke(toDeliver);
                    });
                }
                else
                {
                    textLine.SetLength(0);
                    textLine.WriteByte(firstByte[0]);
                    if (!ReadLineRemainder(stream, textLine))
                        break;

                    var line = Encoding.ASCII.GetString(textLine.ToArray()).TrimEnd('\r', '\n');
                    HandleStatusLine(line);
                }
            }
        }

        byte[] ReadLengthPrefixedFrame(NetworkStream stream, byte firstLengthByte)
        {
            var lengthBytes = new byte[4];
            lengthBytes[0] = firstLengthByte;
            if (!ReadExact(stream, lengthBytes, 1, 3))
                return null;

            var length = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) | (lengthBytes[2] << 8) | lengthBytes[3];
            if (length < 0 || length > MaxFrameBytes)
                return null;

            var frame = new byte[length];
            return ReadExact(stream, frame, 0, length) ? frame : null;
        }

        static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            var remaining = count;
            while (remaining > 0)
            {
                var read = stream.Read(buffer, offset, remaining);
                if (read == 0)
                    return false;
                offset += read;
                remaining -= read;
            }
            return true;
        }

        static bool ReadLineRemainder(NetworkStream stream, MemoryStream into)
        {
            var b = new byte[1];
            while (true)
            {
                if (stream.Read(b, 0, 1) == 0)
                    return false;
                into.WriteByte(b[0]);
                if (b[0] == (byte)'\n')
                    return true;
            }
        }

        void HandleStatusLine(string line)
        {
            if (line == "PONG")
                _lastPongTickMs = Environment.TickCount;

            _mainThreadEvents.Enqueue(() => StatusLineReceived?.Invoke(line));

            if (line.StartsWith("STATE ", StringComparison.Ordinal))
            {
                var token = line.Substring("STATE ".Length).Trim();
                var state = token switch
                {
                    "CONNECTED" => BlenderLiveState.Connected,
                    "LIVE_NODATA" => BlenderLiveState.LiveNoData,
                    "LIVE" => BlenderLiveState.Live,
                    _ => BlenderLiveState.Unknown
                };
                SetBlenderState(state);
            }
        }

        void FlushOutgoingCommands(NetworkStream stream)
        {
            while (_outgoingCommands.TryDequeue(out var command))
                WriteLine(stream, command);
        }

        static void WriteLine(NetworkStream stream, string line)
        {
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        void SetState(ChannelState state)
        {
            State = state;
            _mainThreadEvents.Enqueue(() => StateChanged?.Invoke(state));
        }

        void SetBlenderState(BlenderLiveState state)
        {
            BlenderState = state;
            _mainThreadEvents.Enqueue(() => BlenderStateChanged?.Invoke(state));
        }

        public void Dispose() => Stop();
    }
}
