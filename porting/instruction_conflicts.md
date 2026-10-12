# Instruction conflict register (contract section 3)

Every instruction source found that touches this work, and how each conflict with the
PORT_CONTRACT is resolved. Conflicts are recorded, not silently ignored. Where the contract
overrides a repository statement, the repository statement is preserved here so nothing is lost.

## Sources inspected

| Source | Path | Status |
| --- | --- | --- |
| Port contract (authoritative) | `porting/PORT_CONTRACT.md` | Preserved verbatim |
| Upstream agent workflow | `E:\Droppy\reference\droppy-code\AGENTS.md` | Read in full |
| Upstream build manifest | `E:\Droppy\reference\droppy-code\project.yml` | Read in full |
| Upstream ignore rules | `E:\Droppy\reference\droppy-code\.gitignore` | Read in full |
| Upstream hidden agent config | `.commandcode/taste/taste.md` | Read; file is 0 bytes (empty) |
| Prior Windows port instructions | `C:\Users\omera\Downloads\Claude outputs\agent-workbench\CLAUDE.md` | Read in full |
| Prior Windows port state | `agent-workbench\PROJECT_STATE.md`, `docs/audit/UPSTREAM_AUDIT.md` | Read, enough to establish baseline divergence |
| Nested instruction files | searched `CLAUDE.md` / `AGENTS.md` / `.cursorrules` to depth 3 in both trees | Only the two above exist |

No `.claude/` directory, no hooks, and no branch protections exist in either tree.

---

## C-01 Upstream says the build is the only check. Contract says it is not sufficient.

- Upstream `AGENTS.md`: "There is no test target and no test scripts; the build is the check
  (`xcodebuild -project DroppyCode.xcodeproj -scheme DroppyCode -configuration Debug build`)."
- Contract section 3: "any repository statement that a build is sufficient verification does not satisfy my
  acceptance criteria. Additional source-derived tests, visual comparisons, and Windows runtime checks are mandatory."
- **Resolution:** contract wins. The port carries source-derived tests, UI/interaction tests, visual comparison,
  persistence/restart and cancellation tests, and a single verification entrypoint with a nonzero exit on failure.
  The upstream fact remains relevant in one way only: there are **no upstream tests to port**, so every test in
  the Windows port is newly written and must be derived from source behavior, never from the port's own assumptions
  (contract section 13).

## C-02 Upstream forbids helper verification. Contract requires independent verification.

- Upstream `AGENTS.md`: "A head never builds or otherwise verifies: no `xcodebuild`, no `scripts/quick_run.sh`,
  no check command."
- Contract section 3: "restrictions on helper verification must not eliminate independent verification of this port."
  Contract section 15 requires an independent reviewer that is not instructed to approve.
- **Resolution:** contract wins. This upstream rule governs Droppy's own Hydra chat team inside their macOS
  checkout; it is not a rule about how this port is verified. Subagents used here do verify, and an independent
  adversarial review pass is mandatory.

## C-03 Upstream mandates macOS worktree paths and glab squash merges.

- Upstream `AGENTS.md`: never code in `/Users/jordyspruit/Desktop/DroppyCode`; create worktrees under
  `~/.droppy-code/worktrees/agent-<slug>`; merge only via `glab` with `--squash`; never push straight to `main`.
- **Resolution:** partly not applicable, partly honored.
  - Not applicable: the macOS checkout path and the `glab` merge flow. This port is developed on Windows in an
    isolated workspace (`E:\Droppy\port`) and has no GitLab write access, nor authorization to use any.
  - Honored and reinforced: "never delete Jordy's main checkout, never touch another agent's worktree" matches
    contract section 2 ("No changing the original macOS checkout... or other agents' worktrees") and contract
    section 17 (no publish, push or merge without separate authorization). The pristine reference at
    `E:\Droppy\reference\droppy-code` is never written to; the inventory generator aborts if it is dirty.
  - Note: `~/.droppy-code/worktrees` is also a **product** path used by the app's Hydra feature. That is a product
    requirement to be re-derived from the pinned source, and is unrelated to this agent-workflow rule.

## C-04 Upstream release flow requires contributor credits and glab publishing.

- Upstream `AGENTS.md` "Releasing": every shipping CHANGELOG entry carries a `### Thanks` block;
  `scripts/publish_release.sh` runs `scripts/release_credits.py --check` and refuses to publish while anyone is missing.
- **Resolution:** the publishing flow is out of scope without separate authorization (contract section 17).
  The rule is nonetheless product-relevant: the CHANGELOG structure feeds the app's About and update cards
  ("The app's About cards ignore the Thanks heading"), so changelog parsing behavior is a **parity requirement**
  to be derived from source, not merely a process concern. Flagged for the About/update reading assignment.

## C-05 BASELINE MOVED. The prior Windows port is pinned to an older, different upstream commit.

