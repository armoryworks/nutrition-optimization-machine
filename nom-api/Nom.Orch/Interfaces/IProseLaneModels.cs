using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The writer of the clean-room prose lane: a model that turns a masked method into fact sheets
    /// and writes a method from the sheets alone. It never sees the source's wording: the extractor
    /// sees the masked method, the writer only the sheets, the ingredient names and the dish name.
    /// Both return the model's raw answer, or null when it gave none.
    /// </summary>
    public interface IProseWriter
    {
        string Model { get; }

        Task<string?> ExtractAsync(MaskedMethod method, IReadOnlyList<string> problems, CancellationToken cancellationToken);

        Task<string?> WriteAsync(ProseWriteRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A model that reads a rewrite against its original step by step with the lane's rubric: the
    /// judge of every rewrite before it is written, and the auditor of a sample. Its answers are
    /// verdicts only, never text that is shown.
    /// </summary>
    public interface IProseJudge
    {
        string Model { get; }

        /// <summary>The verdict on each step; null when the answer cannot be read.</summary>
        Task<ProseJudgeVerdict?> JudgeAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, CancellationToken cancellationToken);

        /// <summary>Whether step <paramref name="step"/> of the rewrite, which another reviewer flagged, is wrong; null when the answer cannot be read.</summary>
        Task<bool?> ConfirmAsync(IReadOnlyList<string> original, IReadOnlyList<string> rewrite, int step, CancellationToken cancellationToken);
    }
}
