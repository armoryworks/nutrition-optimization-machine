using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// What one model of the second diet reader read in one recipe for the eleven severe diet types, stored by the shadow
    /// lane diet-second-reader: the model, the reader's prompt signature, the signature of the recipe text it was shown
    /// (title, lines and method), the store version it was written under, whether the reply was readable and whether the
    /// reader found the recipe whole, and one letter per type (<c>c</c> contains, <c>m</c> may, <c>f</c> free) in the
    /// reader's type order. A reading whose prompt or text signature is not the current one is not read by anything; the lane
    /// reads the recipe again. Only the lane writes a row, and a row never holds a nutrition number.
    /// </summary>
    public class RecipeDietReadingEntity
    {
        public long RecipeId { get; set; }

        public string Model { get; set; } = string.Empty;

        public string PromptSignature { get; set; } = string.Empty;

        public string TextSignature { get; set; } = string.Empty;

        public string StoreVersion { get; set; } = string.Empty;

        /// <summary>False when the host gave no readable reply in time; such a reading holds the recipe and is read again on the lane's next pass.</summary>
        public bool Readable { get; set; }

        public bool Complete { get; set; }

        /// <summary>One letter per severe type of the reader (<c>c</c>, <c>m</c> or <c>f</c>); empty when the reply was not readable.</summary>
        public string Verdicts { get; set; } = string.Empty;

        /// <summary>The reply as kept: the unlisted parts and each type's answer and carrier food, as JSON.</summary>
        public string? Answer { get; set; }

        public double Seconds { get; set; }

        public DateTime ReadAt { get; set; }
    }
}
