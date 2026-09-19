# Field captures (owner-supplied, real Mac, dated)

Three screenshots of Droppy Code running on a third party's Mac, supplied by the owner
2026-09-19. Stored beside this file. These are the **best visual oracle available** for this
port, because unlike the marketing captures they can be dated and version-bounded.

| File | Pixels | Bytes | sha256 (first 16) | Menu-bar clock |
| --- | --- | --- | --- | --- |
| `field-01-transcript-heads-effortslider.webp` | 1280x720 | 128204 | `76232c9442a90607` | Thu 17 Sep 4:56 AM |
| `field-02-translucent-system-theme.webp` | 1280x720 | 60002 | `107a0c01515a7739` | Thu 17 Sep 4:50 AM |
| `field-03-sidebar-usage-twopanels.webp` | 1280x827 | 114276 | `a336b23898b3b8af` | Fri 18 Sep 6:42 AM |

Provenance: supplied by the commissioning owner, described as "my cousin['s]" Mac. Not captured
by the app's own harness, so they carry real user data, arbitrary window size, and JPEG/WebP
recompression. Resolution is low (1280 wide) compared with the published set.

## Version bounding (INFERENCE, clearly labeled as such)

2026-09-17 is a Thursday and 2026-09-18 a Friday, which matches the menu-bar clocks, so the
captures are from 2026-09-17 and 2026-09-18.

Upper bound: the pinned commit `dd2b86e` is version **1.6.0**, committed 2026-09-18T21:43:26Z.
Capture 3 is stamped 6:42 AM on 18 Sep, which precedes that commit in any plausible timezone.
Lower bound: capture 1 and 3 show the model `stealth/union alpha`, and CHANGELOG line 157 records
Union Alpha becoming findable in **1.5.0** (2026-09-16).

**Conclusion: the field build is in the range 1.5.0 to 1.5.6 (2026-09-16 to 2026-09-17), one to
two days and one minor version behind the pinned 1.6.0.** This is an inference from timestamps and
changelog dates, not a version string read from the app, so it is not a SOURCE FACT.

## Why the bounding matters: the delta is fully enumerated

Because the version range is known, the exact set of changes between these captures and the pinned
commit can be read out of `CHANGELOG.md` (the `[1.6.0]` and `[Unreleased]` entries). That converts
these from "screenshots of some version" into a **calibrated** oracle: the areas below are known to
have changed after these captures and must NOT be trusted from them; everything else is likely
unchanged and may be used for discovery.

### KNOWN-STALE in these captures (changed in 1.6.0 or Unreleased)

1. **Turn folding.** 1.6.0: "A finished turn reads folded: it shows its final reply with the turn's
   plans and any warning, and the steps it took, its heads and its changed-files card wait behind
   the 'Worked for …' line, which opens them in a card of their own." Also a running turn now keeps
   its steps and the agent's thinking in a card under its bar. The transcript layout in captures 1
   and 3 is therefore pre-1.6.0 behavior.
2. **Chronological order switch** removed from Settings, Conversation in 1.6.0.
3. **Project marks.** 1.6.0 added an emoji or SF Symbol per project, drawn in front of a thread's
   name in the activity list in place of the project-name line, one line per thread; a new install
   starts on the compact list. Capture 3 shows the two-line style with the project name beneath the
   title, which is the pre-1.6.0 presentation.
4. **Usage-limit notice** with Cancel and New thread beside it. Added in 1.6.0.
5. **Auto-hide heads in the sidebar** (Settings, Hydra, Heads). Added in 1.6.0, off by default.
6. **Netlify** added to the MCP catalog in 1.6.0.
7. **Settings, About release-notes cards** moved from a popover to an inline card (Unreleased).
8. **Codex plan naming** changed to "Pro 20x", "Pro 5x", "Business Premium" (Unreleased).
9. Scrolling, diff panel, model list, popover and shortcuts-page layout behavior (1.6.0 bug fixes).

### Everything else in these captures is, absent contrary source evidence, representative

## What these captures establish that the published set does not

**Cross-verified against the pinned source. These are source-confirmed, not merely observed.**

