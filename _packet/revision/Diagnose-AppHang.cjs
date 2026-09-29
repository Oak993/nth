// Bounded VSTest diagnostics only, never a replacement for the requested ordinary gate.
// Uses the installed blame collector without dumps/packages. Preserve original aborted TRX.
// Microsoft reference: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),crypto=require('node:crypto');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
if(cp.execFileSync('git',['branch','--show-current'],{encoding:'utf8',cwd:root}).trim()!=='opencode/chatgpt-1')throw Error('Wrong branch');
const filter=process.argv[2]||'';
if(filter&&!/^FullyQualifiedName=[A-Za-z_][\w.]+$/.test(filter))throw Error('Only one exact Fact/method filter is permitted');
const name=filter?'app-hang-isolated':'app-hang-diagnostic';
const run=path.join(root,'_packet','revision','evidence',name+'-'+new Date().toISOString().replace(/[:.]/g,'-')+'-'+crypto.randomBytes(3).toString('hex'));
fs.mkdirSync(run,{recursive:true});
const quote=s=>"'"+s.replace(/'/g,"''")+"'";
const args=['test','tests\\Marrow.App.Tests','--no-build','--nologo','-v','q','--blame-hang-dump-type','none','--blame-hang-timeout','150s',
 '--logger','trx;LogFileName=diagnostic.trx','--logger','console;verbosity=detailed','--diag',path.join(run,'vstest-diag.log'),
 '--results-directory',run];
if(filter)args.push('--filter',filter);
const source=fs.readFileSync('src/Marrow.App/Window/WindowManager.cs');
const result={started:new Date().toISOString(),classification:'DIAGNOSTIC_NOT_ORDINARY_GATE',command:'dotnet '+args.map(quote).join(' '),
 window_sha256:crypto.createHash('sha256').update(source).digest('hex').toUpperCase(),filter,timeout_per_test:'150s',dump_type:'none'};
fs.writeFileSync(path.join(run,'invocation.json'),JSON.stringify(result,null,2)+'\n');
const setup=`$ErrorActionPreference='Stop';
Get-ChildItem Env: | Where-Object { $_.Name -match '(?i)(^|_)API_?KEY($|_)|^MARROW_(NATIVE|SHELL_PROOF)' } | ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) };
$env:MARROW_PORT_DATA_DIR=(New-Item -ItemType Directory -Force (Join-Path $env:TEMP ('marrow-'+[guid]::NewGuid()))).FullName;
Write-Output ('GATE_ENV='+(ConvertTo-Json -Compress -InputObject @{data=$env:MARROW_PORT_DATA_DIR;temp=$env:TEMP;tmp=$env:TMP;outer_opt_in_variables=@(Get-ChildItem Env: | Where-Object {$_.Name -match '^MARROW_(NATIVE|SHELL_PROOF)'} | ForEach-Object Name);api_key_variables_remaining=@(Get-ChildItem Env: | Where-Object {$_.Name -match '(?i)(^|_)API_?KEY($|_)'} | ForEach-Object Name)}));
& dotnet ${args.map(quote).join(' ')}; exit $LASTEXITCODE`;
console.log('DIAGNOSTIC_EVIDENCE='+run);
const log=fs.createWriteStream(path.join(run,'console.log'),{flags:'wx'});
const child=cp.spawn('powershell.exe',['-NoProfile','-NonInteractive','-Command',setup],{cwd:root,windowsHide:true,stdio:['ignore','pipe','pipe']});
let pending='',last='';
function receive(chunk){
 const text=chunk.toString();log.write(chunk);pending+=text;
 let at;while((at=pending.indexOf('\n'))>=0){const line=pending.slice(0,at).replace(/\r$/,'');pending=pending.slice(at+1);
  if(line.startsWith('GATE_ENV='))result.environment=JSON.parse(line.slice(9));
  if(/Passed |Failed |\[FAIL\]|\[SKIP\]|Starting:|Finished:|test execution was aborted|Aborting|Inactivity|Test Run|Attachments:|Sequence\.xml/.test(line)){
   last=line;fs.writeFileSync(path.join(run,'latest-progress.json'),JSON.stringify({at:new Date().toISOString(),line})+'\n');
   if(!/\sPassed /.test(line))console.log(line);
  }
 }
}
child.stdout.on('data',receive);child.stderr.on('data',receive);
child.on('error',error=>{result.launch_error=String(error);});
child.on('close',(code,signal)=>{log.end();result.exit=code;result.signal=signal;result.ended=new Date().toISOString();result.last_progress=last;
 fs.writeFileSync(path.join(run,'diagnostic-result.json'),JSON.stringify(result,null,2)+'\n');
 console.log('DIAGNOSTIC_COMPLETED exit='+code+' last='+last);process.exitCode=code===0?0:1;
});
