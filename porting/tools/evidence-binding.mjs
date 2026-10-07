// PORT_CONTRACT §14: exact revision and byte hashes; no mtime-only acceptance.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, realpathSync, statSync } from 'node:fs';
import { resolve, relative, isAbsolute, sep } from 'node:path';
import { isDeepStrictEqual } from 'node:util';

export const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
export const json = (root, path) => JSON.parse(readFileSync(resolve(root, path), 'utf8'));
export function artifactPath(root, path) {
 if (typeof path !== 'string' || !path || isAbsolute(path) || path.includes('\\') || path.split('/').includes('..')) throw Error(`unsafe artifact path: ${path}`);
 const full = realpathSync(resolve(root, path));
 const rel = relative(realpathSync(root), full);
 if (rel === '..' || rel.startsWith('..' + sep) || isAbsolute(rel) || !statSync(full).isFile()) throw Error(`artifact escapes root or is not a file: ${path}`);
 return full;
}
export function validateArtifact(root, artifact) {
 if (!artifact || !/^[a-f0-9]{64}$/.test(artifact.sha256)) throw Error('artifact needs SHA256');
 const path = artifactPath(root, artifact.path);
 if (sha256(readFileSync(path)) !== artifact.sha256) throw Error(`artifact hash mismatch: ${artifact.path}`);
 return path;
}
// Bind every tracked input, including EDs, unknowns, fixtures and policies. Run receipts are outputs.
const inputPath = p => !p.startsWith('porting/verification/') || p === 'porting/verification/release-scope.json';
export function captureBinding(root = process.cwd()) {
 const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', maxBuffer: 1 << 26 }).trimEnd();
 const pin = json(root, 'porting/baseline.json').current;
 const files = git('ls-files','-z').split('\0').filter(Boolean).filter(inputPath).sort();
 const inputs = files.map(p => `${p}\0${sha256(readFileSync(resolve(root,p)))}`).join('\n');
 const dirty = git('status','--porcelain','--untracked-files=all').split('\n').filter(Boolean).filter(l => inputPath(l.slice(3)));
 return { head: git('rev-parse','HEAD'), baseline: pin.commit_sha, baseline_tree: pin.tree_sha,
  src_tree: git('rev-parse','HEAD:src'), tests_tree: git('rev-parse','HEAD:tests'), inputs_sha256: sha256(inputs), dirty };
}
export function validateBinding(actual, expected) {
 if (!expected || expected.dirty?.length) throw Error('release inputs are dirty; commit inputs and rerun');
 if (!actual || !isDeepStrictEqual(actual, expected)) throw Error('evidence is stale or unbound to baseline/revision/trees/input hashes');
 for (const k of ['head','baseline','baseline_tree','src_tree','tests_tree']) if (!/^[a-f0-9]{40}$/.test(actual[k])) throw Error(`missing ${k} SHA`);
 if (!/^[a-f0-9]{64}$/.test(actual.inputs_sha256)) throw Error('missing input manifest hash');
}
export function validateRunMetadata(run) {
 const start=Date.parse(run?.started_at),end=Date.parse(run?.finished_at);
 if(!Number.isFinite(start)||!Number.isFinite(end)||end<start||end>Date.now()+60000)throw Error('invalid run times');
 if(!run.environment?.os||!run.environment?.architecture||!run.environment?.platform||!run.environment?.tools?.dotnet||!run.environment?.tools?.node)throw Error('missing run environment/toolchain');
 if(!run.dir||run.dir.includes('\\')||run.dir.split('/').includes('..')||isAbsolute(run.dir))throw Error('unsafe run directory');
}
