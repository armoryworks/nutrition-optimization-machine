using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// General-purpose outbound email (admin notifications etc.), as opposed to
    /// the Identity-bound IEmailSender used for account flows. Implemented in
    /// Nom.Api against the same SMTP settings; a no-op when email is not
    /// configured. Registered explicitly in Program.cs (implementation lives
    /// outside the Nom.Orch convention-scan namespaces).
    /// </summary>
    public interface ISystemEmailService
    {
        Task SendAsync(string toEmail, string subject, string htmlBody);
    }
}
