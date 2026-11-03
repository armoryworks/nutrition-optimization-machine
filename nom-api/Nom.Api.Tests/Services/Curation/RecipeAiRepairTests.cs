using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// R3: the model may only divide an old cookbook's one-paragraph method into steps and point at
    /// amounts the recipe itself states. Anything it rewords or invents is discarded; the numbers
    /// always come from the parser reading verbatim recipe text.
    /// </summary>
    public class RecipeAiRepairTests
    {
        private const string Method =
            "Take a pound of flour, rub into it half a pound of butter, and add a little sugar. " +
            "Beat two eggs with a gill of milk and mix it with the flour into a stiff paste. " +
            "Roll it out thin, cut it into small cakes, and prick them with a fork. " +
            "Bake them in a quick oven for about a quarter of an hour.";

        private static readonly string[] ModelSteps =
        {
            "Take a pound of flour, rub into it half a pound of butter, and add a little sugar.",
            "Beat two eggs with a gill of milk and mix it with the flour into a stiff paste.",
            "Roll it out thin, cut it into small cakes, and prick them with a fork.",
            "Bake them in a quick oven for about a quarter of an hour.",
        };

        private static readonly Dictionary<int, string> ModelQuotes = new()
        {
            [0] = "a pound of flour",
            [1] = "half a pound of butter",
            [2] = "a little sugar",
            [3] = "two eggs",
            [4] = "a gill of milk",
        };

        private sealed class FakeModel : IRecipeRepairModel
        {
            public IReadOnlyList<string>? Steps { get; init; } = ModelSteps;
            public IReadOnlyDictionary<int, string> Quotes { get; init; } = ModelQuotes;
            public Exception? Failure { get; init; }
            public int Calls { get; private set; }
            public bool IsConfigured => true;
            public string ModelName => "fake";

            public Task<IReadOnlyList<string>?> SplitStepsAsync(string method, CancellationToken cancellationToken = default)
            {
                Calls++;
                if (Failure != null) throw Failure;
                return Task.FromResult(Steps);
            }

            public Task<IReadOnlyDictionary<int, string>> QuoteQuantitiesAsync(IReadOnlyList<string> ingredientLines, string method, CancellationToken cancellationToken = default)
            {
                Calls++;
                if (Failure != null) throw Failure;
                return Task.FromResult(Quotes);
            }
        }

        private static async Task<ApplicationDbContext> SeedAsync(params RecipeEntity[] recipes)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1 },
                new BaseMeasurementEntity { Id = 3, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 },
                new BaseMeasurementEntity { Id = 11, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 },
                new BaseMeasurementEntity { Id = 14, Name = "Pound", Symbol = "lb", MeasurementCategoryId = 1 });
            db.References.Add(new ReferenceEntity { Id = 9100, Name = "Recipe" });
            db.Recipes.AddRange(recipes);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static RecipeEntity ShrewsburyCakes(long id = 10, string method = Method)
        {
            var lines = new[] { "flour", "butter", "sugar", "eggs", "milk" };
            var recipe = new RecipeEntity
            {
                Id = id,
                Name = "Shrewsbury Cakes",
                ScrapedAtUtc = DateTime.UtcNow,
                LicenseStatus = RecipeLicenseStatus.PublicDomain,
                CurationStatusId = (long)CurationStatusEnum.RequiresRevision,
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = method[..Math.Min(60, method.Length)], Description = method } },
                RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
                {
                    IngredientId = id * 10 + i,
                    Ingredient = new IngredientEntity { Id = id * 10 + i, Name = l.Split(' ').Last() },
                    RawLine = l,
                    Quantity = 0,
                    MeasurementId = 1,
                }).ToList(),
            };
            recipe.VettingIssues = string.Join("\n", new RecipeVettingService().VetAsync(ScrapedRecipeRepairService.ToVettable(recipe)).Result);
            return recipe;
        }

        private static RecipeAiRepairService Service(ApplicationDbContext db, IRecipeRepairModel model) =>
            new(db, new RecipeVettingService(), model, NullLogger<RecipeAiRepairService>.Instance);

        [Fact]
        public async Task Old_cookbook_recipe_is_split_filled_and_cleared()
        {
            var original = ShrewsburyCakes();
            original.VettingIssues.Should().Contain("5 of 5 ingredient lines have no parseable quantity");
            using var db = await SeedAsync(original);

            var result = await Service(db, new FakeModel()).RepairBatchAsync(0, 10);

            result.StepsSplit.Should().Be(1);
            result.QuantitiesFilled.Should().Be(4);
            result.ProposalsRejected.Should().Be(1, "\"a little sugar\" states no amount");
            result.Cleared.Should().Be(1);

            var recipe = await db.Recipes.Include(r => r.RecipeSteps).Include(r => r.RecipeIngredients).SingleAsync();
            recipe.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
            recipe.VettingIssues.Should().BeNull();
            recipe.RecipeSteps!.OrderBy(s => s.StepNumber).Select(s => s.Description).Should().Equal(ModelSteps);

            var byLine = recipe.RecipeIngredients!.ToDictionary(i => i.RawLine);
            (byLine["flour"].Quantity, byLine["flour"].MeasurementId).Should().Be((1m, 14L));
            (byLine["butter"].Quantity, byLine["butter"].MeasurementId).Should().Be((0.5m, 14L));
            (byLine["eggs"].Quantity, byLine["eggs"].MeasurementId).Should().Be((2m, 3L));
            (byLine["milk"].Quantity, byLine["milk"].MeasurementId).Should().Be((0.5m, 11L));
            byLine["sugar"].Quantity.Should().Be(0);
            byLine.Keys.Should().BeEquivalentTo(new[] { "flour", "butter", "sugar", "eggs", "milk" }, "RawLine is never touched");

            var audit = await db.AuditLogEntries.ToListAsync();
            audit.Should().ContainSingle(a => a.PropertyName == "RecipeSteps" && a.OldValue == Method).Which.NewValue.Should().StartWith("[deterministic] ", "a sentence split needs no model");
            audit.Count(a => a.EntityType == "RecipeIngredient").Should().Be(4);
            audit.Should().ContainSingle(a => a.ChangeType == RecipeAiRepairService.AttemptChangeType);
            audit.Should().OnlyContain(a => a.ChangedByPersonId == SystemConstants.SystemPersonId);
        }

        [Fact]
        public async Task A_recipe_gets_one_model_attempt()
        {
            using var db = await SeedAsync(ShrewsburyCakes());
            var model = new FakeModel { Steps = new[] { "Take some flour.", "Bake it." } };

            await Service(db, model).RepairBatchAsync(0, 10);
            var again = await Service(db, model).RepairBatchAsync(0, 10);

            again.Examined.Should().Be(0);
            model.Calls.Should().Be(1, "the split is deterministic; only the quantities are asked");
        }

        [Theory]
        [InlineData("[fake] Only 1 instruction step(s) — likely an incomplete extraction.")]
        [InlineData("v1 [fake] 5 of 5 ingredient lines have no parseable quantity")]
        public async Task A_recipe_attempted_by_an_older_repair_version_gets_one_more_attempt(string earlierAttempt)
        {
            using var db = await SeedAsync(ShrewsburyCakes());
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = "Recipe",
                EntityId = 10,
                ChangeType = RecipeAiRepairService.AttemptChangeType,
                PropertyName = "VettingIssues",
                NewValue = earlierAttempt,
                ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            var model = new FakeModel { Steps = new[] { "Take some flour.", "Bake it." } };

            (await Service(db, model).RepairBatchAsync(0, 10)).Examined.Should().Be(1);
            (await Service(db, model).RepairBatchAsync(0, 10)).Examined.Should().Be(0, "one attempt per version");

            model.Calls.Should().Be(1);
            var attempts = await db.AuditLogEntries.Where(a => a.ChangeType == RecipeAiRepairService.AttemptChangeType).OrderBy(a => a.Id).ToListAsync();
            attempts.Should().HaveCount(2);
            attempts[1].NewValue.Should().StartWith($"v{RecipeAiRepairService.RepairVersion} [fake] ");
            (await db.RecipeIngredients.Select(r => r.RawLine).ToListAsync()).Should().BeEquivalentTo(new[] { "flour", "butter", "sugar", "eggs", "milk" });
        }

        [Fact]
        public async Task A_recipe_whose_quantities_this_prompt_already_asked_is_not_asked_again()
        {
            using var db = await SeedAsync(ShrewsburyCakes());
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = "Recipe",
                EntityId = 10,
                ChangeType = RecipeAiRepairService.AttemptChangeType,
                PropertyName = "VettingIssues",
                NewValue = $"v{RecipeAiRepairService.QuantityPromptVersion} [fake] 5 of 5 ingredient lines have no parseable quantity",
                ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            var model = new FakeModel();

            var result = await Service(db, model).RepairBatchAsync(0, 10);

            result.Examined.Should().Be(1);
            result.StepsSplit.Should().Be(1, "the sentence split is new in this version");
            model.Calls.Should().Be(0);
            (await db.Recipes.Include(r => r.RecipeSteps).SingleAsync()).RecipeSteps!.Should().HaveCount(4);
            (await Service(db, model).RepairBatchAsync(0, 10)).Examined.Should().Be(0);
        }

        [Fact]
        public async Task A_split_that_rewords_the_method_is_discarded()
        {
            const string shortMethod = "Boil it. Serve hot.";
            using var db = await SeedAsync(ShrewsburyCakes(method: shortMethod));
            var model = new FakeModel { Steps = new[] { "Boil it.", "Serve it warm." } };

            var result = await Service(db, model).RepairBatchAsync(0, 10);

            model.Calls.Should().Be(2, "two fragments too short to stand alone leave the split to the model");
            result.StepsSplit.Should().Be(0);
            var recipe = await db.Recipes.Include(r => r.RecipeSteps).SingleAsync();
            recipe.RecipeSteps!.Should().ContainSingle().Which.Description.Should().Be(shortMethod);
            recipe.CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
        }

        [Theory]
        [InlineData(typeof(HttpRequestException))]
        [InlineData(typeof(TaskCanceledException))]
        [InlineData(typeof(System.Text.Json.JsonException))]
        public async Task A_model_failure_is_recorded_and_the_batch_moves_on(Type failure)
        {
            using var db = await SeedAsync(ShrewsburyCakes(10), ShrewsburyCakes(11));
            var model = new FakeModel { Failure = (Exception)Activator.CreateInstance(failure)! };

            var result = await Service(db, model).RepairBatchAsync(0, 10);

            result.Examined.Should().Be(2);
            result.QuantitiesFilled.Should().Be(0);
            model.Calls.Should().Be(2);
            var attempts = await db.AuditLogEntries.Where(a => a.ChangeType == RecipeAiRepairService.AttemptChangeType).ToListAsync();
            attempts.Should().HaveCount(2).And.OnlyContain(a => a.NewValue!.Contains("failed: " + failure.Name));
            (await db.Recipes.ToListAsync()).Should().OnlyContain(r => r.CurationStatusId == (long)CurationStatusEnum.RequiresRevision);
            (await Service(db, model).RepairBatchAsync(0, 10)).Examined.Should().Be(0, "a failed recipe is not retried until the version changes");
        }

        [Fact]
        public async Task Recipes_a_curator_has_handled_are_left_alone()
        {
            using var db = await SeedAsync(ShrewsburyCakes());
            db.CurationFeedbacks.Add(new CurationFeedbackEntity { EntityId = 10, EntityTypeId = 9100, AdminId = 5, FeedbackNotes = "Please fix the method", FeedbackTypeId = 9199 });
            await db.SaveChangesAsync();
            var model = new FakeModel();

            (await Service(db, model).RepairBatchAsync(0, 10)).Examined.Should().Be(0);
            model.Calls.Should().Be(0);
        }

        [Fact]
        public async Task Unrepairable_issues_skip_the_model()
        {
            var recipe = ShrewsburyCakes();
            recipe.CookTimeMinutes = 100_000;
            recipe.VettingIssues += "\nCook time of 100000 minutes is outside the plausible range (0–1440).";
            using var db = await SeedAsync(recipe);
            var model = new FakeModel();

            await Service(db, model).RepairBatchAsync(0, 10);

            model.Calls.Should().Be(0);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == RecipeAiRepairService.AttemptChangeType)).NewValue.Should().StartWith($"v{RecipeAiRepairService.RepairVersion} not repairable: ");
            (await Service(db, model).RepairBatchAsync(0, 10)).Examined.Should().Be(0, "a recipe the lane cannot fix is not reloaded every sweep");
        }

        [Fact]
        public void Sentences_split_deterministically_and_abbreviations_do_not()
        {
            RecipeRepairGrounding.SplitSentences(Method).Should().Equal(ModelSteps);
            RecipeRepairGrounding.SplitSentences("Boil the sweet potatoes until tender, then mash them with butter.").Should().BeNull("one sentence is not a split");
            RecipeRepairGrounding.SplitSentences("Boil it. Serve hot.").Should().BeNull("fragments fold into one step");
            RecipeRepairGrounding.SplitSentences("Add 1 lb. of flour and stir well; Then bake it slowly.").Should().Equal("Add 1 lb. of flour and stir well;", "Then bake it slowly.");
            RecipeRepairGrounding.SplitSentences("Add 1 lb. of flour and stir well — then bake it slowly.").Should().BeNull("an em dash is not a sentence end");
            RecipeRepairGrounding.SplitSentences(null).Should().BeNull();
        }

        [Fact]
        public void Split_must_reproduce_the_original_text()
        {
            RecipeRepairGrounding.AcceptSplit(Method, ModelSteps).Should().Equal(ModelSteps);
            RecipeRepairGrounding.AcceptSplit("  " + Method.Replace(". ", ".\n  "), ModelSteps).Should().NotBeNull("whitespace differences are not wording changes");

            RecipeRepairGrounding.AcceptSplit(Method, ModelSteps.Take(3).ToList()).Should().BeNull("a dropped sentence is a change");
            RecipeRepairGrounding.AcceptSplit(Method, ModelSteps.Reverse().ToList()).Should().BeNull("reordering is a change");
            RecipeRepairGrounding.AcceptSplit(Method, ModelSteps.Select(s => s.Replace("quick", "hot")).ToList()).Should().BeNull();
            RecipeRepairGrounding.AcceptSplit(Method, ModelSteps.Append("Serve warm.").ToList()).Should().BeNull("added text is a change");
            RecipeRepairGrounding.AcceptSplit(Method, new[] { Method }).Should().BeNull("one step is not a split");
        }

        [Fact]
        public void Split_folds_fragments_too_short_to_be_a_step()
        {
            var steps = RecipeRepairGrounding.AcceptSplit("Boil the milk with the sugar. Stir. Serve it cold.", new[] { "Boil the milk with the sugar.", "Stir.", "Serve it cold." });
            steps.Should().Equal("Boil the milk with the sugar. Stir.", "Serve it cold.");
        }

        [Fact]
        public void Quantity_must_be_written_in_the_recipe()
        {
            var sources = new[] { Method };

            RecipeRepairGrounding.GroundQuantity("flour", "flour", "a pound of flour", sources).Should().Be(new ParsedQuantity(1m, "Pound"));
            RecipeRepairGrounding.GroundQuantity("flour", "flour", "two pounds of flour", sources).Should().BeNull("not in the text");
            RecipeRepairGrounding.GroundQuantity("flour", "flour", "1 lb flour", sources).Should().BeNull("a conversion is not a quote");
            RecipeRepairGrounding.GroundQuantity("salt", "salt", "a little salt", sources).Should().BeNull("no amount stated");
            RecipeRepairGrounding.GroundQuantity("butter", "butter", "a pound of flour", sources).Should().BeNull("quote is about another ingredient");
            RecipeRepairGrounding.GroundQuantity("sugar", "sugar", "a gill of milk", sources).Should().BeNull();
            RecipeRepairGrounding.GroundQuantity("eggs", "egg", "two eggs", sources).Should().Be(new ParsedQuantity(2m, "Piece"));
            RecipeRepairGrounding.GroundQuantity("milk", "milk", "a gill of milk", sources).Should().Be(new ParsedQuantity(0.5m, "Cup"));
            RecipeRepairGrounding.GroundQuantity("currants", "currants", "", sources).Should().BeNull();
        }

        [Fact]
        public void Abbreviated_units_ground_and_partial_ingredients_do_not()
        {
            var sources = new[]
            {
                "Wash 2 lbs. of rice, and boil it in a saucepan with three pints of water till quite soft; then drain it, and add 1 oz. of butter, " +
                "a teaspoonful of salt, and a little grated nutmeg. Put it into a dish, brush it over with the yolk of an egg, and bake.",
            };

            RecipeRepairGrounding.GroundQuantity("rice", "rice", "2 lbs. of rice", sources).Should().Be(new ParsedQuantity(2m, "Pound"));
            RecipeRepairGrounding.GroundQuantity("water", "water", "three pints of water", sources).Should().Be(new ParsedQuantity(6m, "Cup"));
            RecipeRepairGrounding.GroundQuantity("butter", "butter", "1 oz. of butter", sources).Should().Be(new ParsedQuantity(1m, "Ounce"));
            RecipeRepairGrounding.GroundQuantity("salt", "salt", "a teaspoonful of salt", sources).Should().Be(new ParsedQuantity(1m, "Teaspoon"));
            RecipeRepairGrounding.GroundQuantity("nutmeg", "nutmeg", "a little grated nutmeg", sources).Should().BeNull();
            RecipeRepairGrounding.GroundQuantity("egg", "egg", "the yolk of an egg", sources).Should().BeNull("a yolk is not an egg");
        }

        [Fact]
        public void A_count_needs_the_ingredient_right_after_it()
        {
            var sources = new[] { "Add two handsome cucumbers and three ladles of stock." };
            RecipeRepairGrounding.GroundQuantity("stock", "stock", "three ladles of stock", sources).Should().BeNull("'ladle' is no unit NOM knows, so this is not three pieces of stock");
            RecipeRepairGrounding.GroundQuantity("cucumbers", "cucumber", "two handsome cucumbers", sources).Should().BeNull();
        }

        [Fact]
        public void An_amount_in_the_line_itself_counts()
        {
            RecipeRepairGrounding.GroundQuantity("Flour, one pound of the finest", "flour", "Flour, one pound", Array.Empty<string>()).Should().Be(new ParsedQuantity(1m, "Pound"));
            RecipeRepairGrounding.GroundQuantity("Of flour, a pound", "flour", "a pound", Array.Empty<string>()).Should().BeNull("the quote must name the ingredient");
            RecipeRepairGrounding.GroundQuantity("the yolks of three eggs", "egg", "three eggs", Array.Empty<string>()).Should().Be(new ParsedQuantity(3m, "Piece"));
        }
    }
}
