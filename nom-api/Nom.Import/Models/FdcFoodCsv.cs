using CsvHelper.Configuration.Attributes;

namespace Nom.Import.Data.Fdc.CsvModels
{
    /// <summary>
    /// Represents a row from the FDC 'food.csv' file.
    /// </summary>
    public class FdcFoodCsv
    {
        [Name("fdc_id")]
        public string FdcId { get; set; } = string.Empty;

        [Name("data_type")]
        public string DataType { get; set; } = string.Empty;

        [Name("description")]
        public string Description { get; set; } = string.Empty;

        [Name("food_category_id")]
        public string FoodCategoryId { get; set; } = string.Empty; // Keeping as string as per CSV

        [Name("publication_date")]
        public string PublicationDate { get; set; } = string.Empty; // Keeping as string as per CSV
    }
}
