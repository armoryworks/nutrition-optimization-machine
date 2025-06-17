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
        [Required] // Name is now required, as per your UI flow
        [MinLength(2)] // Minimum length for identification
        [MaxLength(255)]
        public required string Name { get; set; } // Mark as required string for C# 11+

        public string? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual IdentityUser? User { get; set; }

        // --- NEW PROPERTY FOR INVITATION/LINKING ---
        /// <summary>
        /// A unique code generated for persons not yet registered as users,
        /// allowing them to link to this Person record during registration.
        /// Should be null for registered users.
        /// </summary>
        [MaxLength(36)] // Standard GUID string length
        public string? InvitationCode { get; set; }
        // --- END NEW PROPERTY ---


        public virtual ICollection<PersonAttributeEntity>? Attributes { get; set; }

        [InverseProperty(nameof(RecipeEntity.Creator))]
        public virtual ICollection<RecipeEntity>? CreatedRecipes { get; set; }

        [InverseProperty(nameof(RecipeEntity.Curator))]
        public virtual ICollection<RecipeEntity>? CuratedRecipes { get; set; }

        [InverseProperty(nameof(ShoppingPreferenceEntity.Person))]
        public virtual ShoppingPreferenceEntity? ShoppingPreference { get; set; }

        [InverseProperty(nameof(ShoppingTripEntity.Person))]
        public virtual ICollection<ShoppingTripEntity>? ShoppingTrips { get; set; }

        // M-M relationships with PlanEntity are configured in ApplicationDbContext,
        // so no InverseProperty is strictly required here.
        public virtual ICollection<PlanEntity>? PlansParticipatingIn { get; set; }
        public virtual ICollection<PlanEntity>? PlansAdministering { get; set; }
    }
}