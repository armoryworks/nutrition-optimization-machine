// Combined USDA + Open Food Facts ETL
// Produces clean, deduplicated CSVs for NOM ingredient import
// Usage: node prepare_combined_import.js

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const readline = require('readline');

// ─── Configuration ──────────────────────────────────────────────────────────

const USDA_BASE = process.env.USDA_BASE || path.resolve(__dirname, '../usda-source');
const OFF_INPUT = process.env.OFF_INPUT || path.resolve(__dirname, '../off/openfoodfacts-products.jsonl.gz');
const OUTPUT_DIR = process.env.ETL_OUTPUT_DIR || path.resolve(__dirname, 'output');

// USDA FDC nutrient_id → NOM nutrient mapping
const FDC_NUTRIENT_MAP = {
  '1008': { nomId: 5035, name: 'Calories',          measId: 16 },
  '1003': { nomId: 5006, name: 'Protein',            measId: 1 },
  '1004': { nomId: 5000, name: 'Fat',                measId: 1 },
  '1005': { nomId: 5003, name: 'Total Carbohydrates', measId: 1 },
  '1258': { nomId: 5001, name: 'Saturated Fat',      measId: 1 },
  '1253': { nomId: 5002, name: 'Cholesterol',        measId: 8 },
  '1093': { nomId: 5004, name: 'Sodium',             measId: 8 },
  '1079': { nomId: 5005, name: 'Dietary Fiber',      measId: 1 },
  '2000': { nomId: 5007, name: 'Added Sugars',       measId: 1 },
  '1106': { nomId: 5008, name: 'Vitamin A',          measId: 9 },
  '1162': { nomId: 5009, name: 'Vitamin C',          measId: 8 },
  '1114': { nomId: 5010, name: 'Vitamin D',          measId: 9 },
  '1109': { nomId: 5011, name: 'Vitamin E',          measId: 8 },
  '1185': { nomId: 5012, name: 'Vitamin K',          measId: 9 },
  '1165': { nomId: 5013, name: 'Thiamin',            measId: 8 },
  '1166': { nomId: 5014, name: 'Riboflavin',         measId: 8 },
  '1167': { nomId: 5015, name: 'Niacin',             measId: 8 },
  '1175': { nomId: 5016, name: 'Vitamin B6',         measId: 8 },
  '1190': { nomId: 5017, name: 'Folate',             measId: 9 },
  '1178': { nomId: 5018, name: 'Vitamin B12',        measId: 9 },
  '1170': { nomId: 5020, name: 'Pantothenic Acid',   measId: 8 },
  '1180': { nomId: 5021, name: 'Choline',            measId: 8 },
  '1087': { nomId: 5022, name: 'Calcium',            measId: 8 },
  '1089': { nomId: 5023, name: 'Iron',               measId: 8 },
  '1091': { nomId: 5024, name: 'Phosphorus',         measId: 8 },
  '1090': { nomId: 5026, name: 'Magnesium',          measId: 8 },
  '1095': { nomId: 5027, name: 'Zinc',               measId: 8 },
  '1103': { nomId: 5028, name: 'Selenium',           measId: 9 },
  '1098': { nomId: 5029, name: 'Copper',             measId: 8 },
  '1101': { nomId: 5030, name: 'Manganese',          measId: 8 },
  '1092': { nomId: 5034, name: 'Potassium',          measId: 8 },
};

