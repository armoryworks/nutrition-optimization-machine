namespace Nom.Orch.Enums
{
    /// <summary>
    /// Recipe Comment Types - maps to Reference domain IDs
    /// </summary>
    public enum RecipeCommentTypeEnum : long
    {
        Unknown = 0,
        
        // Recipe Comment Types (10300-10399)
        General = 10301L,
        Review = 10302L,
        Suggestion = 10303L,
        Question = 10304L
    }
} 