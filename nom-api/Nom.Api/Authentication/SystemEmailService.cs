using Microsoft.Extensions.Options;
using Nom.Api.Settings;
using Nom.Orch.UtilityInterfaces;
using System.Net;
using System.Net.Mail;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// SMTP implementation of the general-purpose system email sender, sharing
    /// the EmailSettings used by the Identity email flows.
    /// </summary>
    public class SmtpSystemEmailService : ISystemEmailService
    {
        private readonly ILogger<SmtpSystemEmailService> _logger;
        private readonly EmailSettings _settings;

        public SmtpSystemEmailService(ILogger<SmtpSystemEmailService> logger, IOptions<EmailSettings> settings)
        {
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            _logger.LogInformation("Sending system email to {Email}: {Subject}", toEmail, subject);

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl,
                Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword),
            };

            await client.SendMailAsync(message);
        }
    }

    /// <summary>Used when no SMTP host is configured — logs instead of sending.</summary>
    public class NoOpSystemEmailService : ISystemEmailService
    {
        private readonly ILogger<NoOpSystemEmailService> _logger;

        public NoOpSystemEmailService(ILogger<NoOpSystemEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            _logger.LogInformation("Email disabled — would have sent to {Email}: {Subject}", toEmail, subject);
            return Task.CompletedTask;
        }
    }
}
