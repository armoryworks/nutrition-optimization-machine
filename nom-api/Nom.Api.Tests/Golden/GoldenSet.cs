using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nom.Import.Golden;

namespace Nom.Api.Tests.Golden
{
    /// <summary>What the code gave for one golden row: whether it is the judged answer, both as text, and the code's own value for each expected column.</summary>
    internal sealed record GoldenOutcome(bool Pass, string Expected, string Actual, IReadOnlyDictionary<string, string> Current);

    /// <summary>Where a golden run reads and writes, and whether the update switch is on.</summary>
    internal sealed record GoldenOptions(string ReadFolder, string WriteFolder, bool Update, bool UnderCi)
    {
        public static GoldenOptions FromEnvironment()
        {
            var update = Environment.GetEnvironmentVariable(GoldenSet.UpdateVariable) == "1";
            return new(
                update ? GoldenSet.SourceFolder() : GoldenSet.Folder,
                GoldenSet.SourceFolder(),
                update,
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")));
        }
    }

    /// <summary>The result of scoring one status column of one golden file.</summary>
    internal sealed class GoldenReport
    {
        public string Key { get; init; } = string.Empty;
        public int Judged { get; set; }
        public int Ok { get; set; }
        public int OkPassing { get; set; }
        public int Agent { get; set; }
        public int AgentPassing { get; set; }
        public int Known { get; set; }
        public int? Baseline { get; set; }
        public List<string> Regressed { get; } = new();
        public List<string> NowPassing { get; } = new();
        public List<string> Unreviewed { get; } = new();
        public List<string> Updated { get; } = new();
        public List<string> StillKnown { get; } = new();

        public bool Failed => Regressed.Count > 0 || NowPassing.Count > 0 || Unreviewed.Count > 0 || Known > (Baseline ?? 0) || Updated.Count > 0;

        public string Summary =>
            $"{Key}: {OkPassing}/{Ok} ok rows pass ({(Ok == 0 ? 100 : 100.0 * OkPassing / Ok):F1} %), "
            + (Agent > 0 ? $"{AgentPassing}/{Agent} agent rows pass (judged by a model reviewer, not yet by a person), " : string.Empty)
            + $"{Known} known failures (baseline {Baseline?.ToString() ?? "none"}), {Unreviewed.Count} unreviewed, {Judged} judged rows";

        public string Message()
        {
            var text = new StringBuilder(Summary);
            void Section(string title, List<string> rows)
            {
                if (rows.Count == 0) return;
                text.Append('\n').Append(title).Append(" (").Append(rows.Count).Append("):");
                foreach (var row in rows.Take(200)) text.Append("\n  ").Append(row);
                if (rows.Count > 200) text.Append("\n  ... and ").Append(rows.Count - 200).Append(" more");
            }
            Section("REGRESSED: ok or agent rows that now fail", Regressed);
            Section("NOW PASSES, promote it: known rows that pass (NOM_GOLDEN_UPDATE=1 marks them ok and lowers the baseline)", NowPassing);
            Section("UNREVIEWED: new or review rows cannot ship until a person sets them to ok or known", Unreviewed);
            Section("UPDATED by NOM_GOLDEN_UPDATE: review the diff, then run again without it", Updated);
            if (Known > (Baseline ?? 0))
                text.Append($"\nKNOWN OVER BASELINE: {Known} known rows, baseline.json allows {Baseline ?? 0}; a new known row is added by raising baseline.json by hand in the same commit");
            return text.ToString();
        }
    }

    /// <summary>
    /// Scores golden files: rows with status ok must pass, known rows must still fail (one that
    /// passes asks to be promoted), new and review rows fail the run, and the known count may not
    /// exceed baseline.json. An agent row was judged by a model reviewer and not yet by a person:
    /// it must pass like an ok row, so the code cannot drift from it, but it is reported apart and
    /// only a person turns it into ok. With NOM_GOLDEN_UPDATE=1 a run fills the blank expected
    /// cells of new rows with the code's output and marks them review, gives a new row whose
    /// expected cells are all judged ok or known by whether it passes (agent instead of ok when
    /// the row's judge is a model), promotes known rows that pass and lowers the baseline. It
    /// never rewrites a judged cell, never raises a baseline, is refused under CI, and always ends
    /// red.
    /// </summary>
    internal static class GoldenSet
    {
        public const string UpdateVariable = "NOM_GOLDEN_UPDATE";
        public const string BaselineFile = "baseline.json";
        public const string Ok = "ok";
        public const string Known = "known";
        public const string New = "new";
        public const string Review = "review";
        public const string Agent = "agent";

        /// <summary>Whether a judge or source cell names a model reviewer (agent-review-*) rather than a person, a rule or a design.</summary>
        public static bool ByModel(string judge) => judge.StartsWith("agent-", StringComparison.Ordinal);

        public static string Folder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Golden");

        public static string SourceFolder([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "Golden"));

        public static GoldenTsv Read(string file, GoldenOptions? options = null) =>
            GoldenTsv.Read(Path.Combine((options ?? GoldenOptions.FromEnvironment()).ReadFolder, file));

        public static Dictionary<string, int> Baseline(string folder)
        {
            var path = Path.Combine(folder, BaselineFile);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? new Dictionary<string, int>()
                : new Dictionary<string, int>();
        }

