# Skills and tools on demand: plan (OD-0099 amendment of 2026-10-04)

Date: 2026-10-04. Status: plan only. Nothing is built, installed, downloaded into Marrow or committed.

**Owner ruling (OD-0099, latest amendment, recorded in English from Hindi):** the tools would make the app too big, so they are not shipped with it. Settings gets a place (Plugins or Skills) where the user downloads the tools and skills they want. When a chat task needs a tool that is not there, Marrow says so in the chat and offers to download it; on yes it downloads and the task goes on; if it was already downloaded from Settings it is simply used. The app is not commercial (the owner and two or three relatives), so installing the skills Anthropic and OpenAI ship is fine; they are fetched by the user's own Marrow from the vendors' official public sources at a pinned version, not copied into Marrow's repository or installer, and each stays under its source's terms. Marrow's own clean-room skills come later. The Settings page and the in-chat prompt are designed first for the owner's pick (OD-0070).

Related rulings: OD-0100 (one "update everything" control; it must cover what this plan installs), OD-0101 (everything the Claude and Codex desktop apps can do), OD-0076 (every change transitioned), OD-0070 (variants first), Q-SKILLS-4 A (no brand-guidelines skill), Q-SKILLS-8 A (Antigravity and Cursor plugin folders only after a live test), Q-V-13 B (remove from a hover trash icon, confirmed by a second click).

Builds on, and does not repeat: `PLAN.md` revision 3 (installation machinery, provider wiring), `research-loading.md` (how each provider loads a skills folder), `research-anthropic.md` and `research-openai.md` (what the vendors publish and under which terms), `ORIGINALS-PLAN.md` (the deferred clean-room programme and its Wave 0).

Labels: **SF** source fact (read at a named primary source, or measured on 2026-10-04 at the URL given); **ED** engineering decision (a choice made here, with its reason); **UN** unresolved (the unit that settles it is named).

Design page for the owner's pick: `E:\Droppy\work\design\decisions-2026-10-04\skills-downloads\` (share copy `share\skills-downloads.html`).

---

## 0. The answer in one screen

1. **Nothing heavy ships.** The installer carries no Python, Node, LibreOffice, packages or vendor skills. It carries a small pinned **catalogue** (names, URLs, versions, SHA-256 hashes, licence names, needs, Marrow's own one-line descriptions) compiled into `Marrow.Services`. (ED: the catalogue is data Marrow wrote about the items, not a copy of them.)
2. **The catalogue (section 2):** 10 vendor skill sets holding 615 skills (Anthropic 388, OpenAI 227), about 49 MB in all, and 11 tools plus 6 package sets. The biggest tool downloads are LibreOffice (356 MB), Playwright's Chromium (325 MB for the two zips) and Git for Windows (60 MB); Python itself is 12.6 MB.
3. **Fetch, verify, install (section 3):** HTTPS from the vendor's own host, resumable, every byte checked against the pinned SHA-256 (per file for skill sets), unpacked into `%LOCALAPPDATA%\Marrow Code\addons\` (tools) and `skills\store\` (skill sets), then locked read-only with the deny entry `PLAN.md` 2.1 already designed. Updates arrive only as new pins in a Marrow release and are applied by OD-0100's "update everything"; a running chat keeps the version it started with. Removal uses the same unlock order as `PLAN.md`.
4. **Providers (section 4):** unchanged from `PLAN.md` 2.2: per-chat locked generations passed with `--plugin-dir` (Claude, Grok), `skills/extraRoots/set` (Codex), `skillDirectories` (Copilot), `OPENCODE_CONFIG_CONTENT` (OpenCode), `--skill` (Pi, Command Code), Marrow-side loading (Cursor, Devin, the API providers). Tools reach every provider through one shim folder, `addons\bin`, put on each provider child's `PATH` at launch, so a tool installed in the middle of a chat is found by the next command without a restart.
5. **Missing tool in a chat (section 5):** four signals: a skill's declared needs, a held approval for a command whose program is missing, a failed command whose output says the program or module is missing, and (native providers) a `request_tool` call by the model. Marrow then shows the offer. On yes it downloads; the task goes on by approving the held command, or by sending one short continuation message when the turn has already failed or ended.
6. **Build (section 7):** 21 units, DL-0 to DL-20, test-first, no network in tests (a fake download server on loopback; every other connection refused). DL-11 reuses `PLAN.md`'s six provider-wiring units.
7. **What stays useful (section 8):** `PLAN.md` 2.1 to 2.4, 2.7, 3 and WU-4 to WU-21 almost whole; from Wave 0, the store/lock/verify/GC design (W0-2, W0-4), the fetch-and-hash method (W0-3), and the clean-room kit and sample corpus (W0-1, W0-6) for later and for live checks. The bundled-runtime parts (payload archives, `marrow-run.exe` as the only launcher, G5 as a ship gate) are superseded.
8. **One owner question (section 9):** Anthropic's four document skills and OpenAI's proprietary-labelled plugins carry terms that forbid keeping copies outside the vendor's services, or allow internal use only. Offer them as downloads or not.

---

## 1. What the ruling changes

| Earlier plan | Now |
|---|---|
| ORIGINALS-PLAN 3: Python, Node, LibreOffice, Tectonic and about 60 packages inside the installer (about 0.95 GB) | Nothing in the installer. Each is an optional download from Settings or from a chat. (ED) Tectonic is not offered now: no vendor skill in the catalogue needs it; it returns with Marrow's own `latex-papers` skill |
| PLAN.md 1: 50 vendor skill folders copied, patched for Windows and shipped in the Velopack payload | Vendor skill sets are downloaded by the user's Marrow from the vendor's repository at a pinned commit, **unmodified**. No vendor text is in Marrow's repository or installer |
| PLAN.md 1.2 and 1.2 E: Marrow patches (icons stripped, Codex paths, encoding) | (ED) No patches: a patch file carries upstream context lines, which would put vendor text in Marrow's repository, and Anthropic's proprietary terms forbid derivative works. The encoding failures that 1.2 E fixed (cp1252 on piped output) are handled instead by the Python shim, which sets `PYTHONUTF8=1` for Marrow's own Python only (3.6), never for the user's Python. The remaining Windows limits are shown on the item's detail (2.6) |
| PLAN.md 2.6 "detect and explain, do not bundle", optional winget | Marrow downloads the tools itself from the official sources (3.4). The 2.6 probe stays, to find what the user already has (3.6) |
| PLAN.md Q5, Q6, Q7, Q12, Q14; ORIGINALS Q1 | Answered by the ruling: the tools are user downloads; the wider catalogue (Q6) is opt-in now; Q14's `slides` is offered (the user's own fetch for personal use, not redistribution). Figma stays with the providers' own sign-in (OD-0099 Q4) |
| ORIGINALS-PLAN's 120 Marrow skills, Waves 1 to 8 | Deferred (ruling, effect 3). They will be catalogue items of their own when built |

---

## 2. The catalogue

All pins are as read on 2026-10-04 (SF, at the URLs given). The catalogue builder (DL-2) re-reads them before each Marrow release; a pin changes only with the owner's approval of its change report.

### 2.1 Vendor skill sets

Sizes are the sum of the blob sizes in the set's folders at the pin (SF: GitHub git-tree API, read 2026-10-04). Skill counts are SKILL.md files (SF: the same trees, and the research notes).

