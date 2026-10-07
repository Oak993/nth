#!/usr/bin/env node
// Run only the tooling's isolated synthetic negative controls. Never runs a Windows application/installer.
import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { resolve, relative } from 'node:path';
import { platform, arch, release } from 'node:os';
import { captureBinding, sha256, validateBinding } from './evidence-binding.mjs';
import { NEGATIVES } from './release-evidence.mjs';
const root=process.cwd(),binding=captureBinding(root);validateBinding(binding,binding);
const dir=resolve(root,'porting/verification/runs',`negative-${new Date().toISOString().replace(/[:.]/g,'-')}`);mkdirSync(dir,{recursive:true});
const command=[process.execPath,'--test','--test-reporter=tap','porting/tools/tests/release-evidence.test.mjs','porting/tools/tests/release-package-identity.test.mjs','porting/tools/tests/release-package-artifacts.test.mjs'];
const started_at=new Date().toISOString(),result=spawnSync(command[0],command.slice(1),{cwd:root,encoding:'utf8'});
const output=`${result.stdout||''}${result.stderr||''}${result.error||''}`;writeFileSync(resolve(dir,'negative.txt'),output);
if(result.status!==0||!/# fail 0\b/.test(output)||!/# pass [1-9]\d*\b/.test(output)||!/# skipped 0\b/.test(output))throw Error('negative control tests did not all pass without skips');
validateBinding(binding,captureBinding(root));
const path=relative(root,resolve(dir,'negative.txt')).replaceAll('\\','/');
const receipt={id:'release-negative-checks',kind:'negative-gate',status:'PASS',binding,command,exit_code:0,started_at,finished_at:new Date().toISOString(),
 environment:{platform:platform(),architecture:arch(),os:`${platform()} ${release()}`,tools:{node:process.versions.node}},producer:'record-release-negative-checks.mjs',negative_cases:NEGATIVES,
 artifacts:[{path,sha256:sha256(readFileSync(resolve(root,path)))}]};
writeFileSync(resolve(dir,'receipt.json'),JSON.stringify(receipt,null,2)+'\n');console.log(`Tooling negative receipt: ${relative(root,dir)}/receipt.json. Synthetic controls only; NO product verification credit.`);
