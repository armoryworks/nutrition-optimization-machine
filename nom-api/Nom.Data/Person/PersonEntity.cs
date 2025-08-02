using Microsoft.AspNetCore.Identity; // Assuming IdentityUser is used here
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Plan;
using Nom.Data.Audit; // For PlanEntity and PlanParticipantEntity
using Nom.Data.Recipe; // For RecipeEntity relationships

namespace Nom.Data.Person
{
    /// <summary>
    /// Represents a person in the system, distinct from their Identity user account.
    /// A person can be an administrator, a plan participant, or a recipient of notifications.
    /// </summary>
    [Table("Person", Schema = "person")]
    public class PersonEntity : BaseEntity
    {
        /// <summary>
        /// The display name for the person within the application (e.g., "John Doe", "Mom", "My Admin").
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional: Foreign key to the ASP.NET Core Identity user ID.
        /// Null if the person is an "unregistered" participant (e.g., a child) or the "System" person.
        /// </summary>
        public string? UserId { get; set; } // Matches IdentityUser.Id type (typically string)

        public virtual ICollection<PlanParticipantEntity> PlanParticipations { get; set; } = new List<PlanParticipantEntity>();

        // Other attributes can be added here or via a PersonAttributeEntity for extensibility
        public virtual ICollection<PersonAttributeEntity> Attributes { get; set; } = new List<PersonAttributeEntity>();
        public virtual ICollection<RestrictionEntity> Restrictions { get; set; } = new List<RestrictionEntity>();

        // User-specific features (from Mealie User entity)
        public virtual ICollection<RecipeEntity> FavoriteRecipes { get; set; } = new List<RecipeEntity>();
        public virtual ICollection<RecipeEntity> RatedRecipes { get; set; } = new List<RecipeEntity>();
        public virtual ICollection<RecipeRatingEntity> RecipeRatings { get; set; } = new List<RecipeRatingEntity>();
        public virtual ICollection<RecipeEntity> AuthoredRecipes { get; set; } = new List<RecipeEntity>();
    }
}
