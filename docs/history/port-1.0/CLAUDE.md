# Droppy Code Windows port

> Orchestrated since 2026-09-22 by Claude (see `porting/STATE.md`, top checkpoint). Implementation agents
> follow `porting/IMPLEMENTING.md` (conventions) and the contract; the old Codex/Marrow worker setup in
> `HANDOFF.md` and `WORKER_BRIEF.md` is historical.

Exact-parity Windows 11 x64 port of the macOS app Droppy Code. Read
`porting/PORT_CONTRACT.md` and `porting/contract_amendments.md` before doing anything; they are the
authority and override this file. This file is the short index, not a substitute.

## Pinned baseline (B3, frozen; owner decision OD-0024, amendment AM-0003)

- Repository `https://gitlab.com/droppyformac1/droppy-code`, branch `main`.
- Commit `52bc7926097cd622d08d6294e0d8b08a48c7516e`, tree `115745914ccc103dfe4e74d96c6c539faab2eedb`.
- App version 1.7.0, build 21, bundle id `iordv.droppycode`.
- Pristine reference: `E:\Droppy\reference\droppy-code-v1.7.0`. **Never write to it.**
- Earlier pins are history only; never implement from them: B2 (1.6.2, `1fbf81e6`,
  `E:\Droppy\reference\droppy-code-v1.6.2`, archived reading record `porting/baselines/B2/`) and B1 (1.6.0, `dd2b86ef`,
  `E:\Droppy\reference\droppy-code`). Code citations to B2 lines in files whose Swift changed are re-pointed as those
  units are re-verified (AM-0003). The integration branch keeps its name `impl/b2`.
- Port workspace: `E:\Droppy\port`, branch `port/windows-exact-parity`. Record: `porting/baseline.json`.
- Product identity on Windows: "Marrow"; install and data identity "Marrow Code"
  (`%LOCALAPPDATA%\Marrow Code`), so it never touches the owner's separate Electron app "Marrow".
- Owner rulings in force: every entry in `porting/owner_decisions.md` and every `deviation` field
  in the spec. They are the only permitted differences from the source.

## The rules that do not bend

- No invented features, defaults, UI elements, labels, config keys, provider capabilities,
  protocol fields, or error behavior. Everything traces to the pinned source.
- Never implement from a README, a screenshot, a filename, a snippet, memory, or another app.
- Never skip a source file for being repetitive, minor, platform-specific, or hard.
- Never replace a hard feature with a stub, mock, canned result, no-op, or external app
  unless the pinned source itself does that. Stubs must be visibly tracked as incomplete.
- Never weaken a test, edit an expectation to match output, or relabel missing work as
  not applicable to get a pass.
- Never claim something was read, run, tested, compared, installed, or verified without
  the artifact that proves it.
- Compilation is not verification. Implementation is not verification.
- Do not modify the macOS reference, the owner's installed app, user data, credentials,
  or the separate Marrow tree at `C:\Users\omera\Downloads\Claude outputs\agent-workbench`.
- Do not publish, push, merge, sign, or release without separate explicit authorization.

## Two conflicts you must know about

- The Marrow tree at `...\agent-workbench` is a **different, deliberately divergent product**
  pinned to an **older upstream commit** (`e2325eb6`, version 1.2.0). Its analysis is stale:
  upstream has since grown from 126 to 192 Swift files and gained a whole MCP subsystem.
  Do not use it as the specification. Reuse its code only with proven equivalence, recorded.
- Upstream `AGENTS.md` says "the build is the check" and forbids helper verification. The
  contract overrides both for this port. See `porting/instruction_conflicts.md` (C-01, C-02, C-05, C-06).

## Mandatory reading paths (contract section 5)

Everything tracked at the pinned SHA. 433 files, 85,392 text lines. In the app target:
`DroppyCode/Core`, `DroppyCode/Services`, `DroppyCode/UI`, `DroppyCode/App`,
`DroppyCode/Resources`, plus `LICENSE`, `THIRD_PARTY_NOTICES.md`, `TRADEMARK.md`.
Outside the app target but still first-party and still required: `project.yml`,
`DroppyCode.xcodeproj/`, `scripts/`, `release/`, `website/`, `docs/`, `.commandcode/`,
and the repo-root files.

Upstream layering (from `AGENTS.md`, confirmed against the tree): `Core` depends on nothing
in the app; `Services` and `UI` depend only on `Core`; `App` depends on everything.

## Ledgers on disk, not in conversation memory

| File | Holds |
| --- | --- |
| `porting/PORT_CONTRACT.md` | The contract, verbatim |
| `porting/baseline.md` | Baseline identity and completeness checks |
| `porting/instruction_conflicts.md` | Every instruction source and conflict resolution |
| `porting/source_inventory.json` | Mechanical inventory, read status per file |
| `porting/read_ledger.jsonl` | Evidence-bearing per-file reading records |
| `porting/architecture.md` | Real modules, flows, ownership, lifecycle |
| `porting/parity_matrix.json` | Every atomic feature and observable behavior |
| `porting/ui_contract.json` | Every screen, control, state, visual property, interaction |
| `porting/platform_adaptations.md` | macOS boundary to Windows replacement, with evidence |
| `porting/unknowns.md` | Unresolved facts, blockers, owner decisions |
| `porting/STATE.md` | Current gate, done IDs, remaining work, next actions |

`read_status` in the inventory is only ever advanced by `porting/tools/ledger_apply.mjs`
from a reader report that carries findings. A hash-and-set script does not count as reading.

## Current gate

See `porting/STATE.md` (its top checkpoint is current). Implementation runs unit by unit under
`porting/IMPLEMENTING.md`: tests first, adversarial review, then code.

## Implementation runs (how a unit agent is set up)

The orchestrator session runs workflows that hand each unit to agents. A unit agent works in its own git worktree
under `E:\Droppy\wt\u\` on a branch `unit/<unit id>`, created from the integration worktree `E:\Droppy\wt\integration`
(branch `impl/b2`); its session starts elsewhere, so it changes into that worktree first. Its context is a work packet
under `E:\Droppy\work\packets\<unit id>\part<n>.md` (the records, the Swift source, the rules that apply, the decisions
that name the file). Only merge agents write to `impl/b2`. Agents never push, and never touch the reference checkout,
the installed apps or their data folders.

## Label every claim

SOURCE FACT (pinned source or matching reference runtime) / ENGINEERING DECISION (internal
choice, justified) / UNRESOLVED (insufficient or conflicting evidence). Never promote an
inference to a source fact. "Probably", "standard behavior", "reasonable approximation" and
"most apps do this" are not evidence.
