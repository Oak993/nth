#!/usr/bin/env node
/**
 * PORT_CONTRACT §14. Derive ALL requirement IDs from raw TRX and unchanged test-source Req traits.
 * Default: run all four solution test projects (and dependencies), never build/run Marrow.Setup.
 * --project <test.csproj> [--filter <expression>] produces explicitly partial evidence for Linux/scoped checks.
 * --no-run rederives from the latest captured run. Stale/dirty evidence cannot verify anything.
 * A test-based VERIFIED entry is only Gate D's unit/contract floor; semantic D remains mandatory.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync, mkdirSync, existsSync } from 'node:fs';
import { resolve, relative } from 'node:path';
import { platform, arch, release, tmpdir } from 'node:os';
import { isDeepStrictEqual } from 'node:util';
import { captureBinding, sha256 } from './evidence-binding.mjs';
import { TEST_PROJECTS, ledgerRecords, deriveRequirements, walk, testArguments, discoveryArguments } from './requirement-evidence.mjs';

const root=process.cwd(),args=process.argv.slice(2),noRun=args.includes('--no-run');
const option=n=>{const i=args.indexOf(n);return i<0?null:args[i+1];};
const project=option('--project'),filter=option('--filter');
if((args.includes('--project')&&!project)||(args.includes('--filter')&&!filter)||(filter&&!project)||args.some((a,i)=>a.startsWith('--')&&!['--no-run','--project','--filter'].includes(a)&&args[i-1]!=='--filter'))throw Error('usage: req_verify.mjs [--no-run | --project test.csproj [--filter expression]]');
if(project&&!(TEST_PROJECTS.includes(project)||project==='tests/Marrow.Shim.OfflineTests/Marrow.Shim.OfflineTests.csproj'))throw Error('only known test projects can run; never the installer');
const runs=resolve(root,'porting/verification/runs');let runDir,run;
if(!noRun){
 runDir=resolve(runs,new Date().toISOString().replace(/[:.]/g,'-'));mkdirSync(runDir,{recursive:true});
 const binding=captureBinding(root),started_at=new Date().toISOString();
 const dataDir=resolve(tmpdir(),`marrow-reqverify-${process.pid}`);mkdirSync(dataDir,{recursive:true});
 const commands=[];let exit_code=0;
 for(const [i,p] of (project?[project]:TEST_PROJECTS).entries()){
  const output=resolve(runDir,`project-${i}`);mkdirSync(output,{recursive:true});
  const argv=testArguments(p,relative(root,output).replaceAll('\\','/'),filter);
  const start=new Date().toISOString();
  console.log(`RUN dotnet ${argv.join(' ')}`);
  const result=spawnSync('dotnet',argv,{cwd:root,encoding:'utf8',env:{...process.env,MARROW_PORT_DATA_DIR:dataDir,DOTNET_CLI_TELEMETRY_OPTOUT:'1'},maxBuffer:1<<28});
  writeFileSync(resolve(output,'command.txt'),`${result.stdout||''}${result.stderr||''}${result.error||''}`);
  const code=result.status??1;if(code!==0)exit_code=code;
  const discovery_command=['dotnet',...discoveryArguments(p)];
  const discovery=spawnSync('dotnet',discovery_command.slice(1),{cwd:root,encoding:'utf8',env:{...process.env,MARROW_PORT_DATA_DIR:dataDir,DOTNET_CLI_TELEMETRY_OPTOUT:'1'},maxBuffer:1<<28});
  writeFileSync(resolve(output,'discovery.txt'),`${discovery.stdout||''}${discovery.stderr||''}${discovery.error||''}`);
  if(discovery.status!==0)exit_code=discovery.status??1;
  commands.push({project:p,command:['dotnet',...argv],filter:filter||null,exit_code:code,discovery_command,discovery_exit_code:discovery.status??1,discovery:relative(root,resolve(output,'discovery.txt')).replaceAll('\\','/'),started_at:start,finished_at:new Date().toISOString(),log:relative(root,resolve(output,'command.txt')).replaceAll('\\','/'),trx:relative(root,resolve(output,'results.trx')).replaceAll('\\','/')});
 }
 const artifacts=[...walk(runDir,'.txt'),...walk(runDir,'.trx')].map(p=>({path:relative(root,p).replaceAll('\\','/'),sha256:sha256(readFileSync(p))}));
 const sdk=spawnSync('dotnet',['--version'],{encoding:'utf8'});
 run={dir:relative(root,runDir).replaceAll('\\','/'),binding,complete:!project&&!filter,exit_code,commands,artifacts,started_at,finished_at:new Date().toISOString(),
  environment:{platform:platform(),architecture:arch(),os:`${platform()} ${release()}`,tools:{dotnet:(sdk.stdout||'unavailable').trim(),node:process.versions.node}},data_dir:dataDir};
 if(platform()==='win32'){const ver=spawnSync('cmd',['/c','ver'],{encoding:'utf8'});run.environment.os=(ver.stdout||run.environment.os).trim();}
 writeFileSync(resolve(runDir,'run.json'),JSON.stringify(run,null,2)+'\n');
}else{
 if(!existsSync(runs))throw Error('no recorded run; run without --no-run');
 const dirs=readdirSync(runs).filter(d=>existsSync(resolve(runs,d,'run.json'))).sort();if(!dirs.length)throw Error('no recorded run');
 runDir=resolve(runs,dirs.at(-1));run=JSON.parse(readFileSync(resolve(runDir,'run.json'),'utf8'));
}
const bound=isDeepStrictEqual(run.binding,captureBinding(root))&&run.binding.dirty.length===0;
const derived=deriveRequirements(root,ledgerRecords(root),run,bound);
const doc={schema:'marrow/requirement-verification/2',run:{...run,bound_to_current_head:bound},...derived};
writeFileSync(resolve(root,'porting/verification/requirements.json'),JSON.stringify(doc,null,1)+'\n');
console.log(`Run ${doc.run.dir}: exit ${run.exit_code}, ${bound?'BOUND':'UNBOUND'}, ${run.complete?'FULL':'PARTIAL'}`);
console.log(`Unit/contract VERIFIED ${doc.verified}/${doc.applicable} (${doc.total} total): ${JSON.stringify(doc.counts)}`);
console.log(`Test results ${JSON.stringify(doc.test_totals)}. Semantic/native/release evidence remains separate.`);
process.exit(!bound||!run.complete||run.exit_code!==0||derived.verified!==derived.applicable||derived.unknown_ids_in_tests.length?1:0);
