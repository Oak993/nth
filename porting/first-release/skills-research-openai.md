# OpenAI skills for ChatGPT and Codex: what exists, how Codex finds it, what Marrow may bundle

Research date: 2026-10-02. Owner ruling: OD-0080 (pre-install the skills Claude and ChatGPT ship inside Marrow; only redistributable ones are bundled, the rest are listed with the reason).
Scope of this file: the OpenAI side (ChatGPT and Codex). The Anthropic side is in `research-anthropic.md` in this folder.

Method. I cloned and read the primary sources directly: `github.com/openai/skills`, `github.com/openai/plugins`, `github.com/openai/codex` (Apache-2.0), OpenAI's own docs at `learn.chatgpt.com` (the old `developers.openai.com/codex/skills` URL now redirects there), the agentskills.io specification, and the Codex installation on this PC (Codex CLI 0.159.3, runtime bundle 26.909.12148). Every licence below was read from the file in the repository or the install, not from a summary. Wherever I could not reach a primary source it is flagged in section 10.

A note on quoting: I was told not to reproduce long licence text, so licences are identified by name and paraphrased, with exact file paths and checksums so the owner or a lawyer can read the exact text. The Figma skills' own licence file says they are governed by the "Figma Developer Terms" (file `LICENSE.TXT`, identical in all 8 Figma skills).

[verified fix] Verification pass, 2026-10-02. A second reviewer re-cloned `openai/skills` (HEAD 49f948f confirmed, 114 commits), `openai/plugins` (HEAD 5fd93af confirmed) and `openai/codex` (main is now `f88a6efe437eec1329d8e8810c788c90ac547888`, 2026-10-02 18:11 UTC; `git diff 2739e82..f88a6ef` shows no change to `codex-rs/skills`, `codex-rs/ext/skills`, `skills_config.rs` or the app-server protocol, so every Codex claim pinned to 2739e82 still holds), re-read every LICENSE file, re-read the local install, and ran a live smoke test of the Codex app-server skill methods against the installed Codex CLI 0.159.3 with a throwaway `CODEX_HOME` (section 8.4). The short clauses that decide redistribution are now quoted in section 4.1. Every change made by this pass is marked "[verified fix]".

---

## 1. Findings in one screen

1. ChatGPT itself ships exactly one pre-installed skill: `skill-creator` (source: OpenAI Help Center article "Skills in ChatGPT", read via search result only, the page returns HTTP 403 to scripts; see section 10). Everything else people call "ChatGPT's built-in skills" (documents, spreadsheets, slides, PDF) is not in ChatGPT's skill list: it ships as plugins inside the Codex "primary runtime" bundle.
2. Codex ships 5 system skills inside the binary: `imagegen`, `openai-docs`, `review-agent`, `skill-creator`, `skill-installer`. They are Apache-2.0 (the Codex repo is Apache-2.0 and each skill carries its own licence file, except `review-agent`, which relies on the repo licence). These can be bundled.
3. The Codex runtime bundle on this PC contains plugins `documents`, `pdf`, `presentations`, `spreadsheets` (with `excel-live-control`), `template-creator`. These are the real successors of the old `doc`, `pdf`, `spreadsheet`, `slides` skills. They are NOT redistributable: the Documents skill's licence file forbids extracting, copying and distributing it; the engine they call, `@oai/artifact-tool`, is proprietary and licensed only for "internal evaluation and testing"; `template-creator` is marked Proprietary. Their `plugin.json` files say "MIT", which contradicts the shipped licence text; I treat the stricter text as binding and recommend asking OpenAI before relying on the MIT label.
4. `github.com/openai/skills` is deprecated since 2026-06-22 (commit 778b0e6). Its last state (commit 49f948f, 2026-06-23) holds 5 system + 39 curated skills. 26 of the curated skills are Apache-2.0, 4 are MIT (Notion Labs), 1 is MIT (Vercel), and 8 Figma skills fall under Figma's Developer Terms (not open). Net: 31 of the 39 curated skills plus the 5 system skills are bundleable (section 4).
5. The old Apache-2.0 `doc`, `spreadsheet`, `slides`, `sora`, `frontend-skill`, `develop-web-game`, `atlas`, `plan` and other skills were deleted from that repo but remain in git history under Apache-2.0 at known commits (section 5). Using the history copy is the only legal route to OpenAI-authored document, spreadsheet and slide skills. They are older and weaker than the current proprietary ones.
6. The successor catalogue is `github.com/openai/plugins` (commit 5fd93af, 2026-09-28): 62 plugins, 535 skills. It has no root LICENSE file; licence is declared per plugin in `plugin.json`. MIT, OpenAI-authored plugins with skills are bundleable (about 11 plugins, section 6). Plugins marked "Proprietary" are not.
7. Format: ChatGPT skills, Codex skills and Anthropic skills all use the open Agent Skills format (a folder with `SKILL.md`, YAML frontmatter `name` + `description`). They are directly interchangeable at the file level. Codex adds an optional `agents/openai.yaml` (UI metadata, invocation policy, tool dependencies). Anthropic and the open spec add optional frontmatter fields (`license`, `compatibility`, `metadata`, `allowed-tools`). Both sides ignore what they do not understand (section 8.5).
8. Marrow integration (the most important engineering finding): never write into `$CODEX_HOME/skills/.system` (Codex deletes and rewrites that folder whenever its embedded skills change). Register Marrow's own bundle folder through the app-server method `skills/extraRoots/set`, which leaves the user's own skill folders untouched (section 8.2 and 9). [verified fix] This now works end to end on the installed Codex CLI 0.159.3 (smoke test in section 8.4); it is not just a reading of codex main.

---

## 2. Table of mechanisms

| # | Mechanism | What it is | Where | Redistributable in Marrow? |
|---|---|---|---|---|
| M1 | ChatGPT built-in skill `skill-creator` | Only default skill on eligible ChatGPT accounts; invoked with `@skill-creator` | ChatGPT Work/desktop | Use the Codex copy (same name, M3, Apache-2.0) |
| M2 | ChatGPT workspace skills and Plugin Directory | Skills built or shared inside a workspace; plugins bundle skills + MCP servers + hooks | ChatGPT cloud | No (account data and proprietary plugins) |
| M3 | Codex system skills (5) | Embedded in the Codex binary, written to `$CODEX_HOME/skills/.system` | `codex-rs/skills/src/assets/samples` | Yes (Apache-2.0) |
| M4 | Codex user, repo, admin skill folders | `$HOME/.agents/skills`, `.agents/skills` up the tree, `$CODEX_HOME/skills` (deprecated but read), admin folder | Disk | Marrow must not overwrite; use extra roots |
| M5 | `skills/list`, `skills/config/write`, `skills/extraRoots/set`, `skills/changed` | App-server JSON-RPC methods (the Mac app and `CodexSession.cs:640` already call `skills/list`) | Codex app-server | n/a (the integration surface) |
| M6 | Codex "primary runtime" bundle (plugins documents, pdf, presentations, spreadsheets, template-creator) | OpenAI's document, PDF, slide, spreadsheet skills plus a proprietary artifact engine | `~/.cache/codex-runtimes/codex-primary-runtime` | No |
| M7 | Codex "openai-bundled" plugins (browser, chrome, computer-use, latex, visualize, code-review, codex-app-tools) | Desktop-app features | `~/.codex/.tmp/bundled-marketplaces/openai-bundled` | No (Proprietary) |
| M8 | `github.com/openai/skills` (deprecated) | Public skills repo, `.system` + `.curated` | GitHub | Yes for 31 of 39 curated + 5 system |
| M9 | `github.com/openai/plugins` | Successor catalogue, 62 plugins | GitHub | Per plugin (section 6) |
| M10 | `$skill-installer` | System skill that downloads skills from the repo or any GitHub path into `$CODEX_HOME/skills` | Codex | Not needed once bundled |

---

