using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nom.Api.Settings;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Nom.Api.Authentication
{
    public class SmtpEmailSender : IEmailSender<IdentityUser>
    {
        private readonly ILogger<SmtpEmailSender> _logger;
        private readonly EmailSettings _settings;

        public SmtpEmailSender(ILogger<SmtpEmailSender> logger, IOptions<EmailSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
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
            // Non-production instances redirect every message to one mailbox so a
            // snapshot-loaded environment cannot mail real users. The intended recipient is
            // preserved in the subject and body so the mail is still testable.
            var intendedRecipient = toEmail;
            var redirected = !string.IsNullOrWhiteSpace(_settings.OverrideRecipient);
            if (redirected)
            {
                toEmail = _settings.OverrideRecipient.Trim();
                subject = $"[staging → {intendedRecipient}] {subject}";
                htmlBody =
                    $"<p style=\"font:13px sans-serif;background:#fff3cd;border:1px solid #ffe08a;" +
                    $"padding:8px;border-radius:4px\"><strong>Non-production message.</strong> " +
                    $"Intended recipient: <code>{WebUtility.HtmlEncode(intendedRecipient)}</code>. " +
                    $"Redirected here by Email:OverrideRecipient.</p>{htmlBody}";
            }

            if (redirected)
            {
                _logger.LogInformation(
                    "Redirecting email intended for {Intended} to {Override}: {Subject}",
                    intendedRecipient, toEmail, subject);
            }
            else
            {
                _logger.LogInformation("Sending email to {Email}: {Subject}", toEmail, subject);
            }

            using var message = new MailMessage();
            message.From = new MailAddress(_settings.FromAddress, _settings.FromName);
            message.To.Add(new MailAddress(toEmail));
            message.Subject = subject;
            message.Body = htmlBody;
            message.IsBodyHtml = true;

            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort);
            client.EnableSsl = _settings.UseSsl;

            if (!string.IsNullOrEmpty(_settings.SmtpUser))
            {
                client.Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword);
            }

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent successfully to {Email}", toEmail);
        }
    }
}
