using System;
using System.Diagnostics;
using CamLinkPro.AR;
using CamLinkPro.Networking;
using CamLinkPro.Pipeline;
using CamLinkPro.UI;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CamLinkPro.App
{
    /// <summary>The three record-flow states as actually displayed -- always driven
    /// by REC_ON/REC_OFF/ARMED status lines from Blender (the source of truth), not
    /// directly by the app's own button presses. CountingDown and Starting are
    /// phone-local sub-states that happen before Blender has confirmed anything.</summary>
    public enum RecordUiState
    {
        Live,
        Armed,
        CountingDown,
        Starting,
        Recording,
    }

    /// <summary>
    /// Top-level orchestrator: owns the pose pipeline, both network channels, and
    /// the record-flow state machine, and ticks them every frame. This is the one
    /// MonoBehaviour that's allowed to know about all the pieces; everything it
    /// owns (PosePipeline, PosePacketSender, VideoCommandChannel, ZoomState) is
    /// plain C# with no Unity lifecycle of its own.
    /// </summary>
    public sealed class CamLinkSessionController : MonoBehaviour
    {
        [SerializeField] ArPoseSource poseSource;
        [SerializeField] VideoCompositor videoCompositor;

        public readonly PosePipeline Pipeline = new PosePipeline();
        public readonly ZoomState Zoom = new ZoomState();

        // PlayerPrefs can't be touched from a field initializer (that runs during
        // Unity's own object construction, before any MonoBehaviour is allowed to
        // call engine APIs) -- loaded in Awake() instead.
        public OutputCalibration Calibration { get; private set; } = new OutputCalibration();

        readonly PosePacketSender poseSender = new PosePacketSender();
        readonly VideoCommandChannel videoChannel = new VideoCommandChannel();

        public event Action<RecordUiState> OnRecordStateChanged;
        public event Action<int> OnCountdownTick; // whole seconds remaining
        public event Action<ChannelState> OnChannelStateChanged;
        public event Action<bool> OnPairedChanged;
        public event Action<float> OnPingUpdated; // round-trip milliseconds
        public event Action<BlenderLiveState> OnBlenderLiveStateChanged;

        public RecordUiState RecordState { get; private set; } = RecordUiState.Live;
        public bool IsPaired { get; private set; }
        public PairingInfo CurrentPairing { get; private set; }
        public ChannelState ChannelState => videoChannel.State;

        /// <summary>Learned only from the additive `STATE &lt;token&gt;` line
        /// (see BlenderLiveState.cs) -- resets to Unknown whenever the
        /// channel leaves Connected, since any previously-known value goes
        /// stale the moment the socket drops.</summary>
        public BlenderLiveState BlenderLiveState { get; private set; } = BlenderLiveState.Unknown;

        /// <summary>Last measured PING-to-PONG round trip, in milliseconds. -1
        /// until the first one completes.</summary>
        public float LastPingMs { get; private set; } = -1f;

        const float PingIntervalSeconds = 2.5f;
        float pingTimer;
        readonly Stopwatch pingStopwatch = new Stopwatch();

        /// <summary>Exposed for the HUD's debug readout -- lets it show live AR
        /// tracking/pose state without this controller needing to format it itself.</summary>
        public ArPoseSource PoseSource => poseSource;
        public long LastSequenceId { get; private set; } = -1;
        public bool LastSendOk { get; private set; }

        /// <summary>The most recent raw (pre-calibration) pipeline output --
        /// what ROP/ROR capture from when pressed.</summary>
        public BlenderPose LastRawPose { get; private set; }
        public bool HasRawPose { get; private set; }

        float countdownRemaining;

        void Awake()
        {
            Calibration = OutputCalibration.Load();
            videoChannel.OnStatusLine += HandleStatusLine;
            videoChannel.OnFrame += frame => videoCompositor?.SubmitFrame(frame);
            videoChannel.OnStateChanged += s =>
            {
                // A previously-known Blender state goes stale the instant
                // the channel isn't Connected -- Connecting/Disconnected
                // both mean "we don't actually know anymore".
                if (s != ChannelState.Connected) SetBlenderLiveState(BlenderLiveState.Unknown);
                OnChannelStateChanged?.Invoke(s);
            };
        }

        void OnDestroy()
        {
            poseSender.Dispose();
            videoChannel.Dispose();
        }

        void Update()
        {
            videoChannel.Pump();

            if (IsPaired && poseSource != null)
            {
                bool ok = Pipeline.Process(poseSource.Position, poseSource.Rotation, Time.deltaTime, poseSource.TrackingReliable, out BlenderPose pose);
                if (ok)
                {
                    LastRawPose = pose; // pre-calibration, for ROP/ROR capture
                    HasRawPose = true;
                    Vector3 calibratedPos = Calibration.ApplyToPosition(pose.Position);
                    Vector3 calibratedEuler = Calibration.ApplyToEulerDegrees(pose.EulerDegrees);
                    float focal = Zoom.Resolve(poseSource.LiveFocalLengthMm);
                    var (seq, sendOk) = poseSender.Send(calibratedPos, calibratedEuler, focal, ZoomState.SensorWidthMm);
                    LastSequenceId = seq;
                    LastSendOk = sendOk;
                }
            }

            if (RecordState == RecordUiState.CountingDown)
            {
                countdownRemaining -= Time.unscaledDeltaTime;
                OnCountdownTick?.Invoke(Mathf.Max(0, Mathf.CeilToInt(countdownRemaining)));
                if (countdownRemaining <= 0f)
                {
                    videoChannel.SendCommand("START");
                    SetRecordState(RecordUiState.Starting);
                }
            }

            // Periodic liveness ping, purely for the round-trip latency readout --
            // only while the command channel actually has a live connection to
            // ping over.
            if (IsPaired && videoChannel.State == ChannelState.Connected)
            {
                pingTimer += Time.unscaledDeltaTime;
                if (pingTimer >= PingIntervalSeconds)
                {
                    pingTimer = 0f;
                    Ping();
                }
            }
            else
            {
                pingTimer = 0f;
            }
        }

        // -- pairing ------------------------------------------------------------

        public void Pair(PairingInfo info)
        {
            if (!info.IsValid)
            {
                Debug.LogWarning("CamLinkPro: ignoring invalid pairing info.");
                return;
            }

            CurrentPairing = info;
            PairingInfoStore.Save(info);
            PairingHistoryStore.Push(info);
            poseSender.Configure(info.Ip, info.PosePort);
            videoChannel.Start(info.Ip, info.VideoPort, info.Token);
            Pipeline.Reset();
            SetRecordState(RecordUiState.Live);
            IsPaired = true;
            OnPairedChanged?.Invoke(true);
        }

        /// <summary>Re-opens both connections against possibly-edited pairing
        /// info (e.g. the user corrected the video port to match what's actually
        /// configured in the Blender add-on) without leaving the app's other
        /// state (rig locks, calibration, etc.) disturbed.</summary>
        public void Reconnect(PairingInfo info)
        {
            Unpair();
            Pair(info);
        }

        public void Unpair()
        {
            IsPaired = false;
            poseSender.Dispose();
            videoChannel.Stop();
            OnPairedChanged?.Invoke(false);
        }

        // -- HUD actions ----------------------------------------------------------

        public void ResetOrigin() => Pipeline.Reset();

        public void ApplyRigPreset(RigPreset preset) => Pipeline.SetRigPreset(preset);

        public void SetFreeze(AxisFreezeState freeze) => Pipeline.SetFreeze(freeze);

        public void SetSteadiness(float value01) => Pipeline.Steadiness = value01;

        public void NudgeZoom(float deltaMm) => Zoom.Nudge(deltaMm);

        public void SetZoomFocalLength(float mm) => Zoom.SetManualFocalLength(mm);

        public void SetZoomAuto() => Zoom.SetAuto();

        const float DollyStepMetres = 0.1f;
        const float MaxDollyMetres = 20f;

        public void NudgeDolly(float directionSign)
        {
            float next = Pipeline.DollyOffsetMetres + directionSign * DollyStepMetres;
            Pipeline.DollyOffsetMetres = Mathf.Clamp(next, -MaxDollyMetres, MaxDollyMetres);
        }

        public void SetOpacity(float value01)
        {
            if (videoCompositor != null) videoCompositor.UserOpacity = Mathf.Clamp01(value01);
        }

        public void SetLevelHorizon(bool enabled) => Pipeline.LevelHorizon = enabled;

        /// <summary>"ROP" -- captures the current raw position as the new zero
        /// point, for when the phone's resting position doesn't line up with
        /// where the Blender rig considers neutral. Independent of Reset Origin:
        /// this is an instant output-stage offset with no AR warm-up pause,
        /// whereas Reset Origin re-anchors tracking itself.</summary>
        public void CapturePositionZero()
        {
            if (HasRawPose) Calibration.CapturePositionOffset(LastRawPose.Position);
        }

        /// <summary>"ROR" -- same idea as <see cref="CapturePositionZero"/>, for
        /// rotation.</summary>
        public void CaptureRotationZero()
        {
            if (HasRawPose) Calibration.CaptureRotationOffset(LastRawPose.EulerDegrees);
        }

        public void ClearPositionZero() => Calibration.ClearPositionOffset();
        public void ClearRotationZero() => Calibration.ClearRotationOffset();

        /// <summary>Clears both starting-point offsets at once -- wired to a
        /// long-press on Reset Origin as a deliberate, hard-to-trigger-by-accident
        /// "reset everything" action.</summary>
        public void ClearAllZeroOffsets()
        {
            Calibration.ClearPositionOffset();
            Calibration.ClearRotationOffset();
        }

        public void LockStart()
        {
            if (!IsPaired) return;
            videoChannel.SendCommand("LOCK_START");
        }

        public void BeginRecordCountdown()
        {
            if (!IsPaired) return;
            countdownRemaining = AppPreferences.RecordCountdownSeconds;
            SetRecordState(RecordUiState.CountingDown);
        }

        public void Stop()
        {
            if (!IsPaired) return;
            videoChannel.SendCommand("STOP");
        }

        /// <summary>Resets the phone's own display back to Live without sending
        /// anything -- there is no "cancel"/"un-arm" message in the wire
        /// contract, so this can't actually tell Blender to stand down; it's a
        /// local-only escape hatch. Covers every pre-recording state Cancel can
        /// be pressed from in one coherent step (previously this chained two
        /// separate methods that each re-checked RecordState, which -- since
        /// the first one's state change made the second one's condition true --
        /// silently cascaded Armed on every press instead of the intended
        /// single transition). Also covers Starting: if START was already sent
        /// but Blender's REC_ON confirmation never arrives (slow/unresponsive
        /// add-on, dropped packet), the record flow would otherwise be stuck on
        /// "Starting..." with no way back short of force-quitting the app.
        /// Pressing Lock Start again re-sends LOCK_START and re-arms cleanly.</summary>
        public void CancelCurrent()
        {
            switch (RecordState)
            {
                case RecordUiState.Armed:
                case RecordUiState.CountingDown:
                case RecordUiState.Starting:
                    SetRecordState(RecordUiState.Live);
                    break;
            }
        }

        public void Ping()
        {
            if (!IsPaired) return;
            pingStopwatch.Restart();
            videoChannel.SendCommand("PING");
        }

        // -- Blender-driven state ------------------------------------------------

        void HandleStatusLine(string line)
        {
            // Additive FIXED CONTRACT 2 line, not part of the original fixed
            // set below -- sent by the add-on on change and whenever a new
            // client joins (never on disconnect). Checked first since it's
            // the one line with a payload (a space-separated token) rather
            // than an exact bare match.
            if (line.StartsWith("STATE ", StringComparison.Ordinal))
            {
                SetBlenderLiveState(ParseBlenderLiveState(line.Substring("STATE ".Length)));
                return;
            }

            switch (line)
            {
                case "REC_ON":
                    SetRecordState(RecordUiState.Recording);
                    break;
                case "REC_OFF":
                    SetRecordState(RecordUiState.Live);
                    break;
                case "ARMED":
                    SetRecordState(RecordUiState.Armed);
                    break;
                case "PONG":
                    if (pingStopwatch.IsRunning)
                    {
                        pingStopwatch.Stop();
                        LastPingMs = (float)pingStopwatch.Elapsed.TotalMilliseconds;
                        OnPingUpdated?.Invoke(LastPingMs);
                    }
                    break;
                default:
                    Debug.Log($"CamLinkPro: unrecognised status line '{line}'");
                    break;
            }
        }

        void SetRecordState(RecordUiState state)
        {
            if (RecordState == state) return;
            RecordState = state;
            OnRecordStateChanged?.Invoke(state);
        }

        static BlenderLiveState ParseBlenderLiveState(string token) => token switch
        {
            "CONNECTED" => BlenderLiveState.Connected,
            "LIVE_NODATA" => BlenderLiveState.LiveNoData,
            "LIVE" => BlenderLiveState.Live,
            _ => BlenderLiveState.Unknown,
        };

        void SetBlenderLiveState(BlenderLiveState state)
        {
            if (BlenderLiveState == state) return;
            BlenderLiveState = state;
            OnBlenderLiveStateChanged?.Invoke(state);
        }
    }
}