// OFF nutrient key → NOM nutrient mapping (with unit conversion factors)
const OFF_NUTRIENT_MAP = {
  'energy-kcal_100g':   { nomId: 5035, measId: 16, factor: 1 },
  'proteins_100g':      { nomId: 5006, measId: 1,  factor: 1 },
  'fat_100g':           { nomId: 5000, measId: 1,  factor: 1 },
  'carbohydrates_100g': { nomId: 5003, measId: 1,  factor: 1 },
  'saturated-fat_100g': { nomId: 5001, measId: 1,  factor: 1 },
  'cholesterol_100g':   { nomId: 5002, measId: 8,  factor: 1000 }, // g → mg
  'sodium_100g':        { nomId: 5004, measId: 8,  factor: 1000 }, // g → mg
  'fiber_100g':         { nomId: 5005, measId: 1,  factor: 1 },
  'sugars_100g':        { nomId: 5007, measId: 1,  factor: 1 },
  'vitamin-a_100g':     { nomId: 5008, measId: 9,  factor: 1 },
  'vitamin-c_100g':     { nomId: 5009, measId: 8,  factor: 1000 }, // g → mg
  'vitamin-d_100g':     { nomId: 5010, measId: 9,  factor: 1 },
  'calcium_100g':       { nomId: 5022, measId: 8,  factor: 1000 }, // g → mg
  'iron_100g':          { nomId: 5023, measId: 8,  factor: 1000 }, // g → mg
  'phosphorus_100g':    { nomId: 5024, measId: 8,  factor: 1000 }, // g → mg
  'magnesium_100g':     { nomId: 5026, measId: 8,  factor: 1000 }, // g → mg
  'zinc_100g':          { nomId: 5027, measId: 8,  factor: 1000 }, // g → mg
  'potassium_100g':     { nomId: 5034, measId: 8,  factor: 1000 }, // g → mg
};

// SR Legacy categories to exclude (by food_category_id from food_category.csv)
const SR_EXCLUDE_CATEGORY_IDS = new Set([
  '3',   // Baby Foods
  '24',  // American Indian/Alaska Native Foods
  '25',  // Restaurant Foods
  '27',  // Quality Control Materials
]);

// ─── CSV Parsing ────────────────────────────────────────────────────────────

function parseCSVLine(line) {
  const fields = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && i + 1 < line.length && line[i + 1] === '"') {
        current += '"'; i++;
      } else { inQuotes = !inQuotes; }
    } else if (ch === ',' && !inQuotes) {
      fields.push(current); current = '';
    } else { current += ch; }
  }
  fields.push(current);
  return fields;
}

async function readCSV(filename) {
  const filePath = path.join(USDA_BASE, filename);
  const rl = readline.createInterface({ input: fs.createReadStream(filePath) });
  let headers = null;
  const rows = [];
  for await (const line of rl) {
    const fields = parseCSVLine(line);
    if (!headers) { headers = fields; continue; }
    const row = {};
    for (let i = 0; i < headers.length; i++) row[headers[i]] = fields[i] || '';
    rows.push(row);
  }
  return rows;
}

async function* streamCSV(filename) {
  const filePath = path.join(USDA_BASE, filename);
  const rl = readline.createInterface({ input: fs.createReadStream(filePath) });
  let headers = null;
  for await (const line of rl) {
    const fields = parseCSVLine(line);
    if (!headers) { headers = fields; continue; }
    const row = {};
    for (let i = 0; i < headers.length; i++) row[headers[i]] = fields[i] || '';
    yield row;
  }
}

// ─── Name Normalization ─────────────────────────────────────────────────────

const STRIP_QUALIFIERS = [
  'separable lean only', 'separable lean and fat', 'separable fat',
  'trimmed to 1/4" fat', 'trimmed to 1/8" fat', 'trimmed to 0" fat',
  'trimmed to', 'all grades', 'select', 'choice', 'prime',
  'not specified', 'not further specified', 'NFS',
  'with added vitamin a and vitamin d', 'with added vitamin d',
  'with added niacin and iron', 'with added nutrients',
  'commercially prepared', 'commercially canned',
  'ready-to-heat', 'ready-to-serve', 'ready-to-eat',
  'refrigerated dough', 'from concentrate',
  'without salt', 'with salt added', 'no salt added',
  'regular pack', 'drained solids',
];

