// Retail Packaging Batch Lookup
// Queries Open Food Facts API for real product packaging sizes
// for ingredients that are missing retail packaging entries.
//
// Usage: node lookup_packaging.js [--limit N] [--dry-run]
//
// Outputs:
//   output/packaging_lookup.json  — structured results with provenance
//   output/packaging_lookup.sql   — ready-to-execute INSERT statements

const fs = require('fs');
const path = require('path');
const https = require('https');

// ─── Configuration ──────────────────────────────────────────────────────────

const OUTPUT_DIR = path.resolve(__dirname, 'output');
const COMBINED_FOOD_CSV = path.join(OUTPUT_DIR, 'combined_food.csv');
const COMBINED_PKG_CSV = path.join(OUTPUT_DIR, 'combined_packaging.csv');
const CUSTOM_MIGRATION = path.resolve(__dirname, '../../nom-api/Nom.Data/_CustomMigration.cs');

const RATE_LIMIT_MS = 6500; // 10 req/min → 6.5s between requests
const MIN_DATA_POINTS = 3;  // require ≥3 products to accept a result
const PAGE_SIZE = 50;       // products per OFF API query
const MAX_LIMIT = parseInt(process.argv.find(a => a.startsWith('--limit='))?.split('=')[1] || '0') || Infinity;
const DRY_RUN = process.argv.includes('--dry-run');

// Unit conversion to base units (g for mass, ml for volume)
const UNIT_TO_BASE = {
  'g': { factor: 1, category: 'mass' },
  'kg': { factor: 1000, category: 'mass' },
  'oz': { factor: 28.3495, category: 'mass' },
  'lb': { factor: 453.592, category: 'mass' },
  'ml': { factor: 1, category: 'volume' },
  'l': { factor: 1000, category: 'volume' },
  'cl': { factor: 10, category: 'volume' },
  'fl oz': { factor: 29.5735, category: 'volume' },
};

// Package name overrides based on ingredient name patterns
const PKG_NAME_OVERRIDES = [
  [/yogurt|kefir/, 'tub'],
  [/cheese|parmesan|cheddar|mozzarella|gruyere|gouda|brie/, 'block'],
  [/butter|margarine/, 'stick'],
  [/cream cheese/, 'block'],
  [/sour cream|ricotta|cottage cheese/, 'container'],
  [/hummus|dip|guacamole|salsa/, 'container'],
  [/jam|jelly|preserve|marmalade/, 'jar'],
  [/pickle|olive|caper|artichoke/, 'jar'],
  [/mustard|mayo|mayonnaise/, 'jar'],
  [/syrup|honey|molasses|agave/, 'bottle'],
  [/oil|vinegar|sauce|dressing/, 'bottle'],
  [/juice|milk|cream|broth|stock/, 'carton'],
  [/nut|seed|almond|walnut|cashew|pecan|pistachio/, 'bag'],
  [/chip|cracker|pretzel|popcorn/, 'bag'],
  [/rice|grain|quinoa|barley|oat|lentil|couscous/, 'bag'],
  [/flour|sugar|starch/, 'bag'],
  [/pasta|spaghetti|penne|macaroni|linguine|noodle/, 'box'],
  [/cereal|granola/, 'box'],
  [/soup|broth|stock|chili|stew/, 'can'],
  [/bean|chickpea|corn/, 'can'],
  [/tomato|tuna|sardine|anchovy/, 'can'],
  [/spice|pepper|cumin|paprika|cinnamon|turmeric|oregano|thyme/, 'jar'],
  [/extract|vanilla/, 'bottle'],
  [/bread|baguette|sourdough/, 'loaf'],
  [/tortilla|wrap|pita|naan/, 'pack'],
  [/egg/, 'dozen'],
  [/tofu|tempeh/, 'block'],
];

// ─── Helpers ────────────────────────────────────────────────────────────────

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

