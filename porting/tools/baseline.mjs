// The pinned upstream baseline, read from porting/baseline.json.
//
// Every tool that names the reference checkout, the pinned commit or its tree takes them from here,
// so a rebaseline authorized by the owner (porting/tools/rebaseline.py, OD-0006) changes one record
// instead of constants scattered across the tools. History keeps every earlier baseline so their
// checkouts can still be checked for tampering.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const doc = JSON.parse(readFileSync(join(process.cwd(), 'porting', 'baseline.json'), 'utf8'));

export const BASELINE = doc.current;
export const REF = doc.current.reference_path;
export const PINNED_SHA = doc.current.commit_sha;
export const PINNED_TREE = doc.current.tree_sha;
export const HISTORY = doc.history || [];
