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
        public void Secret_fields_are_masked(string field)
        {
            var body = $"{{\"email\":\"a@b.test\",\"{field}\":\"Hunter2\\\"x\"}}";

            var sanitized = AuditLoggingMiddleware.SanitizeRequestBody(body);

            Assert.DoesNotContain("Hunter2", sanitized);
            Assert.Contains($"\"{field}\":\"***\"", sanitized);
            Assert.Contains("\"email\":\"a@b.test\"", sanitized);
        }

        [Fact]
        public void Ordinary_fields_are_left_alone()
        {
            const string body = "{\"name\":\"Liz\",\"servings\":\"4\"}";

            Assert.Equal(body, AuditLoggingMiddleware.SanitizeRequestBody(body));
        }
    }
}
