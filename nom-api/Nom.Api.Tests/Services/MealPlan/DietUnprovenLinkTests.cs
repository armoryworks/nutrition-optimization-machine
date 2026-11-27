using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Staging checkpoint 8, T3: no link re-check class refutes the Protose wordings on Fresh Coconut Meat, so the 41 lines
    /// stay on the food until the verified wording route takes them. Meanwhile the unproven-link rule must read such a line
    /// by its words for every severe type, as unknown, never as the coconut the model pick put it on; and the wording's
    /// verified row (batch w03) names no catalog food, so it proves no link to any food.
    /// </summary>
    public class DietUnprovenLinkTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static IngredientEntity Curated(string name, int category, string description) =>
            new() { Name = name, FdcCategoryId = category, CurationStatusId = (long)CurationStatusEnum.Curated, Aliases = new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } } };

        [Fact]
        public async Task A_protose_line_on_a_model_link_to_coconut_reads_unknown_for_every_severe_type()
        {
            using var db = NewContext();
            var severe = DefaultRestrictionCriteria.ByName.Where(t => t.Value.Severity >= 4).Select(t => t.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
            severe.Should().Contain(new[] { "Celiac Disease", "Wheat Allergy", "Peanut Allergy", "Tree Nut Allergy", "Soy Allergy", "Coconut Allergy" });
            long typeId = 80_000;
            foreach (var name in severe) db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId++, Name = name });
            var coconut = Curated("Fresh Coconut Meat", 12, "Nuts, coconut meat, raw");
            db.Ingredients.Add(coconut);
            await db.SaveChangesAsync();
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity { Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "Protose, ½ pound", ProposedValue = coconut.Id.ToString(), Source = "ai:qwen2.5:14b-instruct", Status = FoodProposalStatus.Applied, Batch = "fdc-link" });
            var hash = new RecipeEntity
            {
                Name = "PROTOSE HASH", AuthorId = 1, CurationStatusId = (long)CurationStatusEnum.NonCurated,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = coconut.Id, MeasurementId = 1, Quantity = 0.5m, RawLine = "Protose, ½ pound.", SortOrder = 0 } },
            };
            db.Recipes.Add(hash);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var sets = await db.DietGateSets.AsNoTracking().Where(s => s.Severe).ToListAsync();
            var names = await db.Set<ReferenceEntity>().AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
            sets.Select(s => names[s.RestrictionTypeId]).Distinct().Should().BeEquivalentTo(severe);
            var flags = await db.DietRecipeFlags.AsNoTracking().Where(f => f.RecipeId == hash.Id).ToListAsync();
            sets.Select(s => names[s.RestrictionTypeId] + ": " + string.Join(", ", flags.Where(f => f.SetId == s.Id).Select(f => f.Verdict + "/" + f.Reason)))
                .Where(t => !t.EndsWith(": " + DietVerdict.Unknown + "/" + DietFlagReason.UnknownWord, StringComparison.Ordinal))
                .Should().Equal(new[] { "Coconut Allergy: Contains/Food" },
                    "\"protose\" is no word of the curated catalog and its link to coconut is an unproven model pick, so every severe type reads the line by its words, as unknown; only the type that restricts the food the pick put it on still flags it by that food, on the safe side");
        }

        [Fact]
        public void The_w03_row_gives_protose_no_catalog_food_and_cites_what_it_is()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "VerifiedWordings", "batch-w03.tsv"));
            var problems = new List<VerifiedWordingProblem>();
            var rows = VerifiedWordingService.ParseTsv(text, problems);
            problems.Should().BeEmpty();
            var protose = rows.Single(r => r.Wording == "protose");
            protose.Verdict.Should().Be(IngredientWordingVerdict.None, "the catalog has no protose: a verdict naming a food would prove the link the reading must not trust");
            protose.FoodId.Should().BeNull();
            protose.Note.Should().Contain("meat substitute").And.Contain("unknown");
        }
    }
}