## 3. Codex system skills (M3): the five that ship in the binary

Source: `github.com/openai/codex`, commit `2739e828581c3deee59a270cd740675dc278ed14` (2026-10-02), folder `codex-rs/skills/src/assets/samples/`. The installed copy on this PC (`C:\Users\omera\.codex\skills\.system`) has the same five and a marker file `.codex-system-skills.marker`.

| id | What it does | Files (codex main) | Runtime needs | Windows | Licence | Bundle? |
|---|---|---|---|---|---|---|
| `imagegen` | Generates or edits raster images (photos, sprites, mockups, transparent cut-outs) through the OpenAI Images API | 12 (SKILL.md, agents/openai.yaml, 2 assets, 5 references, `scripts/image_gen.py`, `scripts/remove_chroma_key.py`, LICENSE.txt) | [verified fix] Two modes (SKILL.md lines 14-15). Default: Codex's built-in `image_gen` tool, which "Does not require `OPENAI_API_KEY`" (works for Codex users signed in with ChatGPT). Fallback only when the user asks: `scripts/image_gen.py`, which needs Python 3, `openai` and `pillow` (`uv pip install`), `OPENAI_API_KEY` and network | Yes (pure Python) | Apache-2.0 (own LICENSE.txt; unfilled template with no copyright line) | Yes. [verified fix] Under Codex it works without a key through the built-in tool; under Claude Code, Gemini or other providers there is no `image_gen` tool, so only the key-based CLI fallback can run |
| `openai-docs` | Answers "how do I build with OpenAI / Codex" from official docs with citations; model-upgrade and prompt-upgrade guides; Codex self-knowledge | 17 (9 references, node helper scripts) | Node, OpenAI docs MCP server (optional), network | Yes (the skill text documents `%TEMP%` handling) | Apache-2.0 | Yes (low value for Marrow users) |
| `review-agent` | Read-only, defect-first review of a code change, returns every actionable finding; delegated by other agents | 2 (SKILL.md, agents/openai.yaml) | none | Yes | No licence file in the folder; the repo root is Apache-2.0 | Yes. Set `allow_implicit_invocation: false` stays as shipped |
| `skill-creator` | Guide and scripts for creating or updating a skill (this is the same skill ChatGPT ships as its only default) | 9 (SKILL.md, openai.yaml, 2 assets, reference, 3 scripts, `license.txt`) | Python 3 | Yes | Apache-2.0 | Yes |
| `skill-installer` | Installs curated skills or any GitHub folder into `$CODEX_HOME/skills`; lists installable skills | 8 (3 Python scripts, assets, LICENSE.txt) | Python 3, `git` or network | Yes | Apache-2.0 | Optional: redundant if Marrow bundles the skills, and it writes to `$CODEX_HOME/skills`, which Marrow wants to leave alone. [verified fix] It is also stale: it lists from `openai/skills` `.curated` (the deprecated repo) and its text still offers a `.experimental` folder that no longer exists |

`plugin-creator` was an embedded system skill until recently (still in `openai/skills` `.system`, commit 49f948f; absent from codex main and from the local install). `plan` was a system skill, moved to experimental on 2025-12-20 (commit 2a1cce6) and then deleted. [verified fix] The `openai/skills` copy of `plugin-creator` (5 files, `scripts/create_basic_plugin.py`) carries its own Apache-2.0 `LICENSE.txt` and is bundleable; a newer copy lives at `openai/plugins` `.agents/skills/plugin-creator` but has no licence file there (that repo has no root licence), so take the `openai/skills` copy. The official docs page still names "skill-creator and plan skills" as examples of SYSTEM skills (`learn.chatgpt.com/docs/build-skills.md`), but `plan` is not in codex main or the local install; the docs are out of date on that point.

---

## 4. `github.com/openai/skills` (M8), last state

Repository: `https://github.com/openai/skills`. HEAD `49f948faa9258a0c61caceaf225e179651397431`, 2026-06-23, message "[codex] Update skill installer post-install guidance (#507)". 114 commits. Deprecation notice added 2026-06-22 (commit 778b0e6) pointing to `github.com/openai/plugins`. There is no `.experimental` folder any more ([verified fix] most of it was removed 2026-02-07 by ea6b206; the last experimental skill, `build-things`, was removed 2026-02-09 by 061ce66; see section 5). The README still tells users to install from `.experimental`, which is stale. No repository-level LICENSE file: the README states that each skill's licence is the `LICENSE.txt` inside its folder.

Licence classes found by hashing every licence file:

- Apache-2.0 standard text (unfilled template, no named copyright holder): 26 curated skills and the 5 system skills (31 in all; several byte-identical copies, the rest differ only in whitespace). Grants copying, modification and redistribution with conditions: ship the licence text, keep notices, mark changes, no trademark rights.
- MIT, "Copyright 2025 Notion Labs, Inc.": `notion-knowledge-capture`, `notion-meeting-intelligence`, `notion-research-documentation`, `notion-spec-to-implementation`. Condition: keep the copyright and permission notice.
- MIT, "Copyright (c) 2026 Vercel": `vercel-deploy`. Same condition.
- Figma Developer Terms: 8 `figma*` skills. Their licence file says the skills are governed by the Figma Developer Terms and that the skills are a Beta feature Figma may change or stop at any time. That is a link to platform terms, not an open licence. Not bundleable.
- Playwright skills are Apache-2.0 and carry a `NOTICE.txt` saying they derive from Microsoft's `microsoft/playwright-cli` (Copyright Microsoft Corporation, Apache-2.0). The NOTICE file must ship with them. [verified fix] Unlike the other Apache files, their `LICENSE.txt` is not the unfilled template: its appendix line reads "Copyright (c) Microsoft Corporation." (`microsoft/playwright-cli` is Apache-2.0 per the GitHub licence API, checked 2026-10-02).


Sizes: all non-Figma curated plus system skills together are about 5.3 MB on disk (cloudflare-deploy alone is 2.1 MB with 312 files, mostly docs). Figma skills are 1.2 MB.

Columns: Windows = can the skill's own scripts run on Windows without WSL. MCP = needs an MCP server configured in the provider.

