#!/usr/bin/env node
// Builds nonoverlapping reading assignments from porting/source_inventory.json.
//
// Contract section 5 allows a subagent to read "explicitly assigned, nonoverlapping ranges".
// This partitioner guarantees the nonoverlap mechanically: every file appears in exactly one
// bundle, and no file is ever split across bundles (a file larger than the budget gets its
// own bundle, so every reader reads whole files end to end).

import { readFileSync, writeFileSync } from 'node:fs';

const INV = process.argv[2] || 'porting/source_inventory.json';
const OUT = process.argv[3] || 'porting/assignments.json';
const BUDGET = Number(process.argv[4] || 2600); // target lines per bundle

const inv = JSON.parse(readFileSync(INV, 'utf8'));

// Files that require full textual reading.
const READABLE_TEXT = new Set([
  'swift', 'h', 'm', 'metal', 'json', 'md', 'yml', 'toml', 'sh', 'py', 'js',
  'html', 'css', 'xml', 'txt', 'xcworkspacedata', 'pbxproj', 'resolved',
  'gitignore', 'base64', '(noext)',
]);
// Vector assets: text, but inspected as artwork rather than prose.
const VECTOR = new Set(['svg']);
// Raster assets: inspected with image tooling, not read as text.
const RASTER = new Set(['png', 'webp', 'tiff', 'ico']);

