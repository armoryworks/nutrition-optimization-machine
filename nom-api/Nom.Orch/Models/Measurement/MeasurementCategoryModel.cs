namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Model representing a measurement category in the API layer.
    /// </summary>
    public class MeasurementCategoryModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long BaseUnitId { get; set; }
        public string BaseUnitName { get; set; } = string.Empty;
        public string BaseUnitSymbol { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}
