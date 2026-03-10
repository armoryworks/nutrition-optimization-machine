namespace Nom.Data.CustomMigration
{
    internal class SeedRecipeStepModel
    {
        public int StepNumber { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
