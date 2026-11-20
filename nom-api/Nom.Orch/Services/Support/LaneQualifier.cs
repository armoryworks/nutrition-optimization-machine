using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nom.Data.Curation;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The bars of one lane or tier. <see cref="QualifyBar"/> is the Wilson 95 % lower bound the
    /// audited precision must reach before the lane writes; <see cref="PauseBar"/> is the error
    /// rate that pauses it when the observed error of the last <see cref="PauseWindow"/> audited
    /// writes is above it.
    /// A pause bar at or under 1 − <see cref="QualifyBar"/> is refused, since a lane that just
    /// qualified would pause at once.
    /// </summary>
    public sealed record LanePolicy(
        double QualifyBar,
        double PauseBar,
        int PauseWindow = 200,
        int MinimumAudited = 0,
        bool ResumeNeedsNewSignature = false)
    {
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            if (QualifyBar is <= 0 or >= 1) problems.Add("the qualify bar must lie between 0 and 1");
            if (PauseBar is <= 0 or >= 1) problems.Add("the pause bar must lie between 0 and 1");
            if (PauseWindow < 1) problems.Add("the pause window must hold at least one audited write");
            if (problems.Count == 0 && PauseBar <= 1 - QualifyBar)
                problems.Add(string.Create(CultureInfo.InvariantCulture,
                    $"the pause bar {PauseBar:P1} is not above the error the qualify bar allows ({1 - QualifyBar:P1}), so a lane that qualifies would pause at once"));
            return problems;
        }
    }

    /// <summary>
    /// How well the combined audit check (the auditor's flag confirmed by a second, independent
    /// check) scored on the golden rows: known-bad rows it flagged (sensitivity) and known-good
    /// rows it flagged (false flags). <see cref="Exact"/> is for a check that is a rule with no
    /// sampling error, such as a rule audit replayed in full.
    /// </summary>
    public sealed record AuditorScore(int KnownBad, int FlaggedBad, int KnownGood, int FlaggedGood, string? Source = null, bool Exact = false)
    {
        public static readonly AuditorScore ExactRule = new(0, 0, 0, 0, "rule", true);

        public double Sensitivity => Exact ? 1 : KnownBad == 0 ? 0 : (double)FlaggedBad / KnownBad;

        public double FalseFlagRate => Exact ? 0 : KnownGood == 0 ? 0 : (double)FlaggedGood / KnownGood;

        /// <summary>Why the score cannot correct an error estimate; null when it can.</summary>
        public string? Problem =>
            Exact ? null
            : KnownBad <= 0 || KnownGood <= 0 ? "the audit check has no score on golden known-bad and known-good rows"
            : FlaggedBad > KnownBad || FlaggedGood > KnownGood ? "the audit check's score counts more flags than rows"
            : Sensitivity <= FalseFlagRate ? "the audit check flags known-good rows as often as known-bad ones"
            : null;
    }

    /// <summary>
    /// An error rate from an audited sample: the share of confirmed errors, the same corrected for
    /// the audit check's sensitivity and false flags (Rogan-Gladen), and conservative 95 % bounds
    /// of the corrected rate. The upper bound never credits more false flags than errors were
    /// confirmed, so with no confirmed error no audit check bounds the error below an exact check.
    /// </summary>
    public sealed record ErrorEstimate(int Audited, int Errors, double Observed, double Corrected, double Lower, double Upper)
    {
        /// <summary>
        /// The corrected error the pause test reads: for an exact check the observed share; for a
        /// sampled one the confirmed share less no more false flags than the check's own lower
        /// false-flag bound and no more than were confirmed, divided by its lower sensitivity bound,
        /// so a window is never made to look better than the check can vouch for.
        /// </summary>
        public double Pausing { get; init; } = Corrected;
    }

    /// <summary>Counted decisions: all, written, audited (pass, flagged or error), confirmed errors, unconfirmed flags, and the newest audited one.</summary>
    public sealed record LaneCounts(int Decisions, int Written, int Audited, int Errors, int Flagged, DateTime? NewestAudited = null)
    {
        public static readonly LaneCounts None = new(0, 0, 0, 0, 0);
    }

    /// <summary>What the frame knows when it evaluates a lane or tier.</summary>
    public sealed record LaneQualifyInput(
        string Mode,
        string Signature,
        bool SignatureChanged,
        string? PausedSignature,
        LanePolicy Policy,
        AuditorScore? Auditor,
        string? GoldenProblem,
        IReadOnlyList<string> PauseReasons,
        LaneCounts Qualify,
        LaneCounts Recent);

    /// <summary>The figures of one evaluation, stored with the lane state and shown on the Pipeline page.</summary>
    public sealed record LaneFigures
    {
        public DateTime EvaluatedAt { get; init; }
        public string? Signature { get; init; }
        public int Decisions { get; init; }
        public int Written { get; init; }
        public int Audited { get; init; }
        public int Errors { get; init; }
        public int Flagged { get; init; }
        public double ObservedError { get; init; }
        public double CorrectedError { get; init; }
        public double PrecisionLower { get; init; }
        public double PrecisionUpper { get; init; }
        public double QualifyBar { get; init; }
        public int? NeededAudited { get; init; }
        public int WindowAudited { get; init; }
        public int WindowErrors { get; init; }
        public double WindowError { get; init; }
        public double WindowErrorLower { get; init; }
        public double WindowErrorUpper { get; init; }
        public double PauseBar { get; init; }
        public int Window { get; init; }
        public string? AuditorSource { get; init; }
        public double? AuditorSensitivity { get; init; }
        public double? AuditorFalseFlagRate { get; init; }
        public int? AuditorKnownBad { get; init; }
        public int? AuditorKnownGood { get; init; }
        public bool AuditorExact { get; init; }
        public string? AuditorProblem { get; init; }
        public string? GoldenProblem { get; init; }
        public IReadOnlyList<string> PauseReasons { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// The outcome of one evaluation: the next mode and why, the journal row to write (null when
    /// the mode does not change), whether the recent window was clean, and whether the lane must
    /// undo its writes since its last clean window.
    /// </summary>
    public sealed record LaneQualifyVerdict(string Mode, string Reason, string? Journal, bool Clean, bool Undo, LaneFigures Figures);

    /// <summary>
    /// The arithmetic of the lane frame. A lane qualifies when its audited sample is at least the
    /// sample its bar needs and the Wilson 95 % lower bound of its corrected precision reaches the
    /// bar; it pauses when the observed corrected error of a full rolling window, or the lower
    /// bound of the error over a window still filling, is above the pause bar. Errors are corrected
    /// for the audit check with Rogan-Gladen, using the bounds of the check's own score that make
    /// the lane look worse and never crediting more false flags than errors were confirmed, so a
    /// weak, noisy or barely scored auditor needs more audited decisions than an exact check.
    /// See docs/architecture/lane-frame.md.
    /// </summary>
    public static class LaneQualifier
    {
        public const double Z95 = 1.959963984540054;
        private const int SearchLimit = 1_000_000;

        /// <summary>The Wilson score interval at 95 % for <paramref name="successes"/> of <paramref name="n"/>; (0, 1) when n is 0.</summary>
        public static (double Lower, double Upper) Wilson(int successes, int n)
        {
            if (n <= 0) return (0, 1);
            var p = (double)successes / n;
            var z2 = Z95 * Z95;
            var denominator = 1 + z2 / n;
            var centre = p + z2 / (2.0 * n);
            var margin = Z95 * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n));
            return (Math.Max(0, (centre - margin) / denominator), Math.Min(1, (centre + margin) / denominator));
        }

        /// <summary>The smallest sample whose Wilson lower bound reaches <paramref name="bar"/> with no error.</summary>
        public static int MinimumSample(double bar)
        {
            if (bar <= 0) return 1;
            if (bar >= 1) throw new ArgumentOutOfRangeException(nameof(bar), "No sample reaches a lower bound of 1.");
            var n = Math.Max(1, (int)Math.Floor(bar * Z95 * Z95 / (1 - bar)) - 1);
            while (Wilson(n, n).Lower < bar) n++;
            return n;
        }

        /// <summary>The error rate <paramref name="errors"/> of <paramref name="audited"/>, corrected for the audit check.</summary>
        public static ErrorEstimate Correct(int errors, int audited, AuditorScore score)
        {
            var observed = audited == 0 ? 0 : (double)errors / audited;
            var (low, high) = Wilson(errors, audited);
            if (score.Exact) return new ErrorEstimate(audited, errors, observed, observed, low, high) { Pausing = observed };

            var se = score.Sensitivity;
            var f = score.FalseFlagRate;
            var (seLow, seHigh) = Wilson(score.FlaggedBad, score.KnownBad);
            var (fLow, fHigh) = Wilson(score.FlaggedGood, score.KnownGood);
            return new ErrorEstimate(
                audited, errors, observed,
                RoganGladen(observed, se, f, 0),
                RoganGladen(low, seHigh, fHigh, 0),
                RoganGladen(high, seLow, Math.Min(fLow, observed), 1))
            {
                Pausing = RoganGladen(observed, seLow, Math.Min(fLow, observed), 1),
            };
        }

        /// <summary>
        /// The audited sample the qualify bar needs with no error, after correction; null when no
        /// sample reaches it with this audit check.
        /// </summary>
        public static int? NeededToQualify(LanePolicy policy, AuditorScore score)
        {
            var floor = Math.Max(1, policy.MinimumAudited);
            return Smallest(floor, n => 1 - Correct(0, n, score).Upper >= policy.QualifyBar);
        }

        /// <summary>
        /// The rolling window the pause check reads: the policy's, or more when a clean window that
        /// size could not bound the error under the bar, so a full window holds enough audits to
        /// tell a lane at its bar from one above it.
        /// </summary>
        public static int PauseWindow(LanePolicy policy, AuditorScore score) =>
            Math.Max(policy.PauseWindow, Smallest(1, n => Correct(0, n, score).Upper <= policy.PauseBar) ?? policy.PauseWindow);

        public static LaneQualifyVerdict Decide(LaneQualifyInput input, DateTime now)
        {
            var policy = input.Policy;
            var auditorProblem = input.Auditor?.Problem ?? (input.Auditor == null ? "no audit check score is recorded" : null);
            var score = auditorProblem == null ? input.Auditor! : null;

            var qualify = score == null ? null : Correct(input.Qualify.Errors, input.Qualify.Audited, score);
            var recent = score == null ? null : Correct(input.Recent.Errors, input.Recent.Audited, score);
            var needed = score == null ? null : NeededToQualify(policy, score);
            var window = score == null ? policy.PauseWindow : PauseWindow(policy, score);

            var figures = new LaneFigures
            {
                EvaluatedAt = now,
                Signature = input.Signature,
                Decisions = input.Qualify.Decisions,
                Written = input.Qualify.Written,
                Audited = input.Qualify.Audited,
                Errors = input.Qualify.Errors,
                Flagged = input.Qualify.Flagged,
                ObservedError = qualify?.Observed ?? 0,
                CorrectedError = qualify?.Corrected ?? 0,
                PrecisionLower = qualify == null ? 0 : 1 - qualify.Upper,
                PrecisionUpper = qualify == null ? 1 : 1 - qualify.Lower,
                QualifyBar = policy.QualifyBar,
                NeededAudited = needed,
                WindowAudited = input.Recent.Audited,
                WindowErrors = input.Recent.Errors,
                WindowError = recent?.Pausing ?? 0,
                WindowErrorLower = recent?.Lower ?? 0,
                WindowErrorUpper = recent?.Upper ?? 1,
                PauseBar = policy.PauseBar,
                Window = window,
                AuditorSource = input.Auditor?.Source,
                AuditorSensitivity = input.Auditor?.Sensitivity,
                AuditorFalseFlagRate = input.Auditor?.FalseFlagRate,
                AuditorKnownBad = input.Auditor?.KnownBad,
                AuditorKnownGood = input.Auditor?.KnownGood,
                AuditorExact = input.Auditor?.Exact ?? false,
                AuditorProblem = auditorProblem,
                GoldenProblem = input.GoldenProblem,
                PauseReasons = input.PauseReasons,
            };

            var qualifies = qualify != null && needed is { } need && input.Qualify.Audited >= need && 1 - qualify.Upper >= policy.QualifyBar;
            var standing = qualify == null
                ? auditorProblem!
                : needed == null
                    ? $"no audited sample reaches the qualify bar {Pct(policy.QualifyBar)} with this audit check"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"{input.Qualify.Audited} of {needed} audited, {input.Qualify.Errors} confirmed errors, precision lower bound {Pct(1 - qualify.Upper)} against the bar {Pct(policy.QualifyBar)}");
            var proof = qualify == null ? standing : string.Create(CultureInfo.InvariantCulture,
                $"{input.Qualify.Audited} audited, {input.Qualify.Errors} confirmed errors, precision lower bound {Pct(1 - qualify.Upper)} reaches the bar {Pct(policy.QualifyBar)}");

            LaneQualifyVerdict Stay(string mode, string reason) => new(mode, reason, null, false, false, figures);

            switch (input.Mode)
            {
                case LaneMode.Paused:
                {
                    if (policy.ResumeNeedsNewSignature && input.Signature == input.PausedSignature)
                        return Stay(LaneMode.Paused, "paused; resumes only under a new signature");
                    if (input.PauseReasons.Count > 0) return Stay(LaneMode.Paused, "paused: " + string.Join("; ", input.PauseReasons));
                    if (input.GoldenProblem != null) return Stay(LaneMode.Paused, "paused: " + input.GoldenProblem);
                    if (!qualifies) return Stay(LaneMode.Paused, "paused; probation since the pause: " + standing);
                    return new(LaneMode.Live, "resumed: " + proof, LaneJournal.Resume, false, false, figures);
                }
                case LaneMode.Live:
                {
                    if (input.PauseReasons.Count > 0)
                        return new(LaneMode.Paused, "paused: " + string.Join("; ", input.PauseReasons), LaneJournal.Pause, false, true, figures);

                    var full = recent != null && input.Recent.Audited >= window;
                    if (recent != null && (full ? recent.Pausing > policy.PauseBar : recent.Lower > policy.PauseBar))
                    {
                        return new(LaneMode.Paused, string.Create(CultureInfo.InvariantCulture,
                            $"paused: {input.Recent.Errors} confirmed errors in the last {input.Recent.Audited} audited writes{(input.SignatureChanged ? " under the previous signature" : "")}, {(full ? "observed error" : "error lower bound")} {Pct(full ? recent.Pausing : recent.Lower)} above the pause bar {Pct(policy.PauseBar)}"),
                            LaneJournal.Pause, false, true, figures);
                    }
                    if (input.SignatureChanged)
                        return new(LaneMode.Shadow, "back to shadow: the signature changed", LaneJournal.Shadow, false, false, figures);
                    if (input.GoldenProblem != null)
                        return new(LaneMode.Shadow, "back to shadow: " + input.GoldenProblem, LaneJournal.Shadow, false, false, figures);
                    if (recent == null)
                        return new(LaneMode.Shadow, "back to shadow: " + auditorProblem, LaneJournal.Shadow, false, false, figures);

                    return new(LaneMode.Live, string.Create(CultureInfo.InvariantCulture,
                        $"live: {input.Recent.Errors} confirmed errors in the last {input.Recent.Audited} audited writes of a {window} window, observed error {Pct(recent.Pausing)} against the pause bar {Pct(policy.PauseBar)}"),
                        null, full, false, figures);
                }
                default:
                {
                    if (input.PauseReasons.Count > 0) return Stay(LaneMode.Shadow, "shadow: " + string.Join("; ", input.PauseReasons));
                    if (input.GoldenProblem != null) return Stay(LaneMode.Shadow, "shadow: " + input.GoldenProblem);
                    if (!qualifies) return Stay(LaneMode.Shadow, "shadow: " + standing);
                    return new(LaneMode.Live, "qualified: " + proof, LaneJournal.Qualify, false, false, figures);
                }
            }
        }

        private static double RoganGladen(double p, double sensitivity, double falseFlags, double whenUnusable)
        {
            var span = sensitivity - falseFlags;
            if (span <= 0) return whenUnusable;
            return Math.Clamp((p - falseFlags) / span, 0, 1);
        }

        private static int? Smallest(int floor, Func<int, bool> passes)
        {
            if (passes(floor)) return floor;
            var high = Math.Max(2, floor * 2);
            while (!passes(high))
            {
                if (high >= SearchLimit) return null;
                high = Math.Min(SearchLimit, high * 2);
            }
            var low = high / 2;
            while (high - low > 1)
            {
                var mid = low + (high - low) / 2;
                if (passes(mid)) high = mid;
                else low = mid;
            }
            return high;
        }

        private static string Pct(double value) => value.ToString("0.0#%", CultureInfo.InvariantCulture);
    }
}
