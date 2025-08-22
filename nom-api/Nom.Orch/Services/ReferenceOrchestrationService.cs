// File: Nom.Orch/Services/ReferenceOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Reference;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class ReferenceOrchestrationService : IReferenceOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ReferenceOrchestrationService> _logger;

        public ReferenceOrchestrationService(ApplicationDbContext db, ILogger<ReferenceOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }


    }
}