using Nom.Data.Plan;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Membership of a household in an audience. Explicit join entity (house
    /// rule: no implicit EF many-to-many — they land in the auth schema).
    /// </summary>
    public class AudienceMemberEntity : BaseEntity
    {
        public long AudienceId { get; set; }
        public virtual AudienceEntity? Audience { get; set; }

        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }
    }
}
