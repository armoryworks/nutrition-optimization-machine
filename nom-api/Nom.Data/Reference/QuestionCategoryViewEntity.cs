// Nom.Data.Reference/QuestionCategoryViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Question Categories.
    /// This entity will be materialized by EF Core from the 'ReferenceGroupView'
    /// when the 'GroupId' in the view matches the 'QuestionCategory' ReferenceDiscriminatorEnum value.
    /// It inherits all properties from the base GroupedReferenceViewEntity.
    /// </summary>
    public class QuestionCategoryViewEntity : GroupedReferenceViewEntity
    {
        // No additional properties are needed here if it's just a discriminator.
        // It simply serves to create a distinct type for filtering in the TPH.
    }
}