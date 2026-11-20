using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The model roster: three families, all OSI-licensed, and no checking role of the family it
    /// checks; and the stratified audit sample.
    /// </summary>
    public class LaneModelRosterTests
    {
        [Theory]
        [InlineData("qwen2.5:14b-instruct", "qwen")]
        [InlineData("qwen3:8b", "qwen")]
        [InlineData("qwen2.5:7b-instruct", "qwen")]
        [InlineData("mistral-nemo", "mistral")]
        [InlineData("mistral-nemo:12b-instruct-2407-q4_K_M", "mistral")]
        [InlineData("mixtral:8x7b", "mistral")]
        [InlineData("phi4", "phi")]
        [InlineData("phi4:14b", "phi")]
        [InlineData("registry.example/library/llama3.1:8b", "llama")]
        [InlineData("gemma2:9b", "gemma")]
        public void A_model_tag_names_its_family(string model, string family)
        {
            LaneModelRoster.Family(model).Should().Be(family);
        }

        [Fact]
        public void The_default_roster_is_three_families_and_sound()
        {
            var roster = new LaneModelRoster();

            roster.Problems().Should().BeEmpty();
            new[] { roster.ReaderA, roster.ReaderB, roster.Auditor }.Select(LaneModelRoster.Family).Should().OnlyHaveUniqueItems();
        }

        [Theory]
        [InlineData("qwen2.5:14b-instruct", "qwen3:8b", "phi4", "reader B qwen3:8b is of reader A's family")]
        [InlineData("qwen2.5:14b-instruct", "mistral-nemo", "qwen2.5:7b-instruct", "the auditor qwen2.5:7b-instruct is of reader A's family")]
        [InlineData("qwen2.5:14b-instruct", "mistral-nemo", "mistral-small", "the auditor mistral-small is of reader B's family")]
        [InlineData("qwen2.5:14b-instruct", "llama3.1:8b", "phi4", "licence is not OSI-approved")]
        [InlineData("qwen2.5:14b-instruct", "mistral-nemo", "gemma2:9b", "licence is not OSI-approved")]
        [InlineData("qwen2.5:14b-instruct", "", "phi4", "reader B has no model")]
        public void A_roster_that_lets_a_model_check_its_own_family_or_uses_a_non_osi_family_is_refused(string a, string b, string auditor, string problem)
        {
            var roster = new LaneModelRoster { ReaderA = a, ReaderB = b, Auditor = auditor };

            roster.Problems().Should().Contain(p => p.Contains(problem));
        }

        [Fact]
        public void The_api_refuses_to_start_with_a_same_family_roster()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Roster:ReaderB"] = "qwen3:8b",
            }).Build();
            var services = new ServiceCollection();
            services.AddOptions<LaneModelRoster>().Bind(configuration.GetSection(LaneModelRoster.SectionName)).ValidateOnStart();
            services.AddSingleton<IValidateOptions<LaneModelRoster>, LaneModelRosterValidation>();
            using var provider = services.BuildServiceProvider();

            var act = () => provider.GetRequiredService<IOptions<LaneModelRoster>>().Value;

            act.Should().Throw<OptionsValidationException>().Which.Failures.Should().Contain(f => f.Contains("reader B qwen3:8b"));
        }

        [Fact]
        public void An_audit_sample_is_the_larger_of_its_minimum_and_its_rate()
        {
            new LaneSamplePlan(Minimum: 30, Rate: 0.05).SizeFor(100).Should().Be(30);
            new LaneSamplePlan(Minimum: 30, Rate: 0.05).SizeFor(2000).Should().Be(100);
            new LaneSamplePlan(Minimum: 30, Rate: 0.05).SizeFor(12).Should().Be(12);
        }

        [Fact]
        public void An_audit_sample_takes_each_stratum_in_proportion()
        {
            var items = Enumerable.Range(0, 1000).Select(i => (Id: i, Stratum: i < 900 ? "common" : "rare")).ToList();

            var sample = LaneAuditSampler.Pick(items, i => i.Stratum, new LaneSamplePlan(Minimum: 50, Rate: 0.05), new Random(7));

            sample.Should().HaveCount(50).And.OnlyHaveUniqueItems();
            sample.Count(s => s.Stratum == "common").Should().Be(45);
            sample.Count(s => s.Stratum == "rare").Should().Be(5);
        }
    }
}
