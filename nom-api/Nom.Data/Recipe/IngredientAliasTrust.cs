using System;
using System.Linq;
using System.Linq.Expressions;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// Which <see cref="IngredientAliasEntity.SourceContext"/> values say that a deterministic rule
    /// or a person put the alias on its food. Only such an alias may link a recipe line to the food
    /// without review; an alias with no context, or one a model pick left ("link:ai:..."), is a
    /// wording somebody once saw on the food and nothing more.
    /// </summary>
    public static class IngredientAliasTrust
    {
        /// <summary>The food's own USDA description.</summary>
        public const string FdcPrefix = "fdc:";

        /// <summary>A person approved the wording for the food.</summary>
        public const string AdminPrefix = "admin:";

        /// <summary>A deterministic name rule gave the wording to the food.</summary>
        public const string DeterministicPrefix = "deterministic:";

        /// <summary>The name of a duplicate food that was retired into this one.</summary>
        public const string RetiredPrefix = "retired:";

        /// <summary>A link that a deterministic rule proposed and the lane applied.</summary>
        public const string DeterministicLinkPrefix = "link:deterministic:";

        /// <summary>An alias that had no context and that today's deterministic rules give to the food it is on.</summary>
        public const string RecheckPrefix = DeterministicPrefix + "recheck:";

        public static readonly string[] TrustedPrefixes = { FdcPrefix, AdminPrefix, DeterministicPrefix, RetiredPrefix, DeterministicLinkPrefix };

        public static bool IsTrusted(string? sourceContext) =>
            sourceContext != null && TrustedPrefixes.Any(prefix => sourceContext.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>The same test as <see cref="IsTrusted"/>, in a form a query can run in the database.</summary>
        public static readonly Expression<Func<IngredientAliasEntity, bool>> IsTrustedAlias = a =>
            a.SourceContext != null
            && (a.SourceContext.StartsWith(FdcPrefix)
                || a.SourceContext.StartsWith(AdminPrefix)
                || a.SourceContext.StartsWith(DeterministicPrefix)
                || a.SourceContext.StartsWith(RetiredPrefix)
                || a.SourceContext.StartsWith(DeterministicLinkPrefix));
    }
}
