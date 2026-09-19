# Droppy Code Windows Exact-Parity Port Contract

> Preserved verbatim as required by section 3 of the contract itself.
> Source: commissioning owner's instruction, session 2026-09-19.
> Do not edit. Amendments may come only from an explicit later instruction by the
> owner, recorded separately in `porting/contract_amendments.md`.

## 1 Mission and authority

Repository: https://gitlab.com/droppyformac1/droppy-code

I am commissioning a Windows port of my existing macOS application. The deliverable is the same application on Windows, with the same application-owned UI, functionality, features, interactions, state transitions, integrations, and persistent behavior.

This is NOT a redesign, prototype, MVP, inspired recreation, feature subset, or opportunity to improve the product. A working chat window is not a completed port.

Target Windows 11 x64 as an installed Windows desktop application. Development tooling may run elsewhere, but the installed application must not require macOS, WSL, Docker, a development server, or a browser tab. Existing source-defined provider prerequisites remain legitimate prerequisites. Do not add another target architecture or platform to the acceptance scope without my instruction.

Use the repository's remote default-branch HEAD when you begin, unless I explicitly supply another reference. Resolve it to a full commit SHA and freeze that snapshot. Do not silently move the baseline when upstream changes. Do not use an old release, earlier port, repository ancestor, marketing website, or remembered analysis as the specification.

Your implementation freedom concerns internal engineering necessary for Windows compatibility. It does not include changing the product. Every product requirement must be supported by the pinned source or reference behavior, and every necessary platform adaptation must be recorded.

Proceed through the work without routine approval checkpoints. Investigate uncertainties using the available evidence and tools. Ask only for genuinely unavailable access, an external authorization, or a product decision that cannot be resolved from the source. Block only the affected work and continue independent verified work.

## 2 Non-negotiable rules

- No invented features, defaults, interface elements, labels, configuration keys, provider capabilities, protocol fields, or error behavior.
- No implementing from README descriptions, screenshots alone, filenames, snippets, memory, or assumptions about how similar applications work.
- No skipping source files because they look repetitive, old, minor, platform-specific, complicated, or unlikely to matter.
- No replacing a difficult feature with a disabled button, placeholder, mock, canned result, no-op, static screenshot, or external application unless the source itself does that.
- No framework-default appearance where it differs from the original. No modernizing, simplifying, rearranging, or substituting your preferred UI.
- No declaring success because compilation, unit tests, one provider, or one happy-path demonstration works.
- No weakening tests, editing original expectations to match your output, hiding failures, removing requirements, or relabeling missing work as not applicable to obtain a pass.
- No claiming you read, ran, tested, compared, installed, or verified anything without the corresponding evidence.
- No adding MCP, skills, remote access, cloud services, telemetry, accounts, or other capabilities unless they already exist in the pinned source. Preserve any such capability that actually exists.
- No changing the original macOS checkout, live application, user data, credentials, or other agents' worktrees to facilitate the port.

These rules remain binding after context compaction, delegation, a tool failure, a framework change, or a session restart. Your own notes cannot amend them. Only an explicit subsequent instruction from me may change product scope or acceptance criteria.

## 3 Establish durable controls before implementation

Create an isolated port branch/worktree and preserve a pristine reference snapshot. First inspect existing AGENTS.md, CLAUDE.md, nested instructions, build scripts, and repository protections. Preserve unrelated constraints and record conflicts rather than silently ignoring them.

For this task, any repository statement that a build is sufficient verification does not satisfy my acceptance criteria. Additional source-derived tests, visual comparisons, and Windows runtime checks are mandatory. Similarly, restrictions on helper verification must not eliminate independent verification of this port.

I authorize launching isolated reference and Windows test builds for this task, with temporary profiles and disposable test projects. This does not authorize quitting, replacing, installing over, or relaunching my working application. Do not run scripts with those side effects.

Create a concise project CLAUDE.md retaining the non-negotiable rules, pinned baseline, active gate, verification entrypoint, and mandatory reading paths. Preserve this complete instruction verbatim as porting/PORT_CONTRACT.md. Keep detailed ledgers on disk rather than relying on conversation memory. Avoid conflicting duplicated instructions.

Where the installed Claude Code version supports them, configure project-scoped hooks to guard forbidden writes and require relevant verification before completion. Check the installed version and official documentation before writing hook configuration. Test the guards with deliberate harmless failures. Do not assume a configured hook executed successfully.

