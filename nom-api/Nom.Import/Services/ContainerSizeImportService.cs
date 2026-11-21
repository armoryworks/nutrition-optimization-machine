using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Import.Services
{
    /// <summary>
    /// <c>Nom.Import --container-sizes</c>: proposes one container size per catalog food, kind and
    /// size word from the USDA files on the import host, and writes them in shadow through the
    /// container size lane, which then scores them against the stated sizes. In order: a USDA
    /// portion on the very food, the branded mode of the food's class, a standard of fill for wine
    /// and spirits. Sources of one key must agree within 10 %, or the key is listed as "sources
    /// disagree" and not written. A drained weight comes from a drained portion, or from the
    /// class's drained ratio when its members agree within ±8 %, and only for a food whose USDA
    /// description is drained solids. Every file must be listed in the checksum file.
    /// </summary>
    public class ContainerSizeImportService
    {
        public const decimal DrainedRatioSpread = 0.08m;

        private readonly ApplicationDbContext _db;
        private readonly IContainerSizeService _sizes;

        public ContainerSizeImportService(ApplicationDbContext db, IContainerSizeService sizes)
        {
            _db = db;
            _sizes = sizes;
        }

        public sealed record Options(string? BrandedDir, string SrDir, string FoundationDir, string ChecksumFile, bool DryRun);

        /// <summary>What a run read, proposed, refused, wrote and scored.</summary>
        public sealed class Report
        {
            public bool DryRun { get; init; }
            public DateTime Stamp { get; init; }
            public string BrandedRelease { get; set; } = "-";
            public int Foods { get; set; }
            public int FoodsInFiles { get; set; }
            public BrandedContainerSizes? Branded { get; set; }
            public List<string> Modes { get; } = new();
            public Dictionary<string, int> ProposedBySource { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, int> RefusedByReason { get; } = new(StringComparer.Ordinal);
            public List<string> Refusals { get; } = new();
            public List<ContainerSizeProposal> Proposals { get; } = new();
            public List<string> Checksums { get; } = new();
            public List<string> Stopped { get; } = new();
            public ContainerSizeWriteReport? Written { get; set; }
            public ContainerSizeScoreReport? Scored { get; set; }

            public void Refuse(string reason, string line)
            {
                RefusedByReason[reason] = RefusedByReason.GetValueOrDefault(reason) + 1;
                if (Refusals.Count < 500) Refusals.Add(line);
            }
        }

        private sealed record Food(long Id, string Name, string FdcId, decimal? GramsPerMilliliter);

        private sealed record UsdaFood(string Description, int? CategoryId);

        /// <summary>A catalog food with the USDA facts the proposal reads: its description, category, density and household portions.</summary>
        public sealed record ProposalFood(long Id, string Name, string FdcId, string Description, int? CategoryId, decimal? GramsPerMilliliter, IReadOnlyList<UsdaPortion> Portions);

        private sealed record Candidate(decimal? Net, decimal? Drained, string Source, string Class, decimal? Share = null, int? Products = null);

        private sealed record Chosen(ProposalFood Food, string Kind, string Era, string Word, Candidate Net, Candidate? Drained, int Agreeing, string Class);

        /// <summary>The modes of every class and size word that pass the mode test, read from the branded sizes.</summary>
        public static Dictionary<(string Class, string SizeWord), ContainerMode> Modes(IReadOnlyDictionary<string, Dictionary<int, int>> sizesByClass, IReadOnlyList<ContainerClass> classes, List<string>? lines = null)
        {
            var modes = new Dictionary<(string Class, string SizeWord), ContainerMode>();
            foreach (var c in classes)
            {
                var sizes = sizesByClass.GetValueOrDefault(c.Id) ?? new Dictionary<int, int>();
                foreach (var word in new[] { string.Empty, ContainerKinds.Small, ContainerKinds.Large })
                {
                    var mode = ContainerModeRule.Decide(sizes, word);
                    if (mode == null) continue;
                    lines?.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{c.Id}{(word.Length > 0 ? " (" + word + ")" : string.Empty)}: {mode.Grams} {(c.ByVolume ? "mL" : "g")}, {mode.Band}/{mode.Products} in band ({mode.Share:P0}), runner-up {mode.RunnerUp}: {mode.Refusal ?? "counts"}"));
                    if (mode.Passes) modes[(c.Id, word)] = mode;
                }
            }
            return modes;
        }

        /// <summary>
        /// The sizes the sources give each food, combined per key: the sources must agree within
        /// 10 % and name at most one class; a portion's net wins over the branded mode; a drained
        /// weight is kept only for a food whose description is drained solids, else from its class's
        /// drained ratio. <paramref name="refuse"/> hears every key left out and why.
        /// </summary>
        public static List<ContainerSizeProposal> Propose(IReadOnlyList<ProposalFood> foods, IReadOnlyDictionary<(string Class, string SizeWord), ContainerMode> modes, string brandedRelease, IReadOnlyList<ContainerClass> classes, Action<string, string> refuse)
        {
            var byFood = new Dictionary<long, Dictionary<(string Kind, string Era, string SizeWord), List<Candidate>>>();
            void Add(long food, string kind, string era, string word, Candidate c)
            {
                if (!byFood.TryGetValue(food, out var keys)) byFood[food] = keys = new();
                if (!keys.TryGetValue((kind, era, word), out var list)) keys[(kind, era, word)] = list = new List<Candidate>();
                list.Add(c);
            }

            foreach (var food in foods)
            {
                foreach (var p in UsdaContainerPortion.Sizes(food.Portions, food.Description))
                    Add(food.Id, p.Kind, ContainerEras.Modern, p.SizeWord, new Candidate(p.NetGrams, p.DrainedGrams, $"fdc:{food.FdcId}:portion:{p.Seq}", "portion:" + p.Kind));
                foreach (var c in ContainerClasses.For(food.CategoryId, food.Description, classes))
                {
                    foreach (var word in new[] { string.Empty, ContainerKinds.Small, ContainerKinds.Large })
                    {
                        if (!modes.TryGetValue((c.Id, word), out var mode)) continue;
                        decimal? net = c.ByVolume ? (food.GramsPerMilliliter is > 0 ? mode.Grams * food.GramsPerMilliliter.Value : null) : mode.Grams;
                        if (net == null)
                        {
                            refuse("class read by volume and the food has no density", $"#{food.Id} {food.Name}: {c.Id}");
                            continue;
                        }
                        Add(food.Id, c.Kind, ContainerEras.Modern, word, new Candidate(net, null,
                            $"deterministic:fdc-branded-mode:{brandedRelease}:{c.Id}:{mode.Band}/{mode.Products}", c.Id, (decimal)mode.Share, mode.Products));
                    }
                }
                if (CanSizeTable.Fill(food.Description) is { Milliliters: > 0 } fill)
                {
                    if (food.GramsPerMilliliter is > 0)
                        Add(food.Id, fill.Kind, fill.Era, string.Empty, new Candidate(fill.Milliliters!.Value * food.GramsPerMilliliter.Value, null,
                            $"deterministic:standard-of-fill:{fill.Page.Replace("§", "27 CFR").Trim()}", "fill:" + fill.Kind));
                    else refuse("standard of fill and the food has no density", $"#{food.Id} {food.Name}");
                }
            }

            var chosen = new List<Chosen>();
            foreach (var food in foods.Where(f => byFood.ContainsKey(f.Id)))
            {
                foreach (var ((kind, era, word), list) in byFood[food.Id].OrderBy(k => k.Key.Kind, StringComparer.Ordinal).ThenBy(k => k.Key.Era, StringComparer.Ordinal).ThenBy(k => k.Key.SizeWord, StringComparer.Ordinal))
                {
                    var key = $"#{food.Id} {food.Name} {kind}/{era}/{(word.Length == 0 ? "-" : word)}";
                    var nets = list.Where(c => c.Net is > 0).ToList();
                    var drains = list.Where(c => c.Drained is > 0).ToList();
                    if (nets.Count == 0)
                    {
                        refuse("a drained weight with no net size", key + ": " + string.Join(", ", drains.Select(d => d.Source)));
                        continue;
                    }
                    if (!nets.All(n => ContainerSizeAgreement.Agree(n.Net!.Value, nets[0].Net!.Value)))
                    {
                        refuse("sources disagree", key + ": " + string.Join(", ", nets.Select(n => string.Create(CultureInfo.InvariantCulture, $"{n.Net:0} g {n.Source}"))));
                        continue;
                    }
                    var named = nets.Select(n => n.Class).Where(c => !c.Contains(':')).Distinct().ToList();
                    if (named.Count > 1)
                    {
                        refuse("two classes give a size", key + ": " + string.Join(", ", named));
                        continue;
                    }
                    var net = nets.FirstOrDefault(n => n.Source.Contains(":portion:")) ?? nets[0];
                    var drained = ContainerKinds.DescribesDrained(food.Description) ? drains.FirstOrDefault() : null;
                    var cls = ContainerClasses.For(food.CategoryId, food.Description, classes).FirstOrDefault(c => c.Kind == kind)?.Id ?? net.Class;
                    chosen.Add(new Chosen(food, kind, era, word, net, drained, nets.Count, cls));
                }
            }

            var ratios = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var group in chosen.Where(c => c.Drained != null && !c.Class.Contains(':')).GroupBy(c => c.Class))
            {
                var r = group.Select(c => c.Drained!.Drained!.Value / c.Net.Net!.Value).OrderBy(x => x).ToList();
                if (r.Count < 2) continue;
                var median = r[r.Count / 2];
                if (r.All(x => Math.Abs(x - median) <= DrainedRatioSpread * median)) ratios[group.Key] = median;
            }

            var proposals = new List<ContainerSizeProposal>();
            foreach (var c in chosen)
            {
                decimal? drained = c.Drained?.Drained;
                var drainedSource = c.Drained?.Source;
                if (drained == null && ContainerKinds.DescribesDrained(c.Food.Description) && ratios.TryGetValue(c.Class, out var ratio))
                {
                    drained = Math.Round(c.Net.Net!.Value * ratio, 1);
                    drainedSource = string.Create(CultureInfo.InvariantCulture, $"class ratio {ratio:0.###}");
                }
                var netGrams = Math.Round(c.Net.Net!.Value, 1);
                var text = string.Create(CultureInfo.InvariantCulture, $"{netGrams / CanSizeTable.GramsPerOunce:0.##} oz ({netGrams:0} g) {c.Kind}")
                    + (c.Word.Length > 0 ? $", {c.Word}" : string.Empty)
                    + (drained is { } d ? string.Create(CultureInfo.InvariantCulture, $"; drained about {d:0} g ({drainedSource})") : string.Empty);
                var source = c.Net.Source + (c.Drained != null && c.Drained.Source != c.Net.Source ? "|drained:" + c.Drained.Source : string.Empty);
                proposals.Add(new ContainerSizeProposal(c.Food.Id, c.Kind, c.Era, c.Word, netGrams, drained, text, source, c.Class, c.Agreeing, c.Net.Share, c.Net.Products));
            }
            return proposals;
        }

        public async Task<Report> RunAsync(Options options, CancellationToken cancellationToken = default)
        {
            var report = new Report { DryRun = options.DryRun, Stamp = DateTime.UtcNow };
            var recorded = CatalogAdmissionService.ReadChecksums(options.ChecksumFile);
            var files = new List<string>
            {
                Path.Combine(options.SrDir, "food.csv"), Path.Combine(options.SrDir, "food_portion.csv"),
                Path.Combine(options.FoundationDir, "food.csv"), Path.Combine(options.FoundationDir, "food_portion.csv"),
            };
            if (options.BrandedDir != null)
            {
                files.Add(Path.Combine(options.BrandedDir, "branded_food.csv"));
                files.Add(Path.Combine(options.BrandedDir, "food.csv"));
                report.BrandedRelease = Regex.Match(Path.GetFileName(Path.TrimEndingDirectorySeparator(options.BrandedDir)), @"\d{4}-\d{2}-\d{2}").Value is { Length: > 0 } r ? r : "unknown";
            }
            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    report.Stopped.Add($"{file} is missing");
                    continue;
                }
                var hash = CatalogAdmissionService.Sha256(file);
                report.Checksums.Add($"{hash}  {file}");
                if (!recorded.TryGetValue(Path.GetFileName(file), out var hashes) || !hashes.Contains(hash))
                    report.Stopped.Add($"{file} (sha256 {hash}) is not recorded in {options.ChecksumFile}");
            }
            foreach (var problem in CanSizeTable.Problems(CanSizeTable.Rows).Concat(ContainerClasses.Problems(ContainerClasses.All)))
                report.Stopped.Add("rule file: " + problem);
            if (report.Stopped.Count > 0) return report;

            var types = new[] { FdcFoundationImportService.SrLegacyFood, FdcFoundationImportService.FoundationFood };
            var foods = await _db.Ingredients.AsNoTracking()
                .Where(i => i.FdcId != null && types.Contains(i.FdcDataType) && i.CurationStatusId != (long)CurationStatusEnum.Rejected)
                .Select(i => new Food(i.Id, i.Name, i.FdcId!, i.GramsPerMilliliter))
                .ToListAsync(cancellationToken);
            report.Foods = foods.Count;
            var ids = foods.Select(f => f.FdcId).ToHashSet(StringComparer.Ordinal);
            var usda = ReadFoods(Path.Combine(options.SrDir, "food.csv"), ids);
            foreach (var (k, v) in ReadFoods(Path.Combine(options.FoundationDir, "food.csv"), ids)) usda[k] = v;
            report.FoodsInFiles = foods.Count(f => usda.ContainsKey(f.FdcId));
            var portions = UsdaPortionFile.Read(Path.Combine(options.SrDir, "food_portion.csv"), ids);
            foreach (var (k, v) in UsdaPortionFile.Read(Path.Combine(options.FoundationDir, "food_portion.csv"), ids))
                portions[k] = portions.TryGetValue(k, out var list) ? list.Concat(v).ToList() : v;

            var modes = new Dictionary<(string Class, string SizeWord), ContainerMode>();
            if (options.BrandedDir != null)
            {
                report.Branded = BrandedContainerScan.Scan(options.BrandedDir, ContainerClasses.All);
                modes = Modes(report.Branded.SizesByClass, ContainerClasses.All, report.Modes);
            }

            var proposalFoods = foods.Where(f => usda.ContainsKey(f.FdcId))
                .Select(f => new ProposalFood(f.Id, f.Name, f.FdcId, usda[f.FdcId].Description, usda[f.FdcId].CategoryId, f.GramsPerMilliliter,
                    portions.GetValueOrDefault(f.FdcId) ?? new List<UsdaPortion>()))
                .ToList();
            report.Proposals.AddRange(Propose(proposalFoods, modes, report.BrandedRelease, ContainerClasses.All, report.Refuse));
            foreach (var proposal in report.Proposals)
            {
                var kind = Nom.Orch.Services.ContainerSizeService.SourceKind(proposal.SourceRef);
                report.ProposedBySource[kind] = report.ProposedBySource.GetValueOrDefault(kind) + 1;
            }

            report.Written = await _sizes.WriteAsync(report.Proposals, options.DryRun, report.Stamp, retireMissing: true, SystemConstants.SystemPersonId, cancellationToken);
            report.Scored = options.DryRun
                ? await _sizes.ScoreAsync(false, SystemConstants.SystemPersonId, report.Proposals, cancellationToken)
                : await _sizes.ScoreAsync(true, SystemConstants.SystemPersonId, null, cancellationToken);
            return report;
        }

        private static Dictionary<string, UsdaFood> ReadFoods(string path, ISet<string> ids)
        {
            var result = new Dictionary<string, UsdaFood>(StringComparer.Ordinal);
            using var reader = new StreamReader(path);
            var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
            int id = Array.IndexOf(header, "fdc_id"), description = Array.IndexOf(header, "description"), category = Array.IndexOf(header, "food_category_id");
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length <= Math.Max(description, category) || !ids.Contains(f[id])) continue;
                result[f[id]] = new UsdaFood(f[description], int.TryParse(f[category], NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : null);
            }
            return result;
        }
    }
}
