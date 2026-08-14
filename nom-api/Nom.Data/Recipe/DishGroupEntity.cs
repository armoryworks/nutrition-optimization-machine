namespace Nom.Data.Recipe
{
    /// <summary>
    /// A canonical dish — the plain name a cookbook index would use
    /// ("chocolate chip cookies", "butter chicken"). Recipes that are takes on
    /// the same dish share a group, which powers variation browsing ("12 takes
    /// on banana bread"), duplicate awareness during curation, and grouped
    /// search results. Assignment is suggested automatically (heuristic, or
    /// AI-backed when configured) and stays admin-correctable; a null group on
    /// a recipe simply means "not yet classified".
    /// </summary>
    public class DishGroupEntity : BaseEntity
    {
        /// <summary>Canonical display name, lowercase ("chocolate chip cookies").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>URL-safe unique key ("chocolate-chip-cookies").</summary>
        public string Slug { get; set; } = string.Empty;

        public virtual ICollection<RecipeEntity> Recipes { get; set; } = new List<RecipeEntity>();
    }
}
