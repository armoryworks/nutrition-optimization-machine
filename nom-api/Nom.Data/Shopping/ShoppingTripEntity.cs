using System;
using System.Collections.Generic;
using Nom.Data.Person;
using Nom.Data.Plan;

namespace Nom.Data.Shopping
{
    public class ShoppingTripEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public DateOnly PlannedDate { get; set; }

        public DateOnly? ActualDate { get; set; }

        public long PersonId { get; set; }
        public virtual PersonEntity Person { get; set; } = default!; // Inverse of PersonEntity.ShoppingTrips

        public long? StatusId { get; set; }
        public virtual Reference.ReferenceEntity? Status { get; set; }

        public virtual ICollection<MealEntity>? Meals { get; set; }
    }
}
