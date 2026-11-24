// File: Nom.Data/ApplicationDbContext.cs

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
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
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>, IDataProtectionKeyContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private bool _hardDelete;

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

        /// <summary>ASP.NET Data Protection key ring — persisted so bearer tokens survive container recreates.</summary>
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = default!;

        #region Communication
        public DbSet<MessageEntity> Messages { get; set; } = default!;
        public DbSet<MessageThreadEntity> MessageThreads { get; set; } = default!;
        public DbSet<MessageThreadParticipantEntity> MessageThreadParticipants { get; set; } = default!;
        #endregion

        #region Curation
        public DbSet<CurationFeedbackEntity> CurationFeedbacks { get; set; } = default!;
        public DbSet<FoodCatalogProposalEntity> FoodCatalogProposals { get; set; } = default!;
        public DbSet<LaneStateEntity> LaneStates { get; set; } = default!;
        public DbSet<LaneDecisionEntity> LaneDecisions { get; set; } = default!;
        #endregion

        #region Nutrient
        public DbSet<IngredientNutrientEntity> IngredientNutrients { get; set; } = default!;
        public DbSet<IngredientContainerSizeEntity> IngredientContainerSizes { get; set; } = default!;
        public DbSet<NutrientEntity> Nutrients { get; set; } = default!;
        public DbSet<NutrientGuidelineEntity> NutrientGuidelines { get; set; } = default!;
        #endregion

        #region Person
        public DbSet<PersonEntity> Persons { get; set; } = default!;
        public DbSet<PersonAttributeEntity> PersonAttributes { get; set; } = default!;
        public DbSet<InvitationEntity> Invitations { get; set; } = default!;
        public DbSet<ApiTokenEntity> ApiTokens { get; set; } = default!;
        #endregion

        #region Plan
        public DbSet<GoalEntity> Goals { get; set; } = default!;
        public DbSet<GoalItemEntity> GoalItems { get; set; } = default!;
        public DbSet<MacroGoalEntity> MacroGoals { get; set; } = default!;
        public DbSet<FoodGroupRuleEntity> FoodGroupRules { get; set; } = default!;
        public DbSet<MealEntity> Meals { get; set; } = default!;
        public DbSet<PlanEntity> Plans { get; set; } = default!;
        public DbSet<PlanParticipantEntity> PlanParticipants { get; set; } = default!;
        public DbSet<RestrictionEntity> Restrictions { get; set; } = default!;
        public DbSet<DietGateBuildEntity> DietGateBuilds { get; set; } = default!;
        public DbSet<DietGateSetEntity> DietGateSets { get; set; } = default!;
        public DbSet<DietFoodFactEntity> DietFoodFacts { get; set; } = default!;
        public DbSet<DietFoodStateEntity> DietFoodStates { get; set; } = default!;
        public DbSet<DietRecipeFlagEntity> DietRecipeFlags { get; set; } = default!;
        public DbSet<DietRecipeStateEntity> DietRecipeStates { get; set; } = default!;
        public DbSet<DietGateWriteEntity> DietGateWrites { get; set; } = default!;

        // New Plan entities (from Mealie)
        public DbSet<HouseholdEntity> Households { get; set; } = default!;
        public DbSet<HouseholdPreferenceEntity> HouseholdPreferences { get; set; } = default!;
        public DbSet<HouseholdInviteTokenEntity> HouseholdInviteTokens { get; set; } = default!;
        public DbSet<MemberPolicyEntity> MemberPolicies { get; set; } = default!;
        public DbSet<EnrollmentEventEntity> EnrollmentEvents { get; set; } = default!;
        public DbSet<PolicyContractVersionEntity> PolicyContractVersions { get; set; } = default!;
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

        #region Platform
        public DbSet<Nom.Data.Platform.PlatformFeatureEntity> PlatformFeatures { get; set; } = default!;
        #endregion

        #region Privacy
        public DbSet<UserConsentEntity> UserConsents { get; set; } = default!;
        public DbSet<DataProcessingLogEntity> DataProcessingLogs { get; set; } = default!;
        public DbSet<PrivacyRequestEntity> PrivacyRequests { get; set; } = default!;
        #endregion

        #region Recipe
        public DbSet<IngredientAliasEntity> IngredientAliases { get; set; } = default!;
        public DbSet<IngredientWordingEntity> IngredientWordings { get; set; } = default!;
        public DbSet<IngredientWordingRejectionEntity> IngredientWordingRejections { get; set; } = default!;
        public DbSet<WordingLexiconEntity> WordingLexicon { get; set; } = default!;
        public DbSet<IngredientEntity> Ingredients { get; set; } = default!;
        public DbSet<RecipeEntity> Recipes { get; set; } = default!;
        public DbSet<DishGroupEntity> DishGroups { get; set; } = default!;
        public DbSet<AudienceEntity> Audiences { get; set; } = default!;
        public DbSet<AudienceMemberEntity> AudienceMembers { get; set; } = default!;
        public DbSet<RecipeAudienceEntity> RecipeAudiences { get; set; } = default!;
        public DbSet<RecipeIngredientEntity> RecipeIngredients { get; set; } = default!;
        public DbSet<RecipeStepEntity> RecipeSteps { get; set; } = default!;

        // New Recipe entities (from Mealie)
        public DbSet<RecipeCommentEntity> RecipeComments { get; set; } = default!;
        public DbSet<RecipeRatingEntity> RecipeRatings { get; set; } = default!;
        public DbSet<RecipeAssetEntity> RecipeAssets { get; set; } = default!;
        public DbSet<RecipeImageCandidateEntity> RecipeImageCandidates { get; set; } = default!;
        public DbSet<RecipeNoteEntity> RecipeNotes { get; set; } = default!;
        public DbSet<RecipeTimelineEventEntity> RecipeTimelineEvents { get; set; } = default!;
        public DbSet<RecipeShareTokenEntity> RecipeShareTokens { get; set; } = default!;
        public DbSet<RecipeTagEntity> RecipeTags { get; set; } = default!;
        public DbSet<RecipeCategoryEntity> RecipeCategories { get; set; } = default!;
        public DbSet<TagEntity> Tags { get; set; } = default!;
        public DbSet<CategoryEntity> Categories { get; set; } = default!;
        public DbSet<RecipeToolEntity> RecipeTools { get; set; } = default!;
        public DbSet<KitchenToolSuggestionEntity> KitchenToolSuggestions { get; set; } = default!;
        public DbSet<KitchenToolSuggestionRecipeEntity> KitchenToolSuggestionRecipes { get; set; } = default!;
        public DbSet<RecipeNutritionEntity> RecipeNutrition { get; set; } = default!;
        public DbSet<RecipeNutritionCheckEntity> RecipeNutritionChecks { get; set; } = default!;
        public DbSet<RecipeSettingsEntity> RecipeSettings { get; set; } = default!;
        public DbSet<IngredientExtrasEntity> IngredientExtras { get; set; } = default!;
        public DbSet<ScrapingReportEntity> ScrapingReports { get; set; } = default!;
        public DbSet<ScrapingSourceEntity> ScrapingSources { get; set; } = default!;
        public DbSet<SourceCandidateEntity> SourceCandidates { get; set; } = default!;
        public DbSet<SourceCheckEntity> SourceChecks { get; set; } = default!;
        public DbSet<SourceDiscoveryRunEntity> SourceDiscoveryRuns { get; set; } = default!;
        public DbSet<SourceRightsRecordEntity> SourceRightsRecords { get; set; } = default!;
        public DbSet<SourceRefusingHostEntity> SourceRefusingHosts { get; set; } = default!;
        public DbSet<RecipeRemovalEntity> RecipeRemovals { get; set; } = default!;
        public DbSet<RecipeRemovalRunEntity> RecipeRemovalRuns { get; set; } = default!;
        public DbSet<ScrapedDocumentEntity> ScrapedDocuments { get; set; } = default!;
        public DbSet<ProseStockPhraseEntity> ProseStockPhrases { get; set; } = default!;
        public DbSet<RecipeReprocessJournalEntity> RecipeReprocessJournals { get; set; } = default!;
        public DbSet<ReprocessCandidateEntity> ReprocessCandidates { get; set; } = default!;
        public DbSet<LineGuardHoldEntity> LineGuardHolds { get; set; } = default!;
        public DbSet<RecipeSourceEraEntity> RecipeSourceEras { get; set; } = default!;
        public DbSet<RecipeSubstitutionEntity> RecipeSubstitutions { get; set; } = default!;
        public DbSet<RecipeSubstitutionStepEffectEntity> RecipeSubstitutionStepEffects { get; set; } = default!;
        public DbSet<RecipeAugmentationEntity> RecipeAugmentations { get; set; } = default!;
        public DbSet<RecipeBulkOperationProgressEntity> RecipeBulkOperationProgress { get; set; } = default!;
        public DbSet<IngredientVerificationEntity> IngredientVerifications { get; set; } = default!;
        public DbSet<IngredientContentsEntity> IngredientContents { get; set; } = default!;
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
        public DbSet<FoodGroupTypeViewEntity> FoodGroupTypes { get; set; } = default!;
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
        public DbSet<GroceryConnectionEntity> GroceryConnections { get; set; } = default!;
        public DbSet<ShoppingListLabelEntity> ShoppingListLabels { get; set; } = default!;
        public DbSet<ShoppingListCategoryEntity> ShoppingListCategories { get; set; } = default!;
        public DbSet<ShoppingListGenerationHistoryEntity> ShoppingListGenerationHistory { get; set; } = default!;
        public DbSet<ShoppingListShareEntity> ShoppingListShares { get; set; } = default!;
        public DbSet<HouseholdShoppingItemEntity> HouseholdShoppingItems { get; set; } = default!;
        public DbSet<HouseholdShoppingCheckEntity> HouseholdShoppingChecks { get; set; } = default!;
        #endregion

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("auth");
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // OpenIddict's application/authorization/scope/token stores. NOM is the
            // OIDC authority for its own apps (Brigade's console today), so these
            // tables live beside Identity's in the auth schema.
            modelBuilder.UseOpenIddict();

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

        /// <summary>
        /// Deletes the rows outright, with no soft delete, and saves: for an undo that takes back
        /// rows its own run created, so no deleted copy is left behind to differ from the state
        /// before the run.
        /// </summary>
        public async Task<int> HardDeleteAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : BaseEntity
        {
            _hardDelete = true;
            try
            {
                RemoveRange(entities);
                return await SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _hardDelete = false;
            }
        }

        /// <summary>
        /// Saves with every row marked deleted deleted outright, with no soft delete: for a purge that must leave no copy of
        /// what it removes.
        /// </summary>
        public async Task<int> SaveHardDeletesAsync(CancellationToken cancellationToken = default)
        {
            _hardDelete = true;
            try
            {
                return await SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _hardDelete = false;
            }
        }

        /// <summary>
        /// Saves without stamping the modified rows' LastModifiedDate and LastModifiedByPersonId: for a soft delete and its undo
        /// that must leave each row as it was, so no lane reads the admin who ran them as the rows' editor.
        /// </summary>
        public async Task<int> SaveKeepingModifiedAsync(CancellationToken cancellationToken = default)
        {
            _keepModified = true;
            try
            {
                return await SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _keepModified = false;
            }
        }

        private bool _keepModified;

        /// <summary>
        /// Holds every save until the returned scope is disposed: a save then writes nothing and leaves its changes in the change
        /// tracker, so a dry run can run the code that writes and read there what it would have written. Writes that do not go
        /// through a save (raw SQL, bulk updates) are not held; a dry run that may reach one runs in a transaction it rolls back.
        /// </summary>
        public IDisposable HoldWrites()
        {
            _writesHeld = true;
            return new HeldWrites(this);
        }

        /// <summary>Whether saves are held (<see cref="HoldWrites"/>).</summary>
        public bool WritesHeld => _writesHeld;

        private bool _writesHeld;

        private sealed class HeldWrites : IDisposable
        {
            private readonly ApplicationDbContext _db;

            public HeldWrites(ApplicationDbContext db) => _db = db;

            public void Dispose() => _db._writesHeld = false;
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
            _writesHeld ? 0 : base.SaveChanges(acceptAllChangesOnSuccess);

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
            _writesHeld ? Task.FromResult(0) : base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

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

                if (entry.State == EntityState.Modified && !_keepModified)
                {
                    baseEntity.LastModifiedDate = now;
                    baseEntity.LastModifiedByPersonId = baseEntity.LastModifiedByPersonId ?? currentPersonId;
                }

                if (entry.State == EntityState.Deleted && !_hardDelete)
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