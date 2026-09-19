# STATE

Continuation record. Reread `porting/PORT_CONTRACT.md` before acting on this file.
This file is a navigation aid; the contract and the source are the authority.

Last updated: 2026-09-19, session 1.

## Baseline

| Field | Value |
| --- | --- |
| Pinned source | `dd2b86efef9e77168f304a86e8a8411877578bbe` (Droppy Code `main`, 2026-09-18) |
| Pinned tree | `f761e5ef3745c4f68958ffa4014ab0b618c9d419` |
| App version | 1.6.0, build 19 |
| Reference (read-only) | `E:\Droppy\reference\droppy-code` |
| Port workspace | `E:\Droppy\port`, branch `port/windows-exact-parity` |
| Port commit | none yet (no production code written; reading gate in progress) |

## Current gate

**Gate A, complete-code reading.** Not satisfied. Production application code and stack
selection are blocked until Gate A and Gate B are satisfied (contract sections 5, 6, 9).

## Done

- Upstream reachable; default branch resolved and frozen to a full SHA.
- Full clone at the pinned SHA; no submodules, no LFS, no symlinks, no case collisions,
  `git fsck` clean, working tree matches the pinned tree exactly. Recorded in `porting/baseline.md`.
- Build inclusion rules read from `project.yml`: `DroppyCode/` is a syncedFolder (everything in
  the app target), plus `LICENSE`, `THIRD_PARTY_NOTICES.md`, `TRADEMARK.md` as bundle resources.
- Instruction sources inspected and conflicts recorded in `porting/instruction_conflicts.md`
  (C-01 through C-06). Two matter most: the prior "Marrow" port is pinned to an **older** upstream
  commit and is a **deliberately divergent product**, so it is not the specification.
- Mechanical inventory generated: **433 files, 85,392 text lines**, every file classified with
  build-inclusion evidence. `porting/source_inventory.json`.
- Reading partitioned into **63 nonoverlapping bundles**, mechanically proven to cover all 433
  files with no file in two bundles and no file split across bundles.
  `porting/assignments.json`, `porting/tools/partition.mjs`.
- Read-gate tooling written: `gen_inventory.mjs` (refuses a dirty reference),
  `bundle.mjs` (authoritative per-reader assignment), `ledger_apply.mjs` (rejects a report whose
  sha256 mismatches, whose ranges leave a gap, or whose findings are generic).

## In progress

- Reading gate workflow `wf_a6f89100-da4`: 63 reader agents, then 9 subsystem reconciliation
  critics. Reports land in `porting/reports/<BUNDLE>.json`.
- Lead's own spine reading (contract section 5 requires the lead to personally inspect the
  source for each subsystem it implements; agent reports do not close a file for the lead).

## Not started

- `porting/architecture.md`, `porting/parity_matrix.json`, `porting/ui_contract.json`,
  `porting/platform_adaptations.md` (Gate B).
- Windows architecture decision record (Gate B must complete first; contract section 9 forbids
  choosing a stack before discovery).
- Verification entrypoint and CI checks (contract section 14).
- Any production application code.

## Owner decisions taken (see `porting/owner_decisions.md`)

- **OD-0001** No macOS access at all. UNK-0003 closed as permanently blocked. macOS-delegated
  rendering ships UNVERIFIED and is named individually in the final report.
- **OD-0002** The Windows build ships as **Marrow**. UNK-0002 closed. This is an owner-approved
  deviation limited to product identity, so the final statement can be at best
  "VERIFIED WITH USER-APPROVED DEVIATIONS". It does **not** authorize adopting the separate
  existing Marrow product's features. AGPLv3 section 7 attribution stays intact.
- **OD-0003** Liquid Glass: exact look, zero guesswork. Visual fidelity becomes the primary
  weighted criterion in the stack evaluation; a stack that cannot render a custom material with
  backdrop sampling, blur, specular edge and interactive response is disqualified.
- **OD-0004** Reference material found in-tree: eight real 1320x824 macOS screenshots of the
  running app (`tour-*@2x.png`), plus the app's own capture harness
  (`App/Captures/`, 1740 lines). Approximate oracle only; never a pixel-parity claim.

## Blockers

| ID | What | Blocks |
| --- | --- | --- |
| UNK-0005 | What shell, if any, runs model-issued commands on Windows | PA-0009, PA-0011 decisions |
| UNK-0006 | Command Code's executable is literally `cmd`, colliding with the Windows interpreter | Provider launch; needs executable-identity validation |
| UNK-0007 | SF Symbols (70 distinct, 206 sites) and the macOS system font cannot be redistributed | Icon/type substitution decision; text metrics feed layout |
| UNK-0008 | App icon artwork and in-repo Droppy marks under the Marrow name | Packaging identity |

## Next concrete actions

1. Apply reader reports through `ledger_apply.mjs`; re-dispatch every bundle it rejects.
2. Resolve every critic finding by rereading the real source, not by editing a report.
3. Reconcile inventory coverage to 433/433 READ, then close Gate A with the mechanical check.
4. Derive `architecture.md`, then the parity matrix and UI contract from the ledger plus
   direct source reading.
5. Only then evaluate Windows stacks against the derived requirements.

## Commands actually run (latest)

