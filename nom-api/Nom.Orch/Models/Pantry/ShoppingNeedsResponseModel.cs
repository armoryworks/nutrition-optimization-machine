using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Pantry
{
    /// <summary>
    /// Response for the shopping needs endpoint.
    /// </summary>
    public class ShoppingNeedsResponseModel
    {
        public long HouseholdId { get; set; }
        public int DaysAhead { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public int MealCount { get; set; }
        public List<ShoppingNeedModel> Needs { get; set; } = new();
    }
}
