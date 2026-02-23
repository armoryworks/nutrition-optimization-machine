// Analyze USDA FoodData Central CSV dump for NOM import feasibility
// Usage: node analyze_usda.js

const fs = require('fs');
const path = require('path');
const readline = require('readline');

const BASE = 'C:/Users/danielh/Downloads/FoodData_Central_csv_2025-12-18';

function parseCSVLine(line) {
  const fields = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && i + 1 < line.length && line[i + 1] === '"') {
        current += '"';
        i++;
      } else {
        inQuotes = !inQuotes;
      }
    } else if (ch === ',' && !inQuotes) {
      fields.push(current);
      current = '';
    } else {
      current += ch;
    }
  }
  fields.push(current);
  return fields;
}

async function readCSV(filename, opts = {}) {
  const filePath = path.join(BASE, filename);
  const rl = readline.createInterface({ input: fs.createReadStream(filePath) });
  let headers = null;
  const rows = [];
  let lineNum = 0;
  const limit = opts.limit || Infinity;

  for await (const line of rl) {
    lineNum++;
    const fields = parseCSVLine(line);
    if (!headers) {
      headers = fields;
      continue;
    }
    if (rows.length >= limit) break;
    const row = {};
    for (let i = 0; i < headers.length; i++) {
      row[headers[i]] = fields[i] || '';
    }
    rows.push(row);
  }
  return { headers, rows, totalLines: lineNum - 1 };
}

async function streamCSV(filename, callback) {
  const filePath = path.join(BASE, filename);
  const rl = readline.createInterface({ input: fs.createReadStream(filePath) });
  let headers = null;
  let count = 0;

  for await (const line of rl) {
    const fields = parseCSVLine(line);
    if (!headers) {
      headers = fields;
      continue;
    }
    count++;
    const row = {};
    for (let i = 0; i < headers.length; i++) {
      row[headers[i]] = fields[i] || '';
    }
    callback(row, count);
    if (count % 500000 === 0) process.stdout.write('  ...' + count.toLocaleString() + ' rows\n');
  }
  return count;
}

