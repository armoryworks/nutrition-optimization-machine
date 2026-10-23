using System;
using Nom.Data.Person;
using Nom.Data.Plan;

namespace Nom.Data.Shopping
{
    /// <summary>
    /// Shared check-off state for a computed shopping line (one that comes from the meal plan
    /// or the pantry and so has no row of its own). One row per (household, line key), toggled
    /// in place so the unique index never meets a soft-deleted twin. A check counts only while
    /// <see cref="CheckedDate"/> is within the configured TTL.
    /// </summary>
    public class HouseholdShoppingCheckEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>The UI's key for the line; today the ingredient id for a computed line.</summary>
        public string ItemKey { get; set; } = string.Empty;

        public bool Checked { get; set; }
        public DateTime? CheckedDate { get; set; }

        public long? CheckedByPersonId { get; set; }
        public virtual PersonEntity? CheckedByPerson { get; set; }
    }
}
