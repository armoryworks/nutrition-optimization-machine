namespace Nom.Data.Recipe
{
    /// <summary>
    /// Recipe visibility tier (household-policies design doc §4). Stored as an
    /// int column with a code-owned closed set (not a reference row: the set is
    /// structural, enforced in the orchestration layer, and never user-extended).
    ///
    /// Effective read rule: Public recipes additionally require Approved
    /// curation to appear on public surfaces (curation continues to gate
    /// public listing); Audience recipes are visible to member households of a
    /// linked audience and are NEVER curation-eligible; Household recipes to
    /// the author's household; Private to the author only.
    /// </summary>
    public enum RecipeVisibilityEnum
    {
        Private = 1,
        Household = 2,
        Audience = 3,
        Public = 4,
    }
}
