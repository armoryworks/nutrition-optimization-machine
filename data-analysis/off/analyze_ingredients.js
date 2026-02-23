// Search OFF for common cooking ingredients and analyze data quality
// for NOM's ingredient + nutrient database needs

const https = require('https');

const INGREDIENT_SEARCHES = [
  // Produce
  { search: 'tomatoes', category: 'Produce' },
  { search: 'onion', category: 'Produce' },
  { search: 'garlic', category: 'Produce' },
  { search: 'bell pepper', category: 'Produce' },
  { search: 'broccoli', category: 'Produce' },
  { search: 'spinach', category: 'Produce' },
  // Proteins
  { search: 'ground beef', category: 'Meat' },
  { search: 'chicken breast', category: 'Meat' },
  { search: 'salmon', category: 'Seafood' },
  { search: 'tofu', category: 'Protein' },
  { search: 'eggs', category: 'Dairy' },
  // Dairy
  { search: 'cheddar cheese', category: 'Dairy' },
  { search: 'butter', category: 'Dairy' },
  { search: 'heavy cream', category: 'Dairy' },
  { search: 'yogurt', category: 'Dairy' },
  // Pantry
  { search: 'olive oil', category: 'Pantry' },
  { search: 'soy sauce', category: 'Pantry' },
  { search: 'all purpose flour', category: 'Pantry' },
  { search: 'white rice', category: 'Pantry' },
  { search: 'black beans', category: 'Pantry' },
  { search: 'coconut milk', category: 'Pantry' },
  { search: 'honey', category: 'Pantry' },
  { search: 'peanut butter', category: 'Pantry' },
];

function fetchProduct(searchTerm) {
  return new Promise((resolve, reject) => {
    const url = `https://us.openfoodfacts.org/api/v2/search?search_terms=${encodeURIComponent(searchTerm)}&countries_tags_en=united-states&page_size=5&fields=code,product_name,brands,categories,quantity,product_quantity,product_quantity_unit,serving_size,packaging,nutriments&sort_by=unique_scans_n`;
    const options = { headers: { 'User-Agent': 'NOM-DataAnalysis/1.0' } };

    https.get(url, options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try {
          resolve(JSON.parse(data));
        } catch (e) {
          reject(e);
        }
      });
    }).on('error', reject);
  });
}

function sleep(ms) {
  return new Promise(r => setTimeout(r, ms));
}

async function main() {
  console.log('=== COMMON COOKING INGREDIENTS IN OPEN FOOD FACTS (US) ===\n');

  const results = [];

  for (const item of INGREDIENT_SEARCHES) {
    await sleep(700); // rate limit: 10 req/min for searches
    try {
      const data = await fetchProduct(item.search);
      const count = data.count || 0;
      const products = data.products || [];
      const top = products[0];

      // Check nutrient completeness of top result
      let nutrientCount = 0;
      let hasMacros = false;
      let hasMicros = false;
      if (top && top.nutriments) {
        const keys100g = Object.keys(top.nutriments).filter(k => k.endsWith('_100g'));
        nutrientCount = keys100g.length;
        hasMacros = ['proteins_100g', 'carbohydrates_100g', 'fat_100g', 'energy-kcal_100g']
          .every(k => top.nutriments[k] !== undefined);
        hasMicros = ['calcium_100g', 'iron_100g', 'potassium_100g', 'vitamin-c_100g']
          .some(k => top.nutriments[k] !== undefined);
      }

      const topName = top ? (top.product_name || '?').substring(0, 35) : 'N/A';
      const topBrand = top ? (top.brands || '-').substring(0, 15) : '-';
      const topQty = top ? (top.quantity || '-').substring(0, 12) : '-';

      console.log(item.search.padEnd(20) + ' | ' + String(count).padStart(5) + ' hits | ' +
        topName.padEnd(37) + topBrand.padEnd(17) + topQty.padEnd(14) +
        'nutrients=' + String(nutrientCount).padStart(2) +
        ' macros=' + (hasMacros ? 'Y' : 'N') +
        ' micros=' + (hasMicros ? 'Y' : 'N'));

      results.push({
        search: item.search,
        category: item.category,
        count,
        topProduct: top,
        nutrientCount,
        hasMacros,
        hasMicros,
      });
    } catch (e) {
      console.log(item.search.padEnd(20) + ' | ERROR: ' + e.message);
    }
  }

  // Summary statistics
  console.log('\n=== SUMMARY ===');
  const withHits = results.filter(r => r.count > 0);
  const withMacros = results.filter(r => r.hasMacros);
  const withMicros = results.filter(r => r.hasMicros);
  console.log('Ingredients searched: ' + results.length);
  console.log('With OFF matches:     ' + withHits.length + '/' + results.length);
  console.log('Top hit has macros:   ' + withMacros.length + '/' + results.length);
  console.log('Top hit has micros:   ' + withMicros.length + '/' + results.length);

  // Show detailed nutrient profile for one well-populated product
  const best = results.find(r => r.nutrientCount > 15);
  if (best && best.topProduct && best.topProduct.nutriments) {
    console.log('\n=== DETAILED NUTRIENT PROFILE: ' + best.search + ' ===');
    console.log('Product: ' + best.topProduct.product_name + ' (' + best.topProduct.brands + ')');
    const keys = Object.keys(best.topProduct.nutriments).filter(k => k.endsWith('_100g')).sort();
    for (const k of keys) {
      const v = best.topProduct.nutriments[k];
      if (v !== 0 && v !== '' && v !== undefined) {
        console.log('  ' + k.padEnd(40) + v);
      }
    }
  }

  // Show packaging/quantity data for retail packaging use
  console.log('\n=== PACKAGE SIZE DATA (for retail packaging extraction) ===');
  for (const r of results) {
    if (!r.topProduct) continue;
    const p = r.topProduct;
    console.log('  ' + r.search.padEnd(20) +
      'qty=' + String(p.quantity || '-').padEnd(15) +
      'pq=' + String(p.product_quantity || '-').padEnd(8) +
      'unit=' + String(p.product_quantity_unit || '-').padEnd(5) +
      'serving=' + String(p.serving_size || '-'));
  }
}

main().catch(console.error);
