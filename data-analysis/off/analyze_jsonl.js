// Stream-process the JSONL.gz file, filtering US products and analyzing data quality
// Usage: node analyze_jsonl.js [max_lines]

const fs = require('fs');
const zlib = require('zlib');
const readline = require('readline');

const MAX_LINES = parseInt(process.argv[2]) || Infinity;
const INPUT = 'openfoodfacts-products.jsonl.gz';

// Stats accumulators
let totalProducts = 0;
let usProducts = 0;
let usWithName = 0;
let usWithQuantity = 0;
let usWithNutrients = 0;
let usWithMacros = 0;
let usWithMicros = 0;
let usWithIngredients = 0;

// Nutrient completeness counters (per field)
const nutrientCounts = {};
const TRACKED_NUTRIENTS = [
  'energy-kcal_100g', 'proteins_100g', 'carbohydrates_100g', 'sugars_100g',
  'fat_100g', 'saturated-fat_100g', 'fiber_100g', 'sodium_100g',
  'cholesterol_100g', 'calcium_100g', 'iron_100g', 'potassium_100g',
  'vitamin-a_100g', 'vitamin-c_100g', 'vitamin-d_100g', 'magnesium_100g',
  'zinc_100g', 'phosphorus_100g', 'trans-fat_100g',
  'monounsaturated-fat_100g', 'polyunsaturated-fat_100g',
];
for (const n of TRACKED_NUTRIENTS) nutrientCounts[n] = 0;

// Package quantity distribution (in grams/ml, for US products with complete data)
const quantityBuckets = { '<100g': 0, '100-250g': 0, '250-500g': 0, '500-1000g': 0, '>1000g': 0 };
const unitDistribution = {};

// Category counts (top-level)
const categoryCounts = {};

// Quantity unit distribution
const qtyUnitCounts = {};

// Sample diverse products with good nutrient data
const sampleProducts = [];
const MAX_SAMPLES = 50;

