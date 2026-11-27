using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The tables the diet gate's reading queries, taken from the queries themselves: a whole cycle and a cycle over the
    /// changes of <see cref="DietGateStoreBuilder"/> run on a kitchen that reaches every part of the reading (book recipes
    /// with a consommé, drinks with their retained alcohol, ingredient statements, verified wording rows of both kinds,
    /// applied model picks), and every entity type a compiled query roots at is mapped to its table by the Postgres model.
    /// The store's own tables (<c>plan."Diet..."</c>) are left out: the reading writes them.
    /// </summary>
    internal static class DietGateReadingTables
    {
        private const long PersonId = 970, HouseholdId = 97, CeliacRef = 60800, MilkRef = 60700, NoAlcoholRef = 60913, Gram = 1;

        private sealed class Roots : IQueryExpressionInterceptor
        {
            public HashSet<Type> Types { get; } = new();

            public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            {
                new Visitor(Types).Visit(queryExpression);
                return queryExpression;
            }

            private sealed class Visitor : ExpressionVisitor
            {
                private readonly HashSet<Type> _types;

                public Visitor(HashSet<Type> types) => _types = types;

                protected override Expression VisitExtension(Expression node)
                {
                    if (node is EntityQueryRootExpression root)
                    {
                        lock (_types) _types.Add(root.EntityType.ClrType);
                        return node;
                    }
                    return base.VisitExtension(node);
                }
            }
        }

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private static IngredientEntity Curated(string name, int category, string description) =>
            new() { Name = name, FdcCategoryId = category, CurationStatusId = (long)CurationStatusEnum.Curated, Aliases = new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } } };

        /// <summary>The schema-qualified tables ("recipe.IngredientWording") the reading queries.</summary>
        public static async Task<IReadOnlySet<string>> QueriedAsync()
        {
            var roots = new Roots();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .AddInterceptors(roots)
                .Options;
            using (var db = new ApplicationDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() }))
            {
                await SeedAsync(db);
                var builder = new DietGateStoreBuilder();
                await builder.RunAsync(db);
                var bread = await db.Recipes.Where(r => r.Name == "Bread").Select(r => r.Id).SingleAsync();
                var flour = await db.Ingredients.Where(i => i.Name == "Wheat Flour").Select(i => i.Id).SingleAsync();
                db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = bread, WrittenAt = DateTime.UtcNow });
                db.DietGateWrites.Add(new DietGateWriteEntity { IngredientId = flour, WrittenAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                await builder.RunChangesAsync(db);
            }

            using var postgres = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            return roots.Types
                .Select(t => postgres.Model.FindEntityType(t))
                .OfType<IEntityType>()
                .Where(e => e.GetTableName() != null)
                .Select(e => e.GetSchema() + "." + e.GetTableName())
                .Where(t => !t.StartsWith("plan.Diet", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
        }

        private static async Task SeedAsync(ApplicationDbContext db)
        {
            foreach (var (id, name) in new[] { (CeliacRef, "Celiac Disease"), (MilkRef, "Milk Allergy"), (NoAlcoholRef, "No Alcohol") })
            {
                db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = id, Name = name });
                db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = name, RestrictionTypeId = id, Severity = 5 });
            }
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            var alcohol = new NutrientEntity { Name = DietCriteriaMatcher.AlcoholNutrientName };
            db.Set<NutrientEntity>().Add(alcohol);
            var flour = Curated("Wheat Flour", 20, "Wheat flour, white, all-purpose, enriched");
            var milk = Curated("Milk", 1, "Milk, whole, 3.25% milkfat");
            var beef = Curated("Beef Broth", 6, "Soup, beef broth, canned");
            var egg = Curated("Egg White", 1, "Egg, white, raw, fresh");
            var coconut = Curated("Fresh Coconut Meat", 12, "Nuts, coconut meat, raw");
            var brandy = Curated("Brandy", 14, "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof");
            var soup = Curated("Condensed Cream Of Chicken Soup", 6, "Soup, cream of chicken, canned, condensed");
            var isinglass = new IngredientEntity { Name = "isinglass", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var fish = Curated("Cod", 15, "Fish, cod, Atlantic, raw");
            db.Ingredients.AddRange(flour, milk, beef, egg, coconut, brandy, soup, isinglass, fish);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = brandy.Id, NutrientId = alcohol.Id, Amount = 33.4m, MeasurementId = Gram });
            db.IngredientContents.Add(new IngredientContentsEntity { IngredientId = soup.Id, Statement = "Ingredients: chicken stock, wheat flour, cream.", Source = "fdc:1" });
            db.IngredientWordings.AddRange(
                new IngredientWordingEntity { Wording = "isinglass", Verdict = IngredientWordingVerdict.Category, FdcCategoryId = 15, CategoryHead = "Fish", Batch = "t", Source = "verified:t" },
                new IngredientWordingEntity { Wording = "flour", Verdict = IngredientWordingVerdict.Food, IngredientId = flour.Id, Batch = "t", Source = "verified:t" });
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity { Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "Protose, ½ pound", ProposedValue = coconut.Id.ToString(), Source = "ai:qwen2.5:14b-instruct", Status = FoodProposalStatus.Applied, Batch = "fdc-link" });
            var bread = Recipe("Bread", (flour.Id, "3 cups wheat flour"), (milk.Id, "1 cup milk"));
            var consomme = Recipe("CLARIFIED CONSOMMÉ", (beef.Id, "2 quarts beef broth"), (egg.Id, "the white of an egg"));
            consomme.ScrapedAtUtc = DateTime.UtcNow;
            consomme.SourceUrl = "cookbook://escoffier/1";
            consomme.SourceAttribution = "Escoffier";
            consomme.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Clarify the consommé with the white of an egg." } };
            var soupRecipe = Recipe("Soup", (beef.Id, "1 pint consommé"), (soup.Id, "1 can cream of chicken soup"));
            soupRecipe.ScrapedAtUtc = DateTime.UtcNow;
            soupRecipe.SourceUrl = "cookbook://escoffier/2";
            soupRecipe.SourceAttribution = "Escoffier";
            soupRecipe.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Description = "Heat the consommé." } };
            var hash = Recipe("Protose Hash", (coconut.Id, "Protose, ½ pound."), (isinglass.Id, "1 oz isinglass"));
            var punch = Recipe("Punch", (brandy.Id, "1 cup brandy"), (fish.Id, "1 cod"));
            db.Recipes.AddRange(bread, consomme, soupRecipe, hash, punch);
            db.PlatformFeatures.Add(new Nom.Data.Platform.PlatformFeatureEntity { Key = DietSecondGate.FeatureKey, IsEnabled = true });
            await db.SaveChangesAsync();
            db.RecipeDietReadings.Add(new RecipeDietReadingEntity { RecipeId = bread.Id, Model = DietSecondGate.Pair[0], PromptSignature = Nom.Orch.Services.OllamaDietSecondReader.Signature, TextSignature = "t", StoreVersion = DietSecondGate.Version, Readable = true, Verdicts = "fffffffffff", ReadAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
        }
    }
}
