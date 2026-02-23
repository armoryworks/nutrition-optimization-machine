const fs = require('fs');
const data = JSON.parse(fs.readFileSync('us_sample_50.json', 'utf8'));
const products = data.products;

console.log('=== FIELD COMPLETENESS (50 US products) ===');
const fields = [
  'product_name', 'brands', 'categories', 'quantity',
  'product_quantity', 'product_quantity_unit', 'packaging',
  'ingredients_text_en', 'nutriscore_grade', 'nova_group'
];
for (const f of fields) {
  const filled = products.filter(p => p[f] && String(p[f]).trim()).length;
  const pct = ((filled / 50) * 100).toFixed(0);
  console.log('  ' + f.padEnd(25) + filled + '/50 (' + pct + '%)');
}

console.log('\n=== NUTRIMENT FIELD COMPLETENESS ===');
const nutrientKeys = new Set();
for (const p of products) {
  if (p.nutriments) Object.keys(p.nutriments).forEach(k => nutrientKeys.add(k));
}
const per100g = [...nutrientKeys].filter(k => k.endsWith('_100g')).sort();
console.log('Total unique nutrient_100g fields across sample: ' + per100g.length);

const important = [
  'energy-kcal_100g', 'proteins_100g', 'carbohydrates_100g', 'sugars_100g',
  'fat_100g', 'saturated-fat_100g', 'fiber_100g', 'sodium_100g',
  'cholesterol_100g', 'calcium_100g', 'iron_100g', 'potassium_100g',
  'vitamin-a_100g', 'vitamin-c_100g'
];
for (const k of important) {
  const filled = products.filter(p => p.nutriments && p.nutriments[k] !== undefined && p.nutriments[k] !== '').length;
  const pct = ((filled / 50) * 100).toFixed(0);
  console.log('  ' + k.padEnd(25) + filled + '/50 (' + pct + '%)');
}

console.log('\n=== ALL nutrient_100g FIELDS FOUND ===');
console.log(per100g.join(', '));

console.log('\n=== PACKAGING / QUANTITY EXAMPLES (first 20) ===');
for (const p of products.slice(0, 20)) {
  const name = (p.product_name || '?').substring(0, 35).padEnd(37);
  const qty = String(p.quantity || '-').padEnd(18);
  const pqty = String(p.product_quantity || '-').padEnd(8);
  const unit = String(p.product_quantity_unit || '-').padEnd(6);
  const pkg = (p.packaging || '-').substring(0, 30);
  console.log('  ' + name + ' qty=' + qty + ' pq=' + pqty + ' unit=' + unit + ' pkg=' + pkg);
}

console.log('\n=== NUTRIENT DETAIL FOR FIRST 3 PRODUCTS ===');
for (const p of products.slice(0, 3)) {
  console.log('\n--- ' + (p.product_name || '?') + ' ---');
  if (p.nutriments) {
    const keys = Object.keys(p.nutriments).filter(k => k.endsWith('_100g')).sort();
    for (const k of keys) {
      console.log('  ' + k.padEnd(35) + p.nutriments[k]);
    }
  }
}

console.log('\n=== CATEGORIES EXAMPLES ===');
for (const p of products.slice(0, 10)) {
  console.log('  ' + (p.product_name || '?').substring(0, 30).padEnd(32) + (p.categories || '-'));
}
