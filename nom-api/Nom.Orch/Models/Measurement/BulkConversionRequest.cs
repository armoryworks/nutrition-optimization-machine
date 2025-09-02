using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for bulk measurement conversions
    /// </summary>
    public class BulkConversionRequest
    {
        /// <summary>
        /// Source measurement ID
        /// </summary>
        [Required]
        public long FromId { get; set; }

        /// <summary>
        /// Target measurement ID
        /// </summary>
        [Required]
        public long ToId { get; set; }

        /// <summary>
        /// Value to convert
        /// </summary>
        [Required]
        public decimal Value { get; set; }
    }
}