Do not change global permissions, disable safety controls, or enable permission-bypass modes. Hooks must allow an honest BLOCKED or INCOMPLETE handoff without an endless continuation loop. They must never convert a failed gate into success merely to let a session end.

Also provide a standalone, documented verification command and CI-compatible checks. A chat statement, self-written PASS field, or hook configuration file is not an acceptance gate by itself. Do not claim these controls prove comprehension or are tamper-proof while you have write access to them.

## 4 Source acquisition and baseline integrity gate

Before application scaffolding or stack selection:

1. Record repository URL, resolved default branch, full commit SHA, source tree identity, application version, build configuration, and acquisition method.
2. Obtain the complete checkout at that SHA, including required submodules, actual Git LFS objects, resource bundles, hidden tracked directories, scripts, and dependency lockfiles. Detect missing objects, pointer-only assets, symlinks, and path/case collisions.
3. Inventory the complete pinned Git tree. Reconcile the checkout against it. Record any local modifications separately; never accidentally adopt them into the reference.
4. Inspect build inclusion rules and all configurations. Identify application targets, generated sources, resources, embedded helpers, extensions, plugins, and runtime-loaded files.
5. Preserve the reference without modifying its contents. Use an isolated derived copy for test instrumentation. Record any instrumentation patch and prove it does not change the behavior being measured.

A truncated web view or partial archive is not a complete checkout. Try available authorized clone, API, or archive methods and record failures. Do not invent missing files or claim they are irrelevant. If complete acquisition remains impossible, report precisely what is missing; continue only evidence-backed inventory work, not a guessed port.

Do not restore an older cross-platform ancestor simply because it is easier to build. Reuse is allowed only after independently proving equivalence to the pinned macOS application.

## 5 Complete-code reading gate

Read every first-party source file in the pinned snapshot completely, including hidden support code, embedded scripts, configuration, release tooling, resources expressed as text, and inactive or platform-conditional branches. Inspect binary resources with appropriate viewers and metadata tools. Read relevant documentation and the code of website components that supply app assets or runtime content; do not turn this into a website rewrite.

Create porting/source_inventory.json and porting/read_ledger.jsonl. For every tracked item, record:

- Path, file type, content hash, size, and line count where applicable.
- Classification and build/runtime inclusion, with supporting evidence.
- Exact ranges actually presented and read, reader identity, and read status.
- Purpose, important symbols, callers/callees, state owned, side effects, platform assumptions, and relevant feature IDs.
- Asset inspection result or, for a justified exclusion, the exact reason.

Read long files in bounded, nontruncated ranges and reconcile coverage with the actual file length. Search and symbol indexing are navigation tools, not substitutes for reading complete files. If output is truncated or redirected to an artifact, read the missing content before claiming coverage.

A script opening files, calculating hashes, dumping source into an unread file, or automatically setting read=true does not mean you read them. Record substantive file-specific findings, not repeated generic summaries. Do not fabricate tool transcripts or ledger entries.

Generated and vendored items must still be inventoried. Identify generators, templates, pinned dependencies, local patches, and every dependency boundary used by the app. Do not label authored code generated or third-party to avoid reading it. Pure build caches and unmodified third-party internals may receive a documented non-first-party classification; every first-party file and first-party modification still requires full reading.

A subagent may read explicitly assigned, nonoverlapping ranges, but must return evidence-backed findings, exact coverage, and unresolved questions. The lead must reconcile coverage, verify handoffs, reread important boundaries, and personally inspect the source for each subsystem it implements. An unverified subagent assertion does not close a file.

Complete this whole-repository reading pass before production application code or scaffolding. Repository inspection tools, temporary reference probes, documentation, and verification infrastructure are allowed during this phase. Missing relevant source keeps this gate open.

## 6 Derive the specification from source

Build the application specification before implementing it. Trace every user action through:

entrypoint -> control -> handler -> state transition -> service/provider -> side effect -> persistence -> rendered result

Trace background work and events in the reverse direction too. Follow shared helpers, protocol implementations, defaults, feature flags, compile-time branches, error handling, and cleanup. Do not treat similarly named implementations as interchangeable. Distinguish shipped functionality from debug-only, unreachable, legacy, or disabled code using actual inclusion and reachability evidence. Read and classify that code, but do not activate dormant features during the port.

Create these linked records:

- porting/architecture.md: actual modules, dependencies, initialization, event flows, concurrency, ownership, lifecycle, and shutdown.
- porting/parity_matrix.json: every atomic feature and observable behavior.
- porting/ui_contract.json: every screen, control, state, visual property, and interaction.
- porting/platform_adaptations.md: necessary Windows-specific replacements and their evidence.
- porting/unknowns.md: unresolved facts, attempted investigations, affected requirements, and blockers.

