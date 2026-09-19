using UnityEngine;
using UnityEngine.UIElements;
using CamLinkPro.Networking;
using CamLinkPro.Qr;

namespace CamLinkPro.UI.Screens
{
    /// Live camera preview with real QR decoding (ZXing.Net, Apache-2.0). A
    /// successful decode hands the parsed pairing to Landing via PendingScan
    /// rather than connecting instantly — spec 4.2 wants a review step.
    public class ScanQrScreen : IScreenController
    {
        const int TimeoutMs = 25_000;
        const int ScanIntervalMs = 300;
        const int RequestedWidth = 960;
        const int RequestedHeight = 720;

        WebCamTexture _webCamTexture;
        IVisualElementScheduledItem _timeoutItem;
        IVisualElementScheduledItem _scanItem;
        readonly QrDecoder _decoder = new();
        bool _decoding;
        bool _exited;

        public void Mount(VisualElement root, AppShell shell)
        {
            root.Q<Button>("CancelButton").clicked += () => Exit(shell, ScreenId.Landing);

            if (WebCamTexture.devices.Length > 0)
            {
                _webCamTexture = new WebCamTexture(WebCamTexture.devices[0].name, RequestedWidth, RequestedHeight);
                _webCamTexture.Play();
                root.Q<Image>("CameraPreview").image = _webCamTexture;
                _scanItem = root.schedule.Execute(() => TryScan(shell)).Every(ScanIntervalMs);
            }

            _timeoutItem = root.schedule.Execute(() => Exit(shell, ScreenId.QrTimeout)).StartingIn(TimeoutMs);
        }

        void TryScan(AppShell shell)
        {
            if (_decoding || _webCamTexture.width <= 16)
                return;

            _decoding = true;
            try
            {
                var pixels = _webCamTexture.GetPixels32();
                var text = _decoder.TryDecode(pixels, _webCamTexture.width, _webCamTexture.height);
                if (text != null && PairingInfo.TryParseQr(text, out var pairing))
                {
                    PendingScan.Result = pairing;
                    Exit(shell, ScreenId.Landing);
                }
            }
            finally
            {
                _decoding = false;
            }
        }

        void Exit(AppShell shell, ScreenId destination)
        {
            if (_exited)
                return;
            _exited = true;
            _timeoutItem?.Pause();
            _scanItem?.Pause();
            shell.Navigate(destination);
        }

        public void Unmount()
        {
            _timeoutItem?.Pause();
            _scanItem?.Pause();
            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Object.Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }
    }
}
