/**
 * Grocery department taxonomy shared by the shopping list and pantry.
 * (Previously two divergent classifiers existed; this word-bounded version is canonical.)
 */

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
  'Nuts & Dried Fruit',
  'Frozen',
  'Beverages',
  'Household',
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
  'Nuts & Dried Fruit': 'nutrition',
  Frozen: 'ac_unit',
  Beverages: 'local_cafe',
  Household: 'cleaning_services',
  Other: 'category',
};

/** Non-food staples: never a catalog food, never asked about, filed in their own aisle. */
export const HOUSEHOLD_DEPARTMENT = 'Household';

const FOOD_GROUP_DEPARTMENTS: Record<string, string> = {
  vegetables: 'Produce',
  fruits: 'Produce',
  grains: 'Grains & Pasta',
  'protein foods': 'Meat & Seafood',
  dairy: 'Dairy & Eggs',
  'fats/oils': 'Oils & Vinegars',
  legumes: 'Grains & Pasta',
  'nuts/seeds': 'Nuts & Dried Fruit',
  beverages: 'Beverages',
};

const UNCHANGED_PLURALS = new Set([
  'asparagus',
  'brussels',
  'citrus',
  'couscous',
  'grits',
  'hummus',
  'molasses',
  'swiss',
]);

const IRREGULAR_PLURALS: Record<string, string> = {
  leaves: 'leaf',
  loaves: 'loaf',
  halves: 'half',
  cookies: 'cookie',
  brownies: 'brownie',
  veggies: 'veggie',
  smoothies: 'smoothie',
};

function singular(word: string): string {
  if (word.length < 4 || UNCHANGED_PLURALS.has(word)) return word;
  if (IRREGULAR_PLURALS[word]) return IRREGULAR_PLURALS[word];
  if (word.length > 4 && word.endsWith('ies')) return word.slice(0, -3) + 'y';
  if (word.endsWith('oes')) return word.slice(0, -2);
  if (/(ch|sh|x|z)es$/.test(word)) return word.slice(0, -2);
  if (/(ss|us)$/.test(word)) return word;
  if (word.endsWith('s')) return word.slice(0, -1);
  return word;
}

function singularize(text: string): string {
  return text.replace(/[a-zà-ÿ]+/g, singular);
}

function searchText(name: string): { text: string; packaged: boolean } {
  const lower = name.toLowerCase();
  const brackets = (lower.match(/\[[^\]]*\]|\([^)]*\)/g) ?? []).join(' ');
  const bare = lower.replace(/\[[^\]]*\]|\([^)]*\)/g, ' ');
  const parts = bare
    .split(',')
    .map((p) => p.trim())
    .filter(Boolean);
  const natural = [...parts].reverse().join(' ');
  const text = singularize(`${parts.join(' ')} | ${natural}`).replace(/\s+/g, ' ');
  const packaged = /\b(cans?|canned|jars?|jarred|tins?|tinned)\b/.test(`${bare} ${brackets}`);
  return { text, packaged };
}

type Rule = [RegExp, string];

