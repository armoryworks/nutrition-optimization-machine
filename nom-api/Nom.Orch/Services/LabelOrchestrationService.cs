using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Label;

namespace Nom.Orch.Services
{
    public class LabelOrchestrationService : ILabelOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<LabelOrchestrationService> _logger;

        public LabelOrchestrationService(ApplicationDbContext db, ILogger<LabelOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<LabelResponseModel>> GetLabelsAsync()
        {
            return await _db.References
                .Include(r => r.Groups)
                .AsNoTracking()
                .Select(r => new LabelResponseModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Color = r.Description,
                    GroupName = r.Groups != null ? r.Groups.Select(g => g.Name).FirstOrDefault() : null
                })
                .ToListAsync();
        }

        public async Task<long> CreateLabelAsync(LabelCreateModel model)
        {
            var entity = new ReferenceEntity
            {
                Name = model.Name,
                Description = model.Color
            };

            _db.References.Add(entity);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created label {LabelId} with name {Name}", entity.Id, model.Name);
            return entity.Id;
        }

        public async Task<LabelResponseModel?> UpdateLabelAsync(long id, LabelCreateModel model)
        {
            var entity = await _db.References
                .Include(r => r.Groups)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (entity == null) return null;

            entity.Name = model.Name;
            entity.Description = model.Color;

            await _db.SaveChangesAsync();

            return new LabelResponseModel
            {
                Id = entity.Id,
                Name = entity.Name,
                Color = entity.Description,
                GroupName = entity.Groups?.Select(g => g.Name).FirstOrDefault()
            };
        }

        public async Task<bool> DeleteLabelAsync(long id)
        {
            var entity = await _db.References.FindAsync(id);
            if (entity == null) return false;

            _db.References.Remove(entity);
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
