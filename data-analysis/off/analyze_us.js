const fs = require('fs');
const data = JSON.parse(fs.readFileSync('us_sample_page5.json', 'utf8'));
const products = data.products;

console.log('=== PAGE 5 - US PRODUCTS (language=en) ===');
console.log('Total in query: ' + data.count);
console.log('Products on page: ' + products.length);

console.log('\n=== FIELD COMPLETENESS ===');
const fields = [
  'product_name', 'brands', 'categories', 'quantity',
  'product_quantity', 'product_quantity_unit', 'packaging',
  'ingredients_text_en', 'nutriscore_grade', 'nova_group'
];
for (const f of fields) {
  const filled = products.filter(p => p[f] && String(p[f]).trim()).length;
  const pct = ((filled / products.length) * 100).toFixed(0);
  console.log('  ' + f.padEnd(25) + filled + '/' + products.length + ' (' + pct + '%)');
}

console.log('\n=== KEY NUTRIENT COMPLETENESS ===');
const important = [
  'energy-kcal_100g', 'proteins_100g', 'carbohydrates_100g', 'sugars_100g',
  'fat_100g', 'saturated-fat_100g', 'fiber_100g', 'sodium_100g',
  'cholesterol_100g', 'calcium_100g', 'iron_100g', 'potassium_100g',
  'vitamin-a_100g', 'vitamin-c_100g', 'vitamin-d_100g',
  'trans-fat_100g', 'monounsaturated-fat_100g', 'polyunsaturated-fat_100g'
];
for (const k of important) {
  const filled = products.filter(p => p.nutriments && p.nutriments[k] !== undefined && p.nutriments[k] !== '').length;
  const pct = ((filled / products.length) * 100).toFixed(0);
  console.log('  ' + k.padEnd(30) + filled + '/' + products.length + ' (' + pct + '%)');
}

console.log('\n=== PRODUCT EXAMPLES (name, brand, qty, packaging) ===');
for (const p of products.slice(0, 25)) {
  const name = (p.product_name || '?').substring(0, 30).padEnd(32);
  const brand = (p.brands || '-').substring(0, 15).padEnd(17);
  const qty = String(p.quantity || '-').substring(0, 15).padEnd(17);
  const pkg = (p.packaging || '-').substring(0, 25);
  console.log('  ' + name + brand + qty + pkg);
}

// Analyze quantity format patterns
console.log('\n=== QUANTITY FORMAT PATTERNS ===');
const qtyFormats = {};
for (const p of products) {
  const q = p.quantity || '';
  let format = 'empty';
  if (/^\d+(\.\d+)?\s*(g|kg|oz|lb)\b/i.test(q)) format = 'mass';
  else if (/^\d+(\.\d+)?\s*(ml|l|cl|fl\s*oz)\b/i.test(q)) format = 'volume';
  else if (/\d+\s*x\s*\d+/i.test(q)) format = 'multipack';
  else if (q) format = 'other: ' + q.substring(0, 30);
  qtyFormats[format] = (qtyFormats[format] || 0) + 1;
}
for (const [fmt, count] of Object.entries(qtyFormats)) {
  console.log('  ' + fmt + ': ' + count);
}

// Analyze product_quantity_unit distribution
console.log('\n=== product_quantity_unit DISTRIBUTION ===');
const units = {};
for (const p of products) {
  const u = p.product_quantity_unit || 'empty';
  units[u] = (units[u] || 0) + 1;
}
for (const [u, count] of Object.entries(units).sort((a,b) => b[1]-a[1])) {
  console.log('  ' + u + ': ' + count);
}

// Analyze packaging field values
console.log('\n=== PACKAGING VALUES ===');
const pkgValues = {};
for (const p of products) {
  const pkg = p.packaging || '';
  if (pkg) {
    const parts = pkg.split(',').map(s => s.trim().toLowerCase()).filter(Boolean);
    for (const part of parts) {
      pkgValues[part] = (pkgValues[part] || 0) + 1;
    }
  }
}
const sorted = Object.entries(pkgValues).sort((a,b) => b[1]-a[1]).slice(0, 20);
for (const [val, count] of sorted) {
  console.log('  ' + val + ': ' + count);
}
