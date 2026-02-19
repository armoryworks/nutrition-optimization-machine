namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity for Diets &amp; Eating Patterns (voluntary dietary frameworks).
    /// Materialized by EF Core when GroupId matches the PersonDietaryRestrictionType Group's ID in the view.
    /// </summary>
    public class PersonDietaryRestrictionTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
