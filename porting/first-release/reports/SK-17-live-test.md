# SK-17 supervised local CLI evidence

Date: 2026-10-05, owner's Windows PC. Worktree: `E:\Droppy\wt\cx\sk-17-live`; branch: `codex/sk-17-live`; base: `origin/claude/verify-terminal-20261001` at `18bb508c7fe0f17a3682f8a52380d5db54846188`. Authorization: lead T12 packet and OD-0099 Q-SKILLS-8 A. No Marrow app was run. These are live account calls, separate from the offline tests.

## Verdicts

| CLI / mechanism | Implicit check | Named check | SK-17 verdict |
| --- | --- | --- | --- |
| Antigravity 1.2.17, second `--add-dir` | PASS: read the correct `.agents/skills/marrow-sk17-probe/SKILL.md`; response `MARROW-SK17-OK`; exit 0 | PASS: same read and marker; exit 0 | Eligible for automatic skills through a Marrow-owned added workspace; no plugin installation or global settings edit required |
| Cursor 2026.10.01-e373342, `--plugin-dir`, print mode | PASS: read `plugin/cursor/skills/marrow-sk17-probe/SKILL.md`; marker in final response; exit 0 | PASS: same read and marker; exit 0 | Print-mode success does not establish the port's ACP route |
| Cursor, `--plugin-dir`, ACP | FAIL: probe absent from `available_commands_update`, no probe read, final reply `Not ready.` | The print-mode named check above passed; no additional named flag/ACP model call was spent after discovery failed | Automatic overlay is not proven for Marrow's production transport |
| Cursor, documented `~/.cursor/plugins/local/marrow-sk17`, ACP | FAIL: probe absent from command catalogue; marker came from reading nearby `evidence/plugin-layout.json`, not from skill discovery | FAIL as provider loading evidence: no probe command; reads tried other, incorrect paths; marker alone is not evidence that the local plugin loaded | Keep Cursor explicit-only per BUILD-PLAN. Do not add a flag that the port's live ACP check did not demonstrate |

ACP is long-lived: the probe closed stdin after `session/prompt` returned `stopReason: end_turn`; the CLI remained alive for the 10 s cleanup bound, so the client killed only its own child tree. Recorded process exit is -1, not 0. All ACP turns completed; none demonstrates automatic discovery of the probe. Permission requests for unrelated execution were denied. Other provider-owned files and nearby scratch evidence appeared in ACP searches: these outputs are excluded from the pushed transcript, with the original private transcript's SHA-256 retained in each summary.

## Documentation and layout

SOURCE FACT (installed help): `agent --help` lists repeatable `--plugin-dir <path>` and `--add-dir <path>`; `agy --help` lists repeatable `--add-dir`. This supersedes the plan's older assertion that Cursor has no flag. The live ACP results limit where that new flag can be used.

