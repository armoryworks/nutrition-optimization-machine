namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Allergy Types.
    /// Materialized by EF Core when GroupId matches the AllergyType Group's ID in the view.
    /// </summary>
    public class AllergyTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
