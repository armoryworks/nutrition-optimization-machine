using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>A line row as the line judge reads it: the food, the amount and its unit, and the kind when it is not an ingredient.</summary>
    public sealed record LineJudgeRow(string Food, decimal Quantity, string? Unit, string? Kind = null);

    /// <summary>
    /// One changed line for the judge: the recipe's name, the line as the source states it, the
    /// method sentences that name its food (empty when none), and the row before and after a
    /// rebuild. <see cref="Old"/> is null for a line the rebuild added.
    /// </summary>
    public sealed record LineJudgeCase(int Id, string Recipe, string SourceLine, string Method, LineJudgeRow? Old, LineJudgeRow New);

    /// <summary>The judge's answer on one case.</summary>
    public enum LineJudgement
    {
        Better,
        Same,
        Worse,
        Unclear,
    }

    public sealed record LineJudgeVerdict(int Id, LineJudgement Judgement);

    /// <summary>
    /// The line judge of the reprocess lane: a model of another family than the writers (reader B
    /// of the roster, mistral-nemo by default), asked whether a rebuilt line row reads its source
    /// line better than, as well as or worse than the row it replaces. It checks; it never writes a
    /// row and never supplies an amount.
    /// </summary>
    public interface IReprocessLineJudge
    {
        bool IsConfigured { get; }

        string ModelName { get; }

        Task<IReadOnlyList<LineJudgeVerdict>> JudgeAsync(IReadOnlyList<LineJudgeCase> cases, CancellationToken cancellationToken = default);
    }
}
