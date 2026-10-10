using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The cheap pre-filter of non-food screening: old household cookbooks mix cleaning, toiletry and
    /// remedy receipts in with the cookery. A recipe is a candidate when its name or an ingredient
    /// looks non-culinary and its name names no food. Only candidates go to the model, and only a
    /// candidate the model also calls non-food is rejected.
    /// </summary>
    public static class NonFoodRecipeFilter
    {
        public static bool IsCandidate(string? name, IEnumerable<string?> ingredientLines)
        {
            if (string.IsNullOrWhiteSpace(name) || FoodName.IsMatch(name)) return false;
            return NonFoodName.IsMatch(name) || ingredientLines.Any(l => !string.IsNullOrWhiteSpace(l) && NonFoodIngredient.IsMatch(l));
        }

        private static readonly Regex NonFoodName = new(
            @"\b(?:clean(?:s|se|ing|er|ers)?|wash(?:es|ing)?|polish(?:es|ing)?|remov(?:e|es|ing|al)|stains?|rust|soaps?|inks?|teeth|tooth|furniture|marble|alabaster|varnish|blacking|dye(?:s|ing)?|starch(?:ing)?|linen|laundry|bleach(?:ing)?|moths?|bugs?|insects?|rats|mice|cockroach(?:es)?|fleas|vermin|mildew|freckles|complexion|hair|pomade|cologne|perfume|lotion|salve|ointment|liniment|poultice|cure\s+for|remed(?:y|ies)|cough|chilblains?|corns|warts|rheumatism|glue|cement|whitewash|putty|leather|boots|shoes|brass|silver|stoves?|grates?|carpets?|paint|windows)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex NonFoodIngredient = new(
            @"\b(?:soap|turpentine|ammonia|borax|chloride\s+of\s+lime|potash|lye|oxalic|pumice|rotten[\s-]?stone|emery|vitriol|muriatic|linseed\s+oil|beeswax|bees'?\s+wax|prepared\s+chalk|tincture|camphor|myrrh|orris|glycerine)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex FoodName = new(
            @"\b(?:soups?|broth|chops?|cutlets?|steaks?|hams?|bacon|beef|pork|mutton|veal|lamb|venison|chickens?|fowls?|ducks?|goose|turkey|fish|salmon|cod|oysters?|lobsters?|crabs?|beets?|potato(?:es)?|cabbage|cakes?|pies?|puddings?|bread|biscuits?|sauces?|gravy|jelly|jam|pickles?|pickled|sausages?|cheese|eggs?|stew(?:ed)?|roast(?:ed)?|tarts?|custard|rice|meat|dumplings?|pancakes?|fritters?|salad|vegetables?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
