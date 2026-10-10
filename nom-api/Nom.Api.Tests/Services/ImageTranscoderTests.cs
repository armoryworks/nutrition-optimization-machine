using FluentAssertions;
using Nom.Orch.UtilityServices;
using SkiaSharp;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Untrusted uploads and fetched images are decoded and re-encoded with SkiaSharp (MIT).
    /// </summary>
    public class ImageTranscoderTests
    {
        private static byte[] Png(int width, int height)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(new SKColor(30, 120, 200));
            using var image = SKImage.FromBitmap(bitmap);
            return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        }

        [Fact]
        public void Wide_images_are_scaled_to_the_maximum_width_as_jpeg()
        {
            var jpeg = ImageTranscoder.ToJpeg(Png(2400, 1200), 1200);

            using var decoded = SKBitmap.Decode(jpeg);
            decoded.Width.Should().Be(1200);
            decoded.Height.Should().Be(600);
            SKCodec.Create(new SKMemoryStream(jpeg)).EncodedFormat.Should().Be(SKEncodedImageFormat.Jpeg);
        }

        [Fact]
        public void Small_images_keep_their_size()
        {
            using var decoded = SKBitmap.Decode(ImageTranscoder.ToJpeg(Png(300, 200), 1200));
            decoded.Width.Should().Be(300);
        }

        [Fact]
        public void Grayscale_png_for_ocr()
        {
            using var decoded = SKBitmap.Decode(ImageTranscoder.ToGrayscalePng(Png(40, 30)));
            var pixel = decoded.GetPixel(10, 10);
            pixel.Red.Should().Be(pixel.Green).And.Be(pixel.Blue);
        }

        [Fact]
        public void Bytes_that_are_not_an_image_give_null()
        {
            ImageTranscoder.ToJpeg(new byte[] { 1, 2, 3, 4 }, 1200).Should().BeNull();
            ImageTranscoder.ToGrayscalePng(System.Array.Empty<byte>()).Should().BeNull();
        }
    }
}
