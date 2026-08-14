using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Communication;
using Nom.Orch.Models.Recipe;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    public class ScrapingSourceService : IScrapingSourceService
    {
        private const string AdminClaimType = "CanManageCuration";

        private readonly ApplicationDbContext _db;
        private readonly ICommunicationOrchestrationService _communication;
        private readonly ISystemEmailService _email;
        private readonly ILogger<ScrapingSourceService> _logger;

        public ScrapingSourceService(
            ApplicationDbContext db,
            ICommunicationOrchestrationService communication,
            ISystemEmailService email,
            ILogger<ScrapingSourceService> logger)
        {
            _db = db;
            _communication = communication;
            _email = email;
            _logger = logger;
        }

        public static string? ExtractDomain(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.Host.ToLowerInvariant();
            }

            return null;
        }

        public async Task<ScrapingSourceStatusEnum?> GetDomainStatusAsync(string url)
        {
            var domain = ExtractDomain(url);
            if (domain == null)
            {
                return null;
            }

            var source = await _db.ScrapingSources
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Domain == domain && !s.IsDeleted);

            return source?.Status;
        }

        public async Task<ScrapingSourceModel> RequestSourceAsync(string url, long? requestedByPersonId, string? note = null)
        {
            var domain = ExtractDomain(url)
                ?? throw new ArgumentException("Not a valid http(s) URL.", nameof(url));

            var existing = await _db.ScrapingSources
                .FirstOrDefaultAsync(s => s.Domain == domain && !s.IsDeleted);
            if (existing != null)
            {
                return await MapAsync(existing);
            }

            var source = new ScrapingSourceEntity
            {
                Domain = domain,
                Status = ScrapingSourceStatusEnum.Pending,
                SampleUrl = url,
                Notes = note,
                RequestedByPersonId = requestedByPersonId,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = requestedByPersonId,
            };
            _db.ScrapingSources.Add(source);
            await _db.SaveChangesAsync();

            _logger.LogInformation("New scraping source request for {Domain} by person {PersonId}", domain, requestedByPersonId);

            await NotifyAdminsAsync(source, requestedByPersonId);

            return await MapAsync(source);
        }

        public async Task<ScrapingSourceModel> RegisterAutoApprovedSourceAsync(string url, string reason)
        {
            var domain = ExtractDomain(url)
                ?? throw new ArgumentException("Not a valid http(s) URL.", nameof(url));

            var existing = await _db.ScrapingSources
                .FirstOrDefaultAsync(s => s.Domain == domain && !s.IsDeleted);
            if (existing != null)
            {
                // Never override a human decision (or an earlier automated one).
                return await MapAsync(existing);
            }

            var source = new ScrapingSourceEntity
            {
                Domain = domain,
                Status = ScrapingSourceStatusEnum.Approved,
                SampleUrl = url,
                Notes = reason,
                // ReviewedByPersonId stays null: the review was automated.
                ReviewedDate = DateTime.UtcNow,
                CreatedDate = DateTime.UtcNow,
            };
            _db.ScrapingSources.Add(source);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Scraping source {Domain} auto-whitelisted by discovery", domain);

            await NotifyAdminsAutoApprovedAsync(source);

            return await MapAsync(source);
        }

        public async Task<List<ScrapingSourceModel>> ListAsync(ScrapingSourceStatusEnum? status)
        {
            var query = _db.ScrapingSources
                .Include(s => s.RequestedByPerson)
                .Include(s => s.ReviewedByPerson)
                .Where(s => !s.IsDeleted);

            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            var sources = await query
                .OrderByDescending(s => s.CreatedDate)
                .AsNoTracking()
                .ToListAsync();

            return sources.Select(Map).ToList();
        }

        public Task<ScrapingSourceModel?> ApproveAsync(long id, long reviewerPersonId, string? notes)
            => ReviewAsync(id, reviewerPersonId, notes, ScrapingSourceStatusEnum.Approved);

        public Task<ScrapingSourceModel?> RejectAsync(long id, long reviewerPersonId, string? notes)
            => ReviewAsync(id, reviewerPersonId, notes, ScrapingSourceStatusEnum.Rejected);

        private async Task<ScrapingSourceModel?> ReviewAsync(
            long id, long reviewerPersonId, string? notes, ScrapingSourceStatusEnum newStatus)
        {
            var source = await _db.ScrapingSources.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
            if (source == null)
            {
                return null;
            }

            source.Status = newStatus;
            source.ReviewedByPersonId = reviewerPersonId;
            source.ReviewedDate = DateTime.UtcNow;
            source.Notes = notes ?? source.Notes;
            source.LastModifiedDate = DateTime.UtcNow;
            source.LastModifiedByPersonId = reviewerPersonId;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Scraping source {Domain} {Status} by person {PersonId}",
                source.Domain, newStatus, reviewerPersonId);

            return await MapAsync(source);
        }

        /// <summary>
        /// Notifies every curation admin (holders of the CanManageCuration claim)
        /// via an in-app message thread and, when they have an email address, email.
        /// Notification failures are logged but never block the request itself.
        /// </summary>
        private async Task NotifyAdminsAsync(ScrapingSourceEntity source, long? requestedByPersonId)
        {
            try
            {
                var adminUserIds = await _db.UserClaims
                    .Where(c => c.ClaimType == AdminClaimType && c.ClaimValue == "true")
                    .Select(c => c.UserId)
                    .Distinct()
                    .ToListAsync();

                var admins = await _db.Persons
                    .Where(p => p.UserId != null && adminUserIds.Contains(p.UserId))
                    .Select(p => new { p.Id, p.Name, p.Email })
                    .ToListAsync();

                if (admins.Count == 0)
                {
                    _logger.LogWarning("No curation admins found to notify about scraping source {Domain}", source.Domain);
                    return;
                }

                var summary =
                    $"New scraping source awaiting review: {source.Domain}\n" +
                    $"Requested via: {source.SampleUrl}\n\n" +
                    "No scraping will happen until this domain is approved. Approving it means you accept " +
                    "responsibility for the legality and quality of importing from this site. " +
                    "Review it under Admin → Scraping Sources.";

                // In-app: one thread from the requester (or the system person) to all admins.
                var creatorPersonId = requestedByPersonId ?? SystemConstants.SystemPersonId;
                var threadId = await _communication.CreateThreadAsync(new CreateThreadRequest
                {
                    ParticipantIds = admins.Select(a => a.Id).ToArray(),
                }, creatorPersonId);
                await _communication.SendMessageAsync(new SendMessageRequest
                {
                    ThreadId = threadId,
                    Content = summary,
                }, creatorPersonId);

                // Email every admin with an address.
                var subject = $"NOM: scraping source \"{source.Domain}\" awaits approval";
                var body = $@"
<html><body>
<h2>New scraping source request</h2>
<p><strong>Domain:</strong> {source.Domain}</p>
<p><strong>Requested via:</strong> <a href=""{source.SampleUrl}"">{source.SampleUrl}</a></p>
<p>No scraping will happen until this domain is approved. Approving it means you accept
responsibility for the legality and quality of importing recipes from this site.</p>
<p>Review it in NOM under <strong>Admin &rarr; Scraping Sources</strong>.</p>
</body></html>";

                foreach (var admin in admins.Where(a => !string.IsNullOrWhiteSpace(a.Email)))
                {
                    await _email.SendAsync(admin.Email!, subject, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify admins about scraping source {Domain}", source.Domain);
            }
        }

        /// <summary>
        /// Notifies curation admins that discovery auto-whitelisted a domain,
        /// with a pointer to where the decision can be reversed. Failures are
        /// logged but never block the approval itself.
        /// </summary>
        private async Task NotifyAdminsAutoApprovedAsync(ScrapingSourceEntity source)
        {
            try
            {
                var adminUserIds = await _db.UserClaims
                    .Where(c => c.ClaimType == AdminClaimType && c.ClaimValue == "true")
                    .Select(c => c.UserId)
                    .Distinct()
                    .ToListAsync();

                var admins = await _db.Persons
                    .Where(p => p.UserId != null && adminUserIds.Contains(p.UserId))
                    .Select(p => new { p.Id, p.Name, p.Email })
                    .ToListAsync();

                if (admins.Count == 0)
                {
                    _logger.LogWarning("No curation admins found to notify about auto-whitelisted source {Domain}", source.Domain);
                    return;
                }

                var summary =
                    $"Source discovery auto-whitelisted a new domain: {source.Domain}\n" +
                    $"Evidence: {source.SampleUrl}\n" +
                    $"{source.Notes}\n\n" +
                    "Scraping from this domain is now enabled. If it should not be trusted, " +
                    "reject it under Admin → Scraping Sources — rejected domains are never re-proposed.";

                var threadId = await _communication.CreateThreadAsync(new CreateThreadRequest
                {
                    ParticipantIds = admins.Select(a => a.Id).ToArray(),
                }, SystemConstants.SystemPersonId);
                await _communication.SendMessageAsync(new SendMessageRequest
                {
                    ThreadId = threadId,
                    Content = summary,
                }, SystemConstants.SystemPersonId);

                var subject = $"NOM: scraping source \"{source.Domain}\" was auto-whitelisted";
                var body = $@"
<html><body>
<h2>Source discovery auto-whitelisted a domain</h2>
<p><strong>Domain:</strong> {source.Domain}</p>
<p><strong>Evidence:</strong> <a href=""{source.SampleUrl}"">{source.SampleUrl}</a></p>
<p>{source.Notes}</p>
<p>Scraping from this domain is now <strong>enabled</strong>. If it should not be trusted,
reject it in NOM under <strong>Admin &rarr; Scraping Sources</strong> — rejected domains are
never re-proposed.</p>
</body></html>";

                foreach (var admin in admins.Where(a => !string.IsNullOrWhiteSpace(a.Email)))
                {
                    await _email.SendAsync(admin.Email!, subject, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify admins about auto-whitelisted source {Domain}", source.Domain);
            }
        }

        private async Task<ScrapingSourceModel> MapAsync(ScrapingSourceEntity source)
        {
            await _db.Entry(source).Reference(s => s.RequestedByPerson).LoadAsync();
            await _db.Entry(source).Reference(s => s.ReviewedByPerson).LoadAsync();
            return Map(source);
        }

        private static ScrapingSourceModel Map(ScrapingSourceEntity source) => new()
        {
            Id = source.Id,
            Domain = source.Domain,
            Status = source.Status.ToString(),
            SampleUrl = source.SampleUrl,
            RequestedByName = source.RequestedByPerson?.Name,
            CreatedDate = source.CreatedDate,
            ReviewedByName = source.ReviewedByPerson?.Name,
            ReviewedDate = source.ReviewedDate,
            Notes = source.Notes,
        };
    }
}
