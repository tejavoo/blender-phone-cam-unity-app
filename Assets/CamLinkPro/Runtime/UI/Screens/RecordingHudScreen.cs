using UnityEngine;
using UnityEngine.UIElements;
using CamLinkPro.Calibration;
using CamLinkPro.Networking;
using CamLinkPro.Pipeline;
using CamLinkPro.Preferences;

namespace CamLinkPro.UI.Screens
{
    public class RecordingHudScreen : IScreenController
    {
        static readonly CalibrationSettings Calibration = new();
        static readonly ZoomState Zoom = new();
        static AxisFreezeState _freeze;

        VisualElement _root;
        AppShell _shell;
        PoseUdpSender _poseSender;
        VideoCommandChannel _channel;
        IVisualElementScheduledItem _tickItem;
        readonly PosePipeline _pipeline = new();
        VerticalDragControl _zoomRail;
        DraggableOverlay _diagDrag;

        Label _recordStateLabel;
        Label _zoomLabel;
        Label _zoomModeLabel;
        Label _dollyLabel;
        VisualElement _countdownOverlay;
        Label _countdownNumber;
        VisualElement _savedToast;
        VisualElement _disconnectBanner;
        VisualElement _diagnosticsOverlay;
        Label _diagnosticsText;
        Image _liveMonitorImage;
        Texture2D _monitorTexture;
        bool _isRecording;
        bool _wasConnected = true;
        bool _poseActuallySending;
        bool _armed;
        int _cancelledCountdowns;
        bool _countdownCancelled;
        long _countdownToken;

        BlenderPose _lastBlenderPose;
        Vector3 _lastRawArPos;
        Vector3 _lastRawArEuler;
        float _lastTickTime;

