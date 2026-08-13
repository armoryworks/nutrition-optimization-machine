namespace Nom.Data.Recipe
{
    /// <summary>
    /// Scoping of an audience-visibility recipe to an audience. Explicit join
    /// entity (house rule: no implicit EF many-to-many).
    /// </summary>
    public class RecipeAudienceEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long AudienceId { get; set; }
        public virtual AudienceEntity? Audience { get; set; }
    }
}