/** Simplify USDA-style ingredient names for better OFF search results */
function simplifySearchTerm(name) {
  let term = name
    // Remove parenthetical qualifiers
    .replace(/\([^)]*\)/g, '')
    // Remove common USDA suffixes/qualifiers
    .replace(/\b(raw|cooked|boiled|baked|roasted|grilled|fried|steamed|microwaved|toasted|frozen|canned|dried|dehydrated|fresh|prepared|unprepared|drained|solids and liquids|solids|without skin|with skin|without bone|with bone|boneless|skinless|trimmed|untrimmed|lean only|lean and fat|choice|select|prime|separable|all grades|heated|unheated|dry|rehydrated|enriched|unenriched|fortified|unfortified|reduced calorie|reduced fat|reduced sodium|low fat|low sodium|fat free|nonfat|whole|light|regular|extra|thick|thin|sliced|chopped|diced|ground|minced|shredded|grated|crushed|mashed|pureed|strained|plain|flavored|unsweetened|sweetened|salted|unsalted|smoked|cured|pickled)\b/gi, '')
    // Remove nutritional percentages
    .replace(/\d+%\s*(lean|fat|milk|juice|fruit|vegetable|whole|reduced)/gi, '')
    // Remove "NFS" (not further specified)
    .replace(/\bNFS\b/gi, '')
    // Collapse multiple spaces/commas
    .replace(/[,\s]+/g, ' ')
    .trim();
  // If we stripped it down too much, use original
  if (term.length < 3) term = name.replace(/[,]/g, ' ').trim();
  return term;
}

/** Score ingredient names: lower = more likely to be a common retail product */
function retailRelevanceScore(name) {
  const lower = name.toLowerCase();
  let score = name.length; // shorter names tend to be more common
  // Penalize USDA-specific entries
  if (/\b(nfs|grade|separable|trimmed|type)\b/i.test(lower)) score += 50;
  if (/\b(species|subspecies|variety)\b/i.test(lower)) score += 100;
  // Penalize very specific preparations
  if ((lower.match(/\b(raw|cooked|boiled|baked|roasted|fried|steamed|frozen|canned|dried)\b/gi) || []).length > 1) score += 30;
  // Favor items that sound like retail products
  if (/\b(sauce|dressing|spread|yogurt|cheese|bread|cereal|granola|soup|juice|jam|syrup|oil|vinegar)\b/i.test(lower)) score -= 20;
  return score;
}

function fetchJSON(url) {
  return new Promise((resolve, reject) => {
    const options = {
      headers: { 'User-Agent': 'NOM-PackagingLookup/1.0 (https://github.com/nom)' },
      timeout: 30000,
    };

    https.get(url, options, (res) => {
      // Follow redirects
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        fetchJSON(res.headers.location).then(resolve, reject);
        return;
      }
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try { resolve(JSON.parse(data)); }
        catch (e) { reject(new Error(`Non-JSON response (HTTP ${res.statusCode})`)); }
      });
    }).on('error', reject)
      .on('timeout', function() { this.destroy(); reject(new Error('Timeout')); });
  });
}

async function search(term, retries) {
  if (retries === undefined) retries = 2;
  const url = 'https://world.openfoodfacts.org/cgi/search.pl?' +
    'search_terms=' + encodeURIComponent(term) +
    '&search_simple=1&action=process&json=1' +
    '&page_size=' + PAGE_SIZE +
    '&countries=United+States' +
    '&fields=product_name,quantity,product_quantity,product_quantity_unit,packaging';

  for (let attempt = 0; attempt <= retries; attempt++) {
    try {
      return await fetchJSON(url);
    } catch (e) {
      if (attempt < retries) {
        await sleep(3000); // wait before retry
        continue;
      }
      throw new Error('Failed after ' + (retries + 1) + ' attempts: ' + e.message);
    }
  }
}

/** Parse CSV line (handles quoted fields with commas) */
function parseCSVLine(line) {
  const fields = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && line[i + 1] === '"') { current += '"'; i++; }
      else inQuotes = !inQuotes;
    } else if (ch === ',' && !inQuotes) {
      fields.push(current); current = '';
    } else {
      current += ch;
    }
  }
  fields.push(current);
  return fields;
}

function inferPackageName(ingredientName, category, sizeG) {
  const lower = ingredientName.toLowerCase();
  for (const [pattern, name] of PKG_NAME_OVERRIDES) {
    if (pattern.test(lower)) return name;
  }
  // Generic heuristic by size
  if (category === 'mass') {
    if (sizeG < 100) return 'jar';
    if (sizeG < 250) return 'can';
    if (sizeG < 500) return 'bag';
    return 'bag';
  }
  if (category === 'volume') {
    if (sizeG < 100) return 'bottle';
    if (sizeG < 350) return 'can';
    if (sizeG < 1000) return 'bottle';
    return 'carton';
  }
  return 'package';
}

