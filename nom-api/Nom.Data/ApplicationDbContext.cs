using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

using Nom.Data.Reference;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Nutrient;
using Nom.Data.Shopping;
using Nom.Data.Person;
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitly map Identity tables to the 'auth' schema
            modelBuilder.HasDefaultSchema("auth");

            // Configure PersonEntity to be in 'person' schema
            modelBuilder.Entity<PersonEntity>().ToTable("Person", schema: "person");
            modelBuilder.Entity<PersonAttributeEntity>().ToTable("PersonAttribute", schema: "person");

            // --- REMOVED: Explicit Fluent API for Person-Recipe, Person-ShoppingPreference, Person-ShoppingTrip ---
            // These relationships are now handled by [InverseProperty] and [ForeignKey] attributes on the entities.

            #region Fluent API Configurations by Namespace

            #region Reference Namespace Fluent API Configurations

            // Reference-Group Implicit Many-to-Many with explicit join table name "ReferenceIndex"
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
                .WithMany(p => p.PlansParticipatingIn) // This still explicitly links to PersonEntity's collection
                .UsingEntity<Dictionary<string, object>>(
                    "PlanPersonIndex",
                    j => j.HasOne<Person.PersonEntity>()
                            .WithMany() // Person has many PlansParticipatingIn (M-M)
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
                .WithMany(p => p.PlansAdministering) // This still explicitly links to PersonEntity's collection
                .UsingEntity<Dictionary<string, object>>(
                    "PlanPersonAdministratorIndex",
                    j => j.HasOne<Person.PersonEntity>()
                            .WithMany() // Person has many PlansAdministering (M-M)
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

            #endregion // End of Fluent API Configurations by Namespace region
        }
    }
}