        public static GoldenReport Run(string file, string statusColumn, Func<GoldenRow, GoldenOutcome?> check, GoldenOptions? options = null, Func<GoldenRow, bool>? modelJudged = null)
        {
            options ??= GoldenOptions.FromEnvironment();
            if (options.Update && options.UnderCi)
                throw new InvalidOperationException($"{UpdateVariable}=1 is refused under CI: expected values change only by a reviewed edit.");

            var table = GoldenTsv.Read(Path.Combine(options.ReadFolder, file));
            var key = $"{file}:{statusColumn}";
            var baseline = Baseline(options.ReadFolder);
            var report = new GoldenReport { Key = key, Baseline = baseline.TryGetValue(key, out var b) ? b : null };
            var changed = false;

            foreach (var row in table.Rows)
            {
                var status = row[statusColumn];
                if (status is "" or GoldenTsv.NoAnswer) continue;
                var outcome = check(row);
                if (outcome == null) continue;
                report.Judged++;
                var blanks = outcome.Current.Keys.Where(c => row[c].Length == 0).ToList();
                var line = $"{row.Id} (line {row.LineNumber + 1}): expected {outcome.Expected}, got {outcome.Actual}";

                switch (status)
                {
                    case Ok:
                        report.Ok++;
                        if (outcome.Pass && blanks.Count == 0) report.OkPassing++;
                        else report.Regressed.Add(line);
                        break;
                    case Agent:
                        report.Agent++;
                        if (outcome.Pass && blanks.Count == 0) report.AgentPassing++;
                        else report.Regressed.Add(line + " (agent)");
                        break;
                    case Known:
                        report.Known++;
                        if (!outcome.Pass || blanks.Count > 0)
                        {
                            report.StillKnown.Add(line);
                            break;
                        }
                        if (options.Update)
                        {
                            row[statusColumn] = modelJudged?.Invoke(row) == true ? Agent : Ok;
                            report.Known--;
                            report.Updated.Add($"{row.Id}: known -> {row[statusColumn]}, it now passes");
                            changed = true;
                        }
                        else report.NowPassing.Add(line);
                        break;
                    case New when options.Update:
                        if (blanks.Count > 0)
                        {
                            foreach (var column in blanks) row[column] = outcome.Current[column];
                            row[statusColumn] = Review;
                            report.Updated.Add($"{row.Id}: filled {string.Join(", ", blanks)} with the current output; marked review");
                        }
                        else
                        {
                            var byModel = modelJudged?.Invoke(row) == true;
                            row[statusColumn] = !outcome.Pass ? Known : byModel ? Agent : Ok;
                            if (!outcome.Pass) report.Known++;
                            else if (byModel)
                            {
                                report.Agent++;
                                report.AgentPassing++;
                            }
                            else
                            {
                                report.Ok++;
                                report.OkPassing++;
                            }
                            report.Updated.Add($"{row.Id}: judged row marked {row[statusColumn]}{(outcome.Pass ? string.Empty : $" ({line})")}");
                        }
                        changed = true;
                        break;
                    default:
                        report.Unreviewed.Add($"{row.Id} ({status}): {line}");
                        break;
                }
            }

            if (options.Update && changed)
            {
                Directory.CreateDirectory(options.WriteFolder);
                table.Write(Path.Combine(options.WriteFolder, file));
                if (report.Baseline is { } allowed && report.Known < allowed)
                {
                    WriteBaseline(options.WriteFolder, key, report.Known);
                    report.Updated.Add($"baseline {key}: {allowed} -> {report.Known}");
                    report.Baseline = report.Known;
                }
            }

            return report;
        }

        public static void Assert(GoldenReport report)
        {
            if (report.Failed) throw new GoldenSetFailure(report.Message());
        }

        private static void WriteBaseline(string folder, string key, int count)
        {
            var path = Path.Combine(folder, BaselineFile);
            var json = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject();
            json[key] = count;
            var ordered = new JsonObject();
            foreach (var entry in json.OrderBy(e => e.Key, StringComparer.Ordinal)) ordered[entry.Key] = entry.Value?.DeepClone();
            File.WriteAllText(path, ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        public static string Cell(string? value) => value ?? GoldenTsv.NoAnswer;

        public static string Number(decimal? value) => value is { } v ? Math.Round(v, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : GoldenTsv.NoAnswer;

        /// <summary>Whether every judged cell of the row equals the code's value; a <see cref="GoldenTsv.NotJudged"/> cell is not compared.</summary>
        public static GoldenOutcome Compare(GoldenRow row, IReadOnlyDictionary<string, string> current)
        {
            var judged = current.Where(c => row[c.Key] != GoldenTsv.NotJudged).ToList();
            var pass = judged.All(c => row[c.Key].Length > 0 && string.Equals(row[c.Key], c.Value, StringComparison.Ordinal));
            return new GoldenOutcome(
                pass,
                string.Join(" | ", judged.Select(c => $"{c.Key}={row[c.Key]}")),
                string.Join(" | ", judged.Select(c => $"{c.Key}={c.Value}")),
                judged.ToDictionary(c => c.Key, c => c.Value));
        }
    }

    /// <summary>A golden set that failed: its score and every row that regressed, passes while marked known, or is unreviewed.</summary>
    public sealed class GoldenSetFailure : Exception
    {
        public GoldenSetFailure(string message) : base(message) { }
    }
}
