# T37 / RR-21 — Settings palette refresh

**BLOCKED.** Decision ED-NEW-models-palette-3. The existing Settings provider
refresh checks provider availability/catalogues; it does not research maker
logos, recompute family shades or report new makers. RR-21 is not already done.

SOURCE FACT: OD-0067 requires the same engine for automatic and requested
refreshes, checking all model sources, discovering new makers, deriving their
palettes, re-spacing existing families, showing progress and a short changed/
new-maker summary, with changed shades gliding to their new values. Personal
provider lists cannot determine shades (OD-0065/0066).

Evidence inspected:

- `SettingsView.cs:732-739`: `ProvidersRefreshButton` calls
  `Providers.RefreshEverything()` with `Checking providers…` /
  `Check providers again`. This is the source's provider-check control, not the
  owner-added palette refresh control; it must keep its existing behavior.
- No Settings control binding to palette research/refresh exists in this base.
- The new `PublicModelCatalogueStore.Refresh()` is a validated keyless catalogue
  prerequisite only. Its ten-root/restart/failure tests are in RR-20's receipt;
  it neither extracts logo colours nor produces a palette/new-makers summary.

The cited palette/refresh design is outside the repository. OD-0067's
`Settings > Appearance`, `Colours`, `Refresh`, and `last checked` description
is explicitly a **proposal**, not a licence to choose final geometry, text or
states. Need the locked refresh design plus RR-20's actual palette engine.
The missing external copies were requested during this task; the Windows file
connector is disconnected. Existing owner approvals are not being reopened.

No substitute UI or fabricated success/progress was added. No existing tests or
Settings behavior changed. When sources are available, test first with fake
catalogue/logo sources: progress through every maker; new-maker summary;
same result as the automatic path; offline failures that retain known palettes;
repeat refresh; and animated re-spacing with reduced motion. Run final Settings
layout/scroll/no-overlap checks on Windows. No EffortSlider seam.

Inspection receipt: `porting/evidence/models-palette/rr21/inspection.json`.
