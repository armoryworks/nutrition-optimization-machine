using System;
using FluentAssertions;
using Nom.Orch.UtilityServices;
using SkiaSharp;
using Xunit;
using ZXing;
using ZXing.Common;

namespace Nom.Api.Tests.Services.Shopping
{
    public class BarcodeDecodeServiceTests
    {
        private const string Upc = "036000291452";

        private static SKBitmap WriteUpc(string digits)
        {
            var writer = new ZXing.SkiaSharp.BarcodeWriter
            {
                Format = BarcodeFormat.UPC_A,
                Options = new EncodingOptions { Width = 600, Height = 300, Margin = 20 },
            };
            return writer.Write(digits);
        }

        private static byte[] Png(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        [Fact]
        public void Decodes_AnUpcAImage()
        {
            using var bitmap = WriteUpc(Upc);
            var result = new BarcodeDecodeService().Decode(Png(bitmap));

            result.Should().NotBeNull();
            result!.Text.Should().Be(Upc);
            result.Format.Should().Be("UPC_A");
        }

        [Fact]
        public void Decodes_ARotatedImage()
        {
            using var upright = WriteUpc(Upc);
            using var rotated = new SKBitmap(upright.Height, upright.Width);
            using (var canvas = new SKCanvas(rotated))
            {
                canvas.Clear(SKColors.White);
                canvas.Translate(rotated.Width, 0);
                canvas.RotateDegrees(90);
                canvas.DrawBitmap(upright, 0, 0);
            }

            var result = new BarcodeDecodeService().Decode(Png(rotated));
            result.Should().NotBeNull();
            result!.Text.Should().Be(Upc);
        }

        [Fact]
        public void Decodes_WhenTheBarcodeIsSmallInsideALargePhoto()
        {
            using var code = WriteUpc(Upc);
            using var photo = new SKBitmap(2400, 1800);
            using (var canvas = new SKCanvas(photo))
            {
                canvas.Clear(new SKColor(235, 225, 210));
                canvas.DrawBitmap(code, 900, 750);
            }

            var result = new BarcodeDecodeService().Decode(Png(photo));
            result.Should().NotBeNull();
            result!.Text.Should().Be(Upc);
        }

        [Fact]
        public void ReturnsNull_ForBlankImagesAndNonImages()
        {
            using var blank = new SKBitmap(300, 200);
            using (var canvas = new SKCanvas(blank)) canvas.Clear(SKColors.White);

            new BarcodeDecodeService().Decode(Png(blank)).Should().BeNull();
            new BarcodeDecodeService().Decode(new byte[] { 1, 2, 3, 4, 5 }).Should().BeNull();
            new BarcodeDecodeService().Decode(Array.Empty<byte>()).Should().BeNull();
        }
    }
}