function normalizeName(rawName, dataType) {
  let name = rawName.trim();
  if (!name) return '';

  if (dataType === 'survey_fndds_food') {
    // FNDDS: mostly clean, reverse comma order for 2-part names
    // "Milk, whole" → "Whole Milk"
    // "Milk, reduced fat (2%)" → "Reduced Fat (2%) Milk"
    name = name.replace(/, NFS$/i, '');
    const parts = name.split(',').map(p => p.trim());
    if (parts.length === 2 && parts[1].length < 40) {
      name = parts[1] + ' ' + parts[0];
    } else if (parts.length > 2) {
      // Keep as-is for complex names, just clean up
      name = parts.slice(0, 3).join(', ');
    }
  } else if (dataType === 'foundation_food') {
    // Foundation: light cleanup, keep useful qualifiers
    // "Broccoli, raw" → "Broccoli, Raw"
    // "Cheese, cheddar" → "Cheddar Cheese"
    name = name.replace(/\([^)]*\)/g, '').trim();
    const parts = name.split(',').map(p => p.trim()).filter(Boolean);
    if (parts.length === 2 && parts[1].length < 30) {
      name = parts[1] + ' ' + parts[0];
    } else {
      name = parts.slice(0, 3).join(', ');
    }
  } else if (dataType === 'sr_legacy_food') {
    // SR Legacy: aggressive cleanup
    name = name.replace(/\([^)]*\)/g, '').trim();
    let lower = name.toLowerCase();
    for (const q of STRIP_QUALIFIERS) {
      lower = lower.replace(new RegExp(',?\\s*' + q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi'), '');
    }
    name = lower;
    const parts = name.split(',').map(p => p.trim()).filter(Boolean);
    // For meat cuts: "beef, flank, steak" → "Beef Flank Steak"
    if (parts.length >= 2 && parts.length <= 4) {
      name = parts.join(' ');
    } else if (parts.length > 4) {
      name = parts.slice(0, 3).join(' ');
    } else {
      name = parts[0] || '';
    }
  }

  // Title Case
  name = name.replace(/\b[a-z]/g, c => c.toUpperCase());
  // Clean up whitespace and punctuation
  name = name.replace(/\s+/g, ' ').replace(/,\s*$/, '').trim();
  // Cap length
  if (name.length > 80) name = name.substring(0, 80).replace(/\s+\S*$/, '');

  return name;
}

function normalizeOffCategory(tag) {
  // "en:whole-milks" → "Whole Milk"
  let name = tag.replace(/^en:/, '').replace(/-/g, ' ');
  // De-pluralize common endings
  name = name.replace(/ies$/, 'y').replace(/ves$/, 'f').replace(/ses$/, 's').replace(/s$/, '');
  // Title Case
  name = name.replace(/\b[a-z]/g, c => c.toUpperCase());
  return name.trim();
}

function normalizeKey(name) {
  return name.toLowerCase().replace(/[^a-z0-9]/g, ' ').replace(/\s+/g, ' ').trim();
}

// ─── CSV Output Helpers ─────────────────────────────────────────────────────