- The prior port in `C:\Users\omera\Downloads\Claude outputs\agent-workbench` records its baseline as Droppy Code
  `e2325eb63acbabcd61b5876691098d08d94e96a4` (2026-09-15), acquired as a GitLab archive ZIP.
- This contract requires the remote default-branch HEAD at start: `dd2b86efef9e77168f304a86e8a8411877578bbe`
  (2026-09-18). Upstream moved in the three days between them, and by a large amount:
  - Prior audit recorded 126 Swift files, about 53,858 lines. The pinned tree has **192 Swift files, 72,471 lines**.
  - Prior audit asserted "no connector manager" and "No unified skills import/discovery/management". The pinned
    tree contains a full MCP subsystem (`Services/MCP/MCPHub.swift`, `MCPStore.swift`, `MCPOAuth.swift`,
    `MCPProxy.swift`, `MCPProbe.swift`, `MCPBridgeSource.swift`, `MCPProviderConfig.swift`,
    `MCPSessionOverrides.swift`, `MCPExternalSync.swift`, `MCPKeychain.swift`, `Core/Models/MCPCatalog.swift`,
    `Core/Models/MCPCustom.swift`, `App/Views/Settings/MCPSettingsPage.swift`, `MCPSetupPanel.swift`,
    `MCPKeyPanel.swift`) and 27 `mcp-*` vendor icons in `Resources/Assets.xcassets`.
  - Prior audit recorded app version 1.2.0. The pinned tree is **1.6.0**, build 19.
  - The pinned tree also contains a Z.ai provider (`ZaiSession.swift`, `ZaiAPI.swift`, `ZaiKeychain.swift`,
    `provider-zai` icon), a tour (`App/Views/Tour/`), a Hydra cookbook, and a token ledger.
- **Resolution:** the prior port's analysis is **stale and must not be used as the specification**
  (contract section 1: "Do not use an old release, earlier port, repository ancestor, marketing website, or
  remembered analysis as the specification"). It may be used only as a navigation hint, and any claim taken from
  it must be re-derived from the pinned source before it enters the parity matrix.

## C-06 PRODUCT CONFLICT. The prior port is a deliberately divergent product; this contract demands exact parity.

- The prior port's `CLAUDE.md` defines "Marrow", described as "a modified, independently maintained Windows port
  of Droppy Code", and records large intentional divergences from upstream: a full rename away from Droppy,
  a "nine features" batch (in-app browser, downloads, document viewer with PDFs, memory, casts, desktop control,
  Chrome handoff, new icon), and "27 improvements drawn from comparing the app with the ChatGPT, Codex and Claude
  desktop apps" (queued-message steering, self-compacting context, crash recovery, folded tool runs, diff preview
  before approval, crew board, backups, storage report, and more).
- Contract section 1: "This is NOT a redesign, prototype, MVP, inspired recreation, feature subset, or opportunity
  to improve the product." Contract section 2: "No invented features... No adding MCP, skills, remote access, cloud
  services, telemetry, accounts, or other capabilities unless they already exist in the pinned source."
- The prior `CLAUDE.md` also states: "Never present the app as Droppy Code and never use Droppy names or icons."
  Contract section 10 states: "Preserve app branding as authorized by me."
- **Resolution (recorded, and surfaced to the owner):**
  - The exact-parity port is built fresh in `E:\Droppy\port`, specified only from the pinned source. The Marrow
    tree is **not** the starting point, because it is a different product by its own definition.
  - Marrow code may be reused **only** where this port proves equivalence to the pinned macOS source first
    (contract section 4: "Reuse is allowed only after independently proving equivalence to the pinned macOS
    application"; section 9: "Reuse compatible original logic/assets where possible"). Every reuse is recorded
    with its equivalence evidence. No Marrow-only feature is carried over.
  - The Marrow repository is not modified by this work.
  - **Branding is an owner decision that cannot be resolved from the source** and is tracked as UNK-0002. It does
    not block reading, specification, or implementation; it blocks final packaging identity only. Until the owner
    decides, the port keeps the pinned source's own product strings and marks internally so that parity is
    measurable, and no shipping artifact is produced under any name without an explicit instruction.
  - **Owner amendment, 2026-10-11 (web surfaces; ED-NEW-web-w10-8):** the owner approved the in-app browser (Visus) and the artifacts panel
    (Tabula). "2026-10-11 owner: in-app browser approved; PR-MCP-139 superseded for chat links (setting), kept for OAuth." Links from chats open
    in Visus or the default browser as Settings > Visus > "Open links from chats in" says (Ask the first time by default; Ctrl+click opens the
    other one). MCP OAuth sign-in (`MCPOAuth.cs:549`), nth's own account sign-ins, update and store links, `mailto:` and other non-web schemes
    always open in the system browser, so the reason PR-MCP-139 protected (the user's own session and cookie context) is kept.
