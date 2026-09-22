// Rewrites every non-ASCII character in the .cs files under a folder as a C# \UXXXXXXXX escape (valid in
// string and char literals). Needed because the agent file tool decodes \uXXXX escapes into raw characters on
// write. Comments in these files are ASCII by rule, so every non-ASCII character is inside a literal.
// Also repairs 'U0000XXXX' char literals that lost their backslash.
import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
const BS = String.fromCharCode(92);
function walk(dir, out = []) {
  for (const n of readdirSync(dir)) {
    if (n === 'bin' || n === 'obj') continue;
    const p = join(dir, n);
    if (statSync(p).isDirectory()) walk(p, out); else if (n.endsWith('.cs')) out.push(p);
  }
  return out;
}
let changed = 0;
for (const f of walk(process.argv[2])) {
  const text = readFileSync(f, 'utf8');
  let out = '';
  for (const ch of text) {
    const cp = ch.codePointAt(0);
    out += cp > 0x7E ? BS + 'U' + cp.toString(16).toUpperCase().padStart(8, '0') : ch;
  }
  out = out.replace(/'U([0-9A-F]{8})'/g, (_, hex) => "'" + BS + 'U' + hex + "'");
  if (out !== text) { writeFileSync(f, out); changed++; console.log('escaped', f); }
}
console.log('files changed: ' + changed);
