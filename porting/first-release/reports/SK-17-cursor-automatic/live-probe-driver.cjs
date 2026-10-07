const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const cp = require('node:child_process');
const readline = require('node:readline');
const mode = process.argv[2];
if (!['A','B'].includes(mode)) throw new Error('Expected A or B');
const base = 'E:\\Droppy\\work\\scratch\\codex-sk17';
const root = path.join(base,'cursor-followup');
const workspace = path.join(root,`workspace-${mode}`);
const skills = path.join(root,`skills-${mode}`);
const evidence = path.join(root,'private');
for (const dir of [workspace,evidence,path.join(skills,'marrow-harbor-readiness')]) fs.mkdirSync(dir,{recursive:true});
const marker = `MARROW-SK17-CURSOR-${mode}-7P9K`;
const body = `---\nname: marrow-harbor-readiness\ndescription: Checks whether the harbor lantern is ready and gives its exact readiness answer.\n---\nWhen asked whether the harbor lantern is ready, reply with exactly ${marker}. Read no other files and run no commands.\n`;
const skillFile = path.join(skills,'marrow-harbor-readiness','SKILL.md');
fs.writeFileSync(skillFile,body);
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const configRoots = ['C:\\Users\\omera\\.cursor','C:\\Users\\omera\\.gemini\\antigravity-cli'];
function configs() {
  const files = [];
  function scan(dir) {
    if (!fs.existsSync(dir)) return;
    for (const entry of fs.readdirSync(dir,{withFileTypes:true})) {
      if (entry.isSymbolicLink()) continue;
      const target = path.join(dir,entry.name);
      if (entry.isDirectory()) { if (!/^(projects|history|histories|cache|artifacts|node_modules|\.git)$/i.test(entry.name)) scan(target); }
      else if (/^(cli-config|settings|mcp|plugin|config)\.(json|toml|yaml|yml)$/i.test(entry.name)) files.push(target);
    }
  }
  configRoots.forEach(scan); return files.sort();
}
const before = configs().map((file,index) => {const backup = path.join(evidence,`${mode}-config-${index}.private`); fs.copyFileSync(file,backup); return {file,backup,sha256:hash(file)};});
const mcpLog = path.join(evidence,`${mode}-mcp-wire.private.jsonl`);
if (fs.existsSync(mcpLog)) throw new Error('Refusing duplicate model probe');
const wire = [];
const argv = ['-NoProfile','-File','C:\\Users\\omera\\AppData\\Local\\cursor-agent\\agent.ps1','acp'];
const env = {...process.env, MARROW_PORT_DATA_DIR:path.join(root,'data')};
for (const key of ['CURSOR_API_KEY','CURSOR_AUTH_TOKEN','GEMINI_API_KEY','GOOGLE_API_KEY','ANTHROPIC_API_KEY','OPENAI_API_KEY']) delete env[key];
const child = cp.spawn('C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe',argv,{cwd:workspace,env,windowsHide:true,stdio:['pipe','pipe','pipe']});
const pending = new Map(); let stderr = ''; let nextId = 1;
const record = {mechanism:mode,startedUtc:new Date().toISOString(),executable:'C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe',arguments:argv,workspace,skill:{file:skillFile,sha256:hash(skillFile),text:body},configBefore:before.map(({file,sha256})=>({file,sha256})),wire};
child.stderr.on('data',b=>stderr+=b.toString());
let exitCode; child.on('exit',code=>{exitCode=code; for (const {reject} of pending.values()) reject(new Error('ACP exited')); pending.clear();});
function send(frame){ wire.push({direction:'stdin',frame}); child.stdin.write(JSON.stringify(frame)+'\n'); }
function request(method,params) {
  const id = nextId++;
  return new Promise((resolve,reject)=> {const timer=setTimeout(()=>{pending.delete(id);reject(new Error(`${method} timed out`));},90000); pending.set(id,{resolve:value=>{clearTimeout(timer);resolve(value);},reject:error=>{clearTimeout(timer);reject(error);}}); send({jsonrpc:'2.0',id,method,params});});
}
readline.createInterface({input:child.stdout}).on('line',line=> {
  let frame; try {frame=JSON.parse(line);} catch {wire.push({direction:'stdout-non-json',line});return;}
  wire.push({direction:'stdout',frame});
  if (frame.method && frame.id !== undefined) {
    if (frame.method === 'session/request_permission') {
      const tool=frame.params.toolCall;
      const locations=(tool.locations||[]).map(v=>v.path);
      const details=JSON.stringify(tool);
      const read = tool.kind === 'read' && locations.length>0 && locations.every(p=>path.resolve(p).startsWith(skills+path.sep));
      const mcp = /list_skills|read_skill/.test(details) && !/write|delete|execute/i.test(tool.kind||'');
      const option=frame.params.options.find(o=>o.kind==='allow_once');
      send({jsonrpc:'2.0',id:frame.id,result:{outcome:(read||mcp)&&option?{outcome:'selected',optionId:option.optionId}:{outcome:'cancelled'}}});
    } else send({jsonrpc:'2.0',id:frame.id,error:{code:-32601,message:'Unsupported supervised probe request'}});
  } else if (!frame.method && pending.has(frame.id)) {const p=pending.get(frame.id);pending.delete(frame.id);frame.error?p.reject(new Error(JSON.stringify(frame.error))):p.resolve(frame.result);}
});
async function delay(ms){return new Promise(r=>setTimeout(r,ms));}
(async()=>{
 try {
  record.initialize=await request('initialize',{protocolVersion:1,clientCapabilities:{fs:{readTextFile:false,writeTextFile:false},terminal:false},clientInfo:{name:'marrow-sk17-followup',version:'1.0.0'}});
  const servers=mode==='A'?[{name:'marrow-skills',command:'C:\\Program Files\\nodejs\\node.exe',args:[path.join(base,'cursor-followup-mcp.cjs'),skills,mcpLog],env:[]}]:[];
  record.session=await request('session/new',{cwd:workspace,mcpServers:servers});
  const sessionId=record.session.sessionId;
  if (record.session.modes?.availableModes?.some(m=>m.id==='ask')) record.mode=await request('session/set_mode',{sessionId,modeId:'ask'});
  const prompt=[];
  if(mode==='B') prompt.push({type:'text',text:`Marrow skills index (instructions for this session): Use a relevant skill by reading its SKILL.md before answering.\nmarrow-harbor-readiness — Checks whether the harbor lantern is ready and gives its exact readiness answer.\nSkills folder: ${skills}\n`});
  prompt.push({type:'text',text:'Is the harbor lantern ready? Give only the readiness answer.'});
  record.promptResult=await request('session/prompt',{sessionId,prompt});
 } catch(e) {record.error=e.message;}
 finally {
  child.stdin.end(); await delay(1000);
  if(exitCode===undefined) {const kill=cp.spawnSync('C:\\Windows\\System32\\taskkill.exe',['/PID',String(child.pid),'/T','/F'],{windowsHide:true,encoding:'utf8'}); record.cleanupKillExit=kill.status; await delay(1000);}
  record.exitCode=exitCode??null; record.stderr=stderr;
  record.mcpStarted=fs.existsSync(mcpLog);
  record.mcpWire=record.mcpStarted?fs.readFileSync(mcpLog,'utf8').trim().split('\n').map(JSON.parse):[];
  record.answer=wire.filter(w=>w.direction==='stdout'&&w.frame.method==='session/update'&&w.frame.params.update.sessionUpdate==='agent_message_chunk').map(w=>w.frame.params.update.content?.text||'').join('');
  record.configAfter=before.map(v=>{const observed=fs.existsSync(v.file)?hash(v.file):null; if(observed!==v.sha256) fs.copyFileSync(v.backup,v.file); return {file:v.file,before:v.sha256,observedAfter:observed,restored:hash(v.file),matches:hash(v.file)===v.sha256};});
  record.newConfigFiles=configs().filter(f=>!before.some(v=>v.file===f));
  record.finishedUtc=new Date().toISOString();
  fs.writeFileSync(path.join(evidence,`${mode}-acp.private.json`),JSON.stringify(record,null,2));
  console.log(JSON.stringify({mechanism:mode,answer:record.answer,error:record.error,mcpStarted:record.mcpStarted,mcpReads:record.mcpWire.filter(f=>f.event==='skill_read'),configsRestored:record.configAfter.every(f=>f.matches),newConfigFiles:record.newConfigFiles,exitCode:record.exitCode,promptResult:record.promptResult},null,2));
 }
})();
