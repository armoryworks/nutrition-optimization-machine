// Nom.Orch/UtilityInterfaces/IReferenceDataSeederService.cs
using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Defines the contract for a service responsible for seeding initial reference data
    /// into the database, such as measurement types and core nutrient definitions.
    /// </summary>
    public interface IReferenceDataSeederService
    {
        /// <summary>
        /// Ensures that essential reference data (like common measurement types and core nutrients)
        /// are present in the database. This method is idempotent, meaning it can be run multiple times
        /// without duplicating existing data.
        /// </summary>
        Task SeedReferenceDataAsync();
    }
}
