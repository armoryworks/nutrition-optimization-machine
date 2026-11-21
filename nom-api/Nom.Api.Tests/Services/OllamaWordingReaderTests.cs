using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The blind reader's request and reply: temperature 0, a fixed seed, an 8,192-token context and a
    /// JSON schema; an answer names a candidate by its number in the order that reader was shown, and
    /// anything unreadable is left out rather than guessed.
    /// </summary>
    public class OllamaWordingReaderTests
    {
        private sealed class Reply : HttpMessageHandler
        {
            private readonly string _content;
            public JsonObject? Body { get; private set; }

            public Reply(string content) => _content = content;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                var payload = new JsonObject { ["message"] = new JsonObject { ["content"] = _content } };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
            }
        }

        private static WordingPack Pack(string wording, params long[] ids) => new(wording,
            new[] { new ReaderLine("1 tsp " + wording, "Biscuits", "book", "spoon") }, new Dictionary<string, double> { ["spoon"] = 1.0 },
            ids.Select(id => new ReaderFood(id, "Food " + id, "Food " + id + ", raw", 18, 30, 250m)).ToList(), true);

        [Fact]
        public async Task A_reading_is_asked_at_temperature_zero_with_a_fixed_seed_a_large_context_and_a_schema()
        {
            var handler = new Reply("{\"answers\":[{\"n\":1,\"verdict\":\"food\",\"pick\":2,\"reason\":\"old name\",\"means\":\"baking soda\"}]}");
            var reader = new OllamaWordingReader(new HttpClient(handler), "http://host", "qwen2.5:14b-instruct");

            var answers = await reader.ReadAsync(new[] { Pack("saleratus", 7, 9) });

            answers.Single().Should().Be(new ReaderAnswer(IngredientWordingVerdict.Food, 9, "old name", "baking soda"));
            var options = handler.Body!["options"]!.AsObject();
            options["temperature"]!.GetValue<int>().Should().Be(0);
            options["seed"]!.GetValue<int>().Should().Be(OllamaWordingReader.Seed);
            options["num_ctx"]!.GetValue<int>().Should().Be(8192);
            handler.Body["format"]!.AsObject()["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Contain("answers");
            handler.Body["model"]!.GetValue<string>().Should().Be("qwen2.5:14b-instruct");
            var user = handler.Body["messages"]!.AsArray()[1]!["content"]!.GetValue<string>();
            user.Should().Contain("wording: \"saleratus\"").And.Contain("1. Food 7").And.Contain("2. Food 9").And.Contain("Baked Products").And.Contain("250 kcal per 100 g");
        }

        [Fact]
        public void An_answer_names_a_candidate_by_its_number_and_an_unreadable_entry_is_left_out()
        {
            var packs = new[] { Pack("a", 1, 2), Pack("b", 3), Pack("c", 4), Pack("d", 5) };
            var reply = "{\"answers\":[" +
                "{\"n\":1,\"verdict\":\"food\",\"pick\":1,\"reason\":\"r\",\"means\":\"\"}," +
                "{\"n\":2,\"verdict\":\"food\",\"pick\":5,\"reason\":\"r\",\"means\":\"\"}," +
                "{\"n\":3,\"verdict\":\"none\",\"pick\":0,\"reason\":\"r\",\"means\":\"\"}," +
                "{\"n\":4,\"verdict\":\"maybe\",\"pick\":0,\"reason\":\"r\",\"means\":\"\"}]}";

            var answers = OllamaWordingReader.ReadAnswers(reply, packs);

            answers[0].Should().Be(new ReaderAnswer(IngredientWordingVerdict.Food, 1, "r", null));
            answers[1].Should().BeNull("pick 5 is not a candidate of that pack");
            answers[2]!.Verdict.Should().Be(IngredientWordingVerdict.None);
            answers[3].Should().BeNull("an unknown verdict");
            var act = () => OllamaWordingReader.ReadAnswers("{\"answers\":[]}", packs);
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public async Task A_fixed_reading_offers_only_the_modes_readings_and_reads_one_per_line()
        {
            var handler = new Reply("{\"results\":[{\"n\":1,\"reading\":\"hard\"},{\"n\":2,\"reading\":\"table\"}]}");
            var reader = new OllamaWordingReader(new HttpClient(handler), "http://host", "mistral-nemo");
            var pack = new FixedPack("cider", AlcoholFixedReading.Cider, new[]
            {
                new FixedLine("1 quart cider", "Boiled Ham", new[] { "ham", "cloves" }, "Boil the ham in the cider.", false),
                new FixedLine("2 cups cider", "Cider Jelly", Array.Empty<string>(), null, false),
            });

            var readings = await reader.ReadFixedAsync(pack);

            readings.Should().Equal("hard", null);
            handler.Body!["format"]!.ToJsonString().Should().Contain("\"sweet\"").And.Contain("\"hard\"").And.Contain("\"unsure\"").And.NotContain("\"table\"");
            handler.Body["messages"]!.AsArray()[0]!["content"]!.GetValue<string>().Should().Contain("\"cider\"");
        }

        [Fact]
        public void The_refuter_says_whether_it_found_a_reason()
        {
            OllamaWordingReader.ReadRefutation("{\"refuted\":false,\"reason\":\"none\"}").Should().BeNull();
            OllamaWordingReader.ReadRefutation("{\"refuted\":true,\"reason\":\"treacle may be golden syrup\"}").Should().Be("treacle may be golden syrup");
            var act = () => OllamaWordingReader.ReadRefutation("{\"reason\":\"none\"}");
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void The_readers_signature_moves_with_the_prompt_and_the_fixed_readings()
        {
            OllamaWordingReader.Signature.Should().HaveLength(16);
            WordingReaderRecords.ScorerSignature(WordingReaderRecords.ReadLane).Should().NotBe(WordingReaderRecords.ScorerSignature(WordingReaderRecords.LexiconLane));
            WordingReaderRecords.ScorerSignature(WordingReaderRecords.AuditLane).Should().NotBe(WordingReaderRecords.ScorerSignature(WordingReaderRecords.AlcoholLane));
        }
    }
}
