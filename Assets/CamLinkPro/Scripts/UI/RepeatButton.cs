using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CamLinkPro.UI
{
    /// <summary>A button that fires once immediately on press, then repeats at a
    /// fixed interval while held down -- for the zoom and dolly +/- controls,
    /// which the spec explicitly calls out as "press-and-hold to repeat".</summary>
    [RequireComponent(typeof(Button))]
    public sealed class RepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] float initialDelaySeconds = 0.35f;
        [SerializeField] float repeatIntervalSeconds = 0.08f;

        public event Action OnFire;

        Coroutine repeatRoutine;

        public void OnPointerDown(PointerEventData eventData)
        {
            OnFire?.Invoke();
            repeatRoutine = StartCoroutine(RepeatLoop());
        }

        public void OnPointerUp(PointerEventData eventData) => StopRepeating();
        public void OnPointerExit(PointerEventData eventData) => StopRepeating();
        void OnDisable() => StopRepeating();

        void StopRepeating()
        {
            if (repeatRoutine != null)
            {
                StopCoroutine(repeatRoutine);
                repeatRoutine = null;
            }
        }

        IEnumerator RepeatLoop()
        {
            yield return new WaitForSecondsRealtime(initialDelaySeconds);
            while (true)
            {
                OnFire?.Invoke();
                yield return new WaitForSecondsRealtime(repeatIntervalSeconds);
            }
        }
    }
}
