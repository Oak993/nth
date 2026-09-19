#!/usr/bin/env node
// Generates porting/source_inventory.json from the pinned reference checkout.
//
// This script records MECHANICAL FACTS ONLY: path, git blob id, sha256 of the
// working-tree bytes, size, line count, and a classification derived from the
// build manifest. It deliberately sets read_status to "UNREAD" for every entry.
//
// Per PORT_CONTRACT section 5: "A script opening files, calculating hashes,
// dumping source into an unread file, or automatically setting read=true does
// not mean you read them." Read status is only ever advanced by
// porting/tools/ledger_apply.mjs, from evidence-bearing reader reports.

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';

const REF = process.argv[2];
const OUT = process.argv[3];
if (!REF || !OUT) {
  console.error('usage: gen_inventory.mjs <reference-checkout> <out.json>');
  process.exit(2);
}

const git = (...args) =>
  execFileSync('git', ['-C', REF, ...args], { encoding: 'utf8', maxBuffer: 1 << 28 });

const headSha = git('rev-parse', 'HEAD').trim();
const treeSha = git('rev-parse', 'HEAD^{tree}').trim();
const status = git('status', '--porcelain').trim();
if (status) {
  console.error('REFERENCE IS DIRTY - refusing to inventory a modified reference:');
  console.error(status);
  process.exit(3);
}

// git ls-files -s gives: <mode> <blob> <stage>\t<path>
const entries = git('ls-files', '-s')
  .split('\n')
  .filter(Boolean)
  .map((line) => {
    const [meta, path] = line.split('\t');
    const [mode, blob] = meta.split(/\s+/);
    return { path, mode, blob };
  });

// Build-inclusion rules, read from project.yml at the pinned SHA.
// project.yml target DroppyCode sources:
//   - path: DroppyCode      type: syncedFolder   (everything under DroppyCode/)
//   - LICENSE, THIRD_PARTY_NOTICES.md, TRADEMARK.md  buildPhase: resources
const APP_BUNDLE_RESOURCES = new Set([
  'LICENSE',
  'THIRD_PARTY_NOTICES.md',
  'TRADEMARK.md',
]);

const TEXT_EXT = new Set([
  'swift', 'h', 'm', 'json', 'md', 'yml', 'yaml', 'toml', 'sh', 'py', 'js',
  'mjs', 'html', 'css', 'svg', 'xml', 'txt', 'metal', 'base64',
  'xcworkspacedata', 'pbxproj', 'resolved', 'gitignore',
]);

const BINARY_EXT = new Set(['png', 'webp', 'tiff', 'ico', 'icns', 'jpg', 'jpeg', 'pdf']);

function extOf(p) {
  const base = p.split('/').pop();
  if (!base.includes('.')) return '(noext)';
  return base.split('.').pop().toLowerCase();
}

function classify(p) {
  // Returns { classification, build_inclusion, inclusion_evidence }
  if (p.startsWith('DroppyCode/')) {
    return {
      classification: 'first_party_app',
      build_inclusion: 'in_app_target',
      inclusion_evidence: 'project.yml targets.DroppyCode.sources[0] path=DroppyCode type=syncedFolder',
    };
  }
  if (APP_BUNDLE_RESOURCES.has(p)) {
    return {
      classification: 'first_party_legal',
      build_inclusion: 'in_app_target_resources',
      inclusion_evidence: 'project.yml targets.DroppyCode.sources buildPhase: resources',
    };
  }
  if (p.startsWith('DroppyCode.xcodeproj/')) {
    return {
      classification: 'first_party_build_config',
      build_inclusion: 'build_system',
      inclusion_evidence: 'Xcode project generated from project.yml (XcodeGen); defines the app target',
    };
  }
  if (p === 'project.yml') {
    return {
      classification: 'first_party_build_config',
      build_inclusion: 'build_system',
      inclusion_evidence: 'XcodeGen manifest; authoritative source of target/version/settings',
    };
  }
  if (p.startsWith('scripts/')) {
    return {
      classification: 'first_party_tooling',
      build_inclusion: 'not_in_app_target',
      inclusion_evidence: 'not listed in project.yml sources; developer/release tooling',
    };
  }
  if (p.startsWith('release/')) {
    return {
      classification: 'first_party_release_tooling',
      build_inclusion: 'not_in_app_target',
      inclusion_evidence: 'not listed in project.yml sources; DMG packaging assets/tooling',
    };
  }
  if (p.startsWith('website/')) {
    return {
      classification: 'first_party_website',
      build_inclusion: 'not_in_app_target',
      inclusion_evidence: 'netlify.toml site; may supply runtime content (changelog/update feed) - must be checked',
    };
  }
  if (p.startsWith('docs/')) {
    return {
      classification: 'first_party_docs',
      build_inclusion: 'not_in_app_target',
      inclusion_evidence: 'repository documentation',
    };
  }
  if (p.startsWith('.commandcode/')) {
    return {
      classification: 'first_party_agent_config',
      build_inclusion: 'not_in_app_target',
      inclusion_evidence: 'hidden tracked directory; agent tooling config',
    };
  }
  return {
    classification: 'first_party_repo_root',
    build_inclusion: 'not_in_app_target',
    inclusion_evidence: 'repository root file',
  };
}

