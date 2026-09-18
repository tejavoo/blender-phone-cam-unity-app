using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CamLinkPro.UI
{
    /// <summary>A button with two distinct actions: a quick tap fires
    /// <see cref="OnTap"/>, holding past <see cref="holdSeconds"/> fires
    /// <see cref="OnLongPress"/> instead (and suppresses the tap). Used for
    /// "deliberate, hard-to-trigger-by-accident" actions layered onto a button
    /// that also has an everyday quick-tap behavior -- e.g. Reset Origin
    /// (tap) vs. also clearing the starting-point calibration (long-press).</summary>
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class LongPressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] float holdSeconds = 1.2f;

        public event Action OnTap;
        public event Action OnLongPress;

        Coroutine holdRoutine;
        bool longPressFired;

        public void OnPointerDown(PointerEventData eventData)
        {
            longPressFired = false;
            holdRoutine = StartCoroutine(HoldTimer());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            StopHoldTimer();
            if (!longPressFired) OnTap?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData) => StopHoldTimer();

        void OnDisable() => StopHoldTimer();

        void StopHoldTimer()
        {
            if (holdRoutine != null)
            {
                StopCoroutine(holdRoutine);
                holdRoutine = null;
            }
        }

        IEnumerator HoldTimer()
        {
            yield return new WaitForSecondsRealtime(holdSeconds);
            longPressFired = true;
            OnLongPress?.Invoke();
        }
    }
}
