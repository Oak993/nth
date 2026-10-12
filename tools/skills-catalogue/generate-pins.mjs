#!/usr/bin/env node
// MANUAL REAL-SOURCE RUN ONLY. Do not run this in cloud verification/tests.
import fs from 'node:fs';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { generatePins, requestOfficial } from './pin-generator.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
// ED-NEW-dl-13: --items A3,T4 runs only those items (the manifest then covers exactly them); the other items keep the pins of the run they came from.
const partial = argv.length === 7 && argv[4] === '--items' ? argv[5].split(',').map(s => s.trim()) : undefined;
if (!(argv.length === 5 || partial) || argv[0] !== '--manifest' || argv[2] !== '--out' || argv[argv.length - 1] !== '--run-real-source-fetch') {
  console.error('usage: node tools/skills-catalogue/generate-pins.mjs --manifest <reviewed official-source manifest.json> --out <new folder> [--items A3,T4] --run-real-source-fetch');
  process.exit(2);
}
const manifest = JSON.parse(fs.readFileSync(argv[1], 'utf8'));
const sources = JSON.parse(fs.readFileSync(path.join(here, 'sources.json'), 'utf8'));
const out = path.resolve(argv[3]);
if (fs.existsSync(out)) throw new Error('Output must be a new folder; nothing is overwritten');
const staging = out + '.tmp-' + randomUUID();
fs.mkdirSync(staging, { recursive: true });
try {
  const result = await generatePins(manifest, {
    sources, request: requestOfficial, only: partial,
    saveMetadata: async (url, bytes) => {
      const folder = path.join(staging, "metadata");
      fs.mkdirSync(folder, { recursive: true });
      const sha = createHash("sha256").update(bytes).digest("hex");
      fs.writeFileSync(path.join(folder, sha + ".bin"), bytes);
    },
    saveChunk: async (id, name, bytes) => {
      const file = path.join(staging, 'artifacts', id, ...name.split('/'));
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.appendFileSync(file, bytes);
    }
  });
  fs.writeFileSync(path.join(staging, 'pins.json'), JSON.stringify(result, null, 2) + '\n');
  fs.writeFileSync(path.join(staging, 'source-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  fs.renameSync(staging, out);
  console.log(`Computed real-source pins for ${result.items.length} items in ${out}. Review before copying any facts into the catalogue.`);
} finally { if (fs.existsSync(staging)) fs.rmSync(staging, { recursive: true, force: true }); }
