/**
 * Grocery department taxonomy shared by the shopping list and pantry.
 * (Previously two divergent classifiers existed; this word-bounded version is canonical.)
 */

// Department order for store-aisle flow
export const DEPARTMENT_ORDER = [
  'Produce',
  'Meat & Seafood',
  'Dairy & Eggs',
  'Bakery',
  'Grains & Pasta',
  'Canned & Jarred',
  'Condiments & Sauces',
  'Spices & Seasonings',
  'Oils & Vinegars',
  'Baking',
  'Frozen',
  'Beverages',
  'Other',
];

export const DEPARTMENT_ICONS: Record<string, string> = {
  Produce: 'eco',
  'Meat & Seafood': 'set_meal',
  'Dairy & Eggs': 'egg',
  Bakery: 'bakery_dining',
  'Grains & Pasta': 'grain',
  'Canned & Jarred': 'inventory_2',
  'Condiments & Sauces': 'local_dining',
  'Spices & Seasonings': 'spa',
  'Oils & Vinegars': 'water_drop',
  Baking: 'cake',
  Frozen: 'ac_unit',
  Beverages: 'local_cafe',
  Other: 'category',
};

/** Categorize an ingredient name into a grocery department */
export function categorizeDepartment(name: string): string {
  const n = name.toLowerCase();

  // Produce
  if (/\b(lettuce|spinach|kale|arugula|cabbage|bok choy|collard|chard)\b/.test(n)) return 'Produce';
  if (/\b(tomato|onion|garlic|pepper|carrot|celery|potato|sweet potato)\b/.test(n))
    return 'Produce';
  if (/\b(broccoli|cauliflower|zucchini|squash|eggplant|mushroom|corn)\b/.test(n)) return 'Produce';
  if (/\b(cucumber|avocado|bean sprout|scallion|green onion|shallot|leek)\b/.test(n))
    return 'Produce';
  if (/\b(apple|banana|orange|lemon|lime|berry|berries|blueberr|strawberr|raspberr)\b/.test(n))
    return 'Produce';
  if (/\b(grape|mango|pineapple|peach|pear|melon|watermelon|cherry|plum|kiwi)\b/.test(n))
    return 'Produce';
  if (/\b(ginger|cilantro|parsley|basil|mint|dill|rosemary|thyme|chives|jalape)\b/.test(n))
    return 'Produce';
  if (/\b(asparagus|artichoke|beet|radish|turnip|parsnip|fennel|okra|peas)\b/.test(n))
    return 'Produce';
  if (/\b(green bean|snap pea|snow pea|edamame|brussels sprout|watercress)\b/.test(n))
    return 'Produce';

  // Meat & Seafood
  if (/\b(chicken|turkey|beef|pork|lamb|veal|duck|bison|venison)\b/.test(n))
    return 'Meat & Seafood';
  if (
    /\b(steak|ground meat|ground beef|ground turkey|ground pork|sausage|bacon|ham|prosciutto)\b/.test(
      n,
    )
  )
    return 'Meat & Seafood';
  if (
    /\b(salmon|tuna|shrimp|prawn|cod|tilapia|halibut|trout|crab|lobster|scallop|clam|mussel|anchov)\b/.test(
      n,
    )
  )
    return 'Meat & Seafood';
  if (/\b(fish sauce)\b/.test(n)) return 'Condiments & Sauces';
  if (/\b(fish)\b/.test(n)) return 'Meat & Seafood';

  // Dairy & Eggs
  if (/\b(milk|cream|half.and.half|buttermilk|yogurt|kefir|sour cream|cr[eè]me)\b/.test(n))
    return 'Dairy & Eggs';
  if (
    /\b(cheese|parmesan|mozzarella|cheddar|feta|ricotta|gouda|brie|gruy[eè]re|cream cheese)\b/.test(
      n,
    )
  )
    return 'Dairy & Eggs';
  if (/\b(butter|margarine|ghee)\b/.test(n)) return 'Dairy & Eggs';
  if (/\b(egg)\b/.test(n)) return 'Dairy & Eggs';

  // Bakery
  if (
    /\b(bread|baguette|roll|bun|pita|naan|tortilla|wrap|croissant|english muffin|bagel)\b/.test(n)
  )
    return 'Bakery';

  // Grains & Pasta
  if (
    /\b(rice|pasta|spaghetti|penne|linguine|fettuccine|macaroni|noodle|ramen|udon|soba)\b/.test(n)
  )
    return 'Grains & Pasta';
  if (/\b(quinoa|couscous|barley|farro|bulgur|polenta|oat|oatmeal|cereal|granola)\b/.test(n))
    return 'Grains & Pasta';
  if (/\b(lentil|chickpea|black bean|kidney bean|pinto bean|cannellini|navy bean)\b/.test(n))
    return 'Grains & Pasta';

  // Canned & Jarred
  if (/\b(canned|diced tomato|crushed tomato|tomato paste|tomato sauce|salsa|marinara)\b/.test(n))
    return 'Canned & Jarred';
  if (/\b(broth|stock|bouillon|coconut milk|coconut cream)\b/.test(n)) return 'Canned & Jarred';
  if (/\b(peanut butter|almond butter|jam|jelly|preserve|nutella)\b/.test(n))
    return 'Canned & Jarred';

  // Condiments & Sauces
  if (/\b(soy sauce|tamari|worcestershire|hot sauce|sriracha|tabasco)\b/.test(n))
    return 'Condiments & Sauces';
  if (/\b(ketchup|mustard|mayo|mayonnaise|relish|barbecue|teriyaki|hoisin|oyster sauce)\b/.test(n))
    return 'Condiments & Sauces';
  if (/\b(vinegar|dressing|marinade|miso|tahini|harissa|gochujang|sambal)\b/.test(n))
    return 'Condiments & Sauces';
  if (/\b(honey|maple syrup|agave|molasses)\b/.test(n)) return 'Condiments & Sauces';

  // Spices & Seasonings
  if (/\b(salt|pepper|cumin|paprika|chili powder|cayenne|turmeric|cinnamon|nutmeg)\b/.test(n))
    return 'Spices & Seasonings';
  if (/\b(oregano|thyme|rosemary|sage|bay leaf|coriander|cardamom|clove|allspice)\b/.test(n))
    return 'Spices & Seasonings';
  if (
    /\b(garlic powder|onion powder|smoked paprika|red pepper flake|italian season|curry powder)\b/.test(
      n,
    )
  )
    return 'Spices & Seasonings';
  if (/\b(sesame seed|poppy seed|fennel seed|mustard seed|caraway|star anise|saffron)\b/.test(n))
    return 'Spices & Seasonings';

  // Oils & Vinegars
  if (
    /\b(olive oil|vegetable oil|canola oil|coconut oil|sesame oil|avocado oil|cooking spray)\b/.test(
      n,
    )
  )
    return 'Oils & Vinegars';
  if (/\b(balsamic|rice vinegar|apple cider vinegar|red wine vinegar|white wine vinegar)\b/.test(n))
    return 'Oils & Vinegars';

  // Baking
  if (/\b(flour|sugar|brown sugar|powdered sugar|baking soda|baking powder|yeast)\b/.test(n))
    return 'Baking';
  if (/\b(vanilla|cocoa|chocolate chip|cornstarch|corn starch|gelatin|food color)\b/.test(n))
    return 'Baking';
  if (/\b(almond flour|coconut flour|breadcrumb|panko)\b/.test(n)) return 'Baking';

  // Frozen
  if (/\b(frozen|ice cream)\b/.test(n)) return 'Frozen';

  // Beverages
  if (/\b(juice|coffee|tea|water|soda|wine|beer|sparkling)\b/.test(n)) return 'Beverages';

  // Nuts & Seeds (put in Other or a specific dept)
  if (
    /\b(almond|walnut|pecan|cashew|pistachio|peanut|hazelnut|macadamia|pine nut|sunflower seed|pumpkin seed|chia|flax)\b/.test(
      n,
    )
  )
    return 'Other';
  if (/\b(tofu|tempeh|seitan)\b/.test(n)) return 'Other';

  return 'Other';
}
