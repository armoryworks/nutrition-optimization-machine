using Nom.Api.Middleware;
using Xunit;

namespace Nom.Api.Tests.Middleware
{
    public class AuditLoggingSanitizeTests
    {
        [Theory]
        [InlineData("password")]
        [InlineData("confirmPassword")]
        [InlineData("newPassword")]
        [InlineData("currentPassword")]
        [InlineData("refreshToken")]
        [InlineData("token")]
        [InlineData("apiKey")]
        [InlineData("clientSecret")]
        [InlineData("resetCode")]
        [InlineData("twoFactorCode")]
        [InlineData("recoveryCode")]
        public void Secret_fields_are_masked(string field)
        {
            var body = $"{{\"email\":\"a@b.test\",\"{field}\":\"Hunter2\\\"x\"}}";

            var sanitized = AuditLoggingMiddleware.SanitizeRequestBody(body);

            Assert.DoesNotContain("Hunter2", sanitized);
            Assert.Contains($"\"{field}\":\"***\"", sanitized);
            Assert.Contains("\"email\":\"a@b.test\"", sanitized);
        }

        [Fact]
        public void Reset_password_body_keeps_the_email_and_masks_the_code_and_password()
        {
            const string body = "{\"email\":\"victim@x.test\",\"resetCode\":\"CfDJ8AbC-_123\",\"newPassword\":\"Weakpass1\"}";

            var sanitized = AuditLoggingMiddleware.SanitizeRequestBody(body);

            Assert.Equal("{\"email\":\"victim@x.test\",\"resetCode\":\"***\",\"newPassword\":\"***\"}", sanitized);
        }

        [Fact]
        public void Ordinary_fields_are_left_alone()
        {
            const string body = "{\"name\":\"Liz\",\"servings\":\"4\"}";

            Assert.Equal(body, AuditLoggingMiddleware.SanitizeRequestBody(body));
        }
    }
}
