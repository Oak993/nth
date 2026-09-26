// Generator for SwiftDifferenceRandom.txt.
//
// Requires `linear` and `apply` from ./swiftdifference_linear.cjs (the single COPY of diffsim.js's `linear`
// shared by every generator in this folder; see that file's header for full provenance). This generator adds
// a SEEDED pseudo-random set on top of the small exhaustive set in generate_swiftdifference_exhaustive.cjs,
// because lengths 0..4 barely exercise `middleSnake`'s recursion, the odd/even-delta branches at depth, or the
// prefix/suffix trimming on long runs.
//
// Determinism: uses mulberry32, a small public-domain PRNG (Tommy Ettinger), seeded with the fixed constant
// SEED below so the fixture is exactly reproducible without an external dependency. Changing SEED, the pair
// counts, the length ranges or the generation order changes every pair after the first divergence, so this
// file is only ever run to regenerate SwiftDifferenceRandom.txt from scratch, never edited by hand.
//
// What it generates (3,050 pairs total):
//   - 3,000 pairs of independently random lengths 0..80 (inclusive), 1,000 pairs each over three alphabets of
//     size 2 ("ab"), 3 ("abc") and 8 ("abcdefgh"), to exercise deep recursion and both odd- and even-delta
//     boxes at many depths.
//   - 50 pairs built as a shared random prefix (20..40 elements) and a shared random suffix (20..40
//     elements), with an independent random middle (0..20 elements) on each side, rotating through the three
//     alphabets above. These exercise the common-prefix/suffix trimming in EditGraphRect.Shrunk on long runs,
//     since the trim has to walk 20+ elements before the real (short) diff even starts.
//
// Every pair's `linear` result is checked with `apply` before being written (a self-check, not the C# test):
// if a generated pair were ever an invalid edit script it would mean this generator (or its copy of `linear`)
// has drifted from the algorithm, and the script aborts rather than writing a bad fixture.
//
// Line format, one pair per line (same as SwiftDifferenceExhaustive.txt):
//   <old>|<new>|<removal offsets>|<insertion offsets>
// <old>/<new> are the plain concatenated alphabet letters (each element is one character, so no separator is
// needed); offset lists are ascending, comma-separated integers (empty when there are none). The file's first
// line is a `#`-prefixed comment recording the seed and counts; SwiftDifferenceTests.cs skips it.
//
// Regenerate with: node generate_swiftdifference_random.cjs
// (run from this directory; writes SwiftDifferenceRandom.txt beside this script).

'use strict';
const fs = require('fs');
const path = require('path');
const { linear, apply } = require('./swiftdifference_linear.cjs');

const SEED = 1234567891;

// mulberry32: https://gist.github.com/tommyettinger/46a874533244883189143505d203312 (public domain).
function mulberry32(seed) {
  let a = seed >>> 0;
  return function next() {
    a |= 0;
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rng = mulberry32(SEED);
const randInt = (maxExclusive) => Math.floor(rng() * maxExclusive); // [0, maxExclusive)
const randLen = (minInclusive, maxInclusive) => minInclusive + randInt(maxInclusive - minInclusive + 1);
const randSeq = (length, alphabet) => Array.from({ length }, () => alphabet[randInt(alphabet.length)]);

const alphabets = { 2: 'ab'.split(''), 3: 'abc'.split(''), 8: 'abcdefgh'.split('') };

const pairs = [];

// Group A: 1,000 pairs per alphabet size, independent random lengths 0..80 on each side.
for (const size of [2, 3, 8]) {
  const alphabet = alphabets[size];
  for (let i = 0; i < 1000; i++) {
    const a = randSeq(randLen(0, 80), alphabet);
    const b = randSeq(randLen(0, 80), alphabet);
    pairs.push([a, b]);
  }
}

// Group B: 50 pairs with a long shared prefix and suffix (20..40 elements each) and a short random middle
// (0..20 elements) on each side, so the diff itself is small but sits behind long runs that must be trimmed.
const rotatingAlphabets = [alphabets[2], alphabets[3], alphabets[8]];
for (let i = 0; i < 50; i++) {
  const alphabet = rotatingAlphabets[i % rotatingAlphabets.length];
  const prefix = randSeq(randLen(20, 40), alphabet);
  const suffix = randSeq(randLen(20, 40), alphabet);
  const oldMiddle = randSeq(randLen(0, 20), alphabet);
  const newMiddle = randSeq(randLen(0, 20), alphabet);
  const a = [...prefix, ...oldMiddle, ...suffix];
  const b = [...prefix, ...newMiddle, ...suffix];
  pairs.push([a, b]);
}

const lines = [];
let invalid = 0;
for (const [a, b] of pairs) {
  const changes = linear(a, b);
  if (!apply(a, b, changes)) invalid++;
  const removals = changes.filter(c => c[0] === 'r').map(c => c[1]).sort((x, y) => x - y);
  const insertions = changes.filter(c => c[0] === 'i').map(c => c[1]).sort((x, y) => x - y);
  lines.push(`${a.join('')}|${b.join('')}|${removals.join(',')}|${insertions.join(',')}`);
}
if (invalid > 0) { console.error(`${invalid} pair(s) produced an invalid edit script`); process.exit(1); }

const header = `# seed=${SEED} pairs=${pairs.length} (1000 each over alphabets ab/abc/abcdefgh, lengths 0..80; ` +
  `50 shared-prefix-suffix 20..40 with random middle 0..20)`;
const outPath = path.join(__dirname, 'SwiftDifferenceRandom.txt');
fs.writeFileSync(outPath, header + '\n' + lines.join('\n') + '\n', 'utf8');
console.log(`wrote ${lines.length} pairs (seed ${SEED}) to ${outPath}, all valid edit scripts`);
