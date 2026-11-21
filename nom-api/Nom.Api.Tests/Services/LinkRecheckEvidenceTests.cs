using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Import.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The re-check fix round of staging checkpoint 2 (M7, M21, M22, M23, M24): a link already made
    /// is refused only on positive evidence that its food is wrong, and everything else is kept and
    /// counted as not proven; a drink word that names another food or a flavour word in a cook's
    /// note never moves or takes back a line; a dry run counts what its apply writes, and an undo
    /// gives back every name and reports the picks it restores. Every line here is one the
    /// checkpoint logged.
    /// </summary>
    public class LinkRecheckEvidenceTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static LinkRecheckService Recheck(ApplicationDbContext db) =>
            new(db, new NoUser(), new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions())), new LineRelinkService(db, new NoUser()),
                new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance), NullLogger<LinkRecheckService>.Instance);

        private static IngredientEntity Food(string name, string? description = null, string? fdcId = null, int? category = null, long status = (long)CurationStatusEnum.Curated)
        {
            var food = new IngredientEntity
            {
                Name = name,
                CurationStatusId = status,
                FdcId = fdcId,
                FdcCategoryId = category,
                FdcDataType = fdcId == null ? string.Empty : CatalogAdmissionRule.SrLegacyFood,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            if (description != null) food.Aliases.Add(new IngredientAliasEntity { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext });
            return food;
        }

        private static FoodCatalogProposalEntity Applied(string wording, long target) => new()
        {
            Field = FoodCatalogReviewService.FdcLinkField, Status = FoodProposalStatus.Applied, Batch = IngredientLinkService.Batch,
            Source = "ai:qwen2.5:14b-instruct", CurrentValue = wording, ProposedValue = target.ToString(CultureInfo.InvariantCulture),
            ReviewedAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
        };

        private static RecipeEntity Scraped(string name, params (IngredientEntity Food, string Line)[] lines) => new()
        {
            Name = name,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
            {
                IngredientId = l.Food.Id, SortOrder = i, Quantity = 1, MeasurementId = 1, RawLine = l.Line,
            }).ToList(),
        };

        private static async Task<long> FoodOfAsync(ApplicationDbContext db, long recipeId, string rawLine)
        {
            db.ChangeTracker.Clear();
            return await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId && ri.RawLine == rawLine).Select(ri => ri.IngredientId).SingleAsync();
        }

        [Fact]
        public async Task An_applied_link_is_kept_and_counted_as_not_proven_unless_a_hold_refutes_its_food()
        {
            await using var db = NewContext();
            var ketchup = Food("Ketchup", "Ketchup, restaurant", "747693", 11);
            var tomatoSauce = Food("Tomato Sauce", "Tomato, sauce, canned, with salt added", "2685579", 11);
            var lightCream = Food("Light Cream", "Cream, fluid, light (coffee cream or table cream)", "170857", 1);
            var nonDairy = Food("Non-Dairy Milk", "Milk, imitation, non-soy", "167730", 1);
            var cashew = Food("Cashew Butter", "Nuts, cashew butter, plain, with salt added", "168597", 12);
            var chickpeas = Food("Canned Chickpeas", "Chickpeas (garbanzo beans, bengal gram), mature seeds, canned, drained, rinsed in tap water", "173801", 16);
            var heavy = Food("Heavy Cream", "Cream, fluid, heavy whipping", "170859", 1);
            var garlic = Food("Garlic", "Garlic, raw", "1104647", 11);
            var brown = Food("Brown Sugar", "Sugars, brown", "168833", 19);
            var breast = Food("Chicken Breast", "Chicken, breast, boneless, skinless, raw", "2646170", 5);
            var unsalted = Food("Unsalted Butter", "Butter, without salt", "173430", 1);
            var baked = Food("Baked Potatoes", "Potatoes, baked, flesh, without salt", "170033", 11);
            var goose = Food("Goose", "Goose, domesticated, meat only, raw", "172413", 5);
            var cannedBeans = Food("Canned Black Beans", "Beans, black, canned, sodium added, drained and rinsed", "2644285", 16);
            var blackBeans = Food("Black Beans");
            var flour = Food("Flour", "Wheat flour, white, all-purpose, enriched, bleached", "168894", 20);
            var zeroZero = Food("00 Flour", "Flour, 00", "2003586", 20);
            var salt = Food("Salt");
            var canola = Food("Canola Oil");
            var olive = Food("Olive Oil");
            var foods = new[] { ketchup, tomatoSauce, lightCream, nonDairy, cashew, chickpeas, heavy, garlic, brown, breast, unsalted, baked, goose, cannedBeans, blackBeans, flour, zeroZero, salt, canola, olive };
            db.Ingredients.AddRange(foods);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = zeroZero.Id, AliasName = "00 flour", SourceContext = IngredientAliasTrust.AdminPrefix + "7" });

            var kept = new (IngredientEntity Food, string Wording, string Line)[]
            {
                (ketchup, "ketchup", "1/2 cup ketchup"),
                (tomatoSauce, "tomato sauce", "1 cup tomato sauce"),
                (lightCream, "thin cream", "1 pint thin cream"),
                (nonDairy, "non-dairy milk", "1 cup non-dairy milk"),
                (cashew, "cashew butter", "2 tbsp cashew butter"),
                (chickpeas, "chickpeas, drained and rinsed", "1 can chickpeas, drained and rinsed"),
                (heavy, "heavy cream or heavy whipping cream", "1 cup heavy cream or heavy whipping cream"),
                (garlic, "garlic, minced", "3 cloves garlic, minced (for a milder garlic flavor use 2)"),
                (garlic, "garlic, minced or grated", "2 cloves garlic, minced or grated"),
                (brown, "light or dark brown sugar", "1 cup light or dark brown sugar"),
                (breast, "boneless, skinless chicken breasts", "2 boneless, skinless chicken breasts"),
                (unsalted, "unsalted butter, cold and cubed", "1/2 cup unsalted butter, cold and cubed"),
                (unsalted, "unsalted butter, melted and slightly cooled", "6 tbsp unsalted butter, melted and slightly cooled"),
                (baked, "baked potatoes", "2 baked potatoes"),
                (goose, "goose", "1 goose"),
                (cannedBeans, "15 oz black beans", "15 oz black beans (canned, rinsed)"),
                (flour, "extra flour", "extra flour"),
            };
            var refuted = new (IngredientEntity Food, string Wording, string Line)[]
            {
                (salt, "salt and pepper", "salt and pepper"),
                (canola, "olive oil", "2 tbsp olive oil"),
            };
            foreach (var (food, wording, _) in kept.Concat(refuted)) db.FoodCatalogProposals.Add(Applied(wording, food.Id));
            var recipe = Scraped("Supper", kept.Concat(refuted).Select(k => (k.Food, k.Line)).ToArray());
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var service = Recheck(db);

            var dry = await service.RunAsync(LinkRecheckRules.Applied, max: 50000, sampleSize: 100);
            var run = await service.RunAsync(LinkRecheckRules.Applied, apply: true, max: 50000, sampleSize: 100);

            foreach (var (food, wording, line) in kept)
                (await FoodOfAsync(db, recipe.Id, line)).Should().Be(food.Id, $"'{wording}' on {food.Name}: nothing proves the food wrong");
            (await FoodOfAsync(db, recipe.Id, "2 tbsp olive oil")).Should().Be(olive.Id, "olive oil is another curated food's very name");
            (await FoodOfAsync(db, recipe.Id, "salt and pepper")).Should().NotBe(salt.Id, "two foods joined by and refute either one");
            run!.Unproven.Should().BeGreaterThan(0);
            (run.MoveGroups, run.UnlinkGroups, run.LinesMoved, run.LinesUnlinked).Should().Be((1, 1, 1, 1));
            (dry!.Unproven, dry.UnprovenLines, dry.MoveGroups, dry.UnlinkGroups, dry.LinesMoved, dry.LinesUnlinked, dry.RefutedLines)
                .Should().Be((run.Unproven, run.UnprovenLines, run.MoveGroups, run.UnlinkGroups, run.LinesMoved, run.LinesUnlinked, run.RefutedLines), "not proven and refuted are measured alike in a dry run");
            run.ByUnproven.Should().NotBeEmpty();
            run.Samples.Where(s => s.Outcome == LinkRecheckRules.Unproven).Select(s => s.Wording).Should().Contain(new[] { "ketchup", "tomato sauce", "thin cream", "black beans" });
        }

        [Fact]
        public async Task The_part_and_herb_set_keeps_a_spice_that_is_no_leaf_herb_and_the_leaves_of_a_leaf_food()
        {
            await using var db = NewContext();
            var pepper = Food("Pepper", "Spices, pepper, black", "170931", 2);
            var anise = Food("Anise", "Spices, anise seed", "171316", 2);
            var spinach = Food("Spinach", "Spinach, raw", "168462", 11);
            var parsley = Food("Parsley", "Parsley, fresh", "170416", 11);
            var tarragon = Food("Dried Tarragon", "Spices, tarragon, dried", "170937", 2);
            db.Ingredients.AddRange(pepper, anise, spinach, parsley, tarragon);
            await db.SaveChangesAsync();
            var lines = new (IngredientEntity Food, string Wording, string Line)[]
            {
                (pepper, "freshly cracked black pepper", "freshly cracked black pepper"),
                (anise, "anise seed", "1 tsp anise seed, freshly crushed"),
                (spinach, "spinach leaves", "4 cups spinach leaves"),
                (parsley, "leaves of parsley", "a few leaves of parsley"),
                (tarragon, "freshly chopped tarragon", "2 tbsp freshly chopped tarragon"),
            };
            foreach (var (food, wording, _) in lines) db.FoodCatalogProposals.Add(Applied(wording, food.Id));
            var recipe = Scraped("Herbs", lines.Select(l => (l.Food, l.Line)).ToArray());
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            await Recheck(db).RunAsync(LinkRecheckRules.PartHerb, apply: true);
            foreach (var (food, wording, line) in lines.Take(4))
                (await FoodOfAsync(db, recipe.Id, line)).Should().Be(food.Id, $"'{wording}' is {food.Name}");
            (await FoodOfAsync(db, recipe.Id, "2 tbsp freshly chopped tarragon")).Should().NotBe(tarragon.Id, "a fresh leaf herb is still not the dried spice");
        }

        [Fact]
        public async Task The_alcohol_set_leaves_a_drink_word_that_names_another_food_and_a_flavour_word_in_a_note()
        {
            await using var db = NewContext();
            var vinegar = Food("Red Wine Vinegar", "Vinegar, red wine", "172241", 2);
            var lemon = Food("Lemon Juice", "Lemon juice, raw", "167747", 9);
            var grapes = Food("Grapes", "Grapes, red, seedless, raw", "2263890", 9);
            var olives = Food("Green Olives", "Olives, green, Manzanilla, stuffed with pimiento", "332791", 9);
            var yeast = Food("Active Dry Yeast", "Leavening agents, yeast, baker's, active dry", "175043", 18);
            var custard = Food("Custard", "Desserts, egg custard, baked, prepared-from-recipe", "169595", 19);
            var garlic = Food("Garlic", "Garlic, raw", "1104647", 11);
            var sugar = Food("Sugar", "Sugars, granulated", "746784", 19);
            var almonds = Food("Almonds", "Nuts, almonds", "170567", 12);
            var dessert = Food("Dessert Wine, Sweet", "Alcoholic beverage, wine, dessert, sweet", "173176", 14);
            var dry = Food("Dessert Wine, Dry", "Alcoholic beverage, wine, dessert, dry", "175112", 14);
            var spirit = Food("Distilled Spirit", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "174815", 14);
            var beer = Food("Beer", "Alcoholic beverage, beer, regular, all", "168746", 14);
            db.Ingredients.AddRange(vinegar, lemon, grapes, olives, yeast, custard, garlic, sugar, almonds, dessert, dry, spirit, beer);
            await db.SaveChangesAsync();
            var left = new (IngredientEntity Food, string Line)[]
            {
                (lemon, "2 large smooth-rinded Malaga lemons"),
                (grapes, "6 pounds of Tokay Grapes"),
                (olives, "1 cup pitted manzanilla olives"),
                (yeast, "one spoonful of ale yest"),
                (custard, "1 pint custard (flavoured with French brandy)"),
                (garlic, "2 cloves finely minced garlic (for a milder garlic flavor)"),
                (sugar, "1/4 cup sugar (use 1/3 cup for a sweeter flavor)"),
                (almonds, "two oz. of finely-pounded almonds moistened with a tablespoonful of kirsch"),
            };
            var recipe = Scraped("Old book", left.Append((vinegar, "1 glass of sherry")).ToArray());
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.Alcohol, apply: true);
            foreach (var (food, line) in left) (await FoodOfAsync(db, recipe.Id, line)).Should().Be(food.Id, line);
            (await FoodOfAsync(db, recipe.Id, "1 glass of sherry")).Should().Be(dry.Id, "a real drink line on the wrong food still moves");
            (run!.LinesMoved, run.LinesUnlinked).Should().Be((1, 0));
        }

        [Fact]
        public async Task A_can_line_never_moves_to_a_sweetened_sister_or_one_with_another_food_and_a_drained_juice_line_is_no_wrong_form()
        {
            await using var db = NewContext();
            var cream = Food("Coconut Cream", "Nuts, coconut cream, raw (liquid expressed from grated meat)", "170171", 12);
            var sweetened = Food("Cream of Coconut", "Nuts, coconut cream, canned, sweetened", "170172", 12);
            var corn = Food("Corn", "Corn, sweet, yellow, raw", "169998", 11);
            var peppers = Food("Corn with Peppers", "Corn with red and green peppers, canned, solids and liquids", "169999", 11);
            db.Ingredients.AddRange(cream, sweetened, corn, peppers);
            await db.SaveChangesAsync();
            var recipe = Scraped("Curry", (cream, "1 14-ounce can coconut cream"), (corn, "a can of green corn"));
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.ContainerForm, apply: true);
            (await FoodOfAsync(db, recipe.Id, "1 14-ounce can coconut cream")).Should().Be(cream.Id, "the canned sister is sweetened and the line is not");
            (await FoodOfAsync(db, recipe.Id, "a can of green corn")).Should().Be(corn.Id, "the canned sister adds peppers the line does not name");
            run!.LinesMoved.Should().Be(0);
            LinkRecheckRules.WrongForm("1/2 cup pineapple juice (drained juice from the pineapple chunks)",
                new GateFood(1, "Pineapple Juice", "Pineapple juice, canned or bottled, unsweetened, without added ascorbic acid")).Should().BeFalse("the juice is the drained liquid itself");
        }

        [Fact]
        public async Task The_prepared_sets_dry_run_counts_what_its_apply_writes_a_prepared_food_of_the_wordings_name_lets_its_lines_go_and_the_undo_gives_every_name_back()
        {
            await using var db = NewContext();
            var zwieback = Food("zwieback", "Babyfood, cookies, zwieback", "174999", 3);
            var tortellini = Food("Cheese Tortellini", "Restaurant, Italian, cheese tortellini", "171234", 25);
            var retired = new IngredientEntity { Name = "spinach or cheese tortellini", CurationStatusId = (long)CurationStatusEnum.NonCurated, IsDeleted = true };
            db.Ingredients.AddRange(zwieback, tortellini, retired);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = tortellini.Id, AliasName = "spinach or cheese tortellini", SourceContext = "link:ai:qwen2.5:14b-instruct" });
            var row = new IngredientWordingEntity { Wording = "cheese tortellini", Verdict = IngredientWordingVerdict.Food, IngredientId = tortellini.Id, Batch = "w01", Source = "test" };
            db.IngredientWordings.Add(row);
            var recipe = Scraped("Nursery", (zwieback, "6 zwieback"), (tortellini, "1 lb spinach or cheese tortellini"), (tortellini, "1 lb cheese tortellini"));
            recipe.CurationStatusId = (long)CurationStatusEnum.Curated;
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var service = Recheck(db);
            db.ChangeTracker.Clear();
            var names = await db.Ingredients.IgnoreQueryFilters().AsNoTracking().OrderBy(i => i.Id).Select(i => i.Id + ":" + i.Name).ToListAsync();

            var dry = await service.RunAsync(LinkRecheckRules.Prepared);
            var run = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);
            (dry!.LinesUnlinked, dry.NoHome, dry.FoodsCreated, dry.FoodsRenamed, dry.NamesFreed, dry.AliasesUntrusted, dry.AliasesRemoved, dry.ApprovedRecipes)
                .Should().Be((run!.LinesUnlinked, run.NoHome, run.FoodsCreated, run.FoodsRenamed, run.NamesFreed, run.AliasesUntrusted, run.AliasesRemoved, run.ApprovedRecipes),
                    "the dry run counts lines, homes, created and renamed foods, freed names and aliases by the apply's own steps");
            dry.WordingRows.Should().Equal(run.WordingRows);
            (run.LinesUnlinked, run.NoHome, run.FoodsCreated, run.FoodsRenamed, run.NamesFreed).Should().Be((3, 0, 3, 1, 1));
            run.WordingRows.Should().Equal(row.Id);
            var home = await FoodOfAsync(db, recipe.Id, "6 zwieback");
            home.Should().NotBe(zwieback.Id, "a prepared food holding the wording's very name is renamed to its USDA description so the lines can leave it");
            (await db.Ingredients.AsNoTracking().SingleAsync(i => i.Id == zwieback.Id)).Name.Should().Be("Babyfood, cookies, zwieback");
            (await db.Ingredients.AsNoTracking().SingleAsync(i => i.Id == home)).Name.Should().Be("zwieback");

            var undoDry = await service.UndoAsync(run.Stamp!.Value);
            var undo = await service.UndoAsync(run.Stamp.Value, apply: true);
            (undoDry.LinesRestored, undoDry.FoodsRemoved, undoDry.NamesRestored, undoDry.AliasesRestored, undoDry.Skipped)
                .Should().Be((undo.LinesRestored, undo.FoodsRemoved, undo.NamesRestored, undo.AliasesRestored, undo.Skipped), "the undo's dry run counts what it gives back");
            (undo.LinesRestored, undo.FoodsRemoved, undo.NamesRestored, undo.Skipped).Should().Be((3, 3, 2, 0));
            (await FoodOfAsync(db, recipe.Id, "6 zwieback")).Should().Be(zwieback.Id);
            db.ChangeTracker.Clear();
            var renamed = new[] { zwieback.Id, tortellini.Id, retired.Id };
            (await db.Ingredients.IgnoreQueryFilters().AsNoTracking().Where(i => renamed.Contains(i.Id)).OrderBy(i => i.Id).Select(i => i.Id + ":" + i.Name).ToListAsync())
                .Should().Equal(names.Where(n => renamed.Any(id => n.StartsWith(id + ":", StringComparison.Ordinal))), "the freed and the renamed names are given back");
        }

        [Fact]
        public async Task An_undo_run_again_gives_back_the_names_an_earlier_undo_left_behind_its_soft_deleted_wording_foods()
        {
            await using var db = NewContext();
            var tortellini = Food("Cheese Tortellini", "Restaurant, Italian, cheese tortellini", "171234", 25);
            var retired = new IngredientEntity { Name = "spinach or cheese tortellini", CurationStatusId = (long)CurationStatusEnum.NonCurated, IsDeleted = true };
            db.Ingredients.AddRange(tortellini, retired);
            await db.SaveChangesAsync();
            var recipe = Scraped("Dinner", (tortellini, "1 lb spinach or cheese tortellini"));
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var run = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);
            var home = await FoodOfAsync(db, recipe.Id, "1 lb spinach or cheese tortellini");
            await service.UndoAsync(run!.Stamp!.Value, apply: true);

            db.ChangeTracker.Clear();
            var created = new IngredientEntity { Name = "spinach or cheese tortellini", CurationStatusId = (long)CurationStatusEnum.NonCurated, IsDeleted = true, CreatedDate = run.Stamp.Value.AddSeconds(1) };
            db.Ingredients.Add(created);
            var holder = await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == retired.Id);
            holder.Name = $"spinach or cheese tortellini (deleted #{retired.Id})";
            db.AuditLogEntries.Add(new Nom.Data.Audit.AuditLogEntryEntity
            {
                EntityType = LineRelinkService.RetireEntityType, EntityId = retired.Id, ChangeType = LinkRecheckService.FoodChangeType, PropertyName = nameof(IngredientEntity.Name),
                OldValue = "spinach or cheese tortellini", NewValue = holder.Name, Timestamp = run.Stamp.Value, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();

            var dry = await service.UndoAsync(run.Stamp.Value);
            (dry.NamesRestored, dry.FoodsRemoved, dry.Skipped).Should().Be((1, 1, 0), "the soft-deleted wording food the run created holds nothing and gives the name up");
            var undo = await service.UndoAsync(run.Stamp.Value, apply: true);
            (undo.NamesRestored, undo.FoodsRemoved, undo.Skipped).Should().Be((1, 1, 0));
            db.ChangeTracker.Clear();
            (await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == retired.Id)).Name.Should().Be("spinach or cheese tortellini");
            (await db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id == created.Id)).Should().BeFalse();
            home.Should().NotBe(tortellini.Id);
        }

        [Fact]
        public async Task The_withdrawal_undo_reports_the_picks_it_puts_back_in_its_dry_run_and_its_apply()
        {
            await using var db = NewContext();
            var holder = Food("brandy", status: (long)CurationStatusEnum.NonCurated);
            var liqueur = Food("Coffee Cream Liqueur");
            db.Ingredients.AddRange(holder, liqueur);
            await db.SaveChangesAsync();
            for (var i = 0; i < 3; i++)
                db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    IngredientId = holder.Id, Field = FoodCatalogReviewService.FdcLinkField, Status = FoodProposalStatus.Pending, Batch = IngredientLinkService.Batch,
                    Source = "ai:qwen2.5:14b-instruct", CurrentValue = "brandy", ProposedValue = liqueur.Id.ToString(CultureInfo.InvariantCulture),
                });
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var run = await service.WithdrawPicksAsync(apply: true);

            var dry = await service.UndoAsync(run.Stamp!.Value);
            (dry.PickEntries, dry.PicksRestored).Should().Be((3, 3));
            var undo = await service.UndoAsync(run.Stamp.Value, apply: true);
            (undo.PickEntries, undo.PicksRestored, undo.Skipped).Should().Be((3, 3, 0));
        }

        [Theory]
        [InlineData("00 Flour", "00 flour")]
        [InlineData("2 cups 00 flour", "00 flour")]
        [InlineData("2 cups flour", "flour")]
        [InlineData("0.5 cup sugar", "sugar")]
        public void The_wording_key_keeps_a_number_that_names_a_food(string name, string key) =>
            IngredientNameNormalizer.WordingKey(name).Should().Be(key);

        [Fact]
        public void The_catalog_key_keeps_a_bracketed_cut_and_tells_00_flour_from_flour()
        {
            CatalogAdmissionService.Key("Pork, fresh, loin, top loin (roasts), boneless, separable lean only, raw")
                .Should().NotBe(CatalogAdmissionService.Key("Pork, fresh, loin, top loin (chops), boneless, separable lean only, raw"));
            CatalogAdmissionService.Key("00 Flour").Should().NotBe(CatalogAdmissionService.Key("Flour"));
            CatalogAdmissionService.Key("Flour").Should().Be(CatalogAdmissionService.Key("flour"));
        }
    }
}
