namespace Nom.Data.CustomMigration
{
    internal class SeedRecipeIngredientDto
    {
        public long IngredientId { get; set; }
        public decimal Quantity { get; set; }
        public long MeasurementId { get; set; }
        public string RawLine { get; set; } = string.Empty;
    }
}
