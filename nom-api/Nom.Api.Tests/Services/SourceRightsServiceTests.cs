using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The source rights engine against a database: the class and AI flag derived from the stored report
    /// and journaled; the recipes' stamps and the facts-only withhold that follow the class; permissions and
    /// facts-only approvals with their undo; the gate on harvesting, URL and staged imports; the weekly
    /// re-check; titles, credit lines and prose retention with their dry runs and undo; and the count of
    /// recipes taken from barred sources, which changes nothing.
    /// </summary>
    public class SourceRightsServiceTests
    {
        private const long Admin = 7;
        private static readonly DateTime T0 = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static SourceRightsService Rights(ApplicationDbContext db, Action<SourceRightsSettings>? configure = null)
        {
            var settings = new SourceRightsSettings { Enforce = true };
            configure?.Invoke(settings);
            return new SourceRightsService(db, Options.Create(settings), NullLogger<SourceRightsService>.Instance) { Now = () => T0 };
        }

        internal static ScraperSourceCheckReport FactsOnlyReport(string host, bool aiBlocked = false) => new()
        {
            Domain = host,
            RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion,
            Verdict = SourceAdmissionPolicy.NotClear,
            Complete = true,
            Reasons = new List<ScraperCheckReason> { new() { Code = "licence", Outcome = "fail" } },
            Access = new ScraperCheckAccess { HomepageUrl = $"https://{host}/", HomepageOutcome = "Ok", HomepageStatus = 200 },
            Robots = new ScraperCheckRobots
            {
                Url = $"https://{host}/robots.txt", Status = "found",
                NomBot = new ScraperCheckRobotsGroup { AllowsHomepage = true }, General = new ScraperCheckRobotsGroup { AllowsHomepage = true },
                AiCrawlers = aiBlocked ? new List<ScraperCheckAiCrawler> { new() { Agent = "GPTBot", Rule = "blocked" } } : new List<ScraperCheckAiCrawler>(),
            },
            Reservation = new ScraperCheckReservation { TdmRepStatus = "none" },
            Terms = new ScraperCheckTerms(),
            Licence = new ScraperCheckLicence
            {
                Statements = new List<ScraperCheckStatement> { new() { Kind = "all-rights-reserved", Text = "All rights reserved.", FoundAt = $"https://{host}/" } },
            },
        };

        internal static ScraperSourceCheckReport ForbiddenReport(string host)
        {
            var report = FactsOnlyReport(host);
            report.Terms!.Prohibitions.Add(new ScraperCheckProhibition { Category = "automated", Text = "You agree not to use any robot or spider to access this site.", FoundAt = $"https://{host}/terms" });
            return report;
        }

        internal static ScraperSourceCheckReport ClearReport(string host)
        {
            var report = FactsOnlyReport(host);
            report.Licence = new ScraperCheckLicence { Kind = "cc0", Basis = "per-page", Statements = new List<ScraperCheckStatement> { new() { Kind = "cc0", Text = "CC0 1.0", FoundAt = $"https://{host}/" } } };
            report.Reasons = new List<ScraperCheckReason> { new() { Code = "licence", Outcome = "pass" } };
            return report;
        }

        private static async Task<ScrapingSourceEntity> SourceAsync(ApplicationDbContext db, string domain, ScraperSourceCheckReport? report, Action<ScrapingSourceEntity>? configure = null)
        {
            var source = new ScrapingSourceEntity { Domain = domain, Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = T0 };
            configure?.Invoke(source);
            db.ScrapingSources.Add(source);
            await db.SaveChangesAsync();
            if (report != null)
            {
                var check = new SourceCheckEntity
                {
                    Domain = domain, Purpose = "recheck", ScrapingSourceId = source.Id, Status = SourceCheckEntity.Completed, StartedUtc = T0.AddHours(-1), FinishedUtc = T0,
                    RuleVersion = report.RuleVersion, Verdict = SourceAdmissionPolicy.NotClear, Conclusive = true, ReportJson = JsonSerializer.Serialize(report, Json),
                };
                db.SourceChecks.Add(check);
                await db.SaveChangesAsync();
                source.LastCheckId = check.Id;
                await db.SaveChangesAsync();
            }

            return source;
        }

        private static async Task<RecipeEntity> RecipeAsync(ApplicationDbContext db, string url, Action<RecipeEntity>? configure = null, string? jsonLd = null)
        {
            if (!await db.Persons.AnyAsync(p => p.Id == Admin))
            {
                db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = Admin, Name = "Admin" });
                db.Set<Nom.Data.Reference.ReferenceEntity>().AddRange(
                    new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" },
                    new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" });
                await db.SaveChangesAsync();
            }

            var recipe = new RecipeEntity
            {
                AuthorId = Admin,
                Name = "Weeknight Chili", Description = "A publisher's description.", SourceUrl = url, ScrapedAtUtc = T0.AddDays(-60),
                LicenseStatus = RecipeLicenseStatus.Unknown, CurationStatusId = (long)CurationStatusEnum.Curated,
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Brown the beef.", Description = "Brown the beef in a large pot." } },
            };
            configure?.Invoke(recipe);
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            if (jsonLd != null)
            {
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe.Id, SourceUrl = url, RawJsonLd = jsonLd, FetchedAtUtc = T0.AddDays(-60) });
                await db.SaveChangesAsync();
            }

            db.ChangeTracker.Clear();
            return recipe;
        }

        private static string JsonLd(string description, params string[] steps) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@type"] = "Recipe",
            ["name"] = "Weeknight Chili",
            ["description"] = description,
            ["recipeIngredient"] = new[] { "1 pound ground beef", "1 can kidney beans", "2 tablespoons chili powder" },
            ["recipeInstructions"] = steps.Select(s => new Dictionary<string, string> { ["@type"] = "HowToStep", ["text"] = s }).ToArray(),
        });

        private static void OwnMethod(RecipeEntity r)
        {
            r.ProseLaneVersion = "lane-v";
            r.ProseAttempts = 0;
            r.ProseRewriteAttemptedAt = T0.AddDays(-40);
        }

        [Fact]
        public async Task Each_source_takes_the_class_its_report_gives_it_journaled_and_its_recipes_follow()
        {
            await using var db = NewContext();
            var facts = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example", aiBlocked: true));
            var barred = await SourceAsync(db, "barred.example", ForbiddenReport("barred.example"));
            var oldLane = await RecipeAsync(db, "https://facts.example/chili/");
            var own = await RecipeAsync(db, "https://www.facts.example/stew/", OwnMethod);
            var barredRecipe = await RecipeAsync(db, "https://barred.example/soup/");
            var service = Rights(db);

            var dry = await service.DeriveAsync(apply: false);

            dry.Changed.Should().Be(2);
            dry.Results.Single(r => r.SourceId == facts.Id).Should().Match<SourceRightsChangeModel>(r => r.NewClass == SourceRightsRule.FactsOnly && r.NewAiReserved && r.Quote == "All rights reserved.");
            dry.Results.Single(r => r.SourceId == barred.Id).Quote.Should().Contain("robot or spider");
            (await db.ScrapingSources.AsNoTracking().CountAsync(s => s.RightsClass != null)).Should().Be(0, "a dry run writes nothing");

            var run = await service.DeriveAsync(apply: true);

            run.Changed.Should().Be(2);
            var sources = await db.ScrapingSources.AsNoTracking().ToDictionaryAsync(s => s.Id);
            sources[facts.Id].Should().Match<ScrapingSourceEntity>(s => s.RightsClass == SourceRightsRule.FactsOnly && s.AiReserved && s.RightsRuleVersion == SourceRightsRule.Version
                && s.AiReservedQuote!.Contains("GPTBot") && s.RightsQuoteUrl == "https://facts.example/");
            sources[barred.Id].Should().Match<ScrapingSourceEntity>(s => s.RightsClass == SourceRightsRule.Forbidden && s.RightsQuoteUrl == "https://barred.example/terms");
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == SourceJournal.EntityType).ToListAsync();
            journal.Should().Contain(a => a.EntityId == facts.Id && a.PropertyName == SourceJournal.RightsField && a.NewValue!.StartsWith(SourceRightsRule.FactsOnly));
            journal.Should().Contain(a => a.EntityId == facts.Id && a.PropertyName == SourceJournal.AiReservedField);
            journal.Should().Contain(a => a.EntityId == barred.Id && a.PropertyName == SourceJournal.RightsField && a.NewValue!.StartsWith(SourceRightsRule.Forbidden));

            var recipes = await db.Recipes.AsNoTracking().ToDictionaryAsync(r => r.Id);
            recipes[oldLane.Id].Should().Match<RecipeEntity>(r => r.SourceRights == SourceRightsRule.FactsOnly && r.SourceAiReserved && r.ProseWithheld);
            recipes[own.Id].Should().Match<RecipeEntity>(r => r.SourceRights == SourceRightsRule.FactsOnly && !r.ProseWithheld, "the clean-room lane's own method stays shown");
            recipes[barredRecipe.Id].Should().Match<RecipeEntity>(r => r.SourceRights == SourceRightsRule.Forbidden && !r.ProseWithheld && r.SourceWithheldById == null,
                "nothing happens by itself to recipes already taken from a forbidden source");
            (await db.AuditLogEntries.AsNoTracking().CountAsync(a => a.EntityId == oldLane.Id && a.ChangeType == SourceRightsService.FactsOnlyWithholdChangeType)).Should().Be(1);

            (await service.DeriveAsync(apply: true)).Changed.Should().Be(0, "the same report gives the same class");
        }

        [Fact]
        public async Task A_written_permission_lifts_the_class_and_its_undo_puts_the_facts_only_view_back()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var recipe = await RecipeAsync(db, "https://facts.example/chili/");
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);

            var refused = await rights.RecordPermissionAsync(source.Id, new SourcePermissionRequestModel { GrantedBy = "The editor", Text = "" }, Admin);
            refused.Ok.Should().BeFalse("a permission needs who, when and its text");

            var recorded = await rights.RecordPermissionAsync(source.Id, new SourcePermissionRequestModel { GrantedBy = "The editor", GrantedOn = T0.AddDays(-2), Text = "You may use our recipes in NOM." }, Admin);

            recorded.Ok.Should().BeTrue();
            recorded.Source!.EffectiveRightsClass.Should().Be(SourceRightsRule.Clear);
            recorded.Source.MayFetch.Should().BeTrue();
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id)).Should().Match<RecipeEntity>(r => r.SourceRights == SourceRightsRule.Clear && !r.ProseWithheld);
            var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == SourceJournal.EntityType && a.PropertyName == SourceJournal.PermissionField);

            var admission = new SourceAdmissionService(db, Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Options.Create(new SourceAdmissionSettings()), NullLogger<SourceAdmissionService>.Instance);
            (await admission.JournalAsync(50)).Single(e => e.Id == entry.Id).CanUndo.Should().BeTrue();
            var undone = await admission.UndoAsync(entry.Id, Admin);

            undone.Ok.Should().BeTrue();
            var after = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            after.PermissionRecordedAtUtc.Should().BeNull();
            after.PermissionText.Should().BeNull();
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id)).Should().Match<RecipeEntity>(r => r.SourceRights == SourceRightsRule.FactsOnly && r.ProseWithheld);
        }

        [Fact]
        public async Task Only_clear_sources_permissions_and_approved_facts_only_sources_are_harvested_or_seeded()
        {
            await using var db = NewContext();
            var clear = await SourceAsync(db, "clear.example", ClearReport("clear.example"));
            var facts = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var barred = await SourceAsync(db, "barred.example", ForbiddenReport("barred.example"));
            await SourceAsync(db, "unclassed.example", null);
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);
            var admission = new SourceAdmissionService(db, Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Options.Create(new SourceAdmissionSettings()),
                NullLogger<SourceAdmissionService>.Instance, Options.Create(new SourceRightsSettings { Enforce = true }));

            (await admission.HarvestableAsync()).Select(h => h.Domain).Should().Equal("clear.example");

            (await rights.SetFactsOnlyUseAsync(barred.Id, approve: true, Admin, null)).Ok.Should().BeFalse("facts-only use is only for a facts-only source");
            (await rights.SetFactsOnlyUseAsync(facts.Id, approve: true, Admin, "facts and a link only")).Ok.Should().BeTrue();
            (await rights.RecordPermissionAsync(barred.Id, new SourcePermissionRequestModel { GrantedBy = "Owner", GrantedOn = T0, Text = "Permission granted to harvest." }, Admin)).Ok.Should().BeTrue();

            var harvestable = await admission.HarvestableAsync();
            harvestable.Select(h => h.Domain).Should().Equal("barred.example", "clear.example", "facts.example");
            harvestable.Single(h => h.Domain == "facts.example").RightsClass.Should().Be(SourceRightsRule.FactsOnly);

            var approval = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.PropertyName == SourceJournal.FactsOnlyField);
            (await admission.UndoAsync(approval.Id, Admin)).Ok.Should().BeTrue();
            (await admission.HarvestableAsync()).Select(h => h.Domain).Should().NotContain("facts.example");

            var open = new SourceAdmissionService(db, Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Options.Create(new SourceAdmissionSettings()), NullLogger<SourceAdmissionService>.Instance);
            (await open.HarvestableAsync()).Should().HaveCount(4, "with the gate off (no configuration) the list is as before");
        }

        [Fact]
        public async Task The_pipeline_counts_as_harvestable_exactly_the_sources_the_harvestable_list_returns()
        {
            await using var db = NewContext();
            for (var i = 0; i < 22; i++) await SourceAsync(db, $"unclassed{i}.example", null);
            var admission = new SourceAdmissionService(db, Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Options.Create(new SourceAdmissionSettings()),
                NullLogger<SourceAdmissionService>.Instance, Options.Create(new SourceRightsSettings { Enforce = true }));

            (await admission.HarvestableAsync()).Should().BeEmpty("staging checkpoint 3, N28: no source has a rights class yet");
            (await admission.StatusAsync()).Harvestable.Should().Be(0, "the Pipeline said 22 while the list was empty");

            await SourceAsync(db, "clear.example", ClearReport("clear.example"));
            await Rights(db).DeriveAsync(apply: true);
            (await admission.StatusAsync()).Harvestable.Should().Be((await admission.HarvestableAsync()).Count).And.Be(1);
            var open = new SourceAdmissionService(db, Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Options.Create(new SourceAdmissionSettings()), NullLogger<SourceAdmissionService>.Instance);
            (await open.StatusAsync()).Harvestable.Should().Be(23, "with the gate off every approved web source counts, as before");
        }

        [Fact]
        public async Task A_url_import_never_fetches_and_a_staged_import_never_takes_from_a_barred_source()
        {
            await using var db = NewContext();
            await SourceAsync(db, "barred.example", ForbiddenReport("barred.example"));
            await Rights(db).DeriveAsync(apply: true);
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            var sources = new Mock<IScrapingSourceService>();
            sources.Setup(s => s.GetDomainStatusAsync(It.IsAny<string>())).ReturnsAsync(ScrapingSourceStatusEnum.Approved);
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.RightsGateSetting] = "true" }).Build();
            var service = new RecipeScrapingService(db, user.Object, scraper.Object, sources.Object, new RecipeVettingService(), NullLogger<RecipeScrapingService>.Instance, config);

            var answer = await service.ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel { Url = "https://barred.example/soup/" });

            answer.Success.Should().BeFalse();
            answer.Error.Should().Contain("forbidden").And.Contain("robot or spider");
            scraper.Verify(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            var staged = await service.ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes = new List<ScraperRecipe> { new() { Name = "Soup", SourceUrl = "https://barred.example/soup/", Ingredients = new List<ScraperIngredient> { new() { RawLine = "1 cup water" } } } },
            });

            staged.Imported.Should().Be(0);
            staged.Refused.Should().Be(1);
            staged.Failures.Single().Error.Should().Contain("forbidden");
        }

        [Fact]
        public async Task An_import_from_a_facts_only_source_is_stamped_withheld_titled_and_credited()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example", aiBlocked: true));
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);
            await rights.SetFactsOnlyUseAsync(source.Id, approve: true, Admin, null);
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.RightsGateSetting] = "true" }).Build();
            var service = new RecipeScrapingService(db, user.Object, Mock.Of<IRecipeScraperClient>(), Mock.Of<IScrapingSourceService>(), new RecipeVettingService(),
                NullLogger<RecipeScrapingService>.Instance, config);

            var result = await service.ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes = new List<ScraperRecipe>
                {
                    new()
                    {
                        Name = "The Best Weeknight Chili You'll Ever Make", SourceUrl = "https://facts.example/chili/", Description = "Adapted from Martha Stewart. So cozy.",
                        Ingredients = new List<ScraperIngredient> { new() { RawLine = "1 cup water" } },
                        Steps = new List<ScraperStep> { new() { Order = 1, Instruction = "Simmer everything." } },
                    },
                },
            });

            result.Imported.Should().Be(1);
            var recipe = await db.Recipes.AsNoTracking().SingleAsync();
            recipe.Should().Match<RecipeEntity>(r => r.Name == "Weeknight Chili" && r.SourceRights == SourceRightsRule.FactsOnly && r.SourceAiReserved && r.ProseWithheld
                && r.SourceCredit == "Adapted from Martha Stewart");
            var rows = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityId == recipe.Id && a.EntityType == "Recipe").ToListAsync();
            rows.Should().Contain(a => a.ChangeType == SourceRightsService.TitleChangeType && a.OldValue == "The Best Weeknight Chili You'll Ever Make");
            rows.Should().Contain(a => a.ChangeType == SourceRightsService.FactsOnlyWithholdChangeType);
        }

        [Fact]
        public async Task The_weekly_recheck_rederives_the_class_and_the_flag_and_journals_the_change()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"), s =>
            {
                s.CheckedAtUtc = T0.AddDays(-8);
                s.FactsOnlyApprovedAt = T0.AddDays(-8);
            });
            var recipe = await RecipeAsync(db, "https://facts.example/chili/");
            await Rights(db).DeriveAsync(apply: true);
            db.SourceChecks.Add(new SourceCheckEntity
            {
                ScrapingSourceId = source.Id,
                Domain = "facts.example", Purpose = SourceAdmissionService.RecheckPurpose, Mode = SourceRightsRule.TermsOnlyMode, Status = SourceCheckEntity.Completed,
                StartedUtc = T0.AddDays(-9), FinishedUtc = T0.AddDays(-9), RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion, Verdict = SourceAdmissionPolicy.NotClear,
                ReportJson = JsonSerializer.Serialize(SourceAdmissionServiceTests.TermsOnlyReport("facts.example"), Json),
            });
            await db.SaveChangesAsync();
            var report = ForbiddenReport("facts.example");
            report.Robots!.AiCrawlers.Add(new ScraperCheckAiCrawler { Agent = "CCBot", Rule = "blocked" });
            var job = JsonSerializer.SerializeToElement(new { jobId = "j1", kind = "source-check", status = "completed", stopReason = "none", result = report }, Json);
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.Setup(s => s.StartSourceCheckJobAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ScraperJobAnswer(ScraperJobOutcome.Ok, job.Deserialize<ScraperJob>(Json), null));
            var journalBefore = await db.AuditLogEntries.MaxAsync(a => a.Id);
            var admission = new SourceAdmissionService(db, scraper.Object, new RecipeVettingService(), Options.Create(new SourceAdmissionSettings { Enabled = true, FullChecks = true }), NullLogger<SourceAdmissionService>.Instance)
            {
                Now = () => T0,
                Delay = (_, _) => Task.CompletedTask,
            };

            var check = await admission.RunNextCheckAsync();

            check.Should().NotBeNull();
            var after = await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id);
            after.Should().Match<ScrapingSourceEntity>(s => s.RightsClass == SourceRightsRule.Forbidden && s.AiReserved && s.RightsCheckId == check!.Id);
            var changes = await db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == SourceJournal.EntityType && a.EntityId == source.Id && a.ChangeType == SourceJournal.Rights && a.Id > journalBefore).ToListAsync();
            changes.Select(c => c.PropertyName).Should().BeEquivalentTo(new[] { SourceJournal.RightsField, SourceJournal.AiReservedField });
            changes.Single(c => c.PropertyName == SourceJournal.RightsField).OldValue.Should().StartWith(SourceRightsRule.FactsOnly);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id)).Should().Match<RecipeEntity>(r => r.SourceRights == SourceRightsRule.Forbidden && r.SourceAiReserved && r.ProseWithheld,
                "the facts-only withhold stands while the source is forbidden; what happens to its recipes is the owner's decision");
            (await Rights(db).StatusAsync()).ChangesLast7d.Should().BeGreaterThanOrEqualTo(2);
        }

        [Fact]
        public async Task Titles_of_facts_only_recipes_are_reduced_by_rule_with_a_dry_run_and_an_undo_by_stamp()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var distinctive = await RecipeAsync(db, "https://facts.example/a/", r => r.Name = "The Ultimate Grilled Cheese");
            var plain = await RecipeAsync(db, "https://facts.example/b/", r => r.Name = "Apple Crisp");
            var edited = await RecipeAsync(db, "https://facts.example/c/", r => { r.Name = "Best Ever Banana Bread"; r.Version = 2; });
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);

            var dry = await rights.TitlesAsync(apply: false);

            dry.Changes.Should().ContainSingle(c => c.RecipeId == distinctive.Id && c.New == "Grilled Cheese");
            dry.SkippedByReason.Should().ContainKey("a person edited the recipe");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == distinctive.Id)).Name.Should().Be("The Ultimate Grilled Cheese");

            var run = await rights.TitlesAsync(apply: true);
            var names = await db.Recipes.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
            names[distinctive.Id].Should().Be("Grilled Cheese");
            names[plain.Id].Should().Be("Apple Crisp");
            names[edited.Id].Should().Be("Best Ever Banana Bread");

            (await rights.UndoStepAsync(SourceRightsService.TitlesStep, run.Stamp, apply: false)).Changed.Should().Be(1);
            (await rights.UndoStepAsync(SourceRightsService.TitlesStep, run.Stamp, apply: true)).Changed.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == distinctive.Id)).Name.Should().Be("The Ultimate Grilled Cheese");
            (await rights.UndoStepAsync(SourceRightsService.TitlesStep, run.Stamp, apply: true)).Changed.Should().Be(0, "an undone title is not undone twice");
        }

        [Fact]
        public async Task Third_party_credit_lines_are_stored_from_the_stored_source_and_shown_with_the_link()
        {
            await using var db = NewContext();
            var credited = await RecipeAsync(db, "https://facts.example/a/", jsonLd: JsonLd("A cozy pot. Adapted from Martha Stewart.", "Brown the beef."));
            var own = await RecipeAsync(db, "https://facts.example/b/", jsonLd: JsonLd("Adapted from my vanilla cupcakes.", "Mix."));
            var rights = Rights(db);

            var dry = await rights.CreditsAsync(apply: false);
            dry.Changes.Should().ContainSingle(c => c.RecipeId == credited.Id && c.New == "Adapted from Martha Stewart");

            var run = await rights.CreditsAsync(apply: true);
            var recipes = await db.Recipes.AsNoTracking().ToDictionaryAsync(r => r.Id);
            recipes[credited.Id].SourceCredit.Should().Be("Adapted from Martha Stewart");
            recipes[own.Id].SourceCredit.Should().BeNull("the author citing her own recipe is not a third-party credit");

            var detail = await new RecipeOrchestrationService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>()).GetRecipeAsync(credited.Id, Admin);
            detail!.SourceCredit.Should().Be("Adapted from Martha Stewart");
            detail.SourceUrl.Should().Be("https://facts.example/a/");

            (await rights.UndoStepAsync(SourceRightsService.CreditsStep, run.Stamp, apply: true)).Changed.Should().Be(1);
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == credited.Id)).SourceCredit.Should().BeNull();
        }

        [Fact]
        public async Task A_facts_only_recipe_shows_facts_and_the_link_with_no_publisher_line_or_method()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            db.Measurements.Add(new Nom.Data.Measurement.BaseMeasurementEntity { Id = 1, Name = "Pound", Symbol = "lb", MeasurementCategoryId = 1 });
            var food = new IngredientEntity { Name = "Ground beef", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            var recipe = await RecipeAsync(db, "https://facts.example/chili/", r =>
            {
                r.RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food.Id, Quantity = 1, MeasurementId = 1, RawLine = "1 pound ground beef, spooned and leveled", SortOrder = 0 } };
                r.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Brown", Description = "Brown the beef for 8 minutes in a 12-inch skillet." } };
            });
            await Rights(db).DeriveAsync(apply: true);

            var detail = await new RecipeOrchestrationService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>()).GetRecipeAsync(recipe.Id, Admin);

            detail!.FactsOnly.Should().BeTrue();
            detail.Description.Should().BeEmpty();
            detail.Steps.Should().BeEmpty();
            detail.Ingredients.Should().ContainSingle(i => i.Name == "Ground beef" && i.Notes == null, "the publisher's line wording is not shown, NOM's catalog name is");
            detail.KeyFacts.Should().NotBeNullOrEmpty("with no stored JSON-LD the key facts come from the stored steps by rule");
            detail.SourceUrl.Should().Be("https://facts.example/chili/");
        }

        [Fact]
        public async Task Retention_reduces_the_stored_prose_to_what_a_rebuild_needs_holds_it_for_the_undo_window_then_purges_it()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var rewritten = await RecipeAsync(db, "https://facts.example/a/", OwnMethod, JsonLd("Our family favourite.", "Brown the beef in a big pot over medium heat for 8 minutes."));
            var record = new ScraperRecipe
            {
                Name = "Weeknight Chili", SourceUrl = "https://facts.example/a/", Description = "Our family favourite.",
                Ingredients = new List<ScraperIngredient> { new() { RawLine = "1 pound ground beef", Quantity = 1, Unit = "pound" }, new() { RawLine = "1 can kidney beans" } },
                Steps = new List<ScraperStep> { new() { Order = 1, Instruction = "Brown the beef in a big pot over medium heat for 8 minutes." } },
                PrepTimeMinutes = 10,
            };
            var content = SourceRecord.Content(record);
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = rewritten.Id, SourceUrl = record.SourceUrl!, RawJsonLd = content, Format = ScrapedDocumentFormat.ScraperRecipe, ContentSha256 = SourceRecord.Sha256(content) });
            await db.SaveChangesAsync();
            var recent = await RecipeAsync(db, "https://facts.example/b/", r => { OwnMethod(r); r.ProseRewriteAttemptedAt = T0.AddDays(-3); }, JsonLd("New.", "Mix."));
            var waiting = await RecipeAsync(db, "https://facts.example/c/", r => { r.ProseWithheld = true; r.ProseLaneVersion = "lane-v"; r.ProseAttempts = 0; r.ProseRewriteAttemptedAt = T0.AddDays(-40); }, JsonLd("Old.", "Mix."));
            var gaveUp = await RecipeAsync(db, "https://facts.example/d/", r => { r.ProseWithheld = true; r.ProseLaneVersion = "lane-v"; r.ProseAttempts = ProseCleanRoom.AttemptCount; r.ProseRewriteAttemptedAt = T0.AddDays(-40); },
                JsonLd("Adapted from Julia Child.", "Stir the pot."));
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);

            var dry = await rights.RetentionAsync(apply: false);
            dry.Changes.Select(c => c.RecipeId).Should().BeEquivalentTo(new[] { rewritten.Id, gaveUp.Id }, "a recent rewrite and a recipe still waiting for the lane keep their source");
            (await db.ScrapedDocuments.CountAsync()).Should().Be(5, "a dry run changes nothing");

            var run = await rights.RetentionAsync(apply: true);

            run.Changed.Should().Be(2);
            var live = await db.ScrapedDocuments.AsNoTracking().Where(d => d.RecipeId == rewritten.Id).ToListAsync();
            live.Should().ContainSingle();
            var reduced = JsonSerializer.Deserialize<ScraperRecipe>(live[0].RawJsonLd, Json)!;
            live[0].Format.Should().Be(ScrapedDocumentFormat.ScraperRecipe);
            live[0].Version.Should().Be(2);
            live[0].RawJsonLd.Should().NotContain("family favourite").And.NotContain("big pot").And.Contain("\"reduced\":true");
            reduced.Description.Should().BeNull();
            reduced.Steps.Should().BeEmpty();
            reduced.Ingredients.Select(i => i.RawLine).Should().Equal("1 pound ground beef", "1 can kidney beans");
            reduced.PrepTimeMinutes.Should().Be(10);
            var gaveUpDocs = await db.ScrapedDocuments.AsNoTracking().Where(d => d.RecipeId == gaveUp.Id).ToListAsync();
            JsonSerializer.Deserialize<ScraperRecipe>(gaveUpDocs.Single().RawJsonLd, Json)!.Ingredients.Should().HaveCount(3, "a facts record is built from the JSON-LD's ingredient lines when no record was kept");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == gaveUp.Id)).SourceCredit.Should().Be("Adapted from Julia Child", "credits are read before the prose goes");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == gaveUp.Id)).RecipeSteps.Should().BeNull();
            (await db.RecipeSteps.AsNoTracking().CountAsync(s => s.RecipeId == gaveUp.Id)).Should().Be(1, "a given-up recipe keeps its withheld steps: the lane retries from them when its version changes");
            (await db.ScrapedDocuments.IgnoreQueryFilters().CountAsync(d => d.IsDeleted)).Should().Be(3, "the originals are held, not yet deleted");
            (await rights.RetentionAsync(apply: false)).Changed.Should().Be(0, "a reduced recipe is not reduced again");
            (await db.ScrapedDocuments.AsNoTracking().CountAsync(d => d.RecipeId == recent.Id || d.RecipeId == waiting.Id)).Should().Be(2);

            var undo = await rights.UndoStepAsync(SourceRightsService.RetentionStep, run.Stamp, apply: true);
            undo.Changed.Should().Be(2);
            (await db.ScrapedDocuments.AsNoTracking().CountAsync(d => d.RecipeId == rewritten.Id)).Should().Be(2);
            (await db.ScrapedDocuments.AsNoTracking().Where(d => d.RecipeId == rewritten.Id).Select(d => d.RawJsonLd).ToListAsync()).Should().Contain(d => d.Contains("big pot"));

            var again = await rights.RetentionAsync(apply: true);
            again.Changed.Should().Be(2);
            (await rights.PurgeRetentionAsync(apply: true)).Changed.Should().Be(0, "nothing is purged inside the undo window");
            var later = new SourceRightsService(db, Options.Create(new SourceRightsSettings()), NullLogger<SourceRightsService>.Instance) { Now = () => T0.AddDays(31) };

            var purge = await later.PurgeRetentionAsync(apply: true);

            purge.Changed.Should().Be(2);
            (await db.ScrapedDocuments.IgnoreQueryFilters().CountAsync(d => d.IsDeleted)).Should().Be(0);
            (await db.ScrapedDocuments.IgnoreQueryFilters().AnyAsync(d => d.RawJsonLd.Contains("big pot"))).Should().BeFalse("the publisher's prose is gone for good");
            var late = await later.UndoStepAsync(SourceRightsService.RetentionStep, again.Stamp, apply: true);
            late.Changed.Should().Be(0, "once purged, a reduction cannot be undone");
            late.SkippedByReason.Keys.Should().ContainSingle(k => k.Contains("purged"));
        }

        [Fact]
        public async Task Retention_reduces_every_journal_and_step_copy_of_the_publishers_prose_and_its_undo_puts_them_back()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var written = T0.AddDays(-40);
            var recipe = await RecipeAsync(db, "https://facts.example/a/", r =>
            {
                OwnMethod(r);
                r.Description = "A weeknight chili of beef and beans.";
                r.RecipeSteps = new List<RecipeStepEntity>
                {
                    new() { StepNumber = 1, Summary = "Brown the beef.", Description = "Brown the beef.", LastModifiedByPersonId = SystemConstants.SystemPersonId },
                    new() { StepNumber = 2, Summary = "Stir in the secret sauce.", Description = "Stir in our famous secret sauce and wait.", IsDeleted = true, LastModifiedByPersonId = SystemConstants.SystemPersonId },
                };
            }, JsonLd("Our family favourite.", "Brown the beef in a big pot over medium heat for 8 minutes.", "Stir in our famous secret sauce and wait."));
            const string tag = "[qwen lane-v] ";
            db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.DescriptionProperty, "Our family favourite.", tag + "A weeknight chili of beef and beans.", written));
            db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.StepPrefix + "1", "Brown the beef in a big pot over medium heat for 8 minutes.", tag + "Brown the beef.", written));
            db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.StepPrefix + "2", "Stir in our famous secret sauce and wait.", tag + "(removed)", written));
            db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.StateProperty,
                ProseLaneJournal.StateJson(new ProseState(true, false, 2, null, 0, null, new[] { 1, 2 })), tag + ProseLaneJournal.StateJson(new ProseState(false, false, 2, "lane-v", 0, written, new[] { 1 })), written));
            db.RecipeReprocessJournals.Add(new RecipeReprocessJournalEntity
            {
                RecipeId = recipe.Id, BatchId = "b1",
                NewSteps = JsonSerializer.Serialize(new[] { new Dictionary<string, object> { ["stepNumber"] = 1, ["summary"] = "Brown", ["description"] = "Brown the beef in a big pot over medium heat for 8 minutes." } }),
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);

            var dry = await rights.RetentionAsync(apply: false);
            dry.Changes.Single().Old.Should().Contain("6 journal and step copies", "three old journal values, the removed step's text and summary, and the rebuild journal's steps");

            var run = await rights.RetentionAsync(apply: true);

            run.Changed.Should().Be(1);
            var journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == "Recipe" && a.EntityId == recipe.Id).ToListAsync();
            journal.Should().NotContain(a => (a.OldValue ?? "").Contains("family favourite") || (a.OldValue ?? "").Contains("big pot") || (a.OldValue ?? "").Contains("secret sauce"));
            journal.Should().Contain(a => a.NewValue == tag + "A weeknight chili of beef and beans.", "the clean-room lane's own words stay");
            (await db.RecipeSteps.IgnoreQueryFilters().AsNoTracking().SingleAsync(st => st.RecipeId == recipe.Id && st.StepNumber == 2)).Description.Should().StartWith(SourceRightsService.ReducedValuePrefix);
            (await db.RecipeReprocessJournals.AsNoTracking().SingleAsync()).NewSteps.Should().NotContain("big pot");
            (await db.ScrapedDocuments.IgnoreQueryFilters().AsNoTracking().Where(d => !d.IsDeleted).Select(d => d.RawJsonLd).ToListAsync()).Should().NotContain(d => d.Contains("big pot"));

            var proseUndo = await new ProseRewriteUndoService(db, Mock.Of<ICurrentUserService>()).UndoAsync(T0.AddDays(-100), apply: true);
            proseUndo.Restored.Should().Be(0, "a rewrite whose old text was reduced cannot be undone into placeholders");
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id)).Description.Should().Be("A weeknight chili of beef and beans.");

            (await rights.UndoStepAsync(SourceRightsService.RetentionStep, run.Stamp, apply: true)).Changed.Should().Be(1);
            journal = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == "Recipe" && a.EntityId == recipe.Id).ToListAsync();
            journal.Should().Contain(a => a.OldValue == "Our family favourite.");
            (await db.RecipeSteps.IgnoreQueryFilters().AsNoTracking().SingleAsync(st => st.RecipeId == recipe.Id && st.StepNumber == 2)).Description.Should().Be("Stir in our famous secret sauce and wait.");
            (await db.RecipeReprocessJournals.AsNoTracking().SingleAsync()).NewSteps.Should().Contain("big pot");
            (await db.ScrapedDocuments.IgnoreQueryFilters().CountAsync(d => d.Format == ScrapedDocumentFormat.HeldProse)).Should().Be(0);

            await rights.RetentionAsync(apply: true);
            var later = new SourceRightsService(db, Options.Create(new SourceRightsSettings()), NullLogger<SourceRightsService>.Instance) { Now = () => T0.AddDays(31) };
            (await later.PurgeRetentionAsync(apply: true)).Changed.Should().Be(1);
            (await db.ScrapedDocuments.IgnoreQueryFilters().AnyAsync(d => d.RawJsonLd.Contains("big pot") || d.RawJsonLd.Contains("family favourite"))).Should().BeFalse();
            (await db.AuditLogEntries.AsNoTracking().AnyAsync(a => (a.OldValue ?? "").Contains("family favourite"))).Should().BeFalse("the publisher's prose is gone for good, journal copies included");
        }

        [Fact]
        public async Task A_reduced_recipe_still_rebuilds_from_its_facts_record()
        {
            await using var db = NewContext();
            db.Measurements.Add(new Nom.Data.Measurement.BaseMeasurementEntity { Id = 1, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2 });
            var water = new IngredientEntity { Name = "Water", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(water);
            await db.SaveChangesAsync();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var recipe = await RecipeAsync(db, "https://facts.example/a/", r =>
            {
                OwnMethod(r);
                r.CurationStatusId = (long)CurationStatusEnum.NonCurated;
                r.RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = water.Id, Quantity = 1, MeasurementId = 1, RawLine = "1 cup water", SortOrder = 0 } };
            }, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["@type"] = "Recipe", ["name"] = "Weeknight Chili", ["description"] = "Publisher words.",
                ["recipeIngredient"] = new[] { "1 cup water" }, ["recipeInstructions"] = new[] { new Dictionary<string, string> { ["@type"] = "HowToStep", ["text"] = "Boil the water." } },
            }));
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);
            (await rights.RetentionAsync(apply: true)).Changed.Should().Be(1);

            var rebuild = await new RecipeReprocessService(db, Mock.Of<ICurrentUserService>(), Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Mock.Of<IRecipeNutritionService>(),
                NullLogger<RecipeReprocessService>.Instance).RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = new List<long> { recipe.Id } });

            rebuild.Results.Single().Outcome.Should().NotBe(RecipeReprocessOutcome.Held, rebuild.Results.Single().Reason ?? string.Empty);
            rebuild.Results.Single().SourceFormat.Should().Be(ScrapedDocumentFormat.ScraperRecipe);
        }

        [Fact]
        public async Task A_quarantined_facts_only_recipe_is_withheld_so_no_reader_of_it_gets_the_publishers_text()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var quarantined = await RecipeAsync(db, "https://facts.example/chili/", r =>
            {
                r.ContainsSourceProse = true;
                r.CurationStatusId = (long)CurationStatusEnum.NonCurated;
            });

            await Rights(db).DeriveAsync(apply: true);

            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == quarantined.Id)).Should().Match<RecipeEntity>(r => r.ProseWithheld && r.ContainsSourceProse);
            var detail = await new RecipeOrchestrationService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>()).GetRecipeAsync(quarantined.Id, Admin);
            detail!.Description.Should().BeEmpty("the author still sees the quarantined recipe, but not the publisher's description");
            detail.Steps.Should().BeEmpty();
        }

        [Fact]
        public async Task A_facts_only_recipe_with_a_step_a_person_edited_is_withheld_too()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var edited = await RecipeAsync(db, "https://facts.example/chili/", r => r.RecipeSteps = new List<RecipeStepEntity>
            {
                new() { StepNumber = 1, Summary = "Brown", Description = "Brown the beef in a large pot.", LastModifiedByPersonId = SystemConstants.SystemPersonId },
                new() { StepNumber = 2, Summary = "Simmer", Description = "Simmer for 20 mintues.", LastModifiedByPersonId = Admin },
            });

            await Rights(db).DeriveAsync(apply: true);

            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == edited.Id)).ProseWithheld.Should().BeTrue("a typo fixed in one step does not make the publisher's other steps and description NOM's own");
        }

        [Fact]
        public async Task A_facts_only_withhold_is_not_counted_as_copy_unsure_whatever_mark_an_earlier_withhold_left()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var released = await RecipeAsync(db, "https://facts.example/chili/", r => r.ProseCopyUnsure = true);

            await Rights(db).DeriveAsync(apply: true);

            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == released.Id)).Should().Match<RecipeEntity>(r => r.ProseWithheld && !r.ProseCopyUnsure);
        }

        [Fact]
        public async Task A_facts_only_recipe_serves_no_publisher_line_wording_in_search_export_or_the_shopping_list()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            db.Measurements.Add(new Nom.Data.Measurement.BaseMeasurementEntity { Id = 1, Name = "Pound", Symbol = "lb", MeasurementCategoryId = 1 });
            var food = new IngredientEntity { Name = "Ground beef", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            const string wording = "1 pound ground beef, spooned and leveled";
            var recipe = await RecipeAsync(db, "https://facts.example/chili/", r =>
            {
                OwnMethod(r);
                r.Visibility = RecipeVisibilityEnum.Public;
                r.RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food.Id, Quantity = 1, MeasurementId = 1, RawLine = wording, SortOrder = 0 } };
            });
            await Rights(db).DeriveAsync(apply: true);

            var search = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);
            var found = await search.SearchRecipesAsync(new RecipeSearchModel { Query = "chili", IncludeIngredients = true });
            found.Results.Should().ContainSingle(r => r.Id == recipe.Id);
            JsonSerializer.Serialize(found).Should().NotContain("spooned and leveled").And.Contain("Ground beef");
            JsonSerializer.Serialize(await search.GetRecentRecipesAsync(10)).Should().NotContain("spooned and leveled");

            var shopping = new SmartShoppingListService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(), NullLogger<SmartShoppingListService>.Instance, new System.Net.Http.HttpClient());
            var list = await shopping.GenerateSmartShoppingListAsync(new Nom.Orch.Models.Shopping.SmartShoppingListRequestModel { RecipeIds = new List<long> { recipe.Id }, IncludePantryItems = false });
            list.Items.Should().NotBeEmpty();
            JsonSerializer.Serialize(list).Should().NotContain("spooned and leveled");

            var export = new RecipeBulkOperationsService(db, Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(), NullLogger<RecipeBulkOperationsService>.Instance);
            var result = await export.ExportRecipesAsync(new RecipeBulkExportModel { RecipeIds = new List<long> { recipe.Id }, RequesterPersonId = Admin, ExportType = ExportTypes.Json });
            result.Success.Should().BeTrue(string.Join(";", result.Errors));
            var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "exports", $"recipes_export_{result.ExportId}.json");
            try
            {
                (await System.IO.File.ReadAllTextAsync(path)).Should().NotContain("spooned and leveled").And.Contain("Ground beef");
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public async Task A_facts_only_line_on_a_food_named_from_its_own_text_shows_only_its_food_words_in_the_detail_search_export_and_shopping_list()
        {
            await using var db = NewContext();
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            db.Measurements.Add(new Nom.Data.Measurement.BaseMeasurementEntity { Id = 1, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 1 });
            const string sentence = "Tip-you can swap the oats for rice flakes and add raisins if you like!";
            const string extract = "optional: a few drops maple extract";
            var sentenceFood = new IngredientEntity { Name = sentence, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var extractFood = new IngredientEntity { Name = extract, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var catalogFood = new IngredientEntity { Name = "Peanut butter, smooth style, with salt", CurationStatusId = (long)CurationStatusEnum.PendingCuration, FdcId = "172470" };
            db.Ingredients.AddRange(sentenceFood, extractFood, catalogFood);
            await db.SaveChangesAsync();
            List<RecipeIngredientEntity> Lines() => new()
            {
                new() { IngredientId = catalogFood.Id, Quantity = 1, MeasurementId = 1, RawLine = "1 cup creamy peanut butter, the drippy kind", SortOrder = 0 },
                new() { IngredientId = extractFood.Id, Quantity = 0, MeasurementId = 1, RawLine = extract, SortOrder = 1 },
                new() { IngredientId = sentenceFood.Id, Quantity = 0, MeasurementId = 1, RawLine = sentence, SortOrder = 2 },
            };
            var recipe = await RecipeAsync(db, "https://facts.example/nests/", r =>
            {
                r.Name = "Peanut Butter Nests";
                OwnMethod(r);
                r.Visibility = RecipeVisibilityEnum.Public;
                r.RecipeIngredients = Lines();
            });
            var open = await RecipeAsync(db, "https://open.example/nests/", r =>
            {
                r.Name = "Open Peanut Butter Nests";
                r.Visibility = RecipeVisibilityEnum.Public;
                r.RecipeIngredients = Lines();
            });
            await Rights(db).DeriveAsync(apply: true);
            static void Shows(string json, string why) =>
                json.Should().NotContain("rice flakes", why).And.NotContain("few drops", why).And.Contain("drops maple extract", why).And.Contain(ProseWithholding.UnnamedFood, why)
                    .And.Contain("Peanut butter, smooth style, with salt", "a USDA catalog food keeps its own name");

            var detail = await new RecipeOrchestrationService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>()).GetRecipeAsync(recipe.Id, Admin);
            detail!.Ingredients.Select(i => i.Name).Should().Equal("Peanut butter, smooth style, with salt", "drops maple extract", ProseWithholding.UnnamedFood);
            var openDetail = await new RecipeOrchestrationService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IMediaStorageService>(), Mock.Of<IRecipeNutritionService>()).GetRecipeAsync(open.Id, Admin);
            openDetail!.Ingredients.Select(i => i.Name).Should().Contain(sentence, "a recipe of a source that is not facts-only keeps its food names");

            var search = new RecipeSearchOrchestrationService(db, NullLogger<RecipeSearchOrchestrationService>.Instance);
            var found = await search.SearchRecipesAsync(new RecipeSearchModel { Query = "Peanut Butter Nests", IncludeIngredients = true });
            found.Results.Should().Contain(r => r.Id == recipe.Id);
            Shows(JsonSerializer.Serialize(found.Results.Single(r => r.Id == recipe.Id)), "search");

            var shopping = new SmartShoppingListService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), Mock.Of<ICurrentUserService>(), NullLogger<SmartShoppingListService>.Instance, new System.Net.Http.HttpClient());
            var list = await shopping.GenerateSmartShoppingListAsync(new Nom.Orch.Models.Shopping.SmartShoppingListRequestModel { RecipeIds = new List<long> { recipe.Id }, IncludePantryItems = false });
            JsonSerializer.Serialize(list).Should().NotContain("rice flakes").And.NotContain("few drops");

            var export = new RecipeBulkOperationsService(db, Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(), NullLogger<RecipeBulkOperationsService>.Instance);
            var result = await export.ExportRecipesAsync(new RecipeBulkExportModel { RecipeIds = new List<long> { recipe.Id }, RequesterPersonId = Admin, ExportType = ExportTypes.Json });
            result.Success.Should().BeTrue(string.Join(";", result.Errors));
            var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "exports", $"recipes_export_{result.ExportId}.json");
            try
            {
                Shows(await System.IO.File.ReadAllTextAsync(path), "bulk export");
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public async Task An_unfinished_check_never_clears_an_ai_reservation_it_did_not_reach()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example", aiBlocked: true));
            var recipe = await RecipeAsync(db, "https://facts.example/chili/");
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);
            var unfinished = FactsOnlyReport("facts.example");
            unfinished.Complete = false;
            unfinished.IncompleteReason = "over-budget";
            var check = new SourceCheckEntity
            {
                Domain = "facts.example", Purpose = "recheck", ScrapingSourceId = source.Id, Status = SourceCheckEntity.Completed, StartedUtc = T0, FinishedUtc = T0,
                RuleVersion = unfinished.RuleVersion, Verdict = SourceAdmissionPolicy.NotClear, ReportJson = JsonSerializer.Serialize(unfinished, Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            (await db.ScrapingSources.SingleAsync(s => s.Id == source.Id)).LastCheckId = check.Id;
            await db.SaveChangesAsync();
            var journalBefore = await db.AuditLogEntries.CountAsync();

            var run = await rights.DeriveAsync(apply: true);

            run.Results.Single().Should().Match<SourceRightsChangeModel>(r => !r.Conclusive && r.NewAiReserved && !r.Changed);
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).Should().Match<ScrapingSourceEntity>(s => s.AiReserved && s.AiReservedQuote!.Contains("GPTBot"));
            (await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipe.Id)).SourceAiReserved.Should().BeTrue("no model may read its prose on the strength of a check that stopped early");
            (await db.AuditLogEntries.CountAsync()).Should().Be(journalBefore);
        }

        [Fact]
        public async Task An_undone_class_holds_against_the_stale_rederive_until_a_new_check_comes()
        {
            await using var db = NewContext();
            var source = await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);
            var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == SourceJournal.EntityType && a.PropertyName == SourceJournal.RightsField);
            var admission = new SourceAdmissionService(db, Mock.Of<IRecipeScraperClient>(), new RecipeVettingService(), Options.Create(new SourceAdmissionSettings()), NullLogger<SourceAdmissionService>.Instance);
            (await admission.UndoAsync(entry.Id, Admin)).Ok.Should().BeTrue();
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).RightsClass.Should().BeNull();

            var stale = await rights.DeriveAsync(apply: true, staleOnly: true);

            stale.Sources.Should().Be(0, "the undone class was derived from the same check, so the source lane's tick leaves it");
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).RightsClass.Should().BeNull();

            var check = new SourceCheckEntity
            {
                Domain = "facts.example", Purpose = "recheck", ScrapingSourceId = source.Id, Status = SourceCheckEntity.Completed, StartedUtc = T0, FinishedUtc = T0,
                RuleVersion = SourceAdmissionPolicy.ScraperRuleVersion, Verdict = SourceAdmissionPolicy.NotClear, ReportJson = JsonSerializer.Serialize(ForbiddenReport("facts.example"), Json),
            };
            db.SourceChecks.Add(check);
            await db.SaveChangesAsync();
            (await db.ScrapingSources.SingleAsync(s => s.Id == source.Id)).LastCheckId = check.Id;
            await db.SaveChangesAsync();

            (await rights.DeriveAsync(apply: true, staleOnly: true)).Changed.Should().Be(1, "a new check is derived again");
            (await db.ScrapingSources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).RightsClass.Should().Be(SourceRightsRule.Forbidden);
        }

        [Fact]
        public async Task A_bare_and_a_www_row_of_one_site_count_its_recipes_once_for_the_owner()
        {
            await using var db = NewContext();
            await SourceAsync(db, "barred.example", ForbiddenReport("barred.example"));
            await SourceAsync(db, "www.barred.example", ForbiddenReport("www.barred.example"));
            await RecipeAsync(db, "https://barred.example/a/");
            await RecipeAsync(db, "https://www.barred.example/b/");
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);

            var taken = await rights.TakenAsync();

            taken.Should().ContainSingle().Which.Recipes.Should().Be(2);
            (await rights.StatusAsync()).TakenFromBarredSources.Should().Be(2);
        }

        [Theory]
        [InlineData("Inspired by Thai street food, these noodles are tossed in a spicy peanut sauce and finished with lime.", null)]
        [InlineData("Adapted from Martha Stewart and made dairy-free with coconut milk.", "Adapted from Martha Stewart")]
        [InlineData("Recipe courtesy of Salt Fat Acid Heat by Samin Nosrat, with my notes.", "Recipe courtesy of Salt Fat Acid Heat by Samin Nosrat")]
        [InlineData("Adapted from my vanilla cupcakes.", null)]
        [InlineData("Reprinted with permission from Joy of Cooking, 1931 edition, page 12.", "Reprinted with permission from Joy of Cooking")]
        public void A_credit_line_keeps_only_the_credited_name_and_never_the_publishers_prose(string text, string? expected)
        {
            CreditLines.Join(new[] { text }).Should().Be(expected);
        }

        [Fact]
        public async Task The_dry_run_for_the_owner_counts_recipes_taken_from_barred_sources_and_changes_nothing()
        {
            await using var db = NewContext();
            await SourceAsync(db, "barred.example", ForbiddenReport("barred.example"));
            var needs = await SourceAsync(db, "needs.example", null);
            await SourceAsync(db, "facts.example", FactsOnlyReport("facts.example"));
            await RecipeAsync(db, "https://barred.example/a/");
            await RecipeAsync(db, "https://www.barred.example/b/");
            await RecipeAsync(db, "https://facts.example/c/");
            var rights = Rights(db);
            await rights.DeriveAsync(apply: true);
            var report = FactsOnlyReport("needs.example");
            report.Access!.HomepageOutcome = "HttpError";
            report.Access.HomepageStatus = 403;
            ScrapingSourceEntity needsSource = await db.ScrapingSources.SingleAsync(s => s.Id == needs.Id);
            SourceRightsService.ApplyVerdict(db, needsSource, SourceRightsRule.Derive(report), 0, T0, Admin);
            await db.SaveChangesAsync();
            var before = await db.Recipes.AsNoTracking().Select(r => new { r.Id, r.SourceWithheldById, r.ProseWithheld }).ToListAsync();

            var taken = await rights.TakenAsync();

            taken.Select(t => (t.Domain, t.RightsClass, t.Recipes)).Should().Equal(("barred.example", SourceRightsRule.Forbidden, 2), ("needs.example", SourceRightsRule.PermissionNeeded, 0));
            (await db.Recipes.AsNoTracking().Select(r => new { r.Id, r.SourceWithheldById, r.ProseWithheld }).ToListAsync()).Should().BeEquivalentTo(before);
            (await rights.StatusAsync()).TakenFromBarredSources.Should().Be(2);
        }
    }
}
