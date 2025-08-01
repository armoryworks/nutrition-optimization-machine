using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Person;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;

namespace Nom.Orch.Services
{
    public class InvitationOrchestrationService : IInvitationOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<InvitationOrchestrationService> _logger;

        public InvitationOrchestrationService(ApplicationDbContext db, ILogger<InvitationOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<InvitationModel> CreateInvitationAsync(CreateInvitationRequest request)
        {
            _logger.LogInformation("Creating invitation for inviter {InviterPersonId}", request.InviterPersonId);

            var invitation = new InvitationEntity
            {
                Code = GenerateInvitationCode(),
                InviterPersonId = request.InviterPersonId,
                InvitationType = request.InvitationType,
                PlanId = request.PlanId,
                ExpirationDate = request.ExpirationDate,
                Notes = request.Notes,
                IsUsed = false
            };

            _db.Invitations.Add(invitation);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully created invitation {InvitationId} with code {Code}", invitation.Id, invitation.Code);
            return await GetInvitationModelAsync(invitation.Id);
        }

        public async Task<InvitationModel> ClaimInvitationAsync(ClaimInvitationRequest request)
        {
            _logger.LogInformation("Claiming invitation with code {Code} for invitee {InviteePersonId}", request.InvitationCode, request.InviteePersonId);

            var invitation = await _db.Invitations
                .Include(i => i.Inviter)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.Code == request.InvitationCode);

            if (invitation == null)
            {
                throw new KeyNotFoundException($"Invitation with code {request.InvitationCode} not found.");
            }

            if (invitation.IsUsed)
            {
                throw new InvalidOperationException($"Invitation with code {request.InvitationCode} has already been used.");
            }

            if (invitation.ExpirationDate.HasValue && invitation.ExpirationDate.Value < DateTime.UtcNow)
            {
                throw new InvalidOperationException($"Invitation with code {request.InvitationCode} has expired.");
            }

            invitation.InviteePersonId = request.InviteePersonId;
            invitation.IsUsed = true;
            invitation.UsedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully claimed invitation {InvitationId}", invitation.Id);
            return await GetInvitationModelAsync(invitation.Id);
        }

        public async Task<InvitationModel?> GetInvitationByCodeAsync(string code)
        {
            var invitation = await _db.Invitations
                .Include(i => i.Inviter)
                .Include(i => i.Invitee)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.Code == code);

            return invitation != null ? await GetInvitationModelAsync(invitation.Id) : null;
        }

        public async Task<List<InvitationModel>> GetInvitationsByInviterAsync(long inviterPersonId)
        {
            var invitations = await _db.Invitations
                .Include(i => i.Inviter)
                .Include(i => i.Invitee)
                .Include(i => i.Plan)
                .Where(i => i.InviterPersonId == inviterPersonId)
                .OrderByDescending(i => i.CreatedDate)
                .ToListAsync();

            var models = new List<InvitationModel>();
            foreach (var invitation in invitations)
            {
                models.Add(await GetInvitationModelAsync(invitation.Id));
            }

            return models;
        }

        public async Task<List<InvitationModel>> GetInvitationsByInviteeAsync(long inviteePersonId)
        {
            var invitations = await _db.Invitations
                .Include(i => i.Inviter)
                .Include(i => i.Invitee)
                .Include(i => i.Plan)
                .Where(i => i.InviteePersonId == inviteePersonId)
                .OrderByDescending(i => i.CreatedDate)
                .ToListAsync();

            var models = new List<InvitationModel>();
            foreach (var invitation in invitations)
            {
                models.Add(await GetInvitationModelAsync(invitation.Id));
            }

            return models;
        }

        public async Task<bool> ValidateInvitationAsync(string code)
        {
            var invitation = await _db.Invitations
                .FirstOrDefaultAsync(i => i.Code == code);

            if (invitation == null)
                return false;

            if (invitation.IsUsed)
                return false;

            if (invitation.ExpirationDate.HasValue && invitation.ExpirationDate.Value < DateTime.UtcNow)
                return false;

            return true;
        }

        private async Task<InvitationModel> GetInvitationModelAsync(long invitationId)
        {
            var invitation = await _db.Invitations
                .Include(i => i.Inviter)
                .Include(i => i.Invitee)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.Id == invitationId);

            if (invitation == null)
                throw new KeyNotFoundException($"Invitation with ID {invitationId} not found.");

            return new InvitationModel
            {
                Id = invitation.Id,
                Code = invitation.Code,
                InviterPersonId = invitation.InviterPersonId,
                InviterName = invitation.Inviter?.Name ?? "Unknown",
                InviteePersonId = invitation.InviteePersonId,
                InviteeName = invitation.Invitee?.Name,
                ExpirationDate = invitation.ExpirationDate,
                IsUsed = invitation.IsUsed,
                UsedAt = invitation.UsedAt,
                Notes = invitation.Notes,
                InvitationType = invitation.InvitationType,
                PlanId = invitation.PlanId,
                PlanName = invitation.Plan?.Name,
                CreatedDate = invitation.CreatedDate
            };
        }

        private string GenerateInvitationCode()
        {
            // Generate a unique 8-character alphanumeric code
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var code = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());

            // Ensure uniqueness by checking if code already exists
            while (_db.Invitations.Any(i => i.Code == code))
            {
                code = new string(Enumerable.Repeat(chars, 8)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
            }

            return code;
        }
    }
}