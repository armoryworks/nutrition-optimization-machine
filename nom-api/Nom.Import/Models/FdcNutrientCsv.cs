using CsvHelper.Configuration.Attributes;

namespace Nom.Import.Data.Fdc.CsvModels
{
    /// <summary>
    /// Represents a row from the FDC 'nutrient.csv' file.
    /// </summary>
    public class FdcNutrientCsv
    {
        [Name("id")]
        public string Id { get; set; } = string.Empty;

        [Name("name")]
        public string Name { get; set; } = string.Empty;

        [Name("unit_name")]
        public string UnitName { get; set; } = string.Empty;

        [Name("nutrient_nbr")]
        public string NutrientNbr { get; set; } = string.Empty; // Keeping as string as per CSV

        [Name("rank")]
        public string Rank { get; set; } = string.Empty; // Keeping as string as per CSV
    }
}
