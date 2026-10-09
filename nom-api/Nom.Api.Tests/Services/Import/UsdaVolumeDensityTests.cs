using FluentAssertions;
using Nom.Import.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Import
{
    /// <summary>
    /// Grams per millilitre comes from USDA's own household portions, so a cup of flour weighs
    /// what USDA measured rather than what a cup of water would.
    /// </summary>
    public class UsdaVolumeDensityTests
    {
        [Fact]
        public void Sr_legacy_reads_the_unit_from_the_modifier_and_prefers_the_cup()
        {
            var flour = new[]
            {
                new UsdaPortion(1m, "9999", "tbsp", null, 7.8m),
                new UsdaPortion(1m, "9999", "cup", null, 125m),
            };

            UsdaVolumeDensity.GramsPerMilliliter(flour).Should().Be(0.5283m);
        }

        [Fact]
        public void A_plain_measure_beats_a_prepared_one()
        {
            var onions = new[]
            {
                new UsdaPortion(1m, "9999", "cup, chopped", null, 160m),
                new UsdaPortion(1m, "9999", "tbsp", null, 10m),
                new UsdaPortion(1m, "9999", "large", null, 150m),
            };

            UsdaVolumeDensity.GramsPerMilliliter(onions).Should().Be(0.6763m);
        }

        [Fact]
        public void Foundation_names_the_unit_by_id()
        {
            UsdaVolumeDensity.GramsPerMilliliter(new[] { new UsdaPortion(1m, "1000", "", null, 244m) }).Should().Be(1.0313m);
        }

        [Fact]
        public void Counts_and_weights_give_no_density()
        {
            var portions = new[]
            {
                new UsdaPortion(1m, "9999", "large", null, 50m),
                new UsdaPortion(1m, "9999", "oz", null, 28.35m),
                new UsdaPortion(1m, "9999", "cupcake", null, 40m),
            };

            UsdaVolumeDensity.GramsPerMilliliter(portions).Should().BeNull();
        }
    }
}
