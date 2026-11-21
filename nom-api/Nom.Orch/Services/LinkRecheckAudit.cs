using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;

namespace Nom.Orch.Services
{
    /// <summary>
    /// A same-food judge reading one link re-check decision blind: for a move, whether the wording
    /// is the food it moved to; for a take-back, whether the wording was the food it left. A move to
    /// another food, variety or form, or more than one food, is wrong; a take-back of the same food
    /// is wrong; "unsure" is no answer. The frame's auditor is the roster's auditor model and its
    /// confirming check the same question put to reader B, so a flag counts only when two families
    /// agree (plan C0.3).
    /// </summary>
    public sealed class LinkRecheckAudit : ILaneAuditor, ILaneAuditCheck
    {
        private readonly IIngredientLinkVerifier _judge;

        public LinkRecheckAudit(IIngredientLinkVerifier judge) => _judge = judge;

        public string Model => _judge.ModelName;

        public string Name => "link-verify " + _judge.ModelName;

        /// <summary>Why the auditor and its confirming reader may not audit together: one is not configured, or both are of one family; null when they may.</summary>
        public static string? Problem(IIngredientLinkVerifier auditor, IIngredientLinkVerifier confirmer)
        {
            if (!auditor.IsConfigured || !confirmer.IsConfigured) return "no model host or model is set for the link auditor or its confirming reader";
            var a = LaneModelRoster.Family(auditor.ModelName);
            return a == LaneModelRoster.Family(confirmer.ModelName) ? $"the link auditor {auditor.ModelName} and its confirming reader {confirmer.ModelName} are both of the {a} family" : null;
        }

        public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            var (wrong, note) = await AnswerAsync(decision, cancellationToken);
            return new LaneAuditAnswer(wrong, note);
        }

        public async Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) =>
            (await AnswerAsync(decision, cancellationToken)).Wrong;

        private async Task<(bool? Wrong, string? Note)> AnswerAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            var signals = LaneJournal.Read<LinkRecheckService.LinkRecheckSignals>(decision.SignalsJson);
            if (signals == null) return (null, "the decision has no signals");
            var move = signals.Outcome == LinkRecheckRules.Move;
            if (!move && signals.Outcome != LinkRecheckRules.Unlink) return (false, "kept");
            var text = move ? signals.TargetText : signals.FoodText;
            if (string.IsNullOrWhiteSpace(text)) return (null, "no food text");

            var wording = signals.Examples.FirstOrDefault() ?? signals.Wording;
            var verdict = (await _judge.JudgeAsync(new[] { new LinkPair(0, wording, text) }, cancellationToken)).FirstOrDefault();
            if (verdict == null || verdict.Judgement == LinkJudgement.Unsure) return (null, "unsure");
            var same = verdict.Judgement == LinkJudgement.Same;
            return (move ? !same : same, $"{verdict.Judgement} for \"{wording}\" on {text}");
        }
    }
}
