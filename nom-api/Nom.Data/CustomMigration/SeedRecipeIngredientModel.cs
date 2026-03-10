namespace Nom.Data.CustomMigration
{
    internal class SeedRecipeIngredientModel
    {
        public long IngredientId { get; set; }
        public decimal Quantity { get; set; }
        public long MeasurementId { get; set; }
        public string RawLine { get; set; } = string.Empty;
    }
}