const RULES: Rule[] = [
  [
    /\b(paper towel|toilet paper|toilet roll|tissue|kleenex|napkin|paper plate|paper cup|plastic cup|cutlery|straw|coffee filter)\b/,
    HOUSEHOLD_DEPARTMENT,
  ],
  [
    /\b(dish soap|dishwasher|dishwashing|detergent|laundry|fabric softener|dryer sheet|bleach|cleaner|cleaning|disinfectant|wipe|sponge|scrub|soap|hand wash|sanitizer|sanitiser)\b/,
    HOUSEHOLD_DEPARTMENT,
  ],
  [
    /\b(trash bag|garbage bag|bin bag|bin liner|aluminum foil|aluminium foil|tin foil|foil|plastic wrap|cling film|cling wrap|saran|parchment|wax paper|baking paper|ziploc|zip ?lock|freezer bag|sandwich bag|storage bag|food storage|container|light ?bulb|batter(y|ie)|candle|match(es)?|lighter)\b/,
    HOUSEHOLD_DEPARTMENT,
  ],
  [
    /\b(shampoo|conditioner|toothpaste|toothbrush|floss|mouthwash|deodorant|razor|shaving|lotion|sunscreen|body wash|q-?tip|band-?aid|bandage|tampon|diaper|nappy|nappies|baby wipe|vitamin|supplement|ibuprofen|tylenol|advil|aspirin|medicine|pain reliever)\b/,
    HOUSEHOLD_DEPARTMENT,
  ],
  [/\b(dog|cat|pet|puppy|kitten) (food|treat|chow|litter|toy)\b|\b(kibble|cat litter|birdseed|bird seed)\b/, HOUSEHOLD_DEPARTMENT],
  [/\b(frozen|ice cream|sorbet|gelato|popsicle)\b/, 'Frozen'],
  [/\b(potato|tortilla|corn|pita|plantain|veggie) chip\b|\b(pretzel|popcorn)\b/, 'Other'],
  [/\b(spaghetti squash|cauliflower rice|riced cauliflower|zucchini noodle|sugar snap pea|snap pea|snow pea|green bean|string bean|snap bean|(mustard|turnip|beet|collard|dandelion) green)\b/, 'Produce'],
  [/(?<!\bin )\b(olive|vegetable|canola|coconut|sesame|avocado|peanut|sunflower|grapeseed|walnut|truffle) oil\b|\bcooking spray\b/, 'Oils & Vinegars'],
  [/\b(balsamic|vinegar)\b/, 'Oils & Vinegars'],
  [/\b(apple|orange|grape|cranberry|pineapple|tomato|vegetable|prune) juice\b|\b(coconut water|ginger ale|ginger beer|root beer)\b/, 'Beverages'],

  [/\b(peanut|almond|cashew|sunflower seed|sunflower|apple|hazelnut) butter\b/, 'Canned & Jarred'],
  [/\b(nutella|jam|jelly|preserve|marmalade|fruit spread)\b/, 'Canned & Jarred'],
  [/\b(coconut milk|coconut cream|evaporated milk|condensed milk)\b/, 'Canned & Jarred'],
  [/\b(almond|oat|soy|rice|cashew|hemp|plant|pea) milk\b/, 'Beverages'],
  [/\b(broth|stock|bouillon|soup|chili con carne)\b/, 'Canned & Jarred'],
  [
    /\b(diced|crushed|stewed|whole peeled|fire.roasted|sun.dried) tomato\b|\btomato (paste|sauce|puree|soup)\b|\b(marinara|pasta sauce|pizza sauce|salsa|enchilada sauce)\b/,
    'Canned & Jarred',
  ],
  [/\b(roasted red pepper|pimiento|olive|caper|pickle|sauerkraut|kimchi|artichoke heart|water chestnut|bamboo shoot)\b/, 'Canned & Jarred'],
  [/\b(creamed corn|refried bean|baked bean|pumpkin puree|canned pumpkin|applesauce|apple sauce|adobo)\b/, 'Canned & Jarred'],

  [/\b(rice noodle|egg noodle|noodle|pasta|spaghetti|penne|linguine|fettuccine|macaroni|rigatoni|orzo|lasagna|ramen|udon|soba|gnocchi)\b/, 'Grains & Pasta'],

  [/\b(cream of tartar|baking soda|baking powder|yeast|cornstarch|corn ?starch|arrowroot|gelatin|food colou?ring|sprinkle)\b/, 'Baking'],
  [/\bvanilla (extract|bean|paste|essence)\b|\bextract\b/, 'Baking'],
  [/\b(cocoa|chocolate chip|baking chocolate|dark chocolate|semi.sweet chocolate|unsweetened chocolate|chocolate chunk)\b/, 'Baking'],
  [/\b(flour|cornmeal|corn meal|breadcrumb|bread crumb|panko|shortening)\b/, 'Baking'],
  [/\b(sugar|corn syrup|shredded coconut|coconut flake|marshmallow)\b/, 'Baking'],

  [
    /\b(black|white|pink|ground|cracked|lemon) pepper\b|\bpeppercorn\b|\b(red )?pepper flake\b|\bcrushed red pepper\b|\bchili flake\b|\bcayenne\b/,
    'Spices & Seasonings',
  ],
  [/\b(garlic|onion|chili|chile|curry|ginger|mustard|five spice|wasabi) powder\b|\bgarlic salt\b|\bonion salt\b|\bcelery salt\b/, 'Spices & Seasonings'],
  [/\bground (cinnamon|ginger|cumin|coriander|nutmeg|clove|allspice|cardamom|turmeric|mustard)\b/, 'Spices & Seasonings'],
  [/\bdried (basil|oregano|thyme|rosemary|dill|parsley|sage|mint|tarragon|marjoram|chive|herb)\b/, 'Spices & Seasonings'],
  [/\b(seasoning|spice|herbes de provence|za.atar|garam masala|bay leaf)\b/, 'Spices & Seasonings'],
  [/\b(sesame seed|poppy seed|fennel seed|mustard seed|celery seed|cumin seed|caraway|star anise|saffron)\b/, 'Spices & Seasonings'],

  [/\b(soy sauce|tamari|coconut amino|worcestershire|hot sauce|sriracha|tabasco|fish sauce|oyster sauce|hoisin|teriyaki|barbecue|bbq|steak sauce)\b/, 'Condiments & Sauces'],
  [/\b(ketchup|catsup|mustard|curry paste|mayo|mayonnaise|aioli|relish|dressing|marinade|miso|tahini|harissa|gochujang|sambal|pesto|chutney|horseradish)\b/, 'Condiments & Sauces'],
  [/\b(honey|maple syrup|agave|molasses|syrup)\b/, 'Condiments & Sauces'],

  [/\b(corn tortilla|flour tortilla|tortilla|bread|baguette|roll|bun|pita|naan|wrap|croissant|english muffin|muffin|bagel|flatbread|brioche|ciabatta|sourdough)\b/, 'Bakery'],
  [/\b(rice|quinoa|couscous|barley|farro|bulgur|polenta|grits|oat|oatmeal|cereal|granola|millet|buckwheat|amaranth|cracker|rice cake|corn flake|cornflake|bran flake)\b/, 'Grains & Pasta'],
  [/\b(lentil|chickpea|garbanzo|split pea|black.eyed pea|(black|kidney|pinto|navy|cannellini|great northern|lima|butter|white|red|mung|adzuki|fava|black turtle) bean)\b/, 'Grains & Pasta'],
  [/\b(raisin|sultana|currant|prune|date|dried (fruit|cranberry|apricot|cherry|mango|fig|blueberry))\b/, 'Nuts & Dried Fruit'],

  [
    /\b(bell|sweet|red|green|yellow|orange|jalape\S*|poblano|serrano|habanero|anaheim|banana|chile|chili|hot|thai) pepper\b|\b(green|red|hatch) chile\b|\bjalape|\bpoblano\b|\bhabanero\b|\bserrano\b/,
    'Produce',
  ],
  [/\b(lettuce|spinach|kale|arugula|cabbage|bok choy|collard|chard|romaine|mixed green|salad green|spring mix|microgreen|endive|radicchio)\b/, 'Produce'],
  [/\b(tomato|onion|garlic|carrot|celery|potato|sweet potato|yam|pumpkin(?! seed))\b/, 'Produce'],
  [/\b(broccoli|broccolini|cauliflower|zucchini|squash|eggplant|mushroom|corn|cremini|portobello|shiitake)\b/, 'Produce'],
  [/\b(cucumber|avocado|bean sprout|sprout|scallion|green onion|shallot|leek)\b/, 'Produce'],
  [/\b(apple|banana|orange|lemon|lime|grapefruit|clementine|tangerine|berry|blueberry|strawberry|raspberry|blackberry|cranberry)\b/, 'Produce'],
  [/\b(grape|mango|pineapple|peach|nectarine|pear|melon|watermelon|cantaloupe|honeydew|cherry|plum|kiwi|papaya|pomegranate|apricot|fig|coconut)\b/, 'Produce'],
  [/\b(ginger|cilantro|parsley|basil|mint|dill|rosemary|thyme|chive|lemongrass|jalape)\b/, 'Produce'],
  [/\b(asparagus|artichoke|beet|radish|turnip|parsnip|fennel|okra|pea|rutabaga|jicama|kohlrabi)\b/, 'Produce'],
  [/\b(green bean|string bean|snap pea|snow pea|edamame|brussels sprout|watercress)\b/, 'Produce'],

  [/\bgoat (cheese|milk|yogurt)\b/, 'Dairy & Eggs'],
  [/\b(chicken|turkey|beef|pork|lamb|veal|duck|bison|venison|goat)\b/, 'Meat & Seafood'],
  [/\b(steak|ground meat|sausage|bacon|ham|prosciutto|pancetta|chorizo|salami|pepperoni|hot dog|meatball|brisket|tenderloin|chop|rib)\b/, 'Meat & Seafood'],
  [/\b(salmon|tuna|shrimp|prawn|cod|tilapia|halibut|trout|crab|lobster|scallop|clam|mussel|oyster|anchovy|sardine|mahi|catfish|haddock|pollock|squid|calamari|fish)\b/, 'Meat & Seafood'],

  [/\b(milk|cream|half.and.half|buttermilk|yogurt|yoghurt|kefir|sour cream|cr[eè]me fra[iî]che|whipped cream|creamer)\b/, 'Dairy & Eggs'],
  [/\b(cheese|parmesan|mozzarella|cheddar|feta|ricotta|gouda|brie|gruy[eè]re|swiss|provolone|monterey jack|cottage cheese|mascarpone|goat cheese|halloumi|paneer)\b/, 'Dairy & Eggs'],
  [/\b(butter|margarine|ghee)\b/, 'Dairy & Eggs'],
  [/\b(egg|egg white|egg yolk)\b/, 'Dairy & Eggs'],

  [/\b(juice|coffee|tea|water|soda|wine|beer|sparkling|kombucha|lemonade|seltzer)\b/, 'Beverages'],
  [/\boil\b/, 'Oils & Vinegars'],
  [/\b(vanilla|chocolate)\b/, 'Baking'],

  [
    /\b(almond|walnut|pecan|cashew|pistachio|peanut|hazelnut|macadamia|pine nut|brazil nut|nut|sunflower seed|pumpkin seed|pepita|chia|flax|flaxseed|hemp seed|trail mix)\b/,
    'Nuts & Dried Fruit',
  ],

  [/\b(salt|pepper|cumin|paprika|turmeric|cinnamon|nutmeg|oregano|sage|coriander|cardamom|clove|allspice|chili powder)\b/, 'Spices & Seasonings'],
  [/\b(tofu|tempeh|seitan)\b/, 'Other'],
];

const PACKAGED_FROM = new Set(['Produce', 'Meat & Seafood', 'Grains & Pasta', 'Other']);

/**
 * Categorize an ingredient into a grocery department. The name decides; when it
 * matches nothing, the catalog's nutritional food group (if known) picks the aisle.
 */
export function categorizeDepartment(name: string, foodGroupName?: string | null): string {
  const { text, packaged } = searchText(name);
  const named = RULES.find(([pattern]) => pattern.test(text))?.[1];
  const department = named ?? FOOD_GROUP_DEPARTMENTS[foodGroupName?.toLowerCase() ?? ''] ?? 'Other';
  return packaged ? inPackage(department, 'can') : department;
}

/**
 * The aisle once the retail package is known: a can or jar of something that
 * would otherwise be fresh, meat, dry goods or unsorted sits with canned goods.
 */
export function inPackage(department: string, packageName: string): string {
  return PACKAGED_FROM.has(department) && /\b(cans?|jars?|tins?)\b/i.test(packageName)
    ? 'Canned & Jarred'
    : department;
}
