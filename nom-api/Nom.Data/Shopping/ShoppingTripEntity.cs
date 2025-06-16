using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Plan;

namespace Nom.Data.Shopping
{
    [Table("ShoppingTrip", Schema = "shopping")]
    public class ShoppingTripEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "date")]
        public DateOnly PlannedDate { get; set; }

        [Column(TypeName = "date")]
        public DateOnly? ActualDate { get; set; }

        [Required]
        public long PersonId { get; set; }
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity Person { get; set; } = default!; // Inverse of PersonEntity.ShoppingTrips

        public long? StatusId { get; set; }
        [ForeignKey(nameof(StatusId))]
        public virtual Reference.ReferenceEntity? Status { get; set; }

        public virtual ICollection<MealEntity>? Meals { get; set; }
    }
}