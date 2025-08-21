namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Model representing a measurement conversion rule in the API layer.
    /// </summary>
    public class MeasurementConversionModel
    {
        public long Id { get; set; }
        public long FromMeasurementId { get; set; }
        public string FromMeasurementName { get; set; } = string.Empty;
        public string FromMeasurementSymbol { get; set; } = string.Empty;
        public long ToMeasurementId { get; set; }
        public string ToMeasurementName { get; set; } = string.Empty;
        public string ToMeasurementSymbol { get; set; } = string.Empty;
        public decimal ConversionFactor { get; set; }
        public decimal? Offset { get; set; }
        public string? Formula { get; set; }
        public bool IsDirectConversion { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}
