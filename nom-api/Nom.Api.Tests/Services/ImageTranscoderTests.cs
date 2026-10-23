using System;
using System.Linq;
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
        public void A_header_that_declares_an_absurd_size_is_refused_before_any_pixels_are_allocated()
        {
            ImageTranscoder.ToJpeg(PngHeader(30000, 30000), 1200).Should().BeNull();
            ImageTranscoder.ToGrayscalePng(PngHeader(30000, 30000)).Should().BeNull();
            ImageTranscoder.Decode(PngHeader(30000, 30000)).Should().BeNull();
            ImageTranscoder.ToJpeg(Png(40, 30), 1200).Should().NotBeNull();
        }

        private static byte[] PngHeader(int width, int height)
        {
            var ihdr = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            var type = System.Text.Encoding.ASCII.GetBytes("IHDR");
            var crcInput = type.Concat(ihdr).ToArray();
            var bytes = new System.Collections.Generic.List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
            bytes.AddRange(crcInput);
            var crc = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
            bytes.AddRange(crc);
            bytes.AddRange(new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 });
            return bytes.ToArray();
        }

        private static uint Crc32(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        [Fact]
        public void Bytes_that_are_not_an_image_give_null()
        {
            ImageTranscoder.ToJpeg(new byte[] { 1, 2, 3, 4 }, 1200).Should().BeNull();
            ImageTranscoder.ToGrayscalePng(System.Array.Empty<byte>()).Should().BeNull();
        }
    }
}