1. **The menu bar.** Captures 1 and 3 show `Droppy Code  File  Edit  View  Thread  Window  Help`.
   `App/AppCommands.swift` produces exactly this: `.appSettings` replaced with "Settings…",
   `.saveItem` replaced with nothing (so File's stock Close cannot steal the Archive chord),
   `.newItem` replaced with "New thread" / "New thread in worktree" / divider / "Add project…",
   items added after `.sidebar` ("Toggle sidebar", "Command palette", "Toggle terminal",
   "Toggle changes", "Toggle activity view"), a `CommandMenu("Thread")`, an entry before
   `.windowArrangement`, and `.help` replaced with "Welcome tour". **Runtime matches source.**
2. **The usage popover**, capture 3, open as a panel. Reads "Usage" with a close control;
   "Context window  113.9k / 1M (11%)" over a tinted bar; a provider heading with a mark reading
   "Lead · Claude"; "Plan usage limits · Max (5x)"; then three rows, each a label, a
   "Resets in <H> hr <M> min" phrase, and a percentage:
   "5-hour limit / Resets in 4 hr 48 min / 2%",
   "Weekly · all models / Resets in 13 hr 18 min / 98%",
   "Weekly · Fable / Resets in 13 hr 18 min / 0%".
   Source confirms the strings: `App/Views/Usage/UsagePanel.swift:36,44` renders a
   `Text("Context window")` and a `UsageBar(fraction:tint:label:)`;
   `App/Views/Usage/UsageViews.swift:17` renders
   `"Plan usage limits · \(planName)"`; `Services/Providers/PlanLimits.swift:301-302` maps the
   `"session"` bucket to "5-hour limit" and `"weekly_all"` to "Weekly · all models".
   **Runtime matches source**, and the per-model weekly bucket ("Weekly · Fable") shows the
   pattern for a model-scoped window.
3. **Two floating head panels open at once**, capture 3, one docked each side of the chat, each
   with its own header (head glyph, name, a working indicator, elapsed time, expand and close
   controls), a brief label, an activity summary ("Ran commands", "Read files, ran commands") with
   its own elapsed time and chevron, a **bar-chart activity strip**, a count line
   ("13 commands", "19 commands · 8 reads"), and its **own composer with a provider-specific
   placeholder** reading "Ask OpenCode to build, fix or explai…" and its own stop control.
   This is hard evidence for contract section 10's "Do not reduce multiple panels to one page",
   and it shows the per-head composer placeholder is provider-named, not generic.
4. **The sidebar's activity list**, capture 3: a "Search threads" field, date group headings
   "Today" and "Yesterday", thread rows carrying the project name on a second line, a
   "› N heads" disclosure ("› 4 heads", "› 8 heads"), and beneath it a long flat list of head
   rows in the form "<Name> <N> · <brief>" (for example "Otto 10 · Skill Rack loads…",
   "Hank 9 · Queue-5 benchmark…", "Walter 7 · DeepSeek chief…"), with "+ Add project" and
   "Settings" pinned at the bottom. `AppCommands.swift:48-51` confirms a
   "Toggle activity view" command bound to `model.settings.sidebarActivityView`.
5. **Extreme translucency**, capture 2: the window renders almost fully see-through over a bright
   Golden Gate wallpaper, with desktop widgets legible through it. This is the single most useful
   image for PA-0001, because it shows the material's behavior at the transparent end of its range
   over high-luminance content, where a naive blur-and-tint substitute diverges most visibly.
   User message bubbles read pink/magenta here against blue in the published `hero.webp`, which is
   theme-dependent tinting.
6. **Model chips in the wild**: "stealth/union alpha Max", "stealth/union alpha Default",
   "Sonnet High", and in `hero.webp` "Fable Max". These exercise `ModelOption.chipName` and the
   effort suffix, and confirm an effort of "Default" renders as the word Default, matching
   `Core/Models/Provider.swift:268-274` where `effortTitle("")` returns "Default".
7. **The effort slider for a pair**, capture 1: title "Max" with chevron, pair line
   "stealth/union alpha + stealth/union…" truncated, a reset control, a multi-stop gradient track
   with a large pale knob, and the caption "The lead's effort · heads run at Max effort". Note the
   caption differs from `hero.webp`'s "The lead's effort · heads keep their own", so the caption is
   state-dependent, which is a behavior to pin down in source rather than assume.

## Standing caveat

Approximate oracle. Version-bounded to 1.5.0-1.5.6, not the pinned 1.6.0. Low resolution,
recompressed, arbitrary window size, real user data. Usable for structure, ordering, wording,
iconography, material character and state discovery. **Not** usable for pixel measurement, and
never for a parity claim. Where a capture and the pinned source disagree, the source wins and the
disagreement is recorded.
