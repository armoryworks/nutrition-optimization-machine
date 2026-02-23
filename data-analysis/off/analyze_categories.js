const fs = require('fs');

function analyzeCategory(filename, label) {
  const data = JSON.parse(fs.readFileSync(filename, 'utf8'));
  const products = data.products;
  console.log('\n' + '='.repeat(60));
  console.log('=== ' + label.toUpperCase() + ' (US) — ' + data.count + ' total, showing ' + products.length + ' ===');

  for (const p of products.slice(0, 10)) {
    const name = (p.product_name || '?').substring(0, 35).padEnd(37);
    const brand = (p.brands || '-').substring(0, 15).padEnd(17);
    const qty = String(p.quantity || '-').substring(0, 12).padEnd(14);
    const serving = String(p.serving_size || '-').substring(0, 15).padEnd(17);
    const unit = String(p.product_quantity_unit || '-').padEnd(4);
    console.log('  ' + name + brand + qty + unit + '  srv=' + serving);
  }

  // Nutrient quality check
  console.log('\n  Nutrient completeness:');
  const nutrients = ['energy-kcal_100g', 'proteins_100g', 'carbohydrates_100g', 'fat_100g', 'fiber_100g', 'sodium_100g', 'cholesterol_100g', 'iron_100g', 'calcium_100g', 'potassium_100g'];
  for (const k of nutrients) {
    const filled = products.filter(p => p.nutriments && p.nutriments[k] !== undefined).length;
    const pct = products.length > 0 ? ((filled / products.length) * 100).toFixed(0) : '0';
    console.log('    ' + k.padEnd(25) + filled + '/' + products.length + ' (' + pct + '%)');
  }

  // Show one full nutrient profile
  const best = products.find(p => p.nutriments && Object.keys(p.nutriments).filter(k => k.endsWith('_100g')).length > 10);
  if (best) {
    console.log('\n  Best nutrient profile: ' + (best.product_name || '?') + ' (' + (best.brands || '?') + ')');
    const keys = Object.keys(best.nutriments).filter(k => k.endsWith('_100g')).sort();
    for (const k of keys) {
      if (best.nutriments[k] !== 0 && best.nutriments[k] !== '') {
        console.log('    ' + k.padEnd(35) + best.nutriments[k]);
      }
    }
  }
}

analyzeCategory('chicken.json', 'Chicken Breasts');
analyzeCategory('pasta.json', 'Pasta');
analyzeCategory('olive_oil.json', 'Olive Oils');
analyzeCategory('milk.json', 'Milks');
