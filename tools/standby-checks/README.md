# Real standby checks (T28)

These probes are opt-in and are never invoked by build/test/verification scripts. They use existing CLI sign-ins without opening credentials. The RPC allow-list excludes prompts, turns, compaction, authentication and settings/config APIs. A per-command exclusive attempt file prevents automatic retries. No installer, app launch or config write is included.

The owner's T28 authorization allows one disposable session per installed service. **Do not rerun completed service checks without renewed authorization.** Offline --version, schema generation, and help inspection do not create model turns.

Executed on 2026-10-07 from codex/cli-checks; local evidence root was E:\Droppy\work\scratch\codex-T28. Commands, in order:

```
node tools/standby-checks/check.mjs inventory E:\Droppy\work\scratch\codex-T28
node tools/standby-checks/check.mjs claude E:\Droppy\work\scratch\codex-T28
node tools/standby-checks/check.mjs codex E:\Droppy\work\scratch\codex-T28
node tools/standby-checks/check.mjs opencode E:\Droppy\work\scratch\codex-T28
node tools/standby-checks/check.mjs cursor E:\Droppy\work\scratch\codex-T28
node tools/standby-checks/cleanup-cursor.mjs E:\Droppy\work\scratch\codex-T28\cursor.json
node tools/standby-checks/check.mjs opencode-warm E:\Droppy\work\scratch\codex-T28
node tools/standby-checks/cleanup-opencode.mjs E:\Droppy\work\scratch\codex-T28\opencode.json
```

opencode-warm continues the original failed capability probe only if it created no session. It tests the port's existing warm fallback, not history injection. Cleanup scripts verify ownership before deleting just the generated session. Cursor cleanup requires a metadata-only directory whose cwd matches the disposable project; no recursive deletion. OpenCode uses its own session export/delete commands and confirms absence after deletion.

Claude stops after its version check: the port's Primed standby requires a model prompt and temporary --settings, both forbidden by this packet. Its native identity, effective policy, read-back and restoration remain unverified here.

Codex uses the port's single developer input_text item containing the two synthetic messages. It checks the exact injected text in the rollout and the absence of turn/item/hook/approval events and model activity, then calls thread/delete and confirms rollout absence plus rejected thread/read. The real Desktop userAgent includes a space in the originator; the added fake regression tests reproduce that shape without launching a real CLI. Shared account usage counters were not sampled during the completed real run; the disposable rollout contains no token-count event and both thread snapshots contain no turns.

OpenCode 2.0.24 returned HTML for /doc, so no noReply request was sent. Its warm session export contained zero messages, zero cost and zero tokens. The HTTP injection and ACP load of an HTTP-created session remain BLOCKED.

Attempt/result files are local evidence. Do not remove attempt files to repeat a run. See E:\Droppy\work\scratch\handoff\codex-T28-cli-checks.md for final statuses and offline validation.