async function run() {
  const startTime = Date.now();
  console.log('='.repeat(70));
  console.log('=== USDA FOODDATA CENTRAL — DATA QUALITY ANALYSIS ===');
  console.log('='.repeat(70));

  // ──────────────────────────────────────────────
  // 1. FOOD TABLE — data_type distribution & naming quality
  // ──────────────────────────────────────────────
  console.log('\n--- 1. FOOD TABLE: data_type distribution ---');

  const dataTypeCounts = {};
  const categoryCounts = {};
  const nameLengths = { branded: [], sr_legacy: [], foundation: [], survey_fndds: [] };
  const nameExamples = { branded: [], sr_legacy: [], foundation: [], survey_fndds: [] };
  const allCapsCount = { branded: 0, sr_legacy: 0, foundation: 0, survey_fndds: 0 };
  const nameWordCounts = { branded: [], sr_legacy: [], foundation: [], survey_fndds: [] };
  const duplicateNames = {};
  let totalFoods = 0;

  await streamCSV('food.csv', (row) => {
    totalFoods++;
    const dt = row.data_type || 'unknown';
    dataTypeCounts[dt] = (dataTypeCounts[dt] || 0) + 1;

    const cat = row.food_category_id || 'none';
    categoryCounts[cat] = (categoryCounts[cat] || 0) + 1;

    const desc = row.description || '';
    const dtKey = dt.replace(/_food$/, '').replace('branded_food', 'branded');

    if (nameLengths[dtKey]) {
      nameLengths[dtKey].push(desc.length);
      nameWordCounts[dtKey].push(desc.split(/\s+/).length);
      if (desc === desc.toUpperCase() && desc.length > 3) allCapsCount[dtKey]++;
      if (nameExamples[dtKey].length < 15) nameExamples[dtKey].push(desc.substring(0, 80));
    }

    // Track duplicate descriptions (normalized)
    const norm = desc.toLowerCase().trim();
    duplicateNames[norm] = (duplicateNames[norm] || 0) + 1;
  });

  console.log('Total food entries: ' + totalFoods.toLocaleString());
  for (const [dt, count] of Object.entries(dataTypeCounts).sort((a, b) => b[1] - a[1])) {
    console.log('  ' + dt.padEnd(25) + count.toLocaleString().padStart(12) +
      ' (' + ((count / totalFoods) * 100).toFixed(1) + '%)');
  }

  // Name quality per data type
  console.log('\n--- 2. NAMING QUALITY per data_type ---');
  for (const dt of ['branded', 'sr_legacy', 'foundation', 'survey_fndds']) {
    const lens = nameLengths[dt];
    const words = nameWordCounts[dt];
    if (lens.length === 0) continue;
    lens.sort((a, b) => a - b);
    words.sort((a, b) => a - b);
    const medianLen = lens[Math.floor(lens.length / 2)];
    const medianWords = words[Math.floor(words.length / 2)];
    const capsRate = ((allCapsCount[dt] / lens.length) * 100).toFixed(1);
    console.log('\n  ' + dt.toUpperCase() + ' (' + lens.length.toLocaleString() + ' items):');
    console.log('    Median name length:  ' + medianLen + ' chars, ' + medianWords + ' words');
    console.log('    ALL CAPS rate:       ' + capsRate + '%');
    console.log('    Examples:');
    for (const ex of nameExamples[dt]) {
      console.log('      "' + ex + '"');
    }
  }

  // Duplicate analysis
  console.log('\n--- 3. DUPLICATE NAME ANALYSIS ---');
  let exactDupes = 0;
  let dupeGroups = 0;
  const worstDupes = [];
  for (const [name, count] of Object.entries(duplicateNames)) {
    if (count > 1) {
      dupeGroups++;
      exactDupes += count - 1;
      if (worstDupes.length < 20 || count > worstDupes[worstDupes.length - 1][1]) {
        worstDupes.push([name, count]);
        worstDupes.sort((a, b) => b[1] - a[1]);
        if (worstDupes.length > 20) worstDupes.pop();
      }
    }
  }
  console.log('Unique names:          ' + Object.keys(duplicateNames).length.toLocaleString());
  console.log('Duplicate groups:      ' + dupeGroups.toLocaleString());
  console.log('Excess entries (dupes): ' + exactDupes.toLocaleString());
  console.log('Top duplicate names:');
  for (const [name, count] of worstDupes) {
    console.log('  ' + String(count).padStart(5) + 'x  "' + name.substring(0, 70) + '"');
  }

  // Category distribution
  console.log('\n--- 4. CATEGORY DISTRIBUTION ---');
  const catData = await readCSV('food_category.csv');
  const catMap = {};
  for (const r of catData.rows) catMap[r.id] = r.description;

  const sortedCats = Object.entries(categoryCounts).sort((a, b) => b[1] - a[1]).slice(0, 30);
  for (const [catId, count] of sortedCats) {
    const catName = catMap[catId] || catId;
    console.log('  ' + catName.padEnd(45) + count.toLocaleString().padStart(10));
  }

  // ──────────────────────────────────────────────
  // 5. NUTRIENT COMPLETENESS — stream food_nutrient.csv
  // ──────────────────────────────────────────────
  console.log('\n--- 5. NUTRIENT COVERAGE (streaming food_nutrient.csv) ---');

  // Key nutrient IDs we care about for NOM
  const KEY_NUTRIENTS = {
    '1008': 'Energy (kcal)',
    '1003': 'Protein',
    '1004': 'Total Fat',
    '1005': 'Carbohydrate',
    '1079': 'Fiber',
    '2000': 'Total Sugars',
    '1258': 'Saturated Fat',
    '1257': 'Trans Fat',
    '1292': 'Monounsat Fat',
    '1293': 'Polyunsat Fat',
    '1093': 'Sodium',
    '1253': 'Cholesterol',
    '1087': 'Calcium',
    '1089': 'Iron',
    '1092': 'Potassium',
    '1090': 'Magnesium',
    '1091': 'Phosphorus',
    '1095': 'Zinc',
    '1104': 'Vitamin A (IU)',
    '1162': 'Vitamin C',
    '1114': 'Vitamin D',
    '1109': 'Vitamin E',
    '1183': 'Vitamin K',
    '1165': 'Thiamin (B1)',
    '1166': 'Riboflavin (B2)',
    '1167': 'Niacin (B3)',
    '1175': 'Vitamin B6',
    '1177': 'Folate',
    '1178': 'Vitamin B12',
  };

  // Per-food: which key nutrients does it have?
  const foodNutrientSets = {}; // fdc_id -> Set of nutrient_ids
  let totalNutrientRows = 0;
  const nutrientIdCounts = {}; // nutrient_id -> count of foods that have it

  await streamCSV('food_nutrient.csv', (row) => {
    totalNutrientRows++;
    const fid = row.fdc_id;
    const nid = row.nutrient_id;
    const amt = row.amount;

    if (KEY_NUTRIENTS[nid] && amt !== '' && amt !== undefined) {
      if (!foodNutrientSets[fid]) foodNutrientSets[fid] = new Set();
      foodNutrientSets[fid].add(nid);
      nutrientIdCounts[nid] = (nutrientIdCounts[nid] || 0) + 1;
    }
  });

  console.log('Total nutrient rows:   ' + totalNutrientRows.toLocaleString());
  console.log('Foods with any key nutrient: ' + Object.keys(foodNutrientSets).length.toLocaleString());

  // How many foods have complete macros? Complete micros?
  const macroIds = ['1008', '1003', '1004', '1005'];
  const microIds = ['1087', '1089', '1092', '1104', '1162'];
  let completeMacros = 0;
  let completeMicros = 0;
  let complete15 = 0;

  for (const [fid, nset] of Object.entries(foodNutrientSets)) {
    if (macroIds.every(id => nset.has(id))) completeMacros++;
    if (microIds.every(id => nset.has(id))) completeMicros++;
    if (nset.size >= 15) complete15++;
  }

  console.log('With complete macros (kcal+P+F+C): ' + completeMacros.toLocaleString());
  console.log('With 5 key micros (Ca,Fe,K,VitA,VitC): ' + completeMicros.toLocaleString());
  console.log('With 15+ key nutrients: ' + complete15.toLocaleString());

  console.log('\nPer-nutrient coverage:');
  for (const [nid, name] of Object.entries(KEY_NUTRIENTS).sort((a, b) => (nutrientIdCounts[b[0]] || 0) - (nutrientIdCounts[a[0]] || 0))) {
    const count = nutrientIdCounts[nid] || 0;
    console.log('  ' + name.padEnd(25) + count.toLocaleString().padStart(12));
  }

  // ──────────────────────────────────────────────
  // 6. BRANDED FOOD — analyze brand/category mess
  // ──────────────────────────────────────────────
  console.log('\n--- 6. BRANDED FOOD QUALITY ---');
  const brandOwners = {};
  const brandedCategories = {};
  let brandedWithIngredients = 0;
  let brandedWithServing = 0;
  let brandedTotal = 0;
  const brandedNameExamples = [];

  await streamCSV('branded_food.csv', (row) => {
    brandedTotal++;
    const owner = row.brand_owner || 'unknown';
    brandOwners[owner] = (brandOwners[owner] || 0) + 1;

    const cat = row.branded_food_category || 'none';
    brandedCategories[cat] = (brandedCategories[cat] || 0) + 1;

    if (row.ingredients && row.ingredients.trim()) brandedWithIngredients++;
    if (row.serving_size && row.serving_size.trim()) brandedWithServing++;

    if (brandedNameExamples.length < 5) {
      brandedNameExamples.push({
        owner: (owner).substring(0, 30),
        cat: cat.substring(0, 30),
        serving: row.household_serving_fulltext || '-',
        size: row.serving_size + ' ' + row.serving_size_unit,
      });
    }
  });

  console.log('Branded foods total:   ' + brandedTotal.toLocaleString());
  console.log('With ingredients text:  ' + brandedWithIngredients.toLocaleString() + ' (' + ((brandedWithIngredients / brandedTotal) * 100).toFixed(1) + '%)');
  console.log('With serving size:      ' + brandedWithServing.toLocaleString() + ' (' + ((brandedWithServing / brandedTotal) * 100).toFixed(1) + '%)');
  console.log('Unique brand owners:    ' + Object.keys(brandOwners).length.toLocaleString());
  console.log('Unique branded categories: ' + Object.keys(brandedCategories).length.toLocaleString());

  console.log('\nTop 20 brand owners:');
  for (const [owner, count] of Object.entries(brandOwners).sort((a, b) => b[1] - a[1]).slice(0, 20)) {
    console.log('  ' + owner.substring(0, 40).padEnd(42) + count.toLocaleString().padStart(8));
  }

  console.log('\nTop 30 branded food categories:');
  for (const [cat, count] of Object.entries(brandedCategories).sort((a, b) => b[1] - a[1]).slice(0, 30)) {
    console.log('  ' + cat.substring(0, 45).padEnd(47) + count.toLocaleString().padStart(8));
  }

  // ──────────────────────────────────────────────
  // 7. SR LEGACY — the "generic" foods
  // ──────────────────────────────────────────────
  console.log('\n--- 7. SR LEGACY FOODS (generics) ---');
  const srData = await readCSV('sr_legacy_food.csv');
  const srIds = new Set(srData.rows.map(r => r.fdc_id));
  console.log('SR Legacy entries: ' + srIds.size);

  // Cross-reference with food.csv to get names
  const srNames = [];
  await streamCSV('food.csv', (row) => {
    if (srIds.has(row.fdc_id)) {
      srNames.push({ id: row.fdc_id, name: row.description, cat: row.food_category_id });
    }
  });

  // Show examples per category
  const srByCategory = {};
  for (const sr of srNames) {
    const catName = catMap[sr.cat] || sr.cat;
    if (!srByCategory[catName]) srByCategory[catName] = [];
    srByCategory[catName].push(sr.name);
  }

  console.log('SR Legacy by category:');
  for (const [cat, names] of Object.entries(srByCategory).sort((a, b) => b[1].length - a[1].length)) {
    console.log('  ' + cat + ' (' + names.length + '):');
    for (const n of names.slice(0, 5)) {
      console.log('    "' + n.substring(0, 75) + '"');
    }
    if (names.length > 5) console.log('    ... and ' + (names.length - 5) + ' more');
  }

  // ──────────────────────────────────────────────
  // 8. FOUNDATION FOODS — highest quality generics
  // ──────────────────────────────────────────────
  console.log('\n--- 8. FOUNDATION FOODS (highest quality) ---');
  const ffData = await readCSV('foundation_food.csv');
  const ffIds = new Set(ffData.rows.map(r => r.fdc_id));
  console.log('Foundation Food entries: ' + ffIds.size);

  const ffNames = [];
  await streamCSV('food.csv', (row) => {
    if (ffIds.has(row.fdc_id)) {
      ffNames.push({ id: row.fdc_id, name: row.description, cat: row.food_category_id });
    }
  });

  console.log('Foundation foods with names: ' + ffNames.length);
  for (const ff of ffNames.slice(0, 30)) {
    const catName = catMap[ff.cat] || ff.cat;
    const hasNutrients = foodNutrientSets[ff.id] ? foodNutrientSets[ff.id].size : 0;
    console.log('  ' + ff.name.substring(0, 55).padEnd(57) + catName.substring(0, 25).padEnd(27) + 'nutrients=' + hasNutrients);
  }

  // ──────────────────────────────────────────────
  // 9. PORTION DATA
  // ──────────────────────────────────────────────
  console.log('\n--- 9. PORTION / SERVING DATA ---');
  const portionData = await readCSV('food_portion.csv');
  console.log('Total portion entries: ' + portionData.rows.length.toLocaleString());

  const portionModifiers = {};
  for (const r of portionData.rows) {
    const mod = (r.modifier || 'empty').toLowerCase().substring(0, 30);
    portionModifiers[mod] = (portionModifiers[mod] || 0) + 1;
  }
  console.log('Top portion modifiers:');
  for (const [mod, count] of Object.entries(portionModifiers).sort((a, b) => b[1] - a[1]).slice(0, 25)) {
    console.log('  ' + mod.padEnd(35) + count.toLocaleString().padStart(8));
  }

  const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
  console.log('\n' + '='.repeat(70));
  console.log('Analysis complete in ' + elapsed + 's');
}

run().catch(console.error);
