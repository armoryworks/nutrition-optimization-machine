// File: Nom.Api/Authentication/NoOpEmailSender.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// A "no-op" implementation of IEmailSender for development purposes.
    /// It satisfies the dependency injection requirement for ASP.NET Core Identity
    /// without actually sending emails. Instead, it logs the email details to the console.
    /// </summary>
    public class NoOpEmailSender : IEmailSender<IdentityUser>
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
        {
            _logger = logger;
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
            _logger.LogInformation("--- SIMULATED EMAIL (Password Reset Link) ---");
            _logger.LogInformation("To: {email}", email);
            _logger.LogInformation("User: {userId}", user.Id);
            _logger.LogInformation("Link: {link}", resetLink);
            _logger.LogInformation("---------------------------------------------");
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
        {
            _logger.LogInformation("--- SIMULATED EMAIL (Password Reset Code) ---");
            _logger.LogInformation("To: {email}", email);
            _logger.LogInformation("User: {userId}", user.Id);
            _logger.LogInformation("Code: {code}", resetCode);
            _logger.LogInformation("---------------------------------------------");
            return Task.CompletedTask;
        }
    }
}