| Command | Result |
| --- | --- |
| `git ls-remote https://gitlab.com/droppyformac1/droppy-code.git` | `HEAD` == `refs/heads/main` == `dd2b86e...` |
| `git clone ... && git checkout --force dd2b86e` | exit 0; 218 MB; tree matches |
| `git fsck --no-progress` | no output |
| `git status --porcelain` (reference) | empty |
| `node porting/tools/gen_inventory.mjs` | 433 files, 85,392 lines |
| `node porting/tools/partition.mjs` | 63 bundles, 433/433 assigned, no overlap |

## Session 1 addendum (2026-09-19, after the owner supplied getdroppycode.app)

- Reading gate: 44/63 bundles reported, **243/433 files** past the evidence gate, 0 rejections
  since the two tool fixes. Workflow `wf_a6f89100-da4` still running.
- Ledger tooling defects found and fixed (documented in the tools, standard not lowered):
  extensionless LICENSE classified as binary; 0-byte file rejected for having no ranges;
  .gitignore rejected for having no symbols. Inventory repaired in place without resetting reads.
- **Update feed resolved and it is platform-neutral (PA-0014).** `App/UpdateChecker.swift:66-71`
  reads the public GitLab Releases API, project id 86415974, per_page=20, no token. Launch delay
  8s, interval 6h, plus on-activation and on-About. Keys updateChecker_release,
  updateChecker_lastChecked, updateChecker_notifiedVersion. The feed is reused unchanged on
  Windows; only the artifact and the install mechanism differ. UNK-0009 records that no Windows
  release asset exists yet, so the live update path stays BLOCKED rather than faked.
- **Reference captures mirrored with permission:** 24 real macOS renderings, 3,084,116 bytes,
  in `porting/reference_captures/` with `PROVENANCE.tsv` (url, bytes, sha256, fetch time).
  hero is 2400x1500; most are 2080x1300. Approximate oracle only; source wins on disagreement.
- Capture geometry is a SOURCE FACT: `WebsiteCaptures.swift:658-659` wide=1320x860,
  narrow=860x560, scale from backingScaleFactor, even-rounded pixel dims, scalesToFit=false.
  The Windows port mirrors this harness so captures are like-for-like.

## Next concrete actions (updated)

1. Finish the reading gate to 433/433; re-dispatch any rejected bundle.
2. Work every reconciliation critic finding back against real source.
3. Read the capture harness crop rects per capture, to mirror states and fixtures on Windows.
4. Derive architecture.md, parity_matrix.json, ui_contract.json from ledger + direct reading.
5. Only then evaluate Windows stacks, with glass fidelity as the primary weighted criterion (OD-0003).

## Session 1, checkpoint 3: reading gate closed, verification entrypoint live

### Reading gate (contract section 5)

- Workflow `wf_a6f89100-da4` completed: **63/63 bundles, 0 failed**, 72 agents, 10.4M subagent
  tokens, 1615 tool uses, 49 minutes.
- **433/433 files past the evidence gate**, 433 ledger records, 1:1 with the inventory.
- `ledger_apply.mjs` made idempotent after re-runs produced 744 duplicate lines for 433 files;
  the ledger is now keyed by path and rewritten whole. Verified by applying all 63 reports twice.

### Reconciliation (contract section 15)

- 9 independent subsystem critics raised **182 gaps and contradictions**; 8 of 9 families returned
  NEEDS_REREAD. Findings written per family to `porting/critiques/*.md`.
- The critics independently re-verified 239 reported sha256 digests byte-for-byte and confirmed
  every `lines_total` and every range covering line 1 to the last line. The reading was genuine.
- A critic caught an error in THIS register: PA-0011 said the Shell reader set had 47 entries.
  Counted from source: **52**. The transcribed list was complete; the stated count was wrong.
  Corrected in place with a note. gitReaders 25 and excludedKeys 15 were confirmed correct.
- Repair workflow `wf_51997e5b-95e` running: 9 family adjudicators plus 7 verbatim extractions
  (ThreadRuntime ProviderEvent dispatch, Chrome design tokens, Hydra prompt corpus, markdown link
  grammar, provider argv/handshakes, git command vectors, MCP protocol and OAuth assets).
  Every critic finding is adjudicated against source; a critic may be wrong and must be shown wrong.

### Verification entrypoint (contract section 14)

- `node porting/tools/verify.mjs [--json] [--gate A..F]`. Exits nonzero on any unmet mandatory
  check. Gates B through F report NOT_IMPLEMENTED and **fail by design**, never pass by omission.
- Current: Gate A **11 PASS, 1 FAIL** (critic findings not yet adjudicated - the repair pass is
  running). Overall RESULT: NOT ACCEPTABLE, exit 1. This is the correct state.
- `node porting/tools/negative_test.mjs`: six controlled regressions injected and all six detected
  by name (unread file, tampered digest, duplicated record, partial coverage, reused generic
  finding, inventory diverging from the reference). Evidence restored byte-for-byte; gate state
  matches baseline. Exit 0.
- PA-0015 recorded: the negative test hit a real Windows file-lock defect (errno -4094 writing a
  file a just-exited child had read), which damaged evidence until the gate caught it. Restore is
  now retried with backoff. Same defect class the product will hit in git, checkpoint and store
  writes.
