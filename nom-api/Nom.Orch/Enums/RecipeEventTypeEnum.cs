namespace Nom.Orch.Enums
{
    /// <summary>
    /// Recipe Event Types - maps to Reference domain IDs
    /// </summary>
    public enum RecipeEventTypeEnum : long
    {
        Unknown = 0,
        
        // Recipe Event Types (10000-10099)
        Created = 10001L,
        Updated = 10002L,
        Published = 10003L,
        Rated = 10004L,
        Commented = 10005L,
        Made = 10006L,
        Shared = 10007L,
        Favorited = 10008L,
        AddedToPlan = 10009L,
        Exported = 10010L
    }
} 