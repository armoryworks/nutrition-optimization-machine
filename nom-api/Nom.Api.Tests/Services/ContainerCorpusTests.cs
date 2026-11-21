using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Import.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Sizes for unsized packages, boxes, bottles, cans and jars in old or undated books, derived from
    /// the recipe corpus before any cited period source or today's size: the same book's sized lines
    /// of the food and container, then the same era's books, each with its evidence; the resolver's
    /// order of tiers; the corpus run that writes them in shadow with a journal; the hold-out that
    /// scores each tier with the line left out; and the weighing, which takes a tier's size only
    /// while that tier is live.
    /// </summary>
    public class ContainerCorpusTests
    {
        private const long Ounce = 6, Package = 22, Can = 7, Kcal = 16;
        private const long Calories = 5035;
        private const long Operator = 2;
        private const string BookA = "A Plain Cook Book (1910)";
        private const string BookB = "The Household Book (1925)";
        private const string BookC = "Church Supper Receipts (1931)";

        private static CorpusLine Line(string book, decimal grams, string era = ContainerEras.Historic, long food = 1, string kind = ContainerKinds.Package, string word = "") =>
            new(book, era, food, kind, word, grams);

        [Fact]
        public void A_book_s_own_sized_lines_give_its_package_size_and_two_sizes_stated_once_each_give_none()
        {
            var one = ContainerCorpusSizes.BookSize(BookA, new[] { Line(BookA, 85m) })!;
            (one.Passes, one.Grams, one.Lines, one.WorstCase).Should().Be((true, 85, 1, 0m));

            var mostly = ContainerCorpusSizes.BookSize(BookA, new[] { Line(BookA, 85m), Line(BookA, 85m), Line(BookA, 226.8m) })!;
            (mostly.Passes, mostly.Grams, mostly.Sizes).Should().Be((true, 85, "85:2;227:1"));
            mostly.WorstCase.Should().Be(1.668m, "an 8 oz line in the same book is 167 % over the 3 oz size taken");

            var tie = ContainerCorpusSizes.BookSize(BookA, new[] { Line(BookA, 85m), Line(BookA, 226.8m) })!;
            (tie.Passes, tie.Refusal).Should().Be((false, ContainerModeRule.RunnerUpClose));
        }

        [Fact]
        public void An_era_counts_each_book_once_per_size_needs_two_books_and_keeps_its_spread()
        {
            var lines = Enumerable.Range(0, 5).Select(_ => Line(BookA, 226.8m)).Append(Line(BookB, 85m)).Append(Line(BookC, 85m)).ToList();
            var era = ContainerCorpusSizes.EraSize(ContainerEras.Historic, lines)!;
            (era.Passes, era.Grams, era.Votes, era.Band, era.Books, era.Lines).Should().Be((true, 85, 3, 2, 3, 7), "book A's five 8 oz lines vote once");
            (era.Min, era.Max, era.Sizes).Should().Be((85m, 226.8m, "85:2;227:1"));

            var alone = ContainerCorpusSizes.EraSize(ContainerEras.Historic, new[] { Line(BookA, 85m), Line(BookA, 85m) })!;
            (alone.Passes, alone.Refusal).Should().Be((false, "fewer than two books"));

            var evidence = ContainerCorpusSizes.Evidence(era);
            evidence.Should().Contain("85:2;227:1").And.Contain("7 lines in 3 books").And.Contain("spread 85-226.8 g");
            ContainerCorpusSizes.SourceRef(era, ContainerEras.Historic).Should().Be("deterministic:corpus-era:container-corpus/1:historic:85g:2/3:85-226.8g");
        }

        [Fact]
        public void Only_a_sized_container_line_of_an_old_or_undated_book_is_mined()
        {
            ContainerCorpusSizes.Read(BookA, ContainerEras.Historic, 1, "1 3-oz. package cream cheese", null)!.Grams.Should().Be(85m);
            ContainerCorpusSizes.Read(BookA, ContainerEras.Unknown, 1, "2 (3 oz.) packages cream cheese", null)!.Grams.Should().Be(85m, "the grams of one package");
            ContainerCorpusSizes.Read(BookA, ContainerEras.Historic, 1, "1 large package (8 oz.) cream cheese", null)!.SizeWord.Should().Be(ContainerKinds.Large);
            ContainerCorpusSizes.Read(null, ContainerEras.Historic, 1, "1 (3 oz) package cream cheese", null).Should().BeNull("a web recipe is not a book");
            ContainerCorpusSizes.Read(BookA, ContainerEras.Modern, 1, "1 (3 oz) package cream cheese", null).Should().BeNull("a modern book takes today's sizes");
            ContainerCorpusSizes.Read(BookA, ContainerEras.Historic, 1, "1 package cream cheese", null).Should().BeNull("no size stated");
            ContainerCorpusSizes.Read(BookA, ContainerEras.Historic, 1, "3 oz cream cheese", null).Should().BeNull("no container");
            ContainerCorpusSizes.Read(BookA, ContainerEras.Historic, 1, "1 No. 2 can corn", null).Should().BeNull("a numbered can is sized by the can table");
        }

        [Fact]
        public void Leaving_a_line_out_reads_the_other_lines_of_its_book_or_the_other_books_of_its_era()
        {
            var own = Line(BookA, 85m);
            var lines = new List<CorpusLine> { own, Line(BookA, 85m), Line(BookB, 226.8m), Line(BookC, 226.8m) };
            ContainerCorpusSizes.LeaveOut(ContainerCorpusSizes.BookTier, own, lines)!.Grams.Should().Be(85);
            ContainerCorpusSizes.LeaveOut(ContainerCorpusSizes.EraTier, own, lines)!.Grams.Should().Be(227, "book A's lines are left out of its own era answer");
            ContainerCorpusSizes.LeaveOut(ContainerCorpusSizes.BookTier, own, new[] { own }).Should().BeNull("a line never answers itself");
        }

        private static ContainerSizeCandidate Size(string kind, decimal net, string era = ContainerEras.Modern, string source = "test", string? book = null) =>
            new(kind, era, string.Empty, net, null, source, ContainerSizeStatus.Active, 0, book);

        private static ContainerSizeCandidate Corpus(string tier, decimal net, string era = ContainerEras.Historic, string? book = null, string kind = ContainerKinds.Package)
        {
            var size = new CorpusSize(tier, book ?? era, 1, kind, string.Empty, (int)net, 2, 1, 2, 2, 0, net, net, $"{(int)net}:2", null);
            return Size(kind, net, era, ContainerCorpusSizes.SourceRef(size, era), book);
        }

        [Fact]
        public void An_unsized_line_in_an_old_book_takes_its_book_then_its_era_then_a_cited_source_then_today_s_size()
        {
            var sizes = new[]
            {
                Size(ContainerKinds.Package, 227m),
                Corpus(ContainerCorpusSizes.BookTier, 85m, book: BookA),
                Corpus(ContainerCorpusSizes.BookTier, 113m, book: BookB),
                Corpus(ContainerCorpusSizes.EraTier, 99m),
            };
            var own = ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Historic, sizes, book: BookA);
            (own.Grams, own.Basis).Should().Be((85m, ContainerBasis.SameBook));
            (ContainerSizeResolver.Resolve("2 packages cream cheese", ContainerEras.Historic, sizes, book: BookB).Grams).Should().Be(226m, "two of book B's own packages");
            var era = ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Historic, sizes, book: BookC);
            (era.Grams, era.Basis).Should().Be((99m, ContainerBasis.SameEra), "another book's size is never taken; the era's is");
            ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Unknown, sizes, book: BookC).Basis.Should().Be(ContainerBasis.ModernFallback, "an era size answers its own era only");
            ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Modern, sizes, book: BookA).Grams.Should().Be(227m, "a modern recipe never takes a corpus size");
            ContainerSizeResolver.Resolve("1 (8 oz) package cream cheese", ContainerEras.Historic, sizes, book: BookA).Grams.Should().Be(226.8m, "a stated size wins");

            var wine = ContainerSizeResolver.Resolve("1 bottle claret", ContainerEras.Historic,
                new[] { Size(ContainerKinds.Bottle, 746m, ContainerEras.Any), Corpus(ContainerCorpusSizes.EraTier, 473m, kind: ContainerKinds.Bottle) }, book: BookC);
            (wine.Grams, wine.Basis).Should().Be((473m, ContainerBasis.SameEra), "the books of the time come before a standard that holds in any era");

            var corn = ContainerSizeResolver.Resolve("1 can corn", ContainerEras.Historic, new[] { Size(ContainerKinds.Can, 432m, source: "x") }, usdaCategoryId: 11, book: BookA);
            corn.Basis.Should().Be(ContainerBasis.PeriodNearer, "with no corpus size the cited period source comes before today's size");
        }

        [Fact]
        public void A_drained_line_scales_a_corpus_size_by_the_food_s_drained_share_and_stays_uncounted_without_one()
        {
            var sizes = new[] { new ContainerSizeCandidate(ContainerKinds.Can, ContainerEras.Modern, string.Empty, 432m, 298m, "fdc:1:portion:1"), Corpus(ContainerCorpusSizes.BookTier, 567m, book: BookA, kind: ContainerKinds.Can) };
            ContainerSizeResolver.Resolve("1 can corn, drained", ContainerEras.Historic, sizes, book: BookA).Grams.Should().Be(391.1m, "567 x 298/432");
            ContainerSizeResolver.Resolve("1 can mushrooms", ContainerEras.Historic, new[] { Corpus(ContainerCorpusSizes.BookTier, 227m, book: BookA, kind: ContainerKinds.Can) },
                "Mushrooms, canned, drained solids", book: BookA).Reason.Should().Be(ContainerSizeResolver.NoDrained);
        }

        [Fact]
        public void Each_corpus_and_fallback_size_is_weighed_under_its_own_old_book_tier()
        {
            ContainerBasis.OldBookTier(ContainerBasis.SameBook, ContainerEras.Historic).Should().Be(ContainerSizeService.SameBookTier);
            ContainerBasis.OldBookTier(ContainerBasis.SameEra, ContainerEras.Unknown).Should().Be(ContainerSizeService.SameEraTier);
            ContainerBasis.OldBookTier(ContainerBasis.PeriodNearer, ContainerEras.Historic).Should().Be(ContainerSizeService.PeriodTier);
            ContainerBasis.OldBookTier(ContainerBasis.PeriodSize, ContainerEras.Unknown).Should().Be(ContainerSizeService.PeriodTier);
            ContainerBasis.OldBookTier(ContainerBasis.PeriodSize, ContainerEras.Historic).Should().BeNull("a dated old book's historic size is its own era");
            ContainerBasis.OldBookTier(ContainerBasis.ModernFallback, ContainerEras.Historic).Should().Be(ContainerSizeService.ModernTier);
            ContainerBasis.OldBookTier(ContainerBasis.AnyEra, ContainerEras.Historic).Should().BeNull();
            ContainerSizeService.OldBookTiers.Should().Equal(ContainerSizeService.SameBookTier, ContainerSizeService.SameEraTier, ContainerSizeService.PeriodTier, ContainerSizeService.ModernTier);
        }

        [Fact]
        public void The_cook_reads_which_corpus_tier_gave_the_size()
        {
            var book = ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Historic, new[] { Corpus(ContainerCorpusSizes.BookTier, 85m, book: BookA) }, book: BookA);
            ContainerWeighing.Describe(book).Should().Be("1 package ≈ 3 oz (85 g): the size this book states for it elsewhere (2 of 2 sized lines); estimated");
            var era = ContainerSizeResolver.Resolve("1 package cream cheese", ContainerEras.Historic, new[] { Corpus(ContainerCorpusSizes.EraTier, 85m) }, book: BookC);
            ContainerWeighing.Describe(era).Should().Be("1 package ≈ 3 oz (85 g): the most common size books of its time state (2 of 2 books, spread 85-85g); estimated");
        }

        private sealed class Rig
        {
            public required ApplicationDbContext Db { get; init; }
            public required ContainerSizeService Sizes { get; init; }
            public required LaneFrameService Frame { get; init; }
            public required ContainerCorpusImportService Corpus { get; init; }
            public long Cheese { get; init; }
        }

        private static async Task<Rig> RigAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Ounce, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1, BaseUnitConversionFactor = 28.3495m },
                new BaseMeasurementEntity { Id = Package, Name = "Package", Symbol = "pkg", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.Add(new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal });
            var cheese = new IngredientEntity { Name = "Cream Cheese", Description = "Cheese, cream", FdcId = "173418", FdcDataType = "sr_legacy_food", FdcCategoryId = 1, CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(cheese);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = cheese.Id, NutrientId = Calories, Amount = 350m, MeasurementId = Kcal });
            await db.SaveChangesAsync();
            var undo = new List<ILaneUndo>();
            var frame = new LaneFrameService(db, undo, NullLogger<LaneFrameService>.Instance);
            var sizes = new ContainerSizeService(db, frame, NullLogger<ContainerSizeService>.Instance);
            undo.Add(new ContainerSizeLaneUndo(sizes));
            return new Rig { Db = db, Sizes = sizes, Frame = frame, Corpus = new ContainerCorpusImportService(db, sizes), Cheese = cheese.Id };
        }

        private static async Task<long> RecipeAsync(Rig rig, string? book, params (decimal Quantity, long Unit, string Line)[] lines)
        {
            var recipe = new RecipeEntity { Name = "Cheese dish", AuthorId = 1, Servings = 1, SourceAttribution = book, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            rig.Db.Recipes.Add(recipe);
            await rig.Db.SaveChangesAsync();
            var order = 0;
            foreach (var (quantity, unit, line) in lines)
                rig.Db.RecipeIngredients.Add(new RecipeIngredientEntity
                {
                    RecipeId = recipe.Id, IngredientId = rig.Cheese, Quantity = quantity, MeasurementId = unit, RawLine = line, SortOrder = order,
                    CreatedDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(order++),
                });
            await rig.Db.SaveChangesAsync();
            return recipe.Id;
        }

        private static async Task OldBookAsync(Rig rig, string book, int year)
        {
            rig.Db.RecipeSourceEras.Add(new RecipeSourceEraEntity { SourceAttribution = book, PublishedYear = year, Era = ContainerEras.FromYear(year) });
            await rig.Db.SaveChangesAsync();
        }

        [Fact]
        public async Task The_corpus_run_writes_book_and_era_sizes_in_shadow_with_their_evidence_and_journal_and_the_undo_takes_them_back()
        {
            var rig = await RigAsync();
            await OldBookAsync(rig, BookA, 1910);
            await OldBookAsync(rig, BookB, 1925);
            await RecipeAsync(rig, BookA, (3m, Ounce, "1 (3 oz) package cream cheese"), (1m, Package, "1 package cream cheese"));
            await RecipeAsync(rig, BookB, (3m, Ounce, "1 3-oz. package cream cheese"));
            await RecipeAsync(rig, "Church Supper Receipts", (1m, Package, "1 package cream cheese"));
            await RecipeAsync(rig, null, (8m, Ounce, "1 (8 oz) package cream cheese"));

            var dry = await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: true));
            (dry.SizedLines, dry.BookSizes, dry.EraSizes).Should().Be((2, 2, 1));
            (await rig.Db.IngredientContainerSizes.CountAsync()).Should().Be(0, "a dry run writes nothing");
            dry.UnsizedLines.Should().Be(2);
            dry.Coverage.Should().Contain(new ContainerCoverageRow("package / Dairy and Egg Products", ContainerSizeService.SameBookTier, 1, 0m))
                .And.Contain(r => r.Tier.StartsWith("uncounted: ", StringComparison.Ordinal) && r.Lines == 1, "an undated book has no era size and the food no modern package size");

            var run = await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));
            run.Written!.Added.Should().Be(3);
            var rows = await rig.Db.IngredientContainerSizes.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            rows.Should().OnlyContain(r => r.Status == ContainerSizeStatus.Shadow || r.Status == ContainerSizeStatus.Active);
            var bookRow = rows.Single(r => r.SourceAttribution == BookA);
            (bookRow.ContainerKind, bookRow.Era, bookRow.NetGrams, bookRow.Class).Should().Be((ContainerKinds.Package, ContainerEras.Historic, 85m, "corpus-book:package"));
            bookRow.SourceRef.Should().StartWith(ContainerCorpusSizes.BookSourcePrefix);
            bookRow.Evidence.Should().Contain("85:1");
            var eraRow = rows.Single(r => r.SourceAttribution == null);
            (eraRow.Era, eraRow.NetGrams, eraRow.SourcesAgreeing).Should().Be((ContainerEras.Historic, 85m, 2));
            (await rig.Db.AuditLogEntries.CountAsync(a => a.ChangeType == ContainerSizeService.SetChange)).Should().Be(3);
            (await rig.Db.AuditLogEntries.Where(a => a.EntityId == bookRow.Id).Select(a => a.PropertyName).FirstAsync()).Should().Contain("/book:");

            (await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false))).Written!.Unchanged.Should().Be(3);
            var import = await rig.Sizes.WriteAsync(Array.Empty<ContainerSizeProposal>(), false, DateTime.UtcNow, true, Operator, ContainerSizeService.CorpusSources);
            import.Retired.Should().Be(0, "a USDA import never retires a corpus size");

            var undone = await rig.Sizes.UndoSinceAsync(run.Stamp, true, Operator);
            undone.Skipped.Should().Be(0);
            (await rig.Db.IngredientContainerSizes.CountAsync(r => r.Status != ContainerSizeStatus.Retired)).Should().Be(0);
        }

        [Fact]
        public async Task The_corpus_run_retires_a_size_no_line_gives_any_more_and_never_a_usda_source_s_row()
        {
            var rig = await RigAsync();
            await OldBookAsync(rig, BookA, 1910);
            await rig.Sizes.WriteAsync(new[] { new ContainerSizeProposal(rig.Cheese, ContainerKinds.Package, ContainerEras.Modern, string.Empty, 227m, null, "8 oz", "deterministic:fdc-branded-mode:2025-12-18:cream-cheese:200/300", "cream-cheese") },
                false, DateTime.UtcNow, true, Operator);
            var recipe = await RecipeAsync(rig, BookA, (3m, Ounce, "1 (3 oz) package cream cheese"));
            await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));
            (await rig.Db.IngredientContainerSizes.CountAsync(r => r.SourceAttribution == BookA && r.Status != ContainerSizeStatus.Retired)).Should().Be(1);

            rig.Db.Recipes.Remove(await rig.Db.Recipes.SingleAsync(r => r.Id == recipe));
            await rig.Db.SaveChangesAsync();
            var again = await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));
            again.Written!.Retired.Should().Be(1);
            (await rig.Db.IngredientContainerSizes.CountAsync(r => r.Status != ContainerSizeStatus.Retired)).Should().Be(1, "the branded size stays");
        }

        [Fact]
        public async Task The_hold_out_scores_the_same_book_tier_with_each_line_left_out_and_qualifies_it_on_its_own()
        {
            var rig = await RigAsync();
            await OldBookAsync(rig, BookA, 1910);
            await RecipeAsync(rig, BookA, Enumerable.Range(0, 60).Select(_ => (3m, Ounce, "1 (3 oz) package cream cheese")).ToArray());
            await RecipeAsync(rig, BookB, Enumerable.Range(0, 60).Select(_ => (8m, Ounce, "1 (8 oz) package cream cheese")).ToArray());
            await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));

            var report = await rig.Sizes.ScoreAsync(true, Operator);

            var book = report.Tiers.Single(t => t.Tier == ContainerSizeService.SameBookTier);
            (book.Lines, book.Agree, book.Mode).Should().Be((120, 120, LaneMode.Live), "each line is answered by the other 59 lines of its book");
            report.Tiers.Single(t => t.Tier == ContainerSizeService.SameEraTier).Lines.Should().Be(0, "every line has its book's answer first");
            (await rig.Db.LaneDecisions.CountAsync(d => d.Tier == ContainerSizeService.SameBookTier)).Should().Be(120);
            (await rig.Db.IngredientContainerSizes.Where(r => r.SourceAttribution != null).Select(r => r.Status).ToListAsync())
                .Should().OnlyContain(s => s == ContainerSizeStatus.Active, "each book key agrees on all 60 of its lines");
        }

        [Fact]
        public async Task The_same_era_tier_is_scored_on_lines_of_other_books_and_stays_in_shadow_while_they_disagree()
        {
            var rig = await RigAsync();
            for (var i = 0; i < 30; i++) await RecipeAsync(rig, $"Undated Book {i:00}", (3m, Ounce, "1 (3 oz) package cream cheese"));
            for (var i = 0; i < 30; i++) await RecipeAsync(rig, $"Undated Book {i + 30:00}", (8m, Ounce, "1 (8 oz) package cream cheese"));
            await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));
            (await rig.Db.IngredientContainerSizes.CountAsync(r => r.SourceAttribution == null)).Should().Be(0, "half of the books say 3 oz and half 8 oz: no clear mode");

            var agree = await RigAsync();
            for (var i = 0; i < 60; i++) await RecipeAsync(agree, $"Undated Book {i:00}", (3m, Ounce, "1 (3 oz) package cream cheese"));
            await agree.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));
            var report = await agree.Sizes.ScoreAsync(true, Operator);
            var era = report.Tiers.Single(t => t.Tier == ContainerSizeService.SameEraTier);
            (era.Lines, era.Agree, era.Mode).Should().Be((60, 60, LaneMode.Live), "each line is answered by the other 59 books");
        }

        [Fact]
        public async Task An_old_book_takes_its_own_book_s_size_only_while_the_same_book_tier_is_live_and_another_book_never_does()
        {
            var rig = await RigAsync();
            await OldBookAsync(rig, BookA, 1910);
            await RecipeAsync(rig, BookA, (3m, Ounce, "1 (3 oz) package cream cheese"));
            var unsized = await RecipeAsync(rig, BookA, (1m, Package, "1 package cream cheese"));
            await OldBookAsync(rig, BookC, 1931);
            var other = await RecipeAsync(rig, BookC, (1m, Package, "1 package cream cheese"));
            await rig.Corpus.RunAsync(new ContainerCorpusImportService.Options(DryRun: false));
            foreach (var row in rig.Db.IngredientContainerSizes) row.Status = ContainerSizeStatus.Active;
            rig.Db.LaneStates.Add(new LaneStateEntity { Lane = AiLanes.ContainerSizes, Tier = LaneTiers.Whole, Mode = LaneMode.Live, Since = DateTime.UtcNow });
            await rig.Db.SaveChangesAsync();

            async Task<ContainerWeight> WeighAsync(long id)
            {
                var recipe = await rig.Db.Recipes.AsNoTracking().Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Measurement).Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Ingredient)
                    .SingleAsync(r => r.Id == id);
                return (await ContainerWeighing.ForRecipeAsync(rig.Db, recipe)).Weigh(recipe.RecipeIngredients!.Single())!;
            }

            (await WeighAsync(unsized)).Reason.Should().Be(ContainerWeighing.OtherEraNotLive, "the same-book tier has no row, so it is in shadow");
            rig.Db.LaneStates.Add(new LaneStateEntity { Lane = AiLanes.ContainerSizes, Tier = ContainerSizeService.SameBookTier, Mode = LaneMode.Live, Since = DateTime.UtcNow });
            await rig.Db.SaveChangesAsync();
            var own = await WeighAsync(unsized);
            (own.Grams, own.Basis).Should().Be((85m, ContainerBasis.SameBook));
            var elsewhere = await WeighAsync(other);
            elsewhere.Basis.Should().NotBe(ContainerBasis.SameBook, "book A's size is never another book's");
            elsewhere.Counted.Should().BeFalse("book C has no size of its own and the era tier is not live");
        }
    }
}
