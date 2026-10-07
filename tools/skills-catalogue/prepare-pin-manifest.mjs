#!/usr/bin/env node
// OFFLINE preparation only. Resolved URLs/package choices come from the existing builder's catalogue.raw.json.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { prepareManifest } from './pin-manifest.mjs';
const here = path.dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
if (argv.length !== 4 || argv[0] !== '--resolved' || argv[2] !== '--out') {
  console.error('usage: node tools/skills-catalogue/prepare-pin-manifest.mjs --resolved <catalogue.raw.json> --out <new manifest.json>');
  process.exit(2);
}
const sources = JSON.parse(fs.readFileSync(path.join(here, 'sources.json'), 'utf8'));
const catalogue = JSON.parse(fs.readFileSync(argv[1], 'utf8'));
const manifest = prepareManifest(catalogue, sources);
fs.writeFileSync(argv[3], JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
console.log('Manifest prepared offline. Review null metadata selectors and mixed licences before the real-source run.');
