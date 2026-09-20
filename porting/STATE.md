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

---

# GATE A CLOSED (2026-09-19)

`node porting/tools/verify.mjs --gate A` -> **12/12 PASS**.
Full run -> PASS=12, NOT_IMPLEMENTED=5, exit 1, RESULT: NOT ACCEPTABLE. Correct: Gates B-F are
not built and fail by design.

Port commits: `9f66288` (Gate A evidence), `ddfff51` (line-ending pin).

## What Gate A rests on

| Evidence | Value |
| --- | --- |
| Files read with evidence | 433/433 |
| Ledger records, 1:1 with inventory | 433 |
| Digests re-verified against the pinned reference | 433 |
| Critic findings raised / adjudicated | 182 / 258 |
| Verdicts | 222 CRITIC_CORRECT, 41 PARTIALLY_CORRECT, 1 CRITIC_WRONG |
| Verbatim extracts | 7 files, 520 KB |
| Upstream defects to reproduce | 99 |
| Dead/unreachable items with proof | 60 |
| Port impacts | 264 |
| Platform adaptations | 15 |
| Unknowns | 11 |
| Negative checks detected / injected | 6 / 6 |

## Corrections made to this port`s own records

1. PA-0011 said the Shell reader set had 47 entries. Source has **52**. Caught by an independent
   critic, verified by counting, corrected in place with a note.
2. The git extract said 19 sites at `timeout: 300`, then self-corrected to 12 mid-sentence.
   Counted from source: **18 total, 12 git, 6 forge**. Recorded in the defects register.
3. Upstream defect 22 implied the PKCE/CSRF token generation was cryptographically weak. It is
   not: Swift`s default RNG is CSPRNG-backed on Apple platforms. Reframed as a **port hazard**
   (the byte counts 32 and 24 and base64url are parity; the RNG choice is the port`s and must be
   a platform CSPRNG on Windows).

## Escalated rather than reproduced

- **UNK-0010**: deleting a custom MCP server runs on one click with no confirmation
  (`MCPSettingsPage.swift:564-566`) and also deletes the server`s Keychain secrets
  (`MCPStore.swift:124`, `:193-199`), ignoring `confirmBeforeDeleting` which defaults true and is
  honored by six other destructive paths. Reproducing it destroys credentials the user cannot
  recover; fixing it silently is a product change. Contract section 8 requires escalation, so this
  control is deferred until the owner decides.

## Next: Gate B

1. Derive `porting/architecture.md` from the ledger, the 7 extracts and direct reading.
2. Build `porting/parity_matrix.json`: every atomic behavior with a stable ID, source evidence,
   preconditions, action, expected result, side effects, persistence, failure and cancellation,
   Windows path, verification method, status.
3. Build `porting/ui_contract.json`: every screen, control, state, visual property, interaction,
   seeded from the Chrome token extract, the reference captures and the UI-layer ledger records.
4. Implement the Gate B checks in verify.mjs so completeness is machine-checked, not asserted.
5. Only then the Windows stack evaluation, with Liquid Glass fidelity primary (OD-0003).

---

# GATE B PASSES (2026-09-20)

node porting/tools/verify.mjs -> Gate A 12/12, Gate B 8/8, 21 checks PASS,
Gates C-F NOT_IMPLEMENTED and failing by design. Exit 1, RESULT: NOT ACCEPTABLE. Correct.

Port commits: 4cf7e30 (Gate B tooling), d0d2086 (defect triage + architecture),
9875483 (BLK-0005), 418651b (Gate B passes).

## Specification produced

| Artifact | Value |
| --- | --- |
| Parity requirements | 3827 (3742 SPECIFIED, 85 DISCOVERED) |
| UI contract records | 1029 |
| Areas | 20 |
| Controls enumerated by exact label | 1433 |
| Persisted fields | 570 |
| Background tasks | 294 |
| Architecture record | 63 KB, 29 modules, 194 source citations |
| Layering violations found vs AGENTS.md | 8 |
| Verbatim extracts | 7 files, 520 KB |

## Quarantine (owner decides individually, neither option approved)

| Entry | Criteria | Behaviour |
| --- | --- | --- |
| BLK-0001 | C1,C2,C5 | custom MCP delete: unconfirmed, destroys Keychain secrets |
| BLK-0002 | C4 | malformed custom-MCP variable discards every server, next save overwrites |
| BLK-0003 | C1,C4 | checkpoint truncation by cookie timeout OR forgery; revert then deletes work |
| BLK-0004 | C1,C4 | previewRestore path mixing; revert deletes wrong files, reports success |
| BLK-0005 | C1,C4 | legacy migration folder literal vs AppInfo.name under the rename |
| BLK-0006 | C2,C3 | API key written to defaults in PLAIN TEXT when protected storage refuses |
| BLK-0007 | C2,C3,C6 | plan limits read other apps credential files; Pi transmits the token |
| BLK-0008 | C3,C4 | read-only classifier is ALSO the auto-approval gate; 3 misclassifications |
| BLK-0009 | C1,C5 | Remove project unconfirmed, ignores confirmBeforeDeleting |
| BLK-0010 | C1,C4 | undecodable library.json starts empty, first save overwrites |
| BLK-0011 | C1 | unreadable hasCustomTitle lets auto-titler overwrite a typed name |
| BLK-0012 | C2 | credential deletion on disconnect discards its result |

27 requirements held at DISCOVERED across these. 19 marked by hand after two rounds of
over-broad automatic matching were caught and reverted (76 then 32, both wrong).

## Decisions

- 57 engineering decisions taken and recorded in porting/engineering_decisions.md
- 27 owner decisions still needed, in porting/owner_decisions_pending.md
- 78 spec questions resolved from source

## Next: Windows architecture selection (contract section 9)

Workflow wf_468d7feb-688 evaluating 8 candidate stacks against the 12 derived constraints, with
Liquid Glass fidelity as the primary weighted criterion per OD-0003, then an adversary pass
against the leaders. No stack chosen yet. No Windows code written yet.

Machine constraint found: .NET 10 RUNTIME only (no SDK), no MSVC, no cmake, no Rust. Node 24,
Python 3.14 and winget are present. Toolchain will be installed only for the chosen stack, after
the decision record, rather than speculatively.
