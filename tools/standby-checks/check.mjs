// PORT-ONLY: T28 / OD-0122 / OD-0124. Explicitly invoked real CLI probe, outside tests.
// Never implements a model-turn method, authentication, or a config/settings write.
import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { createInterface } from 'node:readline';

const [service, output] = process.argv.slice(2);
if (!['codex', 'opencode', 'opencode-warm', 'cursor', 'claude', 'inventory'].includes(service) || !output) throw Error('check.mjs <service> <new evidence directory>');
const root = resolve(output);
mkdirSync(root, { recursive: true });
const marker = join(root, service + '.attempt');
writeFileSync(marker, new Date().toISOString(), { flag: 'wx' }); // No automatic session retry.
const actualService = service === 'opencode-warm' ? 'opencode' : service;
const cwd = join(root, actualService + '-project');
mkdirSync(cwd, { recursive: true });
const env = { ...process.env, MARROW_PORT_DATA_DIR: join(root, 'port-data') };
// Existing sign-ins belong to the CLI. The harness never opens any credential file.
for (const name of Object.keys(env)) if (/(?:API_KEY|ACCESS_TOKEN|AUTH_TOKEN)$/.test(name)) delete env[name];
const home = process.env.USERPROFILE;
const local = process.env.LOCALAPPDATA;
const paths = {
  codex: join(local, 'Programs/OpenAI/Codex/bin/codex.exe'),
  claude: join(home, '.local/bin/claude.exe'),
  opencode: join(local, 'Programs/@opencodedesktop/resources/opencode-cli.exe'),
  cursor: join(local, 'cursor-agent/cursor-agent.ps1'),
};
const history = 'test user message\ntest assistant reply';
const hash = text => createHash('sha256').update(text).digest('hex');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const evidence = service === 'opencode-warm' ? JSON.parse(readFileSync(join(root, 'opencode.json'), 'utf8')) : { service, started: new Date().toISOString(), cwd, sourceCommit: '7b910406886137947a101c742e35e94a527ec871', historyHash: hash(history), status: 'BLOCKED', requests: [], notifications: [] };
if (service === 'opencode-warm' && (evidence.status !== 'BLOCKED' || evidence.sessionId || !evidence.error?.includes('/doc'))) throw Error('Warm continuation needs the prior failed gate and no session');
const save = () => writeFileSync(join(root, actualService + '.json'), JSON.stringify(evidence, null, 2) + '\n');
const allowedRpc = new Set(['initialize', 'initialized', 'thread/start', 'thread/inject_items', 'thread/read', 'thread/delete', 'session/new', 'session/load']);
class Rpc {
  constructor(exe, args, version) {
    this.version = version; this.next = 1; this.pending = new Map(); this.errors = ''; this.events = []; this.violation = null;
    this.child = spawn(exe, args, { cwd, env, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    this.child.stderr.on('data', b => { this.errors += b; });
    this.child.on('error', e => { this.error = e.message; });
    this.child.on('exit', code => { this.exitCode = code; for (const p of this.pending.values()) p.reject(Error('CLI exited: ' + code)); });
    createInterface({ input: this.child.stdout }).on('line', line => {
      let msg; try { msg = JSON.parse(line); } catch { return; }
      if (msg.method) {
        this.events.push(msg); evidence.notifications.push(msg);
        if (msg.id !== undefined || /^(turn\/|item\/|hook\/)|approval|permission/.test(msg.method)) this.violation = msg.method;
        const update = msg.params?.update?.sessionUpdate;
        if (['agent_message_chunk', 'agent_thought_chunk', 'tool_call', 'tool_call_update'].includes(update) && !this.replaying) this.violation = update;
      } else if (msg.id !== undefined && this.pending.has(msg.id)) {
        const pending = this.pending.get(msg.id); this.pending.delete(msg.id);
        msg.error ? pending.reject(Error(JSON.stringify(msg.error))) : pending.resolve(msg.result);
      }
    });
  }
  async request(method, params) {
    if (!allowedRpc.has(method)) throw Error('Forbidden RPC: ' + method);
    if (this.violation) throw Error('Standby activity: ' + this.violation);
    const id = this.next++;
    const message = { ...(this.version ? { jsonrpc: '2.0' } : {}), id, method, params };
    evidence.requests.push(message); save();
    const result = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(Error('Timeout: ' + method)); }, 25000);
      this.pending.set(id, { resolve: value => { clearTimeout(timer); resolve(value); }, reject: err => { clearTimeout(timer); reject(err); } });
      this.child.stdin.write(JSON.stringify(message) + '\n');
    });
    return result;
  }
  notify(method, params = {}) {
    if (!allowedRpc.has(method)) throw Error('Forbidden notification');
    const message = { ...(this.version ? { jsonrpc: '2.0' } : {}), method, params };
    evidence.requests.push(message); this.child.stdin.write(JSON.stringify(message) + '\n');
  }
  stop() {
    if (this.child.exitCode === null) spawnSync('taskkill', ['/PID', String(this.child.pid), '/T', '/F'], { windowsHide: true });
    evidence.cliStderrHash = hash(this.errors); // Never print or persist arbitrary user-owned CLI log contents.
    evidence.cliStderrActivityLines = this.errors.split('\n').filter(l => /model.request|inference|turn.started|tool.call/i.test(l)).map(l => l.replace(/(?:Bearer\s+|sk-)[^\s"']+/g, '[redacted]'));
  }
}
const acpInit = cursor => ({ protocolVersion: 1, clientInfo: { name: 'marrow', title: 'Marrow', version: '1.7.0' }, clientCapabilities: { fs: { readTextFile: false, writeTextFile: false }, terminal: false, ...(cursor ? { _meta: { parameterizedModelPicker: true } } : {}) } });
function collectFiles(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(x => x.isDirectory() ? collectFiles(join(dir, x.name)) : [join(dir, x.name)]);
}
async function codex() {
  const schemaDir = join(root, 'codex-schema'); mkdirSync(schemaDir);
  const generated = spawnSync(paths.codex, ['app-server', 'generate-json-schema', '--out', schemaDir], { env, cwd, windowsHide: true, encoding: 'utf8', timeout: 60000 });
  evidence.schemaExit = generated.status;
  evidence.schemaFiles = collectFiles(schemaDir).map(p => p.slice(schemaDir.length + 1));
  const schemaPath = join(schemaDir, 'ClientRequest.json');
  evidence.portSchemaFilenameExists = existsSync(schemaPath);
  const candidates = collectFiles(schemaDir).filter(p => /client.?request/i.test(p));
  evidence.schemaCandidates = candidates.map(p => ({ path: p, sha256: hash(readFileSync(p)), listsInjection: readFileSync(p, 'utf8').includes('"thread/inject_items"') }));
  if (generated.status !== 0 || !evidence.schemaCandidates.some(x => x.listsInjection)) throw Error('CLI schema does not prove injection');
  const rpc = new Rpc(paths.codex, ['app-server'], false); let id, rollout;
  try {
    const initialized = await rpc.request('initialize', { clientInfo: { name: 'marrow', title: 'Marrow', version: '1.7.0' }, capabilities: { experimentalApi: true } });
    evidence.initialize = initialized; rpc.notify('initialized');
    evidence.portVersionGate = /^[^/\s]+\/(\d+)\.(\d+)\.(\d+)/.test(initialized.userAgent);
    const started = await rpc.request('thread/start', { cwd, approvalPolicy: 'untrusted', sandbox: 'read-only', approvalsReviewer: 'user', developerInstructions: '' });
    id = started.thread.id; rollout = started.thread.path; evidence.threadId = id; evidence.rolloutPath = rollout;
    evidence.threadBefore = started.thread; save();
    evidence.injectResult = await rpc.request('thread/inject_items', { threadId: id, items: [{ type: 'message', role: 'developer', content: [{ type: 'input_text', text: history }] }] });
    await sleep(2500);
    evidence.threadAfter = await rpc.request('thread/read', { threadId: id, includeTurns: true });
    if (!rollout || !existsSync(rollout)) throw Error('No persisted rollout');
    const raw = readFileSync(rollout, 'utf8');
    const records = raw.trim().split('\n').map(l => JSON.parse(l));
    evidence.rolloutTypes = records.map(r => ({ type: r.type, payloadType: r.payload?.type }));
    evidence.rolloutHash = hash(raw);
    const injected = records.filter(r => r.type === 'response_item').flatMap(r => r.payload?.content ?? []).filter(x => x.text === history);
    evidence.readBackMatches = injected.length;
    evidence.modelActivity = records.filter(r => r.type === 'event_msg' && /task_started|turn_started|token_count|agent_message|exec_command|tool_call/.test(r.payload?.type ?? ''));
    evidence.quiet = !rpc.violation && evidence.modelActivity.length === 0 && !/model.request|inference|turn.started|tool.call/i.test(rpc.errors);
    if (injected.length !== 1 || !evidence.quiet) throw Error('Injection read-back or no-activity proof failed');
    evidence.status = evidence.portSchemaFilenameExists && evidence.portVersionGate ? 'PASS' : 'FIX_REQUIRED';
  } finally {
    if (id) {
      // Cleanup remains allowed after an activity violation; it never sends a turn.
      rpc.violation = null;
      evidence.deleteResult = await rpc.request('thread/delete', { threadId: id }).catch(e => ({ error: e.message }));
      evidence.rolloutDeleted = !!rollout && !existsSync(rollout);
      try { await rpc.request('thread/read', { threadId: id, includeTurns: true }); evidence.threadReadAfterDeletion = 'still exists'; }
      catch (e) { evidence.threadReadAfterDeletion = e.message; }
      if (!evidence.rolloutDeleted || evidence.deleteResult.error || evidence.threadReadAfterDeletion === 'still exists') {
        evidence.status = 'BLOCKED'; evidence.error = 'Disposable Codex deletion was not confirmed';
      }
    }
    rpc.stop(); save();
  }
}
async function claude() {
  const version = spawnSync(paths.claude, ['--version'], { cwd, env, windowsHide: true, encoding: 'utf8', timeout: 15000 });
  evidence.version = version.stdout?.trim();
  evidence.portVersionGate = /^2\.1\.(\d+) \(Claude Code\)$/.test(evidence.version) && Number(evidence.version.match(/^2\.1\.(\d+)/)?.[1]) >= 281;
  evidence.reason = 'Priming requires --settings and session.Send(model prompt); forbidden by T28. No session opened.';
}
async function cursor() {
  const rpc = new Rpc('powershell.exe', ['-NoProfile', '-File', paths.cursor, 'acp'], true);
  try {
    evidence.initialize = await rpc.request('initialize', acpInit(true));
    evidence.session = await rpc.request('session/new', { cwd, mcpServers: [] });
    evidence.sessionId = evidence.session.sessionId; save();
    await sleep(2500);
    if (rpc.violation) throw Error('Unexpected standby activity: ' + rpc.violation);
    evidence.reason = 'Warm session opened without context or prompt. Deletion must be confirmed using supported local deletion interface.';
  } finally { rpc.stop(); }
}
async function inventory() {
  evidence.installed = Object.fromEntries(Object.entries(paths).map(([k,p]) => [k, { path: p, exists: existsSync(p) }]));
  evidence.notInstalled = {};
  const folders = [...new Set([...(env.PATH ?? '').split(';'), join(home, '.local/bin'), join(home, '.npm-global/bin'), join(home, '.volta/bin'), join(home, '.bun/bin'), join(home, '.local/share/gh/copilot')])];
  for (const [name, bins] of Object.entries({ pi: ['pi'], copilot: ['copilot'], grok: ['grok'], devin: ['devin'], antigravity: ['agy'], commandcode: ['command-code', 'commandcode', 'cmd'] })) {
    const candidates = folders.flatMap(dir => bins.flatMap(bin => ['', '.exe', '.cmd', '.ps1', '.bat'].map(ext => join(dir, bin + ext)))).filter(p => !/\\Windows\\(?:System32|SysWOW64)\\cmd\.exe$/i.test(p));
    evidence.notInstalled[name] = { found: candidates.filter(p => existsSync(p) && statSync(p).isFile()) };
  }
  evidence.status = 'PASS';
}
async function opencode() {
  const child = spawn(paths.opencode, ['serve', '--hostname', '127.0.0.1', '--port', '0'], { cwd, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let logs = '', address, sessionId;
  const found = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(Error('No listening address after 20 seconds')), 20000);
    const read = b => {
      logs += b;
      const match = logs.match(/https?:\/\/(?:127\.0\.0\.1|localhost|\[::1\]):\d+/);
      if (match) { clearTimeout(timer); address = match[0]; resolve(); }
    };
    child.stdout.on('data', read); child.stderr.on('data', read);
    child.on('error', e => { clearTimeout(timer); reject(e); });
    child.on('exit', code => { clearTimeout(timer); reject(Error('Server exited: ' + code)); });
  });
  const http = async (method, path, body) => {
    // All POSTs except create must explicitly request no reply. No generic prompt API exists here.
    if (method === 'POST' && path !== '/session' && body?.noReply !== true) throw Error('Forbidden HTTP model request');
    evidence.requests.push({ method, path, body }); save();
    const result = await fetch(address + path, { method, ...(body ? { body: JSON.stringify(body), headers: { 'Content-Type': 'application/json' } } : {}), signal: AbortSignal.timeout(15000) });
    const text = await result.text();
    return { status: result.status, text, json: (() => { try { return JSON.parse(text); } catch { return null; } })() };
  };
  try {
    await found; evidence.address = address;
    const doc = await http('GET', '/doc');
    evidence.docStatus = doc.status; evidence.docPrefix = doc.text.slice(0, 500);
    if (doc.status !== 200 || !doc.json?.paths) throw Error('Port /doc OpenAPI gate unavailable; no history sent');
    writeFileSync(join(root, 'opencode-openapi.json'), doc.text);
    const operation = Object.entries(doc.json.paths).find(([p]) => p.startsWith('/session/') && p.endsWith('/message'))?.[1]?.post;
    let schema = operation?.requestBody?.content?.['application/json']?.schema;
    if (schema?.$ref) schema = doc.json.components?.schemas?.[schema.$ref.split('/').at(-1)];
    evidence.noReplySchema = schema?.properties?.noReply;
    if (!evidence.noReplySchema || (Object.keys(evidence.noReplySchema).length && evidence.noReplySchema.type !== 'boolean')) throw Error('Port noReply schema gate failed; no history sent');
    const session = await http('POST', '/session', {});
    sessionId = session.json?.id; evidence.sessionId = sessionId;
    if (session.status !== 200 || !sessionId) throw Error('Port create session shape failed');
    const injected = await http('POST', '/session/' + encodeURIComponent(sessionId) + '/message', { noReply: true, parts: [{ type: 'text', text: history }] });
    evidence.injectStatus = injected.status;
    if (injected.status !== 200) throw Error('Port noReply request failed');
    await sleep(2500);
    const messages = await http('GET', '/session/' + encodeURIComponent(sessionId) + '/message');
    evidence.messages = messages.json;
    if (!Array.isArray(messages.json) || !messages.json.some(m => m.parts?.some(p => p.text === history))) throw Error('Read-back failed');
    const acp = new Rpc(paths.opencode, ['acp'], true);
    try {
      evidence.acpInitialize = await acp.request('initialize', acpInit(false));
      if (!evidence.acpInitialize.agentCapabilities?.loadSession) throw Error('ACP does not advertise loadSession');
      acp.replaying = true;
      evidence.acpLoad = await acp.request('session/load', { cwd, mcpServers: [], sessionId });
      acp.replaying = false;
      await sleep(1500);
      if (acp.violation) throw Error('ACP started model activity');
    } finally { acp.stop(); }
    evidence.status = 'PASS';
  } finally {
    if (sessionId) {
      evidence.deletion = await http('DELETE', '/session/' + encodeURIComponent(sessionId)).catch(e => ({ error: e.message }));
      evidence.readAfterDeletion = await http('GET', '/session/' + encodeURIComponent(sessionId)).catch(e => ({ error: e.message }));
      if (evidence.deletion.status !== 200 || evidence.readAfterDeletion.status !== 404) {
        evidence.status = 'BLOCKED'; evidence.error = 'Disposable OpenCode deletion was not confirmed';
      }
    }
    spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true });
    evidence.serverLogHash = hash(logs);
    evidence.serverLogActivity = /model.request|inference|turn.started|tool.call/i.test(logs);
  }
}
async function opencodeWarm() {
  evidence.injectionBlocked = evidence.error; delete evidence.error;
  const rpc = new Rpc(paths.opencode, ['acp'], true);
  try {
    evidence.warmInitialize = await rpc.request('initialize', acpInit(false));
    evidence.warmSession = await rpc.request('session/new', { cwd, mcpServers: [] });
    evidence.sessionId = evidence.warmSession.sessionId;
    if (!evidence.sessionId) throw Error('No ACP session identity');
    await sleep(2500);
    if (rpc.violation) throw Error('Warm standby activity: ' + rpc.violation);
    evidence.warmQuiet = true;
    evidence.reason = 'Injection gate unavailable; warm fallback opened without prompt. Cleanup must be confirmed.';
  } finally { rpc.stop(); }
}
try {
  await ({ codex, claude, cursor, inventory, opencode, 'opencode-warm': opencodeWarm }[service])();
} catch (e) { evidence.status = 'BLOCKED'; evidence.error = e.message; }
finally { evidence.finished = new Date().toISOString(); save(); console.log(JSON.stringify({ service: actualService, status: evidence.status, error: evidence.error, evidence: join(root, actualService + '.json') })); }