Each parity requirement needs a stable ID, source commit/path/symbol/line evidence, preconditions, action, expected result, side effects, persistence, failure/cancellation cases, Windows implementation path, verification method, artifact references, and status.

Use statuses such as DISCOVERED, SPECIFIED, IMPLEMENTED, VERIFIED, FAILED, BLOCKED, and NOT_APPLICABLE_WITH_EVIDENCE. Implementation is not verification. Not applicable is for genuinely nonproduct material, not inconvenient features. An approved deviation remains visible as a deviation and is not mislabeled exact parity.

Every discovered control, menu action, setting, command, provider, persisted field, background task, and resource must map to a requirement or have an evidenced classification. Avoid one row called "settings" or "providers" that hides dozens of unexamined behaviors.

Source and a matching-build runtime are the authority. README, comments, changelogs, and screenshots help discovery but can be stale. Record disagreements and resolve them. If the available macOS binary differs from the pinned commit, do not treat it as an exact oracle. Build the correct reference or record the verification limitation.

## 7 Droppy-specific discovery checklist

Verify these against the pinned implementation. This list is a discovery aid, never the complete specification and never permission to create an absent feature.

Inspect DroppyCode/Core, DroppyCode/Services, DroppyCode/UI, DroppyCode/App, DroppyCode/Resources, the Xcode project, build manifests, hidden directories such as .commandcode, scripts, release resources, dependency pins, and legal notices. Reconcile changed or missing paths instead of assuming this structure is permanent.

Investigate projects and threads; search; pinning; settling/archive/reopen rules; ordering; sidebar resizing; conversation rendering; reasoning; tool calls; plans and tasks; approvals and questions; permission modes; model and effort selection; cancellation; retry and resume.

Investigate every theme and appearance setting; window chrome; floating panels; onboarding and help; notifications and sounds; command palette; keyboard shortcuts; menus; all settings pages; About, licenses, changelog, and update behavior.

Investigate per-turn changes and checkpoints; diffs and revert; worktrees; project scripts; embedded terminals; commit, push, and pull-request flows; generated titles and commit messages; persistence and migration.

Investigate Hydra fully wherever present: leads, heads, provider/model pairing, effort controls, briefs, parallel workspaces, panels, coordination, merge behavior, failure and cancellation, spend tracking, and metrics.

Investigate every provider registered by the actual code, including any source-defined Codex, Claude, Cursor, OpenCode, Grok, Antigravity, Copilot, Command Code, Pi, DeepSeek, Z.ai, and Meta integrations. Do not implement only the familiar providers. Discover any additional providers from source.

Investigate account discovery, authentication, executable detection, usage limits, credits, reset handling, context indicators, multi-provider displays, and unavailable-provider behavior. Inspect helper extensions and protocol adaptations as carefully as the visible UI.

Follow source references until these flows are understood end to end. Do not import capability assumptions from this checklist or from current provider marketing.

## 8 Resolve uncertainties without guessing

For every unknown, first inspect related source, call sites, resources, tests, dependency implementation, build configuration, and the matching reference runtime. Use official version-specific documentation for operating-system or external-protocol facts that the repository cannot establish.

Distinguish three things:

SOURCE FACT: directly supported by pinned source or matching reference behavior.
ENGINEERING DECISION: a necessary internal implementation choice, justified against the contract.
UNRESOLVED: insufficient evidence, conflicting evidence, or unavailable verification.

Do not convert an inference into a source fact. Do not put invented defaults behind phrases like "standard behavior," "probably," "reasonable approximation," or "most apps do this."

The source may itself contain bugs or awkward interactions. Do not silently repair unrelated behavior during a parity port. Record it. If faithfully reproducing something would expose credentials, destroy data, or create another serious security problem, isolate the issue and seek a decision rather than silently reproducing or redesigning it.

When something is unavailable, record the precise missing evidence and a bounded resolution path. Continue independent work. Never use a blocker as permission to substitute a guessed feature or abandon the remaining unblocked port.

## 9 Select the Windows architecture only after discovery

Do not choose Electron, Tauri, WinUI, Qt, Avalonia, or another stack merely because you know it best or an earlier plan recommended it.

