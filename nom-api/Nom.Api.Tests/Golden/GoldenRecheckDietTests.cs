using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Tests.Services;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Import.Golden;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The guard of staging checkpoint 4, P19, against recheck-diet.tsv: each row is one recipe line on a USDA prepared
    /// food the prepared set takes lines off, or a drink line on a food the alcohol set takes it off, run through
    /// <see cref="LinkRecheckService"/> with every default restriction type on its own household. No re-check take-back may
    /// lower a recipe's diet flag for any type unless its wording reads as label language of known contents (a composite
    /// row or a single food of the cited table) or as a plain drink, whose own name keeps the drink's flags (staging
    /// checkpoint 6, R16); a wording of unknown contents that would lower one keeps its line where it is.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenRecheckDietTests
    {
        public const string File = "recheck-diet.tsv";

        private readonly ITestOutputHelper _output;

        public GoldenRecheckDietTests(ITestOutputHelper output) => _output = output;

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static async Task<Dictionary<string, HashSet<long>>> FlagsAsync(ApplicationDbContext db, IReadOnlyDictionary<string, long> households)
        {
            var lines = await db.RecipeIngredients.AsNoTracking().Where(ri => !ri.IsDeleted).Select(ri => new { ri.RecipeId, ri.IngredientId }).ToListAsync();
            var flags = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            foreach (var (type, household) in households)
            {
                var set = await new HouseholdRestrictionResolver(db).ResolveAsync(household);
                flags[type] = lines.Where(l => set.RecipeIds.Contains(l.RecipeId) || set.IngredientIds.Contains(l.IngredientId)).Select(l => l.RecipeId).ToHashSet();
            }
            return flags;
        }

        [Fact]
        public async Task No_take_back_lowers_a_recipes_diet_flag_unless_its_wordings_contents_are_known()
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            LinkRecheckClassBarTests.Qualify(db, LinkGate.Prepared, LinkRecheckRules.AlcoholClass);
            var households = new Dictionary<string, long>(StringComparer.Ordinal);
            var id = 67000L;
            foreach (var type in DefaultRestrictionCriteria.ByName.Keys.OrderBy(t => t, StringComparer.Ordinal))
            {
                db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = id, Name = type });
                db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = id, PersonId = id, IsActive = true });
                db.Restrictions.Add(new RestrictionEntity { PersonId = id, Name = type, RestrictionTypeId = id });
                households[type] = id++;
            }
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var rows = GoldenSet.Read(File).Rows.ToList();
            var foods = new Dictionary<string, IngredientEntity>(StringComparer.Ordinal);
            var recipes = new Dictionary<string, RecipeEntity>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!foods.TryGetValue(row["food"], out var food))
                {
                    food = new IngredientEntity
                    {
                        Name = row["food"], FdcId = row["fdcId"], FdcDataType = CatalogAdmissionRule.SrLegacyFood,
                        CurationStatusId = PreparedFoods.IsPrepared(int.Parse(row["category"]), CatalogAdmissionRule.SrLegacyFood, null) ? (long)CurationStatusEnum.PendingCuration : (long)CurationStatusEnum.Curated,
                        FdcCategoryId = int.Parse(row["category"]),
                        IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
                    };
                    if (row["description"] != GoldenTsv.NoAnswer) food.Aliases.Add(new IngredientAliasEntity { AliasName = row["description"], SourceContext = IngredientLinkService.UsdaDescriptionContext });
                    db.Ingredients.Add(food);
                    await db.SaveChangesAsync();
                    foods[row["food"]] = food;
                }
                var recipe = new RecipeEntity
                {
                    Name = "Recheck diet " + row.Id, ScrapedAtUtc = DateTime.UtcNow,
                    RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1, RawLine = row["line"] } },
                };
                db.Recipes.Add(recipe);
                recipes[row.Id] = recipe;
            }
            await db.SaveChangesAsync();

            var before = await FlagsAsync(db, households);
            var service = new LinkRecheckService(db, new NoUser(), new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions())), new LineRelinkService(db, new NoUser()),
                new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance), NullLogger<LinkRecheckService>.Instance);
            var run = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);
            run.Should().NotBeNull();
            var drinks = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            drinks.Should().NotBeNull();
            db.ChangeTracker.Clear();
            var after = await FlagsAsync(db, households);
            var on = (await db.RecipeIngredients.AsNoTracking().Select(ri => new { ri.RecipeId, ri.IngredientId }).ToListAsync()).ToDictionary(l => l.RecipeId, l => l.IngredientId);
            var names = await db.Ingredients.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name);
            string Wording(string rowId) => names[on[recipes[rowId].Id]];

            var report = GoldenSet.Run(File, "status", row =>
            {
                var recipe = recipes[row.Id].Id;
                var left = on[recipe] != foods[row["food"]].Id;
                var lowered = households.Keys.Where(t => before[t].Contains(recipe) && !after[t].Contains(recipe)).ToList();
                var actual = !left ? "held" : lowered.Count == 0 ? "kept" : "known";
                var pass = row["expect"] switch
                {
                    "held" => !left,
                    "kept" => left && lowered.Count == 0,
                    "known" => left && (lowered.Count == 0 || RecheckDietGuard.Known(Wording(row.Id))),
                    "drink" => left && (lowered.Count == 0 || RecheckDietGuard.ReadsAsDrink(Wording(row.Id))),
                    _ => false,
                };
                return new GoldenOutcome(pass, row["expect"], actual + (lowered.Count > 0 ? " (lowered: " + string.Join(", ", lowered) + ")" : string.Empty), new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);

            var dropped = rows.Where(r => on[recipes[r.Id].Id] != foods[r["food"]].Id)
                .SelectMany(r => households.Keys.Where(t => before[t].Contains(recipes[r.Id].Id) && !after[t].Contains(recipes[r.Id].Id)).Select(t => (Row: r, Type: t)))
                .Where(x => !RecheckDietGuard.Known(Wording(x.Row.Id)) && !RecheckDietGuard.ReadsAsDrink(Wording(x.Row.Id)))
                .Select(x => $"{x.Row.Id} {x.Row["line"]}: {x.Type}");
            dropped.Should().BeEmpty("a take-back to a wording of unknown contents that is not a plain drink never lowers a recipe's diet flag");
            (run!.Held + drinks!.Held).Should().Be(rows.Count(r => r["expect"] == "held"));
        }
    }
}