| Id | Set | Official source and pin | Skills | Size | Terms (as the source states them) | Needs |
|---|---|---|---|---|---|---|
| A1 | Anthropic example skills | `https://github.com/anthropics/skills` @ `8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4`, folders `skills/<name>` | 13: academy-guide, algorithmic-art, canvas-design, claude-api, discernment-nudge, frontend-design, internal-comms, mcp-builder, skill-creator, slack-gif-creator, theme-factory, web-artifacts-builder, webapp-testing (brand-guidelines left out, Q-SKILLS-4 A) | 8.1 MB (canvas-design 5.55, claude-api 1.86) | Apache-2.0 per folder; canvas-design fonts SIL OFL 1.1 | per skill: Python (skill-creator, slack-gif-creator, webapp-testing, mcp-builder evals), Node and pnpm and bash (web-artifacts-builder), Playwright Chromium (webapp-testing), internet (algorithmic-art, mcp-builder) |
| A2 | Anthropic document skills | same repo and pin: `skills/docx`, `xlsx`, `pptx`, `pdf`, `doc-coauthoring` | 5 | 3.5 MB | docx, xlsx, pptx, pdf: proprietary ("source-available"; use governed by the user's agreement with Anthropic; no copies outside Anthropic's services, no derivatives, no distribution). doc-coauthoring: no licence stated. **Section 9 question** | Python + document and PDF packages, Node + document packages, LibreOffice, Poppler, pandoc, qpdf |
| A3 | Anthropic knowledge-work plugins | `https://github.com/anthropics/knowledge-work-plugins` @ `8444efcd48f7012f09797778a36a33e73d0861f4` | 252 in 18 plugin folders (sales 36, small-business 44, partner-built 71, data 10, engineering 10, legal 9, human-resources 9, operations 9, finance 8, marketing 8, product-management 8, design 7, bio-research 6, customer-support 5, enterprise-search 5, productivity 4, cowork-plugin-management 2, pdf-viewer 1) | 8.1 MB whole repo | Apache-2.0; partner plugins MIT (Apollo, brand-voice, Slack, Zoom) or Apache-2.0 (Common Room) | (ED) offered per plugin folder; many name connectors (Slack, HubSpot and others) that Marrow does not provide; the text works without them |
| A4 | Anthropic financial services | `https://github.com/anthropics/financial-services` @ `574ed3624aebd0418c7e96cd101262f30210ab26` | 112 | 2.0 MB | Apache-2.0 | Python for some; connectors named in text |
| A5 | Anthropic life sciences | `https://github.com/anthropics/life-sciences` @ `e96556b637b56d6cc3a5ad33987009be9e60aa5c`, the 6 skill folders only | 6 | 1.1 MB | Apache-2.0 per folder | Python; science packages named in text |
| O1 | Codex's built-in skills, for providers other than Codex | `https://github.com/openai/codex` @ `2739e828581c3deee59a270cd740675dc278ed14`, `codex-rs/skills/src/assets/samples/` | 4: imagegen, openai-docs, review-agent, skill-creator (skill-installer left out: it writes into `$CODEX_HOME/skills`, PLAN.md Q15) | 0.28 MB | Apache-2.0 (repo; folder files where present) | Python (+ `openai`, `pillow` and an OpenAI key for imagegen outside Codex), Node (openai-docs) |
| O2 | OpenAI curated skills | `https://github.com/openai/skills` @ `49f948faa9258a0c61caceaf225e179651397431`, `skills/.curated/*` without the 8 Figma folders, without `pdf` (in O3) and `openai-docs` (O1 has the newer copy), plus `skills/.system/plugin-creator` | 30 | 3.3 MB | Apache-2.0; MIT for the 4 Notion skills (Notion Labs) and vercel-deploy (Vercel); Playwright skills carry a Microsoft NOTICE | gh (gh-address-comments, gh-fix-ci, yeet), Python, Node, uv, keys and logins for the deploy and service skills |
| O3 | OpenAI open document skills | `openai/skills` history: `doc` @ `45d05d75363abf13f99d09e899d61e07b8010685`; `spreadsheet`, `slides` @ `e6afb0d74cc75d220df2faf3dd6c635c2dc6a108`; `pdf` @ `49f948f` | 4 | 0.24 MB | Apache-2.0 per folder (slides' JS helpers also say "All rights reserved"; both notices kept as published) | Python + document and PDF packages, Poppler, LibreOffice; slides also Node packages |
| O4 | OpenAI plugins, MIT | `https://github.com/openai/plugins` @ `5fd93af4cd0c623e020d0cc7e9ce178b4ac1f70f`, per plugin: build-web-apps (6), build-web-data-visualization (18), game-studio (9), plugin-eval (5, fixtures dropped), test-android-apps (2), google-drive (5), ngs-analysis (18), zotero (1), sentry (1) | 65 | 3.9 MB | MIT in each `plugin.json` (no licence text in the folders; a few skill folders carry Apache-2.0 files) | Node for web; connectors for google-drive; adb for Android; Zotero desktop |
| O5 | OpenAI plugins marked proprietary | same repo and pin: codex-security (15), data-analytics (18), life-science-research (50), product-design (10), public-equity-investing (23), openai-developers (5), creative-production (2), openai-ads-conversions (1) | 124 | 18.2 MB | "Proprietary" in `plugin.json`; public-equity-investing's licence file says internal use only. **Section 9 question** | varies |

**Totals:** 10 sets, 615 skills (A 388, O 227), about 49 MB.

**Not in the catalogue, with the reason:**
- The 8 Figma skills and Figma plugins: Figma Developer Terms; Figma works through the providers' own sign-in (OD-0099 Q4).
- OpenAI's `documents`, `presentations`, `spreadsheets`, `pdf` plugins and `@oai/artifact-tool`: not published in any public source; only the Codex desktop app installs them. Shown read-only when a provider reports them (4.3).
- Claude Desktop's own skills and Claude Code's compiled skills: no public source. Shown when the provider reports them.
- Third-party vendor plugins hosted in `openai/plugins` (cloudflare, vercel, zoom, twilio and others): the ruling covers Anthropic's and OpenAI's own; (ED) a later catalogue release can add them per plugin.
- `claude-plugins-official` plugins: conflicting licence signals for 11 of them (research-anthropic.md 2d); (ED) later, per plugin, if the owner wants Claude-only extras.
- macOS-only plugins (`build-ios-apps`, `build-macos-apps`): no use on Windows.

### 2.2 Tools

Sizes and versions are the official listings on 2026-10-04 (SF). "Unpacked" is an estimate unless marked; DL-20 measures each.

| Id | Tool | Official download (pinned file) | Download | Unpacked | Licence | Verify | Needed by |
|---|---|---|---|---|---|---|---|
| T1 | Python 3.14.8, Windows embeddable x64 | `https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip` | 12.6 MB (12,601,679 bytes) | about 25 MB | PSF-2.0 (bundles OpenSSL Apache-2.0, libffi MIT, SQLite PD, others) | pinned SHA-256; the builder also checks python.org's Sigstore bundle | A1, A2, O1 to O5 scripts |
| T1p | pip 26.2.1 (installs package sets; then serves the agent's own `pip install`, 3.6) | `https://files.pythonhosted.org/...pip-26.2.1-py3-none-any.whl` | 1.8 MB | 8 MB | MIT | pinned SHA-256 (PyPI publishes it) | package sets |
| T2 | Node.js 24.21.0 LTS, Windows x64 zip (with npm and npx) | `https://nodejs.org/dist/v24.21.0/node-v24.21.0-win-x64.zip` | 38 MB | about 95 MB (98 MB for an installed Node with npm on this PC, ORIGINALS 3.4) | MIT; npm Artistic-2.0 (the user's own download, so no redistribution question) | pinned SHA-256; the builder checks `SHASUMS256.txt` and its signature | A1 web-artifacts-builder, A2 docx/pptx, O1 openai-docs, O2 playwright, O4 web plugins |
| T3 | LibreOffice 26.2.6, Windows x64 MSI | `https://download.documentfoundation.org/libreoffice/stable/26.2.6/win/x86_64/LibreOffice_26.2.6_Win_x86-64.msi` (26.8.1's Windows folder was still empty on 2026-10-04) | 356 MB | about 0.9 to 1.2 GB (UN, DL-20) | MPL-2.0, with LGPL and other parts as LibreOffice ships them | pinned SHA-256; the builder checks the `.asc` signature | A2 render and recalc, O3 doc and slides |
| T4 | Poppler 26.09.0 (pdftoppm, pdftotext) | No Windows build from the Poppler project (SF: poppler.freedesktop.org publishes source only). (ED) The conda-forge build as packaged by `https://github.com/oschwartz10612/poppler-windows/releases` (`Release-26.09.0-0.zip`) | 43.7 MB | about 110 MB (UN) | GPL-2.0-or-later (the user's own download) | pinned SHA-256 (GitHub publishes the digest) | A2 pdf, O3 pdf and doc |
| T5 | qpdf 12.4.2 | `https://github.com/qpdf/qpdf/releases` `qpdf-12.4.2-msvc64.zip` | 28.2 MB | about 70 MB | Apache-2.0 | pinned SHA-256 (GitHub digest `db87077e...`) | A2 pdf |
| T6 | pandoc 3.12 | `https://github.com/jgm/pandoc/releases` `pandoc-3.12-windows-x86_64.zip` | 42.1 MB | about 200 MB | GPL-2.0-or-later | pinned SHA-256 (`2a77ebc2...`) | A2 docx (text extraction) |
| T7 | Git for Windows 2.56.0, portable (Git Bash) | `https://github.com/git-for-windows/git/releases` `PortableGit-2.56.0-64-bit.7z.exe` | 60.0 MB | about 350 MB | GPL-2.0 | pinned SHA-256 (`eceb5e06...`) | `.sh` scripts: A1 web-artifacts-builder, O2 playwright, sentry, vercel-deploy fallback |
| T8 | GitHub CLI 2.102.0 | `https://github.com/cli/cli/releases` `gh_2.102.0_windows_amd64.zip` | 15.5 MB | about 45 MB | MIT | pinned SHA-256 (`ae64e556...`) | O2 gh-address-comments, gh-fix-ci, yeet (signing in stays the user's: `gh auth login`) |
| T9 | uv 0.12.23 | `https://github.com/astral-sh/uv/releases` `uv-x86_64-pc-windows-msvc.zip` | 18.0 MB | about 45 MB | MIT or Apache-2.0 | pinned SHA-256 (`75d05de6...`) | O2 jupyter-notebook |
| T10 | Playwright Chromium 153.0.8010.12 (Chrome for Testing, revision 1243, the build playwright 1.63.0 pins) | `https://cdn.playwright.dev/builds/cft/153.0.8010.12/win64/chrome-win64.zip` and `.../chrome-headless-shell-win64.zip` | 205.1 + 120.2 MB | about 600 MB (UN) | Chromium BSD-3 and the notices it ships | pinned SHA-256 per zip | A1 webapp-testing (it launches Chromium headless) |

**Package sets** (ED: sets, not single packages, so one offer covers a skill's whole need; wheel sizes SF from PyPI's JSON API for cp314 win_amd64, 2026-10-04):

| Id | Set | Packages (version, licence) | Download |
|---|---|---|---|
| P1 | Python: Office documents | python-docx 1.2.0 (MIT), openpyxl 3.1.5 (MIT) + et-xmlfile, python-pptx 1.0.2 (MIT), XlsxWriter 3.2.9 (BSD-2), lxml 6.1.3 (BSD-3), defusedxml 0.7.1 (PSF), typing-extensions; markitdown 0.1.8 (MIT) with its dependencies (size at DL-2) | 5.3 MB + markitdown |
| P2 | Python: PDF | pypdf 6.19.0 (BSD-3), pdfplumber 0.11.10 + pdfminer.six (MIT), pypdfium2 5.13.0 (BSD-3 / Apache-2.0), reportlab 5.0.1 (BSD), pdf2image 1.17.0 (MIT), cryptography 50.0.2 (Apache-2.0 / BSD-3) + cffi + pycparser, charset-normalizer | 17.2 MB |
| P3 | Python: images and data | pillow 12.3.0 (MIT-CMU), numpy 2.5.3, pandas 3.0.6, matplotlib 3.11.2 and their dependencies (BSD, MIT, Matplotlib licence) | 42.9 MB |
| P4 | Python: browser testing | playwright 1.63.0 (Apache-2.0) + greenlet, pyee | 38.9 MB (T10 is separate) |
| P5 | Python: GIF making | imageio 2.38.0 (BSD-2), imageio-ffmpeg 0.6.0 (BSD-2; carries an FFmpeg binary, GPL, the user's download) | 31.6 MB |
| P6 | Node: document packages | docx 9.8.1, pptxgenjs 4.0.1, react 19.3.0, react-dom 19.3.0, react-icons 5.7.0 (MIT), sharp 0.35.5 + @img/sharp-win32-x64 (Apache-2.0, libvips LGPL-3.0) | about 129 MB unpacked (react-icons alone 88 MB) |

Plus a small "general" set that rides with P1 (PyYAML, Jinja2, requests and friends; 2.5 MB). **Tool and package downloads in all:** about 1,080 MB if everything is taken, plus P6's npm tarballs (measured at DL-2); T3 and T10 are 63% of it. The common document case (T1, T1p, P1, P2, P3, T3, T4) is about 480 MB.

### 2.3 Catalogue file

(ED) `catalogue.json`, compiled into `Marrow.Services` as an embedded resource and shown in the app; never fetched at run time. Per item:
- `id`, `kind` (`skillset`, `tool`, `pyset`, `nodeset`), `version` or `pin`, Marrow's own `title` and one-line `summary` (written by Marrow, never copied from a SKILL.md), `vendor`, `sourceUrl`, `licenceName`, `licenceUrl` (the licence file at the pin), `termsClass` (`open`, `proprietary`, `none`).
- **Artifacts**: for tools and sets of wheels or tarballs, a list of `{url, size, sha256}`; for skill sets, every file as `{path, size, sha256}` under its folder, fetched one by one (3.2).
- `install`: the installer kind (3.4) and its parameters.
- `needs`: other catalogue ids (T1 for P1, P1 + T3 + T4 for O3, and so on); skill sets list needs per skill.
- `provides`: what makes the item findable when missing (5.3): executables (`python`, `python3`, `py`, `pip`, `node`, `npm`, `npx`, `soffice`, `pdftoppm`, `pdftotext`, `qpdf`, `pandoc`, `bash`, `sh`, `gh`, `uv`), Python modules (`docx`, `openpyxl`, `pptx`, `pypdf`, `pdfplumber`, `reportlab`, `pdf2image`, `PIL`, `pandas`, `playwright`, `imageio` and the rest), Node modules (`docx`, `pptxgenjs`, `sharp`, `react-icons`), and Playwright's missing-browser message.
- `windowsLimits`: known Windows failures of the unmodified upstream files, shown on the detail (2.6).
- `providers` and `excludeProviders`, `explicitOnly` per skill: taken over from `PLAN.md` 2.3 (Codex-only, Claude-only, explicit-only `screenshot`, `yeet`, `review-agent`).
- `defaultOn` per skill after install: Q-SKILLS-1 A's groups on (documents, review, security, GitHub, design, skill building); skills needing a key, a login or internet off, with the need shown. The `PLAN.md` 2.7 budget cap still applies, so a large set (A3, A4, O5) installs with its skills off and the user switches on what they want.

### 2.4 How the needs map works

The detail of an item shows what it needs and what uses it, and a download of a skill set offers its missing needs in the same confirmation ("Also needs Python and LibreOffice: 369 MB more"). (ED) Needs are never downloaded without being named and sized first; a set installs and switches on even when a need is missing (as `PLAN.md` 2.6: a missing need does not block the skill), and the chat offer covers the gap later.

### 2.5 Licence handling

- Each item's row says where it comes from and the licence by name ("From anthropics/skills · Apache-2.0"); the detail links the licence file as downloaded. Vendor names appear as plain text only; no vendor logos (PLAN.md 3.1 wording rules).
- (ED) Items whose `termsClass` is not `open` (A2, O5) show the licence text before the download with a separate "I have read these terms" control, and the row reads "Under Anthropic's terms" or "Under OpenAI's terms". The acceptance is stored with the date and the licence file's hash, so a later pin with changed terms asks again.
- THIRD_PARTY_NOTICES gains nothing for downloaded items (Marrow does not distribute them); the Settings footer keeps the not-affiliated line of `PLAN.md` 3.1.

### 2.6 Known Windows limits of unmodified vendor files

(SF, research-anthropic.md 4 and PLAN.md 1.2; shown on the item detail, never fixed by editing the files)
- A2 docx, xlsx, pptx: `scripts/office/soffice.py` uses `socket.AF_UNIX`, which Windows Python lacks, so its LibreOffice calls fail (render, `.doc` conversion, `recalc.py`). Other parts (python-docx, openpyxl edits) work. **This is the largest gap: under Claude's sync the same files have the same problem.**
- A1 skill-creator: `run_eval.py` uses `select.select` on a pipe (fails on Windows); packaging and validation work.
- A1 webapp-testing: `with_server.py` can leave the dev server running; `/tmp` paths in examples.
- O2 skills with `$CODEX_HOME/skills/...` script paths: the agent must use the skill's own folder; the detail says so.
- Encoding (cp1252 on piped output): fixed for Marrow's Python by the shim's `PYTHONUTF8=1` (3.6).

---

## 3. Fetch, verify, install, update, remove

### 3.1 Layout under `%LOCALAPPDATA%\Marrow Code\`

The data root is resolved through `DevDataRoot.LocalApplicationData()` as today, so Debug runs with `MARROW_DEV_DATA_ROOT` stay isolated (SF, PLAN.md 2.1).

```
Marrow Code\
  addons\
    state.json                          installed items, versions, accepted terms, declined offers (per chat)
    downloads\<sha8>.part               resumable partial downloads (writable, Marrow only)
    tools\<id>-<version>-<sha8>\        one folder per installed tool or package set, locked
    bin\                                shims, one per provided executable, locked; rewritten by Marrow only
    work\python-agent\<pyver>\          where an agent's own `pip install` goes (writable, never locked)
    work\lo-profile-<random>\           per-call LibreOffice profiles; temp output
  skills\
    store\<source>@<pin8>\<skill>\      downloaded skill sets, locked (PLAN.md 2.1 store, source level kept)
    views\<provider>\chat-<chatId>\     per-chat generations, exactly as PLAN.md 2.1
    enabled.json                        per-skill switches (PLAN.md 2.1)
```

### 3.2 Fetching

- (ED) **One downloader** in `Marrow.Services`, `HttpClient` with the system proxy, HTTPS only. The first URL must be on the catalogue's host list (`www.python.org`, `nodejs.org`, `download.documentfoundation.org`, `github.com`, `raw.githubusercontent.com`, `files.pythonhosted.org`, `registry.npmjs.org`, `cdn.playwright.dev`); redirects are followed only to HTTPS hosts (LibreOffice's download host redirects to mirrors; the pinned hash, not the host, is what makes the bytes trusted). The final host is logged.
- **Skill sets** are fetched file by file from `https://raw.githubusercontent.com/<owner>/<repo>/<pin>/<path>`, six at a time, because the catalogue holds every file's hash and a whole-repo archive would download far more (openai/plugins is 53.6 MB for the 3.9 MB of O4). (ED) GitHub's generated archives are not used: their bytes are not guaranteed stable, so they cannot carry a pinned hash.
- **Big files** go one at a time, resumable: the `.part` file keeps its bytes, and a restart sends `Range: bytes=<n>-` when the server answered `Accept-Ranges: bytes`; otherwise it starts over. Progress is bytes received over the pinned size, reported about ten times a second.
- **Cancel** stops the transfer and keeps the `.part` for a later resume; partial files older than 7 days are deleted at app start.
- **Disk space** is checked first: the download plus the item's unpacked size plus 10% must fit on the data root's volume, or the item fails at once with the amount needed.
- **No network at all** in tests (DL-0). Offline in real use: the item fails with "needs internet", and nothing half-installed remains.

### 3.3 Verifying

- Every artifact is hashed while it streams to disk; its length must equal the pinned size and its SHA-256 the pinned hash. A mismatch deletes the file and fails the item ("didn't match its checksum"), never retrying silently more than once.
- Skill sets: every file is checked, and the finished folder is verified whole against the catalogue (every file present, no extra file, no reparse point), the same whole-tree rule as `PLAN.md` 2.1.
- Signatures (Sigstore for python.org, the signed `SHASUMS256.txt` for Node, `.asc` for LibreOffice) are checked by the catalogue builder when it pins (DL-2), not at run time: the pinned hash inside Marrow is the run-time trust root (ED: Marrow cannot carry the vendors' keys more safely than the hashes themselves).

### 3.4 Installing, by kind

Every install builds in a `tmp-<random>` folder, then renames into place, then locks (the `PLAN.md` 2.1 deny entry `(OI)(CI)(WD,AD,WEA,WA,DE,DC)` plus read-only attributes), then verifies; a crash at any point leaves only a `tmp-` folder that GC deletes.

| Kind | Steps |
|---|---|
| `zip` (Node, Poppler, qpdf, pandoc, gh, uv, Chromium zips) | Unpack with zip-slip refusal (no absolute or `..` entries, no reparse points). Chromium goes into Playwright's own layout, `tools\chromium-1243\chrome-win64\` and `chromium_headless_shell-1243\`, with the marker files Playwright looks for (UN, DL-20: confirm `PLAYWRIGHT_BROWSERS_PATH` pointing there is accepted by playwright 1.63.0) |
| `python-embed` (T1) | Unpack; write the `python314._pth` to list `python314.zip`, `.`, `Lib\site-packages` and `..\..\work\python-agent\3.14` (absolute), plus `import site`. When a `._pth` file is present, Python ignores `PYTHONPATH` and `PYTHONHOME` (SF: Python's documented embeddable behaviour; DL-20 confirms on 3.14.8). Install pip from its wheel into `Lib\site-packages` |
| `pyset` (P1 to P5) | Download every wheel by its pinned hash; run Marrow's pip as `python -m pip install --no-index --find-links <downloads> --require-hashes --no-deps --target <tools\pyset-...> -r <lock>`; precompile bytecode (`compileall --invalidation-mode unchecked-hash`, because the locked folder cannot hold a `__pycache__` later); lock. Each set is its own folder and is listed in the Python `._pth` at launch time through the shim (3.6), so adding or removing a set never touches the Python folder |
| `nodeset` (P6) | Download every tarball by its pinned `integrity` (sha512) from the npm registry; unpack into `node_modules` by the lock's tree, **without running install scripts** (sharp's native part is its own platform package, so none is needed); lock. Reached through `NODE_PATH`, which the node shim sets (CommonJS `require` honours it; ESM `import` does not: UN, DL-20 checks the A2 scripts) |
| `msi-admin` (LibreOffice) | `msiexec /a <msi> /qn TARGETDIR=<tmp>` (an administrative image: files only, no registry or Start menu entries). UN: whether this runs without elevation on Windows 11 for this per-machine MSI (DL-20). Fallback if it does not: offer the official installer run normally (Windows asks for permission itself), then find it with the `PLAN.md` 2.6 probe and use it from where it is |
| `sfx-7z` (Git portable) | Run the self-extractor with its documented silent switches into `<tmp>` (`-o<dir> -y`), no installer; Git Bash's `bin` is what the `bash` and `sh` shims point at |
| `skillset` | Files fetched and verified as 3.2 and 3.3, laid out as `store\<source>@<pin8>\<skill>\`, locked; then new spare generations are built for every provider (PLAN.md 2.1 "Spare generations") |

### 3.5 Integrity after install

As `PLAN.md` 2.1 and ORIGINALS 3.3, unchanged in substance: whole-tree verification after install and after an app update; a cheap seal check per launch (deny entry present, hashes of every `.exe`, `.dll`, `.pyd`, `.node`); a mismatch marks the item **damaged** in Settings with "Repair" (a fresh download of that version) instead of using it. (UN, DL-20) LibreOffice's admin image is tens of thousands of files; if its full verification is too slow on this PC it falls back to all binaries plus a sample.

### 3.6 Shims and PATH: how a tool is found

- (ED) `addons\bin` holds one small shim per provided executable (`python.exe`, `python3.exe`, `pip.exe`, `node.exe`, `npm.cmd`, `npx.cmd`, `soffice.exe`, `pdftoppm.exe`, `qpdf.exe`, `pandoc.exe`, `bash.exe`, `sh.exe`, `gh.exe`, `uv.exe`). A shim is a copy of one Marrow-built NativeAOT launcher plus a `<name>.shim` text file naming the target and its environment; it starts the target with the same arguments, standard streams and exit code, and kills the child tree if it is killed. Real executables (not `.cmd` files) work the same from cmd, PowerShell and Git Bash. `npm` and `npx` stay `.cmd` because Node ships them that way.
- The Python shim adds `PYTHONUTF8=1` and lists the installed package sets for that run; the node shim sets `NODE_PATH`; the `soffice` shim adds `-env:UserInstallation=file:///<work\lo-profile-<random>>` so parallel conversions never share a profile, and deletes the profile afterwards. These variables reach only the shimmed child, never the provider or the user's own tools.
- **PATH rule** (ED): at every provider launch (and for the native providers' `run_command`), Marrow puts `addons\bin` into the child's `PATH` **immediately before the first `...\Microsoft\WindowsApps` entry**, or at the end if there is none. Everything the user had before that entry still wins (a real Python or LibreOffice the user installed is used, not Marrow's); only the Store's `python.exe` alias, which opens the Store instead of running Python, is shadowed. (SF: PLAN.md 2.6 treats that alias as missing.) The folder is on `PATH` even when empty, so a tool installed during a chat is found by the next command of the running provider, with no restart (PATH is searched at each process start).
- **Found on this PC** (ED): the `PLAN.md` 2.6 probe runs on the Settings page; a tool the user already has shows "Found on this PC" with its path, and the offer never fires for it. "Download Marrow's copy anyway" stays available.
- **Agent package installs** (ED): vendor skills tell the agent to `pip install` what they need. With Marrow's Python, `pip install x` lands in `work\python-agent\3.14` (a `pip.ini` beside Marrow's Python sets `target`; SF to confirm in DL-20: pip reads a site configuration file in `sys.prefix`), which is writable and on the `._pth`, so the skill works as written while the locked sets stay untouched. "Clear agent-installed packages" sits on Python's detail.

### 3.7 Updates (OD-0100)

- (ED) **A pin moves only with a Marrow release.** The catalogue builder writes a change report (versions, hashes, licence changes, sizes) for the owner, and the release carries the new catalogue. Marrow never asks a vendor for "latest" at run time.
- After an app update, any installed item whose catalogue pin differs shows **Update available** with "old → new" and the download size, on the Settings page and in OD-0100's control as one group ("Skills and tools"). The Tools/Updates page of OD-0100 counts them.
- An update installs the new version beside the old one (a new `<id>-<version>-<sha8>` folder or a new `store\<source>@<pin8>`), then switches the shims and the next generations to it in one step. Running chats keep the old version through their lease; GC deletes the old folder once nothing leases it (PLAN.md 2.1 leases and GC).
- A changed licence text in a non-open item asks for the terms again before updating (2.5).
- "Update everything" updates these with the rest, one at a time, after the provider CLIs and before Marrow itself (it needs no restart).

### 3.8 Removing

- (ED) From Settings: the row's trash icon on hover, then a second click to confirm (Q-V-13 B's pattern). If other installed items need it, the confirmation names them ("Python document packages and 2 skill sets use it"), and removing also removes the package sets that cannot run without it; skill sets stay installed and show the missing need.
- Removal unlocks (remove the deny entry, clear read-only), deletes, and is skipped and retried at the next GC if a handle is open (PLAN.md 2.1 GC order). A tool in use by a running chat is removed from the shims at once and its folder when the chat ends.
- **Reset skills and tools** (extends PLAN.md's "Reset skills data"): removes everything under `addons\` and `skills\store`, keeps `enabled.json` and accepted terms.
- **Uninstall:** PLAN.md's Velopack before-uninstall hook (`OnBeforeUninstallFastCallback`) also unlocks and deletes `addons\`, unlocking everything before deleting anything.

### 3.9 Failure states and Retry

| Failure | Message (word set S1) | Retry does |
|---|---|---|
| No connection / DNS / TLS | "Couldn't reach libreoffice.org." | resume from the `.part` |
| HTTP 404 or 410 at the pinned URL | "The file is no longer at its source. A Marrow update will fix this." | tries once more; Retry stays |
| Checksum or size mismatch | "The file didn't match its checksum." | deletes the `.part`, downloads again |
| Disk full | "Needs 1.2 GB free on C:." | rechecks space first |
| Install step failed (msiexec exit code, unpack error) | "Couldn't unpack LibreOffice (code 1603)." | reinstalls from the verified download if still present |
| Cancelled by the user | (no failure; the row returns to Download, with "Resume" if a `.part` exists) | n/a |

---

## 4. Pointing each provider at what is installed

### 4.1 Skills

`PLAN.md` 2.1 to 2.4 and 2.7 stand unchanged, with one change of source: the store is filled by downloads (3.4) instead of a Velopack payload, so the trust root becomes the embedded catalogue's per-file hashes instead of the embedded bundle manifest. Per provider (SF and evidence levels in research-loading.md section 1 and PLAN.md 2.2):

| Provider | How it gets the installed, enabled skills |
|---|---|
| Claude | `--plugin-dir <views\claude\chat-<id>>` (Start and Handshake), when `claude --help` lists it; skills appear as `marrow:<name>` |
| Codex | `skills/extraRoots/set` after `initialize` on every connection (0.136.0+; error tolerated); `skills/config/write` never sent |
| Copilot | `skillDirectories` in `session.create` and `session.resume`, retried once without it on `-32602` |
| OpenCode | `OPENCODE_CONFIG_CONTENT` with the user's effective `skills.paths` copied verbatim plus Marrow's path |
| Pi | `--skill <path>` on live sessions (the probe keeps `--no-skills`) |
| Command Code | `--skill <path>` when `--help` lists it |
| Grok | `--plugin-dir` with its own generation, after the live check |
| Antigravity, Cursor | Q-SKILLS-8 A: a removable Marrow-owned plugin only after the live test; until then explicit pick |
| Devin, API providers | Marrow-side loading: a catalogue block in the system prompt and the `use_skill` tool (API providers), explicit pick (Devin) |

Name clashes: the user's own skill and the vendor's own copy always win (PLAN.md 2.4), and the equivalence groups `{docx, documents, doc}`, `{xlsx, spreadsheets, spreadsheet}`, `{pptx, presentations, slides}`, `{pdf}` stay, so a Claude user whose account already syncs `docx` does not also get A2's `docx` twice. A2 and Claude's sync are the same files; the Settings row says "Your Claude account provides this" when the sync is present (PLAN.md 3.1).

### 4.2 Tools

Every provider child gets `addons\bin` on `PATH` by the 3.6 rule, including Codex's app-server (its command executions inherit it) and the native providers' `run_command`. (UN, carried from PLAN.md 2.2) Codex's elevated Windows sandbox runs commands as separate local users; whether they can read and run `addons\` under `AppData\Local\Marrow Code` is checked live in DL-20, with PLAN.md's fallback (a per-thread readable root through `config`).

### 4.3 What the providers already bring

The read-only section "Already available through your providers" (PLAN.md Q8) lists what the providers report (Claude's synced skills, Codex's system skills and any desktop-runtime plugins, the user's own); nothing there is downloaded or copied.

### 4.4 Skill changes during a chat

A running session keeps its generation (PLAN.md 2.1). A skill set installed during a chat reaches new chats and resumed chats; (ED) the Settings row and the offer's done state say "Ready in new chats", and the offer for a skill set (rare: from `request_tool` or a later slash-search feature) has a "Restart this chat with it" action that ends and resumes the chat with a fresh generation at its stable path. Tools need nothing of this: the shim folder is already on `PATH`.

---

## 5. A chat task needs a missing tool

### 5.1 Signals

| # | Signal | Where it comes from | When it fires |
|---|---|---|---|
| S1 | **A skill's declared needs** | The skill being used: Claude `Skill` tool calls (`Skill · <name>` rows, ClaudeSession), Codex `{type:"skill"}` items, Copilot and Command Code skill rows, Marrow-side `use_skill`. The catalogue's per-skill `needs` | The skill is used and one of its needs is neither installed nor found on this PC |
| S2 | **A held command** | Approval requests: Claude `can_use_tool` (permission prompt over stdio), Codex `item/commandExecution/requestApproval`, ACP `session/request_permission`, the native providers' approval for `run_command`. The command line is parsed (5.3) | The first program of any pipeline segment is a catalogue `provides` name, not installed and not found |
| S3 | **A failed command** | Claude `tool_result` for Bash/PowerShell (`is_error` and text), Codex `item/completed` commandExecution (`command`, `exitCode`, `aggregatedOutput`), Copilot and ACP tool updates, Pi tool results, Command Code's per-turn JSON, native `run_command` results | The output and exit code match a missing-program or missing-module pattern for a catalogue name (5.3) |
| S4 | **The model asks** | (ED) A Marrow tool `request_tool {name, reason}` for the native API providers, which blocks until the user answers and returns "installed at <path>" or "declined". For the CLIs, the same tool through a Marrow-owned MCP server is a later option, only after a live check (DL-20), because it adds a server to every session | The model calls it |

(ED) Natural-language guessing ("LibreOffice is not installed" in the model's prose) is not a signal: it is unreliable, and S3 already sees the failure that led to the sentence.

### 5.2 Event sources per provider

All of these events already arrive in the port's session classes (SF, research-loading.md 1 and 3; PLAN.md 2.2): ClaudeSession (stream-json `tool_use`, `tool_result`, `control_request can_use_tool`), CodexSession (`item/started`, `item/completed`, approval requests), CopilotSession and ACPSession (tool call updates, permission requests), PiSession (RPC tool events), CommandCodeSession (per-turn JSON), OpenAICompatibleSession (its own loop). (ED) A small adapter per session turns them into one normalized event (`CommandHeld`, `CommandFinished`, `SkillUsed`, `ToolRequested`) that the detector reads; the detector never sees provider JSON.

### 5.3 Matching (deterministic, no guessing)

- **Command parsing:** split the command line into pipeline and `&&`/`;` segments with a quote-aware tokenizer (cmd, PowerShell and bash quoting); take each segment's first word; strip a path and an extension (`C:\x\soffice.exe` becomes `soffice`); skip leading `cd`, `&`, `call`, `start`, `env` assignments and `py -3 -m <module>` (the module counts).
- **Missing program** (any locale): cmd exit code 9009; PowerShell `CommandNotFoundException` (an identifier, not translated); bash exit code 127 with "command not found"; the Store alias (exit 9009 for `python`/`python3`). The program must also be a token of the command.
- **Missing module:** Python `ModuleNotFoundError: No module named '<m>'` (not localised); Node `MODULE_NOT_FOUND` / `ERR_MODULE_NOT_FOUND` with `Cannot find module '<m>'` or `Cannot find package '<m>'`; Playwright "Executable doesn't exist at" naming its browsers path.
- **Lookup:** the token goes through the catalogue's `provides` index to one item (for a module, the smallest set that provides it). No hit, no offer.
- **Guards:** no offer when the item is installed and healthy, found on this PC, already offered in this chat, declined in this chat ("Don't ask again in this chat" is the decline's meaning), or currently downloading (the offer then attaches to that download's progress). One offer at a time per chat; a second missing item joins the same card as "also needs".

### 5.4 The offer's states

`asked` → (yes) `downloading` (bytes and percent, with Cancel) → `checking` (hash and unpack) → `done` → `continuing`; `asked` → (no) `declined`; `downloading` or `checking` → `failed` (with the reason and Retry) → (Retry) `downloading`. A cancel returns to `asked`. Items with non-open terms add the terms step inside `asked`. An item that needs others (P1 needs T1) is offered as one download with its combined size.

### 5.5 How the task goes on

| Situation | What Marrow does after the download |
|---|---|
| **R1: the command is held** (S2: approval modes) | Approves the held request: the command runs in the same turn, now finding the tool through `addons\bin`. A decline answers the request as denied with a reason the model can read where the protocol carries one (Claude's deny message): "LibreOffice isn't on this PC and the user chose not to download it now." |
| **R2: the command failed and the turn is still running** (S3 in full-access modes, or after a provider ran it unasked) | Queues a continuation message with the port's existing queue (sent when the turn ends, as Return does while heads are at work) |
| **R3: the turn has ended** (the agent gave up) | Sends the continuation message as the next turn at once |
| **R4: the model asked** (S4) | Returns the tool result; the model carries on in the same turn |

The continuation message (ED, word set S1; shown in the timeline as a Marrow note, sent to the provider as the user's message): "LibreOffice 26.2.6 is installed now (`soffice` is on PATH). Please continue the task from where it stopped." It names the executable so the model retries the same step; it is sent once per offer and never when the user declined.

(ED) Decline sends nothing to the provider in R2 and R3: the agent has already seen its own failure. The card collapses to one line that says where the item can be downloaded later.

### 5.6 What the user sees

The offer's look (inline card in the reply, composer notice, approval-style card, or a pill on the failed step) and its words are the owner's pick on the design page. Whatever is picked: the card names the item, why it is needed (the step it blocks), the size and the source host; it never starts a download without the user's yes; every change of state is transitioned (OD-0076); Reduce Motion stills the movement to fades.

---

## 6. The Settings page

Designed on the decision page in five variants (A list with sections, B tabs, C gallery, D library and detail, E by task), three word sets and three names ("Skills", "Plugins", "Skills and tools"), each drawn in every state the ruling asks for: nothing installed, browsing the catalogue, downloading (progress and size), installed, update available, failed with Retry, remove. Common to all variants (ED):
- placement in the Settings sidebar below MCP (owner may move it);
- what each row carries (2.5): title, Marrow's one-liner, source and licence as text, size, needs, the action;
- per-skill switches inside an installed set (PLAN.md enabled.json and the 2.7 cap), "applies to new chats";
- "Found on this PC" and "Already available through your providers" (4.3);
- "Reset skills and tools" and the not-affiliated footer.

---

## 7. Build units (ordered, test-first, offline)

Rules for every unit: `porting/IMPLEMENTING.md` (tests first, adversarial review, then code; own worktree and packet). **No network in tests**: the harness refuses every socket except the fake server's loopback port (a .NET `SocketsHttpHandler.ConnectCallback` guard in the test host, the Python `sitecustomize` and Node preload guards of ORIGINALS 6.1 for any child). Tests never touch the real `%USERPROFILE%`, `%LOCALAPPDATA%`, provider folders or real CLIs (CLI seams fail closed). Fixture artifacts are tiny Marrow-made zips, wheels, tarballs and skill folders; no vendor file is a fixture.

| # | Unit | Tests first: what each proves | Size |
|---|---|---|---|
| DL-0 | Test harness: fake download server and network guard | A loopback HTTPS server (test certificate trusted only by the test's handler) serves fixture artifacts with switchable behaviour: `Accept-Ranges` on or off, a drop after N bytes, a wrong byte, a wrong length, 404, 500, a slow trickle, a redirect to another loopback host and to plain HTTP. A test proves the guard refuses any non-loopback connection and that the seam fails closed when no fake is configured | M |
| DL-1 | Catalogue schema, embedded catalogue and its gate (Core) | Every item has id, kind, pin, URLs, sizes, SHA-256s, licence name and URL, terms class, needs, provides; ids are unique; `needs` form no cycle and name existing ids; every `provides` name maps to exactly one tool or the smallest package set; every skill folder name equals its frontmatter name rule (agentskills.io); hosts are on the allow-list; Marrow summaries share no 12-word run with the vendor's own description (the G3 tool of W0-1); counts are exact (10 sets, 615 skills, 11 tools, 6 sets) | M |
| DL-2 | Catalogue builder (developer script, network allowed because it is not a test) and its offline check | Run by hand: reads the pins, downloads, verifies signatures (Sigstore, Node SHASUMS signature, LibreOffice `.asc`), records sizes and hashes, writes `catalogue.json` and a change report for the owner. Offline test: a fixture "previous" and "next" catalogue produce a change report listing every version, hash, size and licence-text change; a licence change in a non-open item is flagged "asks the terms again" | M |
| DL-3 | Downloader (Services) | Against DL-0: full download with progress events; resume after a drop with `Range` (bytes not refetched, checked by the server's log); restart from zero without `Accept-Ranges`; size and hash mismatch delete the `.part` and fail with the right reason; 404, 500, TLS failure and offline map to 3.9's messages; plain-HTTP redirect refused; first URL off the allow-list refused before any connection; cancel keeps the `.part`; per-file skill-set fetch with six in flight and one bad file failing the set; disk-space check fails before connecting; progress at least every 100 ms on a slow trickle | L |
| DL-4 | Installers by kind | `zip` with a zip-slip entry and a reparse point is refused; `python-embed` writes the `._pth` exactly and a fixture embeddable Python (a tiny Marrow-built stand-in exe in unit tests; the real 3.14.8 zip only in DL-20) runs; `pyset` uses only local wheels with `--require-hashes` (a wheel with a changed hash fails); `nodeset` lays out `node_modules` and never runs a lifecycle script (a fixture `postinstall` that would write a file leaves no file); `msi-admin` builds the right `msiexec` argv and handles exit codes (fake msiexec); `sfx-7z` argv; `skillset` lays out `store\<source>@<pin8>\<skill>` and builds spares. Every kind: build in `tmp-`, rename, lock, verify; a crash injected at each step leaves only `tmp-` folders | L |
| DL-5 | Store, lock, verify, GC (adapts PLAN.md WU-4 and ORIGINALS W0-4) | PLAN.md WU-4's lock, tamper, extra-file, lease, GC and teardown tests on `addons\tools` and `skills\store`; seal check per launch; a damaged item is reported "damaged" and never used; "Repair" re-downloads the same pin; the unlock order removes everything; a held handle defers deletion to the next GC | L |
| DL-6 | Shims and the PATH rule | The shim passes arguments (with spaces, quotes, Unicode), stdin, stdout, stderr and the exit code through unchanged, from cmd, PowerShell and Git Bash (fake targets); killing the shim kills the child tree; the Python shim sets `PYTHONUTF8=1` and the package-set paths only for its child; the node shim sets `NODE_PATH`; the soffice shim gives each call a fresh profile and deletes it. PATH: `addons\bin` is inserted before the first WindowsApps entry, or appended; a user's real `python` earlier in PATH still resolves first; with only the Store alias, Marrow's resolves; the folder is present even when empty; a shim added after launch is found by the next process of a running fake provider | M |
| DL-7 | State model (Core) | `state.json` round trip; item states (available, queued, downloading, checking, installed, update available, failed, damaged, removing) and their legal transitions; dependents of an item; "update available" computed from the catalogue pin after an app update; accepted terms keyed by licence hash; per-chat declined list; found-on-this-PC results merged | M |
| DL-8 | Update and remove pipelines (+ OD-0100) | A new pin installs beside the old; shims and new generations switch in one step; a leased old version survives until its lease ends, then GC deletes it; a terms change asks again; "update everything" receives the group with per-item progress and results (fake runner); removing Python removes P1 to P5 and leaves skill sets with a missing need; remove while leased; Reset skills and tools | M |
| DL-9 | Skill sets into generations (adapts PLAN.md WU-4, WU-5, WU-14, WU-21) | Downloaded sets feed the per-chat generations with provider filters, clash rules (equivalence groups included), the 2.7 cap and `enabled.json`; default switches per 2.3 (`defaultOn`), large sets installed with skills off; a set installed during a chat does not change that chat's generation; a resumed chat gets it | M |
| DL-10 | Missing-tool detector (pure, Core) | Over recorded fixture events for each provider shape: the 5.3 patterns in English and German Windows messages (exit codes 9009 and 127, `CommandNotFoundException`), the Store alias, Python and Node missing modules, Playwright's missing browser; the tokenizer on cmd, PowerShell and bash quoting, pipelines, `&&`, `py -3 -m`, `C:\...\soffice.exe`; guards: installed, found on PC, already offered, declined, downloading; one card for two missing items; no hit for an unknown program; no offer from prose alone | M |
| DL-11 | Provider wiring (reuses PLAN.md WU-6, WU-7, WU-8, WU-9, WU-10, WU-12; six units) | PLAN.md's own tests for each, with the generation source switched to downloaded sets; plus: every provider's child environment carries `addons\bin` by the DL-6 rule and nothing else changes; with nothing installed, argv, environment and RPC traffic equal today's (the Mac's) | 6 × M |
| DL-12 | Event taps per provider | Each session class emits the normalized `CommandHeld`, `CommandFinished`, `SkillUsed` events from recorded fixture transcripts (Claude stream-json, Codex app-server, Copilot, ACP, Pi, Command Code, native); nothing else in the session changes | M |
| DL-13 | Offer flow and resume | State machine of 5.4 with a fake downloader; R1: a held fake approval is approved after `done` and denied with the reason after decline (Claude shape carries the text); R2: the continuation is queued, sent once, after the turn ends; R3: sent at once; cancel returns to asked; failure and Retry; one offer per item per chat; continuation text names the executable; nothing is sent after decline | L |
| DL-14 | `request_tool` for the native API providers | Schema; the tool call blocks until the user answers; returns the installed path or "declined"; refuses names outside the catalogue; never downloads without the offer | S |
| DL-15 | Terms and records | Non-open items require the terms step before any byte is fetched; acceptance stored with date and licence hash; a changed licence hash asks again; the Settings footer text; no vendor logo or image referenced anywhere (text test) | S |
| DL-16 | Uninstall hook and Reset | PLAN.md WU-18's hook tests extended to `addons\`: everything unlocked before anything is deleted, within 10 s on a fake root with held files; Reset keeps `enabled.json` and accepted terms | S |
| DL-17 | Settings page, built to the owner's pick | Headless Avalonia tests: every state of the picked variant, rows, switches, sizes, needs, found-on-PC, providers section, footer; transitions on the frame clock (OD-0076) with no single-frame jumps; Reduce Motion; no overlap while scrolling (OD-0069), OD-0071 scrolling | L |
| DL-18 | In-chat offer, built to the owner's pick | Headless tests of asked, downloading, checking, done and continuing, declined, failed and Retry; transitions; the offer never covers the composer's text; keyboard: Return confirms the primary action only when the card has focus | M |
| DL-19 | Wiring the app together | Settings and offers share one download per item (an offer attaches to a Settings download in progress and the reverse); progress in both places; app restart during a download resumes from the `.part` on the next Download press, never on its own | M |
| DL-20 | Live checks (manual, recorded under `porting/evidence/`, not tests) | Each pinned artifact downloaded once from its real source and verified; `msiexec /a` without elevation; Python 3.14.8 `._pth`, the site `pip.ini` target, `PYTHONUTF8` through the shim; Chromium layout accepted by playwright 1.63.0; `NODE_PATH` for A2's Node scripts; one real chat per signal (S1 to S4) under Claude and Codex, including Codex's elevated sandbox reading `addons\`; A2's known Windows limits observed and recorded; sizes on disk measured for 2.2's estimates | L |

**Order:** DL-0, DL-1, DL-7; then DL-3, DL-4, DL-5, DL-6 (parallel after DL-3); DL-2 any time after DL-1; DL-8, DL-9; DL-11 (six provider units in parallel); DL-10, DL-12, DL-13, DL-14, DL-15, DL-16; DL-17 and DL-18 only after the owner's pick on the design page; DL-19; DL-20 at the end and wherever a UN above blocks a choice. **21 units** (DL-11 counts as one here and holds six of PLAN.md's).

**Recording:** a new engineering decision for on-demand downloads, `deviation` fields on the touched requirements (the PATH insertion, the shim folder, the provider arguments of PLAN.md 3.3, the offer and the continuation message, `request_tool`), and the DL-20 evidence.

---

## 8. What stays useful from ORIGINALS-PLAN and Wave 0

| Part | Status now |
|---|---|
| ORIGINALS 1, clean-room rules; 2.3, the 120-skill catalogue; Waves 1 to 8 | **Kept for later** (ruling effect 3). When a Marrow skill is built, it becomes a catalogue item like any other, delivered the same way |
| W0-1 clean-room kit (already built) | **Kept.** Its G3 twelve-word overlap tool is reused by DL-1 to keep Marrow's catalogue summaries original |
| W0-2 manifest, gates, store | **Reused, adapted:** the store, lock, generations and GC become DL-5 and DL-9; the manifest's per-file hashes become the catalogue's; the 992-name G1 list waits for Marrow's own skills |
| W0-3 runtime builder | **Superseded as a ship step**, **reused as method:** the same official sources (python.org, nodejs.org, the LibreOffice MSI with `msiexec /a`, PyPI wheels, npm tarballs unpacked without npm) and signature checks move into DL-2's catalogue builder and DL-4's installers. G5's licence allow-list is no longer a gate (Marrow ships none of it); licences are shown per item |
| W0-4 runtime store and launcher | **Reused:** extraction, lock, seal and full verify, GC, uninstall extension (DL-5, DL-16). `marrow-run.exe` as the only launcher is **replaced** by shims under the real executable names (DL-6), because unmodified vendor skills call `python`, `soffice` and `node` directly. `marrow-run`'s OCR, speech and capture wait for Marrow's own skills |
| W0-5 `marrow_office`, `marrow_web` | **Deferred** with Marrow's own skills |
| W0-6 sample corpus | **Kept:** Marrow-made samples are the input files for DL-20's live document checks |
| ORIGINALS 3.2 package set | **Reused** as the source of P1 to P5, now pinned to the 2026-10-04 versions |
| ORIGINALS 3.3 `._pth`, precompiled bytecode | **Kept** (3.4) |
| ORIGINALS 4 Figma | Superseded by OD-0099 Q4 (providers' own Figma sign-in) |
| PLAN.md 2.1 to 2.4, 2.7, 3.1 to 3.3, WU-4 to WU-21 | **Kept** with the source change of 4.1; WU-2 and WU-3 (fetch and patch the bundle) are dropped |

---

## 9. Owner question

**Q-OD-1. Vendor items whose own terms limit copies.** Anthropic's four document skills (A2) say users may not keep copies outside Anthropic's services, make derivatives or distribute them; their use follows the user's own agreement with Anthropic. OpenAI's proprietary-labelled plugins (O5) say "Proprietary", one of them "internal use only". The ruling says each item stays under its source's terms. Options:
- (a) Offer A2 and O5 as downloads, with their terms shown and accepted by the user first (2.5).
- (b) Do not download them. Claude users signed in with a claude.ai account already get A2 from Anthropic's own sync, and Marrow shows them there (4.3); everyone else uses O3, OpenAI's open document skills.
- (c) (b) for A2, (a) for O5.

**Recommendation: (b).** It keeps every download inside its terms, Claude's own sync already delivers the same A2 files where they are licensed, and A2's LibreOffice scripts fail on Windows anyway (2.6), so O3 plus the tools is the working document path on Windows today.

---

## 10. Sources

- Owner ruling: `E:\Droppy\wt\cl\terminal-verify\porting\owner_decisions.md` OD-0099 (latest amendment), OD-0100, OD-0101, OD-0070, OD-0076.
- This folder: `PLAN.md` revision 3, `ORIGINALS-PLAN.md`, `research-anthropic.md`, `research-openai.md`, `research-loading.md`.
- Read on 2026-10-04: `https://nodejs.org/dist/latest-v24.x/` (node-v24.21.0-win-x64.zip, 38 MB); `https://www.python.org/ftp/python/3.14.8/` (embed zip, Content-Length 12,601,679); `https://download.documentfoundation.org/libreoffice/stable/` (26.2.6 MSI 356M, 25.8.7 MSI 349M, 26.8.1 Windows folder empty); GitHub releases API (`qpdf/qpdf` v12.4.2, `jgm/pandoc` 3.12, `git-for-windows/git` v2.56.0.windows.1, `cli/cli` v2.102.0, `astral-sh/uv` 0.12.23, `oschwartz10612/poppler-windows` v26.09.0-0), with the published SHA-256 digests; `https://pypi.org/pypi/<name>/json` for every wheel listed; `https://registry.npmjs.org/<name>/latest` for P6; `https://unpkg.com/playwright-core@1.63.0/browsers.json` and HEAD requests on `cdn.playwright.dev` (205,123,748 and 120,200,717 bytes); GitHub git-tree API at each pin in 2.1 for sizes and counts.
- Port code named: `RequestCards.cs` (ApprovalCard, the shape the approval-style offer follows), the session classes named in 5.2 (research-loading.md section 1 for line numbers).
