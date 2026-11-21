using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The model readers as a lane on the lane frame (lane <c>wording-readers</c>, frame tiers
    /// <c>read</c>, <c>lexicon</c> and <c>alcohol</c>). Each pass takes the in-use uncurated wordings
    /// the deterministic tiers left (no tier answered, or the gate held the answer), most recipes
    /// unblocked first, has readers A and B read each blind on its evidence pack, combines their
    /// answers by the outcome table, puts every food through the link gate, records every decision,
    /// and writes rows only while the frame holds the tier live, through the verified wording loader
    /// with source <c>ai:readers:&lt;hash&gt;</c>, an evidence key and a batch-scoped relink.
    /// </summary>
    public interface IWordingReaderLaneService
    {
        /// <summary>A hash of what decides a tier: the readers' rules and prompts, the gate, the alcohol rules, and the four models.</summary>
        string Signature(string tier);

        /// <summary>
        /// Reads at most <paramref name="max"/> wordings. <paramref name="modes"/> gives each tier's
        /// mode (missing is shadow); <paramref name="remaining"/> the writes each tier may still make
        /// in 24 hours (missing is no cap).
        /// </summary>
        Task<WordingReaderBatchResult> RunBatchAsync(IReadOnlyDictionary<string, string> modes, int max, IReadOnlyDictionary<string, int>? remaining = null, CancellationToken cancellationToken = default);

        /// <summary>Audits a sample of the tier's decisions and evaluates the tier on the frame with its faults and its golden records.</summary>
        Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default);

        /// <summary>The tier's decisions that write in the last 24 hours, against its daily cap.</summary>
        Task<int> WritesTodayAsync(string tier, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One blind reader of the wording lane. It reads evidence packs and answers food (by the
    /// number of a candidate it was shown), none or unclear, with a reason and, for an old, regional
    /// or brand word, what it means; in the fixed-reading mode it gives one reading per line; as the
    /// refuter it looks for a reason a proposed synonym is not the same food.
    /// </summary>
    public interface IWordingReader
    {
        bool IsConfigured { get; }
        string ModelName { get; }

        /// <summary>One answer per pack, in order; null for a pack it gave no readable answer on.</summary>
        Task<IReadOnlyList<ReaderAnswer?>> ReadAsync(IReadOnlyList<WordingPack> packs, CancellationToken cancellationToken = default);

        /// <summary>One reading per line of the pack, in order (a code of <see cref="AlcoholFixedReading.Options"/>); null for a line it gave no readable answer on.</summary>
        Task<IReadOnlyList<string?>> ReadFixedAsync(FixedPack pack, CancellationToken cancellationToken = default);

        /// <summary>A reason the term does not mean the proposed food, or null when it finds none; throws when the reply cannot be read.</summary>
        Task<string?> RefuteAsync(string term, string means, IReadOnlyList<string> lines, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The models the wording reader lane asks: reader A and reader B (of different families, from
    /// the roster), the refuter (reader A's model), and the frame's audit: the auditor (the roster's
    /// auditor, a third family) reading a sampled decision, confirmed by reader B re-reading it
    /// blind. The sentinel is the auditor's same-food judge on moved lines.
    /// </summary>
    public interface IWordingReaders
    {
        bool IsConfigured { get; }
        IWordingReader ReaderA { get; }
        IWordingReader ReaderB { get; }
        IIngredientLinkVerifier Sentinel { get; }
        string AuditorModel { get; }
        string CheckModel { get; }
        ILaneAuditor Auditor { get; }
        ILaneAuditCheck Check { get; }
    }
}
