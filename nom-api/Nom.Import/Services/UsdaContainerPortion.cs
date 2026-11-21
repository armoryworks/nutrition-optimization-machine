using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Nom.Orch.Services.Support;

namespace Nom.Import.Services
{
    /// <summary>One container portion of a USDA food: its kind and size word, net or drained grams, the portion text and its sequence number.</summary>
    public sealed record UsdaContainerSize(string Kind, string SizeWord, decimal? NetGrams, decimal? DrainedGrams, string Portion, string Seq);

    /// <summary>
    /// Reads the container portions of a food's USDA household portions (SR Legacy and Foundation
    /// food_portion.csv): a portion counts only when its unit is the container word itself ("can",
    /// "can (303 x 406)", "package (3 oz)", "envelope", "jar", "packet", "can or bottle (12 fl oz)").
    /// A yield, a prepared or diluted container, a serving, a fraction of the container, a single or
    /// fun-size pack and a "package ... servings" are refused. A portion that says drained, or any
    /// container portion of a food whose description is drained solids, is a drained weight; its net
    /// is the ounces the portion states, when it states them. Portions of one kind and size word
    /// that disagree by more than 10 % give nothing for that key. The sibling of <see cref="UsdaCountWeight"/>,
    /// which throws these portions away.
    /// </summary>
    public static class UsdaContainerPortion
    {
        private static readonly Regex Refused = new(
            @"\b(yields?|yield|per|prepared|reconstituted|diluted|serving|servings|nlea|slice|slices|fun\s+size|single|individual|king\s+size|snack\s+size|bars?|mashed|cooked|mix\s*\+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Ounces = new(@"(?<n>\d+(?:\.\d+)?|\.\d+)\s*(?<fl>fl\.?\s*)?oz\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Drained = new(@"\bdrained\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Net = new(@"\b(solids\s+and\s+liquids?|total\s+can\s+contents|and\s+liquid|with\s+liquid)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<UsdaContainerSize> Sizes(IEnumerable<UsdaPortion> portions, string? foodDescription)
        {
            var drainedFood = ContainerKinds.DescribesDrained(foodDescription);
            var found = new List<UsdaContainerSize>();
            var list = portions.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.Amount != 1m || p.Grams <= 0) continue;
                var text = Text(p);
                if (text.Length == 0 || Refused.IsMatch(text)) continue;
                var first = Regex.Match(text, @"[a-z]+").Value;
                var kind = ContainerKinds.FromText(first);
                if (kind == null || first is "container" or "containers" && !Regex.IsMatch(text, @"\boz\b")) continue;
                var sizeWord = Regex.IsMatch(text, @"\bsmall\b") ? ContainerKinds.Small : Regex.IsMatch(text, @"\blarge\b") ? ContainerKinds.Large : string.Empty;
                var statedOz = Ounces.Match(text) is { Success: true } m && m.Groups["fl"].Length == 0
                    ? decimal.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) * CanSizeTable.GramsPerOunce
                    : (decimal?)null;
                var drained = !Net.IsMatch(text) && (Drained.IsMatch(text) || drainedFood);
                var seq = p.Seq ?? (i + 1).ToString(CultureInfo.InvariantCulture);
                found.Add(drained
                    ? new UsdaContainerSize(kind, sizeWord, statedOz is { } net && net > p.Grams ? Math.Round(net, 1) : null, Math.Round(p.Grams, 1), text, seq)
                    : new UsdaContainerSize(kind, sizeWord, Math.Round(p.Grams, 1), null, text, seq));
            }

            var result = new List<UsdaContainerSize>();
            foreach (var key in found.GroupBy(f => (f.Kind, f.SizeWord)))
            {
                var nets = key.Where(k => k.NetGrams is > 0).ToList();
                var drains = key.Where(k => k.DrainedGrams is > 0).ToList();
                if (!AllAgree(nets.Select(n => n.NetGrams!.Value)) || !AllAgree(drains.Select(d => d.DrainedGrams!.Value))) continue;
                var net = nets.FirstOrDefault();
                var drain = drains.FirstOrDefault();
                result.Add(new UsdaContainerSize(key.Key.Kind, key.Key.SizeWord, net?.NetGrams, drain?.DrainedGrams,
                    string.Join("; ", new[] { net, drain }.Where(x => x != null).Select(x => x!.Portion).Distinct()),
                    (net ?? drain)!.Seq + (net != null && drain != null && drain.Seq != net.Seq ? "+" + drain.Seq : string.Empty)));
            }
            return result;
        }

        private static bool AllAgree(IEnumerable<decimal> values)
        {
            var v = values.ToList();
            return v.Count == 0 || v.All(x => ContainerSizeAgreement.Agree(x, v[0]));
        }

        private static string Text(UsdaPortion p)
        {
            var unit = (p.MeasureUnitName ?? string.Empty).Trim().ToLowerInvariant();
            var modifier = (p.Modifier ?? string.Empty).Trim().ToLowerInvariant();
            var description = (p.Description ?? string.Empty).Trim().ToLowerInvariant();
            var named = unit.Length > 0 && unit != "undetermined";
            return named
                ? string.Join(' ', new[] { unit, modifier, description }.Where(t => t.Length > 0))
                : (modifier.Length > 0 ? modifier : description);
        }
    }
}
