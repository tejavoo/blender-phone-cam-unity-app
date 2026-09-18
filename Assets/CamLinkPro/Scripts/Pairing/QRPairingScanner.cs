using System;
using System.Collections;
using CamLinkPro.Networking;
using UnityEngine;
using ZXing;

namespace CamLinkPro.Pairing
{
    /// <summary>
    /// Scans the pairing QR (FIXED CONTRACT 3) using the device camera via ZXing.Net
    /// -- a pure-C# barcode reader with no native Android plugin or Gradle changes
    /// needed beyond the camera permission the app already requests.
    ///
    /// This deliberately does NOT reuse the AR session's camera feed: ARCameraManager's
    /// image format varies by device and is more integration than a one-time scan
    /// justifies. Instead it briefly opens a second, independent WebCamTexture just
    /// for scanning, then hands off the decoded pairing string and closes it.
    /// A decoded QR that isn't a Cam Link Pro pairing code (wrong shape) is silently
    /// ignored -- scanning just continues -- rather than treated as an error.
    /// </summary>
    public sealed class QRPairingScanner : MonoBehaviour
    {
        [Tooltip("Optional -- a RawImage the live scanning preview is drawn to while this is open.")]
        [SerializeField] UnityEngine.UI.RawImage previewImage;

        public event Action<PairingInfo> OnPaired;

        WebCamTexture webcam;
        BarcodeReaderGeneric reader;
        bool scanning;
        Color32[] pixelBuffer = new Color32[0];
        byte[] rgbaScratch = new byte[0];

        UnityEngine.UI.AspectRatioFitter previewFitter;
        int lastAppliedWidth = -1, lastAppliedHeight = -1, lastAppliedRotation = int.MinValue;
        bool lastAppliedMirror;

        void Awake()
        {
            reader = new BarcodeReaderGeneric
            {
                AutoRotate = false,
                Options = new ZXing.Common.DecodingOptions
                {
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                    TryHarder = true,
                },
            };
        }

        public void Open() => StartCoroutine(RequestCameraAndScan());

        public void Close()
        {
            scanning = false;
            StopAllCoroutines();
            if (webcam != null)
            {
                if (webcam.isPlaying) webcam.Stop();
                Destroy(webcam);
                webcam = null;
            }
        }

