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
using Nom.Data.Audit;
using System.Collections.Generic;

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
        public DbSet<PlanParticipantEntity> PlanParticipants { get; set; } = default!;
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

        // Audit Log DbSet
        public DbSet<AuditLogEntryEntity> AuditLogEntries { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitly map Identity tables to the 'auth' schema
            modelBuilder.HasDefaultSchema("auth");

            #region Person Namespace Fluent API Configurations
            modelBuilder.Entity<PersonEntity>().ToTable("Person", schema: "person");
            modelBuilder.Entity<PersonAttributeEntity>().ToTable("PersonAttribute", schema: "person");

            // Configure unique index for InvitationCode on PersonEntity
            modelBuilder.Entity<PersonEntity>()
                .HasIndex(p => p.InvitationCode)
                .IsUnique()
                .HasFilter("\"InvitationCode\" IS NOT NULL");

            // Configure the relationship between PersonEntity and PlanEntity (PlansAdministering)
            modelBuilder.Entity<PersonEntity>()
                .HasMany(p => p.PlansAdministering)
                .WithOne(plan => plan.CreatedByPerson)
                .HasForeignKey(plan => plan.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);

            // Person can be a participant in many plans
            modelBuilder.Entity<PersonEntity>()
                .HasMany(p => p.PlanParticipations)
                .WithOne(pp => pp.Person)
                .HasForeignKey(pp => pp.PersonId)
                .OnDelete(DeleteBehavior.Cascade);

            // Person can create many PlanParticipant records
            modelBuilder.Entity<PersonEntity>()
                .HasMany(p => p.CreatedPlanParticipations)
                .WithOne(pp => pp.CreatedByPerson)
                .HasForeignKey(pp => pp.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);
            #endregion

            #region Audit Namespace Fluent API Configurations
            modelBuilder.Entity<AuditLogEntryEntity>()
                .ToTable("AuditLogEntry", schema: "audit");

            modelBuilder.Entity<AuditLogEntryEntity>()
                .HasOne(ale => ale.ChangedByPerson)
                .WithMany()
                .HasForeignKey(ale => ale.ChangedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);
            #endregion

            #region Reference Namespace Fluent API Configurations
            modelBuilder.Entity<GroupEntity>()
                .ToTable("Group", schema: "reference");

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
                .HasValue<PlanInvitationRoleViewEntity>((long)ReferenceDiscriminatorEnum.PlanInvitationRole)
                ;

            #endregion // End of Reference Namespace Fluent API Configurations

            #region Plan Namespace Fluent API Configurations
            modelBuilder.Entity<PlanEntity>()
                .ToTable("Plan", schema: "plan");

            // Unique index for InvitationCode on PlanEntity
            modelBuilder.Entity<PlanEntity>()
                .HasIndex(p => p.InvitationCode)
                .IsUnique()
                .HasFilter("\"InvitationCode\" IS NOT NULL");

            // Plan has many Restrictions
            modelBuilder.Entity<PlanEntity>()
                .HasMany(p => p.Restrictions)
                .WithOne(r => r.Plan)
                .HasForeignKey(r => r.PlanId)
                .IsRequired(false) // PlanId is nullable on RestrictionEntity
                .OnDelete(DeleteBehavior.Cascade);

            // Plan has many Participants
            modelBuilder.Entity<PlanEntity>()
                .HasMany(p => p.Participants)
                .WithOne(pp => pp.Plan)
                .HasForeignKey(pp => pp.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Plan.RestrictionEntity
            modelBuilder.Entity<RestrictionEntity>()
                .ToTable("Restriction", schema: "plan");

            // CRITICAL: CHECK CONSTRAINT for RestrictionEntity - At least one of PersonId or PlanId must be non-null.
            modelBuilder.Entity<RestrictionEntity>()
                .HasCheckConstraint("CHK_Restriction_PersonOrPlan",
                                    "\"PersonId\" IS NOT NULL OR \"PlanId\" IS NOT NULL");

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.Person)
                .WithMany()
                .HasForeignKey(r => r.PersonId)
                .IsRequired(false) // PersonId is nullable
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.RestrictionType)
                .WithMany()
                .HasForeignKey(r => r.RestrictionTypeId)
                .IsRequired(false) // RestrictionTypeId is nullable on the entity
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.Ingredient)
                .WithMany()
                .HasForeignKey(r => r.IngredientId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.Nutrient)
                .WithMany()
                .HasForeignKey(r => r.NutrientId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.CreatedByPerson)
                .WithMany()
                .HasForeignKey(r => r.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);


            // Configure PlanParticipantEntity
            modelBuilder.Entity<PlanParticipantEntity>()
                .ToTable("PlanParticipant", schema: "plan");

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasKey(pp => new { pp.PlanId, pp.PersonId }); // Composite primary key

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.Plan)
                .WithMany(p => p.Participants)
                .HasForeignKey(pp => pp.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.Person)
                .WithMany(p => p.PlanParticipations) // Assuming PersonEntity has PlanParticipations collection
                .HasForeignKey(pp => pp.PersonId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.Role)
                .WithMany()
                .HasForeignKey(pp => pp.RoleRefId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.CreatedByPerson)
                .WithMany(p => p.CreatedPlanParticipations)
                .HasForeignKey(pp => pp.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);

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

            modelBuilder.Entity<QuestionEntity>()
                .Property(q => q.NextQuestionOnTrue)
                .IsRequired(false); // Nullable long for optional workflow question ID

            modelBuilder.Entity<QuestionEntity>()
                .Property(q => q.NextQuestionOnFalse)
                .IsRequired(false); // Nullable long for optional workflow question ID

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
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AnswerEntity>()
                .HasOne(a => a.Plan)
                .WithMany()
                .HasForeignKey(a => a.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            // Fluent API for AnswerEntity's specific audit fields
            modelBuilder.Entity<AnswerEntity>()
                .HasOne(a => a.CreatedByPerson)
                .WithMany()
                .HasForeignKey(a => a.CreatedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);
            #endregion // End of Question Namespace Fluent API Configurations
        }
    }
}
