using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// An unquantified line is only a missing amount when it is a real ingredient. References to other
    /// preparations, equipment, notes, stray method text and merge artefacts never had an amount, so
    /// once classified they stop holding a recipe in RequiresRevision. The model classifies; it never
    /// touches a quantity or a raw line.
    /// </summary>
    public class RecipeLineClassificationTests
    {
        private sealed class FakeModel : IRecipeClassificationModel
        {
            public Func<string, string?> Kind { get; init; } = _ => null;
            public Func<string, FoodVerdict> Verdict { get; init; } = _ => FoodVerdict.Unsure;
            public List<string> LinesAsked { get; } = new();
            public List<string> RecipesScreened { get; } = new();
            public int Calls { get; private set; }
            public bool IsConfigured => true;
            public string ModelName => "fake";

            public Task<IReadOnlyDictionary<int, string>> ClassifyLinesAsync(string recipeName, IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
            {
                Calls++;
                LinesAsked.AddRange(lines);
                var kinds = new Dictionary<int, string>();
                for (var i = 0; i < lines.Count; i++)
                {
                    if (Kind(lines[i]) is { } kind) kinds[i] = kind;
                }
                return Task.FromResult<IReadOnlyDictionary<int, string>>(kinds);
            }

            public Task<FoodVerdict> IsFoodAsync(string recipeName, IReadOnlyList<string> ingredientLines, CancellationToken cancellationToken = default)
            {
                Calls++;
                RecipesScreened.Add(recipeName);
                return Task.FromResult(Verdict(recipeName));
            }
        }

        private static readonly Dictionary<string, string> ModelKinds = new()
        {
            ["white sauce"] = RecipeIngredientLineKind.Reference,
            ["gravy"] = RecipeIngredientLineKind.Reference,
            ["custard stuff"] = RecipeIngredientLineKind.Reference,
            ["forcemeat"] = RecipeIngredientLineKind.Reference,
            ["half-glaze sauce"] = RecipeIngredientLineKind.Reference,
            ["good stock"] = RecipeIngredientLineKind.Reference,
            ["_six to eight servings_"] = RecipeIngredientLineKind.Note,
            ["Remove large claws and split a lobster in two lengthwise"] = RecipeIngredientLineKind.Instruction,
            ["lobster"] = RecipeIngredientLineKind.Ingredient,
        };

        private static async Task<ApplicationDbContext> SeedAsync(params RecipeEntity[] recipes)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.References.AddRange(
                new ReferenceEntity { Id = 9100, Name = "Recipe" },
                new ReferenceEntity { Id = 9199, Name = "Approval" },
                new ReferenceEntity { Id = 9198, Name = "Rejection" });
            db.Recipes.AddRange(recipes);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static RecipeEntity Recipe(long id, string name, params (string Raw, decimal Quantity)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Id = id,
                Name = name,
                Visibility = RecipeVisibilityEnum.Public,
                ScrapedAtUtc = DateTime.UtcNow,
                AuthorId = SystemConstants.SystemPersonId,
                LicenseStatus = RecipeLicenseStatus.PublicDomain,
                CurationStatusId = (long)CurationStatusEnum.RequiresRevision,
                RecipeSteps = new List<RecipeStepEntity>
                {
                    new() { StepNumber = 1, Summary = "Prepare", Description = "Prepare everything as directed in the receipt." },
                    new() { StepNumber = 2, Summary = "Finish", Description = "Finish it over a gentle fire and serve it hot." },
                },
                RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
                {
                    IngredientId = id * 100 + i,
                    Ingredient = new IngredientEntity { Id = id * 100 + i, Name = l.Raw, CurationStatusId = (long)CurationStatusEnum.Curated },
                    RawLine = l.Raw,
                    Quantity = l.Quantity,
                    MeasurementId = 1,
                }).ToList(),
            };
            var issues = new RecipeVettingService().VetAsync(ScrapedRecipeRepairService.ToVettable(recipe)).Result;
            recipe.VettingIssues = issues.Count > 0 ? string.Join("\n", issues) : null;
            if (issues.Count == 0) recipe.CurationStatusId = (long)CurationStatusEnum.NonCurated;
            return recipe;
        }

        private static RecipeEntity LobsterNewburg(long id = 10) => Recipe(id, "Lobster a la Newburg",
            ("2 lobsters", 2m), ("white sauce", 0m), ("good stock", 0m), ("bain-marie", 0m), ("butter + butter", 0m), ("salt", 0m));

        private static RecipeLineClassificationService Lines(ApplicationDbContext db, IRecipeClassificationModel model) =>
            new(db, new RecipeVettingService(), model, NullLogger<RecipeLineClassificationService>.Instance);

        private static RecipeNonFoodScreeningService Screening(ApplicationDbContext db, IRecipeClassificationModel model) =>
            new(db, model, NullLogger<RecipeNonFoodScreeningService>.Instance);

        private static FakeModel KnownKinds() => new() { Kind = l => ModelKinds.TryGetValue(l, out var k) ? k : null };

        [Theory]
        [InlineData("butter + butter", RecipeIngredientLineKind.Artefact)]
        [InlineData("Butter + butter.", RecipeIngredientLineKind.Artefact)]
        [InlineData("", RecipeIngredientLineKind.Artefact)]
        [InlineData(" -- ", RecipeIngredientLineKind.Artefact)]
        [InlineData("bain-marie", RecipeIngredientLineKind.Equipment)]
        [InlineData("a buttered mould", RecipeIngredientLineKind.Equipment)]
        [InlineData("A pudding cloth", RecipeIngredientLineKind.Equipment)]
        [InlineData("skewers", RecipeIngredientLineKind.Equipment)]
        [InlineData("For the sauce:", RecipeIngredientLineKind.Note)]
        [InlineData("_six to eight servings_", RecipeIngredientLineKind.Note)]
        [InlineData("Serves four", RecipeIngredientLineKind.Note)]
        [InlineData("white sauce", null)]
        [InlineData("flour", null)]
        [InlineData("butter + sugar", null)]
        [InlineData("flour + 2 cups flour", null)]
        [InlineData("string beans", null)]
        [InlineData("Salt:", null)]
        [InlineData("pan drippings", null)]
        public void Deterministic_pre_pass_only_takes_obvious_lines(string raw, string? expected)
        {
            RecipeLineClassifier.Classify(raw).Should().Be(expected);
        }

        [Fact]
        public async Task Lines_are_classified_deterministically_first_then_by_the_model()
        {
            using var db = await SeedAsync(LobsterNewburg());
            var model = KnownKinds();

            var result = await Lines(db, model).ClassifyBatchAsync(0, 10);

            model.LinesAsked.Should().Equal(new[] { "white sauce", "good stock" }, "quantified, seasoning and obvious lines never reach the model");
            result.Deterministic.Should().Be(2);
            result.ModelClassified.Should().Be(2);
            result.Unclassified.Should().Be(0);

            var rows = (await db.RecipeIngredients.ToListAsync()).ToDictionary(r => r.RawLine);
            rows["white sauce"].LineKind.Should().Be(RecipeIngredientLineKind.Reference);
            rows["good stock"].LineKind.Should().Be(RecipeIngredientLineKind.Reference);
            rows["bain-marie"].LineKind.Should().Be(RecipeIngredientLineKind.Equipment);
            rows["butter + butter"].LineKind.Should().Be(RecipeIngredientLineKind.Artefact);
            rows["salt"].LineKind.Should().BeNull();
            rows["2 lobsters"].LineKind.Should().BeNull();
            rows.Values.Select(r => (r.RawLine, r.Quantity)).Should().BeEquivalentTo(new[]
            {
                ("2 lobsters", 2m), ("white sauce", 0m), ("good stock", 0m), ("bain-marie", 0m), ("butter + butter", 0m), ("salt", 0m),
            }, "quantities and raw lines are never changed");

            var audit = await db.AuditLogEntries.ToListAsync();
            audit.Where(a => a.ChangeType == RecipeLineClassificationService.ClassifyChangeType).Select(a => a.NewValue!.Split(':')[0])
                .Should().BeEquivalentTo("reference (fake)", "reference (fake)", "equipment (deterministic)", "artefact (deterministic)");
            audit.Should().ContainSingle(a => a.ChangeType == RecipeLineClassificationService.AttemptChangeType)
                .Which.NewValue.Should().StartWith(RecipeLineClassificationService.AttemptVersionPrefix + "[fake]");
            audit.Should().OnlyContain(a => a.ChangedByPersonId == SystemConstants.SystemPersonId);
        }

        [Fact]
        public async Task Classifying_the_lines_re_vets_the_recipe_into_NonCurated_and_auto_approval_follows()
        {
            var recipe = Recipe(10, "Lobster Cutlets", ("2 lobsters", 2m), ("white sauce", 0m), ("forcemeat", 0m), ("gravy", 0m));
            recipe.VettingIssues.Should().Contain("3 of 4 ingredient lines have no parseable quantity");
            using var db = await SeedAsync(recipe);

            var result = await Lines(db, KnownKinds()).ClassifyBatchAsync(0, 10);

            result.Cleared.Should().Be(1);
            var revetted = await db.Recipes.SingleAsync();
            revetted.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
            revetted.VettingIssues.Should().BeNull();

            var approval = await new RecipeAutoCurationService(db, new RecipeVettingService(), NullLogger<RecipeAutoCurationService>.Instance).AutoApproveBatchAsync(0, 10);
            approval.Approved.Should().Be(1);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
        }

        [Fact]
        public async Task A_real_ingredient_with_no_amount_keeps_the_recipe_flagged()
        {
            var recipe = Recipe(10, "Lobster a la Newburg", ("cream", 1m), ("lobster", 0m), ("white sauce", 0m), ("gravy", 0m));
            using var db = await SeedAsync(recipe);

            await Lines(db, KnownKinds()).ClassifyBatchAsync(0, 10);

            var row = await db.RecipeIngredients.SingleAsync(r => r.RawLine == "lobster");
            row.LineKind.Should().Be(RecipeIngredientLineKind.Ingredient);
            var stored = await db.Recipes.Include(r => r.RecipeIngredients).SingleAsync();
            stored.CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated, "1 of 4 is not more than half");
            RecipeAutoApprovalPolicy.Evaluate(stored).Reason.Should().Be("1 ingredient line(s) have no quantity");
        }

        [Fact]
        public async Task Unreadable_answers_leave_lines_unclassified_and_are_not_retried()
        {
            using var db = await SeedAsync(Recipe(10, "Lobster Cutlets", ("2 lobsters", 2m), ("white sauce", 0m), ("forcemeat", 0m), ("gravy", 0m)));
            var model = new FakeModel();

            var result = await Lines(db, model).ClassifyBatchAsync(0, 10);

            result.Unclassified.Should().Be(3);
            (await db.RecipeIngredients.Where(r => r.LineKind != null).CountAsync()).Should().Be(0);
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
            (await Lines(db, model).ClassifyBatchAsync(0, 10)).Examined.Should().Be(0);
            model.Calls.Should().Be(1);
        }

        [Fact]
        public void Unreadable_model_output_parses_to_no_answer()
        {
            OllamaRecipeClassificationModel.ParseLineKinds(null, 3).Should().BeEmpty();
            OllamaRecipeClassificationModel.ParseLineKinds("not json", 3).Should().BeEmpty();
            OllamaRecipeClassificationModel.ParseLineKinds("[1,2,3]", 3).Should().BeEmpty();
            OllamaRecipeClassificationModel.ParseLineKinds("{\"answers\":[]}", 3).Should().BeEmpty();

            var kinds = OllamaRecipeClassificationModel.ParseLineKinds(
                "{\"lines\":[{\"n\":1,\"kind\":\"Reference\"},{\"n\":2,\"kind\":\"sauce\"},{\"n\":3,\"kind\":\"note\"},{\"n\":3,\"kind\":\"equipment\"}," +
                "{\"n\":4,\"kind\":\"note\"},{\"n\":\"1\",\"kind\":\"note\"},{\"n\":0,\"kind\":\"note\"}]}", 3);
            kinds.Should().BeEquivalentTo(new Dictionary<int, string> { [0] = RecipeIngredientLineKind.Reference },
                "unknown kinds, conflicting answers and out-of-range numbers are dropped");

            OllamaRecipeClassificationModel.ParseFoodVerdict("{\"answer\":\"no\"}").Should().Be(FoodVerdict.NotFood);
            OllamaRecipeClassificationModel.ParseFoodVerdict("{\"answer\":\"Yes\"}").Should().Be(FoodVerdict.Food);
            OllamaRecipeClassificationModel.ParseFoodVerdict("{\"answer\":\"unsure\"}").Should().Be(FoodVerdict.Unsure);
            OllamaRecipeClassificationModel.ParseFoodVerdict("{\"answer\":\"probably not\"}").Should().Be(FoodVerdict.Unsure);
            OllamaRecipeClassificationModel.ParseFoodVerdict("{\"answer\":false}").Should().Be(FoodVerdict.Unsure);
            OllamaRecipeClassificationModel.ParseFoodVerdict("no").Should().Be(FoodVerdict.Unsure);
            OllamaRecipeClassificationModel.ParseFoodVerdict(null).Should().Be(FoodVerdict.Unsure);
        }

        public static IEnumerable<object?[]> Kinds() => RecipeIngredientLineKind.All.Select(k => new object?[] { k }).Append(new object?[] { null });

        [Theory]
        [MemberData(nameof(Kinds))]
        public void Only_ingredient_and_unclassified_lines_count_as_missing_amounts(string? kind)
        {
            var exempt = kind != null && kind != RecipeIngredientLineKind.Ingredient;
            var recipe = Recipe(10, "Lobster Cutlets", ("2 lobsters", 2m), ("cream", 1m), ("lobster coral", 0m), ("lobster butter", 0m), ("gravy", 0m));
            foreach (var row in recipe.RecipeIngredients!.Where(r => r.Quantity == 0)) row.LineKind = kind;

            var issues = new RecipeVettingService().VetAsync(ScrapedRecipeRepairService.ToVettable(recipe)).Result;
            issues.Any(i => i.Contains("have no parseable quantity")).Should().Be(!exempt);

            recipe.CurationStatusId = (long)CurationStatusEnum.NonCurated;
            recipe.VettingIssues = null;
            var decision = RecipeAutoApprovalPolicy.Evaluate(recipe);
            decision.Eligible.Should().Be(exempt);
            if (!exempt) decision.Reason.Should().Be("3 ingredient line(s) have no quantity");
        }

        private static readonly string[] NonFood =
        {
            "To clean Alabaster, or any other kinds of Marble",
            "Wash for the Teeth",
            "Polish for Bright Stoves and Steel Articles",
            "TO REMOVE IRON RUST",
        };

        private static readonly string[] Food = { "TO CURE HAMS AND BACON", "Polish Chops", "Polish Beet Soup" };

        private static RecipeEntity[] Household() => new[]
        {
            Recipe(1, NonFood[0], ("soap", 0m), ("water", 0m)),
            Recipe(2, NonFood[1], ("tincture of myrrh", 0m), ("water", 0m)),
            Recipe(3, NonFood[2], ("soft soap", 0m), ("emery powder", 0m)),
            Recipe(4, NonFood[3], ("salt", 0m), ("lemon juice", 0m)),
            Recipe(5, Food[0], ("salt", 0m), ("saltpetre", 0m), ("brown sugar", 0m)),
            Recipe(6, Food[1], ("pork chops", 0m), ("sour cream", 0m)),
            Recipe(7, Food[2], ("beets", 0m), ("stock", 0m), ("sour cream", 0m)),
            Recipe(8, "Shrewsbury Cakes", ("flour", 0m), ("butter", 0m)),
        };

        [Fact]
        public async Task Non_food_recipes_are_rejected_when_the_filter_matches_and_the_model_says_no()
        {
            using var db = await SeedAsync(Household());
            var model = new FakeModel { Verdict = _ => FoodVerdict.NotFood };

            var result = await Screening(db, model).ScreenBatchAsync(0, 20);

            result.Rejected.Should().Be(4);
            model.RecipesScreened.Should().BeEquivalentTo(NonFood, "food names, curing included, never reach the model");
            var statuses = await db.Recipes.ToDictionaryAsync(r => r.Name, r => r.CurationStatusId);
            foreach (var name in NonFood) statuses[name].Should().Be((long)CurationStatusEnum.Rejected, name);
            foreach (var name in Food.Append("Shrewsbury Cakes")) statuses[name].Should().NotBe((long)CurationStatusEnum.Rejected, name);

            var notes = await db.CurationFeedbacks.ToListAsync();
            notes.Should().HaveCount(4).And.OnlyContain(n => n.AdminId == SystemConstants.SystemPersonId && n.FeedbackTypeId == 9198 && n.EntityTypeId == 9100
                && n.FeedbackNotes == "auto-rejected: not a food recipe (model fake)");

            (await Screening(db, model).ScreenBatchAsync(0, 20)).Screened.Should().Be(0, "each recipe is screened once per version");
        }

        [Theory]
        [InlineData(FoodVerdict.Unsure)]
        [InlineData(FoodVerdict.Food)]
        public async Task Unsure_or_food_answers_leave_the_recipe_alone(FoodVerdict verdict)
        {
            using var db = await SeedAsync(Household());
            var model = new FakeModel { Verdict = _ => verdict };

            var result = await Screening(db, model).ScreenBatchAsync(0, 20);

            result.Screened.Should().Be(4);
            result.Rejected.Should().Be(0);
            (await db.Recipes.CountAsync(r => r.CurationStatusId == (long)CurationStatusEnum.Rejected)).Should().Be(0);
            (await db.CurationFeedbacks.CountAsync()).Should().Be(0);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == RecipeNonFoodScreeningService.AttemptChangeType)).Should().Be(4);
        }

        [Fact]
        public void Pre_filter_matches_household_receipts_but_not_food()
        {
            foreach (var name in NonFood) NonFoodRecipeFilter.IsCandidate(name, Array.Empty<string>()).Should().BeTrue(name);
            foreach (var name in Food) NonFoodRecipeFilter.IsCandidate(name, new[] { "soft soap" }).Should().BeFalse(name);
            NonFoodRecipeFilter.IsCandidate("Furniture Cream", new[] { "beeswax", "turpentine" }).Should().BeTrue();
            NonFoodRecipeFilter.IsCandidate("Shrewsbury Cakes", new[] { "flour", "butter" }).Should().BeFalse();
            NonFoodRecipeFilter.IsCandidate("Mrs. B's Mixture", new[] { "turpentine", "linseed oil" }).Should().BeTrue();
        }

        [Fact]
        public async Task Line_classification_waits_for_a_flagged_recipe_to_be_screened()
        {
            using var db = await SeedAsync(Recipe(1, NonFood[2], ("soft soap", 0m), ("emery powder", 0m), ("white sauce", 0m)));
            var model = new FakeModel { Kind = _ => RecipeIngredientLineKind.Ingredient, Verdict = _ => FoodVerdict.Unsure };

            (await Lines(db, model).ClassifyBatchAsync(0, 10)).ModelCalls.Should().Be(0);
            await Screening(db, model).ScreenBatchAsync(0, 10);
            (await Lines(db, model).ClassifyBatchAsync(0, 10)).ModelClassified.Should().Be(3);
        }
    }
}
