using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Every applied USDA link is journaled in the audit log and can be taken back from it alone,
    /// and the lanes hold a name match or model pick that a deterministic rule catches for a person.
    /// Wordings and descriptions in the rule tests are the judged failures the rules come from.
    /// </summary>
    public class LinkApplyTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private sealed class NoAudit : IFoodCatalogAuditService
        {
            public Task<FoodCatalogAuditResult> AuditAsync(string? source = null, int limit = 5000) => throw new NotSupportedException();
        }

        private sealed class MovingCleanup : ICatalogCleanupService
        {
            private readonly ApplicationDbContext _db;
            public bool Merges { get; set; } = true;

            public MovingCleanup(ApplicationDbContext db) => _db = db;

            public Task<CatalogCleanupPreview> PreviewAsync(int sampleSize = 50) => throw new NotSupportedException();
            public Task<CatalogCleanupResult> ApplyAsync(int maxActions = 500) => throw new NotSupportedException();
            public Task<CatalogNormalizeResult> NormalizeAsync(bool apply) => throw new NotSupportedException();

            public async Task<bool> MergeIntoAsync(long sourceId, long targetId, long? personId, bool ignoreProposals = false, long? keepProposalId = null)
            {
                if (!Merges) return false;
                var shifts = new List<LinkApplyJournal.LineShift>();
                var moved = new List<LinkApplyJournal.LinePlace>();
                foreach (var line in await _db.RecipeIngredients.Where(ri => ri.IngredientId == sourceId).OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ToListAsync())
                {
                    var recipe = await _db.RecipeIngredients.Where(ri => ri.RecipeId == line.RecipeId).ToListAsync();
                    var position = recipe.Any(ri => ri.IngredientId == targetId && ri.SortOrder == line.SortOrder) ? recipe.Max(ri => ri.SortOrder) + 1 : line.SortOrder;
                    if (position != line.SortOrder) shifts.Add(new LinkApplyJournal.LineShift { RecipeId = line.RecipeId, OldSortOrder = line.SortOrder, SortOrder = position });
                    moved.Add(new LinkApplyJournal.LinePlace { RecipeId = line.RecipeId, SortOrder = position });
                    _db.RecipeIngredients.Remove(line);
                    _db.RecipeIngredients.Add(new RecipeIngredientEntity
                    {
                        RecipeId = line.RecipeId, IngredientId = targetId, SortOrder = position, Quantity = line.Quantity, MeasurementId = line.MeasurementId, RawLine = line.RawLine,
                    });
                    await _db.SaveChangesAsync();
                }

                var stale = await _db.FoodCatalogProposals
                    .Where(p => (p.IngredientId == sourceId || p.IngredientId == targetId) && p.Field == FoodCatalogReviewService.FdcLinkField
                        && p.Status != FoodProposalStatus.Applied && p.Id != keepProposalId
                        && (p.ReviewedByPersonId == null || p.ReviewedByPersonId == SystemConstants.SystemPersonId))
                    .ToListAsync();
                var now = DateTime.UtcNow;
                var person = personId ?? SystemConstants.SystemPersonId;
                if (keepProposalId is { } proposalId)
                {
                    _db.AuditLogEntries.AddRange(stale.Select(p => LinkApplyJournal.Removed(proposalId, p, now, person)));
                    _db.AuditLogEntries.AddRange(LinkApplyJournal.LinesMoved(proposalId, sourceId, targetId, shifts, moved, now, person));
                }
                _db.FoodCatalogProposals.RemoveRange(stale);
                var source = await _db.Ingredients.FindAsync(sourceId);
                source!.IsDeleted = true;
                source.DeletedAt = now;
                await _db.SaveChangesAsync();
                return true;
            }
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static LinkAutoApplyGuard.Catalog Catalog(Dictionary<string, long>? curated = null, params (string Word, int Count)[] leads) => new()
        {
            Curated = new Dictionary<string, long>(curated ?? new Dictionary<string, long>(), StringComparer.OrdinalIgnoreCase),
            CuratedNames = (curated ?? new Dictionary<string, long>()).ToDictionary(kv => kv.Value, kv => kv.Key),
            LeadWords = leads.ToDictionary(l => l.Word, l => l.Count),
        };

        private static string? Hold(string wording, string description, bool nameMatch, LinkAutoApplyGuard.Catalog? catalog = null) =>
            LinkAutoApplyGuard.Hold(new LinkAutoApplyGuard.Pick(wording, 1, 2, description, description, nameMatch), catalog ?? Catalog());

        [Theory]
        [InlineData("salt and pepper")]
        [InlineData("salt & pepper")]
        [InlineData("flour + sugar")]
        [InlineData("bread with butter")]
        [InlineData("lemon or lime juice")]
        [InlineData("flour, sugar")]
        [InlineData("onion; garlic")]
        public void A_wording_that_names_more_than_one_food_is_held(string wording)
        {
            Hold(wording, "Salt, table", nameMatch: false).Should().Be(LinkAutoApplyGuard.CompoundRule);
        }

        [Theory]
        [InlineData("half-and-half", "Cream, fluid, half and half")]
        [InlineData("butter, softened", "Butter, salted")]
        [InlineData("chicken breasts, boneless, skinless", "Chicken breast, boneless")]
        [InlineData("onion, finely chopped", "Onions, raw")]
        public void A_preparation_note_after_a_comma_is_not_a_second_food(string wording, string description)
        {
            Hold(wording, description, nameMatch: false).Should().BeNull();
        }

        [Theory]
        [InlineData("cake chocolate", "Cake, chocolate", true)]
        [InlineData("gravy beef", "Gravy, beef", true)]
        [InlineData("chocolate cake", "Cake, chocolate", false)]
        [InlineData("black pepper", "Spices, pepper, black", false)]
        [InlineData("olive oil", "Oil, olive, salad or cooking", false)]
        [InlineData("butter", "Butter, salted", false)]
        [InlineData("unsalted butter", "Butter, without salt", false)]
        public void A_name_match_in_the_usda_word_order_is_held(string wording, string description, bool held)
        {
            Hold(wording, description, nameMatch: true).Should().Be(held ? LinkAutoApplyGuard.WordOrderRule : null);
            Hold(wording, description, nameMatch: false).Should().BeNull("the word-order rule is for name matches only");
        }

        [Theory]
        [InlineData("chopped tarragon", "Spices, tarragon, dried", "spices", 40, "dried")]
        [InlineData("gravy beef", "Gravy, beef, canned", "gravy", 6, "canned")]
        [InlineData("rice", "Rice, white, long-grain, regular, cooked", "rice", 30, "cooked")]
        [InlineData("peas", "Peas, green, frozen, unprepared", "peas", 9, "frozen")]
        [InlineData("dried tarragon", "Spices, tarragon, dried", "spices", 40, null)]
        [InlineData("ground beef", "Beef, ground, 80% lean meat / 20% fat, raw", "beef", 200, null)]
        [InlineData("powdered sugar", "Sugars, powdered", "sugars", 8, null)]
        [InlineData("confectioners sugar", "Sugars, powdered", "sugars", 8, null)]
        [InlineData("chopped tarragon", "Spices, tarragon, dried", "spices", 1, null)]
        public void A_usda_state_the_wording_does_not_mention_is_held_when_the_food_has_siblings(string wording, string description, string lead, int foods, string? state)
        {
            Hold(wording, description, nameMatch: false, Catalog(null, (lead, foods)))
                .Should().Be(state == null ? null : $"{LinkAutoApplyGuard.StateRule} ({state})");
        }

        [Fact]
        public void A_wording_that_is_the_name_of_another_curated_food_is_held_but_not_its_own_food()
        {
            var catalog = Catalog(new Dictionary<string, long> { ["Sea Salt"] = 9 });

            LinkAutoApplyGuard.Hold(new LinkAutoApplyGuard.Pick("sea salt", 1, 2, "Salt, table", "Salt, table", false), catalog)
                .Should().Be($"{LinkAutoApplyGuard.OtherFoodRule} (Sea Salt)");
            LinkAutoApplyGuard.Hold(new LinkAutoApplyGuard.Pick("sea salt", 9, 2, "Salt, table", "Salt, table", false), catalog)
                .Should().BeNull("a curated seed food linked to its USDA record names itself");
        }

        [Fact]
        public void A_merge_journals_each_line_from_its_old_place_to_its_new_one()
        {
            var at = DateTime.UtcNow;
            var rows = LinkApplyJournal.LinesMoved(7, 100, 200,
                new[] { new LinkApplyJournal.LineShift { RecipeId = 1, OldSortOrder = 0, SortOrder = 5 } },
                new[] { new LinkApplyJournal.LinePlace { RecipeId = 2, SortOrder = 1 }, new LinkApplyJournal.LinePlace { RecipeId = 1, SortOrder = 5 } },
                at, 3).ToList();

            rows.Select(r => (r.EntityId, r.PropertyName, r.OldValue, r.NewValue)).Should().Equal(
                (7L, LinkApplyJournal.Line, "1:100@0", "1:200@5"),
                (7L, LinkApplyJournal.Line, "2:100@1", "2:200@1"));
            LinkApplyJournal.ReadPlace("1:200@5").Should().Be((1L, 200L, 5));
            LinkApplyJournal.ReadPlace("200@5").Should().BeNull();
            LinkApplyJournal.ReadOnFood("Alias@42").Should().Be(("Alias", 42L));
            LinkApplyJournal.ReadOnFood(LinkApplyJournal.Line).Should().BeNull();
        }

        private sealed record Seeded(IngredientEntity Salt, IngredientEntity SeaSalt, FoodCatalogProposalEntity Proposal, RecipeEntity First, RecipeEntity Second, FoodCatalogProposalEntity Rejection);

        private static async Task<Seeded> SeedSeaSaltAsync(ApplicationDbContext db)
        {
            var salt = new IngredientEntity { Name = "Salt, table", FdcId = "173468", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            var seaSalt = new IngredientEntity { Name = "sea salt", CurationStatusId = 9000 };
            db.Ingredients.AddRange(salt, seaSalt);
            var first = new RecipeEntity { Name = "Brine" };
            var second = new RecipeEntity { Name = "Focaccia" };
            db.Recipes.AddRange(first, second);
            await db.SaveChangesAsync();
            db.RecipeIngredients.AddRange(
                new RecipeIngredientEntity { RecipeId = first.Id, IngredientId = salt.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1, RawLine = "1 tsp salt" },
                new RecipeIngredientEntity { RecipeId = first.Id, IngredientId = seaSalt.Id, SortOrder = 0, Quantity = 2, MeasurementId = 1, RawLine = "2 tsp sea salt" },
                new RecipeIngredientEntity { RecipeId = second.Id, IngredientId = seaSalt.Id, SortOrder = 1, Quantity = 3, MeasurementId = 1, RawLine = "3 tsp sea salt" });
            var rejection = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = seaSalt.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "sea salt",
                Source = "ai:fake", Batch = IngredientLinkService.Batch, Status = FoodProposalStatus.Rejected, Reason = "No USDA match", CreatedDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = seaSalt.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "sea salt",
                FdcId = salt.FdcId, ProposedValue = salt.Id.ToString(), Source = "ai:fake", Batch = IngredientLinkService.Batch, Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.AddRange(rejection, proposal);
            await db.SaveChangesAsync();
            return new Seeded(salt, seaSalt, proposal, first, second, rejection);
        }

        private static async Task<List<(long Recipe, long Food, int Sort, decimal Quantity)>> LinesAsync(ApplicationDbContext db) =>
            (await db.RecipeIngredients.IgnoreQueryFilters().ToListAsync())
                .Select(ri => (ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.Quantity))
                .OrderBy(l => l.RecipeId).ThenBy(l => l.SortOrder).ThenBy(l => l.IngredientId)
                .ToList();

        [Fact]
        public async Task An_approved_link_is_journaled_and_the_undo_puts_back_lines_food_aliases_status_name_and_proposals()
        {
            using var db = NewContext();
            var seeded = await SeedSeaSaltAsync(db);
            var linesBefore = await LinesAsync(db);
            var start = DateTime.UtcNow.AddSeconds(-1);
            var review = new FoodCatalogReviewService(db, new NoAudit(), new MovingCleanup(db));

            (await review.ApplyProposalAsync(seeded.Proposal.Id, reviewerPersonId: 2)).Should().BeTrue();
            db.ChangeTracker.Clear();

            var journal = await db.AuditLogEntries.Where(a => a.ChangeType == LinkApplyJournal.ChangeType).ToListAsync();
            journal.Should().OnlyContain(a => a.EntityType == LinkApplyJournal.EntityType && a.EntityId == seeded.Proposal.Id);
            journal.Select(a => a.PropertyName).Should().BeEquivalentTo(new[]
            {
                LinkApplyJournal.Header, $"CurationStatusId@{seeded.Salt.Id}", LinkApplyJournal.Line, LinkApplyJournal.Line, LinkApplyJournal.DeletedProposal,
                $"Name@{seeded.SeaSalt.Id}", $"Name@{seeded.Salt.Id}", $"Alias@{seeded.Salt.Id}",
            });
            (await LinesAsync(db)).Should().Equal(
                (seeded.First.Id, seeded.Salt.Id, 0, 1m), (seeded.First.Id, seeded.Salt.Id, 1, 2m), (seeded.Second.Id, seeded.Salt.Id, 1, 3m));
            (await db.Ingredients.FindAsync(seeded.Salt.Id))!.Name.Should().Be("Sea Salt");

            var undo = new LinkApplyUndoService(db, new NoUser());
            var dryRun = await undo.UndoAsync(start);

            dryRun.Should().BeEquivalentTo(new
            {
                Applied = false, Proposals = 1, ProposalsSkipped = 0, LinesRestored = 2, LinesSkipped = 0, FoodsRestored = 1,
                AliasesRemoved = 1, AliasesRestored = 0, StatusesRestored = 1, NamesRestored = 1, ProposalsRestored = 1, Remaining = 0,
            });
            (await db.FoodCatalogProposals.FindAsync(seeded.Proposal.Id))!.Status.Should().Be(FoodProposalStatus.Applied, "a dry run writes nothing");
            db.ChangeTracker.Clear();

            var done = await undo.UndoAsync(start, apply: true);
            db.ChangeTracker.Clear();

            done.Applied.Should().BeTrue();
            done.Proposals.Should().Be(1);
            (await LinesAsync(db)).Should().Equal(linesBefore);
            var seaSalt = await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == seeded.SeaSalt.Id);
            seaSalt.Should().Match<IngredientEntity>(i => !i.IsDeleted && i.Name == "sea salt");
            var salt = await db.Ingredients.SingleAsync(i => i.Id == seeded.Salt.Id);
            salt.Should().Match<IngredientEntity>(i => i.Name == "Salt, table" && i.CurationStatusId == 9001);
            (await db.IngredientAliases.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            var proposal = await db.FoodCatalogProposals.SingleAsync(p => p.Id == seeded.Proposal.Id);
            proposal.Should().Match<FoodCatalogProposalEntity>(p => p.Status == FoodProposalStatus.Pending && p.ReviewedByPersonId == null && p.ReviewedAt == null);
            var restored = await db.FoodCatalogProposals.SingleAsync(p => p.Id != seeded.Proposal.Id);
            restored.Should().Match<FoodCatalogProposalEntity>(p => p.IngredientId == seeded.SeaSalt.Id && p.Status == FoodProposalStatus.Rejected
                && p.Reason == "No USDA match" && p.CreatedDate == seeded.Rejection.CreatedDate);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkApplyJournal.ChangeType)).Should().Be(0);
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == LinkApplyJournal.UndoChangeType)).EntityId.Should().Be(seeded.Proposal.Id);

            (await undo.UndoAsync(start, apply: true)).Proposals.Should().Be(0, "nothing is left to take back");
            (await review.ApplyProposalAsync(seeded.Proposal.Id, reviewerPersonId: 2)).Should().BeTrue("the proposal can be applied again");
        }

        [Fact]
        public async Task A_line_edited_since_the_link_stays_where_it_is_and_is_counted()
        {
            using var db = NewContext();
            var seeded = await SeedSeaSaltAsync(db);
            var start = DateTime.UtcNow.AddSeconds(-1);
            await new FoodCatalogReviewService(db, new NoAudit(), new MovingCleanup(db)).ApplyProposalAsync(seeded.Proposal.Id, SystemConstants.SystemPersonId);
            db.ChangeTracker.Clear();
            var moved = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == seeded.Second.Id);
            db.RecipeIngredients.Remove(moved);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await new LinkApplyUndoService(db, new NoUser()).UndoAsync(start, apply: true);

            result.Should().BeEquivalentTo(new { Proposals = 1, LinesRestored = 1, LinesSkipped = 1, FoodsRestored = 1 });
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == seeded.First.Id && ri.IngredientId == seeded.SeaSalt.Id)).SortOrder.Should().Be(0);
        }

        [Fact]
        public async Task The_undo_takes_only_the_links_of_the_named_source_since_the_given_time()
        {
            using var db = NewContext();
            var seeded = await SeedSeaSaltAsync(db);
            var start = DateTime.UtcNow.AddSeconds(-1);
            await new FoodCatalogReviewService(db, new NoAudit(), new MovingCleanup(db)).ApplyProposalAsync(seeded.Proposal.Id, SystemConstants.SystemPersonId);
            db.ChangeTracker.Clear();
            var undo = new LinkApplyUndoService(db, new NoUser());

            (await undo.UndoAsync(start, source: IngredientLinkService.ExactSource)).Proposals.Should().Be(0);
            (await undo.UndoAsync(DateTime.UtcNow.AddMinutes(1))).Proposals.Should().Be(0);
            (await undo.UndoAsync(start, systemOnly: true)).Proposals.Should().Be(1);
            (await undo.UndoAsync(start, source: "ai:")).Proposals.Should().Be(1);
        }

        [Fact]
        public async Task A_link_that_copies_usda_values_is_undone_by_removing_only_the_copied_rows_and_measures()
        {
            using var db = NewContext();
            var salted = new IngredientEntity { Name = "Butter, salted", FdcId = "173430", FdcDataType = "sr_legacy_food", CurationStatusId = 9003, GramsPerItem = 14.2m };
            var seedButter = new IngredientEntity { Name = "Butter", CurationStatusId = 9003 };
            db.Ingredients.AddRange(salted, seedButter);
            await db.SaveChangesAsync();
            db.IngredientNutrients.AddRange(
                new IngredientNutrientEntity { IngredientId = salted.Id, NutrientId = 5035, Amount = 717m, MeasurementId = 16 },
                new IngredientNutrientEntity { IngredientId = salted.Id, NutrientId = 5002, Amount = 81m, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = seedButter.Id, NutrientId = 5002, Amount = 80m, MeasurementId = 1 });
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = seedButter.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "Butter",
                FdcId = salted.FdcId, ProposedValue = salted.Id.ToString(), Source = IngredientLinkService.ExactSource, Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            var start = DateTime.UtcNow.AddSeconds(-1);

            (await new FoodCatalogReviewService(db, new NoAudit(), new MovingCleanup(db) { Merges = false }).ApplyProposalAsync(proposal.Id, SystemConstants.SystemPersonId)).Should().BeTrue();
            db.ChangeTracker.Clear();
            (await db.IngredientNutrients.CountAsync(n => n.IngredientId == seedButter.Id)).Should().Be(2);
            (await db.Ingredients.FindAsync(seedButter.Id))!.GramsPerItem.Should().Be(14.2m);
            db.ChangeTracker.Clear();

            var result = await new LinkApplyUndoService(db, new NoUser()).UndoAsync(start, apply: true);
            db.ChangeTracker.Clear();

            result.Should().BeEquivalentTo(new { Proposals = 1, NutrientsRemoved = 1, MeasuresRestored = 1, FoodsRestored = 0, StatusesKept = 0 });
            var rows = await db.IngredientNutrients.Where(n => n.IngredientId == seedButter.Id).ToListAsync();
            rows.Should().ContainSingle().Which.Amount.Should().Be(80m, "the food's own value stays");
            (await db.Ingredients.FindAsync(seedButter.Id))!.GramsPerItem.Should().BeNull();
            (await db.Ingredients.FindAsync(salted.Id))!.CurationStatusId.Should().Be(9003);
        }

        [Fact]
        public async Task The_usda_food_stays_curated_while_another_applied_link_stands_on_it()
        {
            using var db = NewContext();
            var seeded = await SeedSeaSaltAsync(db);
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = 999, Field = FoodCatalogReviewService.FdcLinkField, FdcId = seeded.Salt.FdcId,
                ProposedValue = seeded.Salt.Id.ToString(), Source = IngredientLinkService.StapleSource, Status = FoodProposalStatus.Applied,
            });
            await db.SaveChangesAsync();
            var start = DateTime.UtcNow.AddSeconds(-1);
            await new FoodCatalogReviewService(db, new NoAudit(), new MovingCleanup(db)).ApplyProposalAsync(seeded.Proposal.Id, SystemConstants.SystemPersonId);
            db.ChangeTracker.Clear();

            var result = await new LinkApplyUndoService(db, new NoUser()).UndoAsync(start, apply: true);

            result.Should().BeEquivalentTo(new { Proposals = 1, StatusesRestored = 0, StatusesKept = 1 });
            (await db.Ingredients.FindAsync(seeded.Salt.Id))!.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
        }

        private static async Task<(IngredientEntity Cake, FoodCatalogProposalEntity Held, FoodCatalogProposalEntity Clean, FoodCatalogProposalEntity Staple)> SeedNameMatchesAsync(ApplicationDbContext db)
        {
            var cake = new IngredientEntity { Name = "Cake, chocolate", FdcId = "1", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            var flour = new IngredientEntity { Name = "Wheat flour, white, all-purpose", FdcId = "2", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            db.Ingredients.AddRange(cake, flour);
            await db.SaveChangesAsync();
            FoodCatalogProposalEntity Pick(long source, string wording, IngredientEntity target, string provenance) => new()
            {
                Action = FoodProposalAction.Update, IngredientId = source, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = wording,
                FdcId = target.FdcId, ProposedValue = target.Id.ToString(), Source = provenance, Status = FoodProposalStatus.Pending, Reason = $"→ {target.Name}",
            };
            var held = Pick(100, "cake chocolate", cake, IngredientLinkService.ExactSource);
            var clean = Pick(101, "chocolate cake", cake, IngredientLinkService.ExactSource);
            var staple = Pick(102, "flour", flour, IngredientLinkService.StapleSource);
            db.FoodCatalogProposals.AddRange(held, clean, staple);
            await db.SaveChangesAsync();
            return (cake, held, clean, staple);
        }

        [Fact]
        public async Task A_held_name_match_is_marked_for_a_person_once_and_left_out_of_the_lanes_queue()
        {
            using var db = NewContext();
            var (_, held, clean, staple) = await SeedNameMatchesAsync(db);
            var gate = new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions()));

            var screened = await gate.ScreenAsync(new[] { held.Id, clean.Id, staple.Id });

            screened.Allowed.Should().Equal(clean.Id, staple.Id);
            screened.Held.Should().Equal(held.Id);
            (await db.FoodCatalogProposals.FindAsync(held.Id))!.Reason.Should().Be($"→ Cake, chocolate{LinkAutoApplyGuard.HoldMark}{LinkAutoApplyGuard.WordOrderRule}");
            (await db.FoodCatalogProposals.FindAsync(held.Id))!.Status.Should().Be(FoodProposalStatus.Pending);
            (await gate.ScreenAsync(new[] { held.Id })).Held.Should().Equal(held.Id);
            (await db.FoodCatalogProposals.FindAsync(held.Id))!.Reason.Should().EndWith(LinkAutoApplyGuard.WordOrderRule).And.NotContain(LinkAutoApplyGuard.WordOrderRule + LinkAutoApplyGuard.HoldMark);
            (await gate.PendingNameMatchesAsync(10, Array.Empty<long>(), Array.Empty<string>())).Should().Equal(clean.Id);
        }

        [Fact]
        public async Task The_hold_report_counts_what_the_rules_would_hold_and_writes_nothing()
        {
            using var db = NewContext();
            var (_, held, _, _) = await SeedNameMatchesAsync(db);
            var gate = new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions()));

            var report = await gate.HoldReportAsync();

            report.Should().BeEquivalentTo(new { Examined = 2, Held = 1, AlreadyMarked = 0 });
            report.ByRule.Should().Equal(new Dictionary<string, int> { [LinkAutoApplyGuard.WordOrderRule] = 1 });
            report.BySource.Should().Equal(new Dictionary<string, int> { [IngredientLinkService.ExactSource] = 1 });
            report.Samples.Single().ProposalId.Should().Be(held.Id);
            (await db.FoodCatalogProposals.FindAsync(held.Id))!.Reason.Should().NotContain(LinkAutoApplyGuard.HoldMark);
        }

        [Fact]
        public async Task Name_matches_have_their_own_daily_allowance_and_per_food_share()
        {
            using var db = NewContext();
            for (var i = 0; i < 3; i++)
            {
                db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    Action = FoodProposalAction.Update, IngredientId = 200 + i, Field = FoodCatalogReviewService.FdcLinkField, FdcId = "173468", ProposedValue = "1",
                    Source = IngredientLinkService.ExactSource, Status = FoodProposalStatus.Applied, ReviewedByPersonId = SystemConstants.SystemPersonId, ReviewedAt = DateTime.UtcNow.AddHours(-1),
                });
            }
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = 300, Field = FoodCatalogReviewService.FdcLinkField, FdcId = "999", ProposedValue = "2",
                Source = "ai:fake", Status = FoodProposalStatus.Applied, ReviewedByPersonId = SystemConstants.SystemPersonId, ReviewedAt = DateTime.UtcNow.AddHours(-1),
            });
            await db.SaveChangesAsync();
            var gate = new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions()));

            var allowance = await gate.AllowanceAsync(IngredientLinkService.ExactSource, dailyCap: 5, perTargetCap: 3);

            allowance.Remaining.Should().Be(2, "model picks count against their own allowance, not this one");
            allowance.BusyTargets.Should().Equal("173468");
            (await gate.AllowanceAsync(IngredientLinkService.ExactSource, dailyCap: 0, perTargetCap: 3)).Remaining.Should().Be(0);
        }
    }
}
