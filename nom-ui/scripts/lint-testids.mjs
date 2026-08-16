#!/usr/bin/env node
/*
 * lint-testids — fail the build when an interactive element in an Angular
 * template has no `data-testid`.
 *
 * Why: CLAUDE.md has required data-testid on every interactive/testable element
 * (kebab-case, `{page}-{thing}` convention) since 2026-03-12. Measured on
 * 2026-08-16: 22% coverage overall, and 18% on templates written AFTER the
 * rule — a rule that lives only in prose is not a rule. This makes it one.
 *
 * What counts as interactive: <button>, <a> with routerLink/href, <input>
 * (except type=hidden), <select>, <textarea>, <mat-select>, <mat-checkbox>,
 * <mat-slide-toggle>, <mat-radio-button>, <mat-chip-option>, and any element
 * carrying a (click) binding. Angular control-flow blocks are plain text to
 * this scanner, so nothing is missed by being inside @if/@for.
 *
 * Ratchet contract (per file, scripts/testid-baseline.json):
 *   • a template NOT in the baseline must be 100% covered — new work follows
 *     the rule;
 *   • a baselined template may not have MORE uncovered elements than recorded;
 *   • a template that improved (fewer uncovered) or was deleted FAILS with
 *     RATCHET DOWN / STALE ENTRY until you rerun with
 *     NOM_TESTID_UPDATE_BASELINE=1 and commit the rewritten baseline in the
 *     same commit. It only tightens — never hand-edit a number upward.
 *
 * Exit code: 0 on pass, 1 on any failure.
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.join(ROOT, 'src', 'app');
const BASELINE_PATH = path.join(ROOT, 'scripts', 'testid-baseline.json');
const UPDATE = process.env.NOM_TESTID_UPDATE_BASELINE === '1' || process.env.NOM_TESTID_UPDATE_BASELINE === 'true';

function* walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules') continue;
      yield* walk(full);
    } else if (entry.name.endsWith('.html')) {
      yield full;
    }
  }
}

const rel = (p) => path.relative(ROOT, p).split(path.sep).join('/');

const INTERACTIVE_TAGS = new Set([
  'button', 'input', 'select', 'textarea',
  'mat-select', 'mat-checkbox', 'mat-slide-toggle', 'mat-radio-button', 'mat-chip-option',
]);

/** Returns { total, uncovered, samples } for one template. */
function scan(html) {
  const src = html.replace(/<!--[\s\S]*?-->/g, '');
  let total = 0;
  let uncovered = 0;
  const samples = [];
  // Opening tags only (not closing, not self-closing-void distinctions matter here).
  for (const m of src.matchAll(/<([a-zA-Z][\w-]*)\b([^>]*)>/g)) {
    const tag = m[1].toLowerCase();
    const attrs = m[2];
    if (tag.startsWith('/')) continue;

    const isTagInteractive = INTERACTIVE_TAGS.has(tag);
    const isAnchor = tag === 'a' && /\b(routerLink|href)\b|\[routerLink\]/.test(attrs);
    const hasClick = /\(click\)\s*=/.test(attrs);
    if (!isTagInteractive && !isAnchor && !hasClick) continue;
    if (tag === 'input' && /type=["']hidden["']/.test(attrs)) continue;

    total++;
    if (!/\bdata-testid\b|\[attr\.data-testid\]/.test(attrs)) {
      uncovered++;
      if (samples.length < 3) samples.push(m[0].replace(/\s+/g, ' ').slice(0, 80));
    }
  }
  return { total, uncovered, samples };
}

const current = new Map();
const samplesByFile = new Map();
let grandTotal = 0;
let grandUncovered = 0;
for (const f of walk(SRC)) {
  const { total, uncovered, samples } = scan(fs.readFileSync(f, 'utf8'));
  grandTotal += total;
  grandUncovered += uncovered;
  if (uncovered) {
    current.set(rel(f), uncovered);
    samplesByFile.set(rel(f), samples);
  }
}

const baseline = fs.existsSync(BASELINE_PATH) ? JSON.parse(fs.readFileSync(BASELINE_PATH, 'utf8')) : {};

if (UPDATE) {
  const next = Object.fromEntries([...current].sort(([a], [b]) => a.localeCompare(b)));
  fs.writeFileSync(BASELINE_PATH, JSON.stringify(next, null, 2) + '\n');
  console.log(`lint-testids: baseline rewritten → ${current.size} files with ${grandUncovered} uncovered elements (of ${grandTotal} interactive).`);
  process.exit(0);
}

const failures = [];
for (const [file, n] of current) {
  if (!(file in baseline)) {
    failures.push(`NEW FILE / UNCOVERED  ${file}: ${n} interactive element(s) without data-testid — new templates must be 100% covered.\n      e.g. ${samplesByFile.get(file).join('\n      e.g. ')}`);
  } else if (n > baseline[file]) {
    failures.push(`DEBT GREW  ${file}: ${n} uncovered > baseline ${baseline[file]}.\n      e.g. ${samplesByFile.get(file).join('\n      e.g. ')}`);
  } else if (n < baseline[file]) {
    failures.push(`RATCHET DOWN  ${file}: ${n} < baseline ${baseline[file]} — nice; rerun with NOM_TESTID_UPDATE_BASELINE=1 and commit scripts/testid-baseline.json.`);
  }
}
for (const file of Object.keys(baseline)) {
  if (!current.has(file)) failures.push(`STALE ENTRY  ${file}: baselined but now fully covered/gone — rerun with NOM_TESTID_UPDATE_BASELINE=1 and commit scripts/testid-baseline.json.`);
}

if (failures.length) {
  console.error(`\nlint-testids: ${failures.length} problem(s)\n`);
  for (const f of failures) console.error(`  ${f}`);
  console.error(`\nConvention: kebab-case, {page}-{thing} — see the "E2E Test IDs" table in CLAUDE.md.`);
  process.exit(1);
}

const covered = grandTotal - grandUncovered;
console.log(`OK: lint-testids — ratchet holds. Coverage ${covered}/${grandTotal} (${Math.round((covered * 100) / grandTotal)}%); ${current.size} template(s) still carrying debt.`);
process.exit(0);
