// Shared module: the single copy of `linear` (and the edit-script validity check `apply`) that every
// SwiftDifference fixture generator in this folder requires, instead of each hand-copying its own.
//
// Provenance: `linear` is a COPY of the function of the same name from
// E:/Droppy/work/b3/evidence_6_1/diffsim.js (an independent JavaScript transliteration of swift-6.3-RELEASE /
// swift-6.4.0-RELEASE stdlib/public/core/Diffing.swift's `_linearSpaceMyers`), read and checked line-for-line
// against the pinned Swift source saved at E:/Droppy/work/b3/evidence_6_1/Diffing-swift-6.4.0-RELEASE.swift as
// part of implementing src/Marrow.Core/Port/SwiftDifference.cs; no disagreement was found. `apply` is the same
// file's edit-script validity check: it removes the offsets `linear` marks as removed from `a`, removes the
// offsets `linear` marks as inserted from `b`, and reports whether what is left matches element for element.
// Both are copied here rather than required from the evidence folder, per the work packet's "extend a COPY of
// it ... not the evidence folder", and centralized in one file so generate_swiftdifference_exhaustive.cjs and
// generate_swiftdifference_random.cjs cannot drift from each other by a second, independent transcription.

'use strict';

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

// Validity check (diffsim.js's `apply`): true when removing the 'r' offsets from `a` and the 'i' offsets from
// `b` leaves the same sequence on both sides, i.e. `changes` is a genuine edit script from `a` to `b`.
function apply(a, b, changes) {
  const rem = new Set(changes.filter(c => c[0] === 'r').map(c => c[1]));
  const ins = new Set(changes.filter(c => c[0] === 'i').map(c => c[1]));
  const keptA = a.filter((_, i) => !rem.has(i));
  const keptB = b.filter((_, i) => !ins.has(i));
  return keptA.join('\u0000') === keptB.join('\u0000');
}

module.exports = { linear, apply };
