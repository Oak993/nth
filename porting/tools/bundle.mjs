#!/usr/bin/env node
// Prints the exact, nonoverlapping file list for one reading bundle.
// Readers call this to learn their assignment instead of being handed a list
// that could drift from porting/assignments.json.
import { readFileSync } from 'node:fs';
const id = process.argv[2];
const doc = JSON.parse(readFileSync('porting/assignments.json', 'utf8'));
const b = doc.bundles.find((x) => x.id === id);
if (!b) { console.error(`no bundle ${id}`); process.exit(2); }
console.log(`BUNDLE ${b.id}  group=${b.group}  files=${b.files.length}  lines=${b.lines}`);
console.log(`REFERENCE ROOT: E:/Droppy/reference/droppy-code`);
console.log(`PINNED COMMIT:  ${doc.baseline.commit_sha}`);
console.log('');
for (const f of b.files) {
  console.log(`${String(f.line_count ?? 'binary').padStart(6)}  ${f.path}`);
}
