using UnityEngine;
using UnityEngine.UI;

namespace CamLinkPro.UI
{
    /// <summary>
    /// The "virtual camera monitor": composites received JPEG frames (FIXED
    /// CONTRACT 2) on top of the AR passthrough at a user-controlled opacity.
    /// If no new frame has arrived in ~1.5s, fades the overlay back toward fully
    /// transparent automatically so a stalled connection doesn't leave a frozen,
    /// misleading frame on screen.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class VideoCompositor : MonoBehaviour
    {
        const float StaleAfterSeconds = 1.5f;
        const float FadeDurationSeconds = 1.0f;

        [Range(0f, 1f)] public float UserOpacity = 0.6f;

        RawImage rawImage;
        Texture2D texture;
        float timeSinceLastFrame = Mathf.Infinity;
        float staleFade = 1f; // 1 = fresh, fades to 0 while stale

        void Awake()
        {
            rawImage = GetComponent<RawImage>();
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            rawImage.texture = texture;
            ApplyAlpha(0f);
        }

        /// <summary>Call with a frame's raw JPEG bytes as soon as it's received
        /// (main thread only -- Texture2D APIs aren't thread-safe).</summary>
        public void SubmitFrame(byte[] jpegBytes)
        {
            if (jpegBytes == null || jpegBytes.Length == 0) return;

            // ImageConversion.LoadImage validates the data itself and returns
            // false rather than throwing on garbage input -- a malformed frame
            // just gets skipped instead of crashing the app.
            if (!texture.LoadImage(jpegBytes, markNonReadable: false)) return;

            timeSinceLastFrame = 0f;
        }

        void Update()
        {
            timeSinceLastFrame += Time.unscaledDeltaTime;

            float targetFade = timeSinceLastFrame <= StaleAfterSeconds ? 1f : 0f;
            float fadeSpeed = 1f / FadeDurationSeconds;
            staleFade = Mathf.MoveTowards(staleFade, targetFade, fadeSpeed * Time.unscaledDeltaTime);

            ApplyAlpha(UserOpacity * staleFade);
        }

        void ApplyAlpha(float alpha)
        {
            if (rawImage == null) return;
            Color c = rawImage.color;
            c.a = Mathf.Clamp01(alpha);
            rawImage.color = c;
        }
    }
}
