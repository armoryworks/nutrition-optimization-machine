using System;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// DTO for capturing a single person attribute (e.g., height, weight).
    /// Corresponds to PersonAttributeEntity.
    /// </summary>
    public class PersonAttributeRequest
    {
        [Required(ErrorMessage = "Attribute Type ID is required.")]
        public long AttributeTypeRefId { get; set; } // Reference to a MeasurementType, for example

        [Required(ErrorMessage = "Value is required.")]
        public string Value { get; set; } = string.Empty; // Store as string for flexibility, parse as needed in orchestration
    }
}
