namespace Nom.Orch.Enums
{
    /// <summary>
    /// Recipe Note Types - maps to Reference domain IDs
    /// </summary>
    public enum RecipeNoteTypeEnum : long
    {
        Unknown = 0,
        
        // Recipe Note Types (10400-10499)
        Private = 10401L,
        Public = 10402L,
        CookingTip = 10403L,
        Variation = 10404L,
        Substitution = 10405L
    }
} 