| id | What it does | Files | Needs | Windows | Licence | Bundle? |
|---|---|---|---|---|---|---|
| `aspnet-core` | ASP.NET Core design, review, refactor guidance (Blazor, Razor, MVC, Minimal APIs, SignalR, gRPC, auth, testing) | 17 | none (docs only) | Yes | Apache-2.0 | Yes. Relevant (Marrow is .NET) |
| `chatgpt-apps` | Builds ChatGPT Apps SDK apps (MCP server + widget UI) | 11 | Node (scaffold script), docs | Yes | Apache-2.0 (own variant file) | Yes |
| `cli-creator` | Builds a composable CLI from API docs or a spec, with JSON output and auth | 4 | [verified fix] a language toolchain: Rust/cargo is the default it recommends, TypeScript/Node or Python when they fit (SKILL.md lines 35-42) | Yes | Apache-2.0 | Yes |
| `cloudflare-deploy` | Deploys to Cloudflare Workers, Pages and services | 312 (2.1 MB) | `npx wrangler`, Cloudflare login | Yes | Apache-2.0 | Yes (large) |
| `define-goal` | Turns a fuzzy intention into a concrete measurable goal for Codex's goal tool | 3 | Codex goal tool | Yes | Apache-2.0 | Yes (Codex-specific) |
| `figma` | Fetch Figma context via Figma MCP and turn it into code | 8 | Figma MCP | n/a | Figma Developer Terms | No |
| `figma-code-connect-components` | Maps Figma components to code (Code Connect) | 8 | Figma MCP, Python helper | Yes | Figma Developer Terms | No |
| `figma-create-design-system-rules` | Generates design-system rules for the codebase | 8 | Figma MCP, a `.sh` helper | Needs bash | Figma Developer Terms | No |
| `figma-create-new-file` | Creates a blank Figma or FigJam file | 7 | Figma MCP | n/a | Figma Developer Terms | No |
| `figma-generate-design` | Builds full screens in Figma from code | 7 | Figma MCP | n/a | Figma Developer Terms | No |
| `figma-generate-library` | Builds a design system (variables, components) in Figma | 23 | Figma MCP | n/a | Figma Developer Terms | No |
| `figma-implement-design` | Figma design to production code | 6 | Figma MCP | n/a | Figma Developer Terms | No |
| `figma-use` | Mandatory prerequisite for Figma `use_figma` calls (Plugin API rules) | 29 (713 KB) | Figma MCP | n/a | Figma Developer Terms | No |
| `gh-address-comments` | Addresses review comments on the current branch's PR | 6 | `gh` CLI logged in, Python | Yes | Apache-2.0 | Yes |
| `gh-fix-ci` | Debugs failing GitHub Actions checks, plans a fix, implements after approval | 6 | `gh`, Python | Yes | Apache-2.0 | Yes |
| `hatch-pet` | Creates and packages Codex animated pets (8x9 sprite atlas) | 14 | Python, image tooling, composes `imagegen` | Yes | Apache-2.0 | Yes (Codex-pet specific; low value for Marrow). [verified fix] It hard-codes the path `${CODEX_HOME:-$HOME/.codex}/skills/.system/imagegen/SKILL.md` and forbids calling the Image API directly, so outside Codex it does not work as written |
| `jupyter-notebook` | Scaffolds and edits `.ipynb` notebooks from templates | 12 | Python, `uv`, jupyterlab | Yes | Apache-2.0 | Yes |
| `linear` | Reads and manages Linear issues and projects | 5 | Linear MCP (docs note a WSL workaround on Windows) | With MCP, yes | Apache-2.0 | Yes (inert without the MCP) |
| `migrate-to-codex` | Migrates instruction files, skills, agents and MCP config into Codex files | 19 | Python | Yes | Apache-2.0 | Optional (Codex-specific) |
| `netlify-deploy` | Deploys sites with the Netlify CLI | 8 | `npx netlify`, auth token | Yes | Apache-2.0 | Yes |
| `notion-knowledge-capture` | Turns chats and notes into Notion wiki pages | 18 | Notion MCP | With MCP, yes | MIT (Notion Labs) | Yes, keep notice |
| `notion-meeting-intelligence` | Meeting prep from Notion context | 19 | Notion MCP | With MCP, yes | MIT (Notion Labs) | Yes, keep notice |
| `notion-research-documentation` | Researches across Notion into briefs and reports | 23 | Notion MCP | With MCP, yes | MIT (Notion Labs) | Yes, keep notice |
| `notion-spec-to-implementation` | Turns Notion specs into plans and tasks | 19 | Notion MCP | With MCP, yes | MIT (Notion Labs) | Yes, keep notice |
| `openai-docs` | Same as the system skill (older copy in this repo) | 10 | see section 3 | Yes | Apache-2.0 | Use the Codex-repo version instead |
| `pdf` | Read, create, review PDFs with visual render checks | 4 | Python (`reportlab`, `pdfplumber`, `pypdf`), Poppler (`pdftoppm`); the skill only lists brew and apt install lines | Yes once Poppler is installed (it documents no Windows install line) | Apache-2.0 | Yes. This is the open counterpart of the proprietary `pdf` plugin in M6 |
| `playwright` | Drives a real browser from the terminal via `playwright-cli` | 9 | Node, `@playwright/cli`; wrapper is `scripts/playwright_cli.sh` | Needs Git Bash or WSL for the wrapper (the CLI itself is cross-platform) | Apache-2.0 + NOTICE (Microsoft) | Yes, ship NOTICE.txt; add a Windows wrapper |
| `playwright-interactive` | Persistent browser or Electron debugging through a `js_repl` tool | 6 | Codex `js_repl` tool ([verified fix] off by default: needs `js_repl = true` under `[features]` or `--enable js_repl`, and the skill says to run Codex with `--sandbox danger-full-access` for now), Node + `npm install playwright` | [verified fix] Not stated: the skill has no Windows-specific text (only macOS Retina notes); nothing in it is POSIX-only | Apache-2.0 + NOTICE | Yes (works only where `js_repl` exists, i.e. Codex) |
| `render-deploy` | Deploys to Render, writes `render.yaml` blueprints | 21 | Render MCP or CLI | With MCP, yes | Apache-2.0 | Yes |
| `screenshot` | OS-level screenshots of screen, window or region | 11 | Python; has `take_screenshot.ps1` for Windows and macOS Swift helpers | Yes (PowerShell script ships) | Apache-2.0 | Yes. Directly useful on Windows |
| `security-best-practices` | Language-specific secure-coding review (python, js/ts, go) | 13 (437 KB) | none | Yes | Apache-2.0 | Yes |
| `security-ownership-map` | Git-history security ownership and bus-factor analysis | 8 | Python, `networkx`, `git` | Yes | Apache-2.0 | Yes |
| `security-threat-model` | Repo-grounded threat model into a Markdown file | 5 | none | Yes | Apache-2.0 | Yes |
| `sentry` | Read-only Sentry issue and event queries | 5 | `sentry` CLI, `sentry auth login` or `SENTRY_AUTH_TOKEN` | [verified fix] Queries yes; the only install line it gives is `curl https://cli.sentry.dev/install -fsS \| bash`, which needs Git Bash or WSL | Apache-2.0 | Yes |
| `speech` | Text-to-speech through the OpenAI Audio API | 16 | Python `openai`, `OPENAI_API_KEY` | Yes | Apache-2.0 | Yes (needs key) |
| `transcribe` | Audio to text with optional speaker labels | 7 | Python `openai`, `OPENAI_API_KEY` | Yes | Apache-2.0 | Yes (needs key) |
| `vercel-deploy` | Deploys to Vercel and returns a link | 6 | [verified fix] primary path is the Vercel CLI (`vercel deploy [path] -y`); `scripts/deploy.sh` (bash) is only the no-auth fallback; network | [verified fix] Yes with the Vercel CLI; the fallback script needs Git Bash or WSL | MIT (Vercel) | Yes, keep notice; add a Windows path for the fallback |
| `winui-app` | Builds WinUI 3 desktop apps with the Windows App SDK | 21 | .NET SDK, Windows | Windows only | Apache-2.0 | Yes (Marrow is Avalonia, so low value) |
| `yeet` | Stage, commit, push and open a PR in one flow with `gh` | 5 | `gh` | Yes | Apache-2.0 | Yes |

Practical subtotals for Marrow:
- Bundle without caveat: the 5 system skills plus the curated skills marked "Yes" whose Needs column is empty or only a local tool.
- Bundle but inert unless the user's CLI has the matching MCP server: `linear`, 4 Notion skills, `render-deploy`, `chatgpt-apps` (docs-first, works without), Cloudflare/Netlify/Vercel need their own logins.
- Not bundled: 8 Figma skills.

### 4.1 The licence text that decides redistribution [verified fix]

Short quotes, read from the files at the pinned commits:

- Apache-2.0 (all OpenAI-authored skills in `openai/skills` and the Codex system skills), section 4: "You may reproduce and distribute copies of the Work or Derivative Works thereof in any medium, with or without modifications, and in Source or Object form, provided that You meet the following conditions". The conditions (4a-4d): give recipients a copy of the licence, mark changed files, keep copyright/patent/trademark/attribution notices, and pass on any NOTICE file. Section 6: "This License does not grant permission to use the trade names, trademarks, service marks, or product names of the Licensor". Bundling in a closed-source app is allowed.
- MIT (Notion Labs, Vercel): grants the right "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies", subject to "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software." Bundling allowed with the notice.
- Figma (8 curated skills): "Use of these Figma skills and related files ("Materials") is governed by the Figma Developer Terms" and "These Materials are currently offered as a Beta feature." No grant to redistribute; not bundleable.
- OpenAI Documents skill in the Codex runtime: users may not "Distribute, sublicense, or transfer these materials or derivative works based on these materials to any third party". Not bundleable.
- `@oai/artifact-tool` 2.8.59: a licence "solely for internal evaluation and testing purposes"; may not "Copy, modify, distribute, sublicense, sell, lease, or otherwise make the Software available to any third party". Not bundleable.
- `public-equity-investing/skills/earnings-preview/LICENSE.txt` in `openai/plugins`: "Proprietary - Internal Use Only ... Redistribution outside your organization is not permitted without explicit permission." Not bundleable.
- `openai/skills` README, the only repo-level licence statement: "The license of an individual skill can be found directly inside the skill's directory inside the `LICENSE.txt` file." So a skill folder without a LICENSE file has no grant.

