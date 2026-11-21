using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The wording reader lane's models on the batch model host (Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl): reader A and reader B of the roster (<see cref="OllamaWordingReader"/>), the
    /// link lane's same-food judge (<see cref="OllamaIngredientLinkVerifier"/>) with the roster's
    /// auditor model as the sentinel and the auditor of food and lexicon decisions, and the same
    /// auditor model as a fixed reader as the auditor of alcohol decisions. A decision is wrong when
    /// the auditor calls the pair a variety, a different food or several foods, or reads a sampled
    /// line another way than the decision; the flag is not confirmed by reader B, which made the
    /// decision with reader A (<see cref="AuditorFlagStands"/>). A decision with no food (none or
    /// unclear) is not audited.
    /// </summary>
    public sealed class OllamaWordingReaders : IWordingReaders
    {
        private readonly OllamaIngredientLinkVerifier _auditor;
        private readonly OllamaWordingReader _fixedAuditor;

        public OllamaWordingReaders(HttpClient http, IConfiguration configuration, IOptions<LaneModelRoster> roster)
        {
            var host = configuration["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : configuration["Ai:OllamaUrl"] ?? string.Empty;
            var strict = configuration.GetValue("Ai:StrictSchema", true);
            ReaderA = new OllamaWordingReader(http, host, roster.Value.ReaderA, strict);
            ReaderB = new OllamaWordingReader(http, host, roster.Value.ReaderB, strict);
            _auditor = new OllamaIngredientLinkVerifier(http, host, roster.Value.Auditor, strict);
            _fixedAuditor = new OllamaWordingReader(http, host, roster.Value.Auditor, strict);
            Auditor = new PairReader(_auditor);
            FixedAuditor = new FixedReader(_fixedAuditor);
        }

        public bool IsConfigured => ReaderA.IsConfigured && ReaderB.IsConfigured && _auditor.IsConfigured && _fixedAuditor.IsConfigured;
        public IWordingReader ReaderA { get; }
        public IWordingReader ReaderB { get; }
        public IIngredientLinkVerifier Sentinel => _auditor;
        public string AuditorModel => _auditor.ModelName;
        public ILaneAuditor Auditor { get; }
        public ILaneAuditor FixedAuditor { get; }

        /// <summary>The same-food judge reading one reader decision's pair (the wording, or a lexicon term, against the food's USDA description, or what the term means).</summary>
        public sealed class PairReader : ILaneAuditor
        {
            private readonly IIngredientLinkVerifier _judge;

            public PairReader(IIngredientLinkVerifier judge) => _judge = judge;

            public string Model => _judge.ModelName;

            public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<WordingReaderSignals>(decision.SignalsJson);
                if (signals?.FoodText == null) return new LaneAuditAnswer(null, "no readable answer");
                var judgement = (await _judge.JudgeAsync(new[] { new LinkPair(0, signals.Wording, signals.FoodText) }, cancellationToken)).FirstOrDefault()?.Judgement;
                return judgement switch
                {
                    null or LinkJudgement.Unsure => new LaneAuditAnswer(null, judgement == null ? "no readable answer" : "unsure"),
                    LinkJudgement.Same => new LaneAuditAnswer(false, "same"),
                    _ => new LaneAuditAnswer(true, judgement.ToString()),
                };
            }
        }

        /// <summary>
        /// The auditor in the fixed-reading mode reading an alcohol decision's sampled lines, each
        /// with its recipe's title, other lines and steps, as the readers read them: wrong when it
        /// reads any line another way than the decision, right when it reads every line the same,
        /// no answer when it is unsure or unreadable on any line.
        /// </summary>
        public sealed class FixedReader : ILaneAuditor
        {
            private readonly IWordingReader _reader;

            public FixedReader(IWordingReader reader) => _reader = reader;

            public string Model => _reader.ModelName;

            public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<WordingReaderSignals>(decision.SignalsJson);
                if (signals?.Mode == null || signals.Reading == null || signals.FixedSample is not { Count: > 0 } sample)
                    return new LaneAuditAnswer(null, "the decision keeps no sampled lines");
                var readings = await _reader.ReadFixedAsync(new FixedPack(signals.Wording, signals.Mode, sample), cancellationToken);
                if (readings.Count != sample.Count || readings.Any(r => r == null || r == AlcoholFixedReading.Unsure))
                    return new LaneAuditAnswer(null, "unsure or no readable answer on a line");
                var other = readings.Where(r => r != signals.Reading).ToList();
                return other.Count == 0
                    ? new LaneAuditAnswer(false, signals.Reading)
                    : new LaneAuditAnswer(true, $"read {string.Join(", ", other.Distinct())}, not {signals.Reading}");
            }
        }
    }

    /// <summary>
    /// The audit check of a lane whose deciding models may not check the auditor: the auditor's
    /// flag stands as the error, and the frame corrects the audited error with the auditor's own
    /// golden sensitivity and false flags (plan C0.2, C0.3).
    /// </summary>
    public sealed class AuditorFlagStands : ILaneAuditCheck
    {
        public static readonly AuditorFlagStands Instance = new();

        public string Name => "the auditor alone";

        public Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) => Task.FromResult<bool?>(true);
    }
}
