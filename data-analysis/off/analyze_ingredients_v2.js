// Use CGI search endpoint which actually works for text search
const https = require('https');

const INGREDIENTS = [
  'tomato', 'onion', 'garlic', 'bell pepper', 'broccoli', 'spinach', 'carrot',
  'ground beef', 'chicken breast', 'salmon fillet', 'shrimp', 'bacon', 'tofu',
  'eggs', 'cheddar cheese', 'mozzarella', 'butter', 'heavy cream', 'yogurt', 'milk',
  'olive oil', 'soy sauce', 'flour', 'white rice', 'spaghetti', 'black beans',
  'coconut milk', 'honey', 'peanut butter', 'bread',
];

function search(term) {
  return new Promise((resolve, reject) => {
    const url = 'https://world.openfoodfacts.org/cgi/search.pl?' +
      'search_terms=' + encodeURIComponent(term) +
      '&search_simple=1&action=process&json=1&page_size=5' +
      '&countries=United+States' +
      '&fields=code,product_name,brands,quantity,product_quantity,product_quantity_unit,serving_size,nutriments,categories_tags,packaging';
    const options = { headers: { 'User-Agent': 'NOM-DataAnalysis/1.0' } };

    https.get(url, options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        try { resolve(JSON.parse(data)); }
        catch (e) { reject(e); }
      });
    }).on('error', reject);
  });
}

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

async function main() {
  console.log('=== COMMON COOKING INGREDIENTS — OFF CGI SEARCH (US) ===\n');
  console.log('Search'.padEnd(20) + 'Hits'.padStart(6) + '  ' +
    'Top Product'.padEnd(35) + 'Brand'.padEnd(17) +
    'Qty'.padEnd(15) + 'Nutr'.padStart(4) + ' Mac Mic Pkg');
  console.log('-'.repeat(120));

  const allResults = [];

  for (const term of INGREDIENTS) {
    await sleep(6500); // 10 req/min limit for search
    try {
      const data = await search(term);
      const count = data.count || 0;
      const products = data.products || [];
      const p = products[0];

      let nutrientCount = 0, hasMacros = false, hasMicros = false, hasPkg = false;
      if (p && p.nutriments) {
        const k100 = Object.keys(p.nutriments).filter(k => k.endsWith('_100g'));
        nutrientCount = k100.length;
        hasMacros = ['proteins_100g', 'carbohydrates_100g', 'fat_100g', 'energy-kcal_100g']
          .every(k => p.nutriments[k] !== undefined);
        hasMicros = ['calcium_100g', 'iron_100g', 'potassium_100g']
          .some(k => p.nutriments[k] !== undefined && p.nutriments[k] !== 0);
      }
      if (p) hasPkg = !!(p.quantity && String(p.quantity).trim());

      console.log(
        term.padEnd(20) +
        String(count).padStart(6) + '  ' +
        (p ? (p.product_name || '?').substring(0, 33) : 'N/A').padEnd(35) +
        (p ? (p.brands || '-').substring(0, 15) : '-').padEnd(17) +
        (p ? (p.quantity || '-').substring(0, 13) : '-').padEnd(15) +
        String(nutrientCount).padStart(4) + '  ' +
        (hasMacros ? 'Y' : 'N') + '   ' +
        (hasMicros ? 'Y' : 'N') + '   ' +
        (hasPkg ? 'Y' : 'N')
      );

      allResults.push({ term, count, product: p, nutrientCount, hasMacros, hasMicros, hasPkg });
    } catch (e) {
      console.log(term.padEnd(20) + ' ERROR: ' + e.message);
      allResults.push({ term, count: 0, product: null, nutrientCount: 0, hasMacros: false, hasMicros: false, hasPkg: false });
    }
  }

  // Summary
  console.log('\n=== SUMMARY ===');
  const total = allResults.length;
  console.log('Total ingredients searched: ' + total);
  console.log('With matches (>0 hits):    ' + allResults.filter(r => r.count > 0).length);
  console.log('With macros in top hit:    ' + allResults.filter(r => r.hasMacros).length);
  console.log('With micros in top hit:    ' + allResults.filter(r => r.hasMicros).length);
  console.log('With package size:         ' + allResults.filter(r => r.hasPkg).length);
  console.log('Avg nutrients per product: ' + (allResults.reduce((s, r) => s + r.nutrientCount, 0) / total).toFixed(1));

  // Show best nutrient profile
  const best = allResults.filter(r => r.nutrientCount > 10).sort((a, b) => b.nutrientCount - a.nutrientCount)[0];
  if (best && best.product && best.product.nutriments) {
    console.log('\n=== RICHEST NUTRIENT PROFILE: ' + best.term + ' ===');
    console.log('Product: ' + best.product.product_name + ' (' + best.product.brands + ')');
    const keys = Object.keys(best.product.nutriments).filter(k => k.endsWith('_100g')).sort();
    for (const k of keys) {
      const v = best.product.nutriments[k];
      if (v !== '' && v !== undefined) {
        console.log('  ' + k.padEnd(40) + v);
      }
    }
  }
}

main().catch(console.error);
