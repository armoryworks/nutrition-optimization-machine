using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The AI equipment lane: model answers become AI-sourced tool links, uncommon equipment
    /// becomes a pending suggestion, and approval turns it into a catalog tool linked to every
    /// recipe it was seen in. Author-listed tools always beat AI tags.
    /// </summary>
    public class KitchenToolTaggingTests
    {
        private sealed class FakeTagger : IRecipeToolTagger
        {
            public Func<ToolTagCandidate, ToolTagResult> Answer { get; set; } = c => new ToolTagResult(c.RecipeId, Array.Empty<string>(), Array.Empty<ProposedTool>());
            public bool Fail { get; set; }
            public Func<IReadOnlyList<ToolTagCandidate>, bool>? BadAnswer { get; set; }
            public bool IsConfigured => true;
            public string ModelName => "fake:1b";

            public Task<IReadOnlyList<ToolTagResult>> TagAsync(IReadOnlyList<ToolTagCandidate> recipes, IReadOnlyDictionary<string, string> knownTools, CancellationToken cancellationToken = default)
            {
                if (Fail) throw new System.Net.Http.HttpRequestException("model down");
                if (BadAnswer?.Invoke(recipes) == true) throw new System.Text.Json.JsonException("bad shape");
                return Task.FromResult<IReadOnlyList<ToolTagResult>>(recipes.Select(Answer).ToList());
            }
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static (KitchenToolTaggingService lane, FakeTagger tagger) NewLane(ApplicationDbContext db)
        {
            var tagger = new FakeTagger();
            var lane = new KitchenToolTaggingService(db, tagger, new KitchenToolService(db), NullLogger<KitchenToolTaggingService>.Instance);
            return (lane, tagger);
        }

        private static async Task<RecipeEntity> AddRecipeAsync(ApplicationDbContext db, string name, string step)
        {
            var recipe = new RecipeEntity { Name = name };
            recipe.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Cook", Description = step } };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return recipe;
        }

        [Theory]
        [InlineData("Pizza Stones", "pizza stone")]
        [InlineData("a large Bundt pan", "bundt pan")]
        [InlineData("tortilla press (cast iron)", "tortilla press")]
        [InlineData("Mixing Bowls", null)]
        [InlineData("oven-safe skillet", null)]
        [InlineData("sheet-pan", null)]
        [InlineData("large rimmed baking sheet", null)]
        [InlineData("kitchen shears", null)]
        [InlineData("9-inch tart pan", null)]
        [InlineData("x", null)]
        public void Normalizes_equipment_nouns_and_drops_everyday_kit(string raw, string? expected)
        {
            ToolNameNormalizer.Normalize(raw).Should().Be(expected);
        }

        [Fact]
        public async Task Tags_known_tools_and_queues_uncommon_equipment()
        {
            using var db = NewContext();
            var pizza = await AddRecipeAsync(db, "Neapolitan Pizza", "Slide onto the hot stone in a 550°F oven.");
            var (lane, tagger) = NewLane(db);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, new[] { "oven", "nonsense-key" },
                new[] { new ProposedTool("Pizza Stones", KitchenToolCatalog.CategoryCookware), new ProposedTool("mixing bowl", null) });

            var result = await lane.TagNextBatchAsync(5);

            result!.Tagged.Should().Be(1);
            var links = await db.RecipeTools.Where(rt => rt.RecipeId == pizza.Id).ToListAsync();
            links.Should().ContainSingle(l => l.ToolId == KitchenToolCatalog.Oven && l.Source == "ai:fake:1b");
            var suggestion = await db.KitchenToolSuggestions.Include(s => s.Recipes).SingleAsync();
            suggestion.Name.Should().Be("pizza stone");
            suggestion.SeenCount.Should().Be(1);
            suggestion.Recipes.Should().ContainSingle(r => r.RecipeId == pizza.Id);
            (await db.Recipes.FindAsync(pizza.Id))!.ToolsTaggedAt.Should().NotBeNull();

            (await lane.TagNextBatchAsync(5))!.Seen.Should().Be(0, "tagged recipes are not revisited");
        }

        [Fact]
        public async Task Specialty_tools_need_textual_evidence_everyday_tools_do_not()
        {
            using var db = NewContext();
            var bowl = await AddRecipeAsync(db, "Rice Bowl", "Rinse and cook the jasmine rice. Simmer the beans.");
            var (lane, tagger) = NewLane(db);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, new[] { "rice-cooker", "stovetop" }, Array.Empty<ProposedTool>());

            await lane.TagNextBatchAsync(5);

            (await db.RecipeTools.Select(rt => rt.ToolId).ToListAsync()).Should().Equal(KitchenToolCatalog.Stovetop);
        }

        [Fact]
        public async Task A_proposed_name_that_is_already_a_tool_links_that_tool()
        {
            using var db = NewContext();
            var recipe = await AddRecipeAsync(db, "Pot Roast", "Cook low and slow in the crock-pot.");
            var (lane, tagger) = NewLane(db);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, Array.Empty<string>(), new[] { new ProposedTool("Crock-Pot", null) });

            await lane.TagNextBatchAsync(5);

            (await db.RecipeTools.SingleAsync()).ToolId.Should().Be(KitchenToolCatalog.SlowCooker);
            (await db.KitchenToolSuggestions.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Cast_iron_proposals_link_the_catalog_skillet()
        {
            using var db = NewContext();
            await AddRecipeAsync(db, "Cornbread", "Heat the fat in a 10-inch cast-iron skillet.");
            var (lane, tagger) = NewLane(db);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, Array.Empty<string>(), new[] { new ProposedTool("10-inch cast iron skillet", null), new ProposedTool("cast iron skillet", null) });

            await lane.TagNextBatchAsync(5);

            (await db.RecipeTools.SingleAsync()).ToolId.Should().Be(KitchenToolCatalog.CastIronSkillet);
            (await db.KitchenToolSuggestions.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Model_failure_leaves_recipes_untagged()
        {
            using var db = NewContext();
            var recipe = await AddRecipeAsync(db, "Toast", "Toast the bread.");
            var (lane, tagger) = NewLane(db);
            tagger.Fail = true;

            (await lane.TagNextBatchAsync(5)).Should().BeNull();
            (await db.Recipes.FindAsync(recipe.Id))!.ToolsTaggedAt.Should().BeNull();
        }

        [Fact]
        public async Task Approval_creates_a_tool_backfills_recipes_and_households_see_it_unticked()
        {
            using var db = NewContext();
            db.Set<ReferenceGroupEntity>().Add(new ReferenceGroupEntity { Id = (long)ReferenceDiscriminatorEnum.KitchenToolType, Name = "Kitchen Tools" });
            var a = await AddRecipeAsync(db, "Flour Tortillas", "Press each ball flat.");
            var b = await AddRecipeAsync(db, "Corn Tortillas", "Press the masa.");
            var (lane, tagger) = NewLane(db);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, Array.Empty<string>(), new[] { new ProposedTool("tortilla press", KitchenToolCatalog.CategoryGadgets) });
            await lane.TagNextBatchAsync(5);
            var suggestion = await db.KitchenToolSuggestions.SingleAsync();
            suggestion.SeenCount.Should().Be(2);

            var approved = await lane.ApproveAsync(suggestion.Id, new KitchenToolSuggestionApproveModel(), personId: 1);

            approved!.RecipesLinked.Should().Be(2);
            var links = await db.RecipeTools.Where(rt => rt.ToolId == approved.ToolId).ToListAsync();
            links.Select(l => l.RecipeId).Should().BeEquivalentTo(new[] { a.Id, b.Id });
            links.Should().OnlyContain(l => l.Source == KitchenToolTaggingService.ApprovalSource);

            var settings = await new KitchenToolService(db).GetSettingsAsync(householdId: 3);
            settings.Tools.Should().ContainSingle(t => t.Id == approved.ToolId && t.Name == "Tortilla press" && !t.Owned && t.Category == KitchenToolCatalog.CategoryGadgets);

            (await lane.ApproveAsync(suggestion.Id, new KitchenToolSuggestionApproveModel(), 1)).Should().BeNull("only pending suggestions can be approved");
        }

        [Fact]
        public async Task A_bad_answer_retries_singly_and_never_blocks_the_queue()
        {
            using var db = NewContext();
            var good = await AddRecipeAsync(db, "Soup", "Simmer for 20 minutes.");
            var poison = await AddRecipeAsync(db, "Weird", "Something the model chokes on.");
            var (lane, tagger) = NewLane(db);
            tagger.BadAnswer = batch => batch.Any(r => r.RecipeId == poison.Id);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, new[] { "stovetop" }, Array.Empty<ProposedTool>());

            var result = await lane.TagNextBatchAsync(5);

            result!.Tagged.Should().Be(2, "the poison recipe is skipped, not retried forever");
            (await db.RecipeTools.SingleAsync()).RecipeId.Should().Be(good.Id);
            (await lane.TagNextBatchAsync(5))!.Seen.Should().Be(0);
        }

        [Fact]
        public async Task Rejected_suggestions_stop_collecting()
        {
            using var db = NewContext();
            await AddRecipeAsync(db, "One", "Use the gizmo.");
            var (lane, tagger) = NewLane(db);
            tagger.Answer = c => new ToolTagResult(c.RecipeId, Array.Empty<string>(), new[] { new ProposedTool("gizmo whirler", null) });
            await lane.TagNextBatchAsync(5);
            var suggestion = await db.KitchenToolSuggestions.SingleAsync();
            (await lane.RejectAsync(suggestion.Id)).Should().BeTrue();

            await AddRecipeAsync(db, "Two", "Use the gizmo again.");
            await lane.TagNextBatchAsync(5);

            (await db.KitchenToolSuggestions.SingleAsync()).SeenCount.Should().Be(1);
        }

        private sealed class CannedHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly string _modelJson;
            public CannedHandler(string modelJson) => _modelJson = modelJson;

            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = System.Net.Http.Json.JsonContent.Create(new { response = _modelJson }),
                });
        }

        [Theory]
        [InlineData("{\"recipes\":[{\"n\":1,\"tools\":[\"oven\",\"made-up\"],\"other\":[\"pizza peel\"]}]}")]
        [InlineData("{\"recipes\":[{\"n\":1,\"tools\":[\"Oven\"],\"other\":[{\"name\":\"pizza peel\",\"category\":\"nonsense\"}]}]}")]
        public async Task Tagger_accepts_string_or_object_proposals(string modelJson)
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:OllamaUrl"] = "http://ollama.test" })
                .Build();
            var tagger = new OllamaRecipeToolTagger(new System.Net.Http.HttpClient(new CannedHandler(modelJson)), config);

            var result = await tagger.TagAsync(
                new[] { new ToolTagCandidate(7, "Pizza", "Bake on the stone.") },
                new Dictionary<string, string> { ["oven"] = "Oven" });

            var only = result.Should().ContainSingle().Subject;
            only.KnownToolKeys.Should().Equal("oven");
            only.Other.Should().ContainSingle(o => o.Name == "pizza peel" && o.Category == null);
        }

        [Fact]
        public void Author_tools_beat_ai_tags_and_ai_tags_join_keyword_detection()
        {
            var authored = KitchenToolEvaluator.RequiredTools(
                new[] { new LinkedTool(KitchenToolCatalog.Grill, null), new LinkedTool(KitchenToolCatalog.SousVide, "ai:x") },
                "Cake", new[] { "Bake at 350°F." });
            authored.Should().BeEquivalentTo(new[] { KitchenToolCatalog.Grill });

            var aiOnly = KitchenToolEvaluator.RequiredTools(
                new[] { new LinkedTool(KitchenToolCatalog.StandMixer, "ai:x") }, "Cake", new[] { "Bake at 350°F." });
            aiOnly.Should().BeEquivalentTo(new[] { KitchenToolCatalog.StandMixer, KitchenToolCatalog.Oven });
        }

        [Fact]
        public void Approved_tools_are_detected_by_name_in_recipe_text()
        {
            var tools = new Dictionary<long, KitchenToolDefinition>(KitchenToolCatalog.ById)
            {
                [90001] = KitchenToolCatalog.Approved(90001, "Tortilla press", KitchenToolCatalog.CategoryGadgets),
            };
            KitchenToolCatalog.Detect("Tacos", new[] { "Flatten in a tortilla-press." }, tools).Should().Contain(90001);
        }
    }
}