Evaluate credible options against the derived requirements: visual control, material/transparency behavior, text layout, accessibility, native windows and panels, terminal integration, subprocess control, secure storage, provider compatibility, packaging, updates, resource use, and maintainability.

Prefer the architecture with the strongest evidence of meeting the whole contract and the least unnecessary semantic translation. Reuse compatible original logic/assets where possible. Internal rewrites may be necessary, but external behavior must remain traceable to the original.

Create a documented decision record and small throwaway probes for the hardest compatibility risks. A successful probe proves only the tested capability, not the app. Do not let probe limitations redefine the contract.

Preserve original module responsibilities and dependency direction where feasible. A new architecture does not authorize removing features or replacing source-defined workflows. If no investigated option satisfies a specific requirement, document that requirement as blocked rather than choosing an easier product.

## 10 Exact UI and interaction contract

Reproduce the original application-owned interface, not a generic Windows interpretation of it.

Extract actual source-defined dimensions, constraints, spacing, padding, alignment, corner radii, borders, shadows, gradients, opacity, materials, colors, theme tokens, typography, line heights, icon sizing, and animation behavior. Trace shared styles and platform defaults. Where source delegates appearance to macOS, inspect the matching runtime rather than inventing the result.

Preserve labels, punctuation, ordering, defaults, tooltips, context menus, focus traversal, selection, scrolling, text wrapping, resizing, drag behavior, hover/pressed/disabled/focused states, loading and empty states, errors, modal behavior, and window/panel relationships.

Do not flatten translucent material into a solid fill because the framework defaults to one. Do not replace the embedded terminal with an external terminal. Do not reduce multiple panels to one page. Do not use static original screenshots as the functional Windows interface.

Inventory and reuse legally available original assets. Preserve app branding as authorized by me. Do not extract or redistribute restricted platform assets or fonts. Record any licensing or availability obstacle that affects parity instead of silently substituting assets or rebranding the application.

Create reference captures using the pinned macOS build with deterministic fixture data. Record app version/commit, macOS version, logical client dimensions, scale, theme, font environment, state, and capture steps. Include every app-owned screen and distinct interactive state, not just the main chat.

Compare Windows captures at equivalent logical dimensions and state. Test relevant Windows scaling settings, including 100%, 125%, 150%, and 200%, and resizing across the supported range. Match animation phases and control nondeterministic data. Preserve original captures unchanged.

Use image differences, overlays, measured geometry, text/line-wrap checks, and interaction tests together. Define comparison methods and any narrow rasterization tolerances before evaluating the Windows result. Do not hide missing controls or layout errors behind broad masks, loose thresholds, arbitrary rescaling, or a high average similarity score.

Separate application-owned content from genuinely OS-owned surfaces. Record unavoidable differences in window-manager behavior, system dialogs, glyph rasterization, or protected platform materials precisely. These do not authorize unrelated visual differences. Any visible compromise needs my explicit acceptance and must remain identified as a deviation.

Without matching macOS reference captures, source-based UI implementation may proceed, but visual parity remains UNVERIFIED. Never manufacture reference screenshots or call it pixel-perfect from source inspection alone.

## 11 Windows adaptation contract

For each platform-dependent source boundary, record the original API/behavior, Windows replacement, supporting documentation or probe, affected requirements, and verification.

Inspect process spawning, executable resolution, arguments and quoting, environment inheritance, working directories, streaming I/O, encoding, process trees, cancellation, shutdown, terminal capabilities, filesystem semantics, path handling, shell behavior, credential storage, notifications, menus, windows, clipboard, file dialogs, external links, and updates.

Test paths with spaces and non-ASCII characters, drive/UNC paths where supported, case handling, line endings, file locks, executable shims, shell metacharacters, user profiles, missing tools, and clean-machine behavior. Derive product support expectations from the source; additional edge-case tests do not authorize invented workflows.

Do not blindly replace a Unix command name with a Windows one. Preserve intent and observable behavior with a verified Windows implementation. In particular, where a provider uses the command name cmd, establish how to distinguish that provider from the Windows command interpreter and validate the actual executable identity.

Map platform-reserved keyboard conventions deliberately, with a complete original-to-Windows shortcut table and conflict tests. Do not automatically move or restyle app controls merely because Windows applications often look different.

Do not store secrets in plaintext as a convenient substitute for protected storage. Do not bypass permissions, disable transport verification, or broaden process access to make an integration pass.

A provider or dependency without working Windows support remains a blocker unless a verified compatible route preserves the contract. Do not silently remove it or require WSL for the installed app.

## 12 Real functionality and integration parity

