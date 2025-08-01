namespace Nom.Orch.Enums
{
    /// <summary>
    /// Recipe Status Types - maps to Reference domain IDs
    /// </summary>
    public enum RecipeStatusTypeEnum : long
    {
        Unknown = 0,
        
        // Recipe Status Types (10100-10199)
        Draft = 10101L,
        Published = 10102L,
        Archived = 10103L,
        Deleted = 10104L
    }
} 