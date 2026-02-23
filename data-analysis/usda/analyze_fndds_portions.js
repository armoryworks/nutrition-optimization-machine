// Analyze FNDDS (Survey) food portions — understand the modifier codes
// and what kind of serving/portion data exists for the cleanest food type

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
  const filePath = path.join(BASE, filename);
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

async function run() {
  // Load FNDDS food IDs
  const fndds = await readCSV('survey_fndds_food.csv');
  const fnddsIds = new Set(fndds.map(r => r.fdc_id));
  console.log('FNDDS foods: ' + fnddsIds.size);

  // Load WWEIA categories
  const wweia = await readCSV('wweia_food_category.csv');
  const wweiaMap = {};
  for (const r of wweia) wweiaMap[r.wweia_food_category] = r.wweia_food_category_description;

  // Map fdc_id -> wweia category
  const fdcToWweia = {};
  for (const r of fndds) fdcToWweia[r.fdc_id] = r.wweia_category_code;

  // Load food names (streaming to avoid loading all 2M)
  const filePath = path.join(BASE, 'food.csv');
  const rl1 = readline.createInterface({ input: fs.createReadStream(filePath) });
  let headers1 = null;
  const fdcNames = {};
  for await (const line of rl1) {
    const fields = parseCSVLine(line);
    if (!headers1) { headers1 = fields; continue; }
    const fid = fields[0];
    if (fnddsIds.has(fid)) fdcNames[fid] = fields[2]; // description
  }

  // Load portions for FNDDS foods
  const allPortions = await readCSV('food_portion.csv');
  const fnddsPortions = allPortions.filter(p => fnddsIds.has(p.fdc_id));
  console.log('FNDDS portion entries: ' + fnddsPortions.length);

  // Show portion examples grouped by WWEIA category
  const portionsByCategory = {};
  for (const p of fnddsPortions) {
    const cat = wweiaMap[fdcToWweia[p.fdc_id]] || 'unknown';
    if (!portionsByCategory[cat]) portionsByCategory[cat] = [];
    portionsByCategory[cat].push({
      food: (fdcNames[p.fdc_id] || '?').substring(0, 35),
      desc: p.portion_description || '-',
      modifier: p.modifier || '-',
      amount: p.amount,
      gram: p.gram_weight,
      unit_id: p.measure_unit_id,
    });
  }

  console.log('\n=== FNDDS PORTIONS BY WWEIA CATEGORY ===\n');
  const sortedCats = Object.entries(portionsByCategory).sort((a, b) => b[1].length - a[1].length);
  for (const [cat, portions] of sortedCats.slice(0, 25)) {
    console.log(cat + ' (' + portions.length + ' portion entries):');
    for (const p of portions.slice(0, 4)) {
      console.log('  ' + p.food.padEnd(37) + 'desc="' + p.desc.substring(0, 30) + '" mod=' + p.modifier + ' g=' + p.gram);
    }
    console.log('');
  }

  // Analyze modifier codes
  console.log('=== FNDDS MODIFIER (portion_code) DISTRIBUTION ===');
  const modCounts = {};
  for (const p of fnddsPortions) {
    const m = p.modifier || 'empty';
    modCounts[m] = (modCounts[m] || 0) + 1;
  }
  for (const [m, c] of Object.entries(modCounts).sort((a, b) => b[1] - a[1]).slice(0, 30)) {
    console.log('  ' + m.padEnd(12) + c);
  }

  // Show Foundation food portions for comparison
  const ffData = await readCSV('foundation_food.csv');
  const ffIds = new Set(ffData.map(r => r.fdc_id));
  const ffPortions = allPortions.filter(p => ffIds.has(p.fdc_id));
  console.log('\n=== FOUNDATION FOOD PORTION EXAMPLES ===');
  console.log('Foundation portion entries: ' + ffPortions.length);
  for (const p of ffPortions.slice(0, 30)) {
    const name = (fdcNames[p.fdc_id] || '?').substring(0, 35);
    console.log('  ' + name.padEnd(37) +
      'amt=' + String(p.amount).padEnd(5) +
      'unit=' + String(p.measure_unit_id).padEnd(6) +
      'desc="' + (p.portion_description || '').substring(0, 30) + '" ' +
      'mod="' + (p.modifier || '').substring(0, 25) + '" ' +
      'g=' + p.gram_weight);
  }
}

run().catch(console.error);
