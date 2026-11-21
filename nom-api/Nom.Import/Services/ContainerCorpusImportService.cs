using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Import.Services
{
    /// <summary>
    /// <c>Nom.Import --container-sizes --corpus</c>: mines the sized container lines of old and
    /// undated books (recipe.RecipeSourceEra historic or unknown) from the recipe corpus, derives
    /// the same-book and same-era sizes of <see cref="ContainerCorpusSizes"/> with their evidence,
    /// and writes them in shadow through the container size lane, which then scores every tier on
    /// its own hold-out (each line left out of the size it is scored against). It retires only
    /// corpus rows no line gives any more, never a USDA source's row. It also counts, per product
    /// class and tier, how many unsized container lines of old and undated books each tier would
    /// size and the worst-case error of the sizes it takes, and why the rest stay uncounted. With
    /// a facts path it writes every size it derived, refused ones included, in the columns
    /// ops/container-era-facts.py writes from a capture of the same corpus.
    /// </summary>
    public class ContainerCorpusImportService
    {
        public const string NoNutrients = "the food has no nutrient values (a catalog gap, not a size)";
        public const string BookTooLong = "the book's attribution is longer than 1023 characters";

        private readonly ApplicationDbContext _db;
        private readonly IContainerSizeService _sizes;

        public ContainerCorpusImportService(ApplicationDbContext db, IContainerSizeService sizes)
        {
            _db = db;
            _sizes = sizes;
        }

        public sealed record Options(bool DryRun, string? FactsPath = null);

        /// <summary>What a corpus run read, derived, wrote and scored, and how the unsized lines of old and undated books are sized.</summary>
        public sealed class Report
        {
            public bool DryRun { get; init; }
            public DateTime Stamp { get; init; }
            public int BookLines { get; set; }
            public int SizedLines { get; set; }
            public int Books { get; set; }
            public int BookSizes { get; set; }
            public int BookRefused { get; set; }
            public int EraSizes { get; set; }
            public int EraRefused { get; set; }
            public Dictionary<string, int> RefusedByReason { get; } = new(StringComparer.Ordinal);
            public List<ContainerSizeProposal> Proposals { get; } = new();
            public List<ContainerCoverageRow> Coverage { get; } = new();
            public Dictionary<string, int> UncountedByReason { get; } = new(StringComparer.Ordinal);
            public int UnsizedLines { get; set; }
            public string? FactsWritten { get; set; }
            public ContainerSizeWriteReport? Written { get; set; }
            public ContainerSizeScoreReport? Scored { get; set; }
        }

        /// <summary>A line of a book recipe the run reads: a sized line in a weight or volume unit, or an unsized one in a container unit.</summary>
        public sealed record BookLine(long IngredientId, string RawLine, decimal? GramsPerMilliliter, string Book, bool InContainerUnit, decimal Quantity, int? UsdaCategoryId, string? Description, bool HasNutrients);

        /// <summary>The proposals the sizes that pass give: a book size for its book, at that book's era, and an era size for its era.</summary>
        public static List<ContainerSizeProposal> Propose(IEnumerable<CorpusSize> sizes, IReadOnlyDictionary<string, string> eraOfBook, Action<string>? refuse = null)
        {
            var proposals = new List<ContainerSizeProposal>();
            foreach (var s in sizes.Where(s => s.Passes))
            {
                var book = s.Tier == ContainerCorpusSizes.BookTier ? s.Scope : null;
                if (book is { Length: > 1023 })
                {
                    refuse?.Invoke(BookTooLong);
                    continue;
                }
                var era = book != null ? eraOfBook[book] : s.Scope;
                proposals.Add(new ContainerSizeProposal(s.IngredientId, s.Kind, era, s.SizeWord, s.Grams, null, ContainerCorpusSizes.SizeText(s),
                    ContainerCorpusSizes.SourceRef(s, era), ContainerCorpusSizes.Class(s.Tier, s.Kind), s.Tier == ContainerCorpusSizes.EraTier ? s.BandBooks : s.Band,
                    s.Votes == 0 ? null : Math.Round((decimal)s.Band / s.Votes, 4), s.Lines, book, ContainerCorpusSizes.Evidence(s)));
            }
            return proposals;
        }

        public async Task<Report> RunAsync(Options options, CancellationToken cancellationToken = default)
        {
            var report = new Report { DryRun = options.DryRun, Stamp = DateTime.UtcNow };
            var eras = await _db.RecipeSourceEras.AsNoTracking().ToDictionaryAsync(e => e.SourceAttribution, e => e.Era, cancellationToken);
            var lines = await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.Recipe.IsDeleted && (ri.Recipe.SourceSite == null || ri.Recipe.SourceSite == string.Empty) && ri.Recipe.SourceAttribution != null
                    && ri.Measurement != null
                    && (ri.Measurement.IsContainer || ri.Measurement.MeasurementCategoryId == RecipeNutritionService.CategoryMass || ri.Measurement.MeasurementCategoryId == RecipeNutritionService.CategoryVolume))
                .Select(ri => new BookLine(ri.IngredientId, ri.RawLine, ri.Ingredient.GramsPerMilliliter, ri.Recipe.SourceAttribution!, ri.Measurement.IsContainer, ri.Quantity,
                    ri.Ingredient.FdcCategoryId, ri.Ingredient.Description, ri.Ingredient.IngredientNutrients.Any()))
                .ToListAsync(cancellationToken);

            var eraOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var corpus = new List<CorpusLine>();
            var unsized = new List<(BookLine Line, string Era)>();
            foreach (var line in lines)
            {
                var era = ContainerEras.ForRecipe(null, line.Book, eras);
                if (era == ContainerEras.Modern) continue;
                report.BookLines++;
                eraOf[line.Book] = era;
                if (line.InContainerUnit)
                {
                    if (line.Quantity > 0) unsized.Add((line, era));
                    continue;
                }
                if (ContainerCorpusSizes.Read(line.Book, era, line.IngredientId, line.RawLine, line.GramsPerMilliliter) is { } read) corpus.Add(read);
            }
            report.SizedLines = corpus.Count;
            report.Books = corpus.Select(c => c.Book).Distinct().Count();

            var bookSizes = ContainerCorpusSizes.Book(corpus);
            var eraSizes = ContainerCorpusSizes.Era(corpus);
            report.BookSizes = bookSizes.Count(s => s.Passes);
            report.BookRefused = bookSizes.Count - report.BookSizes;
            report.EraSizes = eraSizes.Count(s => s.Passes);
            report.EraRefused = eraSizes.Count - report.EraSizes;
            foreach (var s in bookSizes.Concat(eraSizes).Where(s => !s.Passes))
                Refuse(report, $"{s.Tier}: {s.Refusal}");
            report.Proposals.AddRange(Propose(bookSizes.Concat(eraSizes), eraOf, reason => Refuse(report, reason)));

            if (options.FactsPath != null)
            {
                var ids = corpus.Select(c => c.IngredientId).Distinct().ToList();
                var fdc = await _db.Ingredients.AsNoTracking().Where(i => ids.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, i => i.FdcId, cancellationToken);
                await File.WriteAllLinesAsync(options.FactsPath,
                    ContainerCorpusSizes.Facts(bookSizes.Concat(eraSizes), id => fdc.GetValueOrDefault(id) is { Length: > 0 } f ? f : "#" + id.ToString(CultureInfo.InvariantCulture)),
                    cancellationToken);
                report.FactsWritten = options.FactsPath;
            }

            var standing = (await _db.IngredientContainerSizes.AsNoTracking().Where(r => r.Status != ContainerSizeStatus.Retired).ToListAsync(cancellationToken))
                .Where(r => ContainerCorpusSizes.TierOf(r.SourceRef) == null)
                .ToList();
            Cover(report, unsized, standing, bookSizes, eraSizes);

            report.Written = await _sizes.WriteAsync(report.Proposals, options.DryRun, report.Stamp, retireMissing: true, Nom.Data.SystemConstants.SystemPersonId,
                ContainerSizeService.ImportSources, cancellationToken);
            report.Scored = options.DryRun
                ? await _sizes.ScoreAsync(false, Nom.Data.SystemConstants.SystemPersonId, standing.Select(ContainerSizeService.AsProposal).Concat(report.Proposals).ToList(), cancellationToken)
                : await _sizes.ScoreAsync(true, Nom.Data.SystemConstants.SystemPersonId, null, cancellationToken);
            return report;
        }

        private static void Refuse(Report report, string reason) => report.RefusedByReason[reason] = report.RefusedByReason.GetValueOrDefault(reason) + 1;

        /// <summary>
        /// Counts how each unsized container line of an old or undated book would be sized: by the
        /// sources' standing sizes (any status) and the corpus sizes of this run, per product class
        /// (container kind and USDA category) and tier, with the worst-case error of each tier's
        /// sizes; a line no tier sizes is counted by its reason.
        /// </summary>
        public static void Cover(Report report, IReadOnlyList<(BookLine Line, string Era)> unsized, IReadOnlyList<Nom.Data.Nutrient.IngredientContainerSizeEntity> standing,
            IReadOnlyList<CorpusSize> bookSizes, IReadOnlyList<CorpusSize> eraSizes)
        {
            var plain = standing.GroupBy(r => r.IngredientId).ToDictionary(g => g.Key, g => g
                .Select(r => new ContainerSizeCandidate(r.ContainerKind, r.Era, r.SizeWord, r.NetGrams, r.DrainedGrams, r.SourceRef, r.Status, r.Id)).ToList());
            var books = bookSizes.Where(s => s.Passes).ToDictionary(s => (s.Scope, s.IngredientId, s.Kind, s.SizeWord));
            var eraBy = eraSizes.Where(s => s.Passes).ToDictionary(s => (s.Scope, s.IngredientId, s.Kind, s.SizeWord));
            var cells = new Dictionary<(string Class, string Tier), (int Lines, decimal? Worst)>();
            foreach (var (line, era) in unsized)
            {
                report.UnsizedLines++;
                var kind = ContainerKinds.FromText(line.RawLine);
                var productClass = $"{kind ?? "no container word"} / {(line.UsdaCategoryId is { } c && WordingReaders.CategoryNames.TryGetValue(c, out var name) ? name : "no USDA category")}";
                if (!line.HasNutrients)
                {
                    report.UncountedByReason[NoNutrients] = report.UncountedByReason.GetValueOrDefault(NoNutrients) + 1;
                    Add(productClass, "uncounted: " + NoNutrients, null);
                    continue;
                }
                var candidates = plain.GetValueOrDefault(line.IngredientId)?.ToList() ?? new List<ContainerSizeCandidate>();
                var word = ContainerKinds.SizeWord(line.RawLine);
                CorpusSize? fromBook = null, fromEra = null;
                if (kind != null && books.TryGetValue((line.Book, line.IngredientId, kind, word), out fromBook))
                    candidates.Add(new ContainerSizeCandidate(kind, era, word, fromBook.Grams, null, ContainerCorpusSizes.SourceRef(fromBook, era), ContainerSizeStatus.Active, -1, line.Book));
                if (kind != null && eraBy.TryGetValue((era, line.IngredientId, kind, word), out fromEra))
                    candidates.Add(new ContainerSizeCandidate(kind, era, word, fromEra.Grams, null, ContainerCorpusSizes.SourceRef(fromEra, era), ContainerSizeStatus.Active, -2));
                var weight = ContainerSizeResolver.Resolve(line.RawLine, era, candidates, line.Description, line.GramsPerMilliliter, activeOnly: false,
                    usdaCategoryId: line.UsdaCategoryId, book: line.Book);
                if (!weight.Counted)
                {
                    var reason = weight.Reason ?? ContainerSizeResolver.NoSize;
                    report.UncountedByReason[reason] = report.UncountedByReason.GetValueOrDefault(reason) + 1;
                    Add(productClass, "uncounted: " + reason, null);
                    continue;
                }
                var tier = ContainerBasis.OldBookTier(weight.Basis, era) ?? weight.Basis ?? "-";
                decimal? worst = weight.Basis switch
                {
                    ContainerBasis.SameBook => fromBook?.WorstCase,
                    ContainerBasis.SameEra => fromEra?.WorstCase,
                    ContainerBasis.PeriodNearer when kind != null => PeriodWorstCase(kind, word, line.UsdaCategoryId),
                    _ => null,
                };
                Add(productClass, tier, worst);
            }
            report.Coverage.AddRange(cells.OrderBy(c => c.Key.Class, StringComparer.Ordinal).ThenBy(c => c.Key.Tier, StringComparer.Ordinal)
                .Select(c => new ContainerCoverageRow(c.Key.Class, c.Key.Tier, c.Value.Lines, c.Value.Worst)));

            void Add(string productClass, string tier, decimal? worst)
            {
                var old = cells.GetValueOrDefault((productClass, tier));
                cells[(productClass, tier)] = (old.Lines + 1, worst is { } w ? Math.Max(old.Worst ?? 0m, w) : old.Worst);
            }
        }

        private static decimal? PeriodWorstCase(string kind, string word, int? category)
        {
            var period = CanSizeTable.Period(kind, word, category);
            if (period.Count < 2) return null;
            var low = period.Min(c => c.Grams);
            return Math.Round((period.Max(c => c.Grams) - low) / low, 3);
        }
    }
}
