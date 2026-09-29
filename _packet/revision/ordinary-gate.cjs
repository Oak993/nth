// Serial ordinary-environment build/test and reversible mutation evidence collector.
// Invokes approved PowerShell commands, without changing script execution policy.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const cp = require('node:child_process');
const { fixtureFailure } = require('./MutationFailureClassifier.cjs');
const root = 'E:\\Droppy\\wt\\oc\\chatgpt-1';
if (process.cwd().toLowerCase() !== root.toLowerCase()) throw Error('Wrong worktree');
const git = (...args) => cp.execFileSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true});
if (git('branch','--show-current').trim() !== 'opencode/chatgpt-1') throw Error('Wrong branch');
const options = {};
for (let i=2;i<process.argv.length;i+=2) {
  if (!process.argv[i].startsWith('--') || process.argv[i+1] === undefined) throw Error('Expected --name value');
  options[process.argv[i].slice(2)] = process.argv[i+1];
}
const mode = options.mode || 'build';
if (!['build','test','mutate'].includes(mode)) throw Error('Unknown mode');
const project = options.project || 'tests\\Marrow.App.Tests';
if (!['tests\\Marrow.App.Tests','tests\\Marrow.UI.Tests','tests\\Marrow.Core.Tests'].includes(project))
  throw Error('Only the three explicitly requested existing test projects are allowed');
