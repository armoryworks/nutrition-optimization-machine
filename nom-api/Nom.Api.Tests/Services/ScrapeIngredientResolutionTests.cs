using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Which catalog row an imported ingredient name lands on: a live ingredient of that name,
    /// else the one food a rule or a person gave the wording to, else a new uncurated ingredient.
    /// An alias nobody vouched for is not followed. Foods and wordings here are real prod rows.
    /// </summary>
    public class ScrapeIngredientResolutionTests
    {
        private const long Importer = 7;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static RecipeScrapingService NewService(ApplicationDbContext db)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(Importer);
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new System.Collections.Generic.List<string>());

            return new RecipeScrapingService(
                db, currentUser.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance);
        }

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name, CurationStatusEnum status = CurationStatusEnum.Curated, bool deleted = false)
        {
            var food = new IngredientEntity { Name = name, CurationStatusId = (long)status, IsDeleted = deleted };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static async Task AliasAsync(ApplicationDbContext db, IngredientEntity food, string wording, string? context)
        {
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = food.Id, AliasName = wording, SourceContext = context });
            await db.SaveChangesAsync();
        }

        private static async Task ClassifiedAsync(ApplicationDbContext db)
        {
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = IngredientLinkService.AliasEntityType, ChangeType = IngredientLinkService.AliasTrustChangeType, EntityId = 999,
                PropertyName = nameof(IngredientAliasEntity.SourceContext), OldValue = "other", NewValue = "deterministic:recheck:name-key", Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        private static async Task<IngredientEntity> ImportAsync(ApplicationDbContext db, string ingredientName)
        {
            var recipeName = $"Recipe {Guid.NewGuid():N}";
            var result = await NewService(db).ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes =
                {
                    new ScraperRecipe
                    {
                        Name = recipeName,
                        Ingredients = { new ScraperIngredient { Name = ingredientName, RawLine = $"1 cup {ingredientName}" } },
                    },
                },
            });

            result.Failures.Should().BeEmpty();
            result.Imported.Should().Be(1);
            var line = await db.RecipeIngredients.SingleAsync(ri => ri.Recipe.Name == recipeName);
            return await db.Ingredients.SingleAsync(i => i.Id == line.IngredientId);
        }

        [Fact]
        public async Task A_name_that_is_a_live_ingredient_links_to_it_before_any_alias_is_tried()
        {
            using var db = NewContext();
            var salt = await FoodAsync(db, "Salt");
            await AliasAsync(db, salt, "kosher salt", "admin:link:ai:fake");
            var kosher = await FoodAsync(db, "Kosher Salt", CurationStatusEnum.NonCurated);

            var landed = await ImportAsync(db, "kosher salt");

            landed.Id.Should().Be(kosher.Id);
        }

        [Fact]
        public async Task Of_two_ingredients_whose_names_differ_only_by_case_the_curated_one_is_taken()
        {
            using var db = NewContext();
            await FoodAsync(db, "white sauce", CurationStatusEnum.NonCurated);
            var curated = await FoodAsync(db, "White Sauce");
            await FoodAsync(db, "WHITE SAUCE", CurationStatusEnum.PendingCuration);

            var landed = await ImportAsync(db, "white sauce");

            landed.Id.Should().Be(curated.Id);
        }

        [Fact]
        public async Task An_alias_with_no_context_is_not_followed_and_the_name_becomes_a_new_uncurated_ingredient()
        {
            using var db = NewContext();
            var salt = await FoodAsync(db, "Salt");
            await AliasAsync(db, salt, "kosher salt", null);
            await ClassifiedAsync(db);

            var landed = await ImportAsync(db, "Kosher salt");

            landed.Id.Should().NotBe(salt.Id);
            landed.Name.Should().Be("Kosher salt");
            landed.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
            landed.CreatedByPersonId.Should().Be(Importer);
        }

        [Fact]
        public async Task Until_the_aliases_are_classified_an_alias_with_no_context_is_followed_as_before_and_no_row_is_created()
        {
            using var db = NewContext();
            var salt = await FoodAsync(db, "Salt");
            await AliasAsync(db, salt, "kosher salt", null);
            var cream = await FoodAsync(db, "Light Cream");
            var heavy = await FoodAsync(db, "Heavy Cream");
            await AliasAsync(db, cream, "cream", null);
            await AliasAsync(db, heavy, "cream", "admin:link:ai:fake");

            (await ImportAsync(db, "kosher salt")).Id.Should().Be(salt.Id);
            (await ImportAsync(db, "cream")).Name.Should().Be("cream", "a wording two foods claim is still followed by neither");
            await ClassifiedAsync(db);
            (await ImportAsync(db, "Kosher Salt")).Id.Should().NotBe(salt.Id);
        }

        [Fact]
        public async Task An_alias_a_model_pick_left_is_not_followed()
        {
            using var db = NewContext();
            var cayenne = await FoodAsync(db, "Cayenne Pepper");
            await AliasAsync(db, cayenne, "salt and pepper", "link:ai:fake");

            var landed = await ImportAsync(db, "salt and pepper");

            landed.Id.Should().NotBe(cayenne.Id);
            landed.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
        }

        [Theory]
        [InlineData("fdc:description")]
        [InlineData("retired:12")]
        [InlineData("link:deterministic:staple")]
        [InlineData("link:deterministic:name-match")]
        [InlineData("deterministic:recheck:name-key")]
        [InlineData("admin:link:ai:fake")]
        public async Task An_alias_from_a_rule_or_a_person_is_followed(string context)
        {
            using var db = NewContext();
            var eggs = await FoodAsync(db, "Eggs");
            await AliasAsync(db, eggs, "large eggs", context);

            var landed = await ImportAsync(db, "2 Large Eggs");

            landed.Id.Should().Be(eggs.Id);
            (await db.Ingredients.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task A_trusted_alias_text_owned_by_two_foods_is_not_followed()
        {
            using var db = NewContext();
            var sweet = await FoodAsync(db, "Sweet Red Peppers");
            var bell = await FoodAsync(db, "Red Bell Pepper");
            await AliasAsync(db, sweet, "red bell peppers", "deterministic:recheck:name-match");
            await AliasAsync(db, bell, "Red Bell Peppers", "deterministic:recheck:name-key");

            var landed = await ImportAsync(db, "red bell peppers");

            landed.Id.Should().NotBe(sweet.Id).And.NotBe(bell.Id);
            landed.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
        }

        [Fact]
        public async Task A_trusted_alias_that_only_one_food_holds_live_is_followed_past_a_removed_one()
        {
            using var db = NewContext();
            var sweet = await FoodAsync(db, "Sweet Red Peppers");
            var bell = await FoodAsync(db, "Red Bell Pepper");
            db.IngredientAliases.Add(new IngredientAliasEntity
            {
                IngredientId = sweet.Id, AliasName = "red bell peppers", SourceContext = "deterministic:recheck:name-match", IsDeleted = true,
            });
            await AliasAsync(db, bell, "red bell peppers", "deterministic:recheck:name-key");

            var landed = await ImportAsync(db, "red bell peppers");

            landed.Id.Should().Be(bell.Id);
        }

        [Fact]
        public async Task A_trusted_alias_on_a_soft_deleted_food_is_not_followed()
        {
            using var db = NewContext();
            var gone = await FoodAsync(db, "Well Water", deleted: true);
            await AliasAsync(db, gone, "spring water", "retired:12");

            var landed = await ImportAsync(db, "spring water");

            landed.Id.Should().NotBe(gone.Id);
            landed.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task A_soft_deleted_ingredient_holding_the_name_is_renamed_out_of_the_way_and_journaled()
        {
            using var db = NewContext();
            var holder = await FoodAsync(db, "crispy fried onions", CurationStatusEnum.NonCurated, deleted: true);

            var landed = await ImportAsync(db, "crispy fried onions");

            landed.Id.Should().NotBe(holder.Id, "the retired row is not brought back; its applied link would keep the lane from ever proposing it again");
            landed.Name.Should().Be("crispy fried onions");
            var renamed = await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == holder.Id);
            renamed.Name.Should().Be($"crispy fried onions (deleted #{holder.Id})");
            renamed.IsDeleted.Should().BeTrue();
            var entry = await db.AuditLogEntries.SingleAsync();
            entry.Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(e =>
                e.EntityType == RecipeScrapingService.NameFreedEntityType && e.ChangeType == RecipeScrapingService.NameFreedChangeType
                && e.EntityId == holder.Id && e.PropertyName == nameof(IngredientEntity.Name)
                && e.OldValue == "crispy fried onions" && e.NewValue == renamed.Name && e.ChangedByPersonId == Importer);
        }

        [Fact]
        public async Task A_name_no_soft_deleted_ingredient_holds_is_taken_without_a_journal_entry()
        {
            using var db = NewContext();
            await FoodAsync(db, "Crispy Fried Onions (deleted #5)", CurationStatusEnum.NonCurated, deleted: true);

            await ImportAsync(db, "crispy fried onions");

            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task A_second_import_of_the_same_untrusted_wording_reuses_the_ingredient_the_first_one_created()
        {
            using var db = NewContext();
            var cream = await FoodAsync(db, "Light Cream");
            await AliasAsync(db, cream, "cream", null);
            await ClassifiedAsync(db);

            var first = await ImportAsync(db, "cream");
            var second = await ImportAsync(db, "1 cup Cream");

            first.Id.Should().NotBe(cream.Id);
            second.Id.Should().Be(first.Id);
            (await db.Ingredients.CountAsync()).Should().Be(2);
        }

        [Fact]
        public async Task A_sized_can_line_landing_on_a_food_weighed_drained_is_stored_as_cans_not_as_the_net_weight()
        {
            using var db = NewContext();
            db.Set<Nom.Data.Measurement.BaseMeasurementEntity>().AddRange(
                new Nom.Data.Measurement.BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new Nom.Data.Measurement.BaseMeasurementEntity { Id = 7, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1 },
                new Nom.Data.Measurement.BaseMeasurementEntity { Id = 20, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, IsContainer = true });
            await db.SaveChangesAsync();
            var chickpeas = await FoodAsync(db, "Chickpeas, canned, drained, rinsed in tap water");
            await FoodAsync(db, "Black Beans");

            var result = await NewService(db).ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes =
                {
                    new ScraperRecipe
                    {
                        Name = "Hummus",
                        Ingredients =
                        {
                            new ScraperIngredient { Name = "Chickpeas, canned, drained, rinsed in tap water", RawLine = "1 (15-ounce) can chickpeas, drained and rinsed", Quantity = 1, Unit = "can" },
                            new ScraperIngredient { Name = "Black Beans", RawLine = "1 (15-ounce) can black beans", Quantity = 1, Unit = "can" },
                        },
                    },
                },
            });

            result.Imported.Should().Be(1);
            var lines = await db.RecipeIngredients.ToListAsync();
            lines.Single(l => l.IngredientId == chickpeas.Id).Should().Match<RecipeIngredientEntity>(l => l.Quantity == 1m && l.MeasurementId == 20);
            lines.Single(l => l.IngredientId != chickpeas.Id).Should().Match<RecipeIngredientEntity>(l => l.Quantity == 15m && l.MeasurementId == 7);
        }

        [Theory]
        [InlineData(false, 0, 1)]
        [InlineData(true, 2, 22)]
        public async Task A_line_whose_measurement_has_no_row_is_stored_without_an_amount_and_never_in_grams(bool packageRow, int quantity, long measurementId)
        {
            using var db = NewContext();
            db.Set<Nom.Data.Measurement.BaseMeasurementEntity>().AddRange(
                new Nom.Data.Measurement.BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new Nom.Data.Measurement.BaseMeasurementEntity { Id = 3, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 });
            if (packageRow)
            {
                db.Set<Nom.Data.Measurement.BaseMeasurementEntity>().Add(
                    new Nom.Data.Measurement.BaseMeasurementEntity { Id = 22, Name = "Package", Symbol = "pkg", MeasurementCategoryId = 3, IsContainer = true });
            }
            await db.SaveChangesAsync();

            var result = await NewService(db).ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes =
                {
                    new ScraperRecipe
                    {
                        Name = "Bread",
                        Ingredients = { new ScraperIngredient { Name = "active dry yeast", RawLine = "2 packages active dry yeast", Quantity = 2, Unit = "packages" } },
                    },
                },
            });

            result.Imported.Should().Be(1);
            var line = await db.RecipeIngredients.SingleAsync();
            (line.Quantity, line.MeasurementId).Should().Be(((decimal)quantity, measurementId));
        }

        [Fact]
        public async Task A_wording_a_lane_applied_link_merged_lands_on_its_food_and_the_link_undo_takes_the_new_line_back_too()
        {
            using var db = NewContext();
            var salt = await FoodAsync(db, "Salt, table");
            var kosher = await FoodAsync(db, "kosher salt", CurationStatusEnum.NonCurated, deleted: true);
            await AliasAsync(db, salt, "kosher salt", "link:ai:fake");
            var proposal = new Nom.Data.Curation.FoodCatalogProposalEntity
            {
                Action = Nom.Data.Curation.FoodProposalAction.Update, IngredientId = kosher.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "kosher salt",
                ProposedValue = salt.Id.ToString(), Source = "ai:fake", Status = Nom.Data.Curation.FoodProposalStatus.Applied, ReviewedByPersonId = Nom.Data.SystemConstants.SystemPersonId,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            var linked = DateTime.UtcNow.AddMinutes(-5);
            db.AuditLogEntries.Add(Nom.Orch.Services.Support.LinkApplyJournal.Entry(proposal.Id, Nom.Orch.Services.Support.LinkApplyJournal.Header,
                Nom.Orch.Services.Support.LinkApplyJournal.Write(new Nom.Orch.Services.Support.LinkApplyJournal.HeaderValue(kosher.Id, "kosher salt", (long)CurationStatusEnum.NonCurated, "ai:fake", true, salt.Id)),
                salt.Id.ToString(), linked, Nom.Data.SystemConstants.SystemPersonId));
            await db.SaveChangesAsync();

            var landed = await ImportAsync(db, "kosher salt");

            landed.Id.Should().Be(salt.Id);
            (await db.Ingredients.IgnoreQueryFilters().CountAsync(i => i.Name.StartsWith("kosher salt"))).Should().Be(1, "no new row takes the name and the retired one keeps it");
            db.ChangeTracker.Clear();

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonIdOrSystem).Returns(Importer);
            var undone = await new LinkApplyUndoService(db, currentUser.Object).UndoAsync(linked.AddSeconds(-1), apply: true);

            undone.Proposals.Should().Be(1);
            (await db.RecipeIngredients.SingleAsync()).IngredientId.Should().Be(kosher.Id);
            (await db.Ingredients.SingleAsync(i => i.Id == kosher.Id)).IsDeleted.Should().BeFalse();
        }
    }
}
