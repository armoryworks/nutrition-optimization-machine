using System;
using System.IO;
using SkiaSharp;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Decodes untrusted image bytes with SkiaSharp and re-encodes them, which drops EXIF and any
    /// other embedded metadata. The camera's orientation is applied first, so a portrait phone
    /// photo stays upright once its EXIF is gone. Returns null for bytes that aren't an image.
    /// </summary>
    public static class ImageTranscoder
    {
        public static byte[]? ToJpeg(byte[] data, int maxWidth, int quality = 85)
        {
            using var bitmap = DecodeUpright(data);
            if (bitmap == null) return null;

            using var sized = bitmap.Width > maxWidth
                ? bitmap.Resize(new SKImageInfo(maxWidth, Math.Max(1, (int)Math.Round(bitmap.Height * (maxWidth / (double)bitmap.Width)))), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : null;
            return Encode(sized ?? bitmap, SKEncodedImageFormat.Jpeg, quality);
        }

        public static byte[]? ToGrayscalePng(byte[] data)
        {
            using var bitmap = DecodeUpright(data);
            if (bitmap == null) return null;

            using var gray = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Gray8, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(gray))
            using (var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                   {
                       0.2126f, 0.7152f, 0.0722f, 0, 0,
                       0.2126f, 0.7152f, 0.0722f, 0, 0,
                       0.2126f, 0.7152f, 0.0722f, 0, 0,
                       0, 0, 0, 1, 0,
                   }) })
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(bitmap, 0, 0, paint);
            }
            return Encode(gray, SKEncodedImageFormat.Png, 100);
        }

        private static SKBitmap? DecodeUpright(byte[] data)
        {
            if (data.Length == 0) return null;
            using var stream = new MemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec == null) return null;

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var decoded = new SKBitmap(info);
            var result = codec.GetPixels(info, decoded.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                decoded.Dispose();
                return null;
            }

            var origin = codec.EncodedOrigin;
            if (origin == SKEncodedOrigin.TopLeft) return decoded;

            var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var upright = new SKBitmap(new SKImageInfo(swap ? decoded.Height : decoded.Width, swap ? decoded.Width : decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(upright))
            {
                canvas.SetMatrix(OriginMatrix(origin, decoded.Width, decoded.Height));
                canvas.DrawBitmap(decoded, 0, 0);
            }
            decoded.Dispose();
            return upright;
        }

        private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        private static byte[]? Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(format, quality);
            return encoded?.ToArray();
        }
    }
}
