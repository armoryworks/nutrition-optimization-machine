using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// That a diet gate build has decided a food under every one of its sets, with the fingerprint of what the decision
    /// read (name, category, aliases, ingredient statement, the criteria's nutrients and the food's weights); the build
    /// job decides the food again when the fingerprint changes.
    /// </summary>
    public class DietFoodStateEntity
    {
        public long BuildId { get; set; }

        public long IngredientId { get; set; }

        public long Fingerprint { get; set; }

        public DateTime ComputedAt { get; set; }
    }
}
