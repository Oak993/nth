// Offline ExtensionAPI stand-in. No CLI/model/network; run with the emitted guard source as argv[2].
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const { default: install } = await import(pathToFileURL(process.argv[2]));
const events = new Map(), commands = new Map(), messages = [];
let tools = ['read', 'bash', 'edit', 'write', 'mcp__server__act'];
let obey = true;
install({
  on: (name, handler) => events.set(name, handler),
  registerCommand: (name, command) => commands.set(name, command),
  setActiveTools: next => { if (obey) tools = next; },
  getActiveTools: () => tools,
  sendMessage: (message, options) => messages.push({ message, options }),
});
await events.get('session_start')();
assert.deepEqual(tools, []);
for (const toolName of ['read', 'write', 'bash', 'edit', 'mcp__server__act', 'unknown'])
  assert.equal((await events.get('tool_call')({ toolName })).block, true);
tools = ['write'];
await events.get('before_agent_start')();
assert.deepEqual(tools, []);
await commands.get('marrow-text-only-proof').handler('nonce');
assert.deepEqual(messages, [{ message: { customType: 'marrow-text-only-proof', content: 'nonce', details: { tools: [] }, display: false }, options: { triggerTurn: false } }]);
tools = ['write']; obey = false;
await assert.rejects(commands.get('marrow-text-only-proof').handler('invalid'), /not applied/);
assert.equal(messages.length, 1);
console.log('Pi guard: empty active tools, all calls blocked, no-turn proof, failed policy refuses proof (PASS)');