const items = [];
for (const e of entries) {
  const abs = join(REF, e.path);
  const buf = readFileSync(abs);
  const sha256 = createHash('sha256').update(buf).digest('hex');
  const ext = extOf(e.path);
  // Extension is a hint; content decides. A file with no NUL byte in its first 8 KiB that
  // decodes as UTF-8 is text, whatever it is called. Fixed 2026-09-19 after the
  // extensionless LICENSE (37 KB of text) was classified 'unknown' with a null line count.
  const probe = buf.subarray(0, 8192);
  const hasNul = probe.includes(0);
  const isBinary = BINARY_EXT.has(ext) || hasNul;
  const isText = !isBinary && (TEXT_EXT.has(ext) || !hasNul);
  let lines = null;
  let crlf = false;
  if (isText) {
    const s = buf.toString('utf8');
    lines = s.length === 0 ? 0 : s.split('\n').length - (s.endsWith('\n') ? 1 : 0);
    crlf = s.includes('\r\n');
  }
  const cls = classify(e.path);
  items.push({
    path: e.path,
    git_mode: e.mode,
    git_blob: e.blob,
    sha256,
    size_bytes: buf.length,
    ext,
    kind: isBinary ? 'binary' : isText ? 'text' : 'unknown',
    line_count: lines,
    has_crlf: crlf,
    ...cls,
    read_status: 'UNREAD',
    ranges_read: [],
    reader: null,
    findings_ref: null,
    asset_inspection: null,
    exclusion_reason: null,
  });
}

items.sort((a, b) => (a.path < b.path ? -1 : 1));

const doc = {
  schema: 'droppy-windows-port/source_inventory/1',
  generated_by: 'porting/tools/gen_inventory.mjs',
  note:
    'Mechanical facts only. read_status is UNREAD for every entry at generation time ' +
    'and may only be advanced by porting/tools/ledger_apply.mjs from evidence-bearing reader reports.',
  baseline: {
    repository: 'https://gitlab.com/droppyformac1/droppy-code',
    default_branch: 'main',
    commit_sha: headSha,
    tree_sha: treeSha,
    reference_path: REF,
  },
  counts: {
    total_files: items.length,
    by_classification: items.reduce((acc, i) => {
      acc[i.classification] = (acc[i.classification] || 0) + 1;
      return acc;
    }, {}),
    by_ext: items.reduce((acc, i) => {
      acc[i.ext] = (acc[i.ext] || 0) + 1;
      return acc;
    }, {}),
    total_text_lines: items.reduce((n, i) => n + (i.line_count || 0), 0),
  },
  items,
};

mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, JSON.stringify(doc, null, 2) + '\n');
console.log(`wrote ${OUT}`);
console.log(`commit ${headSha}`);
console.log(`tree   ${treeSha}`);
console.log(`files  ${items.length}`);
console.log(`lines  ${doc.counts.total_text_lines}`);
console.log(JSON.stringify(doc.counts.by_classification, null, 2));
