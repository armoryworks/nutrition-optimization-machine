// Nom.Data/ApplicationDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

using Nom.Data.Reference;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Nutrient;
using Nom.Data.Shopping;
using Nom.Data.Person;
using Nom.Data.Audit;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading; // For CancellationToken
using System.Threading.Tasks; // For SaveChangesAsync
using Microsoft.AspNetCore.Http; // For IHttpContextAccessor

namespace Nom.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        private IHttpContextAccessor _httpContextAccessor;
        // This constructor is used by the application at runtime, with DI providing IHttpContextAccessor.
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // This constructor is specifically for design-time tools (like migrations).
        // It does not require IHttpContextAccessor because it's not available or needed then.
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
            _httpContextAccessor = null!;
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
        public DbSet<IngredientAliasEntity> IngredientAliases { get; set; } = default!; // NEW: DbSet for IngredientAliasEntity
        #endregion

        #region Nutrient DbSets
        public DbSet<NutrientEntity> Nutrients { get; set; } = default!;
        public DbSet<IngredientNutrientEntity> IngredientNutrients { get; set; } = default!;
        public DbSet<NutrientGuidelineEntity> NutrientGuidelines { get; set; } = default!;
        #endregion

        #region Shopping DbSets
        public DbSet<ShoppingPreferenceEntity> ShoppingPreferences { get; set; } = default!;
        public DbSet<ShoppingTripEntity> ShoppingTrips { get; set; } = default!;
        public DbSet<PantryItemEntity> PantryItems { get; set; } = default!;
        #endregion

        // Audit Log DbSet
        public DbSet<AuditLogEntryEntity> AuditLogEntries { get; set; } = default!;
        public DbSet<ImportJobEntity> ImportJobs { get; set; } = default!;


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

            // Person can be a participant in many plans
            modelBuilder.Entity<PersonEntity>()
                .HasMany(p => p.PlanParticipations)
                .WithOne(pp => pp.Person)
                .HasForeignKey(pp => pp.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
            #endregion

            #region Audit Namespace Fluent API Configurations
            modelBuilder.Entity<AuditLogEntryEntity>()
                .ToTable("AuditLogEntry", schema: "audit");

            modelBuilder.Entity<AuditLogEntryEntity>()
                .HasOne(ale => ale.ChangedByPerson)
                .WithMany()
                .HasForeignKey(ale => ale.ChangedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure ImportJobEntity
            modelBuilder.Entity<ImportJobEntity>()
                .ToTable("ImportJob", schema: "audit"); // Place in audit schema

            // Add a unique index on ProcessId for quick lookup
            modelBuilder.Entity<ImportJobEntity>()
                .HasIndex(ij => ij.ProcessId)
                .IsUnique();

            #endregion // End of Audit Namespace Fluent API Configurations

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
            .ToTable(t => t.HasCheckConstraint("CHK_Restriction_PersonOrPlan",
                                    "\"PersonId\" IS NOT NULL OR \"PlanId\" IS NOT NULL"));

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

            #endregion // End of Plan Namespace Fluent API Configurations

            #region Recipe Namespace Fluent API Configurations
            modelBuilder.Entity<Recipe.RecipeEntity>()
                .ToTable("Recipe", schema: "recipe"); // Explicitly set table name and schema

            // Configure relationships for new fields in RecipeEntity
            modelBuilder.Entity<RecipeEntity>()
                .HasOne(r => r.ServingQuantityMeasurementType)
                .WithMany()
                .HasForeignKey(r => r.ServingQuantityMeasurementTypeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RecipeEntity>()
                .HasOne(r => r.Curator)
                .WithMany() // Assuming PersonEntity doesn't have a collection for CuratedRecipes
                .HasForeignKey(r => r.CuratedById)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deleting person if they curated recipes

            modelBuilder.Entity<RecipeEntity>()
                .HasMany(r => r.Meals)
                .WithMany(m => m.Recipes)
                .UsingEntity<Dictionary<string, object>>(
                    "MealRecipeIndex",
                    j => j.HasOne<MealEntity>()
                            .WithMany()
                            .HasForeignKey("MealId")
                            .HasConstraintName("FK_MealRecipeIndex_MealEntity_MealId"),
                    j => j.HasOne<RecipeEntity>()
                            .WithMany()
                            .HasForeignKey("RecipeId")
                            .HasConstraintName("FK_MealRecipeIndex_RecipeEntity_RecipeId"),
                    j =>
                    {
                        j.ToTable("meal_recipe_index", "plan"); // Assuming 'plan' schema for meal-recipe links
                        j.HasKey("MealId", "RecipeId");
                    });


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

            // Add unique index for IngredientEntity.Name
            modelBuilder.Entity<IngredientEntity>(entity =>
            {
                entity.ToTable("Ingredient", schema: "recipe"); // Ensure table mapping
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.FdcId).IsUnique()
                      .HasFilter("\"FdcId\" IS NOT NULL"); // Only enforce uniqueness if FdcId is not null
            });

            // Configure IngredientAliasEntity
            modelBuilder.Entity<IngredientAliasEntity>(entity =>
            {
                entity.ToTable("IngredientAlias", schema: "recipe"); // Table mapping
                entity.HasKey(e => new { e.IngredientId, e.AliasName }); // Composite primary key for ON CONFLICT
                entity.HasOne(e => e.Ingredient)
                      .WithMany() // Assuming IngredientEntity doesn't have a collection for aliases
                      .HasForeignKey(e => e.IngredientId)
                      .OnDelete(DeleteBehavior.Cascade); // If ingredient is deleted, its aliases should be too
            });

            // Configure RecipeIngredientEntity
            modelBuilder.Entity<RecipeIngredientEntity>(entity =>
            {
                entity.ToTable("RecipeIngredient", schema: "recipe");
                entity.HasKey(e => new { e.RecipeId, e.IngredientId }); // Composite primary key
                entity.HasOne(e => e.Recipe)
                      .WithMany(r => r.RecipeIngredients)
                      .HasForeignKey(e => e.RecipeId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Ingredient)
                      .WithMany()
                      .HasForeignKey(e => e.IngredientId)
                      .OnDelete(DeleteBehavior.Restrict); // Don't delete ingredient if recipe ingredient exists
                entity.HasOne(e => e.MeasurementType)
                      .WithMany()
                      .HasForeignKey(e => e.MeasurementTypeId)
                      .OnDelete(DeleteBehavior.Restrict); // Don't delete measurement type if in use
            });

            // Configure RecipeStepEntity
            modelBuilder.Entity<RecipeStepEntity>(entity =>
            {
                entity.ToTable("RecipeStep", schema: "recipe");
                entity.HasKey(e => new { e.RecipeId, e.StepNumber }); // Composite primary key
                entity.HasOne(e => e.Recipe)
                      .WithMany(r => r.RecipeSteps)
                      .HasForeignKey(e => e.RecipeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            #endregion // End of Recipe Namespace Fluent API Configurations

            #region Nutrient Namespace Fluent API Configurations
            modelBuilder.Entity<NutrientEntity>()
                .ToTable("Nutrient", schema: "nutrient"); // Ensure table mapping

            // Configure self-referential relationship for NutrientEntity (Parent/Child nutrients)
            modelBuilder.Entity<NutrientEntity>()
                .HasOne(n => n.ParentNutrient)
                .WithMany(n => n.ChildNutrients)
                .HasForeignKey(n => n.ParentNutrientId)
                .IsRequired(false) // ParentNutrientId is nullable
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascading delete of parent if child exists

            modelBuilder.Entity<NutrientEntity>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.FdcId).IsUnique();
            });

            // Add composite unique index for IngredientNutrientEntity
            modelBuilder.Entity<IngredientNutrientEntity>(entity =>
            {
                entity.ToTable("IngredientNutrient", schema: "nutrient"); // Ensure table mapping
                entity.HasIndex(e => new { e.IngredientId, e.NutrientId }).IsUnique();
            });


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
        }
        public override int SaveChanges()
        {
            ApplyAuditInformation();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditInformation();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyAuditInformation()
        {
            long? currentPersonId = _httpContextAccessor?.HttpContext?.User?.Claims?
                .FirstOrDefault(c => c.Type == "PersonId")?.Value is string personIdStr && long.TryParse(personIdStr, out long id) ? (long?)id : 1L; // Default to 1L if not found or parsing fails



            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is IAuditableEntity && (
                    e.State == EntityState.Added ||
                    e.State == EntityState.Modified));

            foreach (var entry in entries)
            {
                var auditableEntity = (IAuditableEntity)entry.Entity;

                if (entry.State == EntityState.Added)
                {
                    auditableEntity.CreatedDate = DateTime.UtcNow;
                    auditableEntity.CreatedByPersonId = auditableEntity.CreatedByPersonId ?? currentPersonId; // Prefer explicitly set, else current
                }

                if (entry.State == EntityState.Modified)
                {
                    auditableEntity.LastModifiedDate = DateTime.UtcNow;
                    auditableEntity.LastModifiedByPersonId = auditableEntity.LastModifiedByPersonId ?? currentPersonId; // Prefer explicitly set, else current
                }
            }
        }
    }

}
