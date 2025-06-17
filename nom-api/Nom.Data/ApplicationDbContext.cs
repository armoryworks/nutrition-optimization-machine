using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

using Nom.Data.Reference;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Nutrient;
using Nom.Data.Shopping;
using Nom.Data.Person;
using Nom.Data.Question;
using System.Collections.Generic;
// Removed: using Nom.Data.Configurations; // No longer needed
// Removed: using System.Linq;           // No longer needed for audit loop
// Removed: using System.Reflection;     // No longer needed for audit loop

namespace Nom.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        #region Identity DbSets
        public DbSet<PersonEntity> Persons { get; set; } = default!;
        public DbSet<PersonAttributeEntity> PersonAttributes { get; set; } = default!;
        #endregion

        #region Reference DbSets
        public DbSet<ReferenceEntity> References { get; set; } = default!;
        public DbSet<GroupEntity> Groups { get; set; } = default!;
        public DbSet<GroupedReferenceViewEntity> GroupedReferenceViews { get; set; } = default!;
        #endregion

        #region Plan DbSets
        public DbSet<PlanEntity> Plans { get; set; } = default!;
        public DbSet<MealEntity> Meals { get; set; } = default!;
        public DbSet<GoalEntity> Goals { get; set; } = default!;
        public DbSet<GoalItemEntity> GoalItems { get; set; } = default!;
        public DbSet<RestrictionEntity> Restrictions { get; set; } = default!;
        #endregion

        #region Recipe DbSets
        public DbSet<RecipeEntity> Recipes { get; set; } = default!;
        public DbSet<IngredientEntity> Ingredients { get; set; } = default!;
        public DbSet<RecipeIngredientEntity> RecipeIngredients { get; set; } = default!;
        public DbSet<RecipeStepEntity> RecipeSteps { get; set; } = default!;
        public DbSet<IngredientNutrientEntity> IngredientNutrients { get; set; } = default!;
        #endregion

        #region Nutrient DbSets
        public DbSet<NutrientEntity> Nutrients { get; set; } = default!;
        public DbSet<NutrientComponentEntity> NutrientComponents { get; set; } = default!;
        public DbSet<NutrientGuidelineEntity> NutrientGuidelines { get; set; } = default!;
        #endregion

        #region Shopping DbSets
        public DbSet<ShoppingPreferenceEntity> ShoppingPreferences { get; set; } = default!;
        public DbSet<ShoppingTripEntity> ShoppingTrips { get; set; } = default!;
        public DbSet<PantryItemEntity> PantryItems { get; set; } = default!;
        #endregion

        #region Question DbSets
        public DbSet<QuestionEntity> Questions { get; set; } = default!;
        public DbSet<AnswerEntity> Answers { get; set; } = default!;
        #endregion

        // --- NEW: Audit Log DbSet ---
        public DbSet<AuditLogEntryEntity> AuditLogEntries { get; set; } = default!;
        // --- END NEW ---

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitly map Identity tables to the 'auth' schema
            modelBuilder.HasDefaultSchema("auth");

            // Configure PersonEntity to be in 'person' schema
            modelBuilder.Entity<PersonEntity>().ToTable("Person", schema: "person");
            modelBuilder.Entity<PersonAttributeEntity>().ToTable("PersonAttribute", schema: "person");

            // Configure unique index for InvitationCode on PersonEntity
            modelBuilder.Entity<PersonEntity>()
                .HasIndex(p => p.InvitationCode)
                .IsUnique()
                .HasFilter("\"InvitationCode\" IS NOT NULL");

            // --- NEW: Configure AuditLogEntryEntity ---
            modelBuilder.Entity<AuditLogEntryEntity>()
                .ToTable("AuditLogEntry", schema: "audit"); // Map to 'audit' schema

            modelBuilder.Entity<AuditLogEntryEntity>()
                .HasOne(ale => ale.ChangedByPerson)
                .WithMany() // Person has many audit entries, but AuditLogEntry does not expose a Person navigation collection
                .HasForeignKey(ale => ale.ChangedByPersonId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a person if they're referenced in audit logs
            // --- END NEW ---

            #region Fluent API Configurations by Namespace
            // These sections remain largely unchanged, as BaseEntity no longer dictates audit FKs
            // and we're not adding new audit-related Fluent API here.

            #region Reference Namespace Fluent API Configurations
            modelBuilder.Entity<ReferenceEntity>()
                .HasMany(r => r.Groups)
                .WithMany(g => g.References)
                .UsingEntity<Dictionary<string, object>>(
                    "ReferenceIndex",
                    j => j.HasOne<GroupEntity>()
                            .WithMany()
                            .HasForeignKey("GroupId")
                            .HasConstraintName("FK_ReferenceIndex_GroupEntity_GroupId"),
                    j => j.HasOne<ReferenceEntity>()
                            .WithMany()
                            .HasForeignKey("ReferenceId")
                            .HasConstraintName("FK_ReferenceIndex_ReferenceEntity_ReferenceId"),
                    j =>
                    {
                        j.ToTable("ReferenceIndex", "reference");
                        j.HasKey("ReferenceId", "GroupId");
                    });

            // CONFIGURE TPH FOR GroupedReferenceViewEntity (MAPPED TO VIEW)
            modelBuilder.Entity<GroupedReferenceViewEntity>()
                .ToView("ReferenceGroupView", "reference")
                .HasNoKey()
                .HasDiscriminator<long>(g => g.GroupId)
                .HasValue<MeasurementTypeViewEntity>((long)ReferenceDiscriminatorEnum.MeasurementType)
                .HasValue<MealTypeViewEntity>((long)ReferenceDiscriminatorEnum.MealType)
                .HasValue<RecipeTypeViewEntity>((long)ReferenceDiscriminatorEnum.RecipeType)
                .HasValue<ShoppingStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingStatusType)
                .HasValue<ItemStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.ItemStatusType)
                .HasValue<RestrictionTypeViewEntity>((long)ReferenceDiscriminatorEnum.RestrictionType)
                .HasValue<GoalTypeViewEntity>((long)ReferenceDiscriminatorEnum.GoalType)
                .HasValue<NutrientTypeViewEntity>((long)ReferenceDiscriminatorEnum.NutrientType)
                .HasValue<CuisineTypeViewEntity>((long)ReferenceDiscriminatorEnum.CuisineType)
                .HasValue<QuestionCategoryViewEntity>((long)ReferenceDiscriminatorEnum.QuestionCategory)
                .HasValue<AnswerTypeViewEntity>((long)ReferenceDiscriminatorEnum.AnswerType)
                ;

            #endregion // End of Reference Namespace Fluent API Configurations

            #region Plan Namespace Fluent API Configurations
            modelBuilder.Entity<Plan.MealEntity>()
                .HasMany(m => m.Recipes)
                .WithMany(r => r.Meals)
                .UsingEntity<Dictionary<string, object>>(
                    "MealRecipeIndex",
                    j => j.HasOne<Recipe.RecipeEntity>()
                            .WithMany()
                            .HasForeignKey("RecipeId")
                            .HasConstraintName("FK_MealRecipeIndex_RecipeEntity_RecipeId"),
                    j => j.HasOne<Plan.MealEntity>()
                            .WithMany()
                            .HasForeignKey("MealId")
                            .HasConstraintName("FK_MealRecipeIndex_MealEntity_MealId"),
                    j =>
                    {
                        j.ToTable("meal_recipe_index", "plan");
                        j.HasKey("MealId", "RecipeId");
                    });

            modelBuilder.Entity<Plan.PlanEntity>()
                .HasMany(p => p.Participants)
                .WithMany(p => p.PlansParticipatingIn)
                .UsingEntity<Dictionary<string, object>>(
                    "PlanPersonIndex",
                    j => j.HasOne<Person.PersonEntity>()
                            .WithMany()
                            .HasForeignKey("PersonId")
                            .HasConstraintName("FK_PlanPersonIndex_PersonEntity_PersonId"),
                    j => j.HasOne<Plan.PlanEntity>()
                            .WithMany()
                            .HasForeignKey("PlanId")
                            .HasConstraintName("FK_PlanPersonIndex_PlanEntity_PlanId"),
                    j =>
                    {
                        j.ToTable("plan_person_index", "plan");
                        j.HasKey("PlanId", "PersonId");
                    });

            modelBuilder.Entity<Plan.PlanEntity>()
                .HasMany(p => p.Administrators)
                .WithMany(p => p.PlansAdministering)
                .UsingEntity<Dictionary<string, object>>(
                    "PlanPersonAdministratorIndex",
                    j => j.HasOne<Person.PersonEntity>()
                            .WithMany()
                            .HasForeignKey("PersonId")
                            .HasConstraintName("FK_PlanPersonAdministratorIndex_PersonEntity_PersonId"),
                    j => j.HasOne<Plan.PlanEntity>()
                            .WithMany()
                            .HasForeignKey("PlanId")
                            .HasConstraintName("FK_PlanPersonAdministratorIndex_PlanEntity_PlanId"),
                    j =>
                    {
                        j.ToTable("plan_person_administrator_index", "plan");
                        j.HasKey("PlanId", "PersonId");
                    });

            #endregion // End of Plan Namespace Fluent API Configurations

            #region Recipe Namespace Fluent API Configurations
            modelBuilder.Entity<Recipe.RecipeEntity>()
                .HasMany(r => r.RecipeTypes)
                .WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "RecipeTypeIndex",
                    j => j.HasOne<ReferenceEntity>()
                            .WithMany()
                            .HasForeignKey("RecipeTypeId")
                            .HasConstraintName("FK_RecipeTypeIndex_ReferenceEntity_RecipeTypeId"),
                    j => j.HasOne<Recipe.RecipeEntity>()
                            .WithMany()
                            .HasForeignKey("RecipeId")
                            .HasConstraintName("FK_RecipeTypeIndex_RecipeEntity_RecipeId"),
                    j =>
                    {
                        j.ToTable("recipe_type_index", "recipe");
                        j.HasKey("RecipeId", "RecipeTypeId");
                    });

            #endregion // End of Recipe Namespace Fluent API Configurations

            #region Nutrient Namespace Fluent API Configurations
            // Relationships handled by [InverseProperty] attributes in the entity classes.
            #endregion // End of Nutrient Namespace Fluent API Configurations

            #region Shopping Namespace Fluent API Configurations
            modelBuilder.Entity<Shopping.ShoppingTripEntity>()
                .HasMany(st => st.Meals)
                .WithMany(m => m.ShoppingTrips)
                .UsingEntity<Dictionary<string, object>>(
                    "ShoppingTripMealIndex",
                    j => j.HasOne<Plan.MealEntity>()
                            .WithMany()
                            .HasForeignKey("MealId")
                            .HasConstraintName("FK_ShoppingTripMealIndex_MealEntity_MealId"),
                    j => j.HasOne<Shopping.ShoppingTripEntity>()
                            .WithMany()
                            .HasForeignKey("ShoppingTripId")
                            .HasConstraintName("FK_ShoppingTripMealIndex_ShoppingTripEntity_ShoppingTripId"),
                    j =>
                    {
                        j.ToTable("shopping_trip_meal_index", "shopping");
                        j.HasKey("ShoppingTripId", "MealId");
                    });
            #endregion // End of Shopping Namespace Fluent API Configurations

           #region Question Namespace Fluent API Configurations
            // Configure QuestionEntity
            modelBuilder.Entity<QuestionEntity>()
                .ToTable("Question", schema: "question");

            modelBuilder.Entity<QuestionEntity>()
                .HasOne(q => q.QuestionCategory)
                .WithMany()
                .HasForeignKey(q => q.QuestionCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QuestionEntity>()
                .HasOne(q => q.AnswerType)
                .WithMany()
                .HasForeignKey(q => q.AnswerTypeRefId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure AnswerEntity
            modelBuilder.Entity<AnswerEntity>()
                .ToTable("Answer", schema: "question");

            modelBuilder.Entity<AnswerEntity>()
                .HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AnswerEntity>()
                .HasOne(a => a.Person)
                .WithMany()
                .HasForeignKey(a => a.PersonId)
                .IsRequired(false) // PersonId on AnswerEntity can be null if it's a "system" answer or temporary, etc.
                .OnDelete(DeleteBehavior.Cascade); // If person is deleted, cascade answers.

            modelBuilder.Entity<AnswerEntity>()
                .HasOne(a => a.Plan)
                .WithMany()
                .HasForeignKey(a => a.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            // --- NEW: Fluent API for AnswerEntity's specific audit fields ---
            modelBuilder.Entity<AnswerEntity>()
                .HasOne(a => a.CreatedByPerson)
                .WithMany() // No inverse collection on Person for this specific audit
                .HasForeignKey(a => a.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a person if they created answers
            // --- END NEW ---

            #endregion // End of Question Namespace Fluent API Configurations

            #endregion // End of Fluent API Configurations by Namespace region
        }
    }
}