# nth: next release, start here

This folder is the starting point for anyone (a person or an AI coding tool) who works on the release after nth 1.0.0.
Read this page first, then `SPEED-PLAN.md` (why the app lags and how it will be fixed). The owner's full list of
findings from testing 1.0.0 (the rows named below) is shared with contributors directly, not in this repository.

The repository's visibility is the owner's decision. Keep unreleased work and any non-public material the owner shares
with you private (`CONTRIBUTING.md`, contributor agreement section 9).
The code is licensed under the GNU AGPL v3.0 with additional terms; see `LICENSE` and `TRADEMARK.md` at the root.

> **Older instructions.** The 1.0 port's instructions (exact Mac parity, no pushing) are kept in `docs/history/port-1.0/`. The root `CLAUDE.md` and `AGENTS.md` are current and point here. For next-release work the owner wants Windows-native behaviour instead of exact Mac parity, and contributors hand in their work as patches (section 2).

## 1. What nth is

- A Windows desktop app for working with AI coding agents (Claude Code, Codex and others), with chats, Hydra (one chat
  that leads a team of heads), the Dextra assistant, Tactus PC control, the Ignis cost saver, a browser, artifacts,
  downloads, composer lights and liquid glass.
- C# on .NET 10 (SDK **10.0.401**, pinned by `global.json`) and **Avalonia 12.1.2**. Avalonia is pinned on purpose: the
  code reflects into Avalonia internals, so do not upgrade it.
- It began as a line-by-line port of a macOS SwiftUI app. The UI is built in C# through a SwiftUI-style layer
  (`src/Marrow.UI/Port`); there is no XAML. Comments such as `// :97` point at lines of the original Swift source. Keep
  them; they explain why the code is shaped the way it is.
- Projects: `src/Marrow.App` (the app and its views), `src/Marrow.UI` (controls, glass, motion, wallpapers),
  `src/Marrow.Core` (models and logic), `src/Marrow.Services` (providers, storage, downloads), `src/Marrow.Setup`
  (installer). Tests mirror them under `tests/<Project>.Tests`.
- The owner's direction for this release: keep Avalonia, no rewrite; make it feel native and smooth on Windows; stop
  copying macOS behaviour where Windows does it better. A native macOS app is a separate, later project.

## 2. Branches and handing in work

| Branch | What it is | Who writes to it |
|---|---|---|
| `release/1.0.0` | The shipped 1.0.0 code (tag `nth-1.0.0` = commit 87eb11490) plus the license files; the repository's front page | Only the owner |
| `next-release` | 1.0.0 plus the license files and this folder: the base for all next-release work | Only the owner |
| your local branch | Your own work, one branch per topic, on your own PC (for example `glass-lens`) | You |

Only the owner pushes to this repository. Contributors work in their own copy and hand in patches:

1. Branch from `next-release`: `git switch -c <topic> origin/next-release`.
2. Commit small steps with clear messages. Before handing in, update: `git fetch origin` then
   `git rebase origin/next-release`.
3. Make one patch file with only your changes: `git format-patch origin/next-release --stdout > nth-<topic>-<date>.patch`,
   and send it to the owner with the checklist in section 9. The owner's team reviews it, runs the tests and merges it.
   Read `CONTRIBUTING.md` at the root first: sending a patch means you accept its contributor agreement.

## 3. Build and run

```bash
dotnet --version              # must print 10.0.401
dotnet build src/Marrow.App -v q --nologo
dotnet run --project src/Marrow.App
```

The app keeps its data in `%LOCALAPPDATA%\nth` on your PC. You do not need any AI account for UI work. Chats need the
provider's own command-line tool installed and signed in.

## 4. Smart testing: test what you touched, not everything

The full suites take 20 to 45 minutes each. Run only the tests that cover your change:

1. List what you changed: `git diff --name-only origin/next-release...HEAD`.
2. For each changed `src/<Project>/<Folder>/...`, run the matching `tests/<Project>.Tests/<Folder>/` classes, plus any
   test class that names a type you changed.
3. Build the test project first, then run with a filter (native tests excluded):

```bash
dotnet build tests/Marrow.UI.Tests -v q --nologo
dotnet test tests/Marrow.UI.Tests --no-build --filter "FullyQualifiedName~Glass&FullyQualifiedName!~Native"
```

Rules:

- Make sure the output shows a Passed or Failed total. A filter that matches nothing proves nothing.
- A failure counts only if it also fails when its test runs alone, twice.
- Never loosen, skip or delete a test. If a design change truly needs a different expected value, replace the test with
  one that is equal or stricter and explain why in the note that comes with your patch.
- Tests named `*Native*` need a real desktop and drive the real window. Run them only when your change touches them.
- Tests never call a real AI provider, a real command-line tool or the network. They use the fakes in `tests/` and
  `src/*/Testing`.
- The owner's agents run the full suites once at the integration gate, before a release is packaged.

## 5. The glass: where it lives