        public void Mount(VisualElement root, AppShell shell)
        {
            _root = root;
            _shell = shell;

            // Safety net: guarantees AR is running whenever this screen is
            // visible, regardless of entry path (preflight, or Settings'
            // Back button returning straight here without going through
            // preflight again).
            shell.ArSession.EnableSession();
            _pipeline.Reset();

            var pairing = PairingStore.LoadLastUsed();
            if (pairing.HasValue)
            {
                _poseSender = new PoseUdpSender(pairing.Value.Ip, pairing.Value.PosePort);
                _channel = new VideoCommandChannel(pairing.Value.Ip, pairing.Value.VideoPort, pairing.Value.Token);
                _channel.StateChanged += OnChannelStateChanged;
                _channel.BlenderStateChanged += OnBlenderStateChanged;
                _channel.FrameReceived += OnFrameReceived;
                _channel.Start();
            }

            root.Q<Button>("HomeButton").clicked += () => Exit(ScreenId.Landing);
            root.Q<Button>("HudSettingsButton").clicked += () =>
            {
                _shell.SettingsReturnTo = ScreenId.RecordingHud;
                _shell.Navigate(ScreenId.SettingsGeneral);
            };

            root.Q<Button>("RigHandheld").clicked += () => ApplyPreset(RigPreset.Handheld);
            root.Q<Button>("RigTripod").clicked += () => ApplyPreset(RigPreset.Tripod);
            root.Q<Button>("RigDolly").clicked += () => ApplyPreset(RigPreset.Dolly);
            root.Q<Button>("RigCrane").clicked += () => ApplyPreset(RigPreset.Crane);

            BindFreezeChip("FreezeX", () => _freeze.PositionX, v => _freeze.PositionX = v);
            BindFreezeChip("FreezeY", () => _freeze.PositionY, v => _freeze.PositionY = v);
            BindFreezeChip("FreezeZ", () => _freeze.PositionZ, v => _freeze.PositionZ = v);
            BindFreezeChip("FreezePan", () => _freeze.Pan, v => _freeze.Pan = v);
            BindFreezeChip("FreezeTilt", () => _freeze.Tilt, v => _freeze.Tilt = v);
            BindFreezeChip("FreezeRoll", () => _freeze.Roll, v => _freeze.Roll = v);
            RefreshFreezeChipStates();
            RefreshRotationChipLabels();
            _pipeline.SetFreeze(_freeze);
            _pipeline.LevelHorizon = Calibration.LevelHorizon.Value;

            _recordStateLabel = root.Q<Label>("RecordStateLabel");
            _zoomLabel = root.Q<Label>("ZoomLabel");
            _zoomModeLabel = root.Q<Label>("ZoomModeLabel");
            _dollyLabel = root.Q<Label>("DollyLabel");
            _countdownOverlay = root.Q("CountdownOverlay");
            _countdownNumber = root.Q<Label>("CountdownNumber");
            _savedToast = root.Q("SavedToast");
            _disconnectBanner = root.Q("DisconnectBanner");
            _diagnosticsOverlay = root.Q("DiagnosticsOverlay");
            _diagnosticsText = root.Q<Label>("DiagnosticsText");
            _liveMonitorImage = root.Q<Image>("LiveMonitorImage");

            _diagDrag = new DraggableOverlay(_diagnosticsOverlay, root.Q("DiagnosticsHeader"), root.Q("HudRoot"));
            _diagDrag.SetPosition(new Vector2(AppPrefs.DiagnosticsOverlayX.Value, AppPrefs.DiagnosticsOverlayY.Value));
            _diagDrag.Released += pos =>
            {
                AppPrefs.DiagnosticsOverlayX.Value = pos.x;
                AppPrefs.DiagnosticsOverlayY.Value = pos.y;
            };
            root.Q<Button>("DiagnosticsCloseButton").clicked += () =>
            {
                AppPrefs.DiagnosticsHudVisible.Value = false;
                _diagnosticsOverlay.style.display = DisplayStyle.None;
            };

            root.Q<Button>("RecordButton").clicked += OnRecordClicked;
            root.Q<Button>("StopButton").clicked += OnStopClicked;
            root.Q<Button>("CountdownCancelButton").clicked += OnCountdownCancelClicked;
            root.Q<Button>("ZoomMinus").clicked += () => AdjustZoom(-5f);
            root.Q<Button>("ZoomPlus").clicked += () => AdjustZoom(5f);

            var lockStartToggle = root.Q<Toggle>("LockStartToggle");
            lockStartToggle.RegisterValueChangedCallback(evt =>
            {
                _armed = evt.newValue;
                if (_armed)
                    _channel?.SendCommand("LOCK_START");
            });

            var steadySlider = root.Q<Slider>("SteadySlider");
            var steadyValue = root.Q<Label>("SteadyValue");
            steadySlider.value = _pipeline.Steadiness;
            steadyValue.text = steadySlider.value.ToString("F1");
            steadySlider.RegisterValueChangedCallback(evt =>
            {
                steadyValue.text = evt.newValue.ToString("F1");
                _pipeline.Steadiness = evt.newValue;
            });

            var opacitySlider = root.Q<Slider>("OpacitySlider");
            var opacityValue = root.Q<Label>("OpacityValue");
            _liveMonitorImage.style.opacity = opacitySlider.value;
            opacitySlider.RegisterValueChangedCallback(evt =>
            {
                opacityValue.text = evt.newValue.ToString("F1");
                _liveMonitorImage.style.opacity = evt.newValue;
            });

            root.Q<Button>("ResetOriginButton").clicked += () =>
            {
                Calibration.ClearPositionZero();
                Calibration.ClearRotationZero();
                _pipeline.Reset();
            };
            root.Q<Button>("ZeroStartRopButton").clicked += () => Calibration.CapturePositionZero(_lastBlenderPose.Position);
            root.Q<Button>("ZeroStartRorButton").clicked += () => Calibration.CaptureRotationZero(_lastBlenderPose.EulerDegrees);
            root.Q<Button>("ReconnectNowButton").clicked += () => { };
            root.Q<Button>("StopAndSaveButton").clicked += OnStopClicked;

            var bigZoomSlider = root.Q("BigZoomSlider");
            bigZoomSlider.style.display = AppPrefs.BigZoomSliderVisible.Value ? DisplayStyle.Flex : DisplayStyle.None;
            _zoomRail = new VerticalDragControl(
                root.Q("ZoomRailTrack"), root.Q("ZoomRailThumb"),
                () => AppPrefs.ZoomSliderSnapsToCenter.Value);
            _zoomRail.Value = 0.5f;
            _zoomRail.ValueChanged += _ => { }; // rate applied continuously in Tick from current rail value

            UpdateZoomLabel();
            ApplyHudVisibilityPrefs();
            ApplyHudBackgroundPref();
            _lastTickTime = Time.unscaledTime;
            _tickItem = root.schedule.Execute(Tick).Every(16);
        }

        /// So the HUD can become pure buttons-on-camera-feed: the toggle is a
        /// master on/off (off = fully transparent regardless of the slider);
        /// the slider fine-tunes opacity while the toggle is on.
        void ApplyHudBackgroundPref()
        {
            var alpha = AppPrefs.HudBackgroundEnabled.Value
                ? Mathf.Clamp01(AppPrefs.HudBackgroundOpacity.Value)
                : 0f;
            var color = new Color(13f / 255f, 12f / 255f, 11f / 255f, alpha);

            _root.style.backgroundColor = color; // outer wrapper (safe-area gutter)
            _root.Q("HudRoot").style.backgroundColor = color;
        }

