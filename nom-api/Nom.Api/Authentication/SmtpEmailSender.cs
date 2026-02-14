using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Nom.Api.Authentication
{
    public class SmtpEmailSender : IEmailSender<IdentityUser>
    {
        private readonly ILogger<SmtpEmailSender> _logger;
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUser;
        private readonly string _smtpPassword;
        private readonly string _fromAddress;
        private readonly string _fromName;
        private readonly bool _useSsl;

        public SmtpEmailSender(ILogger<SmtpEmailSender> logger, IConfiguration configuration)
        {
            _logger = logger;
            _smtpHost = configuration["Email:SmtpHost"] ?? "localhost";
            _smtpPort = int.TryParse(configuration["Email:SmtpPort"], out var port) ? port : 587;
            _smtpUser = configuration["Email:SmtpUser"] ?? string.Empty;
            _smtpPassword = configuration["Email:SmtpPassword"] ?? string.Empty;
            _fromAddress = configuration["Email:FromAddress"] ?? "noreply@nom.local";
            _fromName = configuration["Email:FromName"] ?? "NOM";
            _useSsl = !bool.TryParse(configuration["Email:UseSsl"], out var ssl) || ssl;
        }

        public async Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
        {
            var subject = "Confirm your NOM account";
            var body = $@"
<html>
<body>
<h2>Welcome to NOM!</h2>
<p>Please confirm your email address by clicking the link below:</p>
<p><a href=""{confirmationLink}"">Confirm Email Address</a></p>
<p>If you did not create an account, you can safely ignore this email.</p>
</body>
</html>";

            await SendEmailAsync(email, subject, body);
        }

        public async Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink)
        {
            var subject = "Reset your NOM password";
            var body = $@"
<html>
<body>
<h2>Password Reset Request</h2>
<p>You requested a password reset. Click the link below to reset your password:</p>
<p><a href=""{resetLink}"">Reset Password</a></p>
<p>If you did not request this, you can safely ignore this email.</p>
</body>
</html>";

            await SendEmailAsync(email, subject, body);
        }

        public async Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode)
        {
            var subject = "Your NOM password reset code";
            var body = $@"
<html>
<body>
<h2>Password Reset Code</h2>
<p>Your password reset code is:</p>
<h3>{resetCode}</h3>
<p>If you did not request this, you can safely ignore this email.</p>
</body>
</html>";

            await SendEmailAsync(email, subject, body);
        }

        private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            _logger.LogInformation("Sending email to {Email}: {Subject}", toEmail, subject);

            using var message = new MailMessage();
            message.From = new MailAddress(_fromAddress, _fromName);
            message.To.Add(new MailAddress(toEmail));
            message.Subject = subject;
            message.Body = htmlBody;
            message.IsBodyHtml = true;

            using var client = new SmtpClient(_smtpHost, _smtpPort);
            client.EnableSsl = _useSsl;

            if (!string.IsNullOrEmpty(_smtpUser))
            {
                client.Credentials = new NetworkCredential(_smtpUser, _smtpPassword);
            }

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent successfully to {Email}", toEmail);
        }
    }
}
