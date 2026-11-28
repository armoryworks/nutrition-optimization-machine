using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Tests.Golden;
using Nom.Data;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// A name pattern read by words (<see cref="DietPatternRule"/>): a term counts where it begins a
    /// word or is a whole part of a compound, not inside another word ("goat", "avocado"), not when the
    /// text says the food is absent ("eggless", "peanut-free"), and not inside a named exception
    /// ("eggplant", "peanut butter"); a doubled <c>%</c> asks for a substring on purpose. The
    /// PostgreSQL expression the whole-catalog probe runs is the in-memory one in the other dialect.
    /// </summary>
    public class DietPatternRuleTests
    {
        private static IEnumerable<string> DefaultLikePatterns =>
            DefaultRestrictionCriteria.ByName.Values.SelectMany(v => v.Patterns).Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Distinct();

        [Fact]
        public void A_pattern_is_read_by_where_its_term_may_begin_and_end_and_a_doubled_percent_or_wildcard_keeps_the_substring()
        {
            DietPatternRule.For("%egg%").Should().BeEquivalentTo(new { Substring = false, Term = "egg", Start = DietPatternStart.Open, End = DietPatternEnd.Open });
            DietPatternRule.For("oat%").Should().BeEquivalentTo(new { Substring = false, Term = "oat", Start = DietPatternStart.Text });
            DietPatternRule.For("% oat%").Should().BeEquivalentTo(new { Substring = false, Term = "oat", Start = DietPatternStart.Word });
            DietPatternRule.For("%brazil nut%").Words.Should().Equal("brazil", "nut");
            DietPatternRule.For("%ham").End.Should().Be(DietPatternEnd.Text);
            DietPatternRule.For("%%nut%%").Substring.Should().BeTrue();
            DietPatternRule.For("%ice%cream%").Substring.Should().BeTrue();
            DietPatternRule.For("a_c").Substring.Should().BeTrue();
        }

        [Theory]
        [InlineData("%egg%", "2 large eggs", true)]
        [InlineData("%egg%", "2eggs, beaten", true)]
        [InlineData("%egg%", "1 cup eggnog", true)]
        [InlineData("%egg%", "1 medium eggplant", false)]
        [InlineData("%egg%", "2 cups veggies", false)]
        [InlineData("%egg%", "8 oz eggless pasta", false)]
        [InlineData("%egg%", "egg-free mayonnaise", false)]
        [InlineData("%egg%", "1 eggplant and 1 egg", true)]
        [InlineData("%%egg%%", "1 medium eggplant", true)]
        [InlineData("%ham%", "1 sleeve graham crackers", false)]
        [InlineData("%ham%", "1 bottle champagne", false)]
        [InlineData("%ham%", "1 lb ham, diced", true)]
        [InlineData("%ham%", "2 ham hocks", true)]
        [InlineData("%milk%", "1 cup buttermilk", true)]
        [InlineData("%milk%", "1 can coconut milk", false)]
        [InlineData("%milk%", "1 cup coconut milk or whole milk", true)]
        [InlineData("%oat%", "1 cup goat cheese", false)]
        [InlineData("%oat%", "1 root beer float", false)]
        [InlineData("%oat%", "2 cups oatmeal", true)]
        [InlineData("%pear%", "1 bunch asparagus spears", false)]
        [InlineData("%icing%", "1 tomato, for slicing", false)]
        [InlineData("%cod%", "2 ripe avocados", false)]
        [InlineData("%cola%", "1 cup milk chocolate chips", false)]
        [InlineData("%bread%", "1 cup shortbread crumbs", true)]
        [InlineData("%corn%", "2 cups popcorn", true)]
        [InlineData("%corn%", "1 tbsp cornstarch", true)]
        [InlineData("%corn%", "1 tsp peppercorns", false)]
        [InlineData("%radish%", "1 tbsp horseradish", true)]
        [InlineData("%peanut%", "1/2 cup peanut-free chocolate chips", false)]
        [InlineData("%peanut%", "1/2 cup peanut free chocolate chips", false)]
        [InlineData("%flour%", "1 flourless chocolate cake", false)]
        [InlineData("%flour%", "2 cups flour (or almond flour)", true)]
        [InlineData("%brazil nut%", "6 brazil-nuts", true)]
        [InlineData("%nut butter%", "2 tbsp peanut butter", false)]
        [InlineData("%nut butter%", "2 tbsp mixed nut butter", true)]
        [InlineData("%goose%", "1 cup gooseberries", false)]
        [InlineData("%lamb%", "2 cups lambs lettuce", false)]
        [InlineData("%lamb%", "2 cups lamb’s lettuce", false)]
        [InlineData("%goat%", "4 oz goat's cheese", false)]
        [InlineData("oat%", "Oatmeal", true)]
        [InlineData("oat%", "Goat cheese", false)]
        [InlineData("% oat%", "1 cup (oat flour)", true)]
        [InlineData("%anchov%", "4 anchovies", true)]
        [InlineData("%jalape%", "2 jalapeños", true)]
        [InlineData("%flour%", "2 cups cooked rice, flour for dusting", true)]
        [InlineData("%butter%", "Garnish: chopped peanuts, butter", true)]
        [InlineData("%milk%", "1 cup shredded coconut, milk chocolate chips", true)]
        [InlineData("%milk%", "1 cup coconut/milk mixture", true)]
        [InlineData("%butter%", "2 tbsp peanut-butter", false)]
        [InlineData("%egg%", "1 egg free-range", true)]
        [InlineData("%egg%", "2 eggs free range", true)]
        [InlineData("%egg%", "6 egg free-run", true)]
        [InlineData("%chicken%", "1 whole chicken free range", true)]
        [InlineData("%chicken%", "chicken-free broth", false)]
        [InlineData("%flour%", "1 cup garbanzo flour", true)]
        [InlineData("%cream%", "1/2 cup creamy or crunchy peanut butter", false)]
        [InlineData("%cream%", "1 cup creamy natural peanut butter", false)]
        [InlineData("%cream%", "1 cup heavy cream", true)]
        [InlineData("%noix%", "noix de veau", false)]
        [InlineData("%noix%", "1 cup noix", true)]
        [InlineData("% nut%", "rye nuts + rye nuts", false)]
        [InlineData("% nut%", "rye bread with nuts", true)]
        public void A_term_counts_where_it_begins_a_word_or_is_a_whole_compound_part_and_not_in_a_named_exception(string pattern, string text, bool expected)
        {
            DietPatternRule.For(pattern).IsMatch(text).Should().Be(expected, $"{pattern} on \"{text}\"");
        }

        [Fact]
        public void Garbanzo_flour_is_no_gluten_flour_but_stays_a_legume_for_the_types_that_exclude_legumes()
        {
            DietPatternRule.For("%flour%", "Celiac Disease").IsMatch("1 cup garbanzo flour").Should().BeFalse("staging checkpoint 9, U2: garbanzo flour was flagged for Celiac by its flour");
            DietPatternRule.For("%flour%", "Wheat Allergy").IsMatch("1 cup garbanzo bean flour").Should().BeFalse();
            DietPatternRule.For("%flour%", "Paleo").IsMatch("1 cup garbanzo flour").Should().BeTrue("Paleo excludes legumes and names no garbanzo");
            DietPatternRule.For("%flour%", "Celiac Disease").IsMatch("1 cup garbanzo flour and 1 cup flour").Should().BeTrue();
        }

        [Fact]
        public void A_nondairy_milk_written_as_one_word_is_no_milk_for_the_types_that_restrict_milk()
        {
            DietPatternRule.For("%milk%", "Milk Allergy").IsMatch("1/2 cup almond milk (or any nondairy milk)").Should().BeFalse("staging checkpoint 9, U13: the line was named as the milk");
            DietPatternRule.For("%milk%", "Milk Allergy").IsMatch("1 cup nondairy milk and 1 cup milk").Should().BeTrue();
        }

        [Fact]
        public void Every_named_exception_blanks_its_phrase_and_leaves_the_term_beside_it()
        {
            foreach (var e in DietPatternRule.All)
            {
                var pattern = "%" + e.Term + "%";
                var rule = DietPatternRule.For(pattern, e.Types?[0]);
                DietCriteriaMatcher.LikeRegex(pattern).IsMatch(e.Phrase).Should().BeTrue($"\"{e.Phrase}\" holds the term {e.Term}, or it needs no exception");
                var phrase = e.Phrase.Replace("*", "fine");
                rule.IsMatch("1 cup " + phrase).Should().BeFalse($"\"{e.Phrase}\" is a named exception of {e.Term}");
                rule.IsMatch("1 cup " + phrase.ToUpperInvariant() + ", chopped").Should().BeFalse("an exception is read without case");
                rule.IsMatch("1 cup " + phrase + " and 2 " + e.Term).Should().BeTrue($"the term {e.Term} beside \"{e.Phrase}\" still counts");
                e.Reason.Should().NotBeNullOrWhiteSpace();
            }
            DietPatternRule.For("%goose%").IsMatch("1 cup gooseberries").Should().BeFalse("a phrase ending in y also reads its plural in ies");
        }

        [Fact]
        public void A_pattern_an_exception_says_still_holds_the_compound_is_a_default_pattern_that_names_it()
        {
            var defaults = DefaultLikePatterns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var claims = from e in DietPatternRule.All
                         from m in Regex.Matches(e.Reason, @"%[a-z ]+%").Cast<Match>()
                         select (e.Phrase, Pattern: m.Value);
            claims.Should().NotBeEmpty();
            claims.Where(c => !defaults.Contains(c.Pattern)).Select(c => $"{c.Phrase}: {c.Pattern} is no default pattern").Should().BeEmpty();
            claims.Where(c => !DietPatternRule.For(c.Pattern).IsMatch("1 cup " + c.Phrase)).Select(c => $"{c.Phrase}: {c.Pattern} does not name it").Should().BeEmpty();
        }

        [Fact]
        public void Every_exception_belongs_to_a_default_patterns_term_or_to_a_term_an_admin_may_write()
        {
            var terms = DefaultLikePatterns.Select(p => DietPatternRule.For(p)).Where(r => !r.Substring).Select(r => r.Term).ToHashSet(StringComparer.Ordinal);
            DietPatternRule.All.Select(e => e.Term).Distinct().Where(t => !terms.Contains(t)).Should().BeEquivalentTo(new[] { "pea" }, "Peanut Allergy and the tree nut types hold % nut% since diet-patterns/5, a nut a line does not name (staging checkpoint 5)");
        }

        [Theory]
        [InlineData("%creme%", "Crème fraîche")]
        [InlineData("%atun%", "Atún en aceite")]
        [InlineData("%shrimp%", "Shrimp")]
        [InlineData("%sesame%", "ajonjolí or sesame")]
        [InlineData("%bearnaise%", "Sauce Béarnaise")]
        [InlineData("%oeuf%", "Œufs à la neige")]
        public void The_postgres_expression_finds_a_name_whose_only_spelling_of_the_term_is_accented_as_the_folded_reading_does(string pattern, string text)
        {
            var rule = DietPatternRule.For(pattern);
            rule.IsMatch(text).Should().BeTrue("the store reads the folded text");
            var postgres = rule.Expression(DietRegexDialect.Postgres).Replace("[^[:alpha:]]", @"[^\p{L}]").Replace("[[:alpha:]]", @"\p{L}").Replace("[[:space:](-]", @"[\s(-]").Replace("[^[:alnum:]]", @"[^\p{L}\p{N}]");
            Regex.IsMatch(text, postgres, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Should().BeTrue("the database probe reads the stored text unfolded and must return every row the store counts");
        }

        [Theory]
        [InlineData("SNACK BAR (Atkins Nutritionals, Inc.)", true, "SNACK BAR")]
        [InlineData("CORN FLAKES (Kellogg Company Us)", true, "CORN FLAKES")]
        [InlineData("FIG BARS, FIG (J&J Snack Foods Corp)", true, "FIG BARS, FIG")]
        [InlineData("TRAIL MIX (PEANUTS, RAISINS)", true, "TRAIL MIX (PEANUTS, RAISINS)")]
        [InlineData("Pasta (dry)", false, "Pasta (dry)")]
        public void A_branded_foods_closing_bracket_is_its_maker_only_as_the_import_writes_the_owner(string name, bool branded, string read) =>
            DietPatternRule.WithoutMaker(name, branded).Trim().Should().Be(read);

        [Fact]
        public void A_description_bracket_of_a_branded_food_is_still_read_by_its_name()
        {
            var peanut = DefaultRestrictionCriteria.ByName["Peanut Allergy"].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select(p => DietPatternRule.For(p, "Peanut Allergy"));
            peanut.Any(r => r.IsMatch(DietPatternRule.WithoutMaker("TRAIL MIX (PEANUTS, RAISINS)", true))).Should().BeTrue();
        }

        [Fact]
        public void The_postgres_expressions_are_the_dotnet_ones_with_postgres_letter_and_space_classes()
        {
            foreach (var pattern in DefaultLikePatterns)
            {
                var rule = DietPatternRule.For(pattern);
                rule.Substring.Should().BeFalse($"{pattern} is a plain default pattern");
                Swap(rule.Expression(DietRegexDialect.Postgres)).Should().Be(rule.Expression(DietRegexDialect.DotNet));
                Swap(rule.ExceptionExpression(DietRegexDialect.Postgres) ?? string.Empty).Should().Be(rule.ExceptionExpression(DietRegexDialect.DotNet) ?? string.Empty);
                rule.Expression(DietRegexDialect.Postgres).Should().NotContain(@"\p").And.NotContain("(?<");
            }

            static string Swap(string postgres) => Regex.Replace(Regex.Replace(postgres, @"\(\?:(oe|ae|ss)\|[œæß]\)", "$1"), @"\[([a-z])[àáâãäåèéêëìíîïòóôõöøùúûüçñýÿ]+\]", "$1").Replace("[^[:alpha:]]", @"[^\p{L}]").Replace("[[:alpha:]]", @"\p{L}").Replace("[[:space:]-]", @"[\s-]").Replace("[[:space:](-]", @"[\s(-]").Replace("[^[:alnum:]]", @"[^\p{L}\p{N}]").Replace("[[:alnum:]'’]", @"[\p{L}\p{N}'’]").Replace("[[:space:]&]", @"[\s&]");
        }

        [Fact]
        public void The_postgres_expressions_read_every_golden_line_as_the_dotnet_ones_do_in_the_tcl_engine_postgres_took_its_regular_expressions_from()
        {
            var tclsh = new[] { "/usr/bin/tclsh", "/usr/local/bin/tclsh" }.FirstOrDefault(File.Exists);
            if (tclsh == null) return;
            var texts = GoldenSet.Read("diet-patterns.tsv").Rows.Select(r => r["line"])
                .Concat(DietPatternRule.All.SelectMany(e => new[] { "1 cup " + e.Phrase, "2 " + e.Phrase + " and 1 " + e.Term }))
                .Distinct().ToList();
            var cases = new List<(string Pattern, string Text, bool DotNet)>();
            var data = new StringBuilder();
            foreach (var pattern in DefaultLikePatterns.Concat(new[] { "%nut%", "%pea%" }))
            {
                var rule = DietPatternRule.For(pattern);
                foreach (var text in texts.Where(t => DietCriteriaMatcher.LikeRegex(pattern).IsMatch(t) || rule.IsMatch(t)))
                {
                    cases.Add((pattern, text, rule.IsMatch(text)));
                    data.Append(rule.Expression(DietRegexDialect.Postgres)).Append('\t').Append(rule.ExceptionExpression(DietRegexDialect.Postgres) ?? string.Empty).Append('\t').Append(text).Append('\n');
                }
            }
            var folder = Path.Combine(Path.GetTempPath(), "nom-diet-patterns-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "cases.tsv"), data.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "read.tcl"),
                    "set f [open [lindex $argv 0] r]\nfconfigure $f -encoding utf-8\nfconfigure stdout -encoding utf-8\n"
                    + "while {[gets $f line] >= 0} {\n  lassign [split $line \"\\t\"] r e t\n  if {$e ne \"\"} { regsub -all -nocase -- $e $t \" \" t }\n  puts [regexp -nocase -- $r $t]\n}\nclose $f\n");
                var info = new ProcessStartInfo(tclsh) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                info.ArgumentList.Add(Path.Combine(folder, "read.tcl"));
                info.ArgumentList.Add(Path.Combine(folder, "cases.tsv"));
                using var process = Process.Start(info)!;
                var output = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                process.WaitForExit();
                process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
                output.Should().HaveCount(cases.Count);
                cases.Select((c, i) => (c, Tcl: output[i] == "1")).Where(x => x.Tcl != x.c.DotNet)
                    .Select(x => $"{x.c.Pattern} on \"{x.c.Text}\": .NET {x.c.DotNet}, ARE {x.Tcl}").Should().BeEmpty();
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void The_whole_catalog_line_probe_is_one_scan_that_blanks_each_terms_exceptions_before_it_seeks_the_term()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var rules = new[] { "%egg%", "%mayonnaise%", "%milk%" }.Select(p => DietPatternRule.For(p)).ToList();

            var sql = DietCriteriaMatcher.LineRecipeSql(rules);
            sql.Format.Should().Contain("FROM recipe.\"RecipeIngredient\" AS ri").And.Contain("NOT ri.\"IsDeleted\"").And.Contain("DISTINCT").And.NotContain("JOIN");
            sql.Format.Should().Contain("regexp_replace(ri.\"RawLine\", {").And.Contain(", ' ', 'gi') ~* {");
            sql.GetArguments().Should().Contain(DietPatternRule.For("%mayonnaise%").Expression(DietRegexDialect.Postgres), "a term with no exception is sought as it is");
            sql.GetArguments().Should().Contain(DietPatternRule.For("%egg%").ExceptionExpression(DietRegexDialect.Postgres));
            sql.GetArguments().Should().Contain(DietPatternRule.For("%milk%").ExceptionExpression(DietRegexDialect.Postgres));
            sql.ArgumentCount.Should().Be(6, "one prefilter, one plain branch and a term and its exceptions for each of the two terms with exceptions");
            db.Database.SqlQuery<long>(sql).ToQueryString().Should().Contain("regexp_replace").And.Contain("~*");
        }
    }
}
