# T37 / RR-19 — searchable OpenRouter picker

Base: `285b6b7a99b39e0e517f34c8d34a1860c945f83f` (the requested
`origin/claude/verify-terminal-20261001`). Audit: `origin/codex/release-audit`,
`release-readiness.md` RR-19. Decision: ED-NEW-models-palette-1.

**BLOCKED.** The approved picker is still absent. Existing OpenRouter API/session
plumbing is not the picker. No UI implementation is claimed by this change.

SOURCE FACT: OD-0049 requires search in the composer's model picker. OD-0060's
hold is superseded by OD-0075 and OD-0099 Q-PICKER A. OD-0076 requires folding
rows and easing card height in approximately 0.33 s; minimum chip clearance
10 px; shown count `0 models`, `1 model`, `2 models`, etc.; count crossfade;
top-match highlight during search; arrows move it and Return chooses it.

The approved v4/v5 source is cited at
`E:\Droppy\work\design\openrouter-picker\` and
<https://claude.ai/artifact/BNoUoc4vvCLK5pHzYMmVcA>. It is absent from this
base's tracked design sources. The Windows file connector returned
`tunnel_client_not_seen`, so the external copy could not be read. OD-0069's
exact geometry/type/appearance cannot be reconstructed from the decision's
description. Need the locked source and its transition/placement measurements.
Approval already exists; this is a source-access blocker, not a request to
approve the design again.

Verification on Linux, .NET 10.0.401 / Avalonia 12.1.2, isolated data directory:

- New `OpenRouterPickerCatalogueTests`: 5 passed, 0 failed/skipped. The real API
  parser feeds the existing Settings search with fake models; name, maker,
  complete ID, case-insensitive matching and unsuccessful HTTP status covered.
  Every HTTP exchange uses `InProcessHttpHandler`; no socket/key store.
- This proves a usable existing prerequisite, not approved picker layout,
  keyboard interaction, count transitions or composer wiring.
- Baseline solution build: 0 warnings/errors.

Commands and TRX receipts are stored in `porting/evidence/models-palette/rr19/`.
No existing tests, references or assertions changed. No EffortSlider seam.
