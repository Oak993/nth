# Lane B: terminal drawer

Status: IN PROGRESS. Resumed existing uncommitted TerminalHost, TerminalPanel, and test changes on `codex/terminal`; preserving and verifying them in place.

Source: pinned Droppy Code v1.7.0 `TerminalPanel.swift` and `TerminalStore.swift`. Independent verifier: `E:\Droppy\work\codex\verify-b21-terminal.md`.

## Decisions and unresolved work

- ED-046 explicitly calls foreground process-descendant detection a deviation. PR-TERM-031 remains UNRESOLVED as exact parity until the owner accepts that deviation; tests can still prove the implemented approximation.
- ED-048: inspected the pinned SwiftTerm 1.20.0 copy in `_evidence/SwiftTerm-v1.20.0` (untracked). `Terminal.swift:760-767` installs `Color.terminalAppColors`; `Colors.swift:96-114` has its 16 RGB triples. `AppleTerminalView.swift:410-438` measures W glyph advance and ascent+descent+leading, snapped to the backing pixel grid; `MacTerminalView.swift:1267-1271` updates the size on frame changes, and `Buffer.swift:1533-1543` reflows. `LocalProcess.swift:70,521-523,561-586` shows shellPid publication and terminate's SIGTERM path. The Windows surface still lacks SwiftTerm's VT screen semantics and must not be called complete.
- Source-required behavior: `TerminalPanel.swift:191-318` re-parents one long-lived terminal and coalesces live resize to 50 ms while normal frame changes resize directly; `TerminalStore.swift:21-23,47-57` sets 640×240, preferred font, Option-as-Meta, theme foreground/background, accent caret and selection. SwiftTerm's `MacTerminalView.swift:1875-1931` gates bracketed paste and `:1539-1545` encodes Option as Meta. These are required by the chosen pinned dependency even though the app does not restate its parser rules.
- UNRESOLVED owner question: does OD-0008/ED-046's Windows process-descendant approximation count as an accepted deviation for PR-TERM-031? The engineering decision calls it a deviation, and the independent verifier says the owner has not accepted it. Do not count PR-TERM-031 as parity before that answer.

## Work and evidence

- Defect 1 (ordinary layout resize) is fixed in `TerminalHostContainer.ArrangeOverride`: it calls `TerminalHostView.ResizeTo` after the real arranged size changes. `TerminalHostView.MeasureGrid` uses Avalonia font measurement of W and the line height instead of `0.6/1.25` constants. `TerminalPanelHighRiskTests.A_layout_change_outside_live_resize_updates_the_real_ConPTY_grid` drives a real ConPTY PowerShell child from 640×240 to 1000×600 and checks the child's reported columns/rows, the measured grid, and the font change.
- Mutation evidence for that one defect: ordinary-layout `ResizeTo(target)` changed to `ResizeTo(640×240)` => same Req-tagged test red twice (1 failed each); `launcher.ResizePseudoConsole(next)` removed => red twice (1 failed each); measured width changed to `fontSize * 0.6` => red twice (1 failed each). Every source file restored byte-for-byte by SHA256. Restored build: 0 warnings / 0 errors; restored `FullyQualifiedName~TerminalPanel`: 31 passed / 0 failed / 0 skipped.
- A test-run teardown crash exposed an exit callback posting to the disposed Avalonia dispatcher. `TerminalHostView.Terminate` now disarms output/delegate callbacks before asynchronous child exit. Before fix: 9 tests passed then testhost crashed. After fix: 31/31 focused terminal tests pass.
- The inherited partial suite adds hit-tested panel tab actions and hover checks, but no record beyond the resize defect is claimed proven yet. The current surface still strips ANSI into a linear text log and does not render cursor/attributes, so UI-TERM-010 remains open.

records_candidates: none yet

Files touched so far: `src/Marrow.App/Views/Terminal/Port/TerminalHost.cs`, `src/Marrow.App/Views/Terminal/TerminalPanel.cs`, `tests/Marrow.App.Tests/Views/Terminal/TerminalPanelTests.cs`, `tests/Marrow.App.Tests/Views/Terminal/TerminalPanelHighRiskTests.cs`, `REPORT.md`.
