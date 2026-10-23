using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Middleware;
using Xunit;

namespace Nom.Api.Tests.Middleware
{
    public class FileUploadSecurityMiddlewareTests
    {
        private sealed class ForwardOnlyStream : MemoryStream
        {
            public ForwardOnlyStream(byte[] data) : base(data) { }
            public override bool CanSeek => false;
            public override long Position
            {
                get => throw new System.NotSupportedException();
                set => throw new System.NotSupportedException();
            }
            public override long Seek(long offset, SeekOrigin loc) => throw new System.NotSupportedException();
        }

        private static async Task<(bool nextCalled, int status, long formFileLength)> Upload(string fileName, string contentType, byte[] fileBytes)
        {
            const string boundary = "----nomtestboundary";
            var head = Encoding.ASCII.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
            var tail = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");
            var body = head.Concat(fileBytes).Concat(tail).ToArray();

            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = $"multipart/form-data; boundary={boundary}";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new ForwardOnlyStream(body);

            var nextCalled = false;
            long formFileLength = -1;
            var middleware = new FileUploadSecurityMiddleware(async ctx =>
            {
                nextCalled = true;
                var form = await ctx.Request.ReadFormAsync();
                formFileLength = form.Files[0].Length;
            }, NullLogger<FileUploadSecurityMiddleware>.Instance);

            await middleware.InvokeAsync(context);
            return (nextCalled, context.Response.StatusCode, formFileLength);
        }

        [Fact]
        public async Task Jpeg_upload_on_a_non_seekable_body_reaches_the_endpoint_intact()
        {
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(Enumerable.Repeat((byte)0x11, 4000)).ToArray();

            var (nextCalled, status, length) = await Upload("photo.jpg", "image/jpeg", jpeg);

            Assert.True(nextCalled);
            Assert.Equal(200, status);
            Assert.Equal(jpeg.Length, length);
        }

        [Fact]
        public async Task Webp_upload_is_accepted()
        {
            var webp = Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 0x20, 0, 0, 0 })
                .Concat(Encoding.ASCII.GetBytes("WEBPVP8 ")).Concat(new byte[32]).ToArray();

            var (nextCalled, status, _) = await Upload("photo.webp", "image/webp", webp);

            Assert.True(nextCalled);
            Assert.Equal(200, status);
        }

        [Fact]
        public async Task Image_with_a_non_image_signature_is_rejected()
        {
            var fake = Encoding.ASCII.GetBytes("not really an image at all");

            var (nextCalled, status, _) = await Upload("photo.jpg", "image/jpeg", fake);

            Assert.False(nextCalled);
            Assert.Equal(400, status);
        }
    }
}
