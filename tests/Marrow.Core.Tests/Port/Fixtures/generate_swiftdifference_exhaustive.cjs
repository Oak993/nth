// Generator for SwiftDifferenceExhaustive.txt.
//
// Provenance: `linear` (and the validity check `apply`) is required from ./swiftdifference_linear.cjs, the
// single COPY of diffsim.js's `linear` shared by every generator in this folder (see that file's header for
// full provenance: E:/Droppy/work/b3/evidence_6_1/diffsim.js, checked against
// E:/Droppy/work/b3/evidence_6_1/Diffing-swift-6.4.0-RELEASE.swift). This generator only adds the exhaustive
// loop and the compact-text writer below.
//
// What it does: for every pair (old, new) of sequences of length 0..4 over the alphabet {a, b, c} (121
// sequences each way, 14,641 pairs total, matching decisions_draft.md section E3's count), runs the
// linear-space Myers diff, confirms it is a valid edit script (via `apply`), and writes one line of the form
//
//   <old>|<new>|<removal offsets>|<insertion offsets>
//
// where <old>/<new> are the plain concatenated letters (single characters, so no separator is needed) and the
// offset lists are ascending, comma-separated integers (empty when there are none). SwiftDifferenceTests.cs
// reads this file and asserts the C# port produces the same removal and insertion offsets for every line.
//
// Regenerate with: node generate_swiftdifference_exhaustive.cjs
// (run from this directory; writes SwiftDifferenceExhaustive.txt beside this script).

'use strict';
const fs = require('fs');
const path = require('path');
const { linear, apply } = require('./swiftdifference_linear.cjs');

const alpha = ['a', 'b', 'c'];
function* seqs(maxLen) {
  for (let L = 0; L <= maxLen; L++) {
    const n = Math.pow(alpha.length, L);
    for (let i = 0; i < n; i++) {
      const s = []; let v = i;
      for (let j = 0; j < L; j++) { s.push(alpha[v % alpha.length]); v = Math.floor(v / alpha.length); }
      yield s;
    }
  }
}
const all = [...seqs(4)];

const lines = [];
let invalid = 0;
for (const a of all) {
  for (const b of all) {
    const changes = linear(a, b);
    if (!apply(a, b, changes)) invalid++;
    const removals = changes.filter(c => c[0] === 'r').map(c => c[1]).sort((x, y) => x - y);
    const insertions = changes.filter(c => c[0] === 'i').map(c => c[1]).sort((x, y) => x - y);
    lines.push(`${a.join('')}|${b.join('')}|${removals.join(',')}|${insertions.join(',')}`);
  }
}
if (invalid > 0) { console.error(`${invalid} pair(s) produced an invalid edit script`); process.exit(1); }

const outPath = path.join(__dirname, 'SwiftDifferenceExhaustive.txt');
fs.writeFileSync(outPath, lines.join('\n') + '\n', 'utf8');
console.log(`wrote ${lines.length} pairs (expected ${all.length * all.length}) to ${outPath}, all valid edit scripts`);
