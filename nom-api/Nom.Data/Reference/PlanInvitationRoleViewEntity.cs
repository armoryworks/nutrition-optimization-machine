using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents the view-specific entity for Plan Invitation Roles within the ReferenceGroupView.
    /// This entity is part of a Table-Per-Hierarchy (TPH) mapping, where
    /// its discriminator is ReferenceDiscriminatorEnum.PlanInvitationRole.
    /// </summary>
    public class PlanInvitationRoleViewEntity : GroupedReferenceViewEntity
    {
        // No additional properties are typically needed here beyond what's in GroupedReferenceViewEntity,
        // as this entity's primary purpose is to differentiate rows in the view by GroupId.
        // If there were specific properties unique to Plan Invitation Roles in the view, they would go here.
    }
}