function escapeCSV(val) {
  const s = String(val);
  if (s.includes(',') || s.includes('"') || s.includes('\n')) {
    return '"' + s.replace(/"/g, '""') + '"';
  }
  return s;
}

// ─── Main ETL Pipeline ─────────────────────────────────────────────────────

async function run() {
  const startTime = Date.now();
  const stats = {
    usda: { foundation: 0, fndds: 0, sr_legacy: 0, sr_excluded: 0, sr_thin: 0 },
    off: { products_scanned: 0, us_with_macros: 0, categories_raw: 0, categories_kept: 0, categories_deduped: 0 },
    combined: { total_ingredients: 0, total_nutrients: 0, total_aliases: 0, total_packaging: 0 },
  };

  console.log('═══════════════════════════════════════════════════════════════');
  console.log('  COMBINED USDA + OFF ETL');
  console.log('═══════════════════════════════════════════════════════════════');

  // ── Phase A: Load USDA lookup tables ──────────────────────────────────

  console.log('\n[Phase A] Loading USDA lookup tables...');

  const categoryRows = await readCSV('food_category.csv');
  const categoryMap = new Map();
  for (const r of categoryRows) categoryMap.set(r.id, r.description);

  const foundationRows = await readCSV('foundation_food.csv');
  const foundationIds = new Set(foundationRows.map(r => r.fdc_id));
  console.log('  Foundation IDs: ' + foundationIds.size);

  const fnddsRows = await readCSV('survey_fndds_food.csv');
  const fnddsIds = new Set(fnddsRows.map(r => r.fdc_id));
  console.log('  FNDDS IDs: ' + fnddsIds.size);

  const srRows = await readCSV('sr_legacy_food.csv');
  const srIds = new Set(srRows.map(r => r.fdc_id));
  console.log('  SR Legacy IDs: ' + srIds.size);

  // ── Phase B: Stream food.csv, filter + normalize ──────────────────────

  console.log('\n[Phase B] Streaming food.csv (filtering + normalizing)...');

  // Map: normalizedKey → { fdcId, rawName, normalizedName, dataType, priority, categoryId }
  const ingredients = new Map();
  // Map: fdcId → normalizedKey (for nutrient lookup)
  const fdcToKey = new Map();
  let foodCount = 0;

  for await (const row of streamCSV('food.csv')) {
    foodCount++;
    if (foodCount % 500000 === 0) console.log('  ...processed ' + foodCount.toLocaleString() + ' food rows');

    const fdcId = row.fdc_id;
    const dt = row.data_type;
    const desc = row.description || '';
    const catId = row.food_category_id || '';

    let priority;
    if (dt === 'foundation_food' && foundationIds.has(fdcId)) {
      priority = 1;
    } else if (dt === 'survey_fndds_food' && fnddsIds.has(fdcId)) {
      priority = 2;
    } else if (dt === 'sr_legacy_food' && srIds.has(fdcId)) {
      // Apply SR Legacy exclusion rules
      if (SR_EXCLUDE_CATEGORY_IDS.has(catId)) { stats.usda.sr_excluded++; continue; }
      if (desc === desc.toUpperCase() && desc.length > 5) { stats.usda.sr_excluded++; continue; }
      if (desc.length > 80) { stats.usda.sr_excluded++; continue; }
      if (/alaska native|fast food|school lunch/i.test(desc)) { stats.usda.sr_excluded++; continue; }
      priority = 3;
    } else {
      continue; // Skip branded, sub_sample, etc.
    }

    const normalizedName = normalizeName(desc, dt);
    if (!normalizedName || normalizedName.length < 2) continue;

    const key = normalizeKey(normalizedName);
    const existing = ingredients.get(key);

    if (!existing || priority < existing.priority) {
      ingredients.set(key, {
        fdcId, rawName: desc, normalizedName, dataType: dt, priority, categoryId: catId,
      });
      fdcToKey.set(fdcId, key);
    }

    // Also map this FDC ID even if it lost the dedup race (we still need nutrient data)
    if (!fdcToKey.has(fdcId)) fdcToKey.set(fdcId, key);
  }

  // Count by type
  for (const [, ing] of ingredients) {
    if (ing.dataType === 'foundation_food') stats.usda.foundation++;
    else if (ing.dataType === 'survey_fndds_food') stats.usda.fndds++;
    else if (ing.dataType === 'sr_legacy_food') stats.usda.sr_legacy++;
  }

  console.log('  Selected: Foundation=' + stats.usda.foundation +
    ' FNDDS=' + stats.usda.fndds +
    ' SR Legacy=' + stats.usda.sr_legacy +
    ' (excluded ' + stats.usda.sr_excluded + ' SR)');

  // ── Phase C: Stream food_nutrient.csv ─────────────────────────────────

  console.log('\n[Phase C] Streaming food_nutrient.csv (27M rows)...');

  // Map: fdcId → Map<nomNutrientId, { amount, measId }>
  const nutrientsByFdc = new Map();
  let fnCount = 0;

  for await (const row of streamCSV('food_nutrient.csv')) {
    fnCount++;
    if (fnCount % 5000000 === 0) console.log('  ...processed ' + fnCount.toLocaleString() + ' nutrient rows');

    const fdcId = row.fdc_id;
    if (!fdcToKey.has(fdcId)) continue;

    const mapping = FDC_NUTRIENT_MAP[row.nutrient_id];
    if (!mapping) continue;

    const amount = parseFloat(row.amount);
    if (isNaN(amount)) continue;

    if (!nutrientsByFdc.has(fdcId)) nutrientsByFdc.set(fdcId, new Map());
    nutrientsByFdc.get(fdcId).set(mapping.nomId, { amount, measId: mapping.measId });
  }

  console.log('  Foods with nutrients: ' + nutrientsByFdc.size);

  // Drop SR Legacy with < 8 nutrients
  const keysToRemove = [];
  for (const [key, ing] of ingredients) {
    if (ing.dataType === 'sr_legacy_food') {
      const nuts = nutrientsByFdc.get(ing.fdcId);
      if (!nuts || nuts.size < 8) {
        keysToRemove.push(key);
        stats.usda.sr_thin++;
      }
    }
  }
  for (const k of keysToRemove) {
    const ing = ingredients.get(k);
    fdcToKey.delete(ing.fdcId);
    ingredients.delete(k);
  }

  stats.usda.sr_legacy -= stats.usda.sr_thin;
  console.log('  Dropped ' + stats.usda.sr_thin + ' SR Legacy with <8 nutrients');
  console.log('  Final SR Legacy: ' + stats.usda.sr_legacy);

  // ── Phase D: Stream OFF JSONL.gz ──────────────────────────────────────

  console.log('\n[Phase D] Streaming OFF JSONL.gz (~4.3M lines)...');

  // Per-category accumulation: categoryTag → { nutrientArrays: Map<nomId, number[]>, count, packages: [] }
  const offCategories = new Map();
  let offLineNum = 0;

  const offStream = fs.createReadStream(OFF_INPUT);
  const gunzip = zlib.createGunzip();
  const offRL = readline.createInterface({ input: offStream.pipe(gunzip) });

  for await (const line of offRL) {
    offLineNum++;
    if (offLineNum % 500000 === 0) {
      const elapsed = ((Date.now() - startTime) / 1000).toFixed(0);
      console.log('  ...processed ' + offLineNum.toLocaleString() + ' OFF lines (' + elapsed + 's)');
    }

    let product;
    try { product = JSON.parse(line); } catch { continue; }
    stats.off.products_scanned++;

    // Filter: US products
    const countries = product.countries_tags || [];
    if (!countries.includes('en:united-states')) continue;

    // Filter: complete macros
    const nut = product.nutriments || {};
    if (nut['energy-kcal_100g'] === undefined || nut['proteins_100g'] === undefined ||
        nut['fat_100g'] === undefined || nut['carbohydrates_100g'] === undefined) continue;

    stats.off.us_with_macros++;

    // Accumulate per category
    const cats = product.categories_tags || [];
    for (const tag of cats) {
      if (!tag.startsWith('en:')) continue;

      if (!offCategories.has(tag)) {
        offCategories.set(tag, { nutrientArrays: new Map(), count: 0, packages: [] });
      }
      const cat = offCategories.get(tag);
      cat.count++;

      // Accumulate nutrients
      for (const [offKey, mapping] of Object.entries(OFF_NUTRIENT_MAP)) {
        const val = nut[offKey];
        if (val !== undefined && val !== '' && val !== null) {
          const numVal = parseFloat(val) * mapping.factor;
          if (!isNaN(numVal) && numVal >= 0) {
            if (!cat.nutrientArrays.has(mapping.nomId)) cat.nutrientArrays.set(mapping.nomId, []);
            cat.nutrientArrays.get(mapping.nomId).push(numVal);
          }
        }
      }

      // Accumulate package info (first 500 per category)
      if (cat.packages.length < 500) {
        const pq = product.product_quantity;
        const pqu = (product.product_quantity_unit || '').toLowerCase();
        if (pq && pq > 0 && (pqu === 'g' || pqu === 'ml' || pqu === 'oz' || pqu === 'fl oz')) {
          cat.packages.push({ size: parseFloat(pq), unit: pqu });
        }
      }
    }
  }

  stats.off.categories_raw = offCategories.size;
  console.log('  US products with macros: ' + stats.off.us_with_macros.toLocaleString());
  console.log('  Raw OFF categories: ' + stats.off.categories_raw.toLocaleString());

  // Aggregate OFF categories → ingredients
  function median(arr) {
    if (arr.length === 0) return 0;
    arr.sort((a, b) => a - b);
    const mid = Math.floor(arr.length / 2);
    return arr.length % 2 === 0 ? (arr[mid - 1] + arr[mid]) / 2 : arr[mid];
  }

  const offIngredients = []; // { tag, name, key, nutrients: Map<nomId, {amount, measId}>, pkgInfo }

  for (const [tag, cat] of offCategories) {
    if (cat.count < 5) continue; // Require ≥5 products for reliable median
    stats.off.categories_kept++;

    const name = normalizeOffCategory(tag);
    if (!name || name.length < 2) continue;
    const key = normalizeKey(name);

    // Skip if USDA already has this ingredient
    if (ingredients.has(key)) {
      stats.off.categories_deduped++;
      continue;
    }

    // Compute median nutrients (require at least 3 data points per nutrient)
    const nutrients = new Map();
    for (const [nomId, values] of cat.nutrientArrays) {
      if (values.length >= 3) {
        const med = median(values);
        // Sanity check: skip absurd values
        const measId = Object.values(OFF_NUTRIENT_MAP).find(m => m.nomId === nomId)?.measId || 8;
        if (nomId === 5035 && med > 1000) continue; // kcal > 1000 per 100g is suspicious
        if (measId === 1 && med > 100) continue; // > 100g per 100g is impossible
        nutrients.set(nomId, { amount: Math.round(med * 10000) / 10000, measId });
      }
    }

    // Require at least macros
    if (!nutrients.has(5035) || !nutrients.has(5006) || !nutrients.has(5000) || !nutrients.has(5003)) continue;

    // Compute package info (median size, most common unit)
    let pkgInfo = null;
    if (cat.packages.length >= 3) {
      const unitCounts = {};
      for (const p of cat.packages) {
        unitCounts[p.unit] = (unitCounts[p.unit] || 0) + 1;
      }
      const topUnit = Object.entries(unitCounts).sort((a, b) => b[1] - a[1])[0][0];
      const sizes = cat.packages.filter(p => p.unit === topUnit).map(p => p.size);
      const medSize = median(sizes);
      if (medSize > 0) {
        const sizeCategory = (topUnit === 'fl oz' || topUnit === 'ml') ? 'volume' : 'mass';
        const baseUnit = sizeCategory === 'volume'
          ? (topUnit === 'fl oz' ? medSize * 29.5735 : medSize) // fl oz → ml
          : (topUnit === 'oz' ? medSize * 28.3495 : medSize);   // oz → g
        pkgInfo = {
          packageName: medSize < 100 ? 'can' : medSize < 500 ? 'box' : 'container',
          packageSize: Math.round(medSize * 100) / 100,
          packageSizeUnit: topUnit,
          sizeCategory,
          sizeInBaseUnits: Math.round(baseUnit * 100) / 100,
        };
      }
    }

    offIngredients.push({ tag, name, key, nutrients, pkgInfo });
  }

  console.log('  OFF categories kept (≥5 products): ' + stats.off.categories_kept);
  console.log('  Deduped against USDA: ' + stats.off.categories_deduped);
  console.log('  New OFF ingredients: ' + offIngredients.length);

  // ── Phase E: Write output CSVs ────────────────────────────────────────

  console.log('\n[Phase E] Writing output CSVs...');

  // 1. combined_food.csv
  const foodOut = fs.createWriteStream(path.join(OUTPUT_DIR, 'combined_food.csv'));
  foodOut.write('fdc_id,description,data_type,source_priority\n');

  for (const [, ing] of ingredients) {
    foodOut.write(escapeCSV(ing.fdcId) + ',' + escapeCSV(ing.normalizedName) + ',' +
      escapeCSV(ing.dataType) + ',' + ing.priority + '\n');
    stats.combined.total_ingredients++;
  }
  for (const off of offIngredients) {
    const offFdcId = 'OFF_' + off.tag.replace(/^en:/, '');
    foodOut.write(escapeCSV(offFdcId) + ',' + escapeCSV(off.name) + ',off_category,4\n');
    stats.combined.total_ingredients++;
  }
  foodOut.end();

  // 2. combined_food_nutrient.csv
  const nutOut = fs.createWriteStream(path.join(OUTPUT_DIR, 'combined_food_nutrient.csv'));
  nutOut.write('fdc_id,nutrient_id,amount,measurement_id\n');

  for (const [, ing] of ingredients) {
    const nuts = nutrientsByFdc.get(ing.fdcId);
    if (!nuts) continue;
    for (const [nomId, data] of nuts) {
      nutOut.write(escapeCSV(ing.fdcId) + ',' + nomId + ',' + data.amount + ',' + data.measId + '\n');
      stats.combined.total_nutrients++;
    }
  }
  for (const off of offIngredients) {
    const offFdcId = 'OFF_' + off.tag.replace(/^en:/, '');
    for (const [nomId, data] of off.nutrients) {
      nutOut.write(escapeCSV(offFdcId) + ',' + nomId + ',' + data.amount + ',' + data.measId + '\n');
      stats.combined.total_nutrients++;
    }
  }
  nutOut.end();

  // 3. combined_alias.csv
  const aliasOut = fs.createWriteStream(path.join(OUTPUT_DIR, 'combined_alias.csv'));
  aliasOut.write('fdc_id,alias_name,source_context\n');

  for (const [, ing] of ingredients) {
    // Add original USDA name as alias if different from normalized
    if (ing.rawName !== ing.normalizedName) {
      aliasOut.write(escapeCSV(ing.fdcId) + ',' + escapeCSV(ing.rawName) + ',' +
        escapeCSV('USDA FDC ' + ing.dataType) + '\n');
      stats.combined.total_aliases++;
    }
  }
  for (const off of offIngredients) {
    const offFdcId = 'OFF_' + off.tag.replace(/^en:/, '');
    aliasOut.write(escapeCSV(offFdcId) + ',' + escapeCSV(off.tag) + ',Open Food Facts category\n');
    stats.combined.total_aliases++;
  }
  aliasOut.end();

  // 4. combined_packaging.csv
  const pkgOut = fs.createWriteStream(path.join(OUTPUT_DIR, 'combined_packaging.csv'));
  pkgOut.write('ingredient_pattern,package_name,package_size,package_size_unit,size_category,size_in_base_units,is_default,source\n');

  for (const off of offIngredients) {
    if (!off.pkgInfo) continue;
    const p = off.pkgInfo;
    const pattern = off.name.toLowerCase();
    pkgOut.write(escapeCSV(pattern) + ',' + escapeCSV(p.packageName) + ',' +
      p.packageSize + ',' + escapeCSV(p.packageSizeUnit) + ',' +
      escapeCSV(p.sizeCategory) + ',' + p.sizeInBaseUnits + ',true,off-etl\n');
    stats.combined.total_packaging++;
  }
  pkgOut.end();

  // Write report
  const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
  stats.elapsed_seconds = parseFloat(elapsed);
  fs.writeFileSync(path.join(OUTPUT_DIR, 'etl_report.json'), JSON.stringify(stats, null, 2));

  console.log('\n═══════════════════════════════════════════════════════════════');
  console.log('  ETL COMPLETE (' + elapsed + 's)');
  console.log('═══════════════════════════════════════════════════════════════');
  console.log('  USDA: Foundation=' + stats.usda.foundation + ' FNDDS=' + stats.usda.fndds +
    ' SR Legacy=' + stats.usda.sr_legacy);
  console.log('  OFF:  ' + offIngredients.length + ' new categories');
  console.log('  Combined: ' + stats.combined.total_ingredients + ' ingredients, ' +
    stats.combined.total_nutrients + ' nutrient values');
  console.log('  Aliases: ' + stats.combined.total_aliases);
  console.log('  Packaging: ' + stats.combined.total_packaging);
  console.log('  Output: ' + OUTPUT_DIR);
}

run().catch(err => {
  console.error('ETL failed:', err);
  process.exit(1);
});