Official documentation read on the test date: [Cursor CLI parameters](https://cursor.com/docs/cli/reference/parameters), [Cursor plugins](https://prod.cursor.com/docs/plugins), [Antigravity skills](https://www.antigravity.google/docs/skills). Cursor documents a `.cursor-plugin/plugin.json` manifest and `skills/<name>/SKILL.md`, and a local plugin folder. Antigravity documents workspace `.agents/skills/<name>/SKILL.md` and plugin install support. Its first, configuration-free route passed, so `agy plugin install` was not needed or tried.

Scratch root: `E:\Droppy\work\scratch\codex-sk17\plugin\`. One logical harmless skill was copied into the two provider layouts:

```
plugin/cursor/.cursor-plugin/plugin.json
plugin/cursor/skills/marrow-sk17-probe/SKILL.md
plugin/agy/.agents/skills/marrow-sk17-probe/SKILL.md
```

Manifest: `{"name":"marrow-sk17","version":"1.0.0","description":"Harmless Marrow SK-17 live probe"}`. Exact skill text and its hash are in `SK-17-live-test/plugin-layout.json`. The marker appears only in the body, not the name, description, or prompts. Implicit prompt: `Is the harbor lantern ready? Give only the readiness answer.` Named prompt: `Use the marrow-sk17-probe skill: is the harbor lantern ready? Give only the readiness answer.`

## Commands and outputs

Versions were read with these exact commands (both exit 0):

```powershell
& 'C:\Users\omera\AppData\Local\cursor-agent\agent.cmd' --version
# 2026.10.01-e373342
& 'C:\Users\omera\AppData\Local\agy\bin\agy.exe' --version
# 1.2.17
```

The live driver used ProcessStartInfo.ArgumentList, captured stdout/stderr and exit code, imposed a 120 s bound, and removed API-key variables from the child so existing CLI sign-ins were used. All exact argv, timestamps, stdout, stderr and exits for the four print calls are in the adjacent `*-implicit.json` and `*-named.json` files. The commands are equivalent to:

```powershell
& 'C:\Users\omera\AppData\Local\agy\bin\agy.exe' --print '<prompt>' --output-format stream-json --mode plan --print-timeout 90s --add-dir 'E:\Droppy\work\scratch\codex-sk17\workspace' --add-dir 'E:\Droppy\work\scratch\codex-sk17\plugin\agy'
& 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -NoProfile -File 'C:\Users\omera\AppData\Local\cursor-agent\agent.ps1' --print --output-format stream-json --mode ask --trust --workspace 'E:\Droppy\work\scratch\codex-sk17\workspace' --plugin-dir 'E:\Droppy\work\scratch\codex-sk17\plugin\cursor' '<prompt>'
```

Cursor's initial print attempt without `--trust` exited 1 before a model turn with `Workspace Trust Required`; the exact output is in `cursor-trust-preflight.json`. `--trust` was used only for the disposable workspace. It created one `.workspace-trusted` marker, recorded and removed after the print calls. No trust or bypass flag is added to production code.

ACP argv was the same PowerShell/script prefix followed by `--plugin-dir <scratch cursor plugin> acp` for the flag mechanism, or just `acp` for the local-folder mechanism. `initialize` used protocol 1, filesystem client capabilities false, terminal false; `session/new` used the disposable cwd and no MCP servers. Each mechanism sent one tiny `session/prompt` per check. Exact request frames and agent replies are in the adjacent ACP summaries. The raw private transcripts remain under `E:\Droppy\work\scratch\codex-sk17\evidence\`.

## Cleanup and global configuration

Both before/after SHA-256 pairs match:

| Global config | Before and after |
| --- | --- |
| `C:\Users\omera\.cursor\cli-config.json` | `49B64B4A6B8E48D50F21E8FA4B3B55AF02F19FCEA963645E00363B9774753CBF` |
| `C:\Users\omera\.gemini\antigravity-cli\settings.json` | `61366B0A4528865384D530C4CA014EC35F73DB4D192CA68CC58D8F60563CD4AE` |

The snapshot enumerated settings/config/mcp/plugin configuration filenames beneath the two provider roots, excluding histories, cache, projects and artifacts. These were the only existing matching config files. No credential values were inspected or changed. Provider transcripts and normal CLI state/cache files may be created by the CLIs; those are not configuration and were left alone.

The scratch plugin folder and the temporary `~/.cursor/plugins/local/marrow-sk17` were removed. The one newly created workspace trust marker was removed; existing trust markers were preserved. `cleanup-first.json` and `cleanup.json` prove removal but show that Cursor's config initially changed: the CLI added `selectedModel`, `modelSelectionHistory`, `model` and `modelParameters` during the first print call. My initial chat statement that both hashes matched was a mistaken reading of a truncated table and is withdrawn. The exact original bytes were recovered by removing only those added keys from the canonical serialized JSON: their SHA-256 exactly matched the pre-test snapshot. The changed file was backed up privately, its current hash was checked against the observed post-test hash, then the verified original bytes were restored. `config-restored.json` proves final byte identity; Antigravity's settings hash matched throughout. No private config copy is pushed. Automatic approval review rejected the first recursive cleanup command without a detailed reason; cleanup succeeded by removing only the recorded files and then empty directories, with absolute-path and reparse-point checks.

## Implementation, tests and open items

ENGINEERING DECISION: agy generations place automatically eligible skills in `.agents/skills/<name>`; explicit-only skills stay outside that directory and remain readable through `Generation.Invoke`. Only skills are copied: vendor hooks and MCP configuration remain excluded. `SessionConfiguration.SkillGeneration` carries the immutable SK-9 generation lease supplied and retained by the launch owner. `AntigravitySession.Start` verifies that generation, checks it belongs to agy, and appends its folder as a second `--add-dir`, on both new and resumed sessions. No generation leaves the original arguments unchanged. A damaged generation is refused before CLI startup.

Cursor's ACP session code and automatic discovery policy remain unchanged, per the failed production-mode checks. Its generated skill files remain accessible for Marrow-side named use.

Tests were written before implementation. Initial red run: 12 cases, 10 failed, 2 passed, 0 skipped; build had 0 warnings/errors. The initial Cursor overlay expectations used the print-mode result. Those expectations were withdrawn **before any Cursor implementation** when the stronger live ACP evidence disproved their premise, and replaced by explicit-only/unchanged-argv regression tests. No expectation was changed to accommodate newly implemented Cursor behavior: no Cursor behavior was implemented. The initial tests are preserved in commit history.

The first post-implementation build passed with 0 warnings/errors; the focused run had 8 passed, 2 failed, 0 skipped of 10. Each Cursor failure reproduced twice alone through `one-full.sh`: `Storage.Locations is not bound`. This was a fixture omission in the new tests, repaired by binding the existing isolated `StorageScratchFolder`; no production code or argv assertion changed. The final build passed with 0 warnings/errors; focused tests passed 10/10, 0 failed/skipped. The affected regression results are recorded below. All raw runs, including initial failures and four isolated logs, are retained adjacent to this report.

The final tests are `SkillOverlayLayoutTests` (5 cases: agy layout, explicit-only confinement, Cursor named-use layout, agy/Cursor hook and MCP exclusion) and `SkillOverlaySessionTests` (5 cases: agy new/resumed launch, Cursor with/without a generation, and damaged-generation refusal). Affected regression selection passed **464/464, 0 failed, 0 skipped**, covering `Port.Skills.Generations`, `AntigravitySession`, `ACPSession`, `SkillOverlay`, and `TraceabilityTests`; exact filter: `(FullyQualifiedName~Port.Skills.Generations|FullyQualifiedName~AntigravitySession|FullyQualifiedName~ACPSession|FullyQualifiedName~SkillOverlay|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native`. There are no remaining failures in the tested scope. This is scoped evidence, not a full-suite gate.

Tests use existing compiled offline agy/ACP stand-ins, isolated `MARROW_PORT_DATA_DIR`, and fake store protection. No test invokes a real CLI or account. Runs use Git Bash, the unchanged `heavy.sh`, `dotnet build tests/Marrow.Services.Tests -v q --nologo`, then `one-names-long.sh` with `FullyQualifiedName!~Native`. No self-mutation tests or native tests were run.

Open integration item: this required staging ref does not contain the Claude/Codex launch wiring the packet describes, and `ThreadRuntime.EnsureSession` does not yet supply any generation to SessionConfiguration. SK-17 supplies and tests the provider endpoint; the shared generation preparation, lease lifetime, per-turn verification and Settings/resume wiring need the lead's SK-11/SK-16 integration. No fabricated metadata or budget was introduced to substitute for that missing composition. A live persistent agy session launched from Marrow has not been run.

`node porting/tools/verify.mjs --gate A --json` and `--gate B --json` exited 0. `node porting/tools/mark.mjs SK-17 DONE` exited 2: `no task SK-17 in the queue`. Therefore SK-17 is not mechanically DONE. The legacy Gate D command was not used to bypass the required scoped `one*.sh` workflow.

Worktree collision: another concurrent task switched the original `v-cz7a` checkout to `codex/trial-h` while this run was active. The initial test commit landed there as `777b2996e4e77b9120109dac4076955d2b53b5cb`. It was preserved and copied onto the dedicated `codex/sk-17-live` worktree. Our three original uncommitted agy edits and helper file were preserved there too; unrelated `tests/fixtures/dev-tools/` was untouched. Only the named SK-17 branch is pushed. The lead must account for the preserved test commit/partial edits in that other task's checkout before landing it.

Verdict: **NOT READY** for full integration/mechanical DONE. Live decision is settled for the tested versions: **Antigravity automatic route PASS; Cursor ACP automatic routes FAIL, explicit-only fallback.**
