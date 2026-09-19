using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CamLinkPro.UI
{
    /// A hand-built vertical drag rail (track + round thumb) driven entirely
    /// by pointer events. Unity's stock `Slider` in vertical mode renders as a
    /// thin, oddly-proportioned line on this project's theme — this sidesteps
    /// that entirely rather than fighting USS overrides on a built-in control.
    public sealed class VerticalDragControl
    {
        readonly VisualElement _track;
        readonly VisualElement _thumb;
        float _value = 0.5f; // 0 = bottom, 1 = top

        public event Action<float> ValueChanged;
        public event Action Released;

        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(value);
                PositionThumb();
            }
        }

        public VerticalDragControl(VisualElement track, VisualElement thumb, Func<bool> snapToCenterOnRelease = null)
        {
            _track = track;
            _thumb = thumb;
            _getSnap = snapToCenterOnRelease;

            _track.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _track.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _track.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _track.RegisterCallback<GeometryChangedEvent>(_ => PositionThumb());
        }

        readonly Func<bool> _getSnap;

        void OnPointerDown(PointerDownEvent evt)
        {
            _track.CapturePointer(evt.pointerId);
            SetFromLocalY(evt.localPosition.y);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_track.HasPointerCapture(evt.pointerId))
                return;
            SetFromLocalY(evt.localPosition.y);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_track.HasPointerCapture(evt.pointerId))
                return;
            _track.ReleasePointer(evt.pointerId);

            if (_getSnap != null && _getSnap())
            {
                Value = 0.5f;
                ValueChanged?.Invoke(_value);
            }
            Released?.Invoke();
        }

        void SetFromLocalY(float localY)
        {
            var height = _track.resolvedStyle.height;
            if (height <= 0f)
                return;
            // Track-local Y is 0 at top; value 1 (max) should be at the top.
            var t = 1f - Mathf.Clamp01(localY / height);
            _value = t;
            PositionThumb();
            ValueChanged?.Invoke(_value);
        }

        void PositionThumb()
        {
            var trackHeight = _track.resolvedStyle.height;
            var thumbHeight = _thumb.resolvedStyle.height;
            if (trackHeight <= 0f || float.IsNaN(trackHeight))
                return;

            var usableHeight = trackHeight - thumbHeight;
            var topOffset = (1f - _value) * usableHeight;
            _thumb.style.top = topOffset;
        }
    }
}