async function run() {
  console.log('Processing ' + INPUT + '...');
  const startTime = Date.now();

  const fileStream = fs.createReadStream(INPUT);
  const gunzip = zlib.createGunzip();
  const rl = readline.createInterface({ input: fileStream.pipe(gunzip) });

  let lineNum = 0;

  for await (const line of rl) {
    lineNum++;
    if (lineNum > MAX_LINES) break;
    if (lineNum % 500000 === 0) {
      const elapsed = ((Date.now() - startTime) / 1000).toFixed(0);
      console.log('  ...processed ' + lineNum + ' lines (' + elapsed + 's), US=' + usProducts);
    }

    let product;
    try {
      product = JSON.parse(line);
    } catch {
      continue;
    }

    totalProducts++;

    // Filter: US products only
    const countries = product.countries_tags || [];
    if (!countries.includes('en:united-states')) continue;

    usProducts++;

    // Name
    const name = product.product_name || product.product_name_en || '';
    if (name.trim()) usWithName++;

    // Quantity
    const qty = product.product_quantity;
    const qtyUnit = product.product_quantity_unit || '';
    if (qty && qty > 0) {
      usWithQuantity++;
      const u = qtyUnit.toLowerCase() || 'unknown';
      qtyUnitCounts[u] = (qtyUnitCounts[u] || 0) + 1;

      if (u === 'g' || u === 'ml') {
        if (qty < 100) quantityBuckets['<100g']++;
        else if (qty <= 250) quantityBuckets['100-250g']++;
        else if (qty <= 500) quantityBuckets['250-500g']++;
        else if (qty <= 1000) quantityBuckets['500-1000g']++;
        else quantityBuckets['>1000g']++;
      }
    }

    // Nutrients
    const nut = product.nutriments || {};
    const nutKeys100g = Object.keys(nut).filter(k => k.endsWith('_100g'));
    if (nutKeys100g.length > 0) usWithNutrients++;

    const hasMacros = ['proteins_100g', 'carbohydrates_100g', 'fat_100g', 'energy-kcal_100g']
      .every(k => nut[k] !== undefined && nut[k] !== '');
    if (hasMacros) usWithMacros++;

    const hasMicros = ['calcium_100g', 'iron_100g', 'potassium_100g']
      .some(k => nut[k] !== undefined && nut[k] !== '' && nut[k] !== 0);
    if (hasMicros) usWithMicros++;

    for (const n of TRACKED_NUTRIENTS) {
      if (nut[n] !== undefined && nut[n] !== '') nutrientCounts[n]++;
    }

    // Ingredients text
    const ingText = product.ingredients_text || product.ingredients_text_en || '';
    if (ingText.trim()) usWithIngredients++;

    // Categories
    const cats = product.categories_tags || [];
    if (cats.length > 0) {
      // Use 2nd level category if available (more specific than top level)
      const cat = cats.length > 1 ? cats[1] : cats[0];
      categoryCounts[cat] = (categoryCounts[cat] || 0) + 1;
    }

    // Collect samples of diverse, nutrient-rich products
    if (sampleProducts.length < MAX_SAMPLES && hasMacros && nutKeys100g.length > 12 && name.trim()) {
      sampleProducts.push({
        name: name.substring(0, 50),
        brand: (product.brands || '-').substring(0, 20),
        qty: product.quantity || '-',
        nutrients: nutKeys100g.length,
        cats: (cats.slice(0, 3).join(', ')).substring(0, 60),
      });
    }
  }

  const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
  console.log('\n' + '='.repeat(70));
  console.log('=== OPEN FOOD FACTS JSONL ANALYSIS COMPLETE ===');
  console.log('='.repeat(70));
  console.log('Processing time:       ' + elapsed + 's');
  console.log('Total products:        ' + totalProducts.toLocaleString());
  console.log('US products:           ' + usProducts.toLocaleString() + ' (' + ((usProducts / totalProducts) * 100).toFixed(1) + '%)');

  console.log('\n--- US Product Field Completeness ---');
  console.log('Has product name:      ' + usWithName.toLocaleString() + ' (' + ((usWithName / usProducts) * 100).toFixed(1) + '%)');
  console.log('Has quantity:          ' + usWithQuantity.toLocaleString() + ' (' + ((usWithQuantity / usProducts) * 100).toFixed(1) + '%)');
  console.log('Has any nutrients:     ' + usWithNutrients.toLocaleString() + ' (' + ((usWithNutrients / usProducts) * 100).toFixed(1) + '%)');
  console.log('Has full macros:       ' + usWithMacros.toLocaleString() + ' (' + ((usWithMacros / usProducts) * 100).toFixed(1) + '%)');
  console.log('Has some micros:       ' + usWithMicros.toLocaleString() + ' (' + ((usWithMicros / usProducts) * 100).toFixed(1) + '%)');
  console.log('Has ingredients text:  ' + usWithIngredients.toLocaleString() + ' (' + ((usWithIngredients / usProducts) * 100).toFixed(1) + '%)');

  console.log('\n--- Nutrient Field Completeness (US products) ---');
  for (const [k, v] of Object.entries(nutrientCounts).sort((a, b) => b[1] - a[1])) {
    const pct = ((v / usProducts) * 100).toFixed(1);
    console.log('  ' + k.padEnd(30) + String(v).padStart(8) + ' (' + pct + '%)');
  }

  console.log('\n--- Quantity Unit Distribution (US with qty) ---');
  for (const [u, c] of Object.entries(qtyUnitCounts).sort((a, b) => b[1] - a[1])) {
    console.log('  ' + u.padEnd(15) + String(c).padStart(8));
  }

  console.log('\n--- Package Size Distribution (g/ml) ---');
  for (const [bucket, count] of Object.entries(quantityBuckets)) {
    console.log('  ' + bucket.padEnd(15) + String(count).padStart(8));
  }

  console.log('\n--- Top 30 Categories (US) ---');
  const topCats = Object.entries(categoryCounts).sort((a, b) => b[1] - a[1]).slice(0, 30);
  for (const [cat, count] of topCats) {
    console.log('  ' + cat.padEnd(50) + String(count).padStart(8));
  }

  console.log('\n--- Sample Nutrient-Rich US Products ---');
  for (const s of sampleProducts.slice(0, 20)) {
    console.log('  ' + s.name.padEnd(52) + s.brand.padEnd(22) + 'qty=' + String(s.qty).padEnd(15) + 'n=' + s.nutrients);
  }
}

run().catch(console.error);