| What | File |
|---|---|
| The glass shader (lens, rim, frost, tint) | `src/Marrow.UI/Port/GlassMaterial.cs` (lens code around lines 905-925) |
| Looks and presets | `src/Marrow.UI/Port/GlassLooks.cs`, `src/Marrow.UI/Port/SwiftGlass.cs` |
| Glass settings (Settings > Customization > Glass) | `src/Marrow.Core/Port/Settings/GlassSettings.cs`, `src/Marrow.App/Port/Settings/GlassSections.cs` |
| The window-sized frost layer over the wallpaper | `src/Marrow.UI/Chrome/Chrome.cs` (`GlassLayer`, lines 484-522) |
| Window backdrop (Windows DWM, Acrylic) | `src/Marrow.UI/Chrome/WindowChrome.cs` |
| Live wallpapers | `src/Marrow.UI/Theme/Wallpapers/WallpaperRenderer.cs`, `WallpaperVisualHandler.cs` |
| Composer light shaders | `src/Marrow.UI/Port/ComposerLights/ShaderLightShaders.cs` |
| Shader warm-up at start | `src/Marrow.App/Views/Composer/GlassVariantPreparation.cs` |
| The shared frame clock | `src/Marrow.UI/Port/FrameClock.cs` |

Important: the lens in `GlassMaterial.cs` already ports two open-source liquid-glass engines,
rizzytoday/liquid-glass (displacement map, per-channel aberration: scale -180, aberration 0 / 10 / 20) and
dashersw/liquid-glass-js (the rim ring). The engine is there. What hides it (row #87): the window-sized frost layer
washes the wallpaper out before any glass piece reads it, the wallpapers are calm and dark, the panels are tinted
heavily, and the lens bends only the wallpaper under a piece, never the content scrolling inside it. There is no
elastic scroll edge yet, so there is no "jelly" scroll.

## 6. Your rows

Read each full row in the owner's list before you start; the row is the specification.

- **#87** Glass does not look alive: the live wallpaper vivid behind clear panels, visible rim bend and fringe, a jelly
  scroll (content bends at the rims while it scrolls, plus an elastic edge).
- **#32** The wallpaper is washed out behind the glass.
- **#84** The working line's glass ("Combobulating... 17s") is too transparent; it must stand out over the composer.
- **#83** Grey blocky specks around the working line's text, its timer and its white dot (likely the composer light's
  readability guard sampling too coarsely).
- **#58** Light theme: weak contrast everywhere.

## 7. Hard rules for glass work (speed)

The app lags today, and glass is its biggest cost per frame. Read `SPEED-PLAN.md` before you change how glass is drawn.

- The owner's display runs at 239 Hz. Anything that redraws every frame costs four times what it would at 60 Hz.
- No new whole-window pass. The target design is one shared, low-resolution frosted copy of the wallpaper that every
  glass piece reads (speed plan, phase 2), not one blur per piece.
- When nothing moves, nothing redraws: an idle window must draw zero frames.
- Measure before and after, with the window in the foreground on a high-refresh screen. A measurement taken with the
  window in the background does not count (animations pause there, which hid the lag before).
- Phase 2 of the speed plan changes the same files (`GlassMaterial.cs`, `Chrome.cs`, the wallpaper renderer). Agree with
  the owner who changes what before a structural change, so the two pieces of work do not collide.

## 8. UI rules

- Every visible change animates. Nothing snaps, nothing jumps, and no fades between moves: motion is continuous, from a
  designed start to a designed end. Reduce Motion is respected.
- Every control has an accessible name, works with the keyboard, and Escape closes what it opened.
- Write code like the code around it: same naming, same comment style. The comments record decisions with numbers:
  OD-nnnn are the owner's decisions (`porting/owner_decisions.md`, the authority), ED-nnnn are engineering decisions
  (`porting/engineering_decisions.md`). Add a new decision under a heading `## ED-NEW-<topic>` at the end of the
  engineering decisions file; it gets its number at merge.
- No new NuGet package without asking the owner first. No XAML.
- Keep line endings exactly as they are (the repository has `* -text`). The `porting/*.md` ledgers mix CRLF and LF:
  append to them byte for byte.
- Never commit a secret, token or real account data.

## 9. Patch checklist

- [ ] Based on the latest `next-release`; handed in as one patch file.
- [ ] The row numbers this solves are in the note and the commit messages (for example "#87 #32: live wallpaper through clear glass").
- [ ] Tests for what I touched: the filter I used and the Passed / Failed totals.
- [ ] Frame times before and after (idle, scrolling, opening a popover) on a high-refresh screen, window in front.
- [ ] Before and after screenshots or a short video, dark and light theme.
- [ ] No test loosened, skipped or deleted; no new package; no secret.

## 10. Files in this folder

| File | What it holds |
|---|---|
| `README.md` | This page |
| `SPEED-PLAN.md` | The speed plan: root causes with file and line, the Windows-way rules, the phases, the targets |
| `designs/glass-ui/index.html` | The approved glass design page (open it in a browser) |
