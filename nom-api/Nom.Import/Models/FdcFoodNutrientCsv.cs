using CsvHelper.Configuration.Attributes;

namespace Nom.Import.Data.Fdc.CsvModels
{
    /// <summary>
    /// Represents a row from the FDC 'food_nutrient.csv' file.
    /// </summary>
    public class FdcFoodNutrientCsv
    {
        [Name("id")]
        public string Id { get; set; } = string.Empty;

        [Name("fdc_id")]
        public string FdcId { get; set; } = string.Empty;

        [Name("nutrient_id")]
        public string NutrientId { get; set; } = string.Empty;

        [Name("amount")]
        public string Amount { get; set; } = string.Empty; // Read as string, parse later

        [Name("data_points")]
        public string DataPoints { get; set; } = string.Empty;

        [Name("derivation_id")]
        public string DerivationId { get; set; } = string.Empty;

        [Name("min")]
        public string Min { get; set; } = string.Empty;

        [Name("max")]
        public string Max { get; set; } = string.Empty;

        [Name("median")]
        public string Median { get; set; } = string.Empty;

        [Name("loq")]
        public string Loq { get; set; } = string.Empty;

        [Name("footnote")]
        public string Footnote { get; set; } = string.Empty;

        [Name("min_year_acquired")]
        public string MinYearAcquired { get; set; } = string.Empty;

        [Name("percent_daily_value")]
        public string PercentDailyValue { get; set; } = string.Empty;
    }
}
