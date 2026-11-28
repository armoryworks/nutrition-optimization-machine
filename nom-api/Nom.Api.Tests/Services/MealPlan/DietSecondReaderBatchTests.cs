using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Import.Golden;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Plan item ck23 (U4 and the readings that travel): the second reader reads a batch with one model and then the same
    /// batch with the other, so the host that holds one model at a time loads each once a batch; a batch stopped between the
    /// models resumes with the second; its scope leaves out retired, withheld and forbidden-source recipes; its progress gives
    /// per model the readings of the last day and seconds from its own timed readings; and stored readings are exported and
    /// imported again only where the model, the prompt signature and the recipe's text signature match exactly, journaled,
    /// with a dry run and an undo, as are the offline scorer's answers.
    /// </summary>
    public class DietSecondReaderBatchTests
    {
        private const long CeliacRef = 60800, PersonId = 82, HouseholdId = 9;
        private const string Method = "Boil the rice in a large pot of water until it is tender.";

        private sealed class Host : HttpMessageHandler
        {
            public List<(string Model, string Body)> Calls { get; } = new();

            public Func<string, string, string> Answer { get; set; } = (_, _) => "free";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var model = doc.RootElement.GetProperty("model").GetString()!;
                var body = doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
                Calls.Add((model, body));
                var content = JsonSerializer.Serialize(new Dictionary<string, object> { ["unlisted"] = Array.Empty<string>(), ["complete"] = true }
                    .Concat(OllamaDietSecondReader.Types.Select(t => new KeyValuePair<string, object>(t.Key, new { food = "", answer = Answer(model, t.Key) })))
                    .ToDictionary(p => p.Key, p => p.Value));
                var reply = JsonSerializer.Serialize(new { message = new { role = "assistant", content } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Recipe(string name, long food) => new()
        {
            Name = name,
            AuthorId = PersonId,
            Visibility = RecipeVisibilityEnum.Public,
            CurationStatusId = (long)CurationStatusEnum.Curated,
            LicenseStatus = RecipeLicenseStatus.PublicDomain,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food, RawLine = "1 cup rice", Quantity = 1m, MeasurementId = 1 } },
            RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Boil", Description = Method } },
        };

        private static async Task<List<long>> SeedAsync(ApplicationDbContext db, params string[] names)
        {
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(rice);
            await db.SaveChangesAsync();
            var recipes = names.Select(n => Recipe(n, rice.Id)).ToList();
            db.Recipes.AddRange(recipes);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipes.Select(r => r.Id).ToList();
        }

        private static DietSecondReaderLaneService Lane(ApplicationDbContext db, Host host) =>
            new(db, new HttpClient(host), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DietSecondReader:OllamaUrl"] = "http://model.invalid:11434",
            }).Build());

        [Fact]
        public async Task A_batch_is_read_with_one_model_and_then_with_the_other_and_a_batch_stopped_between_them_resumes_with_the_second()
        {
            using var db = NewContext();
            var ids = await SeedAsync(db, "Boiled Rice", "Rice Water", "Rice Soup");
            var host = new Host();

            (await Lane(db, host).RunBatchAsync(3)).Should().Be(new DietSecondBatch(3, 6, 0));
            host.Calls.Select(c => c.Model).Should().Equal(Enumerable.Repeat(DietSecondGate.Pair[0], 3).Concat(Enumerable.Repeat(DietSecondGate.Pair[1], 3)),
                "plan item ck23, U4: one model swap a batch on a host that holds one model at a time, not one a recipe");

            using var other = NewContext();
            await SeedAsync(other, "Boiled Rice", "Rice Water");
            var stopping = new Host();
            var asked = 0;
            (await Lane(other, stopping).RunBatchAsync(10, () => Task.FromResult(++asked > 2))).Should().Be(new DietSecondBatch(2, 2, 0));
            stopping.Calls.Should().OnlyContain(c => c.Model == DietSecondGate.Pair[0]);
            stopping.Calls.Clear();
            (await Lane(other, stopping).RunBatchAsync(10)).Should().Be(new DietSecondBatch(2, 2, 0), "the first model's readings are stored, so only the second reads");
            stopping.Calls.Should().OnlyContain(c => c.Model == DietSecondGate.Pair[1]);
        }

        [Fact]
        public async Task The_scope_leaves_out_retired_withheld_and_forbidden_source_recipes_and_the_progress_measures_each_model()
        {
            using var db = NewContext();
            var ids = await SeedAsync(db, "Boiled Rice", "Retired Rice", "Withheld Rice", "Forbidden Rice");
            var recipes = await db.Recipes.IgnoreQueryFilters().Where(r => ids.Contains(r.Id)).ToListAsync();
            recipes[1].IsDeleted = true;
            recipes[2].SourceWithheldById = 1;
            recipes[3].SourceRights = SourceRightsRule.Forbidden;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var before = await Lane(db, new Host()).ProgressAsync();
            before.Approved.Should().Be(1, "plan item ck23: only recipes a household can see count (22,934 counted where 21,920 were the pool)");

            await Lane(db, new Host()).RunBatchAsync(10);
            var now = DateTime.UtcNow;
            foreach (var i in Enumerable.Range(0, 25))
                db.RecipeDietReadings.Add(new RecipeDietReadingEntity
                {
                    RecipeId = 1000 + i, Model = DietSecondGate.Pair[0], PromptSignature = OllamaDietSecondReader.Signature, TextSignature = "x", StoreVersion = DietSecondGate.Version,
                    Readable = true, Verdicts = "fffffffffff", Seconds = i < 20 ? 30 : 0, ReadAt = now.AddDays(-2).AddMinutes(i),
                });
            await db.SaveChangesAsync();
            var after = await Lane(db, new Host()).ProgressAsync();
            var first = after.PerModel.Single(m => m.Model == DietSecondGate.Pair[0]);
            first.ReadLast24h.Should().Be(1);
            first.LastReadAt.Should().NotBeNull();
            first.SecondsMeasured.Should().BeTrue();
            first.SecondsPerRecipe.Should().BeGreaterThan(20, "an imported reading without a time (0 s) is not a measure");
            after.PerModel.Single(m => m.Model == DietSecondGate.Pair[1]).SecondsMeasured.Should().BeFalse();
        }

        private static DietReadingImportItem ItemOf(DietReadingExport e) => new()
        {
            RecipeId = e.RecipeId, Model = e.Model, PromptSignature = e.PromptSignature, TextSignature = e.TextSignature, Readable = e.Readable, Complete = e.Complete,
            Unlisted = e.Unlisted.ToList(), Answers = e.Answers.ToDictionary(p => p.Key, p => p.Value), Seconds = e.Seconds, ReadAt = e.ReadAt,
        };

        [Fact]
        public async Task Readings_exported_from_one_copy_are_imported_only_where_the_model_and_both_signatures_match_journaled_and_undone()
        {
            using var made = NewContext();
            await SeedAsync(made, "Boiled Rice", "Rice Water");
            await Lane(made, new Host { Answer = (model, key) => key == "milk" ? "may" : "free" }).RunBatchAsync(10);
            var page = await Lane(made, new Host()).ExportAsync(0, 1);
            page.Recipes.Should().Be(1);
            page.Items.Should().HaveCount(2);
            page.Next.Should().NotBeNull();
            var rest = await Lane(made, new Host()).ExportAsync(page.Next!.Value, 10);
            rest.Next.Should().BeNull();
            var exported = page.Items.Concat(rest.Items).ToList();
            exported.Should().HaveCount(4).And.OnlyContain(e => e.Answers.Count == 11 && e.Answers["Milk Allergy"][0] == "may");

            using var copy = NewContext();
            var ids = await SeedAsync(copy, "Boiled Rice", "Rice Water");
            var step = await copy.RecipeSteps.SingleAsync(s => s.RecipeId == ids[1]);
            step.Description = "Simmer the rice in milk.";
            await copy.SaveChangesAsync();
            copy.ChangeTracker.Clear();
            var items = exported.Select(ItemOf).ToList();
            items.Add(new DietReadingImportItem { RecipeId = ids[0], Model = "qwen2.5:14b-instruct", PromptSignature = OllamaDietSecondReader.Signature, TextSignature = exported[0].TextSignature, Answers = exported[0].Answers.ToDictionary(p => p.Key, p => p.Value) });
            items.Add(new DietReadingImportItem { RecipeId = ids[0], Model = DietSecondGate.Pair[0], PromptSignature = "0000000000000000", TextSignature = exported[0].TextSignature, Answers = exported[0].Answers.ToDictionary(p => p.Key, p => p.Value) });

            var lane = Lane(copy, new Host());
            var dry = await lane.ImportAsync(items, apply: false, PersonId);
            dry.Taken.Should().Be(2, "the first recipe's two readings; the second recipe's method changed on this copy");
            dry.Skipped.Should().Contain(new KeyValuePair<string, int>(DietSecondReaderLaneService.SkipText, 2))
                .And.Contain(new KeyValuePair<string, int>(DietSecondReaderLaneService.SkipModel, 1));
            dry.Skipped.Keys.Should().Contain(new[] { DietSecondReaderLaneService.SkipTwice });
            (await copy.RecipeDietReadings.CountAsync()).Should().Be(0, "a dry run writes nothing");

            var applied = await lane.ImportAsync(exported.Select(ItemOf).ToList(), apply: true, PersonId);
            applied.Taken.Should().Be(2);
            var rows = await copy.RecipeDietReadings.ToListAsync();
            rows.Should().HaveCount(2).And.OnlyContain(r => r.RecipeId == ids[0] && r.Verdicts == "ffmffffffff" && r.TextSignature == exported[0].TextSignature);
            (await copy.AuditLogEntries.CountAsync(a => a.ChangeType == DietSecondReaderLaneService.ImportChange && a.Timestamp == applied.Stamp)).Should().Be(2);
            var progress = await Lane(copy, new Host()).ProgressAsync();
            progress.ApprovedReadByPair.Should().Be(1, "an imported reading is current for the lane as its own would be");

            (await Lane(copy, new Host()).ImportAsync(exported.Select(ItemOf).ToList(), apply: true, PersonId)).Skipped
                .Should().Contain(new KeyValuePair<string, int>(DietSecondReaderLaneService.SkipCurrent, 2), "a reading already current is never replaced");

            var dryUndo = await Lane(copy, new Host()).UndoImportAsync(applied.Stamp, apply: false, PersonId);
            dryUndo.Undone.Should().Be(2);
            (await copy.RecipeDietReadings.CountAsync()).Should().Be(2);
            (await Lane(copy, new Host()).UndoImportAsync(applied.Stamp, apply: true, PersonId)).Undone.Should().Be(2);
            (await copy.RecipeDietReadings.CountAsync()).Should().Be(0, "the readings the import added are removed; there was none before");
        }

        [Fact]
        public async Task The_offline_scorers_answers_import_by_their_signature_or_their_text_and_an_undo_skips_a_reading_made_since()
        {
            using var db = NewContext();
            var ids = await SeedAsync(db, "Boiled Rice");
            var recipe = new DietSecondRecipe("Boiled Rice", new[] { "1 cup rice" }, new[] { Method });
            var cases = new List<DietReaderCase> { new("dsr-x", ids[0], "Celiac Disease", DietReaderCase.Safe, "shown", "safe", 1, "draws", "agent-x", recipe) };
            var read = OllamaDietSecondReader.Read(JsonSerializer.Serialize(OllamaDietSecondReader.Types.ToDictionary(t => t.Key, t => (object)new { food = "", answer = "free" })
                .Append(new KeyValuePair<string, object>("complete", true)).ToDictionary(p => p.Key, p => p.Value)))!;
            var path = Path.Combine(Path.GetTempPath(), "dsr-answers-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                ModelLanes.WriteDietAnswers(path, new Dictionary<long, DietSecondRead?> { [ids[0]] = read }, DietSecondGate.Pair[0], cases);
                using var line = JsonDocument.Parse(File.ReadAllLines(path).Single());
                line.RootElement.GetProperty("model").GetString().Should().Be(DietSecondGate.Pair[0]);
                line.RootElement.GetProperty("prompt").GetString().Should().Be(OllamaDietSecondReader.Signature);
                var signed = line.RootElement.GetProperty("text").GetString();
                signed.Should().Be((await Lane(db, new Host()).TextsAsync(ids))[ids[0]].Signature, "the scorer signs the text it was shown as the lane signs the recipe");
                ModelLanes.ReadDietAnswers(path)[ids[0]].Should().NotBeNull("a reader of answers files ignores the new fields");
            }
            finally
            {
                File.Delete(path);
            }

            var answers = OllamaDietSecondReader.Types.ToDictionary(t => t.Type, t => new[] { "free", "" });
            var byText = new DietReadingImportItem
            {
                RecipeId = ids[0], Model = DietSecondGate.Pair[1], PromptSignature = OllamaDietSecondReader.Signature, Complete = true, Answers = answers,
                Text = new DietReadingText { Title = "Boiled  Rice", Lines = new() { "1 cup rice" }, Method = new() { Method } },
            };
            var report = await Lane(db, new Host()).ImportAsync(new[] { byText }, apply: true, PersonId);
            report.Taken.Should().Be(1, "an old answers file without a text signature is matched by the text it was read from, white space folded");
            var progress = await Lane(db, new Host()).ProgressAsync();
            progress.PerModel.Single(m => m.Model == DietSecondGate.Pair[1]).Should().Match<DietSecondModelProgress>(m => m.Current == 1 && m.ReadLast24h == 0 && m.LastReadAt == null,
                "an imported answer is current, but the lane did not read it");
            progress.LastReadAt.Should().BeNull();

            var row = await db.RecipeDietReadings.SingleAsync();
            row.Verdicts = "cffffffffff";
            await db.SaveChangesAsync();
            (await Lane(db, new Host()).UndoImportAsync(report.Stamp, apply: true, PersonId)).Should().Match<DietReadingUndoReport>(u => u.Undone == 0 && u.Skipped == 1);
            (await db.RecipeDietReadings.CountAsync()).Should().Be(1);
        }
    }
}
