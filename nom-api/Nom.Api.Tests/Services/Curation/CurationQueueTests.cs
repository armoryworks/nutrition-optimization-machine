using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// The admin curation queue holds authored submissions only; imported catalog foods are
    /// triaged in Food Catalog. Ingredient rows carry their food group and per-100 g macros.
    /// </summary>
    public class CurationQueueTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        [Fact]
        public async Task Queue_ExcludesAuthorlessCatalogImports_AndCarriesNutrition()
        {
            using var ctx = NewContext();
            var author = new PersonEntity { Name = "Ada Cook" };
            var energy = new NutrientEntity { Name = "Energy" };
            var protein = new NutrientEntity { Name = "Protein" };
            var authored = new IngredientEntity
            {
                Name = "House Granola",
                Author = author,
                CurationStatusId = (long)CurationStatusEnum.PendingCuration,
            };
            authored.IngredientNutrients.Add(new IngredientNutrientEntity { Nutrient = energy, Amount = 471m });
            authored.IngredientNutrients.Add(new IngredientNutrientEntity { Nutrient = protein, Amount = 10.5m });
            ctx.Ingredients.AddRange(
                authored,
                new IngredientEntity
                {
                    Name = "VITAMIN D WHOLE MILK",
                    FdcId = "123456",
                    FdcDataType = "branded_food",
                    CurationStatusId = (long)CurationStatusEnum.PendingCuration,
                });
            await ctx.SaveChangesAsync();

            var queue = await new CurationOrchestrationService(ctx, NullLogger<CurationOrchestrationService>.Instance)
                .GetCurationQueueAsync();

            var item = queue.Should().ContainSingle().Subject;
            item.Name.Should().Be("House Granola");
            item.AuthorName.Should().Be("Ada Cook");
            item.CaloriesPer100g.Should().Be(471m);
            item.ProteinPer100g.Should().Be(10.5m);
            item.FatPer100g.Should().BeNull();
        }
    }
}
