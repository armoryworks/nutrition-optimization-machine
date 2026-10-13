using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Data.Reference
{
    /// <summary>How readily an owned tool stands in for a missing one.</summary>
    public enum KitchenToolSubstitution
    {
        Easy,
        Harder,
    }

    /// <summary>An owned tool that can do the job of a missing one, with the advice shown to the cook.</summary>
    public sealed record KitchenToolAlternative(long ToolId, KitchenToolSubstitution Difficulty, string Note);

    /// <summary>A kitchen tool or appliance a recipe may need and a household may own.</summary>
    public sealed record KitchenToolDefinition(
        long Id,
        string Key,
        string Name,
        string Category,
        bool OwnedByDefault,
        IReadOnlyList<KitchenToolAlternative> Alternatives,
        Regex? Detect);

    /// <summary>
    /// Canonical kitchen-tool vocabulary (ids match the seeded reference rows in group
    /// <see cref="ReferenceDiscriminatorEnum.KitchenToolType"/>), which tools a household is
    /// assumed to own until told otherwise, which owned tools can stand in for a missing one,
    /// and keyword detection of the tools a recipe needs from its name and step text.
    /// </summary>
    public static class KitchenToolCatalog
    {
        public const long Stovetop = 61101;
        public const long Oven = 61102;
        public const long Microwave = 61103;
        public const long SheetPan = 61104;
        public const long BakingDish = 61105;
        public const long Blender = 61106;
        public const long InstantPot = 61107;
        public const long SlowCooker = 61108;
        public const long AirFryer = 61109;
        public const long SousVide = 61110;
        public const long FoodProcessor = 61111;
        public const long ImmersionBlender = 61112;
        public const long StandMixer = 61113;
        public const long HandMixer = 61114;
        public const long Grill = 61115;
        public const long Smoker = 61116;
        public const long DeepFryer = 61117;
        public const long DutchOven = 61118;
        public const long CastIronSkillet = 61119;
        public const long Wok = 61120;
        public const long RiceCooker = 61121;
        public const long ToasterOven = 61122;
        public const long WaffleIron = 61123;
        public const long Dehydrator = 61124;
        public const long IceCreamMaker = 61125;
        public const long PastaMachine = 61126;
        public const long BreadMachine = 61127;
        public const long MuffinTin = 61128;
        public const long LoafPan = 61129;
        public const long SpringformPan = 61130;
        public const long Thermometer = 61131;
        public const long KitchenScale = 61132;
        public const long MortarAndPestle = 61133;
        public const long Mandoline = 61134;

        public const string CategoryCooking = "Stove & oven";
        public const string CategoryAppliances = "Appliances";
        public const string CategoryCookware = "Cookware & bakeware";
        public const string CategoryOutdoor = "Outdoor";
        public const string CategoryGadgets = "Gadgets";

        private const RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static KitchenToolAlternative Alt(long id, KitchenToolSubstitution d, string note) => new(id, d, note);

        public static readonly IReadOnlyList<KitchenToolDefinition> All = new List<KitchenToolDefinition>
        {
            new(Stovetop, "stovetop", "Stovetop", CategoryCooking, true,
                new[] { Alt(InstantPot, KitchenToolSubstitution.Easy, "Use your Instant Pot's sauté or pressure setting instead of the stovetop.") },
                new Regex(@"\b(stove ?top|stove|saucepan|skillet|frying pan|simmer|saut[eé]|boil|(low|medium|high)[- ](low |high )?heat)\b", Rx)),
            new(Oven, "oven", "Oven", CategoryCooking, true,
                new[]
                {
                    Alt(ToasterOven, KitchenToolSubstitution.Easy, "A toaster oven works here — bake in smaller batches if it doesn't fit."),
                    Alt(AirFryer, KitchenToolSubstitution.Harder, "Your air fryer can stand in for the oven, in small batches and with a shorter time."),
                },
                new Regex(@"\b(oven|preheat|bake[ds]?|baking|roast(ed|ing)?|broil(ed|er)?)\b", Rx)),
            new(Microwave, "microwave", "Microwave", CategoryCooking, true,
                new[] { Alt(Stovetop, KitchenToolSubstitution.Easy, "Warm it on the stovetop instead of the microwave.") },
                new Regex(@"\bmicrowav(e|ed|ing)\b", Rx)),
            new(SheetPan, "sheet-pan", "Sheet pan", CategoryCookware, true,
                new[] { Alt(BakingDish, KitchenToolSubstitution.Easy, "A large baking dish works instead of a sheet pan.") },
                new Regex(@"\b(sheet pan|baking sheet|cookie sheet)\b", Rx)),
            new(BakingDish, "baking-dish", "Baking dish", CategoryCookware, true,
                new[] { Alt(DutchOven, KitchenToolSubstitution.Easy, "An oven-safe Dutch oven works instead of a baking dish.") },
                new Regex(@"\b(baking dish|casserole dish|9 ?x ?13)\b", Rx)),
            new(Blender, "blender", "Blender", CategoryAppliances, true,
                new[]
                {
                    Alt(ImmersionBlender, KitchenToolSubstitution.Easy, "An immersion blender does the job — blend in a tall container."),
                    Alt(FoodProcessor, KitchenToolSubstitution.Easy, "Your food processor can blend this."),
                },
                new Regex(@"(?<!immersion |stick )\bblender\b|\bblend (until|on)\b", Rx)),
            new(InstantPot, "instant-pot", "Instant Pot / pressure cooker", CategoryAppliances, false,
                new[] { Alt(Stovetop, KitchenToolSubstitution.Harder, "No pressure cooker? Simmer it on the stovetop — expect it to take several times longer.") },
                new Regex(@"\b(instant ?pot|pressure[- ]cook(er|ed|ing)?|multi[- ]?cooker)\b", Rx)),
            new(SlowCooker, "slow-cooker", "Slow cooker", CategoryAppliances, false,
                new[]
                {
                    Alt(InstantPot, KitchenToolSubstitution.Easy, "Use your Instant Pot's slow-cook setting."),
                    Alt(DutchOven, KitchenToolSubstitution.Easy, "Cook it covered in a Dutch oven in a low oven instead of a slow cooker."),
                    Alt(Stovetop, KitchenToolSubstitution.Harder, "Simmer it on the lowest stovetop heat and stir now and then instead of using a slow cooker."),
                },
                new Regex(@"\b(slow[- ]cook(er|ed)?|crock[- ]?pot)\b", Rx)),
            new(AirFryer, "air-fryer", "Air fryer", CategoryAppliances, false,
                new[] { Alt(Oven, KitchenToolSubstitution.Easy, "Use a hot oven instead of the air fryer — it'll take a little longer.") },
                new Regex(@"\bair[- ]?fr(y|yer|ied|ying)\b", Rx)),
            new(SousVide, "sous-vide", "Sous vide", CategoryAppliances, false,
                new[] { Alt(Thermometer, KitchenToolSubstitution.Harder, "Without a sous vide you can hold a pot of water at temperature with a thermometer — doable, but fiddly.") },
                new Regex(@"\bsous[- ]vide\b", Rx)),
            new(FoodProcessor, "food-processor", "Food processor", CategoryAppliances, false,
                new[] { Alt(Blender, KitchenToolSubstitution.Harder, "A blender can stand in for the food processor — pulse in small batches.") },
                new Regex(@"\bfood processor\b", Rx)),
            new(ImmersionBlender, "immersion-blender", "Immersion blender", CategoryAppliances, false,
                new[] { Alt(Blender, KitchenToolSubstitution.Easy, "Blend it in batches in your blender instead.") },
                new Regex(@"\b(immersion|stick) blender\b", Rx)),
            new(StandMixer, "stand-mixer", "Stand mixer", CategoryAppliances, false,
                new[] { Alt(HandMixer, KitchenToolSubstitution.Easy, "A hand mixer works instead of a stand mixer.") },
                new Regex(@"\b(stand mixer|paddle attachment|dough hook|whisk attachment)\b", Rx)),
            new(HandMixer, "hand-mixer", "Hand mixer", CategoryAppliances, false,
                new[] { Alt(StandMixer, KitchenToolSubstitution.Easy, "Your stand mixer works instead of a hand mixer.") },
                new Regex(@"\b(hand mixer|electric mixer|electric beaters?)\b", Rx)),
            new(Grill, "grill", "Grill", CategoryOutdoor, false,
                new[]
                {
                    Alt(CastIronSkillet, KitchenToolSubstitution.Harder, "No grill? Sear it in a very hot cast-iron skillet."),
                    Alt(Oven, KitchenToolSubstitution.Harder, "No grill? Use the oven broiler and watch it closely."),
                },
                new Regex(@"(?<!grilled )\b(grill|grilling|gas grill|charcoal)\b(?! pan)", Rx)),
            new(Smoker, "smoker", "Smoker", CategoryOutdoor, false,
                new[] { Alt(Grill, KitchenToolSubstitution.Harder, "You can smoke on a grill with wood chips over indirect heat.") },
                new Regex(@"\b(smoker|wood chips)\b", Rx)),
            new(DeepFryer, "deep-fryer", "Deep fryer", CategoryAppliances, false,
                new[]
                {
                    Alt(DutchOven, KitchenToolSubstitution.Easy, "Deep-fry in a heavy Dutch oven with a few inches of oil."),
                    Alt(Stovetop, KitchenToolSubstitution.Harder, "Deep-fry in a deep, heavy pot on the stove — keep a lid nearby and watch the oil temperature."),
                },
                new Regex(@"\bdeep[- ]?fr(y|yer|ied|ying)\b", Rx)),
            new(DutchOven, "dutch-oven", "Dutch oven", CategoryCookware, false,
                new[] { Alt(Stovetop, KitchenToolSubstitution.Harder, "Use your heaviest lidded pot instead of a Dutch oven.") },
                new Regex(@"\bdutch oven\b", Rx)),
            new(CastIronSkillet, "cast-iron", "Cast-iron skillet", CategoryCookware, false,
                new[] { Alt(Stovetop, KitchenToolSubstitution.Easy, "Any heavy skillet works instead of cast iron.") },
                new Regex(@"\bcast[- ]iron\b", Rx)),
            new(Wok, "wok", "Wok", CategoryCookware, false,
                new[] { Alt(Stovetop, KitchenToolSubstitution.Easy, "Use your largest skillet instead of a wok.") },
                new Regex(@"\bwok\b", Rx)),
            new(RiceCooker, "rice-cooker", "Rice cooker", CategoryAppliances, false,
                new[]
                {
                    Alt(InstantPot, KitchenToolSubstitution.Easy, "Use your Instant Pot's rice setting."),
                    Alt(Stovetop, KitchenToolSubstitution.Easy, "Cook the rice in a covered pot on the stove."),
                },
                new Regex(@"\brice cooker\b", Rx)),
            new(ToasterOven, "toaster-oven", "Toaster oven", CategoryAppliances, false,
                new[] { Alt(Oven, KitchenToolSubstitution.Easy, "Use your oven instead of a toaster oven.") },
                new Regex(@"\btoaster oven\b", Rx)),
            new(WaffleIron, "waffle-iron", "Waffle iron", CategoryAppliances, false,
                Array.Empty<KitchenToolAlternative>(),
                new Regex(@"\bwaffle (iron|maker)\b", Rx)),
            new(Dehydrator, "dehydrator", "Dehydrator", CategoryAppliances, false,
                new[] { Alt(Oven, KitchenToolSubstitution.Harder, "Use the oven at its lowest setting with the door cracked open instead of a dehydrator.") },
                new Regex(@"\bdehydrator\b", Rx)),
            new(IceCreamMaker, "ice-cream-maker", "Ice cream maker", CategoryAppliances, false,
                Array.Empty<KitchenToolAlternative>(),
                new Regex(@"\b(ice cream maker|ice cream machine)\b", Rx)),
            new(PastaMachine, "pasta-machine", "Pasta machine", CategoryGadgets, false,
                Array.Empty<KitchenToolAlternative>(),
                new Regex(@"\bpasta (machine|roller)\b", Rx)),
            new(BreadMachine, "bread-machine", "Bread machine", CategoryAppliances, false,
                new[] { Alt(Oven, KitchenToolSubstitution.Harder, "Knead by hand and bake it in the oven instead of a bread machine.") },
                new Regex(@"\bbread (machine|maker)\b", Rx)),
            new(MuffinTin, "muffin-tin", "Muffin tin", CategoryCookware, false,
                new[] { Alt(BakingDish, KitchenToolSubstitution.Harder, "Bake it as one tray in a baking dish and cut into squares — it'll need longer.") },
                new Regex(@"\b(muffin|cupcake) (tin|pan|cups?)\b", Rx)),
            new(LoafPan, "loaf-pan", "Loaf pan", CategoryCookware, false,
                new[] { Alt(BakingDish, KitchenToolSubstitution.Harder, "Use a small baking dish instead of a loaf pan and check it early.") },
                new Regex(@"\bloaf (pan|tin)\b", Rx)),
            new(SpringformPan, "springform-pan", "Springform pan", CategoryCookware, false,
                new[] { Alt(BakingDish, KitchenToolSubstitution.Harder, "Line a baking dish with parchment so you can lift it out instead of using a springform pan.") },
                new Regex(@"\bspring ?form\b", Rx)),
            new(Thermometer, "thermometer", "Kitchen thermometer", CategoryGadgets, false,
                Array.Empty<KitchenToolAlternative>(),
                new Regex(@"\bthermometer\b", Rx)),
            new(KitchenScale, "kitchen-scale", "Kitchen scale", CategoryGadgets, false,
                Array.Empty<KitchenToolAlternative>(),
                new Regex(@"\b(kitchen|digital|food) scale\b", Rx)),
            new(MortarAndPestle, "mortar-and-pestle", "Mortar and pestle", CategoryGadgets, false,
                new[]
                {
                    Alt(FoodProcessor, KitchenToolSubstitution.Easy, "Pulse it in your food processor instead of a mortar and pestle."),
                    Alt(Blender, KitchenToolSubstitution.Easy, "Pulse it in your blender instead of a mortar and pestle."),
                },
                new Regex(@"\bmortar and pestle\b", Rx)),
            new(Mandoline, "mandoline", "Mandoline", CategoryGadgets, false,
                new[] { Alt(FoodProcessor, KitchenToolSubstitution.Easy, "Use the food processor's slicing disc instead of a mandoline.") },
                new Regex(@"\bmandoline\b", Rx)),
        };

        public static readonly IReadOnlyDictionary<long, KitchenToolDefinition> ById = All.ToDictionary(t => t.Id);

        private static readonly Regex Shadowing = new(
            @"\b(dutch oven|toaster oven|oven[- ]safe|oven mitts?|baking (soda|powder|chocolate|spices?)|(fire|dry)[- ]roasted|roasted (red )?peppers|roasted garlic)\b", Rx);

        public const string CategoryOther = "Specialty equipment";

        /// <summary>
        /// A tool an admin approved from the AI lane's suggestions: stored only as a reference row,
        /// assumed not owned, no stand-ins, detected by its own name in recipe text.
        /// </summary>
        public static KitchenToolDefinition Approved(long id, string name, string? category)
        {
            var key = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            var pattern = @"\b" + Regex.Escape(name.ToLowerInvariant()).Replace(@"\ ", @"[\s-]+") + @"(e?s)?\b";
            return new KitchenToolDefinition(id, key, name, string.IsNullOrWhiteSpace(category) ? CategoryOther : category,
                false, Array.Empty<KitchenToolAlternative>(), new Regex(pattern, Rx));
        }

        /// <summary>
        /// Tool ids a recipe appears to need, inferred from its name and step text. Phrases
        /// naming one tool inside another ("Dutch oven") are masked so they don't also imply
        /// the other ("oven").
        /// </summary>
        public static IReadOnlySet<long> Detect(string? name, IEnumerable<string?> stepTexts, IReadOnlyDictionary<long, KitchenToolDefinition>? tools = null)
        {
            var text = string.Join("\n", new[] { name }.Concat(stepTexts).Where(s => !string.IsNullOrWhiteSpace(s)));
            var found = new HashSet<long>();
            if (text.Length == 0) return found;

            foreach (var tool in (tools ?? ById).Values)
            {
                if (tool.Detect == null) continue;
                var haystack = tool.Id == Oven || tool.Id == Stovetop ? Shadowing.Replace(text, " ") : text;
                if (tool.Detect.IsMatch(haystack)) found.Add(tool.Id);
            }
            return found;
        }
    }
}
