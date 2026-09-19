using System.Collections.Generic;
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace CamLinkPro.Qr
{
    /// Thin wrapper around ZXing.Net (Apache-2.0, github.com/micjahn/ZXing.Net)
    /// for decoding a QR code out of a live camera frame. Throttle calls from
    /// the caller — this does real work per call and shouldn't run every frame.
    public sealed class QrDecoder
    {
        readonly BarcodeReaderGeneric _reader = new()
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true,
            }
        };

        byte[] _buffer;

        /// pixels must be in WebCamTexture.GetPixels32() order (rows bottom-to-top).
        public string TryDecode(Color32[] pixels, int width, int height)
        {
            var needed = width * height * 4;
            if (_buffer == null || _buffer.Length != needed)
                _buffer = new byte[needed];

            for (var y = 0; y < height; y++)
            {
                var srcRowOffset = (height - 1 - y) * width;
                var dstOffset = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var c = pixels[srcRowOffset + x];
                    var o = dstOffset + x * 4;
                    _buffer[o] = c.r;
                    _buffer[o + 1] = c.g;
                    _buffer[o + 2] = c.b;
                    _buffer[o + 3] = c.a;
                }
            }

            var luminanceSource = new RGBLuminanceSource(_buffer, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
            var result = _reader.Decode(luminanceSource);
            return result?.Text;
        }
    }
}
