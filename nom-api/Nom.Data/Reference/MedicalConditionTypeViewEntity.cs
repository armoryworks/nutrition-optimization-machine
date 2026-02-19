namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Medical Condition Types.
    /// Materialized by EF Core when GroupId matches the MedicalConditionType Group's ID in the view.
    /// </summary>
    public class MedicalConditionTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
