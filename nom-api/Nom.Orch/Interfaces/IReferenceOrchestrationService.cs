// File: Nom.Orch/Interfaces/IReferenceOrchestrationService.cs

using Nom.Orch.Models.Reference;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IReferenceOrchestrationService
    {
        Task<List<ReferenceItemModel>> GetMeasurementTypesAsync();
    }
}