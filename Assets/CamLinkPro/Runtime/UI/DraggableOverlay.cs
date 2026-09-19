using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CamLinkPro.UI
{
    /// Drags an absolute-positioned `target` by pointer events on `handle`
    /// (typically a header bar inside it), clamped to stay within `bounds`.
    /// Fires `Released` with the final position once the drag ends, so
    /// callers can persist it without writing on every pointer move.
    public sealed class DraggableOverlay
    {
        readonly VisualElement _target;
        readonly VisualElement _handle;
        readonly VisualElement _bounds;
        Vector2 _pointerStart;
        Vector2 _originStart;

        public event Action<Vector2> Released;

        public DraggableOverlay(VisualElement target, VisualElement handle, VisualElement bounds)
        {
            _target = target;
            _handle = handle;
            _bounds = bounds;

            _handle.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _handle.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _handle.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        public void SetPosition(Vector2 pos)
        {
            _target.style.left = pos.x;
            _target.style.top = pos.y;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            _handle.CapturePointer(evt.pointerId);
            _pointerStart = evt.position;
            _originStart = new Vector2(_target.resolvedStyle.left, _target.resolvedStyle.top);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_handle.HasPointerCapture(evt.pointerId))
                return;

            var delta = (Vector2)evt.position - _pointerStart;
            var pos = _originStart + delta;

            if (_bounds != null)
            {
                var maxX = Mathf.Max(0f, _bounds.resolvedStyle.width - _target.resolvedStyle.width);
                var maxY = Mathf.Max(0f, _bounds.resolvedStyle.height - _target.resolvedStyle.height);
                pos.x = Mathf.Clamp(pos.x, 0f, maxX);
                pos.y = Mathf.Clamp(pos.y, 0f, maxY);
            }

            SetPosition(pos);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_handle.HasPointerCapture(evt.pointerId))
                return;
            _handle.ReleasePointer(evt.pointerId);
            Released?.Invoke(new Vector2(_target.resolvedStyle.left, _target.resolvedStyle.top));
        }
    }
}
