using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nom.Import.Golden;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The second diet reader (plan item ck20) and its golden lane diet-second-reader: the reader asks
    /// once per recipe for the eleven severe types and reads only a whole, well-formed reply; the
    /// lane's rows are cookbook recipes judged by agent reviewers, split by recipe; its scorer counts a
    /// contains row read free as a false accept and leaves doubtful rows out; and the reader ships
    /// unwired, unscored and unable to write.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenDietSecondReaderTests
    {
        private static string Reply(Func<string, string>? word = null, bool complete = true) =>
            JsonSerializer.Serialize(new Dictionary<string, object> { ["unlisted"] = new[] { "pie crust: wheat flour" }, ["complete"] = complete }
                .Concat(OllamaDietSecondReader.Types.Select(t => new KeyValuePair<string, object>(t.Key, new { food = "", answer = word?.Invoke(t.Key) ?? "free" })))
                .ToDictionary(p => p.Key, p => p.Value));

        [Fact]
        public void The_reader_asks_for_exactly_the_eleven_severe_types_of_the_diet_vocabulary()
        {
            OllamaDietSecondReader.Types.Select(t => t.Type).Should().BeEquivalentTo(DietVocabulary.Classes.Select(c => c.ReferenceType));
            OllamaDietSecondReader.Types.Select(t => t.Key).Should().OnlyHaveUniqueItems().And.HaveCount(11);
        }

        [Fact]
        public void A_whole_reply_is_read_per_type_a_recipe_found_not_whole_is_never_free_and_a_reply_missing_a_type_or_with_an_unknown_word_is_not_read_at_all()
        {
            var read = OllamaDietSecondReader.Read(Reply(k => k == "egg" ? "contains" : k == "milk" ? "may" : "free"))!;
            read.Unlisted.Should().Equal("pie crust: wheat flour");
            var answers = read.Answers;
            answers.Should().HaveCount(11);
            answers.Single(a => a.Type == "Egg Allergy").Reading.Should().Be(DietSecondReading.Contains);
            answers.Single(a => a.Type == "Milk Allergy").Reading.Should().Be(DietSecondReading.May);
            answers.Where(a => a.Type is not ("Egg Allergy" or "Milk Allergy")).Should().OnlyContain(a => a.Reading == DietSecondReading.Free);

            read.Complete.Should().BeTrue();
            var partial = OllamaDietSecondReader.Read(Reply(k => k == "egg" ? "contains" : "free", complete: false))!;
            partial.Complete.Should().BeFalse();
            partial.Answers.Single(a => a.Type == "Egg Allergy").Reading.Should().Be(DietSecondReading.Contains);
            partial.Answers.Where(a => a.Type != "Egg Allergy").Should().OnlyContain(a => a.Reading == DietSecondReading.May, "a recipe the reader finds is not whole is never free");
            OllamaDietSecondReader.Read(Reply(k => k == "soy" ? "probably" : "free")).Should().BeNull();
            var missing = JsonSerializer.Serialize(OllamaDietSecondReader.Types.Skip(1).ToDictionary(t => t.Key, _ => new { food = "", answer = "free" }));
            OllamaDietSecondReader.Read(missing).Should().BeNull();
            OllamaDietSecondReader.Read("not json").Should().BeNull();
            OllamaDietSecondReader.Read("[]").Should().BeNull();
        }

        [Fact]
        public void The_reader_is_shown_the_title_lines_and_method_only_and_a_long_method_is_cut()
        {
            var recipe = new DietSecondRecipe("Leek  Pie", new[] { "2 leeks", " ", "butter" }, new[] { "Line a dish with paste.", new string('x', OllamaDietSecondReader.MaxMethodChars) });
            var body = OllamaDietSecondReader.Body(recipe);

            body.Should().StartWith("Title: Leek Pie\nIngredient lines:\n- 2 leeks\n- butter\nMethod:\nLine a dish with paste.\n");
            body.Should().EndWith(" ...");
            body.Length.Should().BeLessThan(OllamaDietSecondReader.MaxMethodChars + 100);
            OllamaDietSecondReader.Body(new DietSecondRecipe("Water", Array.Empty<string>(), Array.Empty<string>())).Should().Be("Title: Water\nIngredient lines:\n(none)\nMethod:\n(none)");
        }

        [Fact]
        public void The_golden_rows_are_judged_agent_rows_of_cookbook_recipes_with_a_known_truth_verdict_and_kind()
        {
            var rows = GoldenSet.Read(ModelLanes.DietSecondReaderFile).Rows;
            var cases = ModelLanes.DietReaderCases(GoldenSet.Folder);

            rows.Should().HaveCountGreaterThan(450);
            rows.Select(r => r["status"]).Should().OnlyContain(s => s == GoldenSet.Agent, "every truth was judged by an agent reviewer, never set by a model's answer");
            rows.Select(r => r["source"]).Should().OnlyContain(s => GoldenSet.ByModel(s));
            cases.Select(c => c.Truth).Should().OnlyContain(t => t == DietReaderCase.Contains || t == DietReaderCase.Safe || t == DietReaderCase.Doubtful);
            cases.Select(c => c.Rules).Should().OnlyContain(r => r == "shown" || r == "flagged" || r == "unknown");
            cases.Select(c => c.Type).Should().OnlyContain(t => OllamaDietSecondReader.Types.Any(x => x.Type == t));
            cases.Should().OnlyContain(c => c.Recipe.Title.Length > 0 && c.Recipe.Lines.Count > 0);
            cases.Where(c => c.Rules == "shown" && c.Truth == DietReaderCase.Contains).Should().Contain(c => DietReaderReport.HiddenKinds.Contains(c.Kind))
                .And.Contain(c => c.Kind == "referral");
            cases.Where(c => c.Rules == "unknown").Select(c => c.Truth).Distinct().Should().Contain(new[] { DietReaderCase.Safe, DietReaderCase.Contains });
            rows.Select(r => r.Id).Should().OnlyContain(id => id.StartsWith("dsr-", StringComparison.Ordinal));
        }

        [Fact]
        public void The_rows_split_by_recipe_into_two_fixed_halves()
        {
            var cases = ModelLanes.DietReaderCases(GoldenSet.Folder);
            var working = cases.Where(c => ModelLanes.InDietSplit(c, ModelLanes.SplitWorking)).ToList();
            var heldOut = cases.Where(c => ModelLanes.InDietSplit(c, ModelLanes.SplitHeldOut)).ToList();

            working.Count.Should().BeGreaterThan(cases.Count / 3);
            heldOut.Count.Should().BeGreaterThan(cases.Count / 3);
            (working.Count + heldOut.Count).Should().Be(cases.Count);
            working.Select(c => c.RecipeId).Should().NotIntersectWith(heldOut.Select(c => c.RecipeId));
            ModelLanes.Splittable.Should().Contain(ModelLanes.DietSecondReaderLane);
        }

        [Fact]
        public async Task The_scorer_asks_once_per_recipe_and_counts_a_contains_row_read_free_as_a_false_accept()
        {
            var folder = Path.Combine(Path.GetTempPath(), "dsr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllLines(Path.Combine(folder, ModelLanes.DietSecondReaderFile), new[]
                {
                    "id\trecipe\ttype\ttitle\tlines\tmethod\ttruth\trules\tkind\tweight\tframe\tstatus\tsource\tnote",
                    "dsr-1-egg\t1\tEgg Allergy\tFrittata\tpotatoes ¦ butter\tFry.\tcontains\tshown\ttitle\t\tdraws\tagent\tagent-x\t",
                    "dsr-1-milk\t1\tMilk Allergy\tFrittata\tpotatoes ¦ butter\tFry.\tcontains\tshown\tword\t\tdraws\tagent\tagent-x\t",
                    "dsr-2-egg\t2\tEgg Allergy\tStewed Rhubarb\trhubarb ¦ sugar\tStew.\tsafe\tshown\tsafe\t10\tdraws\tagent\tagent-x\t",
                    "dsr-3-celiac\t3\tCeliac Disease\tMutton Broth\tmutton ¦ water\tBoil.\tsafe\tunknown\tunknown\t\t\tagent\tagent-x\t",
                    "dsr-4-celiac\t4\tCeliac Disease\tSauce\tbutter\tAs No. 3.\tdoubtful\tshown\treferral\t\tdraws\tagent\tagent-x\t",
                });
                var handler = new ScriptedHandler(title => title switch
                {
                    "Frittata" => Reply(k => k == "milk" ? "contains" : "free"),
                    "Stewed Rhubarb" => Reply(k => k == "egg" ? "may" : "free"),
                    "Sauce" => "{}",
                    _ => Reply(),
                });
                using var http = new HttpClient(handler);

                var run = await ModelLanes.DietReaderRunAsync(folder, http, "http://model.invalid", "m", null, _ => { }, CancellationToken.None);
                var results = ModelLanes.DietReaderResults(run).ToDictionary(r => r.Id);

                handler.Titles.Should().Equal("Frittata", "Stewed Rhubarb", "Mutton Broth", "Sauce");
                results.Keys.Should().BeEquivalentTo(new[] { "dsr-1-egg", "dsr-1-milk", "dsr-2-egg", "dsr-3-celiac" }, "a doubtful row is not scored");
                results["dsr-1-egg"].Should().Match<LaneCaseResult>(r => !r.Pass && r.FalseAccept && r.CanFalseAccept && r.Actual == "free");
                results["dsr-1-milk"].Should().Match<LaneCaseResult>(r => r.Pass && !r.FalseAccept);
                results["dsr-2-egg"].Should().Match<LaneCaseResult>(r => !r.Pass && !r.CanFalseAccept && r.Actual == "may");
                results["dsr-3-celiac"].Pass.Should().BeTrue();
                run.Answers[4].Should().BeNull();

                var report = DietReaderReport.Lines(run);
                report.Should().Contain(l => l.StartsWith("all | 1/2 | 100.0 % of 1 | 1/1 | 0/0", StringComparison.Ordinal));
                report.Should().Contain("Draws frame: 13 weighted shown picks (4 rows read)");
                report.Should().Contain(l => l.Contains("by rules and reader both clear 1 in 1 weighted picks still shown (100.00 %", StringComparison.Ordinal) && l.EndsWith("not met", StringComparison.Ordinal));
                report.Should().Contain(l => l.Contains("shown picks the reader also calls clear 7.7 %", StringComparison.Ordinal));

                var answers = Path.Combine(folder, "answers.jsonl");
                ModelLanes.WriteDietAnswers(answers, run.Answers);
                var again = ModelLanes.ReadDietAnswers(answers);
                again[1]!.Unlisted.Should().Equal("pie crust: wheat flour");
                again[1]!.Answers.Single(a => a.Type == "Milk Allergy").Reading.Should().Be(DietSecondReading.Contains);
                again[4].Should().BeNull();
                var replay = ModelLanes.DietReaderReplay(folder, again);
                ModelLanes.DietReaderResults(replay).Should().BeEquivalentTo(ModelLanes.DietReaderResults(run));
                replay.Seconds.Should().BeEmpty();
                DietReaderReport.Lines(run, again).Should().Contain(l => l.StartsWith("Both readers clear on unknown holds: plainly safe 1/1; not safe 0/0", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void The_lane_ships_unscored_and_not_required_and_its_signature_moves_with_the_prompt()
        {
            var floor = ModelLanes.Floors(GoldenSet.Folder)[ModelLanes.DietSecondReaderLane];

            floor.Scorer.Should().BeTrue();
            floor.Required.Should().BeFalse("no bar is set until the owner sets one");
            ModelLanes.Recorded(GoldenSet.Folder, ModelLanes.DietSecondReaderLane).Should().BeNull("no record is committed until the owner sets a bar");
            ModelLanes.Unscored(GoldenSet.Folder).Should().Contain(ModelLanes.DietSecondReaderLane);
            OllamaDietSecondReader.Signature.Should().HaveLength(16);
        }

        [Fact]
        public void The_reader_is_taken_only_by_its_shadow_lane_and_holds_nothing_that_could_write()
        {
            var reader = typeof(OllamaDietSecondReader);
            var assemblies = new[] { reader.Assembly, typeof(Nom.Api.Controllers.DietAdminController).Assembly, typeof(Nom.Data.ApplicationDbContext).Assembly };
            var users = UsersOf(reader, assemblies.SelectMany(a => a.GetTypes()));

            users.Where(u => !IsOf(u, typeof(DietSecondReaderLaneService)) && !IsOf(u, typeof(DietSecondGate))).Should().BeEmpty(
                "plan item ck21: only the shadow lane builds the reader, and the stored gate reads its prompt signature and type order only; no other service, lane or gate takes it");
            users.Should().Contain(typeof(DietSecondReaderLaneService).FullName).And.Contain(typeof(DietSecondGate).FullName);
            ConstructorsOf(reader, assemblies.SelectMany(a => a.GetTypes())).Should().NotBeEmpty().And.OnlyContain(u => IsOf(u, typeof(DietSecondReaderLaneService)), "only the lane builds a reader");
            reader.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(HttpClient), typeof(string), typeof(string));
            reader.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType).Should().OnlyContain(t => t == typeof(HttpClient) || t == typeof(string));
        }

        [Fact]
        public void The_wiring_check_finds_a_reader_built_inside_a_method_or_registered_for_injection()
        {
            static IEnumerable<Type> WithNested(Type t) => new[] { t }.Concat(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested));
            var reader = typeof(OllamaDietSecondReader);

            UsersOf(reader, WithNested(typeof(BuildsTheReaderInAMethod))).Should().NotBeEmpty();
            UsersOf(reader, WithNested(typeof(RegistersTheReader))).Should().NotBeEmpty();
            UsersOf(reader, WithNested(typeof(RegistersTheReaderByType))).Should().NotBeEmpty();
            UsersOf(reader, WithNested(typeof(BuildsTheReaderInALambda))).Should().NotBeEmpty();
            UsersOf(reader, WithNested(typeof(TakesAnotherReader))).Should().BeEmpty();
        }

        [Fact]
        public void The_bar_divides_the_misses_still_shown_by_the_picks_still_shown_and_bounds_the_estimated_picks()
        {
            var recipe = new DietSecondRecipe("x", new[] { "x" }, Array.Empty<string>());
            DietReaderCase Row(int id, string truth, string kind, double weight) => new($"dsr-{id}", id, "Egg Allergy", truth, "shown", kind, weight, "draws", "agent-x", recipe);
            var frame = new[] { Row(1, DietReaderCase.Contains, "title", 1) }
                .Concat(Enumerable.Range(2, 6).Select(i => Row(i, DietReaderCase.Safe, "safe", 1000))).ToList();
            var keep = new HashSet<long> { 1, 2, 3 };

            var bar = DietReaderReport.BarOf(frame, c => keep.Contains(c.RecipeId));

            bar.Hidden.Should().Be(1);
            bar.KeptPicks.Should().Be(2001, "the bar is over the picks the combined rule still shows, not the 6,001 the rules alone show");
            bar.KeptLow.Should().BeApproximately(1 + 2000 * LaneQualifier.Wilson(2, 6).Lower * 3, 1e-6);
            bar.Upper.Should().Be(LaneQualifier.Wilson(1, bar.KeptLowPicks).Upper);
            bar.Verdict.Should().Be("met at the point figure only", "1 in 6,001 would meet the bar at its upper bound, but the 2,001 picks still shown are estimated from two samples");
            DietReaderReport.BarOf(frame.Take(1).ToList(), _ => true).Should().Match<DietReaderBar>(b => b.KeptPicks == 1 && b.KeptLow == 1 && b.Verdict == "not met");
        }

        [Fact]
        public async Task A_replay_is_never_recorded_and_takes_no_limit()
        {
            var folder = Path.Combine(Path.GetTempPath(), "dsr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.Copy(Path.Combine(GoldenSet.Folder, ModelLanes.FloorsFile), Path.Combine(folder, ModelLanes.FloorsFile));
                File.WriteAllLines(Path.Combine(folder, ModelLanes.DietSecondReaderFile), new[]
                {
                    "id\trecipe\ttype\ttitle\tlines\tmethod\ttruth\trules\tkind\tweight\tframe\tstatus\tsource\tnote",
                    "dsr-1-egg\t1\tEgg Allergy\tFrittata\teggs\tFry.\tcontains\tshown\ttitle\t\t\tagent\tagent-x\t",
                });
                var answers = Path.Combine(folder, "answers.jsonl");
                ModelLanes.WriteDietAnswers(answers, new Dictionary<long, DietSecondRead?> { [1] = OllamaDietSecondReader.Read(Reply(k => k == "egg" ? "contains" : "free")) });
                string[] Args(params string[] more) => new[] { GoldenScoreCommand.Flag, "--lane", ModelLanes.DietSecondReaderLane, "--host", "none", "--model", "phi4:latest", "--golden", folder, "--replay", answers }.Concat(more).ToArray();

                (await GoldenScoreCommand.RunAsync(Args("--record"))).Should().Be(2);
                File.Exists(ModelLanes.RecordPath(folder, ModelLanes.DietSecondReaderLane)).Should().BeFalse("a kept run carries no signature, model or row set to record");
                (await GoldenScoreCommand.RunAsync(Args("--limit", "1"))).Should().Be(2);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static IReadOnlyList<string> UsersOf(Type reader, IEnumerable<Type> types)
        {
            var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => (ushort)o.Value);
            bool Involves(Type? t) => t != null && (t == reader || (t.IsGenericType && t.GetGenericArguments().Any(Involves)) || (t.HasElementType && Involves(t.GetElementType())));
            bool Mentions(MemberInfo? m) => m switch
            {
                Type t => Involves(t),
                MethodInfo mi => Involves(mi.DeclaringType) || Involves(mi.ReturnType) || (mi.IsGenericMethod && mi.GetGenericArguments().Any(Involves)) || mi.GetParameters().Any(p => Involves(p.ParameterType)),
                MethodBase mb => Involves(mb.DeclaringType) || mb.GetParameters().Any(p => Involves(p.ParameterType)),
                FieldInfo fi => Involves(fi.DeclaringType) || Involves(fi.FieldType),
                _ => false,
            };
            bool BodyMentions(MethodBase method)
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return false;
                var typeArgs = method.DeclaringType is { IsGenericType: true } d ? d.GetGenericArguments() : null;
                var methodArgs = method is MethodInfo { IsGenericMethod: true } g ? g.GetGenericArguments() : null;
                for (var at = 0; at < il.Length;)
                {
                    var op = il[at] == 0xFE ? opCodes[(ushort)(0xFE00 | il[at + 1])] : opCodes[il[at]];
                    at += op.Size;
                    switch (op.OperandType)
                    {
                        case OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineField:
                            MemberInfo? member;
                            try { member = method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArgs, methodArgs); }
                            catch (Exception e) when (e is ArgumentException or TypeLoadException or FileNotFoundException or BadImageFormatException) { member = null; }
                            if (Mentions(member)) return true;
                            at += 4;
                            break;
                        case OperandType.InlineSwitch:
                            at += 4 + 4 * BitConverter.ToInt32(il, at);
                            break;
                        case OperandType.InlineNone:
                            break;
                        case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                            at += 1;
                            break;
                        case OperandType.InlineVar:
                            at += 2;
                            break;
                        case OperandType.InlineI8 or OperandType.InlineR:
                            at += 8;
                            break;
                        default:
                            at += 4;
                            break;
                    }
                }
                return false;
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            return types.Where(t => t != reader && !IsNestedIn(t, reader))
                .Where(t => t.GetFields(all).Any(f => Involves(f.FieldType))
                    || t.GetProperties(all).Any(p => Involves(p.PropertyType))
                    || t.GetConstructors(all).Cast<MethodBase>().Concat(t.GetMethods(all)).Any(m => Mentions(m) || BodyMentions(m)))
                .Select(t => t.FullName ?? t.Name).ToList();
        }

        private static bool IsOf(string user, Type type) => user == type.FullName || user.StartsWith(type.FullName + "+", StringComparison.Ordinal);

        private static IReadOnlyList<string> ConstructorsOf(Type reader, IEnumerable<Type> types)
        {
            var ctor = reader.GetConstructors().Single();
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            bool Builds(MethodBase method)
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return false;
                for (var at = 0; at + 4 < il.Length; at++)
                {
                    if (il[at] != OpCodes.Newobj.Value) continue;
                    try
                    {
                        if (method.Module.ResolveMethod(BitConverter.ToInt32(il, at + 1)) == ctor) return true;
                    }
                    catch (Exception e) when (e is ArgumentException or TypeLoadException or FileNotFoundException or BadImageFormatException)
                    {
                    }
                }
                return false;
            }
            return types.Where(t => t != reader && t.GetConstructors(all).Cast<MethodBase>().Concat(t.GetMethods(all)).Any(Builds)).Select(t => t.FullName ?? t.Name).ToList();
        }

        private static bool IsNestedIn(Type t, Type outer) => t.DeclaringType != null && (t.DeclaringType == outer || IsNestedIn(t.DeclaringType, outer));

        private sealed class BuildsTheReaderInAMethod
        {
            public object Make(HttpClient http) => new OllamaDietSecondReader(http, "http://model.invalid", "m");
        }

        private sealed class BuildsTheReaderInALambda
        {
            public Func<HttpClient, object> Make() => http => new OllamaDietSecondReader(http, "http://model.invalid", "m");
        }

        private sealed class RegistersTheReader
        {
            public void Register(IServiceCollection services) => services.AddSingleton<OllamaDietSecondReader>();
        }

        private sealed class RegistersTheReaderByType
        {
            public void Register(IServiceCollection services) => services.AddSingleton(typeof(OllamaDietSecondReader));
        }

        private sealed class TakesAnotherReader
        {
            public HttpClient Make(HttpClient http) => new HttpClient();
        }

        [Fact]
        public async Task A_recipe_the_host_never_answers_in_time_is_kept_unreadable_and_the_run_goes_on()
        {
            var folder = Path.Combine(Path.GetTempPath(), "dsr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllLines(Path.Combine(folder, ModelLanes.DietSecondReaderFile), new[]
                {
                    "id\trecipe\ttype\ttitle\tlines\tmethod\ttruth\trules\tkind\tweight\tframe\tstatus\tsource\tnote",
                    "dsr-1-egg\t1\tEgg Allergy\tEndless\teggs\tBeat.\tcontains\tshown\ttitle\t\t\tagent\tagent-x\t",
                    "dsr-2-egg\t2\tEgg Allergy\tStewed Rhubarb\trhubarb\tStew.\tsafe\tshown\tsafe\t\t\tagent\tagent-x\t",
                });
                var handler = new ScriptedHandler(title => title == "Endless" ? throw new TaskCanceledException("timeout", new TimeoutException()) : Reply());
                using var http = new HttpClient(handler);
                var notes = new List<string>();

                var run = await ModelLanes.DietReaderRunAsync(folder, http, "http://model.invalid", "m", null, notes.Add, CancellationToken.None);

                run.Answers[1].Should().BeNull();
                run.Answers[2].Should().NotBeNull();
                handler.Titles.Count(t => t == "Endless").Should().Be(ModelLanes.CallAttempts);
                ModelLanes.DietReaderResults(run).Single(r => r.Id == "dsr-1-egg").Should().Match<LaneCaseResult>(r => r.Pass && r.Actual == "no answer");
                notes.Should().Contain(n => n.Contains("recipe 1 not answered in time", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private sealed class ScriptedHandler : HttpMessageHandler
        {
            private readonly Func<string, string> _reply;

            public ScriptedHandler(Func<string, string> reply) => _reply = reply;

            public List<string> Titles { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                body.GetProperty("options").GetProperty("temperature").GetInt32().Should().Be(0);
                body.GetProperty("options").GetProperty("seed").GetInt32().Should().Be(1);
                body.GetProperty("format").GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Equal(new[] { "unlisted", "complete" }.Concat(OllamaDietSecondReader.Types.Select(t => t.Key)));
                var user = body.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
                var title = user.Split('\n')[0]["Title: ".Length..];
                Titles.Add(title);
                var content = JsonSerializer.Serialize(new { message = new { content = _reply(title) } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            }
        }
    }
}