if (options.project && mode !== 'test') throw Error('Project selection is for ordinary tests, never mutation ownership');
const stamp = new Date().toISOString().replace(/[:.]/g,'-');
const run = path.join(root,'_packet','revision','evidence',(options.name || mode)+'-'+stamp+'-'+crypto.randomBytes(3).toString('hex'));
fs.mkdirSync(run,{recursive:true});
console.log('EVIDENCE='+run);
const save = (name,value) => fs.writeFileSync(path.join(run,name),JSON.stringify(value,null,2)+'\n');
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
const safe = relative => {
  const full=path.resolve(root,relative);
  if (!full.toLowerCase().startsWith(root.toLowerCase()+path.sep)) throw Error('Path outside worktree');
  return full;
};
const quote = value => "'" + value.replace(/'/g,"''") + "'";
const reset = `Get-ChildItem Env: | Where-Object { $_.Name -match '^MARROW_(NATIVE|SHELL_PROOF)' } | ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) };\n'ZAI_API_KEY','OPENROUTER_API_KEY','PIXABAY_API_KEY','PEXELS_API_KEY','ELEVENLABS_API_KEY','DEEPSEEK_API_KEY','MODEL_API_KEY','COMMAND_CODE_API_KEY','ANTHROPIC_API_KEY','OPENAI_API_KEY','GEMINI_API_KEY','GROQ_API_KEY','MISTRAL_API_KEY','PERPLEXITY_API_KEY','XAI_API_KEY','BRAVE_API_KEY','FIRECRAWL_API_KEY','AI_GATEWAY_API_KEY','AIML_API_KEY' | ForEach-Object { Remove-Item Env:$_ -ErrorAction SilentlyContinue }; $env:MARROW_PORT_DATA_DIR = (New-Item -ItemType Directory -Force (Join-Path $env:TEMP ("marrow-" + [guid]::NewGuid()))).FullName;`;
const decode = s => s.replace(/&#x([\da-f]+);/gi,(_,n)=>String.fromCodePoint(parseInt(n,16)))
  .replace(/&#(\d+);/g,(_,n)=>String.fromCodePoint(Number(n)))
  .replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&apos;/g,"'").replace(/&amp;/g,'&');
function readTrx(file) {
  if (!fs.existsSync(file)) return [];
  const xml=fs.readFileSync(file,'utf8');
  const result=[];
  const regex=/<UnitTestResult\b([^>]*?)(\/?>)/g;
  for (const match of xml.matchAll(regex)) {
    const attrs=Object.fromEntries([...match[1].matchAll(/(\w+)="([^"]*)"/g)].map(m=>[m[1],decode(m[2])]));
    const body=match[2]==='/>'?'':xml.slice(match.index+match[0].length,xml.indexOf('</UnitTestResult>',match.index));
    result.push({name:attrs.testName,outcome:attrs.outcome,error:decode(body.match(/<Message>([\s\S]*?)<\/Message>/)?.[1] || '')});
  }
  return result;
}
function retainOwnedChildEvidence(label, environment) {
  const data=environment?.data;
  if (!data || !path.isAbsolute(data) || !fs.existsSync(data) || !/^marrow-[0-9a-f-]+$/i.test(path.basename(data))) return [];
  // Only the fresh data directory made for this command, and only the explicit child-protocol
  // artifacts. Do not copy profiles, credentials, package caches or arbitrary temporary files.
  const children=[];
  for(const entry of fs.readdirSync(data,{withFileTypes:true})) {
    if(!entry.isDirectory() || entry.isSymbolicLink()) continue;
    if(/^marrow-window-process-[0-9a-f]{32}$/i.test(entry.name)) {
      const ownedRoot=path.join(data,entry.name);
      children.push(ownedRoot);
      for(const phase of ['phase-1','phase-2']) {
        const phaseRoot=path.join(ownedRoot,phase);
        if(!fs.existsSync(phaseRoot))continue;
        if(!fs.lstatSync(phaseRoot).isDirectory()||fs.lstatSync(phaseRoot).isSymbolicLink())throw Error('Unexpected phase directory');
        for(const phaseChild of fs.readdirSync(phaseRoot,{withFileTypes:true}))
          if(phaseChild.isDirectory()&&!phaseChild.isSymbolicLink()&&/^marrow-window-process-[0-9a-f]{32}$/i.test(phaseChild.name))children.push(path.join(phaseRoot,phaseChild.name));
      }
    }
    if(entry.name==='WindowManagerNative') {
      for(const nested of fs.readdirSync(path.join(data,entry.name),{withFileTypes:true}))
        if(nested.isDirectory()&&!nested.isSymbolicLink()&&/^[0-9a-f]{32}$/i.test(nested.name))children.push(path.join(data,entry.name,nested.name));
    }
  }
  const retained=[];
  const names=['request.json','invocation.json','ready.json','permit.json','child.log','exit-path.json','failure.json','parent-result.json','child-result.json','runner.stdout.log','runner.stderr.log','results/native-child.trx','results/child-runner.trx','results/child.trx','restart-root.json','restart-request.json','restart-result.json','restart-failure.json','phase-1-complete.json','phase-result.json','input-store.bin','output-store.bin'];
  for(const child of children) {
    const record={source:child,files:[]};
    const placement=fs.readdirSync(child).filter(name=>/^placement-(topology-start|coverage|case-\d{3}|topology-change-\d{3})\.json$/.test(name));
    for(const name of names.concat(placement)) {
      const file=path.join(child,name);
      if(!fs.existsSync(file))continue;
      const stat=fs.lstatSync(file);
      if(!stat.isFile()||stat.isSymbolicLink()||stat.size>8*1024*1024)throw Error('Unexpected child evidence file shape');
      const bytes=fs.readFileSync(file);
      const target=path.join(run,'children',label,path.basename(child),name);
      fs.mkdirSync(path.dirname(target),{recursive:true});fs.writeFileSync(target,bytes);
      record.files.push({name,retained:path.relative(run,target),sha256:sha(bytes)});
    }
    retained.push(record);
  }
  return retained;
}
function validateProcessMutationFailure(result) {
  const proof = result.child_evidence?.find(child => child.files.some(file => file.name === 'parent-result.json'));
  if (!proof) return {valid:false,reason:'No retained child process evidence'};
  function json(name) {
    const file=proof.files.find(file=>file.name===name);
    return file ? JSON.parse(fs.readFileSync(path.join(run,file.retained),'utf8')) : null;
  }
  const parent=json('parent-result.json'),failure=json('failure.json');
  const valid=parent?.KernelChildExitCode===74&&parent?.CleanupComplete===true&&parent?.ExitProofVerified===false&&
    failure?.ExitCode===74&&failure.Identity?.ChildProcessId===parent.Child?.ChildProcessId&&
    /WindowManagerProcessChild\.(Scenario|.*ExitFromShell)/.test(failure.Error||'')&&
    !/NATIVE_PRECONDITION|TimeoutException|before the selected child Fact|authorize the identified child/.test(failure.Error||'');
  return {valid,child_exit:parent?.KernelChildExitCode,cleanup_complete:parent?.CleanupComplete,child_error:failure?.Error||'',reason:valid?'Owned child reached scenario and failed with code 74':'Child failure was not a verified scenario assertion'};
}
function invoke(label,args) {
  console.log('RUN '+label+': dotnet '+args.join(' '));
  const start=new Date().toISOString();
  const command=`$ErrorActionPreference='Stop';\nGet-ChildItem Env: | Where-Object { $_.Name -match '(?i)(^|_)API_?KEY($|_)' } | ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) };\n${reset}\nWrite-Output ('GATE_ENV=' + (ConvertTo-Json -Compress -InputObject @{ data=$env:MARROW_PORT_DATA_DIR; temp=$env:TEMP; tmp=$env:TMP; outer_opt_in_variables=@(Get-ChildItem Env: | Where-Object { $_.Name -match '^MARROW_(NATIVE|SHELL_PROOF)' } | ForEach-Object Name); api_key_variables_remaining=@(Get-ChildItem Env: | Where-Object { $_.Name -match '(?i)(^|_)API_?KEY($|_)' } | ForEach-Object Name) }));\n& dotnet ${args.map(quote).join(' ')}; exit $LASTEXITCODE`;
  const child=cp.spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',command],{cwd:root,encoding:'utf8',windowsHide:true,maxBuffer:32*1024*1024});
  const output=(child.stdout||'')+(child.stderr||'');
  fs.writeFileSync(path.join(run,label+'.log'),output);
  const metadata=output.match(/^GATE_ENV=(.+)$/m);
  const item={name:label,command:'dotnet '+args.map(quote).join(' '),exit:child.status,start,end:new Date().toISOString(),environment:metadata?JSON.parse(metadata[1]):null,
    source_hashes:['src/Marrow.App/Window/WindowManager.cs','src/Marrow.App/Port/AppShell.cs','src/Marrow.App/AppDelegate.cs','src/Marrow.App/Views/Settings/SettingsView.cs','src/Marrow.App/Window/ThreadWindow.cs','src/Marrow.App/Views/Palette/CommandPalette.cs','src/Marrow.Core/Support/Text.cs','src/Marrow.Services/Port/SettingsStore.cs'].map(name=>({name,sha256:sha(fs.readFileSync(path.join(root,name)))}))};
  if (child.error) item.launch_error=String(child.error);
  if (args[0]==='build') {
    item.clean=child.status===0 && /0 Warning\(s\)/.test(output) && /0 Error\(s\)/.test(output);
    item.binaries=['Marrow.App.dll','Marrow.App.Tests.dll','Marrow.Core.dll','Marrow.Services.dll','Marrow.UI.dll'].map(name=>{
      const binary=path.join(root,'tests','Marrow.App.Tests','bin','Debug','net10.0-windows10.0.19041.0',name);
      return {name,sha256:fs.existsSync(binary)?sha(fs.readFileSync(binary)):null};
    });
    console.log('BUILD '+label+' exit='+child.status+' zero_warnings_errors='+item.clean);
    if (!item.clean) console.log(output);
  } else {
    item.tests=readTrx(path.join(run,label+'.trx'));
    item.total=item.tests.length;
    item.passed=item.tests.filter(t=>t.outcome==='Passed').length;
    item.failed=item.tests.filter(t=>t.outcome==='Failed').length;
    item.child_evidence=retainOwnedChildEvidence(label,item.environment);
    console.log(`TEST ${label} exit=${child.status} total=${item.total} passed=${item.passed} failed=${item.failed}`);
    for (const test of item.tests.filter(t=>t.outcome!=='Passed')) console.log(test.name+': '+test.error.split(/\r?\n/).slice(0,7).join('\n'));
    if (item.total===0) console.log(output);
  }
  save(label+'.json',item);
  return item;
}
const build = label => invoke(label,['build','Marrow.sln','-c','Debug','--nologo','-v','q']);
function test(label,filter) {
  const args=['test',project,'--no-build','--nologo','-v','q'];
  if (filter) args.push('--filter',filter);
  args.push('--logger','trx;LogFileName='+label+'.trx','--results-directory',run);
  return invoke(label,args);
}
if (mode==='build') {
  const r=build('build'); process.exitCode=r.clean?0:1;
} else if (mode==='test') {
  const r=test('tests',options.filter); process.exitCode=r.exit===0&&r.total>0&&r.total===r.passed?0:1;
} else {
  if (!options.manifest) throw Error('A manifest is required');
  let mutations=JSON.parse(fs.readFileSync(safe(options.manifest),'utf8'));
  if (options.record) mutations=mutations.filter(m=>m.records.includes(options.record));
  if (options.ids) mutations=mutations.filter(m=>options.ids.split(',').includes(m.id));
  if (!mutations.length) throw Error('No mutations selected');
  const results=[];
  for (const m of mutations) {
    const file=safe(m.path), original=fs.readFileSync(file), diff=git('diff','--no-ext-diff','--',m.path);
    const r={...m,original_sha256:sha(original),runs:[],status:'NOT_RUN',restored:false};
    const selection='FullyQualifiedName='+m.killer+'&Req='+(options.record||m.records[0]);
    try {
      let text=original.toString('utf8');
      if (text.includes('\r\n')) throw Error('Production source is not LF');
      for (const replacement of m.replacements) {
        const before=replacement.before.replace(/\r\n/g,'\n'),after=replacement.after.replace(/\r\n/g,'\n');
        const count=text.split(before).length-1;
        if (count!==1) throw Error('Exact source match count '+count+', expected one');
        text=text.replace(before,after);
      }
      fs.writeFileSync(file,text);
      r.mutant_sha256=sha(fs.readFileSync(file));
      fs.writeFileSync(path.join(run,m.id+'.diff'),git('diff','--no-ext-diff','--',m.path));
      const b=build(m.id+'-mutant-build'); r.runs.push(b);
      if (!b.clean) throw Error('Mutant build failure is not a kill');
      r.status='RED_TWICE';
      for(let i=1;i<=2;i++) {
        const red=test(m.id+'-red-'+i,selection);r.runs.push(red);
        if(red.exit===0||red.total!==1||red.failed!==1) r.status='SURVIVED_OR_INVALID_RED';
        if(fixtureFailure(red.tests)) r.status='FIXTURE_FAILURE_NOT_A_KILL';
        if(m.killer.includes('.WindowManagerProcessTests.')) {
          red.process_failure=validateProcessMutationFailure(red);
          if(!red.process_failure.valid)r.status='INVALID_CHILD_FAILURE_NOT_A_KILL';
          console.log('CHILD_FAILURE '+m.id+' verified='+red.process_failure.valid+' '+red.process_failure.reason);
          save(red.name+'.json',red);
        }
      }
    } catch(error) {r.status='ERROR';r.error=String(error);console.log(r.error);}
    finally {
      fs.writeFileSync(file,original);
      r.restored_sha256=sha(fs.readFileSync(file));
      r.git_diff_unchanged=diff===git('diff','--no-ext-diff','--',m.path);
      r.restored=r.original_sha256===r.restored_sha256&&r.git_diff_unchanged;
    }
    if(!r.restored) {save(m.id+'.json',r);throw Error('Byte restoration failed');}
    const b=build(m.id+'-restored-build');r.runs.push(b);
    if(!b.clean) r.status='RESTORED_BUILD_FAILED';
    else {
      const green=test(m.id+'-green',selection);r.runs.push(green);
      if(green.exit!==0||green.total!==1||green.passed!==1)r.status='RESTORED_TEST_FAILED';
      else if(r.status==='RED_TWICE')r.status='KILLED_TWICE_ALONE_AND_ORDINARY_GREEN';
    }
    results.push(r);save('results.json',results);
    console.log('FINAL '+m.id+' '+r.status+' restored='+r.restored);
    if(r.status!=='KILLED_TWICE_ALONE_AND_ORDINARY_GREEN') {process.exitCode=1;break;}
  }
  console.log('COMPLETED_MUTATIONS='+results.filter(r=>r.status==='KILLED_TWICE_ALONE_AND_ORDINARY_GREEN').length);
}
