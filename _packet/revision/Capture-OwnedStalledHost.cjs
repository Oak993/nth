// One explicitly authenticated, supervisor-pinned existing testhost. Never discover or
// attach to an arbitrary process. Diagnostic attachment invalidates ordinary-pass status.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {captureOwned,parseTrace}=require('./OwnedEventPipe.cjs');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase()||process.argv.length!==3)throw Error('Expected owned request path from exact worktree');
const requestFile=path.resolve(process.argv[2]),directory=path.dirname(requestFile);
const prefix=path.join(root,'_packet','revision','evidence','owned-live-stack-');
if(!directory.startsWith(prefix)||path.basename(requestFile)!=='request.json'||fs.lstatSync(directory).isSymbolicLink())throw Error('Request is not an owned diagnostic directory');
const r=JSON.parse(fs.readFileSync(requestFile,'utf8'));
if(r.directory!==directory||r.supervisor_pid!==process.ppid||!Number.isInteger(r.target_pid)||r.target_pid<=0||!/^[a-f0-9]{32}$/.test(r.nonce)||r.scope!=='PINNED_OWNED_STALLED_APP_TESTHOST'||r.coordinator_stopped!==true)throw Error('Supervisor identity or ownership permit invalid');
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
const output=[];
(async()=>{
 for(let i=1;i<=2;i++){
  const result=await captureOwned('\\\\.\\pipe\\dotnet-diagnostic-'+r.target_pid,2000);
  const raw=path.join(directory,'capture-'+i+'.nettrace');fs.writeFileSync(raw,result.bytes,{flag:'wx'});
  const parsed=parseTrace(result.bytes),report=path.join(directory,'capture-'+i+'-stacks.txt');
  fs.writeFileSync(report,`OWNED TESTHOST DIAGNOSTIC ONLY; target_pid=${r.target_pid}; nonce=${r.nonce}; target_start_utc=${r.target_start_utc}\n`+parsed.report+'\n',{flag:'wx'});
  const summary={index:i,target_pid:r.target_pid,started:result.started,ended:result.ended,session:result.session,stop_attempted:result.stop_attempted,errors:result.errors,raw_sha256:hash(result.bytes),report_sha256:hash(fs.readFileSync(report)),parse:{...parsed,report:undefined}};
  output.push(summary);fs.writeFileSync(path.join(directory,'capture-'+i+'.json'),JSON.stringify(summary,null,2)+'\n',{flag:'wx'});
  console.log(JSON.stringify(summary));
  if(i===1)await new Promise(resolve=>setTimeout(resolve,1000));
 }
 fs.writeFileSync(path.join(directory,'captures.json'),JSON.stringify({request_sha256:hash(fs.readFileSync(requestFile)),scope:r.scope,ordinary_pass:false,captures:output},null,2)+'\n',{flag:'wx'});
 process.exitCode=output.every(x=>!x.errors.length&&x.parse.ended&&x.parse.samples>0)?0:1;
})().catch(error=>{console.error(String(error));process.exitCode=1;});
