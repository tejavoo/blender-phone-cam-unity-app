using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CamLinkPro.UI
{
    /// <summary>Snaps a Slider back to its centered rest position (0) the
    /// moment it's released -- turns an ordinary Slider into a camcorder-style
    /// zoom rocker: push away from center to zoom continuously (driven
    /// elsewhere, by polling <see cref="Slider.value"/> every frame), let go
    /// and it springs back to "not zooming". Slider already handles the drag
    /// positioning itself; this only adds the release behavior.</summary>
    [RequireComponent(typeof(Slider))]
    public sealed class ZoomRockerSlider : MonoBehaviour, IPointerUpHandler
    {
        public void OnPointerUp(PointerEventData eventData) => GetComponent<Slider>().value = 0f;
    }
}