For each provider, trace executable discovery, startup arguments, environment, authentication, protocol negotiation, request/event formats, streaming boundaries, session lifecycle, resume semantics, models, effort levels, permissions, tool approvals, questions, cancellation, errors, and usage reporting.

Preserve provider-specific behavior. A single generic adapter must not discard capabilities or collapse distinct events. Do not substitute direct API billing for a source-defined subscription/CLI workflow, or vice versa.

Use source-derived contract fixtures and controlled protocol simulations for deterministic tests, then verify against the real supported Windows executable/service. Record the versions and actual outcomes. Distinguish a test double from a live integration in every report.

Do not fabricate live model replies, approval flows, usage values, credits, or authentication success. Do not change production code to return canned responses when credentials are missing. Missing credentials mean live verification is BLOCKED, not passed.

Request authorization before chargeable calls or external writes not already explicitly authorized. Test destructive Git/revert/merge behavior only in disposable repositories. Use local remotes and approved test accounts where possible; never push or create real pull requests merely to prove a button works.

Verify terminal interaction, input, resize, rendering, process termination, and cleanup. Verify Hydra through actual coordination and merge outcomes, not panel appearances. Verify persistence after restart and cancellation during work, not only within one session.

## 13 Incremental implementation discipline

Once source reading and specification gates are satisfied, implement small complete vertical slices. For each slice:

1. Reopen the original source and its parity requirements.
2. Define source-derived tests or reference fixtures before implementing the behavior.
3. Implement the actual UI, state, service, persistence, and platform boundary involved.
4. Run relevant automated tests and Windows checks available at that stage.
5. Compare the result to the reference, repair differences, and record artifacts.
6. Update the parity matrix honestly and commit the coherent change locally.

No giant speculative rewrite followed by retrospective claims of equivalence. No app-wide static mockup used as a substitute for implementation. Temporary stubs must be visibly tracked as incomplete and removed or replaced before acceptance.

Do not stop after building foundations, the primary chat flow, or the first two providers. Continue until every applicable requirement has been implemented and verified, or until genuine external blockers prevent remaining work.

Keep tests and reference fixtures independent of the Windows implementation. A test that merely repeats the implementation's own assumptions does not establish source parity. If a test is genuinely wrong, document the original-source evidence and exact correction, obtain independent review where available, and retain the change history. Never change an expectation merely because the port fails it.

## 14 Verification and anti-false-completion gates

Create a single documented verification entrypoint using the selected toolchain, callable on Windows and in suitable CI. It must produce nonzero exit status for unmet mandatory checks and an evidence report. Do not invent a command name before creating and running the command.

Gate A: complete pinned source acquired; source tree reconciliation passes; every first-party file fully read; assets inspected; all exclusions justified.

Gate B: source-derived feature/UI contracts are complete; controls, state, persistence, providers, and platform boundaries are mapped; no unresolved specification question is silently implemented.

Gate C: all applicable production features are real; no new fake-success paths, unimplemented handlers, dead controls, hidden omissions, or unapproved product changes.

Gate D: source-derived unit and contract tests, integration tests, UI interaction tests, visual comparisons, persistence/restart tests, failure/cancellation tests, and relevant end-to-end workflows pass with authentic artifacts.

Gate E: the release artifact installs, launches, and runs as an actual Windows desktop application in a clean Windows 11 environment without developer-machine-only dependencies. Verify application closure and child-process cleanup, second launch, upgrade behavior, and uninstall/data-retention behavior. Do not destroy real user data while testing these paths.

Gate F: an independent parity review finds no unresolved required discrepancy; the final report and artifacts correspond to the exact source and Windows commits being delivered.

Mechanically check inventory coverage, requirement links, missing/duplicate IDs, stale hashes, artifact existence, skipped tests, unresolved statuses, and unchanged reference baselines. Combine this with semantic review: a machine-readable manifest alone cannot prove comprehension or fidelity.

Bind verification artifacts to the source SHA, implementation revision, fixture/reference hashes, environment, command, exit code, and capture/run time. Code changes invalidate affected earlier results. Keep reference baselines and acceptance-policy changes auditable; do not edit guard configuration to escape a failure.

Add negative checks proving the gate fails when evidence is missing, a requirement is unverified, or an intentional controlled regression is introduced. Restore the regression after confirming detection. Document limitations of the gate itself.

Do not use arbitrary code-coverage or screenshot-similarity percentages as substitutes for complete product coverage. Report VERIFIED / TOTAL requirements and outstanding IDs; never invent an overall completion percentage.

