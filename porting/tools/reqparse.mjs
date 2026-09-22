// The trait parser of E:\Droppy\port\porting\tools\req_verify.mjs (lines 58-89), copied verbatim, run on a sample.
const text = `namespace Marrow.Core.Tests.Port;

public sealed class SampleTests
{
    [Fact]
    [Trait("Req", "PR-STORE-017")]
    public void First() { var x = 1; x++; } // a record Foo in a trailing comment

    [Fact]
    [Trait("Req", "PR-STORE-018")]
    public void Second() { }

    [Fact]
    [Trait("Req", "PR-STORE-020")]
    [InlineData(1,
        2)]
    public void Third(int a, int b) { }
}
`;
const ns = (text.match(/^\s*namespace\s+([\w.]+)\s*;/m) || text.match(/^\s*namespace\s+([\w.]+)/m) || [])[1] || '';
const lines = text.split(/\r?\n/);
let cls = null; let pending = []; let isTest = false; const out = [];
for (const line of lines) {
  const c = line.match(/\b(?:class|record)\s+(\w+)/);
  if (c && !/^\s*\/\//.test(line)) cls = c[1];
  if (/^\s*\[/.test(line)) {
    for (const m of line.matchAll(/Trait\(\s*"Req"\s*,\s*"([^"]+)"\s*\)/g)) pending.push(m[1]);
    if (/\b(Fact|Theory|AvaloniaFact|AvaloniaTheory)\b/.test(line)) isTest = true;
    continue;
  }
  const m = line.match(/^\s*public\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?)\s+(\w+)\s*\(/);
  if (m) {
    if (isTest && pending.length && cls) for (const id of pending) out.push(`${id} -> ${ns}.${cls}.${m[1]}`);
    pending = []; isTest = false;
  } else if (line.trim() && !/^\s*\/\//.test(line)) { pending = []; isTest = false; }
}
console.log(out.join('\n'));