---

## 5. Skills OpenAI deleted from the public repo (still Apache-2.0 in history)

Because a git history copy is a lawful Apache-2.0 copy, these can be used. Each entry: deleting commit, the parent commit to take the files from, what it was.

| id | Where it was | Removed by | Take files from (parent commit) | Licence in history | Notes |
|---|---|---|---|---|---|
| `doc` | `.curated/doc` | 228962a, 2026-05-01 "Remove curated doc skill (#382)" | `45d05d75363abf13f99d09e899d61e07b8010685` | Apache-2.0 | `.docx` read/create/edit; `scripts/render_docx.py`; [verified fix] needs `python-docx` and `pdf2image` plus Poppler; LibreOffice (`soffice`) is optional for DOCX to PDF. 6 files. Replaced by the proprietary Documents plugin (M6) |
| `spreadsheet` and `slides` | `.curated/spreadsheet`, `.curated/slides` | fdf90d6, 2026-04-16 "Remove spreadsheets and slides skills (#350)" | `e6afb0d74cc75d220df2faf3dd6c635c2dc6a108` | Apache-2.0 (folder LICENSE.txt) | spreadsheet: openpyxl and pandas guidance, 4 openpyxl examples (9 files). slides: [verified fix] PptxGenJS helper library of 9 JS files, 2,371 lines (not about 3,000), plus 5 Python scripts (render, overflow test, montage, font detection, raster conversion), 20 files. slides needs npm `pptxgenjs`, `skia-canvas`, `linebreak`, `fontkit`, `prismjs`, `mathjax-full`; Python `Pillow`, `pdf2image`, `python-pptx`, `numpy`; LibreOffice `soffice`, Poppler, `fc-list`. [verified fix] Licence wrinkle: every slides JS helper starts with "// Copyright (c) OpenAI. All rights reserved." and the folder's Apache LICENSE.txt has the appendix line "Copyright (c) Microsoft Corporation." (apparently copied from the Playwright skill). The folder Apache grant still covers the files, and an "All rights reserved" copyright line does not withdraw it, but keep both notices and record this in ATTRIBUTION.md. Replaced by proprietary Spreadsheets and Presentations plugins |
| `frontend-skill`, `develop-web-game` | `.curated` | 11c6438, 2026-04-23 | `30444aed500c00c85294d12074f6e3ee794f808a` | Apache-2.0 | `develop-web-game` has a Playwright client script. Superseded by `build-web-apps` and `game-studio` plugins (section 6) |
| `sora` | `.curated/sora` | fb7b56d, 2026-04-29 | `728a3e9ba7240b7dfdda961b67775ec324fd639a` | Apache-2.0 | Video generation CLI (1,274-line `sora.py`); OpenAI API side may be retired, check before shipping |
| `atlas` | `.curated/atlas` | 2f7901e, 2026-02-04 | `7d56b27c73a64aa84a143c358b72163731e9e7fa` | Apache-2.0 | ChatGPT Atlas browser helper; low value |
| `create-plan`, `codex-readiness-unit-test`, `codex-readiness-integration-test`, `gitlab-address-comments`, `wrapped` | `.experimental` | ea6b206, 2026-02-07 (wrapped later 7fc1e3f) | `a5119697b819090e00e5d11ee1d86834d7c1043a` | Apache-2.0 ([verified fix] all five checked: each folder has an Apache-2.0 LICENSE.txt at a5119697) | Planning, test-readiness, GitLab review comments, `wrapped` |
| `build-things` | `.experimental` | 061ce66, 2026-02-09 | `7fc1e3f3b1778a0ce14bc3ee6afc223d5b997ca2` | [verified fix] No LICENSE file in the folder (5 files), so no grant under the repo's per-folder licence rule. Not bundleable | Small |
| `plan` | `.system/plan` | 2a1cce6, 2025-12-20 (moved to experimental, then removed) | `5a9bb4cbe6d79d44aa619c70edbfa1654358f69c` | Apache-2.0 (system skill) | Plan-file generator using `$CODEX_HOME/plans` |
| 3 old Figma skills (`figma-code-connect-components`, `figma-create-design-system-rules`, `figma-implement-design`) | `.curated` | bd3c623, 2025-12-19 | `b37577b57f8f081bc9e482ea2d555844792f29cd` | [verified fix] No licence file at all (each folder is a single SKILL.md), so no grant; not Figma terms | Not bundleable |

