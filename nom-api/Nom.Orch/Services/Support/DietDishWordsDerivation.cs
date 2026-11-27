using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The derivation of <c>Contents/diet-dish-words.tsv</c> from a database (<c>Nom.Import --dish-words</c>), so the
    /// committed table can be read again from the repository and checked against the corpus it was read from. Each live
    /// recipe's lines are read with no title and no method (<see cref="DietGateStoreBuilder.Read"/> without its book) by
    /// the severe default set of each class's reference type (<see cref="DietVocabularyClass.ReferenceType"/>), and its
    /// words (<see cref="DietDishWords.Evidence"/>) are counted for the classes those lines are flagged for, over every
    /// recipe and over the web recipes alone (<see cref="DietDishWords.Derive"/>, <see cref="DietDishWords.DeriveLost"/>).
    /// Reads only; nothing is written to the database.
    /// </summary>
    internal static class DietDishWordsDerivation
    {
        /// <summary>The file's text for the database's live recipes; <paramref name="corpus"/> names the copy it was read from in the header. With <paramref name="statements"/> false the foods' label statements are not read (a read-only role that may not read the contents table; the table of 2026-11-26 was read so).</summary>
        public static async Task<string> DeriveAsync(ApplicationDbContext db, string corpus, bool statements = true, CancellationToken cancellationToken = default)
        {
            var types = DietVocabulary.Classes.ToDictionary(c => c.ReferenceType, c => c.Name, StringComparer.OrdinalIgnoreCase);
            var specs = (await DietGateStoreBuilder.SpecsAsync(db))
                .Where(s => s.Severe && s.MinimumSeverity == 0 && s.Criteria.Count > 0 && s.Criteria[0].TypeName is { } t && types.ContainsKey(t))
                .GroupBy(s => s.Criteria[0].TypeName!, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(s => s.TypeId).First()).ToList();
            var nutrientIds = specs.SelectMany(s => s.Criteria).Where(c => c.NutrientId != null).Select(c => c.NutrientId!.Value).Distinct().ToList();
            var catalog = await DietFoodCatalog.LoadAsync(db, nutrientIds, cancellationToken, withStatements: statements);
            if (statements) await catalog.LoadStatementsAsync(db, catalog.Foods.Keys.ToList(), cancellationToken);
            var reading = DietFoodReader.Read(catalog, catalog.Foods.Keys.ToList());
            var builder = new DietGateStoreBuilder();
            foreach (var id in catalog.Foods.Keys) builder.FoodPrints[id] = 1;
            var sets = new List<DietGateStoreBuilder.SetState>();
            var n = 0;
            foreach (var spec in specs)
            {
                var set = new DietGateStoreBuilder.SetState(new DietGateSetEntity { Id = ++n, CriteriaKey = spec.Key, RestrictionTypeId = spec.TypeId, Severe = spec.Severe, State = DietGateStore.Complete });
                DietGateStoreBuilder.Configure(set, spec);
                foreach (var (food, fact) in DietGateStoreBuilder.Facts(DietFoodReader.Decide(reading, spec.Criteria), spec.Criteria)) set.Facts[food] = fact;
                sets.Add(set);
            }
            var readers = DietGateStoreBuilder.ReadersOf(specs);
            var words = await DietGateStoreBuilder.WordsAsync(db, readers, specs, sets, cancellationToken);

            var recipes = await db.Recipes.AsNoTracking().Select(r => new { r.Id, r.Name, r.SourceUrl, r.ScrapedAtUtc }).ToListAsync(cancellationToken);
            var lines = (await db.RecipeIngredients.AsNoTracking()
                    .Select(ri => new DietGateStoreBuilder.LineRow(ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine, ri.Quantity, ri.MeasurementId, ri.LineKind, ri.AlcoholRetention, ri.AlcoholRetentionBasis))
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.RecipeId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.IngredientId).ToList());
            var methods = (await db.RecipeSteps.AsNoTracking().Select(s => new { s.RecipeId, s.StepNumber, s.Description }).ToListAsync(cancellationToken))
                .GroupBy(s => s.RecipeId).ToDictionary(g => g.Key, g => string.Join("\n", g.OrderBy(s => s.StepNumber).Select(s => s.Description)));

            var empty = new Dictionary<long, Dictionary<long, RecipeAlcohol>>();
            var evidence = new ConcurrentBag<(DietDishWords.Words Words, IReadOnlySet<string> Flagged, bool Web, bool Dough)>();
            Parallel.ForEach(recipes, r =>
            {
                var recipeLines = lines.TryGetValue(r.Id, out var l) ? l : new List<DietGateStoreBuilder.LineRow>();
                var bare = DietGateStoreBuilder.Read(readers, r.Id, recipeLines, 0, null, words, catalog.Foods);
                var flagged = sets.Where(s => builder.Decide(bare, r.Id, s, empty) is { Verdict: DietVerdict.Contains }).Select(s => types[s.Spec.Criteria[0].TypeName!]).ToHashSet(StringComparer.Ordinal);
                var ingredientLines = recipeLines.Where(x => x.LineKind == null || x.LineKind == RecipeIngredientLineKind.Ingredient).Select(x => x.RawLine);
                var web = !DietRecipeText.FromBook(r.SourceUrl, r.ScrapedAtUtc) && !string.IsNullOrEmpty(r.SourceUrl);
                evidence.Add((DietDishWords.Evidence(r.Name, methods.GetValueOrDefault(r.Id), ingredientLines), flagged, web, DietRecipeText.DoughTitled(DietFoodWords.Respell(r.Name))));
            });
            var all = evidence.ToList();
            var rows = DietDishWords.Derive(all.Select(e => (e.Words, e.Flagged)), DietDishWords.AllCorpus)
                .Concat(DietDishWords.Derive(all.Where(e => e.Web).Select(e => (e.Words, e.Flagged)), DietDishWords.WebCorpus))
                .Concat(DietDishWords.DeriveLost(all.Select(e => (e.Words, e.Dough, e.Flagged)), DietDishWords.AllCorpus))
                .Concat(DietDishWords.DeriveLost(all.Where(e => e.Web).Select(e => (e.Words, e.Dough, e.Flagged)), DietDishWords.WebCorpus))
                .ToList();
            return DietDishWords.Write(rows, corpus);
        }
    }
}