        IEnumerator RequestCameraAndScan()
        {
#if PLATFORM_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                float waited = 0f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && waited < 10f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    Debug.LogWarning("CamLinkPro: camera permission denied -- can't scan the pairing QR. Use manual entry instead.");
                    yield break;
                }
            }
#endif
            // A camera/permission quirk here (no device, driver hiccup, OS
            // denying access despite the earlier permission check) must never
            // crash the app -- it should just fail this scan attempt and leave
            // manual entry as the fallback. C# doesn't allow "yield return"
            // inside a try/catch block, so the risky, non-yielding setup work is
            // isolated into its own try below rather than wrapping the whole
            // coroutine.
            WebCamDevice[] devices;
            try
            {
                devices = WebCamTexture.devices;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"CamLinkPro: couldn't enumerate camera devices ({e.GetType().Name}: {e.Message}). Use manual entry instead.");
                yield break;
            }

            if (devices.Length == 0)
            {
                Debug.LogWarning("CamLinkPro: no camera device found for QR scanning.");
                yield break;
            }

            string deviceName = devices[0].name;
            foreach (var d in devices)
            {
                if (!d.isFrontFacing) { deviceName = d.name; break; }
            }

            try
            {
                webcam = new WebCamTexture(deviceName, 1280, 720, 30);
                if (previewImage != null)
                {
                    previewImage.texture = webcam;
                    previewFitter = previewImage.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                    if (previewFitter == null) previewFitter = previewImage.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
                    previewFitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                }
                webcam.Play();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"CamLinkPro: failed to start the camera for QR scanning ({e.GetType().Name}: {e.Message}). Use manual entry instead.");
                if (webcam != null) { Destroy(webcam); webcam = null; }
                yield break;
            }

            scanning = true;
            lastAppliedWidth = lastAppliedHeight = -1;
            lastAppliedRotation = int.MinValue;

            while (scanning)
            {
                yield return new WaitForSeconds(0.15f); // a few scans a second is plenty
                if (webcam == null || !webcam.didUpdateThisFrame) continue;

                UpdatePreviewOrientation();

                int w = webcam.width, h = webcam.height;
                try
                {
                    if (pixelBuffer.Length != w * h) pixelBuffer = new Color32[w * h];
                    webcam.GetPixels32(pixelBuffer);
                }
                catch (Exception e)
                {
                    // The camera can be yanked away mid-scan (permission revoked,
                    // device disconnected/reassigned) -- treat that as "stop
                    // scanning", not a crash.
                    Debug.LogWarning($"CamLinkPro: lost the camera mid-scan ({e.GetType().Name}: {e.Message}).");
                    Close();
                    yield break;
                }

                // WebCamTexture rows run bottom-to-top; ZXing expects a normal
                // top-to-bottom bitmap, so flip rows while packing into RGBA32
                // bytes (Color32's r,g,b,a field order matches RGBA32 exactly).
                int byteCount = w * h * 4;
                if (rgbaScratch.Length != byteCount) rgbaScratch = new byte[byteCount];
                for (int y = 0; y < h; y++)
                {
                    int srcRow = (h - 1 - y) * w;
                    int dstIndex = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = pixelBuffer[srcRow + x];
                        rgbaScratch[dstIndex++] = c.r;
                        rgbaScratch[dstIndex++] = c.g;
                        rgbaScratch[dstIndex++] = c.b;
                        rgbaScratch[dstIndex++] = c.a;
                    }
                }

                Result result;
                try { result = reader.Decode(rgbaScratch, w, h, RGBLuminanceSource.BitmapFormat.RGBA32); }
                catch (Exception e) { Debug.LogWarning($"CamLinkPro: QR decode error ({e.GetType().Name}: {e.Message})"); continue; }

                if (result == null) continue;

                if (PairingStringParser.TryParse(result.Text, out PairingInfo info))
                {
                    scanning = false;
                    OnPaired?.Invoke(info);
                    Close();
                }
                // else: some other QR code, not ours -- keep scanning.
            }
        }

        /// <summary>Keeps the preview looking like a normal, unskewed camera
        /// feed: WebCamTexture reports its *sensor* resolution regardless of how
        /// the phone is held, so without this the RawImage (a fixed-size box)
        /// just stretches whatever comes in to fit -- visibly skewed unless the
        /// box happens to match the feed's aspect ratio exactly. Fixes the
        /// aspect ratio via AspectRatioFitter and counter-rotates by
        /// <c>videoRotationAngle</c> (commonly 90 degrees on a phone's rear
        /// camera), only touching the transform when something actually
        /// changed.</summary>
        void UpdatePreviewOrientation()
        {
            if (previewImage == null || previewFitter == null || webcam == null) return;
            int w = webcam.width, h = webcam.height;
            if (w <= 16 || h <= 16) return; // still the placeholder size before the real feed starts

            int rotation = webcam.videoRotationAngle;
            bool mirror = webcam.videoVerticallyMirrored;
            if (w == lastAppliedWidth && h == lastAppliedHeight && rotation == lastAppliedRotation && mirror == lastAppliedMirror)
                return;

            lastAppliedWidth = w;
            lastAppliedHeight = h;
            lastAppliedRotation = rotation;
            lastAppliedMirror = mirror;

            bool swapped = rotation == 90 || rotation == 270;
            previewFitter.aspectRatio = swapped ? (float)h / w : (float)w / h;
            previewImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, -rotation);
            previewImage.rectTransform.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
        }

        void OnDestroy() => Close();
    }
}
