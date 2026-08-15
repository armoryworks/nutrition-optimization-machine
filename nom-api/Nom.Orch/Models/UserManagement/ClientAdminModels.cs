// File: Nom.Orch/Models/UserManagement/ClientAdminModels.cs

using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.UserManagement
{
    /// <summary>
    /// Admin-facing summary of one household ("client") on this instance.
    /// </summary>
    public class AdminHouseholdModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsPersonal { get; set; }

        /// <summary>External management marker (e.g. "brigade:456"); null = self-managed.</summary>
        public string? ManagedBy { get; set; }

        public int MemberCount { get; set; }
        public int ActiveMemberCount { get; set; }
        public DateTime CreatedDate { get; set; }

        /// <summary>Date of the most recent meal-plan slot, as a rough activity signal.</summary>
        public DateOnly? LastPlanDate { get; set; }
    }

    /// <summary>
    /// Admin-facing view of one member within a household.
    /// </summary>
    public class AdminHouseholdMemberModel
    {
        public long PersonId { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Identity user id when the person has a login; null for profile-only members (kids etc.).</summary>
        public string? UserId { get; set; }
        public string? Email { get; set; }

        public string Role { get; set; } = "Member";
        public bool IsActive { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime? JoinedDate { get; set; }
    }
}
