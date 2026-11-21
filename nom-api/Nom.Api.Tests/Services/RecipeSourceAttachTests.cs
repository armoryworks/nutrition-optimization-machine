using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The source records of recipes imported before the staged import kept them are stored on
    /// the one recipe each became: found by URL, else by name and attribution, and only when the
    /// recipe's lines agree with the record's. The staged import stores the same text itself, and
    /// the undo removes what an attach stored.
    /// </summary>
    public class RecipeSourceAttachTests
    {
        private const long Operator = 2;
        private const string Book = "The Boston Cooking-School Cook Book (1896)";
        private static readonly DateTime Imported = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

        private sealed class Admin : ICurrentUserService
        {
            public long? PersonId => Operator;
            public long PersonIdOrSystem => Operator;
            public long RequiredPersonId => Operator;
            public string? UserId => "operator";
            public string RequiredUserId => "operator";
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static RecipeSourceService Service(ApplicationDbContext db) => new(db, new Admin());

        private static async Task<long> RecipeAsync(ApplicationDbContext db, string name, string? attribution, string? url, params string[] lines)
        {
            var food = new IngredientEntity { Name = $"food {Guid.NewGuid():N}", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            var recipe = new RecipeEntity
            {
                Name = name,
                SourceAttribution = attribution,
                SourceUrl = url,
                ScrapedAtUtc = Imported,
                RecipeIngredients = lines.Select((line, i) => new RecipeIngredientEntity
                {
                    IngredientId = food.Id, SortOrder = i, MeasurementId = 1, RawLine = line, CreatedDate = Imported,
                }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return recipe.Id;
        }

        private static ScraperRecipe Record(string name, string? url, params string[] lines) => new()
        {
            Name = name,
            SourceUrl = url,
            Ingredients = lines.Select(l => new ScraperIngredient { RawLine = l, Name = l }).ToList(),
            Steps = { new ScraperStep { Order = 1, Instruction = "Mix and bake." } },
        };

        private static RecipeSourceAttachRequestModel Batch(string? attribution, params ScraperRecipe[] records) =>
            new() { SourceAttribution = attribution, Records = records.ToList() };

        private static readonly string[] Gingerbread =
            { "1 cup molasses", "1/2 cup butter", "1 teaspoon soda", "2 cups flour", "1 tablespoon ginger" };

        [Fact]
        public async Task A_book_record_is_found_by_name_and_attribution_and_stored_with_its_checksum_and_journal()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            await RecipeAsync(db, "Gingerbread", "Another Book (1910)", null, Gingerbread);
            var record = Record("Gingerbread", null, Gingerbread);

            var result = await Service(db).AttachAsync(Batch(Book, record), apply: true);

            result.Should().BeEquivalentTo(new { Applied = true, Records = 1, Matched = 1, Documents = 1, Ambiguous = 0 });
            result.Results.Single().Should().BeEquivalentTo(new
            {
                Outcome = RecipeSourceAttachOutcome.Matched, MatchedBy = "name", RecipeId = recipe, Score = 1.0,
                Format = ScrapedDocumentFormat.StagedCookbook, Version = 1, Documents = 1,
            });
            var document = await db.ScrapedDocuments.SingleAsync();
            var content = SourceRecord.Content(record);
            document.Should().BeEquivalentTo(new
            {
                RecipeId = recipe, Format = ScrapedDocumentFormat.StagedCookbook, Version = 1, RawJsonLd = content,
                ContentSha256 = SourceRecord.Sha256(content), SourceUrl = string.Empty, CreatedByPersonId = (long?)Operator,
            });
            document.ContentSha256.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
            var journal = await db.AuditLogEntries.SingleAsync();
            journal.Should().BeEquivalentTo(new
            {
                EntityType = RecipeSourceService.DocumentEntityType, EntityId = document.Id, ChangeType = RecipeSourceService.AttachChangeType,
                PropertyName = ScrapedDocumentFormat.StagedCookbook, ChangedByPersonId = Operator, Timestamp = result.Stamp,
            });
            journal.NewValue.Should().Be($"recipe={recipe};version=1;sha256={document.ContentSha256};score=1;by=name");
        }

        [Fact]
        public async Task A_dry_run_reports_the_same_outcome_and_writes_nothing()
        {
            using var db = NewContext();
            await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var batch = Batch(Book, Record("Gingerbread", null, Gingerbread));

            var dryRun = await Service(db).AttachAsync(batch);

            dryRun.Should().BeEquivalentTo(new { Applied = false, Matched = 1, Documents = 1 });
            (await db.ScrapedDocuments.AnyAsync()).Should().BeFalse();
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();

            var applied = await Service(db).AttachAsync(batch, apply: true);

            applied.Should().BeEquivalentTo(dryRun, o => o.Excluding(r => r.Applied).Excluding(r => r.Stamp));
        }

        [Fact]
        public async Task A_web_record_is_found_by_its_normalized_url_and_brings_its_json_ld_when_the_recipe_has_none()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Weeknight Chili", "Recipe by A. Cook, example.com", "https://example.com/chili", "1 lb beef", "1 can beans");
            var record = Record("Weeknight Chili", "https://EXAMPLE.com/chili#comments", "1 lb beef", "1 can beans");
            record.RawJsonLd = """{"@type":"Recipe","name":"Weeknight Chili","recipeIngredient":["1 lb beef","1 can beans"]}""";

            var result = await Service(db).AttachAsync(Batch(null, record), apply: true);

            result.Results.Single().Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, MatchedBy = "url", RecipeId = recipe, Documents = 2 });
            var documents = await db.ScrapedDocuments.OrderBy(d => d.Format).ToListAsync();
            documents.Select(d => d.Format).Should().Equal(ScrapedDocumentFormat.JsonLd, ScrapedDocumentFormat.ScraperRecipe);
            documents[0].RawJsonLd.Should().Be(record.RawJsonLd);
            documents[0].ContentSha256.Should().Be(SourceRecord.Sha256(record.RawJsonLd));
            documents[1].RawJsonLd.Should().NotContain("rawJsonLd");
            documents.Should().OnlyContain(d => d.SourceUrl == "https://example.com/chili" && d.Version == 1);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == RecipeSourceService.AttachChangeType)).Should().Be(2);
        }

        [Fact]
        public async Task A_web_recipe_that_already_holds_its_json_ld_gets_only_the_record()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Chili", null, "https://example.com/chili", "1 lb beef");
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe, RawJsonLd = "{}", SourceUrl = "https://example.com/chili" });
            await db.SaveChangesAsync();
            var record = Record("Chili", "https://example.com/chili", "1 lb beef");
            record.RawJsonLd = """{"@type":"Recipe"}""";

            var result = await Service(db).AttachAsync(Batch(null, record), apply: true);

            result.Documents.Should().Be(1);
            (await db.ScrapedDocuments.CountAsync(d => d.Format == ScrapedDocumentFormat.JsonLd)).Should().Be(1);
        }

        [Fact]
        public async Task Lines_agree_through_fold_parts_entities_spacing_and_case()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Plum Pudding", Book, null,
                "1 cup suet + 1 cup raisins", "2  cups   FLOUR", "1/2 teaspoon salt &amp; spice", "1 cup milk");
            var record = Record("Plum Pudding", null, "1 cup suet", "1 cup raisins", "2 cups flour", "1/2 teaspoon salt & spice", "1 cup milk");

            var result = await Service(db).AttachAsync(Batch(Book, record));

            result.Results.Single().Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, RecipeId = recipe, Score = 1.0 });
        }

        [Fact]
        public async Task A_record_line_the_compound_split_wrote_as_parts_agrees_with_the_line_as_the_split_journal_holds_it()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "How to stew a HARE", Book, null,
                "1 hare", "A little flour", "A little butter", "salt", "pepper", "carrots", "turnips", "1 quart water");
            foreach (var (line, sort) in new[] { ("A little flour and butter", 1), ("salt and pepper", 3), ("carrots and turnips", 5) })
            {
                db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.RelinkEntityType, EntityId = recipe, ChangeType = CompoundLineService.SplitChangeType,
                    PropertyName = "RawLine", Timestamp = Imported,
                    OldValue = System.Text.Json.JsonSerializer.Serialize(new { i = 1, s = sort, q = 0, m = 1, r = line }),
                    NewValue = "[]",
                });
            }

            await db.SaveChangesAsync();
            var record = Record("How to stew a HARE", null, "1 hare", "A little flour and butter", "salt and pepper", "carrots and turnips", "1 quart water");

            var result = await Service(db).AttachAsync(Batch(Book, record));

            result.Results.Single().Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, RecipeId = recipe, Score = 1.0 });
        }

        [Fact]
        public async Task Fewer_than_four_in_five_agreeing_lines_is_a_low_score_and_nothing_is_stored()
        {
            using var db = NewContext();
            await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread.Take(3).Append("2 cups rye meal").Append("1 egg").ToArray());

            var low = await Service(db).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);

            low.Results.Single().Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.LowScore, RecipeId = (long?)null, Score = 0.6, Documents = 0 });
            (await db.ScrapedDocuments.AnyAsync()).Should().BeFalse();

            using var other = NewContext();
            await RecipeAsync(other, "Gingerbread", Book, null, Gingerbread.Take(4).Append("1 egg").ToArray());
            (await Service(other).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)))).Results.Single()
                .Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, Score = 0.8 });
        }

        [Fact]
        public async Task Two_recipes_of_one_name_in_one_book_are_ambiguous_when_their_lines_agree_equally()
        {
            using var db = NewContext();
            var first = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var second = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);

            var result = await Service(db).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);

            result.Results.Single().Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Ambiguous, RecipeId = (long?)null });
            result.Results.Single().Candidates.Select(c => c.RecipeId).Should().Equal(first, second);
            (await db.ScrapedDocuments.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task Two_recipes_of_one_name_in_one_book_each_get_the_record_whose_lines_they_hold()
        {
            using var db = NewContext();
            var soft = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var hard = await RecipeAsync(db, "Gingerbread", Book, null, "1 cup molasses", "1/2 cup butter", "1 teaspoon soda", "4 cups flour", "1 tablespoon ginger");

            var result = await Service(db).AttachAsync(Batch(Book,
                Record("Gingerbread", null, "1 cup molasses", "1/2 cup butter", "1 teaspoon soda", "4 cups flour", "1 tablespoon ginger"),
                Record("Gingerbread", null, Gingerbread)), apply: true);

            result.Results.Select(r => (r.Outcome, r.RecipeId)).Should().Equal(
                (RecipeSourceAttachOutcome.Matched, (long?)hard), (RecipeSourceAttachOutcome.Matched, (long?)soft));
            (await db.ScrapedDocuments.Select(d => d.RecipeId).ToListAsync()).Should().BeEquivalentTo(new[] { hard, soft });
        }

        [Fact]
        public async Task A_later_different_record_of_one_batch_on_a_recipe_an_earlier_one_took_is_a_double_match_and_not_stored()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var full = Record("Gingerbread", null, Gingerbread);
            var shorter = Record("Gingerbread", null, Gingerbread.Take(4).ToArray());

            var result = await Service(db).AttachAsync(Batch(Book, full, shorter), apply: true);

            result.Should().BeEquivalentTo(new { Matched = 1, DoubleMatch = 1, Documents = 1 });
            result.Results.Select(r => (r.Outcome, r.RecipeId, r.Documents)).Should().Equal(
                (RecipeSourceAttachOutcome.Matched, (long?)recipe, 1), (RecipeSourceAttachOutcome.DoubleMatch, (long?)recipe, 0));
            result.Results.Select(r => r.ContentSha256).Should().Equal(SourceRecord.Sha256(SourceRecord.Content(full)), SourceRecord.Sha256(SourceRecord.Content(shorter)));
            (await db.ScrapedDocuments.SingleAsync()).RawJsonLd.Should().Be(SourceRecord.Content(full), "the import kept the first record of a name and attribution");
        }

        [Fact]
        public async Task A_record_repeated_in_one_batch_is_stored_once_with_the_same_outcomes_as_when_the_batches_split_between_the_copies()
        {
            using var one = NewContext();
            var recipe = await RecipeAsync(one, "Gingerbread", Book, null, Gingerbread);
            using var two = NewContext();
            await RecipeAsync(two, "Gingerbread", Book, null, Gingerbread);

            var together = await Service(one).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread), Record("Gingerbread", null, Gingerbread)), apply: true);
            var first = await Service(two).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);
            var second = await Service(two).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);

            together.Results.Select(r => (r.Outcome, r.RecipeId, r.Documents)).Should().Equal(
                (RecipeSourceAttachOutcome.Matched, (long?)recipe, 1), (RecipeSourceAttachOutcome.AlreadyAttached, (long?)recipe, 0));
            first.Results.Concat(second.Results).Select(r => (r.Outcome, r.RecipeId, r.Documents)).Should().Equal(
                together.Results.Select(r => (r.Outcome, r.RecipeId, r.Documents)));
            (await one.ScrapedDocuments.CountAsync()).Should().Be(1);
            (await one.AuditLogEntries.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task A_repeated_web_record_brings_its_json_ld_once()
        {
            using var db = NewContext();
            await RecipeAsync(db, "Chili", null, "https://example.com/chili", "1 lb beef");
            var record = Record("Chili", "https://example.com/chili", "1 lb beef");
            record.RawJsonLd = """{"@type":"Recipe"}""";

            var result = await Service(db).AttachAsync(Batch(null, record, record), apply: true);

            result.Should().BeEquivalentTo(new { Matched = 1, AlreadyAttached = 1, Documents = 2 });
            (await db.ScrapedDocuments.CountAsync(d => d.Format == ScrapedDocumentFormat.JsonLd)).Should().Be(1);
        }

        [Fact]
        public async Task A_record_with_no_recipe_of_its_url_or_name_is_unmatched()
        {
            using var db = NewContext();
            await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);

            var result = await Service(db).AttachAsync(Batch(Book,
                Record("Seed Cakes", null, "1 cup sugar"), Record("Gingerbread", "https://example.com/gingerbread", Gingerbread)));

            result.Unmatched.Should().Be(1);
            result.Results[0].Outcome.Should().Be(RecipeSourceAttachOutcome.Unmatched);
            result.Results[1].Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, MatchedBy = "name" }, "a record whose URL no recipe holds falls back to name and attribution");
        }

        [Fact]
        public async Task A_second_run_of_the_same_batch_stores_nothing_again()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var batch = Batch(Book, Record("Gingerbread", null, Gingerbread));
            await Service(db).AttachAsync(batch, apply: true);

            var again = await Service(db).AttachAsync(batch, apply: true);

            again.Should().BeEquivalentTo(new { AlreadyAttached = 1, Matched = 0, Documents = 0 });
            again.Results.Single().RecipeId.Should().Be(recipe);
            (await db.ScrapedDocuments.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task A_different_record_for_a_recipe_that_holds_one_is_a_conflict_unless_a_new_version_is_asked_for()
        {
            using var db = NewContext();
            await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            await Service(db).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);
            var corrected = Record("Gingerbread", null, Gingerbread);
            corrected.Steps[0].Instruction = "Mix, pour into a shallow pan and bake twenty minutes.";

            var conflict = await Service(db).AttachAsync(Batch(Book, corrected), apply: true);

            conflict.Should().BeEquivalentTo(new { Conflict = 1, Documents = 0 });
            (await db.ScrapedDocuments.CountAsync()).Should().Be(1);

            var newer = await Service(db).AttachAsync(Batch(Book, corrected), apply: true, newVersion: true);

            newer.Results.Single().Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, Version = 2 });
            (await db.ScrapedDocuments.OrderBy(d => d.Id).Select(d => d.Version).ToListAsync()).Should().Equal(1, 2);
        }

        [Fact]
        public async Task A_newer_extraction_pinned_to_its_recipe_is_stored_as_the_next_version_although_its_lines_differ()
        {
            using var db = NewContext();
            var id = await RecipeAsync(db, "Pasta frolla", Book, null, "Burro - 100g", "Farina - 200g");
            var unpinned = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            await Service(db).AttachAsync(Batch(Book, Record("Pasta frolla", null, "Burro - 100g", "Farina - 200g")), apply: true);
            var english = Record("Shortcrust pastry", null, "100 grams of butter", "200 grams of flour");
            var pinned = Batch(Book, english, Record("Gingerbread", null, "a stranger's lines"));
            pinned.RecipeIds = new List<long?> { id, unpinned };

            var lowScore = await Service(db).AttachAsync(Batch(Book, english), apply: false, newVersion: true);
            var result = await Service(db).AttachAsync(pinned, apply: true, newVersion: true);

            lowScore.Results.Single().Outcome.Should().Be(RecipeSourceAttachOutcome.Unmatched, "unpinned, the new name finds no recipe");
            result.Results[0].Should().BeEquivalentTo(new { Outcome = RecipeSourceAttachOutcome.Matched, MatchedBy = RecipeSourceService.PinnedBy, RecipeId = id, Version = 2 });
            result.Results[1].Outcome.Should().Be(RecipeSourceAttachOutcome.Unmatched, "a pin replaces only a record its recipe already holds");
            (await db.ScrapedDocuments.Where(d => d.RecipeId == id).OrderBy(d => d.Version).Select(d => d.Version).ToListAsync()).Should().Equal(1, 2);
        }

        [Fact]
        public async Task Two_records_pinned_to_one_recipe_are_both_a_double_match_and_neither_is_stored()
        {
            using var db = NewContext();
            var id = await RecipeAsync(db, "Risotto", Book, null, "Riso - 300g", "Funghi - 200g");
            await Service(db).AttachAsync(Batch(Book, Record("Risotto", null, "Riso - 300g", "Funghi - 200g")), apply: true);
            var pinned = Batch(Book, Record("Risotto with mushrooms", null, "300 grams of rice", "200 grams of mushrooms"),
                Record("Risotto with mushrooms", null, "250 grams of rice", "2 eggs"));
            pinned.RecipeIds = new List<long?> { id, id };

            var result = await Service(db).AttachAsync(pinned, apply: true, newVersion: true);

            result.Results.Select(r => r.Outcome).Should().Equal(RecipeSourceAttachOutcome.DoubleMatch, RecipeSourceAttachOutcome.DoubleMatch);
            result.Results.Should().OnlyContain(r => r.RecipeId == id);
            (await db.ScrapedDocuments.CountAsync(d => d.RecipeId == id)).Should().Be(1, "neither record replaces the recipe's source");
        }

        [Fact]
        public async Task The_undo_removes_what_an_attach_stored_journals_it_and_leaves_import_documents()
        {
            using var db = NewContext();
            var recipe = await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var other = await RecipeAsync(db, "Chili", null, "https://example.com/chili", "1 lb beef");
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = other, RawJsonLd = "{}", SourceUrl = "https://example.com/chili" });
            await db.SaveChangesAsync();
            var since = DateTime.UtcNow.AddSeconds(-1);
            var batch = Batch(Book, Record("Gingerbread", null, Gingerbread));
            await Service(db).AttachAsync(batch, apply: true);
            db.ChangeTracker.Clear();

            var dryRun = await Service(db).UndoAsync(since);
            db.ChangeTracker.Clear();

            dryRun.Should().BeEquivalentTo(new { Applied = false, Removed = 1, Skipped = 0 });
            (await db.ScrapedDocuments.CountAsync()).Should().Be(2);

            var undo = await Service(db).UndoAsync(since, apply: true);
            db.ChangeTracker.Clear();

            undo.Should().BeEquivalentTo(new { Applied = true, Removed = 1, Skipped = 0 });
            (await db.ScrapedDocuments.Select(d => d.RecipeId).ToListAsync()).Should().Equal(other);
            var removed = await db.ScrapedDocuments.IgnoreQueryFilters().SingleAsync(d => d.RecipeId == recipe);
            removed.Should().BeEquivalentTo(new { IsDeleted = true, DeletedByPersonId = (long?)Operator });
            var journal = await db.AuditLogEntries.SingleAsync(a => a.ChangeType == RecipeSourceService.UndoChangeType);
            journal.Should().BeEquivalentTo(new { EntityType = RecipeSourceService.DocumentEntityType, EntityId = removed.Id, PropertyName = ScrapedDocumentFormat.StagedCookbook });
            journal.OldValue.Should().StartWith($"recipe={recipe};version=1;");

            (await Service(db).UndoAsync(since, apply: true)).Should().BeEquivalentTo(new { Removed = 0, Skipped = 0, AlreadyUndone = 1 },
                "a repeated undo reports the documents it already removed apart from skipped ones (K13)");
            (await Service(db).AttachAsync(batch, apply: true)).Should().BeEquivalentTo(new { Matched = 1, Documents = 1 }, "an undone attach can be applied again");
        }

        [Fact]
        public async Task The_undo_counts_a_document_removed_some_other_way_as_skipped_not_as_already_undone()
        {
            using var db = NewContext();
            await RecipeAsync(db, "Gingerbread", Book, null, Gingerbread);
            var since = DateTime.UtcNow.AddSeconds(-1);
            await Service(db).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);
            db.ChangeTracker.Clear();
            var document = await db.ScrapedDocuments.SingleAsync();
            document.IsDeleted = true;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await Service(db).UndoAsync(since)).Should().BeEquivalentTo(new { Removed = 0, Skipped = 1, AlreadyUndone = 0 });
        }

        [Fact]
        public async Task The_staged_import_stores_the_record_it_received_so_a_later_attach_finds_it_there()
        {
            using var db = NewContext();
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(Operator);
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            var scraping = new RecipeScrapingService(
                db, currentUser.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance);
            var book = Record("Gingerbread", null, Gingerbread);
            static ScraperRecipe Web()
            {
                var record = Record("Chili", "https://example.com/chili", "1 pound beef", "1 can beans");
                record.Ingredients[0].Quantity = 1m;
                record.Ingredients[0].Unit = "pound";
                record.RawJsonLd = """{"@type":"Recipe","name":"Chili"}""";
                return record;
            }

            var web = Web();
            var bookText = SourceRecord.Content(book);
            var webText = SourceRecord.Content(web);

            var imported = await scraping.ImportStagedAsync(new StagedImportRequestModel { Recipes = { book }, SourceAttribution = Book, PublicDomain = true });
            await scraping.ImportStagedAsync(new StagedImportRequestModel { Recipes = { web } });

            imported.Imported.Should().Be(1);
            var documents = await db.ScrapedDocuments.Include(d => d.Recipe).OrderBy(d => d.Id).ToListAsync();
            documents.Select(d => (d.Recipe!.Name, d.Format)).Should().Equal(
                ("Gingerbread", ScrapedDocumentFormat.StagedCookbook), ("Chili", ScrapedDocumentFormat.JsonLd), ("Chili", ScrapedDocumentFormat.ScraperRecipe));
            documents[0].RawJsonLd.Should().Be(bookText);
            documents[0].ContentSha256.Should().Be(SourceRecord.Sha256(bookText));
            documents[1].ContentSha256.Should().Be(SourceRecord.Sha256(Web().RawJsonLd!));
            documents[2].RawJsonLd.Should().Be(webText, "the record is stored as received, before the import normalizes its amounts");
            documents[2].SourceUrl.Should().Be("https://example.com/chili");
            documents.Should().OnlyContain(d => d.Version == 1);

            var attach = await Service(db).AttachAsync(Batch(Book, Record("Gingerbread", null, Gingerbread)), apply: true);
            var webAttach = await Service(db).AttachAsync(Batch(null, Web()), apply: true);

            attach.AlreadyAttached.Should().Be(1);
            webAttach.AlreadyAttached.Should().Be(1);
            (await db.ScrapedDocuments.CountAsync()).Should().Be(3);
        }
    }
}
