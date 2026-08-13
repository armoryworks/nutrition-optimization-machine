using System.Linq;

using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Extensions
{
    /// <summary>
    /// The single visibility rule for recipes (household-policies design doc §4).
    /// Every read surface — search, browse, detail, images, assets, share
    /// tokens, export, shuffle pools — composes this instead of re-deriving
    /// curation checks. The enforcement checklist in the design doc is
    /// normative: if a query returns recipes to a requester, it goes through
    /// here.
    /// </summary>
    public static class RecipeVisibilityExtensions
    {
        /// <summary>
        /// Filters to recipes the given person may see:
        ///  - Public + Approved curation (the public pool; anonymous callers get only this),
        ///  - their own recipes (any visibility, any curation),
        ///  - Household-visibility recipes authored within their households,
        ///  - Audience-visibility recipes scoped to an audience containing one of their households,
        ///  - plus departure-grace (grandfathering): audience recipes the person has
        ///    cooked (completed plan or Made timeline event) or favorited (rating >= 4),
        ///    which stay readable after the household leaves the audience.
        /// </summary>
        public static IQueryable<RecipeEntity> VisibleTo(
            this IQueryable<RecipeEntity> recipes,
            ApplicationDbContext context,
            long? personId)
        {
            // Anonymous: public + approved only.
            if (!personId.HasValue)
            {
                return recipes.Where(r =>
                    r.Visibility == RecipeVisibilityEnum.Public
                    && r.CurationStatus!.Name == "Approved");
            }

            var pid = personId.Value;
            var householdIds = context.HouseholdMembers
                .Where(hm => hm.PersonId == pid && hm.IsActive)
                .Select(hm => hm.HouseholdId);

            return recipes.Where(r =>
                // The public pool.
                (r.Visibility == RecipeVisibilityEnum.Public && r.CurationStatus!.Name == "Approved")
                // Your own recipes.
                || r.AuthorId == pid
                // Household-visibility: authored by a member of one of your households.
                || (r.Visibility == RecipeVisibilityEnum.Household
                    && context.HouseholdMembers.Any(hm =>
                        hm.PersonId == r.AuthorId && hm.IsActive && householdIds.Contains(hm.HouseholdId)))
                // Audience-visibility: scoped to an audience containing one of your households.
                || (r.Visibility == RecipeVisibilityEnum.Audience
                    && r.Audiences.Any(ra => ra.Audience!.Members.Any(am => householdIds.Contains(am.HouseholdId))))
                // Departure grace (grandfathering): audience recipes you cooked or favorited.
                || (r.Visibility == RecipeVisibilityEnum.Audience
                    && (context.MealPlans.Any(mp => mp.RecipeId == r.Id
                            && mp.CompletedDate != null && householdIds.Contains(mp.HouseholdId))
                        || context.RecipeTimelineEvents.Any(te => te.RecipeId == r.Id
                            && te.ActorId == pid && te.EventTypeId == 10006 /* Made */)
                        || context.RecipeRatings.Any(rr => rr.RecipeId == r.Id
                            && rr.RaterId == pid && rr.Rating >= 4))));
        }
    }
}
