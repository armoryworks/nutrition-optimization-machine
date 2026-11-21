using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The wording tier lane's audit readings, both the same-food judge of the link lane
    /// (<see cref="OllamaIngredientLinkVerifier"/>, prompt version
    /// <see cref="OllamaIngredientLinkVerifier.CurrentPromptVersion"/>) on the batch model host: the
    /// auditor with the roster's auditor model, the second check with reader B. A decision is wrong
    /// when the auditor calls the pair a variety, a different food or several foods; the check
    /// confirms it on the same answers, clears it on "same", and cannot tell on "unsure".
    /// </summary>
    public sealed class OllamaWordingTierJudges : IWordingTierJudges
    {
        private readonly OllamaIngredientLinkVerifier _auditor;
        private readonly OllamaIngredientLinkVerifier _check;

        public OllamaWordingTierJudges(HttpClient http, IConfiguration configuration, IOptions<LaneModelRoster> roster)
        {
            _auditor = new OllamaIngredientLinkVerifier(http, new Overlay(configuration, roster.Value.Auditor));
            _check = new OllamaIngredientLinkVerifier(http, new Overlay(configuration, roster.Value.ReaderB));
            Auditor = new PairReader(_auditor);
            Check = new PairReader(_check);
        }

        public bool IsConfigured => _auditor.IsConfigured && _check.IsConfigured;
        public string AuditorModel => _auditor.ModelName;
        public string CheckModel => _check.ModelName;
        public ILaneAuditor Auditor { get; }
        public ILaneAuditCheck Check { get; }

        private sealed class PairReader : ILaneAuditor, ILaneAuditCheck
        {
            private readonly OllamaIngredientLinkVerifier _judge;

            public PairReader(OllamaIngredientLinkVerifier judge) => _judge = judge;

            public string Model => _judge.ModelName;
            public string Name => _judge.ModelName + " blind re-read";

            private async Task<LinkJudgement?> ReadAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<WordingTierSignals>(decision.SignalsJson);
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

        private sealed class Overlay : IConfiguration
        {
            private const string ModelKey = "IngredientLinking:VerifyModel";
            private readonly IConfiguration _inner;
            private readonly string _model;

            public Overlay(IConfiguration inner, string model)
            {
                _inner = inner;
                _model = model;
            }

            public string? this[string key]
            {
                get => key == ModelKey ? _model : _inner[key];
                set => _inner[key] = value;
            }

            public IConfigurationSection GetSection(string key) => _inner.GetSection(key);
            public IEnumerable<IConfigurationSection> GetChildren() => _inner.GetChildren();
            public IChangeToken GetReloadToken() => _inner.GetReloadToken();
        }
    }
}
