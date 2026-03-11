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
using System.Linq;
using System.Linq.Expressions;
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
        public DbSet<MealPlanExclusionEntity> MealPlanExclusions { get; set; } = default!;
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
        public DbSet<RetailPackagingEntity> RetailPackagings { get; set; } = default!;

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
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // Apply soft-delete configuration to all BaseEntity types:
            // - Default IsDeleted to false so seed SQL INSERTs don't need to specify it
            // - Query filter only on root entity types (EF Core TPH restriction)
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(nameof(BaseEntity.IsDeleted))
                        .HasDefaultValue(false);

                    // For composite-PK entities, the inherited Id column isn't the PK
                    // and won't get identity generation — give it a default so seed SQL works
                    var pk = entityType.FindPrimaryKey();
                    if (pk != null && !pk.Properties.Any(p => p.Name == nameof(BaseEntity.Id)))
                    {
                        modelBuilder.Entity(entityType.ClrType)
                            .Property(nameof(BaseEntity.Id))
                            .HasDefaultValue(0L);
                    }

                    if (entityType.BaseType == null)
                    {
                        var parameter = Expression.Parameter(entityType.ClrType, "e");
                        var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                        var filter = Expression.Lambda(Expression.Not(property), parameter);
                        modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
                    }
                }
            }
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
                    e.State == EntityState.Modified ||
                    e.State == EntityState.Deleted));

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

                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    baseEntity.IsDeleted = true;
                    baseEntity.DeletedAt = now;
                    baseEntity.DeletedByPersonId = currentPersonId;
                }
            }
        }
    }
}