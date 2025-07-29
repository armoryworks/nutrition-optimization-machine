namespace Nom.Data.Recipe
{
    public enum CurationStatusEnum : long
    {
        NonCurated = 9000L,
        PendingCuration = 9001L,
        RequiresRevision = 9002L,
        Curated = 9003L,
        Rejected = 9004L
    }
}