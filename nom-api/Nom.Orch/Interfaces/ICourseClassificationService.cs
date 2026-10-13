using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public sealed record CourseClassificationResult(int Candidates, int Classified, int Changed, int Undetermined);

    /// <summary>
    /// Re-derives snack / dessert / both for recipes already treated as one or the other, from the
    /// added-sugar share of their energy (USDA data via linked ingredients). Recipes without enough
    /// nutrition data keep their current classification.
    /// </summary>
    public interface ICourseClassificationService
    {
        Task<CourseClassificationResult> ClassifyAsync(CancellationToken cancellationToken = default);
    }
}