        void ApplyHudVisibilityPrefs()
        {
            // Hide-the-element-only, never hide the function: every toggle here
            // only removes the visual — the underlying setting stays reachable
            // and functional from Settings itself.
            SetVisible("StatusStrip", AppPrefs.StatusStripVisible.Value);
            SetVisible("RigPresetRow", AppPrefs.RigPresetRowVisible.Value);
            SetVisible("FreezeAxisRow", AppPrefs.FreezeAxisRowVisible.Value);
            SetVisible("BottomBar", AppPrefs.BottomControlBarVisible.Value);
            SetVisible("DiagnosticsOverlay", AppPrefs.DiagnosticsHudVisible.Value);
            // Opacity only means anything relative to the live monitor image,
            // so hide the slider along with it rather than leaving a control
            // on screen that visibly does nothing.
            SetVisible("OpacityGroup", AppPrefs.LiveMonitorVisible.Value);
            RefreshLiveMonitorVisibility();
        }

        void OnFrameReceived(byte[] jpegBytes)
        {
            _monitorTexture ??= new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!_monitorTexture.LoadImage(jpegBytes))
                return;

            _liveMonitorImage.image = _monitorTexture;
            RefreshLiveMonitorVisibility();
        }

        void RefreshLiveMonitorVisibility()
        {
            var visible = AppPrefs.LiveMonitorVisible.Value && _monitorTexture != null;
            _liveMonitorImage.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void SetVisible(string elementName, bool visible) =>
            _root.Q(elementName).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        void BindFreezeChip(string name, System.Func<bool> get, System.Action<bool> set)
        {
            var chip = _root.Q<Button>(name);
            chip.clicked += () =>
            {
                var next = !get();
                set(next);
                chip.EnableInClassList("chip--selected", next);
                _pipeline.SetFreeze(_freeze);
            };
        }

        void RefreshFreezeChipStates()
        {
            _root.Q<Button>("FreezeX").EnableInClassList("chip--selected", _freeze.PositionX);
            _root.Q<Button>("FreezeY").EnableInClassList("chip--selected", _freeze.PositionY);
            _root.Q<Button>("FreezeZ").EnableInClassList("chip--selected", _freeze.PositionZ);
            _root.Q<Button>("FreezePan").EnableInClassList("chip--selected", _freeze.Pan);
            _root.Q<Button>("FreezeTilt").EnableInClassList("chip--selected", _freeze.Tilt);
            _root.Q<Button>("FreezeRoll").EnableInClassList("chip--selected", _freeze.Roll);
        }

        void RefreshRotationChipLabels()
        {
            var remap = Calibration.RotationRemap;
            _root.Q<Button>("FreezePan").text = $"Pan-{remap.WireSlotFor('P')}";
            _root.Q<Button>("FreezeTilt").text = $"Tilt-{remap.WireSlotFor('T')}";
            _root.Q<Button>("FreezeRoll").text = $"Roll-{remap.WireSlotFor('R')}";
        }

        void ApplyPreset(RigPreset preset)
        {
            _freeze = AxisFreezeState.FromPreset(preset);
            RefreshFreezeChipStates();
            _pipeline.SetRigPreset(preset);
        }

        void AdjustZoom(float deltaMm)
        {
            Zoom.CurrentMode = ZoomState.Mode.Manual;
            Zoom.SetManualFocalLength(Zoom.ManualFocalLengthMm + deltaMm);
            UpdateZoomLabel();
        }

        void UpdateZoomLabel()
        {
            _zoomLabel.text = $"{Zoom.ManualFocalLengthMm:F0}mm";
            _zoomModeLabel.text = Zoom.CurrentMode == ZoomState.Mode.Auto ? "Zoom · auto" : "Zoom · manual";
        }

        /// The floating rail is a spring-centred rate control (like a real
        /// zoom rocker): dragging away from centre changes zoom continuously
        /// at a rate proportional to distance x sensitivity. On release it
        /// either snaps back to centre (stops changing) or — if the Settings
        /// toggle says "leave where it is" — keeps applying that same rate
        /// indefinitely, sticky, until touched again.
        const float MaxZoomRateMmPerSec = 120f;

        void ApplyZoomRail(float dt)
        {
            if (_zoomRail == null)
                return;
            var offset = _zoomRail.Value - 0.5f; // -0.5..0.5
            if (Mathf.Abs(offset) < 0.02f)
                return;

            Zoom.CurrentMode = ZoomState.Mode.Manual;
            var rate = offset * 2f * AppPrefs.ZoomSliderSensitivity.Value * MaxZoomRateMmPerSec;
            Zoom.SetManualFocalLength(Zoom.ManualFocalLengthMm + rate * dt);
            UpdateZoomLabel();
        }

        void OnRecordClicked()
        {
            if (_isRecording)
                return;
            HapticFeedback.Trigger();

            var seconds = AppPrefs.RecordCountdownSeconds.Value;
            if (seconds <= 0f)
            {
                StartRecording();
                return;
            }

            _countdownCancelled = false;
            var myToken = ++_countdownToken;
            _countdownOverlay.style.display = DisplayStyle.Flex;
            RunCountdown(Mathf.CeilToInt(seconds), myToken);
        }

        void RunCountdown(int secondsLeft, long token)
        {
            if (token != _countdownToken || _countdownCancelled)
                return;

            if (secondsLeft <= 0)
            {
                _countdownOverlay.style.display = DisplayStyle.None;
                StartRecording();
                return;
            }

            _countdownNumber.text = secondsLeft.ToString();
            _root.schedule.Execute(() => RunCountdown(secondsLeft - 1, token)).StartingIn(1000);
        }

        void OnCountdownCancelClicked()
        {
            HapticFeedback.Trigger();
            _countdownCancelled = true;
            _countdownToken++;
            _cancelledCountdowns++;
            _countdownOverlay.style.display = DisplayStyle.None;
        }

        void StartRecording()
        {
            _isRecording = true;
            _recordStateLabel.text = "Recording";
            _channel?.SendCommand("START");
        }

        void OnStopClicked()
        {
            if (!_isRecording)
                return;
            HapticFeedback.Trigger();

            _isRecording = false;
            _recordStateLabel.text = "Idle";
            _channel?.SendCommand("STOP");
            ShowSavedToast();
        }

        void ShowSavedToast()
        {
            _savedToast.Q<Label>("SavedToastLabel").text = "✓ Saved";
            _savedToast.style.display = DisplayStyle.Flex;
            _root.schedule.Execute(() => _savedToast.style.display = DisplayStyle.None).StartingIn(3500);
        }

        void OnChannelStateChanged(ChannelState state)
        {
            SetPill("LinkIcon", "LinkLabel", "connection", state switch
            {
                ChannelState.Connected => PillLevel.Good,
                ChannelState.Connecting => PillLevel.Degraded,
                _ => PillLevel.Lost
            });

            var connected = state == ChannelState.Connected;
            if (!connected && _wasConnected && _isRecording)
                _disconnectBanner.style.display = DisplayStyle.Flex;
            else if (connected)
                _disconnectBanner.style.display = DisplayStyle.None;
            _wasConnected = connected;
        }

        void OnBlenderStateChanged(BlenderLiveState state)
        {
            SetPill("BlenderIcon", "BlenderLabel", "stream", state switch
            {
                BlenderLiveState.Live => PillLevel.Good,
                BlenderLiveState.LiveNoData or BlenderLiveState.Connected => PillLevel.Degraded,
                _ => PillLevel.Lost
            });
        }

        enum PillLevel { Good, Degraded, Lost }

        void SetPill(string iconName, string labelName, string baseText, PillLevel level)
        {
            var icon = _root.Q<Label>(iconName);
            var label = _root.Q<Label>(labelName);
            label.text = baseText;
            // Plain ASCII only — Android's default font silently drops several
            // Unicode dingbats (see llms_complete_specs.txt section 5.3).
            icon.text = level == PillLevel.Lost ? "X" : level == PillLevel.Degraded ? "!" : "OK";
            icon.RemoveFromClassList("status-pill__icon--good");
            icon.RemoveFromClassList("status-pill__icon--degraded");
            icon.RemoveFromClassList("status-pill__icon--lost");
            icon.AddToClassList(level switch
            {
                PillLevel.Good => "status-pill__icon--good",
                PillLevel.Degraded => "status-pill__icon--degraded",
                _ => "status-pill__icon--lost"
            });
        }

        Vector3? _dollyOrigin;

        void Tick()
        {
            var now = Time.unscaledTime;
            var dt = now - _lastTickTime;
            _lastTickTime = now;

            _channel?.Pump();
            ApplyZoomRail(dt);
            SendPoseIfReady(dt);
            RefreshDiagnostics();
        }

        float _lastDiagLogTime;

        void SendPoseIfReady(float dt)
        {
            var tracking = _shell.ArSession.TrackingReliable;

            if (Time.unscaledTime - _lastDiagLogTime > 1f)
            {
                _lastDiagLogTime = Time.unscaledTime;
                Debug.Log($"CAMLINK_POSE_DIAG arState={UnityEngine.XR.ARFoundation.ARSession.state} " +
                          $"trackingReliable={tracking} warmedUp={_pipeline.IsWarmedUp} " +
                          $"poseSenderNull={_poseSender == null} camTransformNull={_shell.ArSession.CameraTransform == null} " +
                          $"packetsSent={_poseSender?.PacketsSent} lastSendError={_poseSender?.LastError} " +
                          $"camPos={_shell.ArSession.CameraTransform?.position}");

                var devices = UnityEngine.InputSystem.InputSystem.devices;
                var deviceList = string.Join(", ", System.Linq.Enumerable.Select(devices, d => $"{d.displayName}[{d.layout}]"));
                Debug.Log($"CAMLINK_POSE_DIAG devices({devices.Count})={deviceList}");
            }
            if (_poseActuallySending != (tracking && _poseSender != null))
            {
                _poseActuallySending = tracking && _poseSender != null;
                SetPill("PoseIcon", "PoseLabel", "gyro", _poseActuallySending ? PillLevel.Good : PillLevel.Lost);
            }

            var camTransform = _shell.ArSession.CameraTransform;
            if (camTransform == null)
                return;

            _lastRawArPos = camTransform.position;
            _lastRawArEuler = camTransform.eulerAngles;

            var ready = _pipeline.Process(_lastRawArPos, camTransform.rotation, dt, tracking, out var blenderPose);
            if (tracking)
                _lastBlenderPose = blenderPose;

            if (_poseSender == null || !ready)
                return;

            _dollyOrigin ??= blenderPose.Position;
            _dollyLabel.text = $"Dolly · {Vector3.Distance(_dollyOrigin.Value, blenderPose.Position):F1}m";

            var pos = Calibration.ApplyToPosition(blenderPose.Position);
            var rot = Calibration.ApplyToRotation(blenderPose.EulerDegrees);

            var aspect = Screen.width / (float)Screen.height;
            var focalLength = Zoom.Resolve(_shell.ArSession.GetVerticalFovDeg(), aspect);

            var sample = new PoseSample(_poseSender.NextSequenceId(),
                pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, focalLength, ZoomState.SensorWidthMm);
            _poseSender.Send(sample);
        }

        void RefreshDiagnostics()
        {
            if (!AppPrefs.DiagnosticsHudVisible.Value)
                return;

            var sb = new System.Text.StringBuilder();
            if (AppPrefs.DiagShowTracking.Value)
                sb.AppendLine($"AR: {UnityEngine.XR.ARFoundation.ARSession.state}  reliable={_shell.ArSession.TrackingReliable}  warmedUp={_pipeline.IsWarmedUp}");
            if (AppPrefs.DiagShowPosition.Value)
            {
                sb.AppendLine($"Raw AR pos: ({_lastRawArPos.x:F3}, {_lastRawArPos.y:F3}, {_lastRawArPos.z:F3})");
                sb.AppendLine($"Blender pos: ({_lastBlenderPose.Position.x:F3}, {_lastBlenderPose.Position.y:F3}, {_lastBlenderPose.Position.z:F3})");
            }
            if (AppPrefs.DiagShowRotation.Value)
            {
                sb.AppendLine($"Raw AR rot: ({_lastRawArEuler.x:F1}, {_lastRawArEuler.y:F1}, {_lastRawArEuler.z:F1})");
                sb.AppendLine($"Blender rot: ({_lastBlenderPose.EulerDegrees.x:F1}, {_lastBlenderPose.EulerDegrees.y:F1}, {_lastBlenderPose.EulerDegrees.z:F1})");
            }
            if (AppPrefs.DiagShowPackets.Value)
                sb.AppendLine($"Packets sent: {_poseSender?.PacketsSent ?? 0}  lastError: {_poseSender?.LastError}");
            if (AppPrefs.DiagShowArmed.Value)
                sb.AppendLine($"Armed: {_armed}");
            if (AppPrefs.DiagShowCancelled.Value)
                sb.AppendLine($"Cancelled countdowns: {_cancelledCountdowns}");
            if (AppPrefs.DiagShowRecordState.Value)
                sb.AppendLine($"Record: {(_isRecording ? "Recording" : "Idle")}");

            _diagnosticsText.text = sb.ToString().TrimEnd();
        }

        void Exit(ScreenId destination)
        {
            _shell.ArSession.DisableSession();
            _shell.Navigate(destination);
        }

        public void Unmount()
        {
            _tickItem?.Pause();
            _channel?.Dispose();
            _poseSender?.Dispose();
            if (_monitorTexture != null)
                Object.Destroy(_monitorTexture);
        }
    }
}