/** Find the mode (most common value) from an array of display-unit sizes, bucketed to common retail sizes */
function findModeSize(displaySizes) {
  // Common US retail sizes in oz / fl oz
  const nice = [0.5, 1, 1.5, 2, 3, 3.5, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 13.5, 14, 14.5, 15, 16, 17, 18, 20, 24, 26, 28, 32, 40, 48, 64];
  const buckets = new Map();
  for (const s of displaySizes) {
    let best = nice[0], bestDist = Math.abs(s - nice[0]);
    for (const n of nice) {
      const d = Math.abs(s - n);
      if (d < bestDist) { best = n; bestDist = d; }
    }
    buckets.set(best, (buckets.get(best) || 0) + 1);
  }
  let modeVal = 0, modeCount = 0;
  for (const [val, count] of buckets) {
    if (count > modeCount) { modeVal = val; modeCount = count; }
  }
  return { size: modeVal, count: modeCount };
}

function escapeSQL(s) { return s.replace(/'/g, "''"); }

// ─── Phase 1: Load existing data ───────────────────────────────────────────

function loadIngredients() {
  if (!fs.existsSync(COMBINED_FOOD_CSV)) {
    console.error('ERROR: combined_food.csv not found. Run prepare_combined_import.js first.');
    process.exit(1);
  }
  const lines = fs.readFileSync(COMBINED_FOOD_CSV, 'utf8').split('\n');
  const ingredients = [];
  for (let i = 1; i < lines.length; i++) { // skip header
    if (!lines[i].trim()) continue;
    const fields = parseCSVLine(lines[i]);
    // combined_food.csv: fdc_id,description,data_type,source_priority
    if (fields[1]) ingredients.push(fields[1].trim());
  }
  return ingredients;
}

function loadExistingPatterns() {
  const patterns = new Set();

  // Load from combined_packaging.csv
  if (fs.existsSync(COMBINED_PKG_CSV)) {
    const lines = fs.readFileSync(COMBINED_PKG_CSV, 'utf8').split('\n');
    for (let i = 1; i < lines.length; i++) {
      if (!lines[i].trim()) continue;
      const fields = parseCSVLine(lines[i]);
      if (fields[0]) patterns.add(fields[0].trim().toLowerCase());
    }
  }

  // Parse seed data from _CustomMigration.cs
  if (fs.existsSync(CUSTOM_MIGRATION)) {
    const content = fs.readFileSync(CUSTOM_MIGRATION, 'utf8');
    // Match patterns like: (10000, 'coconut milk', 'can', ...)
    const regex = /\(\d+,\s*'([^']+)',\s*'[^']+',/g;
    let match;
    while ((match = regex.exec(content)) !== null) {
      patterns.add(match[1].toLowerCase());
    }
  }

  return patterns;
}

function findUnmatched(ingredients, existingPatterns) {
  const patternsArray = [...existingPatterns];
  const unmatched = [];
  const seenSearchTerms = new Set();

  for (const name of ingredients) {
    const lower = name.toLowerCase();
    const hasMatch = patternsArray.some(p => lower.includes(p));
    if (!hasMatch) {
      const searchTerm = simplifySearchTerm(name);
      const termKey = searchTerm.toLowerCase();
      // Deduplicate: only keep one entry per unique search term
      if (!seenSearchTerms.has(termKey)) {
        seenSearchTerms.add(termKey);
        unmatched.push({ name, searchTerm, score: retailRelevanceScore(name) });
      }
    }
  }

  // Sort by relevance score (lower = more likely to be useful)
  unmatched.sort((a, b) => a.score - b.score);
  return unmatched;
}

// ─── Phase 2: Query OFF API ────────────────────────────────────────────────

async function lookupPackaging(ingredientName) {
  const data = await search(ingredientName);
  const products = data.products || [];
  const totalHits = data.count || 0;

  // Collect all product_quantity values grouped by unit category
  const massSizes = [];   // in grams
  const volumeSizes = []; // in ml

  for (const p of products) {
    let qty = parseFloat(p.product_quantity);
    let unit = (p.product_quantity_unit || '').toLowerCase().trim();

    // If structured fields are missing, try parsing the quantity text field
    // e.g., "16 fl oz", "500 ml", "12 oz (340g)"
    if ((!qty || qty <= 0 || !unit) && p.quantity) {
      const m = p.quantity.match(/^(\d+(?:\.\d+)?)\s*(fl\.?\s*oz|oz|g|kg|ml|l|cl|lb)/i);
      if (m) {
        qty = parseFloat(m[1]);
        unit = m[2].toLowerCase().replace(/\.\s*/g, ' ').trim();
      }
    }

    if (!qty || qty <= 0 || !unit) continue;

    // Normalize unit aliases
    if (unit === 'gram' || unit === 'grams') unit = 'g';
    if (unit === 'kilogram' || unit === 'kilograms') unit = 'kg';
    if (unit === 'ounce' || unit === 'ounces') unit = 'oz';
    if (unit === 'pound' || unit === 'pounds') unit = 'lb';
    if (unit === 'milliliter' || unit === 'milliliters' || unit === 'millilitre') unit = 'ml';
    if (unit === 'liter' || unit === 'liters' || unit === 'litre') unit = 'l';
    if (unit === 'centiliter' || unit === 'centilitre') unit = 'cl';
    if (unit === 'fl. oz' || unit === 'fl. oz.' || unit === 'fluid ounce' || unit === 'fl oz') unit = 'fl oz';

    const conv = UNIT_TO_BASE[unit];
    if (!conv) continue;

    const baseVal = qty * conv.factor;
    // Filter out implausibly small values (sachets, samples, serving sizes)
    // Minimum retail package: ~28g (1 oz) for mass, ~30ml (1 fl oz) for volume
    if (conv.category === 'mass' && baseVal >= 28) massSizes.push(baseVal);
    else if (conv.category === 'volume' && baseVal >= 30) volumeSizes.push(baseVal);
  }

  // Pick the category with more data points
  let category, sizes;
  if (massSizes.length >= volumeSizes.length && massSizes.length >= MIN_DATA_POINTS) {
    category = 'mass'; sizes = massSizes;
  } else if (volumeSizes.length >= MIN_DATA_POINTS) {
    category = 'volume'; sizes = volumeSizes;
  } else if (massSizes.length >= MIN_DATA_POINTS) {
    category = 'mass'; sizes = massSizes;
  } else {
    return null; // not enough data
  }

  // Convert base units (g/ml) to display units (oz/fl oz) before bucketing
  const displaySizes = category === 'mass'
    ? sizes.map(g => g / 28.3495)   // g → oz
    : sizes.map(ml => ml / 29.5735); // ml → fl oz

  const mode = findModeSize(displaySizes);
  // Require at least 2 products to agree on the mode size
  if (mode.count < 2) return null;
  const displayUnit = category === 'mass' ? 'oz' : 'fl oz';
  const baseUnits = category === 'mass' ? mode.size * 28.3495 : mode.size * 29.5735;
  const pkgName = inferPackageName(ingredientName, category, baseUnits);

  return {
    ingredientPattern: ingredientName.toLowerCase(),
    packageName: pkgName,
    packageSize: mode.size,
    packageSizeUnit: displayUnit,
    sizeCategory: category,
    sizeInBaseUnits: parseFloat(baseUnits.toFixed(2)),
    source: 'off-search',
    confidence: mode.count,
    searchResults: totalHits,
    dataPoints: sizes.length,
  };
}

// ─── Phase 3: Main ─────────────────────────────────────────────────────────

async function main() {
  console.log('═══════════════════════════════════════════════════════════════');
  console.log('  Retail Packaging Batch Lookup (Open Food Facts)');
  console.log('═══════════════════════════════════════════════════════════════\n');

  // Phase 1: Identify unmatched ingredients
  console.log('[Phase 1] Loading existing data...');
  const ingredients = loadIngredients();
  console.log(`  Loaded ${ingredients.length} ingredients from combined_food.csv`);

  const existingPatterns = loadExistingPatterns();
  console.log(`  Loaded ${existingPatterns.size} existing packaging patterns`);

  const unmatched = findUnmatched(ingredients, existingPatterns);
  console.log(`  Found ${unmatched.length} ingredients without packaging matches`);

  const toProcess = unmatched.slice(0, MAX_LIMIT);
  if (toProcess.length < unmatched.length) {
    console.log(`  Limiting to ${MAX_LIMIT} (use --limit=N to change)`);
  }

  if (DRY_RUN) {
    console.log('\n[Dry Run] Would query OFF API for these ingredients (sorted by relevance):');
    toProcess.slice(0, 30).forEach(item =>
      console.log(`  - "${item.name}" → search: "${item.searchTerm}" (score: ${item.score})`)
    );
    if (toProcess.length > 30) console.log(`  ... and ${toProcess.length - 30} more`);
    const eta = Math.ceil(toProcess.length * RATE_LIMIT_MS / 60000);
    console.log(`\nEstimated time: ~${eta} minutes`);
    return;
  }

  // Phase 2: Query OFF API
  const eta = Math.ceil(toProcess.length * RATE_LIMIT_MS / 60000);
  console.log(`\n[Phase 2] Querying OFF API for ${toProcess.length} ingredients (~${eta} min)...\n`);

  const results = [];
  let found = 0, notFound = 0, errors = 0;

  for (let i = 0; i < toProcess.length; i++) {
    const item = toProcess[i];
    if (i > 0) await sleep(RATE_LIMIT_MS);

    try {
      const result = await lookupPackaging(item.searchTerm);
      const progress = `[${i + 1}/${toProcess.length}]`;

      if (result) {
        // Use simplified name as the pattern for matching
        result.ingredientPattern = item.searchTerm.toLowerCase();
        results.push(result);
        found++;
        console.log(`  ${progress} ${item.searchTerm} → ${result.packageSize} ${result.packageSizeUnit} ${result.packageName} (${result.confidence} matches)`);
      } else {
        notFound++;
        console.log(`  ${progress} ${item.searchTerm} → insufficient data`);
      }
    } catch (e) {
      errors++;
      console.log(`  [${i + 1}/${toProcess.length}] ${item.searchTerm} → ERROR: ${e.message}`);
    }
  }

  // Phase 3: Write output
  console.log(`\n[Phase 3] Writing results...`);
  console.log(`  Found: ${found}, Not found: ${notFound}, Errors: ${errors}`);

  if (results.length === 0) {
    console.log('  No results to write.');
    return;
  }

  // JSON output
  const jsonPath = path.join(OUTPUT_DIR, 'packaging_lookup.json');
  fs.writeFileSync(jsonPath, JSON.stringify(results, null, 2));
  console.log(`  Wrote ${jsonPath} (${results.length} entries)`);

  // SQL output
  const sqlPath = path.join(OUTPUT_DIR, 'packaging_lookup.sql');
  const sqlLines = [
    '-- Retail Packaging from Open Food Facts search',
    '-- Generated by lookup_packaging.js',
    '-- Safe to re-run: skips rows where IngredientPattern already exists.',
    '',
    ...results.map(r =>
      `INSERT INTO reference."RetailPackaging" ` +
      `("IngredientPattern", "PackageName", "PackageSize", "PackageSizeUnit", "SizeCategory", "SizeInBaseUnits", "IsDefault", "Source", "CreatedDate")` +
      ` SELECT '${escapeSQL(r.ingredientPattern)}', '${escapeSQL(r.packageName)}', ${r.packageSize}, '${escapeSQL(r.packageSizeUnit)}', '${r.sizeCategory}', ${r.sizeInBaseUnits}, true, 'off-search', NOW()` +
      ` WHERE NOT EXISTS (SELECT 1 FROM reference."RetailPackaging" WHERE LOWER("IngredientPattern") = '${escapeSQL(r.ingredientPattern)}');`
    ),
  ];
  fs.writeFileSync(sqlPath, sqlLines.join('\n') + '\n');
  console.log(`  Wrote ${sqlPath}`);

  console.log('\n═══════════════════════════════════════════════════════════════');
  console.log(`  Packaging Lookup Complete — ${results.length} new entries`);
  console.log('═══════════════════════════════════════════════════════════════');
}

main().catch(e => { console.error('FATAL:', e); process.exit(1); });
