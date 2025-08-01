namespace Nom.Orch.Enums
{
    /// <summary>
    /// Recipe Share Token Types - maps to Reference domain IDs
    /// </summary>
    public enum RecipeShareTokenTypeEnum : long
    {
        Unknown = 0,
        
        // Recipe Share Token Types (10200-10299)
        Public = 10201L,
        Private = 10202L,
        Temporary = 10203L
    }
} 