// Subsystem grouping. Order matters: first match wins. Keeps related files with one reader
// so that the reader can answer callers/callees questions instead of guessing.
const GROUPS = [
  ['core-models-hydra',      (p) => /^DroppyCode\/Core\/Models\/(Hydra|HydraCookbook|HydraDelegationStream)/.test(p)],
  ['core-models-mcp',        (p) => /^DroppyCode\/Core\/Models\/MCP/.test(p)],
  ['core-models',            (p) => p.startsWith('DroppyCode/Core/Models/')],
  ['core-support',           (p) => p.startsWith('DroppyCode/Core/Support/')],
  ['services-providers-claude', (p) => /^DroppyCode\/Services\/Providers\/(Claude|PlanLimits|UsageLimitSignal)/.test(p)],
  ['services-providers-codex',  (p) => /^DroppyCode\/Services\/Providers\/Codex/.test(p)],
  ['services-providers-acp',    (p) => /^DroppyCode\/Services\/Providers\/(ACP|Antigravity)/.test(p)],
  ['services-providers-copilot',(p) => /^DroppyCode\/Services\/Providers\/Copilot/.test(p)],
  ['services-providers-cmdcode',(p) => /^DroppyCode\/Services\/Providers\/CommandCode/.test(p)],
  ['services-providers-pi',     (p) => /^DroppyCode\/Services\/Providers\/Pi/.test(p)],
  ['services-providers-api',    (p) => /^DroppyCode\/Services\/Providers\/(OpenAICompatible|DeepSeek|Meta|Zai|TextGeneration|APIKeychain|ProviderCredits|NativeFileTools)/.test(p)],
  ['services-providers-registry',(p) => p.startsWith('DroppyCode/Services/Providers/')],
  ['services-mcp',           (p) => p.startsWith('DroppyCode/Services/MCP/')],
  ['services-git',           (p) => p.startsWith('DroppyCode/Services/Git/')],
  ['services-store',         (p) => p.startsWith('DroppyCode/Services/Store/')],
  ['ui-markdown',            (p) => p.startsWith('DroppyCode/UI/Markdown/')],
  ['ui-chrome',              (p) => p.startsWith('DroppyCode/UI/Chrome/')],
  ['ui-common',              (p) => p.startsWith('DroppyCode/UI/Common/')],
  ['ui-theme-sound',         (p) => /^DroppyCode\/UI\/(Theme|Sound)\//.test(p)],
  ['app-runtime',            (p) => p.startsWith('DroppyCode/App/Runtime/')],
  ['app-model-hydra',        (p) => /^DroppyCode\/App\/AppModel\+Hydra/.test(p) || /^DroppyCode\/App\/HydraCookbook/.test(p)],
  ['app-model',              (p) => /^DroppyCode\/App\/(AppModel|AppSettings|AppCommands|AppDelegate|DroppyCodeApp|LegacyMigration|AppUpdater|UpdateChecker)/.test(p)],
  ['app-window',             (p) => p.startsWith('DroppyCode/App/Window/')],
  ['app-captures',           (p) => p.startsWith('DroppyCode/App/Captures/')],
  ['app-views-chat-hydra',   (p) => /^DroppyCode\/App\/Views\/Chat\/Hydra/.test(p) || /^DroppyCode\/App\/Views\/Chat\/SubagentPanel/.test(p)],
  ['app-views-chat',         (p) => p.startsWith('DroppyCode/App/Views/Chat/')],
  ['app-views-composer',     (p) => p.startsWith('DroppyCode/App/Views/Composer/')],
  ['app-views-sidebar',      (p) => p.startsWith('DroppyCode/App/Views/Sidebar/')],
  ['app-views-settings',     (p) => p.startsWith('DroppyCode/App/Views/Settings/')],
  ['app-views-terminal',     (p) => p.startsWith('DroppyCode/App/Views/Terminal/')],
  ['app-views-diff',         (p) => p.startsWith('DroppyCode/App/Views/Diff/')],
  ['app-views-usage',        (p) => p.startsWith('DroppyCode/App/Views/Usage/')],
  ['app-views-tour',         (p) => p.startsWith('DroppyCode/App/Views/Tour/')],
  ['app-views-palette',      (p) => p.startsWith('DroppyCode/App/Views/Palette/')],
  ['app-views-root',         (p) => p.startsWith('DroppyCode/App/Views/')],
  ['app-other',              (p) => p.startsWith('DroppyCode/App/')],
  ['resources-appicon',      (p) => p.startsWith('DroppyCode/Resources/AppIcon.icon/')],
  ['resources-assets',       (p) => p.startsWith('DroppyCode/Resources/')],
  ['build-config',           (p) => p === 'project.yml' || p.startsWith('DroppyCode.xcodeproj/')],
  ['scripts',                (p) => p.startsWith('scripts/')],
  ['release-tooling',        (p) => p.startsWith('release/')],
  ['website',                (p) => p.startsWith('website/')],
  ['docs',                   (p) => p.startsWith('docs/')],
  ['repo-root',              () => true],
];

function groupOf(p) {
  for (const [name, test] of GROUPS) if (test(p)) return name;
  return 'repo-root';
}

const textItems = [];
const vectorItems = [];
const rasterItems = [];
for (const it of inv.items) {
  if (RASTER.has(it.ext)) rasterItems.push(it);
  else if (VECTOR.has(it.ext)) vectorItems.push(it);
  else if (READABLE_TEXT.has(it.ext)) textItems.push(it);
  else rasterItems.push(it); // unknown kinds get asset inspection, never silent skipping
}

// Bucket text files by group, preserving path order inside a group.
const byGroup = new Map();
for (const it of textItems) {
  const g = groupOf(it.path);
  if (!byGroup.has(g)) byGroup.set(g, []);
  byGroup.get(g).push(it);
}

const bundles = [];
for (const [group, items] of byGroup) {
  let cur = [];
  let curLines = 0;
  const flush = () => {
    if (!cur.length) return;
    bundles.push({ group, files: cur, lines: curLines });
    cur = [];
    curLines = 0;
  };
  for (const it of items) {
    const n = it.line_count || 0;
    // A file at or above the budget is read alone, whole.
    if (n >= BUDGET) {
      flush();
      bundles.push({ group, files: [it], lines: n });
      continue;
    }
    if (curLines + n > BUDGET) flush();
    cur.push(it);
    curLines += n;
  }
  flush();
}

// Vector assets: group into inspection bundles of up to 25.
for (let i = 0; i < vectorItems.length; i += 25) {
  const chunk = vectorItems.slice(i, i + 25);
  bundles.push({
    group: 'assets-vector',
    files: chunk,
    lines: chunk.reduce((n, f) => n + (f.line_count || 0), 0),
  });
}
// Raster/binary assets: one inspection bundle per 30.
for (let i = 0; i < rasterItems.length; i += 30) {
  const chunk = rasterItems.slice(i, i + 30);
  bundles.push({ group: 'assets-binary', files: chunk, lines: 0 });
}

bundles.forEach((b, i) => {
  b.id = `R${String(i + 1).padStart(3, '0')}`;
  b.files = b.files.map((f) => ({
    path: f.path,
    line_count: f.line_count,
    size_bytes: f.size_bytes,
    sha256: f.sha256,
    ext: f.ext,
    kind: f.kind,
  }));
});

// Mechanical nonoverlap + total-coverage proof.
const seen = new Set();
for (const b of bundles) {
  for (const f of b.files) {
    if (seen.has(f.path)) throw new Error(`OVERLAP: ${f.path} appears in more than one bundle`);
    seen.add(f.path);
  }
}
const missing = inv.items.filter((i) => !seen.has(i.path)).map((i) => i.path);
if (missing.length) throw new Error(`UNASSIGNED FILES: ${missing.join(', ')}`);

const doc = {
  schema: 'droppy-windows-port/assignments/1',
  baseline: inv.baseline,
  budget_lines: BUDGET,
  counts: {
    bundles: bundles.length,
    files: seen.size,
    inventory_files: inv.items.length,
    text_bundles: bundles.filter((b) => !b.group.startsWith('assets-')).length,
    asset_bundles: bundles.filter((b) => b.group.startsWith('assets-')).length,
    total_lines: bundles.reduce((n, b) => n + b.lines, 0),
  },
  bundles,
};
writeFileSync(OUT, JSON.stringify(doc, null, 2) + '\n');
console.log(`wrote ${OUT}`);
console.log(JSON.stringify(doc.counts, null, 2));
console.log('\nbundles by group:');
const g = {};
for (const b of bundles) g[b.group] = (g[b.group] || 0) + 1;
for (const [k, v] of Object.entries(g).sort()) console.log(`  ${k}: ${v}`);
console.log('\nlargest bundles:');
for (const b of [...bundles].sort((a, z) => z.lines - a.lines).slice(0, 12)) {
  console.log(`  ${b.id} ${b.group} ${b.lines} lines, ${b.files.length} files`);
}
