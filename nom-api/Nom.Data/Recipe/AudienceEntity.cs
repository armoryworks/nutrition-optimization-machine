using System.Collections.Generic;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A named set of households that audience-visibility recipes can be scoped
    /// to (household-policies design doc §4). Owned by a person; optionally
    /// maintained by an external management tool (ManagedBy marker).
    /// </summary>
    public class AudienceEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public long OwnerPersonId { get; set; }
        public virtual PersonEntity? OwnerPerson { get; set; }

        /// <summary>Opaque external-manager marker (e.g. "brigade:456"); NULL = user-managed.</summary>
        public string? ManagedBy { get; set; }

        public virtual ICollection<AudienceMemberEntity> Members { get; set; } = new List<AudienceMemberEntity>();
        public virtual ICollection<RecipeAudienceEntity> Recipes { get; set; } = new List<RecipeAudienceEntity>();
    }
}
