// File: Nom.Data/ApplicationDbContext.cs

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nom.Data.Audit;
using Nom.Data.Communication;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Privacy;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Shopping;
using Nom.Data.Measurement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
            _httpContextAccessor = null!;
        }

        #region Audit
        public DbSet<AuditLogEntryEntity> AuditLogEntries { get; set; } = default!;
        #endregion

        #region Communication
        public DbSet<MessageEntity> Messages { get; set; } = default!;
        public DbSet<MessageThreadEntity> MessageThreads { get; set; } = default!;
        public DbSet<MessageThreadParticipantEntity> MessageThreadParticipants { get; set; } = default!;
        #endregion

        #region Curation
        public DbSet<CurationFeedbackEntity> CurationFeedbacks { get; set; } = default!;
        #endregion

        #region Nutrient
        public DbSet<IngredientNutrientEntity> IngredientNutrients { get; set; } = default!;
        public DbSet<NutrientEntity> Nutrients { get; set; } = default!;
        public DbSet<NutrientGuidelineEntity> NutrientGuidelines { get; set; } = default!;
        #endregion

        #region Person
        public DbSet<PersonEntity> Persons { get; set; } = default!;
        public DbSet<PersonAttributeEntity> PersonAttributes { get; set; } = default!;
        public DbSet<InvitationEntity> Invitations { get; set; } = default!;
        #endregion

        #region Plan
        public DbSet<GoalEntity> Goals { get; set; } = default!;
        public DbSet<GoalItemEntity> GoalItems { get; set; } = default!;
        public DbSet<MealEntity> Meals { get; set; } = default!;
        public DbSet<PlanEntity> Plans { get; set; } = default!;
        public DbSet<PlanParticipantEntity> PlanParticipants { get; set; } = default!;
        public DbSet<RestrictionEntity> Restrictions { get; set; } = default!;

        // New Plan entities (from Mealie)
        public DbSet<HouseholdEntity> Households { get; set; } = default!;
        public DbSet<HouseholdPreferenceEntity> HouseholdPreferences { get; set; } = default!;
        public DbSet<HouseholdInviteTokenEntity> HouseholdInviteTokens { get; set; } = default!;
        public DbSet<HouseholdWebhookEntity> HouseholdWebhooks { get; set; } = default!;
        public DbSet<HouseholdEventNotifierEntity> HouseholdEventNotifiers { get; set; } = default!;
        public DbSet<HouseholdRecipeActionEntity> HouseholdRecipeActions { get; set; } = default!;
        public DbSet<HouseholdCookbookEntity> HouseholdCookbooks { get; set; } = default!;
        public DbSet<HouseholdCookbookRecipeEntity> HouseholdCookbookRecipes { get; set; } = default!;
        public DbSet<HouseholdIngredientEntity> HouseholdIngredients { get; set; } = default!;
        public DbSet<HouseholdToolEntity> HouseholdTools { get; set; } = default!;
        public DbSet<HouseholdRecipeEntity> HouseholdRecipes { get; set; } = default!;
        public DbSet<HouseholdMemberEntity> HouseholdMembers { get; set; } = default!;
        public DbSet<HouseholdGroupEntity> HouseholdGroups { get; set; } = default!;
        public DbSet<MealPlanEntity> MealPlans { get; set; } = default!;
        public DbSet<MealPlanRuleEntity> MealPlanRules { get; set; } = default!;
        #endregion

        #region Privacy
        public DbSet<UserConsentEntity> UserConsents { get; set; } = default!;
        public DbSet<DataProcessingLogEntity> DataProcessingLogs { get; set; } = default!;
        public DbSet<PrivacyRequestEntity> PrivacyRequests { get; set; } = default!;
        #endregion

        #region Recipe
        public DbSet<IngredientAliasEntity> IngredientAliases { get; set; } = default!;
        public DbSet<IngredientEntity> Ingredients { get; set; } = default!;
        public DbSet<RecipeEntity> Recipes { get; set; } = default!;
        public DbSet<RecipeIngredientEntity> RecipeIngredients { get; set; } = default!;
        public DbSet<RecipeStepEntity> RecipeSteps { get; set; } = default!;

        // New Recipe entities (from Mealie)
        public DbSet<RecipeCommentEntity> RecipeComments { get; set; } = default!;
        public DbSet<RecipeRatingEntity> RecipeRatings { get; set; } = default!;
        public DbSet<RecipeAssetEntity> RecipeAssets { get; set; } = default!;
        public DbSet<RecipeNoteEntity> RecipeNotes { get; set; } = default!;
        public DbSet<RecipeTimelineEventEntity> RecipeTimelineEvents { get; set; } = default!;
        public DbSet<RecipeShareTokenEntity> RecipeShareTokens { get; set; } = default!;
        public DbSet<RecipeTagEntity> RecipeTags { get; set; } = default!;
        public DbSet<RecipeCategoryEntity> RecipeCategories { get; set; } = default!;
        public DbSet<TagEntity> Tags { get; set; } = default!;
        public DbSet<CategoryEntity> Categories { get; set; } = default!;
        public DbSet<RecipeToolEntity> RecipeTools { get; set; } = default!;
        public DbSet<RecipeNutritionEntity> RecipeNutrition { get; set; } = default!;
        public DbSet<RecipeSettingsEntity> RecipeSettings { get; set; } = default!;
        public DbSet<IngredientExtrasEntity> IngredientExtras { get; set; } = default!;
        public DbSet<ScrapingReportEntity> ScrapingReports { get; set; } = default!;
        public DbSet<RecipeBulkOperationProgressEntity> RecipeBulkOperationProgress { get; set; } = default!;
        #endregion

        #region Reference
        public DbSet<ReferenceGroupEntity> ReferenceGroups { get; set; } = default!;
        public DbSet<ReferenceEntity> References { get; set; } = default!;

        public DbSet<MealTypeViewEntity> MealTypes { get; set; } = default!;
        public DbSet<RecipeTypeViewEntity> RecipeTypes { get; set; } = default!;
        public DbSet<ShoppingStatusTypeViewEntity> ShoppingStatusTypes { get; set; } = default!;
        public DbSet<ItemStatusTypeViewEntity> ItemStatusTypes { get; set; } = default!;
        public DbSet<RestrictionTypeViewEntity> RestrictionTypes { get; set; } = default!;
        public DbSet<GoalTypeViewEntity> GoalTypes { get; set; } = default!;
        public DbSet<NutrientTypeViewEntity> NutrientTypes { get; set; } = default!;
        public DbSet<CuisineTypeViewEntity> CuisineTypes { get; set; } = default!;
        public DbSet<PlanInvitationRoleViewEntity> PlanInvitationRoles { get; set; } = default!;
        public DbSet<PrivacyConsentTypeViewEntity> PrivacyConsentTypes { get; set; } = default!;
        public DbSet<CurationStatusTypeViewEntity> CurationStatusTypes { get; set; } = default!; // NEW
        public DbSet<FeedbackEntityTypeViewEntity> FeedbackEntityTypes { get; set; } = default!; // NEW
        public DbSet<FeedbackTypeViewEntity> FeedbackTypes { get; set; } = default!;             // NEW
        #endregion

        #region Measurement
        public DbSet<BaseMeasurementEntity> Measurements { get; set; } = default!;
        public DbSet<MeasurementCategoryEntity> MeasurementCategories { get; set; } = default!;
        public DbSet<MeasurementConversionEntity> MeasurementConversions { get; set; } = default!;
        public DbSet<IngredientMeasurementEntity> IngredientMeasurements { get; set; } = default!;
        public DbSet<NutrientMeasurementEntity> NutrientMeasurements { get; set; } = default!;
        #endregion

        #region Shopping
        public DbSet<PantryItemEntity> PantryItems { get; set; } = default!;
        public DbSet<ShoppingPreferenceEntity> ShoppingPreferences { get; set; } = default!;
        public DbSet<ShoppingTripEntity> ShoppingTrips { get; set; } = default!;

        // New Shopping entities (from Mealie)
        public DbSet<ShoppingListGroupEntity> ShoppingListGroups { get; set; } = default!;
        public DbSet<ShoppingListEntity> ShoppingLists { get; set; } = default!;
        public DbSet<ShoppingListItemEntity> ShoppingListItems { get; set; } = default!;
        public DbSet<ShoppingListLabelEntity> ShoppingListLabels { get; set; } = default!;
        public DbSet<ShoppingListCategoryEntity> ShoppingListCategories { get; set; } = default!;
        public DbSet<ShoppingListGenerationHistoryEntity> ShoppingListGenerationHistory { get; set; } = default!;
        public DbSet<ShoppingListShareEntity> ShoppingListShares { get; set; } = default!;
        #endregion

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("auth");

            #region Person Namespace Fluent API Configurations
            modelBuilder.Entity<PersonEntity>().ToTable("Person", schema: "person");
            modelBuilder.Entity<PersonAttributeEntity>().ToTable("PersonAttribute", schema: "person");

            modelBuilder.Entity<PersonEntity>()
                .HasIndex(p => p.UserId)
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL");

            modelBuilder.Entity<PersonEntity>()
                .HasMany(p => p.PlanParticipations)
                .WithOne(pp => pp.Person)
                .HasForeignKey(pp => pp.PersonId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<InvitationEntity>()
                .HasIndex(i => i.Code)
                .IsUnique();

            modelBuilder.Entity<InvitationEntity>()
                .HasOne(i => i.Inviter)
                .WithMany()
                .HasForeignKey(i => i.InviterPersonId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<InvitationEntity>()
                .HasOne(i => i.Invitee)
                .WithMany()
                .HasForeignKey(i => i.InviteePersonId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<InvitationEntity>()
                .HasOne(i => i.Plan)
                .WithMany()
                .HasForeignKey(i => i.PlanId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            #endregion

            #region Audit Namespace Fluent API Configurations
            modelBuilder.Entity<AuditLogEntryEntity>().ToTable("AuditLogEntry", schema: "audit");

            modelBuilder.Entity<AuditLogEntryEntity>()
                .HasOne(ale => ale.ChangedByPerson)
                .WithMany()
                .HasForeignKey(ale => ale.ChangedByPersonId)
                .OnDelete(DeleteBehavior.Restrict);
            #endregion

            #region Privacy Namespace Fluent API Configurations
            modelBuilder.Entity<UserConsentEntity>().ToTable("UserConsent", schema: "privacy");
            modelBuilder.Entity<DataProcessingLogEntity>().ToTable("DataProcessingLog", schema: "privacy");
            modelBuilder.Entity<PrivacyRequestEntity>().ToTable("PrivacyRequest", schema: "privacy");
            #endregion

            #region Reference Namespace Fluent API Configurations
            modelBuilder.Entity<ReferenceGroupEntity>().ToTable("Group", schema: "reference");
            modelBuilder.Entity<ReferenceEntity>().ToTable("Reference", schema: "reference");

            modelBuilder.Entity<ReferenceEntity>()
                .HasMany(r => r.Groups)
                .WithMany(g => g.References)
                .UsingEntity<Dictionary<string, object>>(
                    "ReferenceIndex",
                    j => j.HasOne<ReferenceGroupEntity>().WithMany().HasForeignKey("GroupId").HasConstraintName("FK_ReferenceIndex_ReferenceGroupEntity_GroupId"),
                    j => j.HasOne<ReferenceEntity>().WithMany().HasForeignKey("ReferenceId").HasConstraintName("FK_ReferenceIndex_ReferenceEntity_ReferenceId"),
                    j => { j.ToTable("ReferenceIndex", "reference"); j.HasKey("ReferenceId", "GroupId"); });

            modelBuilder.Entity<GroupedReferenceViewEntity>()
                .ToView("ReferenceGroupView", "reference")
                .HasNoKey()
                .HasDiscriminator<long>(g => g.GroupId)

                .HasValue<MealTypeViewEntity>((long)ReferenceDiscriminatorEnum.MealType)
                .HasValue<RecipeTypeViewEntity>((long)ReferenceDiscriminatorEnum.RecipeType)
                .HasValue<ShoppingStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingStatusType)
                .HasValue<ItemStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.ItemStatusType)
                .HasValue<RestrictionTypeViewEntity>((long)ReferenceDiscriminatorEnum.RestrictionType)
                .HasValue<GoalTypeViewEntity>((long)ReferenceDiscriminatorEnum.GoalType)
                .HasValue<NutrientTypeViewEntity>((long)ReferenceDiscriminatorEnum.NutrientType)
                .HasValue<CuisineTypeViewEntity>((long)ReferenceDiscriminatorEnum.CuisineType)
                .HasValue<PlanInvitationRoleViewEntity>((long)ReferenceDiscriminatorEnum.PlanInvitationRole)
                .HasValue<PrivacyConsentTypeViewEntity>((long)ReferenceDiscriminatorEnum.PrivacyConsentType)
                .HasValue<CurationStatusTypeViewEntity>((long)ReferenceDiscriminatorEnum.CurationStatusType)       // NEW
                .HasValue<FeedbackEntityTypeViewEntity>((long)ReferenceDiscriminatorEnum.FeedbackEntityType) // NEW
                .HasValue<FeedbackTypeViewEntity>((long)ReferenceDiscriminatorEnum.FeedbackType)             // NEW
                .HasValue<ShoppingPriorityTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingPriorityType)
                .HasValue<ShoppingCategoryTypeViewEntity>((long)ReferenceDiscriminatorEnum.ShoppingCategoryType)
                .HasValue<RecipeDifficultyTypeViewEntity>((long)ReferenceDiscriminatorEnum.RecipeDifficultyType)
                .HasValue<PersonActivityLevelTypeViewEntity>((long)ReferenceDiscriminatorEnum.PersonActivityLevelType)
                .HasValue<DayOfWeekTypeViewEntity>((long)ReferenceDiscriminatorEnum.DayOfWeekType);
            #endregion

            #region Plan Namespace Fluent API Configurations
            modelBuilder.Entity<PlanEntity>().ToTable("Plan", schema: "plan");

            modelBuilder.Entity<PlanEntity>()
                .HasIndex(p => p.InvitationCode)
                .IsUnique()
                .HasFilter("\"InvitationCode\" IS NOT NULL");

            modelBuilder.Entity<PlanEntity>()
                .HasOne(p => p.CurationStatus)
                .WithMany()
                .HasForeignKey(p => p.CurationStatusId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PlanEntity>()
                .HasOne(p => p.Author)
                .WithMany()
                .HasForeignKey(p => p.AuthorId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PlanEntity>()
                .HasOne(p => p.ParentPlan)
                .WithMany()
                .HasForeignKey(p => p.ParentPlanId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PlanEntity>()
                .HasMany(p => p.Restrictions)
                .WithOne(r => r.Plan)
                .HasForeignKey(r => r.PlanId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlanEntity>()
                .HasMany(p => p.Participants)
                .WithOne(pp => pp.Plan)
                .HasForeignKey(pp => pp.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RestrictionEntity>().ToTable("Restriction", schema: "plan");

            modelBuilder.Entity<RestrictionEntity>()
                .ToTable(t => t.HasCheckConstraint("CHK_Restriction_PersonOrPlan", "\"PersonId\" IS NOT NULL OR \"PlanId\" IS NOT NULL"));

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.Person)
                .WithMany()
                .HasForeignKey(r => r.PersonId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RestrictionEntity>()
                .HasOne(r => r.RestrictionType)
                .WithMany()
                .HasForeignKey(r => r.RestrictionTypeId)
                .IsRequired(false)
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

            modelBuilder.Entity<PlanParticipantEntity>().ToTable("PlanParticipant", schema: "plan");

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasKey(pp => new { pp.PlanId, pp.PersonId });

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.Plan)
                .WithMany(p => p.Participants)
                .HasForeignKey(pp => pp.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.Person)
                .WithMany(p => p.PlanParticipations)
                .HasForeignKey(pp => pp.PersonId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlanParticipantEntity>()
                .HasOne(pp => pp.Role)
                .WithMany()
                .HasForeignKey(pp => pp.RoleRefId)
                .OnDelete(DeleteBehavior.Restrict);
            #endregion

            #region Recipe Namespace Fluent API Configurations
            modelBuilder.Entity<RecipeEntity>(entity =>
            {
                entity.ToTable("Recipe", schema: "recipe");

                entity.HasOne(r => r.ServingQuantityMeasurement)
                      .WithMany()
                      .HasForeignKey(r => r.ServingQuantityMeasurementId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Author)
                      .WithMany()
                      .HasForeignKey(r => r.AuthorId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.ParentRecipe)
                      .WithMany()
                      .HasForeignKey(r => r.ParentRecipeId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(r => r.CurationStatus)
                      .WithMany()
                      .HasForeignKey(r => r.CurationStatusId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(r => r.Meals)
                      .WithMany(m => m.Recipes)
                      .UsingEntity<Dictionary<string, object>>(
                          "MealRecipeIndex",
                          j => j.HasOne<MealEntity>().WithMany().HasForeignKey("MealId").HasConstraintName("FK_MealRecipeIndex_MealEntity_MealId"),
                          j => j.HasOne<RecipeEntity>().WithMany().HasForeignKey("RecipeId").HasConstraintName("FK_MealRecipeIndex_RecipeEntity_RecipeId"),
                          j => { j.ToTable("meal_recipe_index", "plan"); j.HasKey("MealId", "RecipeId"); });

                entity.HasMany(r => r.RecipeTypes)
                      .WithMany()
                      .UsingEntity<Dictionary<string, object>>(
                          "RecipeTypeIndex",
                          j => j.HasOne<ReferenceEntity>().WithMany().HasForeignKey("RecipeTypeId").HasConstraintName("FK_RecipeTypeIndex_ReferenceEntity_RecipeTypeId"),
                          j => j.HasOne<RecipeEntity>().WithMany().HasForeignKey("RecipeId").HasConstraintName("FK_RecipeTypeIndex_RecipeEntity_RecipeId"),
                          j => { j.ToTable("recipe_type_index", "recipe"); j.HasKey("RecipeId", "RecipeTypeId"); });
            });

            modelBuilder.Entity<IngredientEntity>(entity =>
            {
                entity.ToTable("Ingredient", schema: "recipe");
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.FdcId).IsUnique().HasFilter("\"FdcId\" IS NOT NULL");

                entity.HasOne(i => i.Author)
                      .WithMany()
                      .HasForeignKey(i => i.AuthorId)
                      .IsRequired(false)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.CurationStatus)
                      .WithMany()
                      .HasForeignKey(i => i.CurationStatusId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<IngredientAliasEntity>(entity =>
            {
                entity.ToTable("IngredientAlias", schema: "recipe");
                entity.HasKey(e => new { e.IngredientId, e.AliasName });
                entity.HasOne(e => e.Ingredient).WithMany(i => i.Aliases).HasForeignKey(e => e.IngredientId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RecipeIngredientEntity>(entity =>
            {
                entity.ToTable("RecipeIngredient", schema: "recipe");
                entity.HasKey(e => new { e.RecipeId, e.IngredientId });
                entity.HasOne(e => e.Recipe).WithMany(r => r.RecipeIngredients).HasForeignKey(e => e.RecipeId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Ingredient).WithMany().HasForeignKey(e => e.IngredientId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Measurement).WithMany().HasForeignKey(e => e.MeasurementId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<RecipeStepEntity>(entity =>
            {
                entity.ToTable("RecipeStep", schema: "recipe");
                entity.HasKey(e => new { e.RecipeId, e.StepNumber });
                entity.HasOne(e => e.Recipe).WithMany(r => r.RecipeSteps).HasForeignKey(e => e.RecipeId).OnDelete(DeleteBehavior.Cascade);
            });
            #endregion

            #region Nutrient Namespace Fluent API Configurations
            modelBuilder.Entity<NutrientEntity>().ToTable("Nutrient", schema: "nutrient");

            modelBuilder.Entity<NutrientEntity>()
                .HasOne(n => n.ParentNutrient)
                .WithMany(n => n.ChildNutrients)
                .HasForeignKey(n => n.ParentNutrientId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<NutrientEntity>(entity =>
            {
                entity.HasIndex(e => new { e.Name, e.DefaultMeasurementId }).IsUnique().HasFilter("\"FdcId\" IS NOT NULL");
                entity.HasIndex(e => e.FdcId).IsUnique().HasFilter("\"FdcId\" IS NOT NULL");
            });

            modelBuilder.Entity<IngredientNutrientEntity>(entity =>
            {
                entity.ToTable("IngredientNutrient", schema: "nutrient");
                entity.HasIndex(e => new { e.IngredientId, e.NutrientId }).IsUnique();
            });
            #endregion

            #region Shopping Namespace Fluent API Configurations
            modelBuilder.Entity<ShoppingTripEntity>().ToTable("ShoppingTrip", schema: "shopping");
            modelBuilder.Entity<PantryItemEntity>().ToTable("PantryItem", schema: "shopping");
            modelBuilder.Entity<ShoppingPreferenceEntity>().ToTable("ShoppingPreference", schema: "shopping");

            modelBuilder.Entity<ShoppingTripEntity>()
                .HasMany(st => st.Meals)
                .WithMany(m => m.ShoppingTrips)
                .UsingEntity<Dictionary<string, object>>(
                    "ShoppingTripMealIndex",
                    j => j.HasOne<MealEntity>().WithMany().HasForeignKey("MealId").HasConstraintName("FK_ShoppingTripMealIndex_MealEntity_MealId"),
                    j => j.HasOne<ShoppingTripEntity>().WithMany().HasForeignKey("ShoppingTripId").HasConstraintName("FK_ShoppingTripMealIndex_ShoppingTripEntity_ShoppingTripId"),
                    j => { j.ToTable("shopping_trip_meal_index", "shopping"); j.HasKey("ShoppingTripId", "MealId"); });
            #endregion

            #region Curation Namespace Fluent API Configurations
            modelBuilder.Entity<CurationFeedbackEntity>(entity =>
            {
                entity.ToTable("CurationFeedback", schema: "curation");

                entity.HasOne(cf => cf.Admin)
                      .WithMany()
                      .HasForeignKey(cf => cf.AdminId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(cf => cf.EntityType)
                      .WithMany()
                      .HasForeignKey(cf => cf.EntityTypeId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(cf => cf.FeedbackType)
                      .WithMany()
                      .HasForeignKey(cf => cf.FeedbackTypeId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Restrict);
            });
            #endregion

            #region Communication Namespace Fluent API Configurations
            modelBuilder.Entity<MessageThreadEntity>(entity =>
            {
                entity.ToTable("MessageThread", schema: "communication");

                entity.HasOne(mt => mt.Recipe).WithMany().HasForeignKey(mt => mt.RecipeId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(mt => mt.Ingredient).WithMany().HasForeignKey(mt => mt.IngredientId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(mt => mt.Plan).WithMany().HasForeignKey(mt => mt.PlanId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<MessageThreadParticipantEntity>(entity =>
            {
                entity.ToTable("MessageThreadParticipant", schema: "communication");
                entity.HasKey(p => new { p.MessageThreadId, p.PersonId });

                entity.HasOne(p => p.MessageThread).WithMany(t => t.Participants).HasForeignKey(p => p.MessageThreadId);
                entity.HasOne(p => p.Person).WithMany().HasForeignKey(p => p.PersonId);
            });

            modelBuilder.Entity<MessageEntity>(entity =>
            {
                entity.ToTable("Message", schema: "communication");

                entity.HasOne(m => m.MessageThread).WithMany(t => t.Messages).HasForeignKey(m => m.MessageThreadId);
                entity.HasOne(m => m.SenderPerson).WithMany().HasForeignKey(m => m.SenderPersonId);
            });
            #endregion

            #region Measurement Namespace Fluent API Configurations
            modelBuilder.Entity<MeasurementEntity>(entity =>
            {
                entity.ToTable("Measurement", schema: "measurement");
                entity.HasDiscriminator<string>("MeasurementType")
                    .HasValue<BaseMeasurementEntity>("Base")
                    .HasValue<IngredientMeasurementEntity>("Ingredient")
                    .HasValue<NutrientMeasurementEntity>("Nutrient");
            });

            modelBuilder.Entity<MeasurementCategoryEntity>(entity =>
            {
                entity.ToTable("MeasurementCategory", schema: "measurement");
                
                entity.HasOne(mc => mc.BaseUnit)
                    .WithMany()
                    .HasForeignKey(mc => mc.BaseUnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<MeasurementConversionEntity>(entity =>
            {
                entity.ToTable("MeasurementConversion", schema: "measurement");
                
                entity.HasOne(mc => mc.FromMeasurement)
                    .WithMany()
                    .HasForeignKey(mc => mc.FromMeasurementId)
                    .OnDelete(DeleteBehavior.Restrict);
                
                entity.HasOne(mc => mc.ToMeasurement)
                    .WithMany()
                    .HasForeignKey(mc => mc.ToMeasurementId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<IngredientMeasurementEntity>(entity =>
            {
                entity.ToTable("Measurement", schema: "measurement");
                
                entity.HasOne(im => im.Ingredient)
                    .WithMany()
                    .HasForeignKey(im => im.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<NutrientMeasurementEntity>(entity =>
            {
                entity.ToTable("Measurement", schema: "measurement");
                
                entity.HasOne(nm => nm.Nutrient)
                    .WithMany()
                    .HasForeignKey(nm => nm.NutrientId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            #endregion
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
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext == null) return;

            var personIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "PersonId")?.Value;
            long? currentPersonId = long.TryParse(personIdClaim, out long id) ? (long?)id : null;

            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is BaseEntity && (
                    e.State == EntityState.Added ||
                    e.State == EntityState.Modified));

            foreach (var entry in entries)
            {
                var baseEntity = (BaseEntity)entry.Entity;
                var now = DateTime.UtcNow;

                if (entry.State == EntityState.Added)
                {
                    baseEntity.CreatedDate = now;
                    baseEntity.CreatedByPersonId = baseEntity.CreatedByPersonId ?? currentPersonId;
                }

                if (entry.State == EntityState.Modified)
                {
                    baseEntity.LastModifiedDate = now;
                    baseEntity.LastModifiedByPersonId = baseEntity.LastModifiedByPersonId ?? currentPersonId;
                }
            }
        }
    }
}