## 15 Independent review and delegation

Use an independent reviewer/subagent, where available, with the same pinned source and contract but without instructions to approve the implementation. Its job is to find omissions and contradictions, not endorse your summary.

Review all user-facing workflows and high-risk boundaries, including permissions, persistence, subprocesses, terminal behavior, worktrees, Hydra, updates, and provider-specific handling. Recheck source against Windows code and evidence. Sampling alone cannot establish complete parity.

Every finding must identify the source requirement, original behavior, Windows discrepancy, reproduction, and severity. Repair findings and rerun affected checks. Do not accept "looks good" as review evidence.

If independent execution is unavailable, state that limitation and perform a separate adversarial pass. Do not invent a second reviewer or imply that another invocation of yourself constitutes independently validated evidence.

Delegation never transfers responsibility for the whole port. Reconcile all subagent outputs and integration boundaries. Do not allow a helper to narrow scope, choose different UI conventions, or weaken acceptance rules.

## 16 Context continuity and working updates

Maintain porting/STATE.md with the baseline SHA, Windows commit/worktree, current gate, exact completed requirement IDs, remaining work, blockers, last commands and results, and next concrete actions.

Maintain persistent source notes and evidence links. Before context limits, preserve state without compressing away the original contract. At session restart or after compaction, reread the contract, state, current requirement records, and relevant original source. A summary is a navigation aid, not a replacement for source evidence.

Give concise progress reports containing what was actually read, implemented, verified, and blocked. Never imply background work continues after the session ends.

Do not finish with only a plan while implementation is possible. Do not ask me to confirm already-set requirements or select a framework before doing the analysis. If a hard runtime, permission, or session limit prevents continuation, leave a truthful resumable handoff, not a false declaration of completion.

## 17 Packaging and final deliverables

Deliver the Windows source on the isolated branch/worktree, reproducible dependency locks, build/test/package commands, a release installer or packaged artifact, and all required runtime resources. Internal development tools and audit files must not accidentally ship inside the product.

Test the packaged artifact, not only the development build. Record file paths, hashes, build configuration, architecture, toolchain/dependency versions, and the Windows environment used. Reproducible build instructions do not imply byte-for-byte reproducible signed artifacts unless demonstrated.

Preserve source-defined update functionality through a real Windows-appropriate implementation. Do not download a macOS package from the Windows updater, invent a release endpoint, remove update controls, or claim unconfigured publishing is working. Missing endpoints, certificates, or publishing access must remain explicit blockers for those capabilities.

Do not publish, push, merge into main, use signing credentials, or create an external release without separate authorization. A locally built unsigned test artifact must be labeled unsigned; do not call it signed or production-distribution-ready. Absence of a signing certificate does not excuse failing to build and test an unsigned local artifact where permitted.

Deliver the contract, inventory/read ledger, architecture, parity matrix, UI contract, adaptation register, unknowns, state, tests, verification command, test logs, reference/Windows capture pairs, independent review, and final parity report. Keep original licensing and third-party notices intact; flag any unresolved distribution-rights issue without making an unauthorized branding change.

## 18 Final acceptance statement

End with one of these truthful statuses:

VERIFIED EXACT PARITY: all applicable requirements verified against the pinned reference, all mandatory gates pass, and no unapproved or unresolved product deviations remain. Describe documented platform-only adaptations precisely; do not suggest literal pixel identity where the evidence does not establish it.

VERIFIED WITH USER-APPROVED DEVIATIONS: all remaining deviations are explicitly authorized by me and individually listed. This is not an exact-parity claim.

INCOMPLETE / BLOCKED: one or more requirements, runtime checks, reference comparisons, integrations, packaging checks, or necessary authorizations remain unresolved.

Provide the pinned source SHA; Windows commit; delivered artifact and hash; clean-install/runtime result; verification command and authentic result; VERIFIED / TOTAL requirements; every unresolved requirement ID; every approved deviation; and any external prerequisite needed to finish.

Never equate BLOCKED, NOT RUN, SKIPPED, UNVERIFIED, a mock-only pass, or compilation success with acceptance. Do not write "everything works," "fully ported," or "pixel-perfect" without the corresponding evidence.

Begin now with repository acquisition, instruction-conflict inspection, baseline freezing, and complete-source inventory. Then read the complete code and derive the specification before creating the application. Continue through implementation and verification wherever the evidence and environment permit. Do not invent anything to get past a gate.
