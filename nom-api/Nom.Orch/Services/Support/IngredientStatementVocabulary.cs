using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The ingredients a statement may name that are not foods of the catalog but are known for what they are: water,
    /// salt, food additives, vitamins and minerals added by name, cultures and enzymes. A recognised term is judged by
    /// the gate on its own text, exactly as a recipe line naming it would be, so a term that could hide a restricted food
    /// does not belong here. A compound whose parts the statement lists ("korean bbq sauce (...)", "enriched flour (...)")
    /// is known by those parts, which the gate reads one by one, while its own name is still read for the diet patterns.
    /// The list is part of the reader's version (<see cref="IngredientStatementReader.Version"/>).
    /// </summary>
    public static class IngredientStatementVocabulary
    {
        /// <summary>The terms recognised as they stand, lower-cased.</summary>
        public static readonly IReadOnlySet<string> Terms = new HashSet<string>(StringComparer.Ordinal)
        {
            "water", "filtered water", "purified water", "carbonated water", "ice", "salt", "sea salt", "kosher salt", "iodized salt", "salt substitute",
            "citric acid", "ascorbic acid", "lactic acid", "malic acid", "acetic acid", "fumaric acid", "phosphoric acid", "tartaric acid", "succinic acid", "sorbic acid", "erythorbic acid",
            "xanthan gum", "guar gum", "locust bean gum", "gum arabic", "acacia gum", "gellan gum", "cellulose gum", "carrageenan", "pectin", "agar", "konjac", "methylcellulose",
            "hydroxypropyl methylcellulose", "microcrystalline cellulose", "powdered cellulose", "cellulose", "sodium alginate", "propylene glycol alginate",
            "sodium phosphate", "sodium phosphates", "disodium phosphate", "trisodium phosphate", "sodium tripolyphosphate", "sodium hexametaphosphate", "tetrasodium pyrophosphate",
            "sodium acid pyrophosphate", "sodium aluminum phosphate", "monocalcium phosphate", "dicalcium phosphate", "tricalcium phosphate", "calcium phosphate", "potassium phosphate", "dipotassium phosphate",
            "sodium bicarbonate", "baking soda", "potassium bicarbonate", "ammonium bicarbonate", "sodium carbonate", "potassium carbonate", "calcium carbonate", "magnesium carbonate",
            "sodium benzoate", "potassium benzoate", "potassium sorbate", "calcium sorbate", "calcium propionate", "sodium propionate", "sodium diacetate", "sodium acetate", "potassium acetate",
            "sodium lactate", "potassium lactate", "calcium lactate", "sodium citrate", "potassium citrate", "calcium citrate", "sodium nitrite", "sodium nitrate", "potassium nitrate",
            "sodium erythorbate", "sodium ascorbate", "calcium ascorbate", "calcium chloride", "potassium chloride", "magnesium chloride", "calcium sulfate", "magnesium sulfate", "ferrous sulfate",
            "zinc sulfate", "zinc oxide", "magnesium oxide", "copper sulfate", "potassium iodide", "sodium selenite", "silicon dioxide", "titanium dioxide", "calcium silicate",
            "sodium metabisulfite", "sodium bisulfite", "sodium sulfite", "potassium metabisulfite", "sulfur dioxide",
            "monosodium glutamate", "msg", "disodium inosinate", "disodium guanylate", "disodium inosinate and guanylate",
            "bht", "bha", "tbhq", "tocopherols", "mixed tocopherols", "natural tocopherols", "rosemary extract", "green tea extract",
            "polysorbate 60", "polysorbate 80", "sodium stearoyl lactylate", "calcium stearoyl lactylate", "datem", "propylene glycol", "glycerin", "glycerol", "sorbitol", "mannitol", "xylitol", "erythritol",
            "sucralose", "aspartame", "acesulfame potassium", "stevia", "stevia leaf extract", "rebaudioside a", "monk fruit extract", "neotame", "saccharin",
            "niacin", "niacinamide", "reduced iron", "iron", "electrolytic iron", "thiamine mononitrate", "thiamin mononitrate", "thiamine hydrochloride", "thiamin hydrochloride", "thiamin", "thiamine", "riboflavin",
            "folic acid", "pyridoxine hydrochloride", "pyridoxine hcl", "cyanocobalamin", "vitamin b12", "vitamin a palmitate", "vitamin a acetate", "vitamin d3", "vitamin d2", "cholecalciferol",
            "vitamin e acetate", "dl-alpha tocopheryl acetate", "d-calcium pantothenate", "calcium pantothenate", "biotin", "beta carotene", "vitamin c", "vitamin a", "vitamin d", "vitamin e",
            "caramel color", "annatto", "annatto extract", "annatto color", "paprika extract", "paprika oleoresin", "turmeric extract", "turmeric oleoresin", "beet juice color", "spirulina extract",
            "color", "colors", "artificial color", "artificial colors", "fd&c red 40", "red 40", "yellow 5", "yellow 6", "blue 1", "fd&c yellow 5", "fd&c yellow 6", "fd&c blue 1",
            "natural flavor", "natural flavors", "natural flavoring", "natural flavorings", "artificial flavor", "artificial flavors", "artificial flavoring", "flavor", "flavors", "flavoring", "flavorings",
            "natural and artificial flavor", "natural and artificial flavors", "smoke flavor", "smoke flavoring", "natural smoke flavor", "natural smoke flavoring", "liquid smoke",
            "spice", "spices", "spice extractives", "spice extract", "extractives of paprika", "extractives of turmeric", "oleoresin of paprika",
            "yeast", "active dry yeast", "baker's yeast", "nutritional yeast", "yeast extract", "autolyzed yeast extract", "autolyzed yeast", "torula yeast",
            "enzyme", "enzymes", "cultures", "culture", "bacterial culture", "bacterial cultures", "lactic acid starter culture", "cheese culture", "cheese cultures", "microbial enzymes", "vegetable enzymes",
            "mono and diglycerides", "mono- and diglycerides", "monoglycerides", "diglycerides", "ethoxylated mono and diglycerides", "soy lecithin", "sunflower lecithin", "lecithin",
            "dextrose", "maltodextrin", "maltodextrins", "corn syrup solids", "modified food starch", "modified corn starch", "modified tapioca starch", "modified potato starch", "food starch-modified", "food starch modified",
            "sodium caseinate", "calcium caseinate", "sodium chloride", "vinegar powder", "dried vinegar", "cultured dextrose", "cultured sugar", "fermented sugar",
            "inulin", "chicory root fiber", "oat fiber", "citrus fiber", "pea fiber", "bamboo fiber",
        };

        /// <summary>The most words a compound's name may have for its listed parts to make it known; a longer name usually hides a missing comma.</summary>
        public const int MaxCompoundWords = 6;

        /// <summary>Whether a named ingredient is known without a catalog food: a listed term, or a compound whose parts the statement lists and whose name is no longer than <see cref="MaxCompoundWords"/> words.</summary>
        public static bool Recognises(StatementIngredient ingredient) =>
            Terms.Contains(ingredient.Name)
            || (ingredient.HasParts && ingredient.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxCompoundWords);
    }
}
