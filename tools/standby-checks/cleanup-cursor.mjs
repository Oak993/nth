// T28: remove ONLY this probe's empty ACP session metadata after its process exits.
import { readFileSync, readdirSync, unlinkSync, rmdirSync, existsSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
const evidencePath = resolve(process.argv[2]);
const proof = JSON.parse(readFileSync(evidencePath, 'utf8'));
if (proof.service !== 'cursor' || !/^[0-9a-f-]{36}$/.test(proof.sessionId)) throw Error('No probe session identity');
const base = resolve(process.env.USERPROFILE, '.cursor/acp-sessions');
const folder = resolve(base, proof.sessionId);
if (!folder.startsWith(base + '\\')) throw Error('Session escapes ACP session root');
const names = readdirSync(folder);
const meta = JSON.parse(readFileSync(join(folder, 'meta.json'), 'utf8'));
if (resolve(meta.cwd) !== resolve(proof.cwd) || names.length !== 1 || names[0] !== 'meta.json') throw Error('Session is not this probe metadata-only directory');
proof.sessionFilesBeforeDelete = names;
proof.sessionMetadata = meta;
proof.persistedModelRollout = false;
// No recursive delete: an unexpected file causes the guarded cleanup to stop.
unlinkSync(join(folder, 'meta.json')); rmdirSync(folder);
proof.sessionDeleted = !existsSync(folder);
const activity = proof.notifications.filter(n => ['agent_message_chunk', 'agent_thought_chunk', 'tool_call', 'tool_call_update'].includes(n.params?.update?.sessionUpdate));
proof.activityEvents = activity.length;
if (proof.sessionDeleted && activity.length === 0 && proof.cliStderrActivityLines.length === 0) {
  proof.status = 'PASS'; proof.reason = 'Warm ACP session, zero prompts, only meta.json persisted (no model rollout); guarded metadata deletion confirmed.';
}
writeFileSync(evidencePath, JSON.stringify(proof, null, 2) + '\n');
console.log(JSON.stringify({ status: proof.status, sessionDeleted: proof.sessionDeleted, folder }));
