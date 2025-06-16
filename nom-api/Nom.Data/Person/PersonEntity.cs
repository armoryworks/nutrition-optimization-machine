using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Shopping;

namespace Nom.Data.Person
{
    [Table("Person", Schema = "person")]
    public class PersonEntity : BaseEntity
    {
        [MaxLength(255)]
        public string? Name { get; set; }

        public string? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual IdentityUser? User { get; set; }

        public virtual ICollection<PersonAttributeEntity>? Attributes { get; set; }

        // Person-Plan (M-M) - No [InverseProperty] needed here, defined by UsingEntity's WithMany()
        public virtual ICollection<PlanEntity>? PlansParticipatingIn { get; set; }
        public virtual ICollection<PlanEntity>? PlansAdministering { get; set; }

        // Person-Recipe (1-M) - [InverseProperty] on collections
        [InverseProperty(nameof(RecipeEntity.Creator))]
        public virtual ICollection<RecipeEntity>? CreatedRecipes { get; set; }

        [InverseProperty(nameof(RecipeEntity.Curator))]
        public virtual ICollection<RecipeEntity>? CuratedRecipes { get; set; }

        // Person-ShoppingPreference (1-1) - [InverseProperty] on collection
        [InverseProperty(nameof(ShoppingPreferenceEntity.Person))]
        public virtual ShoppingPreferenceEntity? ShoppingPreference { get; set; }

        // Person-ShoppingTrip (1-M) - [InverseProperty] on collection
        [InverseProperty(nameof(ShoppingTripEntity.Person))]
        public virtual ICollection<ShoppingTripEntity>? ShoppingTrips { get; set; }
    }
}