// Generator for SwiftDifferenceExhaustive.txt.
//
// Provenance: this file is a COPY of the `linear` function from
// E:/Droppy/work/b3/evidence_6_1/diffsim.js (an independent JavaScript transliteration of swift-6.3-RELEASE /
// swift-6.4.0-RELEASE stdlib/public/core/Diffing.swift's `_linearSpaceMyers`, read and checked line-for-line
// against the pinned Swift source saved at
// E:/Droppy/work/b3/evidence_6_1/Diffing-swift-6.4.0-RELEASE.swift as part of implementing
// src/Marrow.Core/Port/SwiftDifference.cs; no disagreement was found between diffsim.js's `linear` and the
// Swift source). It is copied rather than required from the evidence folder per the SwiftDifference work
// packet ("extend a COPY of it ... not the evidence folder"), and extended below with an exhaustive loop and
// a compact-text writer.
//
// What it does: for every pair (old, new) of sequences of length 0..4 over the alphabet {a, b, c} (121
// sequences each way, 14,641 pairs total, matching decisions_draft.md section E3's count), runs the
// linear-space Myers diff and writes one line of the form
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

// ---- begin copy of diffsim.js's `linear`, verbatim ----
function linear(a, b) {
  const shrunk = (r) => { r = { ...r }; while (r.left < r.right && r.top < r.bottom && a[r.left] === b[r.top]) { r.left++; r.top++; } while (r.right > r.left && r.bottom > r.top && a[r.right - 1] === b[r.bottom - 1]) { r.right--; r.bottom--; } return r; };
  const W = r => r.right - r.left, H = r => r.bottom - r.top, S = r => W(r) + H(r), D = r => W(r) - H(r), odd = r => (D(r) % 2 !== 0), MX = r => Math.trunc((S(r) + 1) / 2);
  let box = shrunk({ left: 0, top: 0, right: a.length, bottom: b.length }); const size = S(box);
  const range = size * 2 + 1; const buf = new Array(range * 2).fill(0); const fo = size, bo = range + size;
  const F = { get: i => buf[fo + i], set: (i, v) => { buf[fo + i] = v; } }, B = { get: i => buf[bo + i], set: (i, v) => { buf[bo + i] = v; } };
  function fwd(box, depth) {
    for (let k = depth; k >= -depth; k -= 2) {
      let nr, nb, nl, nt, ins, ei;
      if (k === -depth || (k !== depth && F.get(k - 1) < F.get(k + 1))) { nr = F.get(k + 1); nl = nr; nb = box.top + (nr - box.left) - k; ins = true; ei = nb - 1; }
      else { nl = F.get(k - 1); nr = nl + 1; nb = box.top + (nr - box.left) - k; ins = false; ei = nl; }
      nt = (depth === 0 || nr !== nl) ? nb : nb - 1;
      while (nr < box.right && nb < box.bottom && a[nr] === b[nb]) { nr++; nb++; }
      F.set(k, nr); if (!odd(box)) continue; const c = k - D(box);
      if (c >= -(depth - 1) && c <= depth - 1 && nb >= B.get(c)) return [[ins ? 'i' : 'r', ei], { left: nl, top: nt, right: nr, bottom: nb }];
    }
    return null;
  }
  function bwd(box, depth) {
    for (let c = depth; c >= -depth; c -= 2) {
      const k = c + D(box); let nl, nt, nr, nb, ins, ei;
      if (c === -depth || (c !== depth && B.get(c - 1) > B.get(c + 1))) { nt = B.get(c + 1); nb = nt; nl = box.left + (nt - box.top) + k; ins = false; ei = nl; }
      else { nb = B.get(c - 1); nt = nb - 1; nl = box.left + (nt - box.top) + k; ins = true; ei = nt; }
      nr = (depth === 0 || nt !== nb) ? nl : nl + 1;
      while (nl > box.left && nt > box.top && a[nl - 1] === b[nt - 1]) { nl--; nt--; }
      B.set(c, nt); if (odd(box)) continue;
      if (k >= -depth && k <= depth && nl <= F.get(k)) return [[ins ? 'i' : 'r', ei], { left: nl, top: nt, right: nr, bottom: nb }];
    }
    return null;
  }
  function middle(box) {
    if (S(box) <= 1) { if (S(box) === 0) return [null, null]; if (W(box) === 1) return [['r', box.left], null]; return [['i', box.top], null]; }
    F.set(1, box.left); B.set(1, box.bottom);
    for (let d = 0; d <= MX(box); d++) { if (odd(box)) { const r = fwd(box, d); if (r) return r; bwd(box, d); } else { fwd(box, d); const r = bwd(box, d); if (r) return r; } }
    throw new Error('unreachable');
  }
  const res = []; const stack = []; let cur = box;
  while (true) {
    const [e, sb] = middle(cur); if (e) res.push(e); if (!sb) { if (!stack.length) return res; cur = stack.pop(); continue; }
    const head = shrunk({ left: cur.left, top: cur.top, right: sb.left, bottom: sb.top }); const tail = shrunk({ left: sb.right, top: sb.bottom, right: cur.right, bottom: cur.bottom }); cur = head; stack.push(tail);
  }
}
// ---- end copy of diffsim.js's `linear` ----

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
for (const a of all) {
  for (const b of all) {
    const changes = linear(a, b);
    const removals = changes.filter(c => c[0] === 'r').map(c => c[1]).sort((x, y) => x - y);
    const insertions = changes.filter(c => c[0] === 'i').map(c => c[1]).sort((x, y) => x - y);
    lines.push(`${a.join('')}|${b.join('')}|${removals.join(',')}|${insertions.join(',')}`);
  }
}

const outPath = path.join(__dirname, 'SwiftDifferenceExhaustive.txt');
fs.writeFileSync(outPath, lines.join('\n') + '\n', 'utf8');
console.log(`wrote ${lines.length} pairs (expected ${all.length * all.length}) to ${outPath}`);
