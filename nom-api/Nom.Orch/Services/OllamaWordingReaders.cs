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
    /// Ai:OllamaUrl): reader A and reader B of the roster (<see cref="OllamaWordingReader"/>), and the
    /// link lane's same-food judge (<see cref="OllamaIngredientLinkVerifier"/>) as the sentinel and
    /// auditor with the roster's auditor model, confirmed by the same judge with reader B. A decision
    /// is wrong when the auditor calls the pair a variety, a different food or several foods; the
    /// check confirms on the same answers, clears on "same", and cannot tell on "unsure". A decision
    /// with no food (none or unclear) is not audited.
    /// </summary>
    public sealed class OllamaWordingReaders : IWordingReaders
    {
        private readonly OllamaIngredientLinkVerifier _auditor;
        private readonly OllamaIngredientLinkVerifier _check;

        public OllamaWordingReaders(HttpClient http, IConfiguration configuration, IOptions<LaneModelRoster> roster)
        {
            var host = configuration["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : configuration["Ai:OllamaUrl"] ?? string.Empty;
            var strict = configuration.GetValue("Ai:StrictSchema", true);
            ReaderA = new OllamaWordingReader(http, host, roster.Value.ReaderA, strict);
            ReaderB = new OllamaWordingReader(http, host, roster.Value.ReaderB, strict);
            _auditor = new OllamaIngredientLinkVerifier(http, host, roster.Value.Auditor, strict);
            _check = new OllamaIngredientLinkVerifier(http, host, roster.Value.ReaderB, strict);
            Auditor = new PairReader(_auditor);
            Check = new PairReader(_check);
        }

        public bool IsConfigured => ReaderA.IsConfigured && ReaderB.IsConfigured && _auditor.IsConfigured && _check.IsConfigured;
        public IWordingReader ReaderA { get; }
        public IWordingReader ReaderB { get; }
        public IIngredientLinkVerifier Sentinel => _auditor;
        public string AuditorModel => _auditor.ModelName;
        public string CheckModel => _check.ModelName;
        public ILaneAuditor Auditor { get; }
        public ILaneAuditCheck Check { get; }

        /// <summary>The same-food judge reading one reader decision's pair (the wording, or a lexicon term, against the food's USDA description, or what the term means).</summary>
        public sealed class PairReader : ILaneAuditor, ILaneAuditCheck
        {
            private readonly IIngredientLinkVerifier _judge;

            public PairReader(IIngredientLinkVerifier judge) => _judge = judge;

            public string Model => _judge.ModelName;
            public string Name => _judge.ModelName + " blind re-read";

            private async Task<LinkJudgement?> ReadAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<WordingReaderSignals>(decision.SignalsJson);
                if (signals?.FoodText == null) return null;
                var verdicts = await _judge.JudgeAsync(new[] { new LinkPair(0, signals.Wording, signals.FoodText) }, cancellationToken);
                return verdicts.FirstOrDefault()?.Judgement;
            }

            public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var judgement = await ReadAsync(decision, cancellationToken);
                return judgement switch
                {
                    null or LinkJudgement.Unsure => new LaneAuditAnswer(null, judgement == null ? "no readable answer" : "unsure"),
                    LinkJudgement.Same => new LaneAuditAnswer(false, "same"),
                    _ => new LaneAuditAnswer(true, judgement.ToString()),
                };
            }

            public async Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var judgement = await ReadAsync(decision, cancellationToken);
                return judgement switch
                {
                    null or LinkJudgement.Unsure => null,
                    LinkJudgement.Same => false,
                    _ => true,
                };
            }
        }
    }
}
