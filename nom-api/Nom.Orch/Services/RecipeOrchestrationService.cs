// Nom.Orch/Services/RecipeOrchestrationService.cs
using Nom.Orch.Models.Recipe; // Reference KaggleRawRecipeDataModel, RecipeImportRequest/Response
using Nom.Data; // For ApplicationDbContext
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using System;
using Nom.Data.Audit; // For ImportJobEntity, ImportStatusEnum
using Nom.Orch.Interfaces; // For IRecipeOrchestrationService
using Nom.Orch.UtilityInterfaces; // For IKaggleRecipeIngestionService
using Microsoft.Extensions.DependencyInjection; // For IServiceScopeFactory
using Microsoft.EntityFrameworkCore; // For FirstOrDefaultAsync

namespace Nom.Orch.Services
{
    /// <summary>
    /// Service responsible for orchestrating high-level recipe-related operations,
    /// primarily initiating and managing the lifecycle of data import jobs.
    /// It delegates the detailed ingestion process to specialized services.
    /// </summary>
    public class RecipeOrchestrationService : IRecipeOrchestrationService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory; // Needed to create scopes for background tasks
        private readonly ILogger<RecipeOrchestrationService> _logger;

        public RecipeOrchestrationService(IServiceScopeFactory serviceScopeFactory, ILogger<RecipeOrchestrationService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }


    }
}
