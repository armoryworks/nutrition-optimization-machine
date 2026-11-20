using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>Which of the picks put to the hold rules may be applied without a person, and which were held.</summary>
    public sealed record LinkScreenResult(IReadOnlyList<long> Allowed, IReadOnlyList<long> Held);

    /// <summary>
    /// The rails on links the lanes apply on their own: deterministic holds that keep a name match
    /// or a model pick for a person (see <see cref="Nom.Orch.Services.Support.LinkAutoApplyGuard"/>),
    /// and a daily and per-food allowance for name matches like the one model picks have.
    /// </summary>
    public interface ILinkAutoApplyService
    {
        /// <summary>
        /// Pending exact name matches, most-used first, leaving out those already held, those in
        /// <paramref name="skip"/> and those that point at a USDA food in <paramref name="excludeTargets"/>,
        /// and no more for one USDA food than <paramref name="perTargetCap"/> less what
        /// <paramref name="appliedByTarget"/> says it has already taken.
        /// </summary>
        Task<IReadOnlyList<long>> PendingNameMatchesAsync(int count, IReadOnlyCollection<long> skip, IReadOnlyCollection<string> excludeTargets, CancellationToken cancellationToken = default,
            int perTargetCap = int.MaxValue, IReadOnlyDictionary<string, int>? appliedByTarget = null);

        /// <summary>
        /// How many more links whose source starts with <paramref name="sourcePrefix"/> the lane may
        /// apply on its own in the rolling day, and the USDA foods that have already taken their
        /// share; counted from what the System person applied, so a restart does not reset it.
        /// </summary>
        Task<LinkApplyAllowance> AllowanceAsync(string sourcePrefix, int dailyCap, int perTargetCap, CancellationToken cancellationToken = default);

        /// <summary>
        /// Puts the given proposals to the hold rules. A name match or model pick a rule holds is
        /// marked on its reason (" · hold: rule") and stays Pending for a person; one marked before
        /// stays held. Staples, same-name attaches and other sources pass through.
        /// </summary>
        Task<LinkScreenResult> ScreenAsync(IReadOnlyList<long> proposalIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// What the hold rules make of every pending name match and model pick, by rule and by
        /// source, with samples. Writes nothing.
        /// </summary>
        Task<LinkHoldReport> HoldReportAsync(int sampleSize = 50, CancellationToken cancellationToken = default);
    }
}
