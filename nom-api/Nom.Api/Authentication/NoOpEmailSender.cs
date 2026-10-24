// File: Nom.Api/Authentication/NoOpEmailSender.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Api.Settings;
using System.Threading.Tasks;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// A "no-op" implementation of IEmailSender for development purposes.
    /// It satisfies the dependency injection requirement for ASP.NET Core Identity
    /// without actually sending emails. Instead, it logs the email details to the console.
    /// In Production it never logs a password-reset link or code, only that one was not sent.
    /// </summary>
    public class NoOpEmailSender : IEmailSender<IdentityUser>
    {
        private readonly ILogger<NoOpEmailSender> _logger;
        private readonly FrontendSettings _frontend;
        private readonly bool _isProduction;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger, IOptions<FrontendSettings> frontend, IHostEnvironment environment)
        {
            _logger = logger;
            _frontend = frontend.Value;
            _isProduction = environment.IsProduction();
        }

        public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
        {
            _logger.LogInformation("--- SIMULATED EMAIL (Confirmation Link) ---");
            _logger.LogInformation("To: {email}", email);
            _logger.LogInformation("User: {userId}", user.Id);
            _logger.LogInformation("Link: {link}", confirmationLink);
            _logger.LogInformation("-------------------------------------------");
            return Task.CompletedTask;
        }

        public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink)
        {
            if (_isProduction)
                return WarnResetNotSent(user);

            _logger.LogInformation("--- SIMULATED EMAIL (Password Reset Link) ---");
            _logger.LogInformation("To: {email}", email);
            _logger.LogInformation("User: {userId}", user.Id);
            _logger.LogInformation("Link: {link}", resetLink);
            _logger.LogInformation("---------------------------------------------");
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
        {
            if (_isProduction)
                return WarnResetNotSent(user);

            _logger.LogInformation("--- SIMULATED EMAIL (Password Reset Code) ---");
            _logger.LogInformation("To: {email}", email);
            _logger.LogInformation("User: {userId}", user.Id);
            _logger.LogInformation("Link: {link}", PasswordResetEmail.Link(_frontend.Url, email, resetCode));
            _logger.LogInformation("---------------------------------------------");
            return Task.CompletedTask;
        }

        private Task WarnResetNotSent(IdentityUser user)
        {
            _logger.LogWarning("Password reset requested for user {userId}, but Email:SmtpHost is not configured, so no email was sent.", user.Id);
            return Task.CompletedTask;
        }
    }
}
