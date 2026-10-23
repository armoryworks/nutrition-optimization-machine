using System;
using System.Collections.Generic;
using Nom.Orch.UtilityInterfaces;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Nom.Orch.UtilityServices
{
    public class BarcodeDecodeService : IBarcodeDecodeService
    {
        private const int RetryWidth = 1600;
        private const int FullSizePassMaxWidth = 3200;
        private const double CentreCropFraction = 0.6;

        public BarcodeDecodeResult? Decode(byte[] image)
        {
            using var bitmap = ImageTranscoder.Decode(image);
            if (bitmap == null) return null;

            using var resized = bitmap.Width > RetryWidth
                ? bitmap.Resize(new SKImageInfo(RetryWidth, Math.Max(1, (int)Math.Round(bitmap.Height * (RetryWidth / (double)bitmap.Width)))), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : null;
            var found = Read(resized ?? bitmap);
            if (found == null)
            {
                var w = (int)(bitmap.Width * CentreCropFraction);
                var h = (int)(bitmap.Height * CentreCropFraction);
                if (w > 0 && h > 0)
                {
                    using var crop = new SKBitmap(w, h, bitmap.ColorType, bitmap.AlphaType);
                    if (bitmap.ExtractSubset(crop, SKRectI.Create((bitmap.Width - w) / 2, (bitmap.Height - h) / 2, w, h)))
                        found = Read(crop);
                }
            }
            if (found == null && resized != null && bitmap.Width <= FullSizePassMaxWidth)
                found = Read(bitmap);
            return found;
        }

        private static BarcodeDecodeResult? Read(SKBitmap bitmap)
        {
            var reader = new ZXing.SkiaSharp.BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.UPC_A, BarcodeFormat.UPC_E, BarcodeFormat.EAN_13, BarcodeFormat.EAN_8 },
                },
            };
            var result = reader.Decode(bitmap);
            return result == null || string.IsNullOrWhiteSpace(result.Text)
                ? null
                : new BarcodeDecodeResult(result.Text, result.BarcodeFormat.ToString());
        }
    }
}
