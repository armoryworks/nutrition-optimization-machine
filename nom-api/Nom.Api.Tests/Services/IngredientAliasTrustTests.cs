using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// An alias is trusted only when its context says a deterministic rule or a person put it on
    /// its food. The in-memory test and the one a query runs in the database must agree.
    /// </summary>
    public class IngredientAliasTrustTests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("link:ai:x", false)]
        [InlineData("link:ai:qwen3:14b", false)]
        [InlineData("ai:fake", false)]
        [InlineData("link:admin:x", false)]
        [InlineData("FDC:description", false)]
        [InlineData(" fdc:description", false)]
        [InlineData("fdc:description", true)]
        [InlineData("admin:link:ai:fake", true)]
        [InlineData("deterministic:recheck:name-key", true)]
        [InlineData("deterministic:recheck:staple", true)]
        [InlineData("deterministic:recheck:name-match", true)]
        [InlineData("retired:274955", true)]
        [InlineData("link:deterministic:staple", true)]
        [InlineData("link:deterministic:name-match", true)]
        public void A_context_from_a_rule_or_a_person_is_trusted_and_nothing_else_is(string? context, bool trusted)
        {
            IngredientAliasTrust.IsTrusted(context).Should().Be(trusted);
            IngredientAliasTrust.IsTrustedAlias.Compile()(new IngredientAliasEntity { SourceContext = context }).Should().Be(trusted);
        }

        [Fact]
        public void Every_trusted_prefix_is_in_the_query_form_too()
        {
            foreach (var prefix in IngredientAliasTrust.TrustedPrefixes)
            {
                IngredientAliasTrust.IsTrustedAlias.Compile()(new IngredientAliasEntity { SourceContext = prefix + "x" }).Should().BeTrue(prefix);
            }
            IngredientAliasTrust.IsTrusted(IngredientAliasTrust.RecheckPrefix + "name-key").Should().BeTrue();
        }

        [Fact]
        public void The_query_form_runs_in_postgres_as_prefix_matches()
        {
            using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options);

            var sql = db.IngredientAliases.Where(IngredientAliasTrust.IsTrustedAlias).Select(a => a.IngredientId).ToQueryString();

            sql.Should().Contain("\"SourceContext\" IS NOT NULL");
            foreach (var prefix in IngredientAliasTrust.TrustedPrefixes)
            {
                sql.Should().Contain($"\"SourceContext\" LIKE '{prefix}%'");
            }
        }
    }
}