Recommendation: take `doc`, `spreadsheet`, `slides` from history as the licensed baseline for Office-document work (`research-anthropic.md` covers whether Anthropic's own `docx`, `xlsx`, `pptx` and `pdf` skills are redistributable; if they are not, these OpenAI-history copies are the clean option).

---

## 6. `github.com/openai/plugins` (M9), the current catalogue

Repository HEAD `5fd93af4cd0c623e020d0cc7e9ce178b4ac1f70f`, 2026-09-28, "Enable GitHub API OAuth and document public clients (#395)". 62 plugin folders, 535 `SKILL.md` files under `plugins/`, 5,388 files in the tree. [verified fix] There is a 536th `SKILL.md` outside the plugins: `.agents/skills/plugin-creator` (a repo-local skill with no licence file; use the Apache copy from `openai/skills` instead). The README is partly stale (it names `plugins/netlify` and `plugins/google-slides`, which do not exist). No root LICENSE file and the README says nothing about licensing. The only licence signal per plugin is `license` in `.codex-plugin/plugin.json`, plus an occasional LICENSE file in the plugin or a skill folder. That makes "MIT" claims weaker than a file in the repo; practical mitigation: ship attribution naming the repo, commit SHA and `plugin.json` author, and ask OpenAI to confirm if the owner wants certainty.

Marketplace manifests: `.agents/plugins/marketplace.json` (name `openai-curated`, display "Codex official"; [verified fix] 65 entries, more than the 62 folders) and `.agents/plugins/api_marketplace.json` (for API-key users; [verified fix] name `openai-api-curated`, 50 entries).

### 6.1 OpenAI-authored, MIT: candidates to bundle

| Plugin (version) | Skills | Size | Purpose |
|---|---|---|---|
| `build-web-apps` (0.1.2) | 6: `frontend-app-builder`, `frontend-testing-debugging`, `react-best-practices` (author vercel), `shadcn-best-practices`, `stripe-best-practices`, `supabase-best-practices` (author supabase) | 791 KB | Web app building, React and Next.js performance, shadcn, Stripe, Postgres. The Vercel and Supabase skills are third-party material under the plugin's MIT label; origin licences not shown in the folder. [verified fix] `shadcn-best-practices` (frontmatter name `shadcn`) and `stripe-best-practices` also read as vendor material. Upstream licences checked 2026-10-02: `vercel-labs/agent-skills` README says "MIT" and its `react-best-practices` SKILL.md has `license: MIT`; `supabase/agent-skills`, `shadcn-ui/ui` and `stripe/ai` are MIT per the GitHub licence API. Two frontmatter names do not match their folders (`shadcn`, `supabase-postgres-best-practices`), which breaks the agentskills.io rule that `name` must match the folder; rename on import |
| `build-web-data-visualization` (0.1.21) | 18 (D3, Canvas2D, Three.js, dashboards, geo, UML, accessibility and more) | 1.1 MB | Charting and visualization engineering |
| `game-studio` (0.1.2) | 9 (Phaser, Three.js, React Three Fiber, sprite pipeline, playtest) | 181 KB | Browser games |
| `build-ios-apps` (0.1.2) | 9 (SwiftUI patterns, Liquid Glass, perf audit, simulator debug) | 439 KB | iOS; needs Xcode, so macOS-only in practice |
| `build-macos-apps` (0.1.4) | 11 (SwiftUI/AppKit, packaging, notarization, signing) | 192 KB | macOS-only in practice |
| `test-android-apps` (0.1.2) | 2 (`android-emulator-qa`, `android-performance`) | 151 KB | adb, Android emulator; [verified fix] only `android-emulator-qa` has its own Apache LICENSE.txt; `android-performance` relies on the plugin's MIT label |
| `sentry` (0.1.2) | 1 | 50 KB | Same idea as curated `sentry`; has own Apache LICENSE.txt |
| `google-drive` (0.1.15) | 5 (`google-docs`, `google-drive`, `google-drive-comments`, `google-sheets`, `google-slides`) | 1.2 MB | Needs the Google Drive connector, not usable offline |
| `plugin-eval` (0.1.2) | 9 `SKILL.md` files, [verified fix] of which 5 are real skills (`evaluate-plugin`, `evaluate-skill`, `improve-skill`, `metric-pack-designer`, `plugin-eval`) and 4 are test fixtures under `fixtures/` | 461 KB | Skill quality tooling; drop the fixtures. [verified fix] Author field is "OpenAI Codex" |
| `ngs-analysis` (1.0.3) | 18 (sequencing analysis: FASTQ QC, RNA-seq, variants, single-cell) | 1.3 MB | Niche (bioinformatics) |
| `zotero` (0.1.2) | 1 | 73 KB | Zotero Desktop local API |
| Connector-only OpenAI plugins (`github`, `gmail`, `google-calendar`, `outlook-*`, `sharepoint`, `teams`; [verified fix] `slack` is also connector-only and MIT but its author is Slack, not OpenAI) | 0 skills | n/a | MCP/app connectors, no skills to bundle |

### 6.2 OpenAI-authored but Proprietary: not bundleable

| Plugin | Skills | What |
|---|---|---|
| `codex-security` (0.1.24) | 15 | Security scan, triage, fix, verify, vulnerability write-ups |
| `data-analytics` (0.2.8) | 18 | KPI design, dashboards, reports, metric diagnostics ([verified fix] author field "Data Analytics Maintainers") |
| `life-science-research` (1.0.3) | 50 | Life-science research workflows (1.1 MB) |
| `product-design` (0.1.52) | 10 | Audit, ideate, image-to-code, URL-to-code |
| `public-equity-investing` (0.1.31) | 23 | Earnings, DCF, comps, memos. [verified fix] Checked: the `earnings-preview` LICENSE.txt says "Proprietary - Internal Use Only" and forbids redistribution outside your organization, so it confirms the manifest |
| `openai-developers` (1.2.3) | 5 | `agents-sdk`, `build-chatgpt-app`, `chatgpt-app-submission`, `openai-api-troubleshooting`, `openai-platform-api-key`. Contradiction: manifest says Proprietary, but two skill folders carry an Apache LICENSE.txt. Use the Apache `chatgpt-apps` and `openai-docs` from section 4 instead |
| `openai-ads-conversions` (0.1.2) | 1 | Ads conversion setup |
| `creative-production` (0.1.25) | 2 (`intake`, `produce`) | `Proprietary` ([verified fix] author field "Creative Production", not OpenAI by name) |

### 6.3 Third-party plugins hosted in the OpenAI repo (licence set by their authors)

Only the ones whose manifest names a permissive licence and that have skills are listed. Each should be treated as that vendor's work, so attribution must name the vendor.

- MIT: `boltz-api-cli` (8 skills), `circleci` (4), `coderabbit` (1), `cloudflare` (9, plus own LICENSE.txt per skill), `expo` (13, LICENSE file), `mixpanel-headless` (4), `notion` (4, the same four as in section 4), `posthog` (1), `superpowers` (14, Jesse Vincent, LICENSE file), `supabase` (2), `temporal` (4), `twilio-developer-kit` (55), `zoom` (53, LICENSE file), `remotion` (12).
- Apache-2.0: `hyperframes` (HeyGen, 5), `vercel` (Vercel Labs, 54), `nvidia` (Apache-2.0 AND CC-BY-4.0, 12).
- Other: `chatcut` GPL-3.0-only (do not bundle in a closed app), `magicpath` UNLICENSED, `figma` LicenseRef-Figma-Developer-Terms (12 skills), and many with no licence field (`adobe`, `airtable`, `canva`, `dropbox`, `higgsfield`, `shopify`, `stripe`, and others), which means no redistribution right.

Recommendation: do not bundle any third-party vendor plugin by default. Most need that vendor's account or MCP server anyway. If the owner wants them, offer them as an opt-in download that fetches from the vendor's repo at a pinned SHA.

---

## 7. What ChatGPT and Codex actually ship pre-installed on this PC (read from disk)

### 7.1 Primary runtime bundle (M6)

Location: `C:\Users\omera\.cache\codex-runtimes\codex-primary-runtime`. `runtime.json`: bundle version 26.909.12148, artifact tool 2.8.59, Node v24.19.0, Python 3.12.14, pnpm 11.19.0, native dependencies libheif, jxrlib, poppler, powershell, git, target `win32` x64. [verified fix] `libreOfficeVersion` is `null` and `bundledSkills` is empty: LibreOffice is not part of the Windows bundle, so any render step that calls `soffice` needs a separately installed LibreOffice. The same file lists skills to remove from older installs: `codex-primary-runtime/spreadsheets`, `codex-primary-runtime/slides`, `doc`, `docs`, `spreadsheet`, `slides` (that is OpenAI migrating the old skills into the plugins below). The marketplace is `openai-primary-runtime`, enabled in `~/.codex/config.toml`.

| Plugin and skill id | What it does | Files | Needs | Windows | Licence as shipped | Bundle? |
|---|---|---|---|---|---|---|
| `documents` -> skill `documents` | Create, edit, redline, comment on `.docx` and Google-Docs-targeted documents with a render-and-verify loop; writing-quality rules; template creation | 80 | Bundled Node and Python runtime, python-docx, LibreOffice rendering ([verified fix] not bundled on Windows, see above), Poppler, proprietary session tooling (`container_tools`) | Yes (this bundle is built for win32) | `plugin.json`: MIT. `LICENSE.txt` in the skill: "Copyright 2026 OpenAI. All rights reserved", governed by OpenAI's Terms of Use or services agreement, and users may not extract the materials or keep copies outside the services, copy them, or distribute, sublicense or transfer them or derivatives to third parties; may not reverse engineer. md5-checkable: `...plugins/documents/skills/documents/LICENSE.txt`, 1,437 bytes | No |
| `pdf` -> `pdf` | Read, create, render, verify PDFs, including fillable AcroForms | 4 plus assets | Python `reportlab`, `pdfplumber`, `pypdf`; Poppler `pdftoppm`; calls a Node helper `mark_artifact_operation_started.mjs` | Yes | `plugin.json`: MIT; no LICENSE file in the folder | Not safe: no licence text, tied to the `container_tools` contract. Use the Apache `pdf` from section 4 |
| `presentations` -> `Presentations` | Read, create, edit PowerPoint and Google Slides decks; style rules; template-following scripts | 60 | `@oai/artifact-tool` ([verified fix] 13 files mention `artifact-tool`, 6 of them by the package name `@oai/artifact-tool`), Poppler, LibreOffice (not bundled) | Yes | `plugin.json`: MIT; no LICENSE file; depends on a proprietary engine | No |
| `spreadsheets` -> `Spreadsheets` and `excel-live-control` | Create and analyze `.xlsx`/`.csv`; the second drives an open Microsoft Excel workbook through the ChatGPT Excel add-in via Office.js; connector id `connector_openai_codex_document_control` | 19 + 13 | `@oai/artifact-tool`; the Excel add-in (a ChatGPT account) | Yes, Excel on Windows needed for live control | `plugin.json`: MIT; no LICENSE file; proprietary engine and connector | No |
| `template-creator` -> `template-creator` | Makes reusable personal template skills from files, Google links, images, email, Slack | 6 | Codex runtime | Yes | `plugin.json`: Proprietary | No |

The engine behind documents, presentations and spreadsheets is `@oai/artifact-tool` 2.8.59 (65 MB) in `dependencies\node\node_modules\@oai\artifact-tool`. Its `LICENSE.md` says "PROPRIETARY AND CONFIDENTIAL", copyright 2026 OpenAI, L.L.C., a non-exclusive, non-transferable, revocable licence for internal evaluation and testing only: no copying, no distribution, no production or commercial use, no reverse engineering. Marrow cannot ship it and the skills cannot work without it. Python 3.12 runtime, python-docx, openpyxl, python-pptx, pypdf, pdfplumber, reportlab, pandas, numpy and Pillow in that bundle are open-source libraries under their own licences, but the bundle as a whole is OpenAI's.

### 7.2 "openai-bundled" desktop plugins (M7)

Location `C:\Users\omera\.codex\.tmp\bundled-marketplaces\openai-bundled\plugins`.

| Plugin | Skills | Licence |
|---|---|---|
| `browser` | `control-in-app-browser` | Proprietary |
| `chrome` | `control-chrome` | Proprietary |
| `computer-use` | `computer-use` (control Windows apps) | Proprietary |
| `visualize` | `visualize` (inline charts, diagrams, simulations) | Proprietary |
| `latex` | `latex-compile`, `latex-doctor`, `texlive-runtime-installer` (bundled Tectonic, third-party notices file) | Proprietary |
| `code-review`, `codex-app-tools`, `unified-computer-use` | none (MCP servers) | Proprietary or unset |

These are tied to the ChatGPT or Codex desktop app and are not redistributable.

### 7.3 Remote-curated plugins installed through the plugin directory

Seen in `~/.codex/plugins/cache/openai-curated-remote`: `openai-templates` ([verified fix] 20 template skills, `artifact-template-*`: Business Review, Financial Budget, Legal Memorandum, Sales Pipeline, Three-Statement Forecast and others; Proprietary), `pages` (4 skills for ChatGPT Pages/Space; Proprietary), `plugin-management` (1 skill; Proprietary), `work-pets` (3 skills; Proprietary), `figma` (14 skills, version 15.0.0; [verified fix] this installed copy has no `license` field and no licence file, so no grant at all; the repo copy declares `LicenseRef-Figma-Developer-Terms`), `github` (connector, MIT, no skills), a vidIQ plugin (12 skills, vidIQ, Inc, no licence field). [verified fix] `google-calendar` (MIT, OpenAI) and `slack` (MIT, author Slack) are connectors with no skills and sit in the separate cache `~/.codex/plugins/cache/openai-curated`, not in `openai-curated-remote`. Under `created-by-me-remote` are the owner's own six developer plugins, not part of this research.

---

## 8. How Codex discovers and controls skills (primary sources)

### 8.1 Sources
- `https://learn.chatgpt.com/docs/build-skills.md` (the page that `developers.openai.com/codex/skills` redirects to, status 308), read 2026-10-02.
- `https://learn.chatgpt.com/docs/skills-and-plugins.md`, `.../plugins.md`, `.../enterprise/skills.md`.
- Codex source at commit `2739e828581c3deee59a270cd740675dc278ed14`: `codex-rs/ext/skills/src/host_roots.rs`, `loader/mod.rs`, `loader/host.rs`, `codex-rs/skills/src/lib.rs`, `codex-rs/config/src/skills_config.rs`, `codex-rs/app-server-protocol/src/protocol/common.rs` and `schema/typescript/v2/Skill*.ts`.

### 8.2 Where Codex looks (scope and precedence)

Documentation table, highest to lowest, then what the code adds:

| Scope | Location | Notes |
|---|---|---|
| REPO | `$CWD/.agents/skills`, then each parent up to and including the repo root (`.agents/skills` in every directory from the working directory up to the project root; root markers are configurable via `project_root_markers`) | Checked in by teams |
| REPO (config layer) | `<project>/.codex/skills` | Code path for project config layers |
| USER | `$HOME/.agents/skills` | Personal, cross-repo. On Windows `$HOME` is the user profile (`C:\Users\omera`), via the `home_dir()` call |
| USER (deprecated, still read) | `$CODEX_HOME/skills` (default `~/.codex/skills`) | Code comment: "Deprecated user skills location, kept for backward compatibility" |
| USER (extra) | Folders registered with the app-server method `skills/extraRoots/set` | Added with USER scope, separate from the user's own folders |
| ADMIN | `/etc/codex/skills` on Unix; on Windows the system config folder is `%ProgramData%\OpenAI\Codex` (path read from the config loader; the exact admin skills subfolder on Windows is by code reading, not tested) | Machine-wide defaults |
| SYSTEM | `$CODEX_HOME/skills/.system`, written from the binary | Rewritten by Codex. [verified fix] The docs table still gives "skill-creator and plan skills" as examples; `plan` is no longer shipped (section 3) |
| Plugin | Skills inside installed plugins (`pluginId` is returned on each skill) | Under `~/.codex/plugins/cache/<marketplace>/<plugin>/<version>/skills` |

Rules in the code and docs:
- Duplicates are not merged. Docs: if two skills share the same `name`, Codex does not merge them and both can appear in selectors. Roots are de-duplicated by path only.
- Symlinked skill folders are followed for user, repo and admin scopes; they are ignored for the system scope.
- Hidden directories are skipped. Scan limits: depth 6, 2,000 directories per root, 20,000 entries per root. [verified fix] The hidden-directory rule applies to any folder whose name starts with `.` below a root (`has_hidden_ancestor_below_root` in `codex-rs/ext/skills/src/loader/discovery.rs`), so Marrow's bundle must not put skills under a dot-folder such as `.openai`.
- Limits on fields: `name` at most 64 characters; `description` at most 1,024 characters; dependency values at most 1,024.
- Codex watches local skill files and notifies the client; if a new skill does not appear, restart.
- System skills install: `install_system_skills` writes the embedded folder to `CODEX_HOME/skills/.system` and first deletes the old folder, skipping the rewrite only when the marker fingerprint (a `DefaultHasher` hash of the embedded files, salt `v1`) matches. Anything a third party puts under `.system` will be deleted on the next Codex update. This is why Marrow must not use it.

### 8.3 Enabling, disabling, budget, invocation

- Disable one skill: `~/.codex/config.toml`
  ```
  [[skills.config]]
  path = "/path/to/skill/SKILL.md"
  enabled = false
  ```
  The config type also accepts `name = "..."` as an alternative selector. [verified fix] A name rule applies to every skill with that name (`SkillConfigRules::resolve_disabled_paths` in `codex-rs/config/src/skills_config.rs`), so disabling Marrow's `pdf` by name would also disable a user's own `pdf`; Marrow must use the path selector. Rules are applied in order and a later rule wins. Other keys in `[skills]`: `bundled.enabled` (default true; turns the embedded system skills off), `include_instructions` (whether the automatic skills block goes into the prompt), `max_context_tokens` (catalogue budget, capped at 10,000).
- Context budget: the initial skills list uses at most 2% of the model's context window, or 8,000 characters when the window is unknown; Codex shortens descriptions first and may omit skills with a warning. A selected skill is always read in full. Consequence for Marrow: bundling 80 skills will make descriptions shrink for every Codex user; ship a curated default set and let the user enable the rest.
- Invocation: explicit `$skill-name` (or `/skills`) in Codex CLI and the IDE extension, `@` in ChatGPT; implicit when the task matches the description. `agents/openai.yaml` with `policy.allow_implicit_invocation: false` turns implicit use off (default true), explicit `$skill` still works.
- `agents/openai.yaml` fields (all optional): `interface.display_name`, `short_description`, `icon_small`, `icon_large`, `brand_color`, `default_prompt`; `policy.allow_implicit_invocation`; `dependencies.tools[]` with `type` (for example `mcp`), `value`, `description`, `transport`, `command`, `url`.

### 8.4 The app-server methods (what Marrow calls)

| Method | Params | Result / effect |
|---|---|---|
| `skills/list` | `cwds?: string[]` (defaults to the session working directory), `forceReload?: boolean` (bypass cache, rescan disk) | `{ data: [ { cwd, skills: [ { name, description, shortDescription?, interface?, dependencies?, path, scope: "user"|"repo"|"system"|"admin", enabled, pluginId|null } ], errors: [ {path, message} ] } ] }` |
| `skills/extraRoots/set` | `extraRoots: AbsolutePath[]` | Registers extra skill folders (USER scope) for the server. Used so a host app can ship its own bundle without touching user folders. [verified fix] Returns `{}`, emits `skills/changed`, replaces (does not append to) the previous list, and an empty list clears it (`set_extra_roots_replaces_runtime_roots_and_clears_cache` test in `codex-rs/ext/skills/src/host_service_tests.rs`). It is held in memory by the app-server process (`catalog_processor.rs` passes it to the thread manager's skills service and the file watcher), so it applies to threads as well as to `skills/list`, and it must be sent again after every app-server start |
| `skills/config/write` | `path` or `name`, `enabled: boolean` | Enables or disables a skill (writes `[[skills.config]]`). [verified fix] Returns `{ effectiveEnabled }`. It writes into the user's `$CODEX_HOME/config.toml`, so it does touch a user file; Marrow should only write path-selector rules for its own skill paths |
| notification `skills/changed` | none | Local skill files changed; re-run `skills/list` |

The Mac app builds slash commands from `skills/list` (`Provider.swift:297-347`, `CodexSession.swift:355`: `cwds` plus `forceReload: true`), skipping entries with `enabled == false`, and sends a `{"type":"skill","name":...,"path":...}` input item. Marrow's port already calls `skills/list` at `E:\Droppy\wt\cl\terminal-verify\src\Marrow.Services\Providers\CodexSession.cs:640`. It does not yet call `skills/extraRoots/set` or `skills/config/write`.

[verified fix] Smoke test on the real binary, 2026-10-02. The three methods are present in the `rust-v0.159.3` tag (2026-09-30), the version installed here. With a throwaway `CODEX_HOME` (so the owner's config was not touched), `codex app-server` over stdio, `initialize`, then:
1. `skills/list` (`cwds` + `forceReload: true`) returned the 5 system skills (`imagegen`, `openai-docs`, `review-agent`, `skill-creator`, `skill-installer`, scope `system`).
2. `skills/extraRoots/set` with a folder holding one test skill returned `{}` and a `skills/changed` notification.
3. `skills/list` now also returned the test skill with scope `user`, `enabled: true`.
4. `skills/config/write` `{name, enabled: false}` returned `{"effectiveEnabled": false}` and wrote `[[skills.config]] name = ... enabled = false` into that `CODEX_HOME/config.toml`; the next `skills/list` showed the skill with `enabled: false`.

An `agents/openai.yaml` with `policy.allow_implicit_invocation: false` in the test skill loaded without errors.

### 8.5 Compatibility between ChatGPT, Codex and Anthropic skills

- OpenAI says skills "build on the open agent skills standard" (agentskills.io); ChatGPT and Codex use the same skill folders ("Skills bundled in plugins are also available in Chat and Work").
- agentskills.io specification, frontmatter: `name` (required, 1-64 chars, lowercase letters, digits, single hyphens, must match the folder name), `description` (required, 1-1,024 chars), `license`, `compatibility` (max 500 chars), `metadata` (string map), `allowed-tools` (experimental, space-separated). Optional folders `scripts/`, `references/`, `assets/`.
- Codex reads `name` and `description` and accepts `metadata.short-description`. The system skills in `openai/skills` use only those, plus `agents/openai.yaml` for UI.
- Anthropic's Agent Skills use the same `SKILL.md` model, so one folder works for both; Anthropic ignores `agents/openai.yaml`, Codex ignores Anthropic-only extras. Test at integration time that Claude Code accepts a folder containing `agents/openai.yaml` (it should, the file is just an extra in the folder; not tested here). [verified fix] Still untested: a `claude -p` probe (Claude Code 2.1.287) was attempted but the account had hit its weekly usage limit.
- Known mismatches: Codex `name` appears as `$name`, Claude as `/name`; Codex tool-dependency declarations (`dependencies.tools`) have no Claude equivalent; Anthropic-specific frontmatter such as `allowed-tools` has no effect in Codex.
- Plugins: OpenAI's plugin format is `.codex-plugin/plugin.json` plus `skills/`, `.app.json`, `.mcp.json`, hooks, and a marketplace file `.agents/plugins/marketplace.json`. It is separate from Claude's plugin format.

### 8.6 ChatGPT side
- ChatGPT desktop: "Skills" in the sidebar lists skills; `@` invokes; `@skill-creator` builds one. Standalone skills work in the ChatGPT desktop app, Codex CLI and IDE extension; skills bundled in plugins also work in ChatGPT web and mobile. Plugins appear in the "Plugins" tab (one universal directory shared by ChatGPT and Codex; not in the IDE extension).
- Help Center "Skills in ChatGPT" (via search result): skills are available to eligible Business, Enterprise, Healthcare and Edu accounts; the one built-in skill is `skill-creator`.
- Administration: workspace skills, local filesystem skills and plugins are separate controls (`learn.chatgpt.com/docs/enterprise/skills.md`).
- The doc, spreadsheet, slide and PDF capabilities in ChatGPT chat are product features backed by OpenAI's proprietary tooling, not user-visible skills.

---

## 9. Recommended bundle for Marrow (for the owner's decision)

Tier A, bundle by default (license-clean, useful on Windows, no extra service): `skill-creator`, `review-agent`, `imagegen` ([verified fix] keyless only under Codex, through its built-in `image_gen` tool; under every other provider it needs `OPENAI_API_KEY`, so for non-Codex providers it belongs in Tier B), `pdf` (curated Apache version), `screenshot`, `gh-address-comments`, `gh-fix-ci`, `yeet`, `jupyter-notebook`, `security-best-practices`, `security-threat-model`, `security-ownership-map`, `aspnet-core`, `cli-creator`, `define-goal`, plus `doc`, `spreadsheet`, `slides` restored from history (section 5) as the open Office-document baseline ([verified fix] `slides` needs Node packages, LibreOffice and Poppler, and `doc` needs Poppler, none of which Windows has by default; Marrow must install or detect them).

Tier B, bundle but disabled until the user connects the service: `linear`, `notion-*` (4), `render-deploy`, `netlify-deploy`, `vercel-deploy`, `cloudflare-deploy`, `sentry`, `speech`, `transcribe`, `playwright`, `playwright-interactive`, `chatgpt-apps`, `openai-docs`, `skill-installer`, `migrate-to-codex`, `hatch-pet`, `winui-app`, [verified fix] `plugin-creator` (Apache copy from `openai/skills` `.system`; Codex-specific), and the MIT OpenAI-authored plugins in section 6.1 (`build-web-apps`, `build-web-data-visualization`, `game-studio`, `plugin-eval`, `test-android-apps`, `zotero`; macOS-only ones as opt-in).

Not bundled, with reason: all `figma*` (Figma Developer Terms, beta; the three older Figma skills in history have no licence file at all); [verified fix] `build-things` (history, no licence file); the repo-local `plugin-creator` copy in `openai/plugins` (no licence file); `documents`, `pdf` (plugin), `presentations`, `spreadsheets`, `excel-live-control` (proprietary licence text and proprietary engine); `template-creator`, `openai-templates`, `pages`, `work-pets` (Proprietary); `codex-security`, `data-analytics`, `life-science-research`, `product-design`, `public-equity-investing`, `openai-developers`, `openai-ads-conversions`, `creative-production` (Proprietary); every `openai-bundled` desktop plugin; `chatcut` (GPL); vendor plugins without a licence.

Delivery rules to follow (so Marrow does not overwrite what the user has):
1. Keep the whole bundle in Marrow's own folder, for example `%LOCALAPPDATA%\Marrow\skills\openai\<id>\`, versioned and read-only.
2. For Codex: call `skills/extraRoots/set` with that folder on every app-server start. Do not write to `$HOME/.agents/skills`, `$CODEX_HOME/skills` or `.system`. Use `skills/config/write` for per-skill switches (or `bundled.enabled` for the system skills), and `skills/list` to show them. [verified fix] Always use the `path` selector for Marrow's own skills, never `name` (a name rule also switches off the user's same-named skills), and resend `skills/extraRoots/set` with the complete list each time, since it replaces the previous list.
3. Obey the context budget: do not turn on more than about 30 skills at once for Codex; make all Tier B skills off by default.
4. Licence compliance: ship the original `LICENSE.txt` in each skill folder, keep `NOTICE.txt` for the Playwright skills, keep the Notion and Vercel MIT notices, keep an `ATTRIBUTION.md` listing source repo, commit SHA and licence for each skill, and record local modifications (Apache-2.0 requires saying files were changed).
5. Do not use OpenAI, Notion, Linear, Figma, Vercel and similar logos from `assets/` or `agents/openai.yaml` as Marrow branding (Apache-2.0 gives no trademark rights). Replace the icons or leave `icon_*` out.
6. Provide Windows shims for the skills whose wrappers are bash only (`playwright_cli.sh`, `vercel-deploy/scripts/deploy.sh`, `figma-create-design-system-rules` helper, `ensure_macos_permissions.sh` is mac-only and harmless), and add a Windows install line for Poppler to the `pdf` skill.
7. Pin each source to a commit SHA: `openai/skills` `49f948faa9258a0c61caceaf225e179651397431`; `openai/plugins` `5fd93af4cd0c623e020d0cc7e9ce178b4ac1f70f`; `openai/codex` `2739e828581c3deee59a270cd740675dc278ed14`; history copies at the parent SHAs in section 5.

---

## 10. Gaps, conflicts and things to verify

1. Help Center article `help.openai.com/en/articles/20001066-skills-in-chatgpt` returned HTTP 403 to every automated fetch (and the in-app browser was refused). The "one default skill, `skill-creator`" statement comes from the search engine's quote of that page. Please confirm by opening the page in a normal browser. [verified fix] Re-checked 2026-10-02: still HTTP 403; a fresh search result again quotes that eligible accounts "include one skill by default: skill-creator". The official docs page `learn.chatgpt.com/docs/build-skills.md` independently calls `@skill-creator` the "built-in creator" in ChatGPT Work and names no other built-in ChatGPT skill.
2. Licence conflicts inside OpenAI's own files: the `documents`, `pdf`, `presentations`, `spreadsheets` manifests say MIT while the Documents LICENSE.txt and the artifact tool's LICENSE.md say the opposite. `openai-developers` says Proprietary but has Apache files. `openai/plugins` has no root LICENSE. If the owner wants any of these, the only safe route is a written answer from OpenAI.
3. The Apache-2.0 files in `openai/skills` are the unfilled template (no named copyright holder) ([verified fix] except the two Playwright skills and the historical `slides` skill, whose LICENSE.txt names Microsoft Corporation). That is how OpenAI published them and the grant still applies, but attribution lines will say "OpenAI, per the repository".
4. The Windows admin path (`%ProgramData%\OpenAI\Codex\skills`) is inferred from the config loader code (`DEFAULT_PROGRAM_DATA_DIR_WINDOWS`, `%ProgramData%\OpenAI\Codex\requirements.toml`) and is untested.
5. [verified fix] Resolved: the local binary is Codex CLI 0.159.3, tagged 2026-09-30 (two days older than codex main, not a few weeks), the method is in that tag, and a smoke test against it passed (section 8.4). A fallback that needs no new method is `$HOME/.agents/skills` (the shared user location, but it touches the user's folder, so use it only with an install marker and uninstall support).
6. The `sora` skill (OpenAI video API) and other API-backed skills depend on OpenAI products that change; recheck availability before shipping.
7. Skill counts depend on the pinned commits above; OpenAI is moving content from `openai/skills` to `openai/plugins`, so a later run will show different numbers.
8. [verified fix] `openai/plugins` MIT labels exist only in `plugin.json`; MIT's condition is to include "the above copyright notice and this permission notice", and these plugins ship no such notice text. Bundling them under MIT therefore means writing the standard MIT text with "Copyright OpenAI" (or the named vendor) into ATTRIBUTION.md, which is a reasonable reading of the label but not a file OpenAI published.

## Sources

- https://github.com/openai/skills (HEAD 49f948faa9258a0c61caceaf225e179651397431, 2026-06-23); history commits 228962a, fdf90d6, 11c6438, fb7b56d, 2f7901e, ea6b206, 7fc1e3f, 061ce66, 2a1cce6, bd3c623, 778b0e6, e940b8a, 728a3e9
- https://github.com/openai/plugins (HEAD 5fd93af4cd0c623e020d0cc7e9ce178b4ac1f70f, 2026-09-28)
- https://github.com/openai/codex (HEAD 2739e828581c3deee59a270cd740675dc278ed14, 2026-10-02, Apache-2.0)
- https://learn.chatgpt.com/docs/build-skills.md (and `.../skills-and-plugins.md`, `.../plugins.md`, `.../enterprise/skills.md`, `.../llms.txt`)
- https://agentskills.io/specification
- [verified fix] Added by the verification pass: https://github.com/openai/codex main `f88a6efe437eec1329d8e8810c788c90ac547888` (2026-10-02) and tag `rust-v0.159.3` (2026-09-30); live `codex app-server` smoke test on Codex CLI 0.159.3; GitHub licence API for `vercel-labs/agent-skills` (README "MIT"), `supabase/agent-skills`, `shadcn-ui/ui`, `stripe/ai` (MIT) and `microsoft/playwright-cli` (Apache-2.0), checked 2026-10-02; `C:\Users\omera\.codex\.tmp\bundled-marketplaces\openai-bundled\plugins\*\.codex-plugin\plugin.json`; `C:\Users\omera\.codex\plugins\cache\openai-curated`
- https://help.openai.com/en/articles/20001066-skills-in-chatgpt (not fetchable, see gap 1)
- Local install on this PC: `C:\Users\omera\.codex\skills\.system`, `C:\Users\omera\.codex\config.toml`, `C:\Users\omera\.codex\plugins\cache\*`, `C:\Users\omera\.cache\codex-runtimes\codex-primary-runtime` (runtime.json, plugins, `dependencies\node\node_modules\@oai\artifact-tool\LICENSE.md`)
- Marrow and Mac sources: `E:\Droppy\wt\cl\terminal-verify\src\Marrow.Services\Providers\CodexSession.cs:640`; `E:\Droppy\reference\droppy-code-v1.7.0\DroppyCode\Core\Models\Provider.swift:297-347`; `...\Services\Providers\CodexSession.swift:355`; owner decision OD-0080 in `E:\Droppy\wt\cl\terminal-verify\porting\owner_decisions.md`
