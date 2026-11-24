using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Shopping;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.MealPlan;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The removal of recipes taken from forbidden sources (owner's answer of 2026-11-24, point 4). Stage 1, retire: only a host
    /// whose every web row is forbidden in effect qualifies (a written permission, a lifted class, permission-needed and
    /// facts-only sources are untouched); the dry run equals the apply; the recipe leaves every surface (search, popular,
    /// recent, random, suggestions, the detail and a share page, a plan's picker, cookbooks, the curation queue, the image
    /// finder, the bulk export, the prose stock index, the lanes that read lines, steps and documents, the stored diet gate)
    /// while a household keeps its meal plan entries, favourites, cookbook entries and shopping items with the title, the
    /// source link and a plain note, and nothing of the source's wording; the run resumes under its stamp after a cut-off
    /// and is undone by that stamp. Stage 2, purge: refused without a backup, irreversible, and no column of a purged recipe
    /// holds source text afterwards. A removed address is refused by URL import and by the staged import the harvester uses.
    /// </summary>
    public class RecipeRemovalTests
    {
        private const long Admin = 7, Member = 70, HouseholdId = 7;
        private const string Marker = "PUBLISHER-WORDS";
        private static readonly DateTime T0 = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static RecipeRemovalService Removal(ApplicationDbContext db, int batch = RecipeRemovalService.BatchSize) =>
            new(db, NullLogger<RecipeRemovalService>.Instance) { Now = () => T0, Batch = batch };

        private sealed record World(long Stew, long Cake, long Old, long Lifted, long Needs, long Facts, long Mixed, long Open, long ForbiddenSource, long NeedsSource, long CatalogFood, long LineFood);

        private static async Task<World> SeedAsync(ApplicationDbContext db)
        {
            db.Persons.AddRange(new Nom.Data.Person.PersonEntity { Id = Admin, Name = "Admin", UserId = "u7" }, new Nom.Data.Person.PersonEntity { Id = Member, Name = "Member", UserId = "u70" });
            db.Set<Nom.Data.Reference.ReferenceEntity>().AddRange(
                new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" },
                new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" },
                new Nom.Data.Reference.ReferenceEntity { Id = 1102, Name = "Dinner" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = Member, IsActive = true });
            var catalog = new IngredientEntity { Name = "Beef Stew Meat", CurationStatusId = (long)CurationStatusEnum.Curated };
            var fromLine = new IngredientEntity { Name = $"secret spice blend, {Marker} to taste", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(catalog, fromLine);
            ScrapingSourceEntity Source(string domain, string? rights, bool permission = false) => new()
            {
                Domain = domain, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0, RightsClass = rights,
                RightsQuote = rights == SourceRightsRule.Forbidden ? "You agree not to use any robot or spider." : null,
                PermissionRecordedAtUtc = permission ? T0 : null, PermissionGrantedBy = permission ? "The publisher" : null,
            };
            var forbidden = Source("forbidden.example", SourceRightsRule.Forbidden);
            var needs = Source("needs.example", SourceRightsRule.PermissionNeeded);
            db.ScrapingSources.AddRange(forbidden, Source("www.forbidden.example", SourceRightsRule.Forbidden),
                Source("lifted.example", SourceRightsRule.Forbidden, permission: true), needs, Source("facts.example", SourceRightsRule.FactsOnly),
                Source("mixed.example", SourceRightsRule.Forbidden), Source("www.mixed.example", SourceRightsRule.FactsOnly));
            await db.SaveChangesAsync();

            RecipeEntity Recipe(string url, string name, bool deleted = false) => new()
            {
                AuthorId = Admin, Name = name, Description = $"{Marker} a description.", DescriptionNormalized = $"{Marker.ToLowerInvariant()} a description",
                SourceUrl = url, SourceSite = new Uri(url).Host, ScrapedAtUtc = T0.AddDays(-60), SourceImageUrl = $"https://cdn.example/{Marker}.jpg",
                SourceAttribution = $"Recipe by {Marker}", SourceCredit = $"Adapted from {Marker}", RecipeYield = $"{Marker} serves 4", VettingIssues = $"{Marker} issue",
                CurationStatusId = (long)CurationStatusEnum.Curated, Visibility = RecipeVisibilityEnum.Public, Servings = 4, IsDeleted = deleted,
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = $"{Marker} brown.", Description = $"{Marker} brown the beef in a pot." } },
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = catalog.Id, Quantity = 1, RawLine = $"1 pound beef, {Marker} trimmed", SortOrder = 0 },
                    new() { IngredientId = fromLine.Id, Quantity = 0, RawLine = $"secret spice blend, {Marker} to taste", SortOrder = 1 },
                },
            };
            var stew = Recipe("https://forbidden.example/stew/", "Beef Stew");
            var cake = Recipe("https://www.forbidden.example/cake", "Lemon Cake");
            var old = Recipe("http://forbidden.example/old-soup/", "Old Soup", deleted: true);
            var lifted = Recipe("https://lifted.example/pie/", "Pie");
            var needsRecipe = Recipe("https://needs.example/tart/", "Tart");
            var facts = Recipe("https://facts.example/chili/", "Chili");
            var mixed = Recipe("https://mixed.example/curry/", "Curry");
            var open = Recipe("https://open.example/stew/", "Open Beef Stew");
            db.Recipes.AddRange(stew, cake, old, lifted, needsRecipe, facts, mixed, open);
            await db.SaveChangesAsync();
            foreach (var r in new[] { stew, cake, old, lifted, needsRecipe, facts, mixed, open })
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = r.Id, SourceUrl = r.SourceUrl!, RawJsonLd = $"{{\"description\":\"{Marker} page\"}}", Format = ScrapedDocumentFormat.JsonLd, FetchedAtUtc = T0.AddDays(-60) });
            await db.SaveChangesAsync();
            return new World(stew.Id, cake.Id, old.Id, lifted.Id, needsRecipe.Id, facts.Id, mixed.Id, open.Id, forbidden.Id, needs.Id, catalog.Id, fromLine.Id);
        }

        private static async Task HoldAsync(ApplicationDbContext db, World w)
        {
            db.MealPlans.Add(new MealPlanEntity { Id = 501, HouseholdId = HouseholdId, AuthorId = Member, Date = new DateOnly(2026, 11, 24), MealTypeId = 1102, RecipeId = w.Stew, Title = "Beef Stew" });
            db.RecipeRatings.AddRange(new RecipeRatingEntity { RecipeId = w.Stew, RaterId = Member, Rating = 5 }, new RecipeRatingEntity { RecipeId = w.Open, RaterId = Admin, Rating = 4 });
            db.Set<HouseholdCookbookEntity>().Add(new HouseholdCookbookEntity { Id = 601, HouseholdId = HouseholdId, Name = "Ours" });
            db.HouseholdCookbookRecipes.AddRange(new HouseholdCookbookRecipeEntity { HouseholdCookbookId = 601, RecipeId = w.Stew }, new HouseholdCookbookRecipeEntity { HouseholdCookbookId = 601, RecipeId = w.Open });
            db.ShoppingLists.Add(new ShoppingListEntity { Id = 701, Name = "Groceries", AuthorId = Member, HouseholdId = HouseholdId });
            db.ShoppingListItems.AddRange(
                new ShoppingListItemEntity { Id = 801, ShoppingListId = 701, Name = "Beef Stew Meat", IngredientId = w.CatalogFood, RecipeId = w.Stew, Note = ShoppingItemNote.ForRecipe("Beef Stew") },
                new ShoppingListItemEntity { Id = 802, ShoppingListId = 701, Name = $"secret spice blend, {Marker} to taste", IngredientId = w.LineFood, RecipeId = w.Stew, Note = ShoppingItemNote.ForRecipe("Beef Stew") });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task The_dry_run_equals_the_apply_and_only_a_host_forbidden_on_every_row_qualifies()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            await HoldAsync(db, w);

            var dry = await Removal(db).RetireAsync(null, apply: false, Admin, null);
            db.ChangeTracker.Clear();
            var applied = await Removal(db, batch: 2).RetireAsync(null, apply: true, Admin, "owner's decision 2026-11-24");

            dry.Refused.Should().BeNull();
            dry.Recipes.Should().Be(3, "stew, cake (the www row) and the recipe its author had deleted");
            dry.RecipeIds.Should().BeEquivalentTo(new[] { w.Stew, w.Cake, w.Old });
            dry.Sources.Should().ContainSingle().Which.Domain.Should().Be("forbidden.example");
            var source = dry.Sources.Single();
            (source.Recipes, source.AlreadyDeleted, source.Approved, source.InMealPlans, source.Favourited, source.InCookbooks, source.InShoppingLists)
                .Should().Be((3, 1, 3, 1, 1, 1, 1));
            applied.Recipes.Should().Be(dry.Recipes);
            applied.RecipeIds.Should().Equal(dry.RecipeIds);
            JsonSerializer.Serialize(applied.Sources).Should().Be(JsonSerializer.Serialize(dry.Sources));
            applied.Applied.Should().BeTrue();
            applied.Stamp.Should().Be(T0);

            var removals = await db.RecipeRemovals.AsNoTracking().OrderBy(r => r.RecipeId).ToListAsync();
            removals.Select(r => r.RecipeId).Should().Equal(w.Stew, w.Cake, w.Old);
            removals.Should().OnlyContain(r => r.Host == "forbidden.example" && r.RightsClass == SourceRightsRule.Forbidden && r.RetiredAtUtc == T0 && r.RetiredByPersonId == Admin);
            removals.Single(r => r.RecipeId == w.Cake).UrlKey.Should().Be("forbidden.example/cake");
            removals.Single(r => r.RecipeId == w.Old).WasDeleted.Should().BeTrue();
            (await db.RecipeRemovalRuns.SingleAsync()).Should().Match<RecipeRemovalRunEntity>(r => r.FinishedAtUtc != null && r.Done == 3 && r.Planned == 3);

            var untouched = new[] { w.Lifted, w.Needs, w.Facts, w.Mixed, w.Open };
            (await db.Recipes.Where(r => untouched.Contains(r.Id)).CountAsync()).Should().Be(5, "a permission, a permission-needed, a facts-only and a host with a facts-only row are not touched");
            (await db.RecipeIngredients.CountAsync(l => untouched.Contains(l.RecipeId))).Should().Be(10);
            foreach (var id in new[] { w.Stew, w.Cake })
            {
                var recipe = await db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == id);
                (recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId).Should().Be((true, (DateTime?)T0, (long?)Admin));
                (await db.RecipeIngredients.IgnoreQueryFilters().Where(l => l.RecipeId == id).ToListAsync()).Should().OnlyContain(l => l.IsDeleted && l.DeletedAt == T0);
                (await db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == id).ToListAsync()).Should().OnlyContain(s => s.IsDeleted && s.DeletedAt == T0);
                (await db.ScrapedDocuments.IgnoreQueryFilters().Where(d => d.RecipeId == id).ToListAsync()).Should().OnlyContain(d => d.IsDeleted && d.DeletedAt == T0);
            }

            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == RecipeRemovalService.RetireChangeType)).Should().Be(3);
            var again = await Removal(db).RetireAsync(null, apply: true, Admin, null);
            again.Recipes.Should().Be(0);
            again.AlreadyDone.Should().Be(3);

            var refused = await Removal(db).RetireAsync(w.NeedsSource, apply: false, Admin, null);
            refused.Refused.Should().Contain("not forbidden").And.Contain(SourceRightsRule.PermissionNeeded);
        }

        [Fact]
        public async Task A_retired_recipe_leaves_every_surface_and_a_household_keeps_its_entries_with_the_title_the_link_and_a_plain_note()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            await HoldAsync(db, w);
            db.RecipeRatings.AddRange(new[] { w.Lifted, w.Needs, w.Facts, w.Mixed }.Select(id => new RecipeRatingEntity { RecipeId = id, RaterId = Admin, Rating = 3 }));
            await db.SaveChangesAsync();
            await Removal(db).RetireAsync(null, apply: true, Admin, null);
            db.ChangeTracker.Clear();

            var search = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);
            var found = await search.SearchRecipesAsync(new RecipeSearchModel { Query = "Stew" });
            found.Results.Select(r => r.Id).Should().Contain(w.Open, "the walk would pass on an empty answer otherwise").And.NotContain(w.Stew);
            (await search.GetPopularRecipesAsync(50)).Results.Select(r => r.Id).Should().Contain(w.Open).And.NotContain(new[] { w.Stew, w.Cake });
            (await search.GetRecentRecipesAsync(50)).Results.Select(r => r.Id).Should().Contain(w.Open).And.NotContain(new[] { w.Stew, w.Cake });
            (await search.GetRandomRecipesAsync(50)).Results.Select(r => r.Id).Should().Contain(w.Open).And.NotContain(new[] { w.Stew, w.Cake });
            (await search.GetSearchSuggestionsAsync("Beef")).Should().Contain("Open Beef Stew").And.NotContain("Beef Stew");
            (await db.Recipes.VisibleTo(db, Member).Select(r => r.Id).ToListAsync()).Should().Contain(w.Open).And.NotContain(new[] { w.Stew, w.Cake });
            (await db.Recipes.VisibleTo(db, null).Select(r => r.Id).ToListAsync()).Should().NotContain(new[] { w.Stew, w.Cake }, "the public pages read the anonymous pool");
            (await db.Recipes.VisibleTo(db, Admin).Select(r => r.Id).ToListAsync()).Should().NotContain(new[] { w.Stew, w.Cake }, "not even its author sees it");

            var detail = new RecipeOrchestrationService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(), Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>());
            (await detail.GetRecipeAsync(w.Stew, Admin)).Should().BeNull();
            (await detail.GetRecipeAsync(w.Open, Admin)).Should().NotBeNull();

            var plans = new MealPlanOrchestrationService(db, Mock.Of<IPolicyEnforcementService>());
            var pick = () => plans.CreateMealPlanAsync(new MealPlanCreateModel { HouseholdId = HouseholdId, Date = new DateOnly(2026, 11, 25), MealTypeId = 1102, RecipeId = w.Stew }, Member);
            await pick.Should().ThrowAsync<InvalidOperationException>().WithMessage("*recipe_not_visible*", "a plan's picker cannot take it");

            var export = new RecipeBulkOperationsService(db, Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(), NullLogger<RecipeBulkOperationsService>.Instance);
            var exported = await export.ExportRecipesAsync(new RecipeBulkExportModel { RecipeIds = new List<long> { w.Stew, w.Open }, RequesterPersonId = Admin, ExportType = ExportTypes.Json });
            var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "exports", $"recipes_export_{exported.ExportId}.json");
            try
            {
                if (System.IO.File.Exists(path))
                    (await System.IO.File.ReadAllTextAsync(path)).Should().NotContain("forbidden.example").And.Contain("Open Beef Stew");
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }

            (await db.Recipes.Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated).Select(r => r.Id).ToListAsync())
                .Should().NotContain(new[] { w.Stew, w.Cake }, "the curation queue, the image finder and every lane read recipes through the live filter");
            (await db.RecipeIngredients.Select(l => l.RecipeId).Distinct().ToListAsync()).Should().NotContain(new[] { w.Stew, w.Cake, w.Old }, "the link and wording lanes read live lines");
            (await db.RecipeSteps.Select(s => s.RecipeId).Distinct().ToListAsync()).Should().NotContain(new[] { w.Stew, w.Cake }, "the prose and reprocess lanes read live steps");
            (await db.ScrapedDocuments.Select(d => d.RecipeId).ToListAsync()).Should().NotContain(new[] { w.Stew, w.Cake, w.Old }, "the reprocess lane and the prose stock index read live documents");
            var stock = await new ProseStockService(db, Mock.Of<ICurrentUserService>()).RebuildAsync();
            stock.Documents.Should().Be(5, "only the five recipes not removed keep a document the index reads");

            var cookbook = await new CookbookOrchestrationService(db, NullLogger<CookbookOrchestrationService>.Instance).GetCookbookRecipesAsync(601);
            cookbook.Select(r => r.Id).Should().BeEquivalentTo(new[] { w.Stew, w.Open });
            var kept = cookbook.Single(r => r.Id == w.Stew);
            (kept.Name, kept.SourceUrl, kept.Removed, kept.RemovedNote).Should().Be(("Beef Stew", "https://forbidden.example/stew/", true, RecipeRemovalService.RemovedNote));
            JsonSerializer.Serialize(kept).Should().NotContain(Marker);
            cookbook.Single(r => r.Id == w.Open).Removed.Should().BeFalse();

            var entries = await plans.GetAllMealPlansAsync(householdIds: new List<long> { HouseholdId });
            var entry = entries.Single(e => e.Id == 501);
            (entry.Title, entry.RecipeName, entry.RecipeRemoved, entry.RecipeSourceUrl, entry.RecipeRemovedNote)
                .Should().Be(("Beef Stew", "Beef Stew", true, "https://forbidden.example/stew/", RecipeRemovalService.RemovedNote));
            (await plans.GetMealPlanAsync(501))!.RecipeRemoved.Should().BeTrue();
            var week = await plans.GetWeekAsync(HouseholdId, new DateOnly(2026, 11, 23));
            var cell = JsonSerializer.Serialize(week);
            cell.Should().Contain(RecipeRemovalService.RemovedNote).And.Contain("https://forbidden.example/stew/").And.NotContain(Marker);

            var users = new UserManagementOrchestrationService(new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object,
                NullLogger<UserManagementOrchestrationService>.Instance, db, Mock.Of<IPersonOrchestrationService>());
            var favourite = (await users.GetUserFavoritesAsync("u70")).Should().ContainSingle().Subject;
            (favourite.RecipeId, favourite.RecipeName, favourite.RecipeImage, favourite.RecipeRemoved, favourite.RecipeSourceUrl, favourite.RecipeRemovedNote)
                .Should().Be((w.Stew, "Beef Stew", "", true, "https://forbidden.example/stew/", RecipeRemovalService.RemovedNote));
            (await users.GetUserRatingsAsync("u70")).Should().ContainSingle(r => r.RecipeRemoved);

            var smart = new SmartShoppingListService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(), NullLogger<SmartShoppingListService>.Instance, new System.Net.Http.HttpClient());
            var optimized = await smart.OptimizeShoppingListAsync(new ShoppingListOptimizationModel { ShoppingListId = 701 });
            optimized.Items.Should().HaveCount(2, "the household keeps its items");
            optimized.Items.Should().OnlyContain(i => i.Notes == $"From recipe: Beef Stew. {RecipeRemovalService.RemovedNote} Source: https://forbidden.example/stew/");
            JsonSerializer.Serialize(optimized).Should().NotContain(Marker, "an item named from the source's line keeps its food words only");
            optimized.Items.Select(i => i.Name).Should().BeEquivalentTo("Beef Stew Meat", "secret spice blend");
        }

        [Fact]
        public async Task The_stored_diet_gate_drops_a_retired_recipe_and_a_later_build_does_not_bring_it_back()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            (await db.DietRecipeStates.AnyAsync(s => s.RecipeId == w.Stew)).Should().BeTrue("the walk needs a stored state to drop");

            await Removal(db).RetireAsync(null, apply: true, Admin, null);
            db.ChangeTracker.Clear();
            (await db.DietRecipeStates.AnyAsync(s => s.RecipeId == w.Stew || s.RecipeId == w.Cake)).Should().BeFalse();
            await builder.RunAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);
            (await db.DietRecipeStates.AnyAsync(s => s.RecipeId == w.Stew || s.RecipeId == w.Cake)).Should().BeFalse();
            (await db.DietRecipeStates.AnyAsync(s => s.RecipeId == w.Open)).Should().BeTrue();
        }

        [Fact]
        public async Task A_run_cut_off_by_a_restart_resumes_under_its_stamp_and_the_undo_by_that_stamp_puts_everything_back()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            await HoldAsync(db, w);
            var cut = T0.AddMinutes(-5);
            var first = await Removal(db, batch: 1).RetireAsync(w.ForbiddenSource, apply: false, Admin, null);
            first.Recipes.Should().Be(3);
            var before = await SnapshotAsync(db);

            var removal = new RecipeRemovalService(db, NullLogger<RecipeRemovalService>.Instance) { Now = () => cut, Batch = 1 };
            using (var cancel = new CancellationTokenSource())
            {
                var calls = 0;
                db.SavedChanges += (_, _) =>
                {
                    if (++calls == 3) cancel.Cancel();
                };
                var act = () => removal.RetireAsync(w.ForbiddenSource, apply: true, Admin, null, cancel.Token);
                await act.Should().ThrowAsync<OperationCanceledException>();
            }

            db.ChangeTracker.Clear();
            var unfinished = await db.RecipeRemovalRuns.SingleAsync();
            unfinished.FinishedAtUtc.Should().BeNull();
            var partly = await db.RecipeRemovals.CountAsync();
            partly.Should().BeInRange(1, 2, "the cut came after a committed batch");
            (await Removal(db).StatusAsync()).Unfinished!.Id.Should().Be(unfinished.Id);

            var otherDry = await Removal(db).RetireAsync(null, apply: false, Admin, null);
            var otherApply = await Removal(db).RetireAsync(null, apply: true, Admin, null);
            foreach (var other in new[] { otherDry, otherApply })
            {
                other.Refused.Should().Contain("was cut off").And.Contain($"sourceId={w.ForbiddenSource}").And.Contain("undo it by its stamp");
                other.Resumed.Should().BeFalse("a run of another scope neither resumes the cut-off run nor takes over its scope");
            }

            (await db.RecipeRemovals.CountAsync()).Should().Be(partly);
            var www = await db.ScrapingSources.Where(s => s.Domain == "www.forbidden.example").Select(s => s.Id).SingleAsync();
            var resumeDry = await Removal(db).RetireAsync(www, apply: false, Admin, null);
            (resumeDry.Resumed, resumeDry.Stamp, resumeDry.Recipes).Should().Be((true, cut, 3 - partly), "the dry run of the same host shows the resume the apply does");

            var resumed = await Removal(db).RetireAsync(w.ForbiddenSource, apply: true, Admin, null);
            resumed.Resumed.Should().BeTrue();
            resumed.Stamp.Should().Be(cut);
            resumed.SourceId.Should().Be(w.ForbiddenSource, "the run's own scope");
            resumed.Recipes.Should().Be(3 - partly);
            (await db.RecipeRemovals.Select(r => r.RetiredAtUtc).Distinct().ToListAsync()).Should().Equal(cut);
            (await db.RecipeRemovalRuns.SingleAsync()).FinishedAtUtc.Should().NotBeNull();
            (await db.ShoppingListItems.SingleAsync(i => i.Id == 802)).Name.Should().Be("secret spice blend");

            var dryUndo = await Removal(db).UndoRetireAsync(cut, apply: false, Admin);
            dryUndo.Recipes.Should().Be(3);
            var undo = await Removal(db).UndoRetireAsync(cut, apply: true, Admin);
            undo.Recipes.Should().Be(3);
            db.ChangeTracker.Clear();
            (await SnapshotAsync(db)).Should().Be(before, "the undo puts back exactly what the retire took off");
            (await db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == w.Old)).IsDeleted.Should().BeTrue("a recipe deleted before stays deleted");
            (await Removal(db).UndoRetireAsync(cut, apply: false, Admin)).AlreadyDone.Should().Be(3);
            (await RecipeRemovalService.RefusalAsync(db, "https://forbidden.example/stew/")).Should().BeNull("an undone removal refuses nothing");
        }

        private static async Task<string> SnapshotAsync(ApplicationDbContext db)
        {
            var recipes = await db.Recipes.IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => new { r.Id, r.IsDeleted, r.DeletedAt, r.DeletedByPersonId, r.Name, r.Description }).ToListAsync();
            var lines = await db.RecipeIngredients.IgnoreQueryFilters().OrderBy(l => l.RecipeId).ThenBy(l => l.SortOrder).Select(l => new { l.RecipeId, l.SortOrder, l.IsDeleted, l.RawLine }).ToListAsync();
            var steps = await db.RecipeSteps.IgnoreQueryFilters().OrderBy(s => s.RecipeId).Select(s => new { s.RecipeId, s.StepNumber, s.IsDeleted, s.Description }).ToListAsync();
            var docs = await db.ScrapedDocuments.IgnoreQueryFilters().OrderBy(d => d.Id).Select(d => new { d.Id, d.IsDeleted, d.RawJsonLd }).ToListAsync();
            var items = await db.ShoppingListItems.IgnoreQueryFilters().OrderBy(i => i.Id).Select(i => new { i.Id, i.Name, i.Note }).ToListAsync();
            return JsonSerializer.Serialize(new { recipes, lines, steps, docs, items });
        }

        [Fact]
        public async Task A_cut_off_retire_run_whose_source_no_longer_qualifies_is_closed_and_blocks_no_other_source()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            var other = new ScrapingSourceEntity { Domain = "other.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0, RightsClass = SourceRightsRule.Forbidden };
            db.ScrapingSources.Add(other);
            var soup = new RecipeEntity { AuthorId = Admin, Name = "Other Soup", SourceUrl = "https://other.example/soup/", SourceSite = "other.example", CurationStatusId = (long)CurationStatusEnum.NonCurated, Visibility = RecipeVisibilityEnum.Public, Servings = 2 };
            db.Recipes.Add(soup);
            var cut = T0.AddMinutes(-5);
            db.RecipeRemovalRuns.Add(new RecipeRemovalRunEntity { Kind = RecipeRemovalService.RetireKind, Stamp = cut, ScrapingSourceId = w.ForbiddenSource, PersonId = Admin, StartedAtUtc = cut, Planned = 3, Reason = "r" });
            foreach (var source in await db.ScrapingSources.Where(s => s.Domain.EndsWith("forbidden.example")).ToListAsync())
            {
                source.PermissionRecordedAtUtc = T0;
                source.PermissionGrantedBy = "The publisher";
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var dry = await Removal(db).RetireAsync(other.Id, apply: false, Admin, null);
            db.ChangeTracker.Clear();
            var applied = await Removal(db).RetireAsync(other.Id, apply: true, Admin, null);
            db.ChangeTracker.Clear();

            foreach (var report in new[] { dry, applied })
            {
                report.Refused.Should().BeNull();
                (report.Resumed, report.Recipes, report.Stamp).Should().Be((false, 1, T0));
                report.Warning.Should().Contain("no longer qualifies");
            }

            var old = await db.RecipeRemovalRuns.SingleAsync(r => r.Stamp == cut);
            old.FinishedAtUtc.Should().NotBeNull("a run that can never be finished is closed, not left to block every later retire");
            old.Reason.Should().Contain("no longer qualifies");
            (await db.RecipeRemovals.SingleAsync()).RecipeId.Should().Be(soup.Id);
            (await Removal(db).StatusAsync()).Unfinished.Should().BeNull();
        }

        [Fact]
        public async Task An_undo_of_a_cut_off_retire_run_closes_it_so_a_later_retire_does_not_resume_it_under_the_old_stamp()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            var cut = T0.AddMinutes(-5);
            var removal = new RecipeRemovalService(db, NullLogger<RecipeRemovalService>.Instance) { Now = () => cut, Batch = 1 };
            using (var cancel = new CancellationTokenSource())
            {
                var calls = 0;
                void Cut(object? sender, SavedChangesEventArgs e)
                {
                    if (++calls == 3) cancel.Cancel();
                }

                db.SavedChanges += Cut;
                var act = () => removal.RetireAsync(null, apply: true, Admin, null, cancel.Token);
                await act.Should().ThrowAsync<OperationCanceledException>();
                db.SavedChanges -= Cut;
            }

            db.ChangeTracker.Clear();
            (await db.RecipeRemovalRuns.SingleAsync()).FinishedAtUtc.Should().BeNull();
            var dryUndo = await Removal(db).UndoRetireAsync(cut, apply: false, Admin);
            dryUndo.Warning.Should().Contain("cut off");
            var undo = await Removal(db).UndoRetireAsync(cut, apply: true, Admin);
            undo.Recipes.Should().Be(dryUndo.Recipes).And.BeGreaterThan(0);
            db.ChangeTracker.Clear();
            (await db.RecipeRemovalRuns.SingleAsync(r => r.Kind == RecipeRemovalService.RetireKind)).FinishedAtUtc.Should().NotBeNull("the undo abandons the cut-off run");

            var again = await Removal(db).RetireAsync(w.ForbiddenSource, apply: true, Admin, null);
            again.Resumed.Should().BeFalse();
            again.Recipes.Should().Be(3);
            (await db.RecipeRemovals.Where(r => r.UndoneAtUtc == null).Select(r => r.RetiredAtUtc).Distinct().ToListAsync()).Should().Equal(T0);
            (await db.RecipeRemovalRuns.CountAsync(r => r.Kind == RecipeRemovalService.RetireKind && r.FinishedAtUtc == null)).Should().Be(0);
        }

        [Fact]
        public async Task A_retire_through_the_api_and_its_undo_leave_no_admin_as_the_editor_of_the_recipe_its_lines_steps_or_documents()
        {
            var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("PersonId", Admin.ToString(System.Globalization.CultureInfo.InvariantCulture)) }, "test")),
                },
            };
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options, accessor);
            var w = await SeedAsync(db);
            db.ChangeTracker.Clear();
            var before = await EditorsAsync(db);
            JsonSerializer.Deserialize<List<string>>(before)!.Should().NotBeEmpty();

            await Removal(db).RetireAsync(w.ForbiddenSource, apply: true, Admin, null);
            db.ChangeTracker.Clear();
            (await db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == w.Stew).ToListAsync())
                .Should().OnlyContain(s => s.LastModifiedByPersonId == null, "the prose lanes read a step's editor as a person's text");
            await Removal(db).UndoRetireAsync(T0, apply: true, Admin);
            db.ChangeTracker.Clear();

            (await EditorsAsync(db)).Should().Be(before, "the undo leaves every row as it was before the retire");
        }

        private static async Task<string> EditorsAsync(ApplicationDbContext db)
        {
            var rows = new List<string>();
            rows.AddRange(await db.Recipes.IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => $"recipe {r.Id} {r.IsDeleted} {r.LastModifiedByPersonId} {r.LastModifiedDate:O}").ToListAsync());
            rows.AddRange(await db.RecipeIngredients.IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => $"line {r.Id} {r.IsDeleted} {r.LastModifiedByPersonId} {r.LastModifiedDate:O}").ToListAsync());
            rows.AddRange(await db.RecipeSteps.IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => $"step {r.Id} {r.IsDeleted} {r.LastModifiedByPersonId} {r.LastModifiedDate:O}").ToListAsync());
            rows.AddRange(await db.ScrapedDocuments.IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => $"document {r.Id} {r.IsDeleted} {r.LastModifiedByPersonId} {r.LastModifiedDate:O}").ToListAsync());
            return JsonSerializer.Serialize(rows);
        }

        [Fact]
        public async Task Approved_by_a_person_counts_only_a_persons_recipe_approval_and_an_approval_with_no_record_is_counted_apart()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            db.Set<Nom.Data.Reference.ReferenceEntity>().AddRange(
                new Nom.Data.Reference.ReferenceEntity { Id = 1201, Name = "Recipe" },
                new Nom.Data.Reference.ReferenceEntity { Id = 1202, Name = "Ingredient" },
                new Nom.Data.Reference.ReferenceEntity { Id = 1203, Name = "Approval" });
            db.CurationFeedbacks.AddRange(
                new CurationFeedbackEntity { EntityId = w.Stew, EntityTypeId = 1201, FeedbackTypeId = 1203, AdminId = Member, FeedbackNotes = "looks right" },
                new CurationFeedbackEntity { EntityId = w.Cake, EntityTypeId = 1201, FeedbackTypeId = 1203, AdminId = SystemConstants.SystemPersonId, FeedbackNotes = "auto-approved" },
                new CurationFeedbackEntity { EntityId = w.Stew, EntityTypeId = 1202, FeedbackTypeId = 1203, AdminId = SystemConstants.SystemPersonId, FeedbackNotes = "an ingredient with the same id" });
            await db.SaveChangesAsync();

            var dry = await Removal(db).RetireAsync(w.ForbiddenSource, apply: false, Admin, null);

            var source = dry.Sources.Single();
            (source.Approved, source.ApprovedByPerson, source.ApprovedUnrecorded).Should().Be((3, 1, 1),
                "stew was approved by a person, cake by the system, and old soup, curated with no approval row, has no recorded approver");
        }

        [Fact]
        public async Task A_per_source_purge_finds_the_host_retired_under_either_of_its_rows()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            await Removal(db).RetireAsync(null, apply: true, Admin, null);
            db.ChangeTracker.Clear();
            var www = await db.ScrapingSources.Where(s => s.Domain == "www.forbidden.example").Select(s => s.Id).SingleAsync();
            www.Should().NotBe(w.ForbiddenSource);

            var dry = await Removal(db).PurgeAsync(www, apply: false, backupTaken: false, Admin);
            dry.Recipes.Should().Be(3);
            var purged = await Removal(db).PurgeAsync(www, apply: true, backupTaken: true, Admin);
            purged.Recipes.Should().Be(3);
            db.ChangeTracker.Clear();
            (await db.RecipeRemovals.CountAsync(r => r.PurgedAtUtc != null)).Should().Be(3);
            (await Removal(db).PurgeAsync(w.ForbiddenSource, apply: false, backupTaken: false, Admin)).AlreadyDone.Should().Be(3);
        }

        [Fact]
        public async Task A_purge_is_refused_without_a_backup_and_leaves_no_column_of_a_purged_recipe_holding_source_text()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            await HoldAsync(db, w);
            db.AuditLogEntries.Add(new AuditLogEntryEntity { EntityType = "Recipe", EntityId = w.Stew, ChangeType = "ProseLaneRewrite", PropertyName = "Description", OldValue = $"{Marker} old", NewValue = $"{Marker} new", Timestamp = T0.AddDays(-1), ChangedByPersonId = 1 });
            db.AuditLogEntries.Add(new AuditLogEntryEntity { EntityType = "RecipeStep", EntityId = w.Stew, ChangeType = "PersonEditBase", PropertyName = "Steps", NewValue = $"{Marker} base", Timestamp = T0.AddDays(-1), ChangedByPersonId = Member });
            db.RecipeReprocessJournals.Add(new RecipeReprocessJournalEntity { RecipeId = w.Stew, OldSteps = $"[{{\"description\":\"{Marker}\"}}]", NewSteps = $"[{{\"description\":\"{Marker}\"}}]" });
            db.LineGuardHolds.Add(new LineGuardHoldEntity { RecipeId = w.Stew, IngredientId = w.CatalogFood, RawLine = $"{Marker} line", StoredFood = Marker, Reason = "r", RuleVersion = "v" });
            db.ReprocessCandidates.Add(new ReprocessCandidateEntity { RecipeId = w.Stew });
            db.RecipeImageCandidates.Add(new RecipeImageCandidateEntity { RecipeId = w.Stew, ImageUrl = $"https://img.example/{Marker}.jpg" });
            db.LaneDecisions.Add(new LaneDecisionEntity { Lane = "prose", Subject = $"recipe:{w.Stew}", SignalsJson = $"{{\"s\":\"{Marker}\"}}", VerdictsJson = $"{{\"v\":\"{Marker}\"}}", AuditJson = Marker });
            await db.SaveChangesAsync();
            await Removal(db).RetireAsync(null, apply: true, Admin, null);
            db.ChangeTracker.Clear();

            var dry = await Removal(db).PurgeAsync(null, apply: false, backupTaken: false, Admin);
            dry.Recipes.Should().Be(3);
            dry.Warning.Should().Contain("cannot be undone").And.Contain("backup");
            dry.Targets.Select(t => t.Table).Should().Contain(new[]
            {
                "recipe.Recipe", "recipe.RecipeStep", "recipe.RecipeIngredient", "recipe.ScrapedDocument", "recipe.RecipeReprocessJournal", "recipe.LineGuardHold",
                "recipe.RecipeImageCandidate", "audit.AuditLogEntry", "curation.LaneDecision", "shopping.ShoppingListItem",
            });
            dry.Targets.Single(t => t.Table == "recipe.Recipe").Columns.Split(", ").Should().Contain(new[] { "Description", "SourceImageUrl" }).And.NotContain(new[] { "Name", "SourceUrl", "SourceSite" });
            dry.Targets.Single(t => t.Table == "recipe.RecipeStep").Rows.Should().Be(3);
            (await db.RecipeSteps.IgnoreQueryFilters().CountAsync(s => s.RecipeId == w.Stew)).Should().Be(1, "a dry run deletes nothing");

            var refused = await Removal(db).PurgeAsync(null, apply: true, backupTaken: false, Admin);
            refused.Refused.Should().Contain("backup");
            (await db.RecipeRemovals.CountAsync(r => r.PurgedAtUtc != null)).Should().Be(0);

            var purged = await Removal(db, batch: 2).PurgeAsync(null, apply: true, backupTaken: true, Admin);
            purged.Recipes.Should().Be(dry.Recipes);
            db.ChangeTracker.Clear();

            foreach (var id in new[] { w.Stew, w.Cake, w.Old })
            {
                var held = await RowsHoldingAsync(db, id, Marker);
                held.Should().BeEmpty($"no column of purged recipe {id} may hold source text");
            }

            (await RowsHoldingAsync(db, w.Open, Marker)).Should().NotBeEmpty("the scan finds the text where it was not purged");
            var stew = await db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == w.Stew);
            (stew.Name, stew.SourceUrl).Should().Be(("Beef Stew", "https://forbidden.example/stew/"));
            var row = await db.RecipeRemovals.SingleAsync(r => r.RecipeId == w.Stew);
            (row.Title, row.SourceUrl, row.Host, row.PurgedAtUtc).Should().Be(("Beef Stew", "https://forbidden.example/stew/", "forbidden.example", (DateTime?)T0));
            row.PurgeSummary.Should().Contain("recipe.RecipeStep");
            (await db.MealPlans.CountAsync(m => m.RecipeId == w.Stew)).Should().Be(1, "the household's entry stays");
            (await db.ShoppingListItems.CountAsync(i => i.RecipeId == w.Stew)).Should().Be(2);

            var undo = await Removal(db).UndoRetireAsync(T0, apply: true, Admin);
            undo.Recipes.Should().Be(0);
            undo.SkippedByReason.Keys.Should().ContainSingle().Which.Should().Contain("purged");
            (await db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == w.Stew)).IsDeleted.Should().BeTrue();
            (await Removal(db).PurgeAsync(null, apply: false, backupTaken: false, Admin)).Recipes.Should().Be(0);
        }

        /// <summary>Every row of the model keyed to the recipe (its own row, every entity with a RecipeId, its journal rows and lane decisions) with a text column holding <paramref name="text"/>.</summary>
        private static async Task<List<string>> RowsHoldingAsync(ApplicationDbContext db, long recipeId, string text)
        {
            var found = new List<string>();
            var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
            var ignore = typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters), new[] { typeof(IQueryable<>).MakeGenericType(Type.MakeGenericMethodParameter(0)) })!;
            foreach (var type in db.Model.GetEntityTypes().Where(t => t.FindPrimaryKey() != null && !t.IsOwned() && !t.HasSharedClrType && t.BaseType == null))
            {
                var key = type.ClrType == typeof(RecipeEntity) ? "Id" : type.FindProperty("RecipeId") != null ? "RecipeId" : null;
                if (key == null || type.ClrType == typeof(RecipeRemovalEntity)) continue;
                var query = (IQueryable)ignore.MakeGenericMethod(type.ClrType).Invoke(null, new[] { setMethod.MakeGenericMethod(type.ClrType).Invoke(db, null) })!;
                foreach (var row in query.Cast<object>().ToList())
                {
                    var value = type.ClrType.GetProperty(key)!.GetValue(row);
                    if (value is not long id || id != recipeId) continue;
                    foreach (var property in type.ClrType.GetProperties().Where(p => p.PropertyType == typeof(string)))
                        if (property.GetValue(row) is string s && s.Contains(text, StringComparison.Ordinal))
                            found.Add($"{type.ClrType.Name}.{property.Name}");
                }
            }

            var documents = await db.ScrapedDocuments.IgnoreQueryFilters().Where(d => d.RecipeId == recipeId).Select(d => d.Id).ToListAsync();
            foreach (var entry in await db.AuditLogEntries.Where(a => (a.EntityId == recipeId && (a.EntityType == "Recipe" || a.EntityType == "RecipeStep" || a.EntityType == "RecipeIngredient"))
                         || (a.EntityType == "ScrapedDocument" && documents.Contains(a.EntityId))).ToListAsync())
                if ((entry.OldValue ?? "").Contains(text, StringComparison.Ordinal) || (entry.NewValue ?? "").Contains(text, StringComparison.Ordinal))
                    found.Add($"AuditLogEntry {entry.ChangeType}");
            var itemIds = await db.ShoppingListItems.IgnoreQueryFilters().Where(i => i.RecipeId == recipeId).Select(i => i.Id).ToListAsync();
            foreach (var entry in await db.AuditLogEntries.Where(a => a.EntityType == RecipeRemovalService.ItemEntityType && itemIds.Contains(a.EntityId)).ToListAsync())
                if ((entry.OldValue ?? "").Contains(text, StringComparison.Ordinal) || (entry.NewValue ?? "").Contains(text, StringComparison.Ordinal))
                    found.Add($"AuditLogEntry {entry.ChangeType}");
            var subject = $"recipe:{recipeId}";
            foreach (var decision in await db.LaneDecisions.IgnoreQueryFilters().Where(d => d.Subject == subject).ToListAsync())
                if (new[] { decision.SignalsJson, decision.VerdictsJson, decision.AuditJson }.Any(v => (v ?? "").Contains(text, StringComparison.Ordinal)))
                    found.Add("LaneDecision");
            return found;
        }

        [Fact]
        public async Task A_removed_address_is_refused_by_url_import_and_by_the_staged_import_even_after_its_source_is_lifted()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            await Removal(db).RetireAsync(null, apply: true, Admin, null);
            foreach (var source in await db.ScrapingSources.Where(s => s.Domain.EndsWith("forbidden.example")).ToListAsync())
            {
                source.PermissionRecordedAtUtc = T0;
                source.PermissionGrantedBy = "The publisher";
            }

            await db.SaveChangesAsync();
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            var sources = new Mock<IScrapingSourceService>();
            sources.Setup(s => s.GetDomainStatusAsync(It.IsAny<string>())).ReturnsAsync(ScrapingSourceStatusEnum.Approved);
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.RightsGateSetting] = "true" }).Build();
            var service = new RecipeScrapingService(db, user.Object, scraper.Object, sources.Object, new RecipeVettingService(), NullLogger<RecipeScrapingService>.Instance, config);

            foreach (var url in new[] { "https://forbidden.example/stew/", "https://www.forbidden.example/stew", "http://FORBIDDEN.example/stew/?utm=1" })
            {
                var answer = await service.ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel { Url = url });
                answer.Success.Should().BeFalse(url);
                answer.Error.Should().Contain("This address was removed").And.Contain("forbidden.example").And.Contain("not taken again");
            }

            scraper.Verify(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            var staged = await service.ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes = new List<ScraperRecipe>
                {
                    new() { Name = "Stew", SourceUrl = "https://forbidden.example/stew/", Ingredients = new List<ScraperIngredient> { new() { RawLine = "1 cup water" } } },
                    new() { Name = "Cake", SourceUrl = "https://forbidden.example/cake/", Ingredients = new List<ScraperIngredient> { new() { RawLine = "1 cup flour" } } },
                },
            });
            staged.Imported.Should().Be(0);
            staged.Refused.Should().Be(2);
            staged.Failures.Should().OnlyContain(f => f.Error.Contains("This address was removed"));
        }

        [Fact]
        public async Task The_rights_report_and_the_pipeline_figures_show_taken_retired_and_purged_per_source()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            var rights = new SourceRightsService(db, Options.Create(new SourceRightsSettings { Enforce = true }), NullLogger<SourceRightsService>.Instance) { Now = () => T0 };
            var before = (await rights.TakenAsync()).Single(t => t.Domain == "forbidden.example");
            (before.Recipes, before.Retired, before.Purged, before.Removable).Should().Be((2, 0, 0, true));
            (await rights.TakenAsync()).Single(t => t.Domain == "needs.example").Removable.Should().BeFalse();

            await Removal(db).RetireAsync(w.ForbiddenSource, apply: true, Admin, null);
            await Removal(db).PurgeAsync(w.ForbiddenSource, apply: true, backupTaken: true, Admin);
            db.ChangeTracker.Clear();
            var after = (await rights.TakenAsync()).Single(t => t.Domain == "forbidden.example");
            (after.Recipes, after.Retired, after.Purged).Should().Be((0, 3, 3));
            var status = await rights.StatusAsync();
            (status.RecipesRetired, status.RecipesPurged).Should().Be((3, 3));
            status.RemovalBySource.Single(s => s.Domain == "forbidden.example").Should().Match<RecipeRemovalFiguresModel>(s => s.Taken == 0 && s.Retired == 3 && s.Purged == 3 && s.Removable);
            status.RemovalBySource.Single(s => s.Domain == "needs.example").Should().Match<RecipeRemovalFiguresModel>(s => s.Taken == 1 && s.Retired == 0 && !s.Removable);
            var runs = (await Removal(db).StatusAsync()).Runs;
            runs.Select(r => r.Kind).Should().Equal(RecipeRemovalService.PurgeKind, RecipeRemovalService.RetireKind);
            runs.Single(r => r.Kind == RecipeRemovalService.RetireKind).Undoable.Should().Be(0, "a purged recipe cannot be put back");
        }
    }
}
