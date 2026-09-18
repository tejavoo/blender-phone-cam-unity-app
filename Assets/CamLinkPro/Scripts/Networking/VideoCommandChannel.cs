using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CamLinkPro.Networking
{
    public enum ChannelState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    /// <summary>
    /// FIXED CONTRACT 2: a single persistent TCP connection carrying both outgoing
    /// commands (START/STOP/LOCK_START/PING) and incoming status lines
    /// (REC_ON/REC_OFF/ARMED/PONG) plus interleaved length-prefixed JPEG video
    /// frames.
    ///
    /// All socket I/O runs on a dedicated background thread so a slow or
    /// disconnected connection can never stall the caller (in particular, never
    /// the pose-sending path, which doesn't touch this class at all). Received
    /// frames/status lines are handed to the main thread only via <see cref="Pump"/>,
    /// which must be called once per frame (e.g. from Update()) -- nothing here
    /// touches Unity APIs off the main thread.
    ///
    /// On disconnect, reconnects with a fixed ~1.5s backoff and re-sends AUTH on
    /// every new connection, until <see cref="Stop"/> is called.
    /// </summary>
    public sealed class VideoCommandChannel : IDisposable
    {
        const int ReconnectDelayMs = 1500;
        const int MaxFrameBytes = 16 * 1024 * 1024; // spec: frames are "well under 16MB"
        const int MaxStatusLineBytes = 256;

        public event Action<ChannelState> OnStateChanged;
        public event Action<string> OnStatusLine;
        /// <summary>Raised on the main thread (via Pump) with the newest frame's
        /// raw JPEG bytes. Only the newest undelivered frame is kept -- older ones
        /// arriving before the last Pump() are simply dropped, per contract.</summary>
        public event Action<byte[]> OnFrame;

        Thread worker;
        volatile bool running;
        volatile ChannelState state = ChannelState.Disconnected;
        volatile TcpClient currentTcp; // closed by Stop() to unblock a pending read immediately

        readonly object writeLock = new object();
        NetworkStream writeStream; // guarded by writeLock; null while disconnected

        readonly object frameLock = new object();
        byte[] pendingFrame;

        readonly object statusLock = new object();
        readonly Queue<string> pendingStatusLines = new Queue<string>();

        readonly object stateLock = new object();
        ChannelState pendingStateForMainThread = ChannelState.Disconnected;
        bool stateDirty;

        string ip;
        int port;
        string token;

        public ChannelState State => state;

        public void Start(string ip, int port, string token)
        {
            Stop();
            this.ip = ip;
            this.port = port;
            this.token = token;
            running = true;
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "CamLinkPro-VideoCommandChannel" };
            worker.Start();
        }

        public void Stop()
        {
            running = false;
            lock (writeLock) { writeStream = null; }
            // The worker thread is very likely blocked in a synchronous read --
            // setting running=false alone wouldn't unblock it until the remote
            // side sends more data. Closing the socket forces that read to throw
            // immediately, so Stop() returns promptly instead of waiting out (or
            // timing out on) the Join below.
            try { currentTcp?.Close(); } catch { /* already closed/disposed */ }
            worker?.Join(2000);
            worker = null;
            SetState(ChannelState.Disconnected);
        }

        /// <summary>Queues a command to send, e.g. "START". The trailing newline is
        /// added automatically. Safe to call from the main thread at any time --
        /// if not currently connected, the command is simply dropped (there is
        /// nothing meaningful to reconnect-and-resend here; the user can just press
        /// the button again once connected).</summary>
        public void SendCommand(string command)
        {
            NetworkStream stream;
            lock (writeLock) { stream = writeStream; }
            if (stream == null) return;

            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(command + "\n");
                lock (writeLock)
                {
                    // Re-check under the lock: the stream may have been cleared by
                    // a concurrent disconnect between the read above and here.
                    if (writeStream == null) return;
                    writeStream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"CamLinkPro: command send failed ({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>Call once per frame from the main thread. Dispatches any
        /// events accumulated on the background thread since the last call.</summary>
        public void Pump()
        {
            if (stateDirty)
            {
                ChannelState s;
                lock (stateLock) { s = pendingStateForMainThread; stateDirty = false; }
                OnStateChanged?.Invoke(s);
            }

            List<string> lines = null;
            lock (statusLock)
            {
                if (pendingStatusLines.Count > 0)
                {
                    lines = new List<string>(pendingStatusLines);
                    pendingStatusLines.Clear();
                }
            }
            if (lines != null)
                foreach (var line in lines) OnStatusLine?.Invoke(line);

            byte[] frame = null;
            lock (frameLock)
            {
                if (pendingFrame != null) { frame = pendingFrame; pendingFrame = null; }
            }
            if (frame != null) OnFrame?.Invoke(frame);
        }

        void SetState(ChannelState s)
        {
            state = s;
            lock (stateLock) { pendingStateForMainThread = s; stateDirty = true; }
        }

        void WorkerLoop()
        {
            while (running)
            {
                TcpClient tcp = null;
                try
                {
                    SetState(ChannelState.Connecting);
                    tcp = new TcpClient();
                    currentTcp = tcp;
                    tcp.Connect(ip, port);
                    var stream = tcp.GetStream();

                    lock (writeLock) { writeStream = stream; }
                    byte[] auth = Encoding.ASCII.GetBytes("AUTH " + token + "\n");
                    stream.Write(auth, 0, auth.Length);

                    SetState(ChannelState.Connected);
                    ReadLoop(stream);
                }
                catch (Exception e)
                {
                    if (running) Debug.LogWarning($"CamLinkPro: video/command channel error ({e.GetType().Name}: {e.Message})");
                }
                finally
                {
                    lock (writeLock) { writeStream = null; }
                    tcp?.Close();
                    currentTcp = null;
                    SetState(ChannelState.Disconnected);
                }

                // Fixed backoff before the next reconnect attempt, checked in small
                // increments so Stop() is responsive rather than blocking on a full
                // sleep.
                int waited = 0;
                while (running && waited < ReconnectDelayMs)
                {
                    Thread.Sleep(100);
                    waited += 100;
                }
            }
        }

        void ReadLoop(NetworkStream stream)
        {
            var statusBuffer = new List<byte>(64);

            while (running)
            {
                int first = ReadByteBlocking(stream);
                if (first < 0) return; // stream closed

                if (first == 0)
                {
                    // Length-prefix byte: frames are well under 16MB, so the
                    // most-significant byte of the 4-byte big-endian length is
                    // always 0 -- that's exactly how a frame is told apart from a
                    // status line.
                    int b1 = ReadByteBlocking(stream);
                    int b2 = ReadByteBlocking(stream);
                    int b3 = ReadByteBlocking(stream);
                    if (b1 < 0 || b2 < 0 || b3 < 0) return;

                    int length = (b1 << 16) | (b2 << 8) | b3;
                    if (length <= 0 || length > MaxFrameBytes)
                    {
                        // Malformed/oversized -- there's no reliable way to resync
                        // mid-stream without delimiters, so treat the connection as
                        // corrupted and let the reconnect loop start clean.
                        throw new IOException($"Rejecting frame with implausible length {length}");
                    }

                    byte[] frame = ReadExact(stream, length);
                    if (frame == null) return;

                    lock (frameLock) { pendingFrame = frame; } // newest wins, per contract
                }
                else
                {
                    statusBuffer.Clear();
                    statusBuffer.Add((byte)first);
                    while (true)
                    {
                        int b = ReadByteBlocking(stream);
                        if (b < 0) return;
                        if (b == '\n') break;
                        if (statusBuffer.Count >= MaxStatusLineBytes)
                            throw new IOException("Status line exceeded sane length without a newline");
                        statusBuffer.Add((byte)b);
                    }

                    string line = Encoding.ASCII.GetString(statusBuffer.ToArray()).TrimEnd('\r');
                    lock (statusLock) { pendingStatusLines.Enqueue(line); }
                }
            }
        }

        static int ReadByteBlocking(NetworkStream stream)
        {
            try { return stream.ReadByte(); }
            catch (IOException) { return -1; }
            catch (ObjectDisposedException) { return -1; }
        }

        static byte[] ReadExact(NetworkStream stream, int length)
        {
            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read;
                try { read = stream.Read(buffer, offset, length - offset); }
                catch (IOException) { return null; }
                if (read <= 0) return null; // stream closed mid-frame
                offset += read;
            }
            return buffer;
        }

        public void Dispose() => Stop();
    }
}
