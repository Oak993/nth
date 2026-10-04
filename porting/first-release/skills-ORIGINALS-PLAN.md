# Marrow's own skills: build plan (owner ruling of 2026-10-04)

Date: 2026-10-04. Status: plan only. Nothing is built, installed or committed.

**Owner ruling (2026-10-04, in chat, paraphrased from Hindi; the lead records it as an owner decision):** do not ask Anthropic or OpenAI for permission and do not ship their skills. Study the skills and plugins they publish, including the document ones (Word, Excel, PDF, slides, document co-authoring), and build Marrow's own original skills with the full functionality, so that no vendor licence or third-party dependency is needed. Integrate all of them into the app and ship them ready to use, even if the app grows to about 1 GB. Bundle the basic tools the skills need (Python, Node, LibreOffice) instead of asking users to install them. A Figma skill is an ordinary skill: when someone uses it, Figma's own sign-in appears on screen, they authenticate, and it works.
Also answered on the same day:
- **Q-SKILLS-1 A:** install all of them. Switch on the document, review, security, GitHub, design and skill-building ones. Skills that need a key, a login or internet start off, with the need shown.
- **Q-SKILLS-4 A:** no brand-guidelines skill.
- **Q-SKILLS-8 A:** one removable Marrow-owned plugin in Antigravity's and Cursor's plugin folders, and only after a live test shows it works.

**What this changes in `PLAN.md` (revision 3).** The 50 upstream folders in PLAN.md 1.2 are no longer shipped, and that includes the Apache-2.0 and MIT ones.
- Superseded: PLAN.md section 1 (the bundle, the pins, the patches, 1.2 E), 1.3 and 1.4, 2.6 ("detect, do not bundle"), WU-2 (fetch and pin), WU-3 (upstream patches), and owner questions Q2 to Q5, Q12, Q14 and Q16.
- Kept, with the changes listed in section 6 Track P: the installation machinery (2.1 store, lock, per-chat generations; 2.2 provider wiring; 2.3 filters; 2.4 name clashes; 2.5 updates; 2.7 catalogue budget), the app (section 3) and WU-1 and WU-4 to WU-21.

**Labels used below (from the port's rules):**
- **SF** (source fact): read in a primary source, which is named.
- **ED** (engineering decision): a choice made here, with its reason.
- **UN** (unresolved): not yet proven; the unit that settles it is named.

---

## 0. The answer in one screen

1. **What Anthropic and OpenAI publish.** About 1,110 skill entries in all: 470 on Anthropic's side and 640 on OpenAI's, plus 26 third-party entries that reach Codex through OpenAI's plugin directory. They hold **992 distinct skill names** (SF, counted from the clones and lists in section 2.1). Most are duplicates, vendor-product guides, or skills tied to a vendor's own app.
2. **What Marrow builds: 120 original skills** in 8 groups (section 2.3):
   - Documents: 12
   - Design and media: 18
   - Build, test and review: 37
   - Data: 3
   - Business work: 17
   - Finance: 6
   - Science: 6
   - Integrations: 21

   Of these, 19 are on by default, following Q-SKILLS-1 A, and 18 of those 19 enter the model's catalogue. 31 start off because their main use needs a key, a login or internet. The rest start off because the catalogue budget is small (PLAN.md 2.7) or, for one, because it needs a local app (section 2.3).
3. **Clean room (section 1).** Two roles. *Readers* study what the published skills do and write a capability spec in Marrow's own words. *Implementers* build only from that spec, public standards and library documentation, and never open a vendor skill. Mechanical gates reject:
   - any skill name already used upstream (992 names);
   - any upstream script file name;
   - any 12-word run of text shared with an upstream skill;
   - copied code;
   - any shipped component whose licence is not on the allow-list.

   Each skill carries a provenance record.
4. **Bundled tools (section 3):**
   - Python 3.14 embeddable with about 60 open-source packages (python-docx, openpyxl, python-pptx, XlsxWriter, pypdf, pdfplumber, pypdfium2, reportlab, lxml, Pillow, pandas, matplotlib, Jupyter and others).
   - `node.exe` only, without npm (npm's licence is Artistic-2.0), plus playwright-core, esbuild, React and a Mermaid bundle.
   - A trimmed headless LibreOffice.
   - Tectonic for LaTeX.
   - One Marrow-built helper, `marrow-run.exe`. It launches the tools and uses Windows' own OCR, speech and screen capture.

   No Poppler, pandoc, Git or FFmpeg is bundled, because their licences are GPL or LGPL.

   **Size, estimated:** about 0.95 GB unpacked (range 0.8 to 1.2), a download about 350 to 450 MB larger, and about 1.4 GB on disk. Section 3.4 has the details; WU-R1 measures it.
5. **Figma (section 4).** There are three routes, and the skill picks whichever works:
   1. Figma's remote MCP server signed in through the provider CLI. Claude Code, Codex and Cursor are on Figma's allow-list of MCP clients; Marrow itself is not.
   2. The Figma desktop app's local MCP server.
   3. Figma's REST API with a personal access token, stored in Windows Credential Manager.

   Figma's sign-in page opens in the user's browser for route 1, and the desktop app's own sign-in covers route 2.
6. **Waves (section 6):**
   - Wave 0: foundations (6 units).
   - Waves 1 to 8: the skills, documents first.
   - Track P: provider wiring and the app, run in parallel. It reuses PLAN.md's work units.

   9 waves in all (0 to 8), plus Track P. About 200 agent-days in total (section 6.4).
7. **Open questions for the owner:** 4 (section 7). They are: licence exceptions for parts of LibreOffice and some wheels; the licence of Marrow's own skill text; whether Marrow's document skills stand next to a vendor's own; and whether to register a Marrow Figma app.

---

## 1. Clean-room rules (legal originality)

### 1.1 What may be read, by whom

| Source class | Examples | A reader may use | An implementer may use |
|---|---|---|---|
| Proprietary or unlicensed vendor skills | Anthropic `docx`, `xlsx`, `pptx`, `pdf` (licence forbids copying, derivative works and keeping copies outside Anthropic's services; research-anthropic.md 3.2); `doc-coauthoring` (no licence); Claude Desktop-only skills; Claude Code's compiled skills; OpenAI's runtime plugins `documents`, `presentations`, `spreadsheets`, `pdf` and `template-creator`; OpenAI's Proprietary plugins (`codex-security`, `data-analytics`, `product-design`, `public-equity-investing`, `life-science-research` and others); all Figma skills (Figma Developer Terms) | **Only public descriptions:** the skill's name, its one-line description as shown on the public repo or marketplace page, and the vendor's public product documentation and help pages. **Never** the SKILL.md body, references, scripts, templates or examples | Nothing from these |
| Open vendor skills (Apache-2.0, MIT) | `anthropics/skills` (14 Apache-2.0 folders), `knowledge-work-plugins`, `financial-services`, `life-sciences`, `openai/skills` and its history, MIT plugins in `openai/plugins` | The full text, but to learn **what** each skill does and which public formats and APIs it relies on. Notes are written in Marrow's words, with no quotations | Nothing from these |
| Public standards and specifications | ECMA-376 / ISO/IEC 29500 (OOXML), Microsoft's Open Specifications [MS-DOCX], [MS-XLSX] and [MS-PPTX], ISO 32000-2 (PDF 2.0), OASIS ODF 1.3, RFC 4180 (CSV), CommonMark, the Agent Skills spec (agentskills.io), the MCP specification, Figma's REST and MCP docs, vendors' public API references | Yes | Yes |
| Library documentation | The docs of every library in section 3 | Yes | Yes |
| Marrow's capability spec (CS-*) | Written by the reader | It is the reader's own output | Yes. **This is the implementer's only view of the vendor skill** |

(ED) The proprietary row is stricter than copyright alone requires. Anthropic's licence also forbids "inventions embodied in these materials" being made or sold, and forbids keeping copies outside its services (research-anthropic.md 3.2, SF). Learning only from public descriptions keeps Marrow's work clear of both clauses.

### 1.2 Hygiene before Wave 0

- (ED) The research clone `...\E--Droppy\c13e7b6a-...\scratchpad\skills` holds Anthropic's four proprietary document skills, which their licence says must not be kept outside Anthropic's services. The lead deletes that clone, or at least its `skills/docx`, `skills/pdf`, `skills/pptx` and `skills/xlsx` folders, before any agent of this plan starts.
- No work packet may name the scratch clones, `~\.claude\skills\synced\`, the Claude Desktop `skills-plugin` folder, `~\.codex\skills\`, `~\.codex\plugins\cache\` or `~\.cache\codex-runtimes\`. Each implementer packet lists its allowed sources, and the agent's final report states that it opened nothing else.

### 1.3 Writing rules (every Marrow skill)

1. **New words, new structure.**
   - SKILL.md and every guide is written fresh, in Marrow's voice: plain English, short sentences, Windows first.
   - The section skeleton is Marrow's own:

     ```
     When to use / What you need / How to do it / Check your work / Hand back
     ```

   - Subfolders are `tools/` (scripts), `guides/` (references) and `samples/` (assets). Upstream uses `scripts/`, `references/` and `assets/`. The Agent Skills spec makes these folder names conventions, not requirements (SF, research-anthropic.md 6.1).
2. **No copying of any kind.** That covers prompts, instruction text, examples, sample data, template wording, script code, function or file names of upstream scripts, and the order of sections.
3. **Names.**
   - Every skill name is new: none of the 992 upstream names (gate G1).
   - Every script name is new: none of the upstream script basenames, such as `recalc.py`, `soffice.py`, `thumbnail.py`, `render_docx.py`, `inventory.py`, `pack.py` and `unpack.py` (gate G2).
   - Marrow's script names follow `<verb>_<object>.py` or `.mjs`.
4. **Windows first.**
   - Every command is shown in a PowerShell form and a bash form.
   - Tools are always called through `marrow-run` (section 3.3), so no skill ever says `python3`, `/tmp`, `export VAR=` or `soffice`.
   - Every Python file opens text with `encoding="utf-8"` and sets stdout and stderr to UTF-8. These are PLAN.md's WU-3 rules, kept.
5. **Every skill has at least one tool.** Each skill ships at least one deterministic script that does its mechanical part: render, convert, validate, lint, build or report. That script is what the headless acceptance test exercises (section 6.2). Guidance-only text is tested by the recorded live runs.
6. **Vendor names.** Products are named only as plain text, to say what a skill works with ("Stripe", "Figma"). No logos and no brand colours. The page carries the not-affiliated line. A vendor name may appear inside a skill name only for the integration skills (group I), where it is descriptive.
7. **Shared code.**
   - Document helpers live once, in Marrow's own Python package `marrow_office` inside the runtime (W0-5). Skills call it rather than repeating code.
   - Web helpers live in `marrow_web` (Node).
   - Both are Marrow originals, under the same rules.

### 1.4 Mechanical gates (part of WU-1; every build fails on any of them)

| Gate | Rule |
|---|---|
| G1 name | The skill name is not in the upstream name list (992 names at the pins below), is lowercase with single hyphens, at most 64 characters, and matches its folder. (SF) The 120 names in section 2.3 were checked against the list on 2026-10-04 and none collides |
| G2 file names | No file basename in a skill equals an upstream script or reference basename, from a reviewed generic list (`README.md`, `LICENSE.txt` and similar are allowed) |
| G3 text similarity | No run of 12 consecutive words (after normalising case, punctuation and whitespace) shared with any upstream SKILL.md, reference or example, outside a reviewed allow-list of unavoidable technical strings (XML element names, CLI flags, API paths, library call names, standard titles) |
| G4 code similarity | No normalised sequence of 6 or more identical lines shared with an upstream script, and per-file winnowing-fingerprint similarity below 15% against every upstream script |
| G5 licence | Every shipped third-party file belongs to a component on the reviewed allow-list (section 3.5), its licence text is carried, and it appears in THIRD_PARTY_NOTICES |
| G6 provenance | `origin.json` is complete (1.5) |
| G7 content review | PLAN.md's WU-1 checks are kept: frontmatter key allow-list, no `` !` `` injection, one SKILL.md per folder, no plugin, MCP or hook files, archive and media review |
| G8 Windows | PLAN.md's WU-3 static and runtime gates (encoding, output, POSIX-only lines), now applied to Marrow's own files |

**How G3 and G4 are run (ED).**
- A compliance job fetches the upstream repositories at the pins in 2.1 into memory or a temporary folder, compares, deletes the copies, and keeps only the numeric report: the matched spans as offsets and hashes, never as text.
- The proprietary four are compared the same way, transiently, from the public repo.
- (UN) Whether a transient comparison copy is acceptable under Anthropic's "retain copies" clause. If the lead prefers, those four are left out of G3 and G4, and the reader and implementer separation (1.1) stands on its own for them.

### 1.5 Provenance record (`origin.json`, one per skill, shipped)

It records:
- the capability spec id;
- each upstream capability source studied (repository, path, pinned SHA, licence class, and what was read: "description only" or "full text");
- the public standards used;
- each library used, with version, SPDX id and licence-file hash;
- the author agents and models (reader, implementer, reviewer);
- the hashes of the G1 to G8 reports;
- the date.

`ATTRIBUTION.md` and the Skills section of THIRD_PARTY_NOTICES are generated from these records and from the runtime manifest.

### 1.6 What stays true without any vendor licence

(SF, from the research files)
- The Agent Skills folder format is an open standard: agentskills.io, code Apache-2.0, docs CC-BY-4.0. Marrow uses the format and copies none of its text.
- Public file formats and public APIs are free to implement.
- Marrow skill text is Marrow's copyright, and its licence is owner question Q2.
- Libraries are used under their own licences (section 3.5).
- Nothing is downloaded from Anthropic or OpenAI at build or run time.

---

## 2. The catalogue

### 2.1 What was studied (counts; pins re-checked 2026-10-04 with `git ls-remote`)

(SF) The pins below match the remote HEAD on 2026-10-04 except where noted. The counts come from the clones in the earlier research scratchpad and from the research files.

**Anthropic**

| Source | Pin | Skill entries | Notes |
|---|---|---|---|
| `anthropics/skills` | `8a1541c` (HEAD unchanged) | 19 | 14 Apache-2.0, 4 proprietary (`docx`, `xlsx`, `pptx`, `pdf`), 1 unlicensed (`doc-coauthoring`) |
| Claude Desktop pre-installed (not in any repo) | local manifest | 13 more | `built-in-browser`, `chrome-browser`, `computer-use`, `docs`, `google-workspace`, `deep-research`, `import-memory`, `morning`, `schedule`, `setup-claude`, `setup-cowork`, `consolidate-memory`, `explain-usage` (plus the repo's 4 document skills and `skill-creator`) |
| Claude Code compiled skills | CLI 2.1.287/288 | about 25 | 20 marked "Skill" in the docs, plus `keybindings-help`, `artifact-design`, `plugin-authoring`, `init` and `security-review` (research-anthropic.md 2c) |
| `anthropics/claude-plugins-official` | `ab024cd`. HEAD is now `d182ca4`; the SKILL.md list is identical (checked through the GitHub tree API) | 33 | 27 in `plugins/`, 6 in `external_plugins/` |
| `anthropics/claude-code` `plugins/` | `52c7644` | 10 | All duplicates of the above |
| `anthropics/knowledge-work-plugins` | `8444efc` (HEAD unchanged) | 252 | sales 36, small-business 44, Zoom 57, data 10, engineering 10, legal 9, HR 9, operations 9, finance 8, marketing 8, product 8, design 7, bio 6, Common Room 6, support 5, enterprise-search 5, productivity 4, Apollo 3, brand-voice 3, cowork 2, Slack 2, pdf-viewer 1 |
| `anthropics/financial-services` | `574ed36` (HEAD unchanged) | 112 | About 57 distinct names; for example `xlsx-author` appears 9 times and `audit-xls` 7 times |
| `anthropics/life-sciences` | `e96556b` (HEAD unchanged) | 6 | 5 duplicate `bio-research` |
| **Anthropic total** | | **470** | |

**OpenAI**

| Source | Pin | Skill entries | Notes |
|---|---|---|---|
| Codex system skills | `openai/codex` `2739e82`. HEAD is now `550eb50`; the folder holds the same 5 (checked through the GitHub contents API) | 5 | `imagegen`, `openai-docs`, `review-agent`, `skill-creator`, `skill-installer` |
| `openai/skills` (deprecated) | `49f948f` (HEAD unchanged) | 44 | 5 system, plus `plugin-creator` and 39 curated (8 of them Figma) |
| `openai/skills` history only | research-openai.md 5 | 14 | `doc`, `spreadsheet`, `slides`, `frontend-skill`, `develop-web-game`, `sora`, `atlas`, `create-plan`, 2 `codex-readiness-*`, `gitlab-address-comments`, `wrapped`, `build-things`, `plan` |
| `openai/plugins` | `5fd93af` (HEAD unchanged) | 536 | 62 plugins. The largest are Twilio 55, Vercel 54, Zoom 53, life-science-research 50, public-equity 23, Shopify 20, data-analytics 18, ngs-analysis 18 and web-data-visualization 18 |
| Codex desktop runtime plugins | 26.909.12148 | 6 | `documents`, `pdf`, `Presentations`, `Spreadsheets`, `excel-live-control`, `template-creator` |
| `openai-bundled` desktop plugins | local | 7 | browser control, Chrome control, computer use, `visualize`, 3 LaTeX skills |
| Remote-curated proprietary | local | 28 | `openai-templates` 20, `pages` 4, `work-pets` 3, `plugin-management` 1 |
| **OpenAI total** | | **640** | Plus 26 third-party entries (Figma 14, vidIQ 12) |

**Grand total:** about 1,110 entries, 992 distinct names.

### 2.2 What is dropped, and why (no Marrow equivalent)

| Dropped | Upstream entries | Reason |
|---|---|---|
| Vendor-app surfaces | Claude Desktop browser, Chrome and computer-use skills, `docs`; Claude Code `artifact-*`, `design-sync`, `slides` (the claude.ai Slides type), `workflow-authoring`, `claude-in-chrome`; Codex app browser, Chrome, computer-use and `visualize`; ChatGPT `pages`; `excel-live-control` (ChatGPT Excel add-in) | Each drives a tool or service that only exists inside that vendor's app |
| Provider self-configuration | `/doctor`, `/update-config`, `/fewer-permission-prompts`, `keybindings-help`, `setup-claude`, `setup-cowork`, `cowork-plugin-*`, the Discord, iMessage and Telegram channel `access`/`configure` skills, `skill-installer`, `plugin-management` | They configure one vendor's CLI. Marrow has its own Settings |
| Vendor memory, usage and scheduling | `import-memory`, `consolidate-memory`, `memory-management`, `explain-usage`, `receipts`, `session-report`, `/loop`, `/schedule`, Desktop `schedule` | They depend on a vendor's own memory store or transcripts. Scheduling would be a Marrow feature, not a skill (PLAN.md Q16) |
| Codex-only mechanics | `define-goal` (Codex goal tool), `playwright-interactive` (Codex `js_repl`), `hatch-pet` and `work-pets` (Codex pets) | They need tools only Codex has |
| macOS and iOS only | `build-ios-apps` (9), `build-macos-apps` (11), the macOS and iOS parts of the Zoom kits | They need Xcode and a Mac |
| Vendor-specific teaching | `academy-guide` (Claude courses), `brand-guidelines` (Q-SKILLS-4 A), `example-plugin`, `template` | They make no sense in a third-party app |
| Products with no usable public interface, or unsuitable | `magicpath`, `chatcut` (vendor desktop app; its plugin is GPL), `higgsfield` (vendor video service), `openai-ads-conversions` (OpenAI's ad platform), `atlas` (ChatGPT Atlas browser), `sora` (API status unknown, research-openai.md 10.6), `wrapped`, `cwc-makers` (Anthropic's maker-kit app), the NVIDIA plugin's 12 Linux GPU-cluster products (Dynamo, cuOpt, AIQ, Omniverse services) | No public API Marrow can rely on, Linux datacentre only, or the API is retired or unknown |

Everything else maps to a Marrow skill (2.3). Overlaps are merged. For example: 9 `xlsx-author` copies, Anthropic `xlsx`, OpenAI `Spreadsheets` and `spreadsheet` all become `spreadsheet-workbooks`. Zoom appears twice (57 + 53) and becomes `zoom-developer`. Notion appears twice (4 + 4) and becomes `notion-workspace`.

### 2.3 The Marrow skills (120)

Column key:
- **On:** on by default (Q-SKILLS-1 A). "x" means explicit-only: it is on, but runs only when the user asks for it, and costs no catalogue budget.
- **Needs:** `K` = an API key, `L` = a sign-in or login, `N` = internet, `T` = a user-installed tool outside the bundle. Any of K, L or N means the skill starts off (Q-SKILLS-1 A).
- **Runtime:** which bundled tools the skill uses: Py = Python, Nd = Node, LO = LibreOffice, Tx = Tectonic, MR = `marrow-run` native helpers.

#### D. Documents (12)

| # | Skill | What it gives the user | Built from (capability sources) | Runtime | Needs | On | Wave |
|---|---|---|---|---|---|---|---|
| D1 | `word-documents` | Create, read and edit .docx and .dotx: styles, headings, contents, tables, images, headers and footers, sections, footnotes, comments, tracked changes (add, accept, reject), find and replace that keeps formatting, template fill, mail merge, conversion to PDF, ODT, RTF, HTML and Markdown and from .doc, and page renders for a visual check | Anthropic `docx` (description only), OpenAI `documents` (description only) and history `doc` | Py, LO | none | yes | 1 |
| D2 | `spreadsheet-workbooks` | Create and edit .xlsx, .xlsm, .csv and .ods: formulas, named ranges, formats, conditional formatting, validation, charts, freeze panes, summaries; recalculation with formula-error scan; reading messy data with pandas; financial-model conventions check; macros preserved, never run | Anthropic `xlsx` (description only), OpenAI `Spreadsheets` (description only) and `spreadsheet`, financial-services `xlsx-author`, `audit-xls` and `clean-data-xls` | Py, LO | none | yes | 1 |
| D3 | `slide-decks` | Create .pptx and .potx from an outline (layouts, themes, charts, tables, images, speaker notes); edit existing decks (text, reorder, duplicate, delete); follow a template; thumbnail grid; text overflow and overlap check; PDF export; text and notes extraction | Anthropic `pptx` (description only), OpenAI `Presentations` (description only) and history `slides`, financial-services `pptx-author`, `deck-refresh`, `ib-check-deck` and `ppt-template-creator` | Py, LO | none | yes | 1 |
| D4 | `pdf-documents` | Text and table extraction; merge, split, rotate and crop; fill and flatten AcroForms; overlay text on forms that are not fillable; create PDFs; watermark; encrypt and decrypt; metadata and bookmarks; image extraction; page renders; OCR of scanned pages with Windows' own OCR; text comparison of two PDFs | Anthropic `pdf` (description only), OpenAI `pdf` (curated and plugin), `view-pdf` | Py, MR | none (OCR uses the Windows OCR language pack, present with English Windows) | yes | 1 |
| D5 | `office-conversions` | Convert between office formats, singly or in batches, headless: DOCX, DOC, ODT, RTF to PDF; XLSX, CSV, ODS; PPTX to PDF and PNG; HTML to PDF (Edge headless print) | Conversion parts of the four document skills | LO, Py, MR | none | yes | 2 |
| D6 | `document-templates` | Turn the user's own files into reusable templates and fill them. Includes a starter set of Marrow-original templates: business review deck, budget workbook, memo, pipeline tracker, three-statement forecast | OpenAI `template-creator` (description only), `openai-templates` (titles only), `ppt-template-creator` | Py, LO | none | no | 2 |
| D7 | `document-drafting` | A structured co-writing flow for specs, proposals and decision documents: gather context, agree the outline, draft section by section, test it as a reader would, then finish | `doc-coauthoring` (description only) | Py (outline-to-docx tool) | none | yes | 2 |
| D8 | `team-updates` | Status reports, stand-ups, newsletters, incident write-ups, FAQs, stakeholder and weekly updates, daily briefs | `internal-comms`, operations `status-report`, product `stakeholder-update`, engineering `standup`, Desktop `morning` (description only) | Py | none | no | 2 |
| D9 | `visual-themes` | A set of Marrow-original colour and font themes applied to decks, documents and pages, plus making new themes | `theme-factory` | Py | none | yes | 2 |
| D10 | `python-notebooks` | Create, edit, run and check .ipynb notebooks (SQL and Python) | OpenAI `jupyter-notebook`, data-analytics `jupyter-notebooks` (description only) | Py (Jupyter set) | none | no | 2 |
| D11 | `latex-papers` | Compile and fix LaTeX documents | Codex `latex-compile` and `latex-doctor` (description only) | Tx | N (Tectonic fetches TeX packages on first use) | no | 2 |
| D12 | `office-desktop-automation` | Drive an installed Microsoft Excel, Word or PowerPoint through COM: open, refresh, recalculate, export | `excel-live-control` (description only), as a Windows-native capability | PowerShell COM | T (Microsoft Office) | no | 2 |

#### V. Design and media (18)

| # | Skill | What it gives the user | Built from | Runtime | Needs | On | Wave |
|---|---|---|---|---|---|---|---|
| V1 | `ui-visual-design` | Distinctive, intentional UI direction: type, colour, layout, motion | `frontend-design`, `frontend-skill`, `frontend-app-builder`, Claude Code `/design` (description only) | none (lint tool: Py) | none | yes | 3 |
| V2 | `web-app-builder` | Multi-file React apps bundled into one offline HTML file; interactive single-file playgrounds | `web-artifacts-builder`, `playground`, `frontend-app-builder` | Nd (esbuild, React) | none | no | 5 |
| V3 | `poster-design` | Posters and static art as PNG or PDF, from a written design direction, with bundled open fonts | `canvas-design` | Py | none | yes | 3 |
| V4 | `generative-art` | Seeded generative art in plain Canvas and SVG, with an offline parameter viewer. No p5.js, which is LGPL, and no CDN | `algorithmic-art` | Nd or none | none | no | 5 |
| V5 | `animated-gifs` | Small animated GIFs for chat platforms, within size and frame limits | `slack-gif-creator` | Py (Pillow) | none | no | 5 |
| V6 | `diagrams` | Flowcharts, sequence, ER, state and architecture diagrams rendered offline to SVG or PNG | Figma `generate-diagram` (description only), the UML and node-link visualization skills | Nd (Mermaid bundle), MR (Edge headless) | none | no | 5 |
| V7 | `chart-building` | Static charts (matplotlib) and web charts (D3 or Canvas), dashboards as single HTML files, chart critique and accessibility | Claude Code `/dataviz` (description only), `build-web-data-visualization` (18), data `create-viz`, `data-visualization` and `build-dashboard`, data-analytics `visualize-data` and `build-dashboard` | Py, Nd | none | no | 5 |
| V8 | `ux-design-review` | Critique of screens and flows, accessibility review, UX copy | design `design-critique`, `accessibility-review` and `ux-copy`, product-design `audit` (description only) | Py (report tool) | none | yes | 5 |
| V9 | `design-systems` | Tokens, component inventories, handoff specs | design `design-system` and `design-handoff`, `figma-generate-library` (description only) | Py | none | no | 5 |
| V10 | `ux-research-plans` | Research plans, interview guides, synthesis of findings | design `user-research` and `research-synthesis`, product `synthesize-research`, product-design `research` (description only) | Py | none | no | 5 |
| V11 | `screenshot-to-code` | Implement a screenshot or mockup as a responsive frontend; recreate a public page as a local app | product-design `image-to-code` and `url-to-code` (description only) | Nd, MR (Edge headless) | N for URLs | no | 5 |
| V12 | `browser-game-dev` | Browser games in 2D and 3D: stack choice, game loop, sprites, playtests | `game-studio` (9), `develop-web-game` | Nd (playwright-core for playtests) | none | no | 5 |
| V13 | `image-generation` | Generate and edit images through a provider image API | Codex `imagegen` | Py | K, N | no | 5 |
| V14 | `speech-synthesis` | Text to speech offline with the voices installed in Windows; optionally through a provider API | OpenAI `speech` | MR (Windows speech) | none offline; K and N for API voices | no | 5 |
| V15 | `audio-transcription` | Audio to text with speaker labels, through a provider API | OpenAI `transcribe` | Py | K, N | no | 5 |
| V16 | `video-from-code` | Animated video built from HTML or React compositions, using the open frameworks the user installs | `remotion` (12), `hyperframes` (5) (description only) | Nd | N, T (npm and the framework; Remotion has its own company licence, which the skill states) | no | 5 |
| V17 | `figma-design` | Read Figma files, implement designs as code, export assets; write to Figma where the route allows it | Figma's public REST and MCP documentation; Figma skills (description only) | Py | L, N | no | 5 |
| V18 | `screen-capture` | Screenshot of the whole screen, a window or a region | OpenAI `screenshot` | MR | none | no (explicit-only when on) | 5 |

#### B. Build, test and review (37)

| # | Skill | What it gives the user | Built from | Needs | On | Wave |
|---|---|---|---|---|---|---|
| B1 | `skill-authoring` | Create and improve skills in the open format, for any provider | both `skill-creator`s, `writing-skills`, `skill-development`, `/run-skill-generator` | none | yes | 3 |
| B2 | `skill-evaluation` | Trigger and quality evals of skills across provider CLIs, with a report | `plugin-eval` (5), `skill-creator` eval tools | T (a provider CLI) | yes | 3 |
| B3 | `agent-plugin-packaging` | Package skills, commands, hooks and MCP servers as Claude-format and Codex-format plugins | `plugin-creator`, plugin-dev (7), `/plugin-authoring` | none | yes | 3 |
| B4 | `mcp-servers` | Build, test and package MCP servers (Python, Node), MCP apps with UI, and ChatGPT-app style servers | `mcp-builder`, `build-mcp-server`, `build-mcp-app`, `build-mcpb`, `chatgpt-apps`, `build-chatgpt-app` (description only) | none | no | 3 |
| B5 | `browser-testing` | Drive a local web app with Playwright through the installed Microsoft Edge: screenshots, console, server lifecycle | `webapp-testing`, `playwright`, `frontend-testing-debugging`, `game-playtest`, `agent-browser-verify` | none (Edge comes with Windows) | no | 4 |
| B6 | `debug-from-symptoms` | Reproduce a bug, isolate it, find the root cause, fix it, guard it | `/debug` (description only), `systematic-debugging`, engineering `debug` | none | no | 4 |
| B7 | `simplify-changes` | Review the current diff for reuse and simplification, then apply the fixes | `/simplify` (description only) | T (git) | no | 4 |
| B8 | `verify-changes` | Prove a change works by running the CLI, server, desktop app or web app | `/verify` and `/run` (description only), `verification-before-completion` | none | no | 4 |
| B9 | `change-review` | Read-only, defect-first review of a diff or branch | `review-agent`, `/code-review` (description only), `requesting-code-review`, engineering `code-review`, `coderabbit-review` (description only) | T (git) | yes | 3 |
| B10 | `address-review-comments` | Work through PR or MR review comments, reply and fix | `gh-address-comments`, `gitlab-address-comments`, `receiving-code-review` | T (gh or glab, signed in) | yes | 3 |
| B11 | `secure-code-review` | Language-aware secure-code audit, diff scan, triage, fix and verification of findings | `security-best-practices`, `/security-review` (description only), `codex-security` (description only), `claude-security` (description only) | none | yes | 3 |
| B12 | `threat-modeling` | A threat model grounded in the repository, with attack paths | `security-threat-model`, `codex-security` `threat-model` and `attack-path-analysis` (description only) | none | yes | 3 |
| B13 | `code-ownership-map` | Ownership and bus-factor map from git history | `security-ownership-map` | T (git) | no | 3 |
| B14 | `test-first-development` | Tests first, then code; test strategy | `test-driven-development`, engineering `testing-strategy`, `codex-readiness-*` | none | no | 4 |
| B15 | `feature-brainstorm` | Turn an idea into an agreed design before any code | `brainstorming` | none | no | 4 |
| B16 | `implementation-plans` | Write plans, then execute them with checkpoints and parallel workers where the provider allows | `writing-plans`, `executing-plans`, `subagent-driven-development`, `dispatching-parallel-agents`, `create-plan`, `plan`, `/batch` (description only) | none | no | 4 |
| B17 | `ship-branch` | Stage, commit, push and open a pull request | `yeet`, `finishing-a-development-branch` | T (git, gh) | x | 3 |
| B18 | `worktree-isolation` | Isolated work in git worktrees | `using-git-worktrees` | T (git) | no | 4 |
| B19 | `ci-failure-fix` | Find out why CI failed and fix it (GitHub Actions, GitLab CI, CircleCI) | `gh-fix-ci`, CircleCI (4) | T (gh or glab); N | yes | 3 |
| B20 | `architecture-records` | Architecture documents, ADRs, system design reviews | engineering `architecture` and `system-design` | none | no | 4 |
| B21 | `codebase-docs` | READMEs, API docs, docs for onboarding to a codebase | engineering `documentation` | none | no | 4 |
| B22 | `tech-debt-audit` | Find, rank and plan technical debt | engineering `tech-debt` | none | no | 4 |
| B23 | `incident-postmortems` | Incident response notes, postmortems, runbooks, deploy checklists | engineering `incident-response` and `deploy-checklist`, operations `runbook` | none | no | 4 |
| B24 | `agent-instructions` | Write and audit AGENTS.md, CLAUDE.md and GEMINI.md; recommend automations | `/init` (description only), `claude-md-improver`, `claude-automation-recommender` | none | no | 4 |
| B25 | `agent-config-migration` | Move instructions, skills, agents and MCP config between provider CLIs | `migrate-to-codex` | none | no | 4 |
| B26 | `cli-tools` | Build a composable CLI from an API or spec | `cli-creator` | T (a language toolchain) | no | 4 |
| B27 | `aspnet-web-apps` | ASP.NET Core design and review | `aspnet-core` | T (.NET SDK) | no | 4 |
| B28 | `windows-desktop-apps` | WinUI 3 and Windows App SDK apps | `winui-app` | T (.NET SDK) | no | 4 |
| B29 | `android-app-qa` | Android emulator QA and performance checks | `test-android-apps` (2) | T (Android SDK) | no | 4 |
| B30 | `react-nextjs-practices` | React and Next.js performance and patterns | `react-best-practices`, Vercel's Next.js skills | none | no | 4 |
| B31 | `postgres-performance` | Postgres schema and query performance | `supabase-postgres-best-practices` | none | no | 4 |
| B32 | `sql-authoring` | Write, explain and optimise SQL across dialects | data `sql-queries` and `write-query` | none | no | 4 |
| B33 | `shadcn-components` | shadcn/ui components: add, compose, fix | `shadcn-best-practices`, Vercel `shadcn` | N (shadcn CLI) | no | 4 |
| B34 | `ai-provider-apis` | Building with LLM APIs and agent SDKs (Anthropic, OpenAI, Google, OpenAI-compatible), from their public docs | `claude-api`, `openai-docs`, `agents-sdk` (description only) | N (current docs) | no | 4 |
| B35 | `math-problem-solving` | Hard maths problems with staged reasoning and adversarial checking | `math-olympiad`, `siege`, `solo` | none | no | 4 |
| B36 | `critical-followups` | After a substantive answer, add 2 or 3 questions that help the user check it | `discernment-nudge` | none | no | 4 |
| B37 | `web-research-report` | Multi-source research with citations into a written report | Desktop `deep-research` (description only), enterprise-search (5), `notion-research-documentation`, `aiq-research` | N (a web search tool) | no | 4 |

#### A. Data (3)

| # | Skill | What it gives the user | Built from | Needs | On | Wave |
|---|---|---|---|---|---|---|
| A1 | `data-analysis` | Explore, profile, test statistically, validate quality | data `analyze`, `explore-data`, `statistical-analysis`, `validate-data` and `data-context-extractor`; data-analytics `analyze-data-quality`, `validate-data` and `create-data-context` (description only) | none | no | 6 |
| A2 | `metrics-and-kpis` | KPI design, metric diagnostics, scorecards and readouts | data-analytics `design-kpis`, `metric-diagnostics`, `kpi-reporting` and `product-business-analysis` (description only); product `metrics-review` | none | no | 6 |
| A3 | `analytics-reports` | Answer-first analytical reports as DOCX, PDF or HTML | data-analytics `build-report` and `report-to-pdf` (description only) | none | no | 6 |

#### W. Business work (17)

All 17 work offline on files the user provides. Connectors such as a CRM or ticket system are optional extras: the skill names the MCP it can use, if the user has one.

| # | Skill | Built from (main sources) | On | Wave |
|---|---|---|---|---|
| W1 | `sales-account-research` | sales account-research, account-plan, account-tiering, stakeholder-map, rep/account-context; Apollo; Common Room | no | 6 |
| W2 | `sales-calls-and-outreach` | sales call-prep, call-summary, draft-outreach, handle-objection, schedule-meeting; small-business outreach-composer, speed-to-lead, reactivate | no | 6 |
| W3 | `sales-pipeline-review` | sales pipeline-review, forecast, deal-review, deal-signals, close-plan, win-loss-review, renewal-radar, crm-hygiene-check (and the rest of the 36) | no | 6 |
| W4 | `support-tickets` | customer-support (5); small-business ticket-deflector | no | 6 |
| W5 | `hiring` | HR draft-offer, interview-prep, recruiting-pipeline; small-business hiring-screener, job-post-builder | no | 6 |
| W6 | `people-ops` | HR onboarding, performance-review, comp-analysis, org-planning, people-report, policy-lookup | no | 6 |
| W7 | `contract-analysis` | legal review-contract, triage-nda, vendor-check, compliance-check, legal-risk-assessment; small-business contract-review (always states "not legal advice") | no | 6 |
| W8 | `legal-drafting` | legal brief, legal-response, meeting-briefing, signature-request | no | 6 |
| W9 | `marketing-campaigns` | marketing campaign-plan, content-creation, draft-content, email-sequence, performance-report; small-business ad-manager, social-content-engine, content-strategy, review-reputation | no | 6 |
| W10 | `search-visibility-audit` | marketing seo-audit; small-business seo-ai-visibility | no (N to crawl live pages) | 6 |
| W11 | `brand-voice-guide` | brand-voice (3), marketing brand-review, small-business brand-style. Builds the user's own brand guide; nothing about any vendor's brand | no | 6 |
| W12 | `operations-processes` | operations (9); small-business inventory-planner, restock | no | 6 |
| W13 | `accounting-close` | finance (8); financial-services fund-admin and month-end (accruals, roll-forward, GL reconciliation, NAV tie-out, break trace, variance commentary) | no | 6 |
| W14 | `product-specs` | product-management (8) | no | 6 |
| W15 | `small-business-finance` | small-business cash-flow-snapshot, invoice-chase, ap-processor, pay-the-bills, payroll-prep, plan-payroll, tax-prep, tax-season-organizer, close-month, month-end-prep | no | 6 |
| W16 | `proposals-and-rfps` | small-business grant-rfp-writer, proposal-builder; sales create-an-asset | no | 6 |
| W17 | `competitor-briefs` | marketing and product competitive-brief, sales competitive-intelligence, data-analytics market-sizing (description only) | no | 6 |

#### F. Finance (6)

These use D2 and D3 through `marrow_office`.

| # | Skill | Built from | Needs | On | Wave |
|---|---|---|---|---|---|
| F1 | `financial-models` | 3-statement, DCF, LBO, merger, comps, returns and unit-economics models; scenario sensitivity (financial-services; public-equity description only) | none | no | 7 |
| F2 | `equity-research-notes` | earnings preview and analysis, initiating coverage, model update, morning note, thesis tracker, catalyst calendar, sector overview, idea generation, tear sheet | none (market data supplied by the user) | no | 7 |
| F3 | `deal-documents` | pitch deck, CIM, teaser, buyer list, process letter, datapack, strip profile, deal tracker, client report and review, investment proposal | none | no | 7 |
| F4 | `private-equity-diligence` | deal sourcing and screening, DD checklist and meeting prep, IC memo, value-creation plan, portfolio monitoring, AI readiness | none | no | 7 |
| F5 | `kyc-review` | KYC document parsing and rules | none | no | 7 |
| F6 | `markets-analysis` | fixed income, FX carry, options volatility, rates and swap curves, macro monitors (LSEG and S&P partner skills, description only) | K, N (a market-data source) | no | 7 |

#### S. Science (6)

| # | Skill | Built from | Needs | On | Wave |
|---|---|---|---|---|---|
| S1 | `bio-databases` | Queries to public biology databases (UniProt, PDB, Ensembl, NCBI, ClinVar, GWAS Catalog and others) through their public APIs (`life-science-research`, description only) | N | no | 7 |
| S2 | `sequencing-pipelines` | Nextflow and NGS workflows: QC, RNA-seq, variants, metagenomics (`nextflow-development`, `ngs-analysis` (18)) | T (Nextflow, usually under WSL or Docker) | no | 7 |
| S3 | `single-cell-analysis` | Single-cell QC and analysis (`single-cell-rna-qc`, `scvi-tools`, `scrna-seq-qc`) | T (scanpy and scvi installed by the user) | no | 7 |
| S4 | `lab-data-standards` | Instrument data to open lab-data formats (`instrument-data-to-allotrope`) | none | no | 7 |
| S5 | `clinical-protocols` | Clinical-trial protocol drafting (`clinical-trial-protocol-skill`) | none | no | 7 |
| S6 | `research-problem-selection` | Choosing and scoping research problems (`scientific-problem-selection`) | none | no | 7 |

#### I. Integrations (21)

Every skill in this group needs internet and the vendor's sign-in or key, except I20, which needs only the local Zotero app. All start off. Each is written from the vendor's public developer docs.

| # | Skill | Covers | Wave |
|---|---|---|---|
| I1 | `web-deploy` | Vercel, Netlify, Cloudflare Pages, Render (4 upstream deploy skills, Vercel's platform skills) | 8 |
| I2 | `cloudflare-workers` | Workers, Durable Objects, agents on Cloudflare (Cloudflare (9)) | 8 |
| I3 | `stripe-payments` | Stripe integration (Stripe (7), `stripe-best-practices`) | 8 |
| I4 | `twilio-messaging` | SMS, voice, WhatsApp, Verify, SendGrid email (Twilio (55)) | 8 |
| I5 | `shopify-apps` | Shopify apps, themes, functions (Shopify (20)) | 8 |
| I6 | `expo-mobile-apps` | Expo and React Native (Expo (13)) | 8 |
| I7 | `supabase-backend` | Supabase (2) | 8 |
| I8 | `temporal-workflows` | Temporal (4) | 8 |
| I9 | `product-analytics` | PostHog and Mixpanel | 8 |
| I10 | `sentry-issues` | Sentry (2) | 8 |
| I11 | `notion-workspace` | Notion (4 + 4) | 8 |
| I12 | `linear-issues` | Linear | 8 |
| I13 | `gdrive-documents` | Google Docs, Sheets, Slides and Drive (`google-drive` (5), Desktop `google-workspace` description only) | 8 |
| I14 | `dropbox-files` | Dropbox (6) | 8 |
| I15 | `airtable-bases` | Airtable (3) | 8 |
| I16 | `zoom-developer` | Zoom apps, SDKs, REST API and webhooks (Zoom 57 + 53, Windows and web parts) | 8 |
| I17 | `slack-workspace` | Slack messaging and search (Slack (2)) | 8 |
| I18 | `canva-designs` | Canva Connect API (Canva (8), description only) | 8 |
| I19 | `adobe-creative-apis` | Adobe's public creative APIs (Adobe (6), description only) | 8 |
| I20 | `zotero-library` | Local Zotero library through Zotero's local API (needs Zotero desktop, not internet) | 8 |
| I21 | `protein-design-api` | Boltz protein and molecule design API (Boltz (8)) | 8 |

**Counts:** 12 + 18 + 37 + 3 + 17 + 6 + 6 + 21 = **120**.

**Default-on set (19, of which 18 are listed for the model):** D1 to D5, D7, D9, V1, V3, V8, B1, B2, B3, B9, B10, B11, B12, B19, plus B17 (explicit-only).
- Their descriptions are held to 200 characters each. 18 × 200 = 3,600 characters, below Codex's half-budget cap of 4,000 at the 8,000-character default.
- PLAN.md 2.7 (WU-21) still applies: the user's own skills are listed first.

**Need counts:**
- **31 skills start off because their main use needs a key, a sign-in or internet:** D11, V13, V15, V16, V17, B33, B34, B37, W10, F6, S1, and the 20 integrations other than I20.
- **Two work offline**, and need internet or a key for only one mode: V11 (URL mode) and V14 (API voices).
- **I20 needs only the local Zotero app.**
- **B19 needs internet but is on**, because Q-SKILLS-1 A puts the GitHub skills in the default-on set.
- **The other 69 skills start off only to protect the catalogue budget** (120 − 19 on − 31 need − 1 I20 = 69).

---

## 3. The bundled tool runtime

### 3.1 Components

| Component | Version (pin at WU-R1) | What it is for | Licence (to be confirmed by gate G5) |
|---|---|---|---|
| Python, Windows x64 embeddable distribution | 3.14.x | All Python tools | PSF-2.0. Bundled: OpenSSL Apache-2.0, libffi MIT, SQLite public domain, expat MIT, bzip2, xz, zlib, mpdecimal BSD |
| Python packages (pure wheels, no pip at run time) | pinned in `runtime.lock` | See 3.2 | See 3.2 |
| Node.js `node.exe` only (no npm, npx or corepack) | 24.x LTS (re-pin at WU-R1: Node 26 enters LTS in late October 2026) | Web tools, Playwright, esbuild, Mermaid | MIT. Bundled: V8 BSD-3, OpenSSL Apache-2.0, ICU Unicode-3.0, libuv, llhttp, nghttp2, c-ares, undici and others MIT, zlib, brotli MIT. **npm is not shipped: Artistic-2.0** |
| Node packages (vendored, `node_modules` built at WU-R1 from registry tarballs, no npm) | pinned | `playwright-core` (Apache-2.0, NOTICE), `esbuild` + `@esbuild/win32-x64` (MIT), `react`, `react-dom`, `scheduler` (MIT), Mermaid's prebuilt `dist` bundle only (MIT, plus notices for its bundled deps: d3 ISC, DOMPurify Apache-2.0/MPL-2.0, KaTeX MIT, cytoscape MIT and others), `d3` (ISC) | as listed |
| LibreOffice, trimmed headless image | 25.8.x or 26.2.x (SF: both are current on libreoffice.org; the 25.8.7 x64 MSI is 349 MB) | Conversion, rendering, formula recalculation, legacy formats | MPL-2.0 core. **Third-party parts under other licences, some LGPL. Audit in WU-R1; owner question Q1** |
| Tectonic | 0.15.x | `latex-papers` | MIT (bundles HarfBuzz MIT, ICU, FreeType FTL, graphite2 MPL-2.0 option) |
| `marrow-run.exe` (Marrow original, .NET NativeAOT) | Marrow version | Tool launcher (3.3); Windows OCR (`Windows.Media.Ocr`); Windows speech; screen capture; Edge headless print and screenshot; process-tree kill for LibreOffice | Marrow's own; uses only OS components |
| Fonts (curated open set for `poster-design` and themes) | pinned | Typography | SIL OFL-1.1 (Q1) |
| `marrow_office` (Py) and `marrow_web` (Node) | Marrow version | Shared document and web helpers | Marrow's own |

**Not bundled, with reasons:**
- Poppler (GPL): page rendering uses pypdfium2 instead.
- pandoc (GPL): conversion uses python-docx and LibreOffice.
- Git (GPL-2.0): it is a user tool and shown as a need.
- FFmpeg (LGPL or GPL): no video encoding is bundled.
- Tesseract: Windows' own OCR is used.
- p5.js (LGPL-2.1): `generative-art` uses plain Canvas.
- A Chromium download: Playwright uses the Microsoft Edge that ships with Windows 11, through its `msedge` channel.
- `sharp` (libvips is LGPL).
- `gh` (MIT; only useful signed in, so it is a need, not bundled).
- pip, at run time. The runtime is locked; agents that need more packages use the user's own Python.

### 3.2 Python package set (about 60 distributions with dependencies)

**Sizes:** (SF) measured on 2026-10-04, read-only, in the comparable Python 3.12 site-packages of the Codex desktop runtime on this PC. Other figures are estimates.

| Purpose | Packages (licence) | Size |
|---|---|---|
| Word, Excel, PowerPoint | python-docx (MIT) 2.2 MB, openpyxl (MIT) 1.8 + et-xmlfile (MIT), python-pptx (MIT) 2.4, XlsxWriter (BSD-2) 1.6, lxml (BSD-3; libxml2 and libxslt MIT) 8.9, defusedxml (PSF-2.0) | about 17 MB |
| PDF | pypdf (BSD-3) 3.0, pdfplumber (MIT) 0.3 + pdfminer.six (MIT) 8.9 + charset-normalizer (MIT) 0.7, pypdfium2 (Apache-2.0 or BSD-3; PDFium BSD-3 with FreeType FTL, libjpeg-turbo, OpenJPEG BSD-2, lcms2 MIT) 7.6, reportlab (BSD-3) 7.9, cryptography (Apache-2.0 or BSD-3; OpenSSL Apache-2.0) 10.5 + cffi (MIT) + pycparser (BSD-3) | about 41 MB |
| Images | Pillow (MIT-CMU/HPND, Q1) 15.4 | 15 MB |
| Data and charts | pandas (BSD-3) 61.5, numpy (BSD-3; the Windows wheel bundles OpenBLAS BSD-3 and a compiler runtime whose licence G5 must read, Q1) 52, python-dateutil (Apache-2.0 / BSD-3), tzdata (Apache-2.0), six (MIT), matplotlib (Matplotlib licence, PSF-based) + contourpy, cycler, kiwisolver (BSD-3), fonttools (MIT), pyparsing (MIT), packaging (Apache-2.0 / BSD-2) | about 155 MB (estimate) |
| Notebooks | nbformat, nbclient, jupyter-client, jupyter-core, ipykernel, ipython, traitlets, comm, matplotlib-inline (BSD-3); pyzmq (BSD-3; libzmq MPL-2.0); tornado (Apache-2.0); jsonschema (MIT), fastjsonschema (BSD-3); prompt-toolkit (BSD-3), jedi, parso (MIT), pygments (BSD-2), psutil (BSD-3), platformdirs (MIT), pywin32 (PSF) | about 35 MB (estimate) |
| General | PyYAML (MIT), Jinja2 + MarkupSafe (BSD-3), markdown-it-py + mdurl (MIT), networkx (BSD-3), beautifulsoup4 + soupsieve (MIT), requests (Apache-2.0) + urllib3 (MIT) + idna (BSD-3) + certifi (MPL-2.0) | about 12 MB |

**Total:** about 275 MB installed, including precompiled bytecode (estimate; WU-R1 measures it).

(ED) **Bytecode is precompiled at build time** with `compileall --invalidation-mode unchecked-hash`. The runtime folder is locked (3.3), so Python cannot write `__pycache__` there; without precompiling, every import would compile from source.

(ED) **The embeddable `python3XX._pth` file** lists `Lib\site-packages` and the `marrow_office` folder. When a `._pth` file is present, Python ignores `PYTHONPATH` and `PYTHONHOME` and does not use the user site. SF: this is the documented behaviour of the embeddable distribution; WU-R1 confirms it. So nothing in the user's environment can break or shadow the runtime.

### 3.3 Where it installs, how skills find it, integrity

**Package payload (ED).**
- The Velopack package carries the runtime as deterministic archives: fixed timestamps, sorted entries, one archive per component, about 350 to 450 MB compressed (estimate).
- They sit in `current\runtime-payload\`, beside the skills payload of PLAN.md 2.1.
- Deterministic archives mean an app release that does not change a component produces no delta bytes for it.

**Runtime store (ED; extends PLAN.md 2.1):**

```
%LOCALAPPDATA%\Marrow Code\runtime\<component>-<version>-<hash8>\
```

`<component>` is one of `python`, `node`, `libreoffice`, `tectonic`, `fonts`, `tools`.
- **Populated:** on the first start of a Marrow version that brings a component version not yet in the store. Unchanged components carry over across app updates with no re-extraction.
- **Extracted:** in the background at low priority. The Skills page shows "Preparing tools" until it is done; a skill used before then says so.
- **Verified:** whole-tree, against the per-file manifest compiled into `Marrow.Services`, once after extraction.
- **Locked:** with the same deny entry and read-only attributes as skill generations (PLAN.md 2.1), so an agent's `pip install` or edit cannot change what other chats use.
- **Checked per launch:** a cheap seal check only (lock entry present, plus the hashes of every `.exe`, `.dll`, `.pyd` and `.node` file). The full tree check runs again after an app update and on "Reset skills data".
- (UN) Whether the full LibreOffice tree (about 15 to 25 thousand files) verifies within an acceptable time on this PC; WU-R2 measures it. If it does not, verification falls back to a sampled check plus all binaries.

**After extraction (UN, settled in WU-R2).** Marrow deletes `current\runtime-payload\*` to save about 400 MB.
- This needs a test with Velopack 1.2.161 in a fake layout. The test proves that Velopack's next update and its uninstall do not need those files. Velopack applies updates from its stored package, not from `current\`; that is to be confirmed.
- If the test fails, the payload stays, and the disk footprint rises by about 400 MB.

**Work folders (writable, never locked):**

```
%LOCALAPPDATA%\Marrow Code\runtime-work\
```

- LibreOffice gets a fresh user profile per call, `lo-profile-<random>`, copied from a template profile prepared at extraction. This avoids profile locks between concurrent conversions and cuts the 2 to 5 s first-start cost. The 2 to 5 s figure is to be measured.
- Temporary output goes here.
- Cleaned at app start and when sessions end.

**How a skill finds the tools (ED):**
1. **`marrow-run.exe`** sits in the store's `tools` component, and its folder is **appended** to each provider child process's `PATH`, never prepended, so nothing of the user's is shadowed. This is recorded as a deviation (PLAN.md 3.3). The name is unique.
2. **Skills always call** `marrow-run python <tool> ...`, `marrow-run node <tool> ...`, `marrow-run office --convert-to pdf <file>`, `marrow-run ocr <image>`, `marrow-run speak ...`, `marrow-run capture ...` or `marrow-run edge-print <html> <pdf>`. The same line works in PowerShell, cmd and Git Bash.
3. **`marrow-run` resolves the current store paths** from `MARROW_RUNTIME_ROOT`, which Marrow sets in each provider child's environment, and falls back to a pointer file under the data root.
   - It runs the tool with a clean environment for that tool: no `PYTHONPATH`, and `PYTHONUTF8=1` for the child only (this does not touch the user's own Python).
   - It applies a timeout, kills the whole process tree on timeout (important for `soffice.bin`), and returns the tool's exit code.
4. **Under native API providers** (Marrow runs the tool loop), `run_command` gets the same `PATH` and environment.
5. **Codex's elevated Windows sandbox** runs commands as sandbox users (PLAN.md 2.2). (UN) Whether they can read and run the locked store under `AppData\Local\Marrow Code` is settled in Track P's live checks (WU-11), with the PLAN.md fallbacks.

**Updates (ED).**
- Runtime components change only with a Marrow release.
- A developer-only script (`scripts/runtime/refresh.mjs`, network allowed because it is not a test) does the following:
  - fetches the pinned versions (python.org embeddable zip; wheels from PyPI; nodejs.org zip; npm registry tarballs, unpacked without npm; the LibreOffice MSI, admin-extracted with `msiexec /a` into a scratch folder and trimmed; the Tectonic release);
  - checks the published hashes and signatures;
  - runs gate G5;
  - writes `runtime.lock`, the manifest and the THIRD_PARTY_NOTICES runtime section;
  - produces a change report for the owner.
- A quarterly refresh, plus a release for any critical security fix in Python, Node, OpenSSL or LibreOffice, is the maintenance cost of bundling.

**Uninstall.** PLAN.md's before-uninstall hook (2.1, r3) is extended to unlock and delete `runtime\` and `runtime-work\` as well.

### 3.4 Size estimate

| Part | Unpacked (estimate) | Basis |
|---|---|---|
| Python embeddable | 25 MB | estimate; WU-R1 measures |
| Python packages | 275 MB | 3.2 (partly measured) |
| `node.exe` | 80 MB | (SF) `C:\Program Files\nodejs` is 98 MB with npm on this PC |
| Node packages | 35 MB | estimate |
| LibreOffice, trimmed (Writer, Calc, Impress and Draw filters; English UI; no help, Base, Math, Java, extensions or extra dictionaries) | 450 MB (range 350 to 650) | (UN) the 349 MB MSI is the only fixed figure; WU-R1 measures |
| Tectonic | 25 MB | estimate |
| `marrow-run.exe` | 10 MB | estimate |
| Fonts | 10 MB | estimate |
| 120 skills (text, tools, samples, templates) | 20 MB | estimate |
| **Total unpacked** | **about 0.95 GB (0.8 to 1.2)** | |

- **Download:** the app plus about 350 to 450 MB of compressed runtime.
- **Disk after first start:** about 1.4 GB with the payload deleted after extraction (Velopack's stored package, plus the store, plus the app). About 1.8 GB if WU-R2 shows the payload must stay.
- **First start:** a one-time background extraction, then a first-read scan by antivirus. PLAN.md measured 2 to 6 s for 255 files; LibreOffice has far more. WU-R2 measures it and the Skills page shows progress.

### 3.5 Licence allow-list for shipped components (gate G5)

**On the list (the lead's rule):** MIT, BSD-2-Clause, BSD-3-Clause, Apache-2.0, PSF-2.0, and MPL-2.0 (file-level).

**Also met in the runtime, and needing an explicit ruling (Q1):**
- ISC (d3; MIT-equivalent);
- MIT-CMU / HPND (Pillow);
- Unicode-3.0 (ICU);
- Zlib;
- FTL (FreeType);
- IJG (libjpeg);
- BSL-1.0 (Boost, inside LibreOffice);
- SIL OFL-1.1 (fonts; in LibreOffice and the curated set);
- the Matplotlib licence (PSF-based);
- GCC Runtime Library Exception and possibly LGPL-2.1 pieces inside the numpy and pandas Windows wheels (to be read by G5);
- LGPL-only and dual-licensed libraries inside LibreOffice (to be listed by WU-R1).

Every component's licence texts go into THIRD_PARTY_NOTICES, generated from the runtime manifest.

---

## 4. Figma

### 4.1 What is public

| Fact | Source (read 2026-10-04) |
|---|---|
| (SF) The remote MCP server is `https://mcp.figma.com/mcp` and uses OAuth. "Only clients listed in the Figma MCP Catalog like VS Code, Cursor, or Claude Code can connect." Codex is also listed. New clients go on a waitlist | developers.figma.com/docs/figma-mcp-server/remote-server-installation |
| (SF) The desktop server runs at `http://127.0.0.1:3845/mcp`. It is enabled in the Figma desktop app, in Dev Mode, from the inspect panel ("Enable desktop MCP server"). Figma "strongly recommends" the remote server for the broadest features | .../local-server-installation |
| (SF) MCP tool calls are rate-limited by seat and plan: View and Collab seats get a handful per month; Dev and Full seats get up to hundreds per day | .../rate-limits-access (exact numbers re-read at build time) |
| (SF) REST API personal access tokens are made in Figma under Settings > Security > "Generate new token", with chosen scopes and an expiry, and sent in the `X-Figma-Token` header | developers.figma.com/docs/rest-api/personal-access-tokens |
| (SF) Figma OAuth apps:<br>- authorization at `https://www.figma.com/oauth`;<br>- token at `https://api.figma.com/v1/oauth/token`, with client id and **client secret** (HTTP Basic) always required;<br>- PKCE (S256) optional;<br>- redirect URLs pre-registered;<br>- access tokens last 90 days, with refresh;<br>- **public apps are reviewed by Figma** | developers.figma.com/docs/rest-api/oauth-apps |
| (SF) The port already has the Mac's MCP OAuth client: protected-resource discovery, dynamic client registration, a loopback callback and secure storage | `src/Marrow.Services/MCP/MCPOAuth.cs` (from `MCPOAuth.swift`) |

### 4.2 The skill's three routes (ED)

`figma-design` (V17) checks the routes in order and uses the first that works. All of them are routes Figma publishes, and Marrow ships no Figma skill text.

1. **Remote MCP through an allow-listed provider CLI** (Claude Code, Codex, Cursor). This is the full feature set, including writing to Figma.
   - **What the user sees:**
     - Marrow's MCP settings offer a "Figma" entry (canvas first, OD-0070), which adds `https://mcp.figma.com/mcp` to those providers' sessions.
     - On first use the provider CLI starts Figma's OAuth: the system browser opens Figma's own sign-in and consent page, the user signs in and allows access, and the browser returns to the CLI's local callback.
     - Marrow shows a sheet, "Sign in to Figma in your browser", with the sign-in link and a Cancel button, then a "Connected to Figma" state.
   - The client identity Figma sees is the CLI's, which is on Figma's catalogue.
   - (UN, WU-11 live) How each CLI starts that sign-in when Marrow runs it headless:
     - Codex has a CLI login for MCP servers.
     - Claude Code's headless path is to be found.
     - Whether Marrow's own MCPOAuth registration is refused by Figma, which is expected from the allow-list.

     If a CLI can only sign in from its own terminal UI, Marrow opens a visible terminal running that CLI's documented sign-in command, and the user completes the Figma page there.
2. **Figma desktop app, local MCP server** (any provider, including native API providers through Marrow's own MCP client).
   - **What the user sees:** "Open Figma desktop, sign in, open Dev Mode, and turn on 'Enable desktop MCP server'." Figma's own app sign-in is the authentication.
   - Marrow probes `127.0.0.1:3845` and connects.
   - Needs a Dev or Full seat for useful limits.
3. **REST API with a personal access token** (any provider; reading files, nodes, images, components, styles and comments).
   - **What the user sees:** a "Connect with a token" sheet. It links to Figma's Security settings page; the user pastes the token.
   - Marrow stores the token in Windows Credential Manager (the port's existing secure store) and passes it to the skill's `tools/fetch_figma.py` only in the environment of that tool call. It is never written to disk or logs.
   - The token is used read-only unless the user granted write scopes.

**Fallback.** If no route is available, the skill says which ones exist and how to turn each on. It never pretends to have read a design.

(ED) Marrow does not register its own Figma OAuth app. Doing so would need Figma's review, a client secret embedded in a desktop app, and a place on Figma's MCP client waitlist. This is owner question Q4.

---

## 5. Integration into the app (summary; details stay in PLAN.md)

- **Delivery to providers (PLAN.md 2.1 to 2.4, 2.7).** Delivery is unchanged: per-chat locked generations, `--plugin-dir` (Claude, Grok), `skills/extraRoots/set` (Codex), `skillDirectories` (Copilot), `OPENCODE_CONFIG_CONTENT` (OpenCode), `--skill` (Pi, Command Code), Marrow-side loading (Cursor, Devin, native API providers). Antigravity and Cursor follow Q-SKILLS-8 A (Track P).
- **Name clashes.** With the G1 rule, Marrow's names never equal a vendor skill's name. PLAN.md's equivalence groups (Marrow steps aside when the vendor's own document skill is present) are owner question Q3.
- **Skills page (canvas C1, OD-0070).**
  - Groups as in 2.3.
  - Each row says "Marrow original" and shows needs badges: "Needs internet", "Needs a sign-in to Figma", "Needs an OpenAI key", "Needs Git".
  - "Preparing tools" progress.
  - A "Bundled tools" section listing Python, Node, LibreOffice and Tectonic with versions and licences (read-only; replaces PLAN.md Q7's row).
  - The not-affiliated footer.
- **Keys for skills.** Skills such as `image-generation` and `audio-transcription` need a key. The key is the user's own, chosen on the skill's row, kept in the secure store, and placed only in that skill tool's environment. No key is ever sent to a provider it was not meant for.
- **Missing needs.** The PLAN.md 2.6 probe stays for what is not bundled: Git, gh or glab, Microsoft Office, Figma desktop, the Windows OCR language, Android and .NET SDKs. It keeps the confirmed winget offer with agreements accepted by the user.

---

## 6. Waves

### 6.1 Rules for every unit (from `porting/IMPLEMENTING.md` and this plan)

- Test-first. Each unit has its own worktree, branch and packet. An adversarial review comes before the code is accepted.
- No network in tests. The harness installs a network guard: a Python `sitecustomize` that refuses socket connections and a Node preload that refuses `net`, `http`, `https` and `dns`. Any attempt fails the test.
- Tests never touch the real `%USERPROFILE%`, provider folders or real CLIs (memory: CLI seams fail closed).
- Sample files are Marrow-made. They are generated by scripts in `tests/fixtures/skills/samples/` from Marrow's own text and images, never taken from vendor skills or the web.
- Every unit's report gives the gate reports (G1 to G8), test output and the `origin.json`.

### 6.2 Acceptance pattern for a skill

1. **Static:** G1 to G8 pass. Every tool answers `--help` with output piped and code page 1252.
2. **End-to-end headless:** each tool runs through `marrow-run` on the bundled runtime, on Marrow sample files, offline, writing to a temporary folder. Assertions are specific to the skill (examples in 6.3). Tests check outputs structurally by reopening them with the libraries, round-tripping through LibreOffice and rendering pages to PNG and checking they are not blank, never by eye.
3. **Locked run:** the same run from a locked fixture generation and the locked fixture runtime leaves both trees byte-identical.
4. **Live, recorded at wave close (not CI):** one model-driven use of the skill under Claude, Codex and one native API provider. The transcript is kept as evidence. Keys and logins come from throwaway or the owner's accounts, with the owner present.

### 6.3 Ordered units

Estimates are agent-days of implementation plus review. "Size": S = up to 1, M = 1 to 3, L = 3 to 5.

**Wave 0: foundations** (sequential where marked; about 18 agent-days)

| Unit | Content | Acceptance tests (offline) | Est. |
|---|---|---|---|
| W0-1 Clean-room kit | Capability-spec template (CS-*); reader and implementer packet templates with allowed-source lists; Marrow SKILL.md style guide; the upstream name list (992) and script-basename list (names only); G1 to G4 tools; `origin.json` schema and G6 | Fixtures: a skill named `pdf` fails G1; a tool named `recalc.py` fails G2; a fixture paragraph sharing 12 words with a fixture "upstream" text fails G3, and one sharing 11 passes; a fixture script with 6 copied lines fails G4; a missing `origin.json` field fails G6; the allow-list exempts XML element names only when listed | M, 2 |
| W0-2 Manifest, gates and store (adapts PLAN.md WU-1 and WU-4) | `bundle.json` gains `origin`, `needs`, `runtime` and `explicitOnly`. Upstream pins and patch fields are removed. Store, lock, generations and GC as PLAN.md | PLAN.md's WU-1 and WU-4 tests, minus the upstream-pin and patch cases, plus: exact count 120; every entry's needs drawn from a closed set | M, 2 |
| W0-3 Runtime builder (WU-R1; dev script plus offline gate) | `scripts/runtime/refresh.mjs`; LibreOffice admin-extract and trim; deterministic archives; `runtime.lock`; manifest; G5 licence audit; notices generator. **Measures every size in 3.4** | Offline: G5 on fixture components (an LGPL-only fixture DLL fails unless on the Q1 list; a missing licence text fails); archives built twice from the same input are byte-identical; the notices list every component. The script itself is run once by hand, with its output recorded as evidence | L, 4 |
| W0-4 Runtime store and launcher (WU-R2) | Extraction, lock, seal and full verify, GC, the payload-deletion test with Velopack 1.2.161 in a fake layout, `marrow-run.exe` (python, node, office, ocr, speak, capture, edge-print; timeout and tree kill), PATH append and `MARROW_RUNTIME_ROOT` in provider environments, uninstall hook extension | A fake runtime is extracted, locked and verified; a planted `.pyc`, a changed `.dll` and an extra file are each detected; `marrow-run python -c` runs with `PYTHONPATH` set to a poisoned folder and ignores it; a fake hanging `soffice` is killed with its child on timeout; the provider argv and environment carry `MARROW_RUNTIME_ROOT` and the appended PATH entry and nothing else changes; the uninstall hook removes the locked runtime; timings are recorded against budgets | L, 4 |
| W0-5 `marrow_office` and `marrow_web` | Safe OOXML open and pack (zip-slip and XXE safe), LibreOffice wrapper (per-call profile, `--convert-to`, recalc), PDF render through pypdfium2, page-grid montage, Edge headless print, common report writer | Unit tests on samples: docx to pdf to png gives a non-blank page; xlsx recalc fills cached values; a zip-slip fixture is refused; an XXE fixture is refused | L, 4 |
| W0-6 Sample corpus | Scripts that generate the sample docx, xlsx, pptx and pdf files (forms, tracked changes, scanned image page, merged cells, charts, legacy .doc, .xls and .ppt made by LibreOffice from Marrow's own files) | Generation is deterministic; every sample opens in its library and in LibreOffice | M, 2 |

**Wave 1: core documents** (D1 to D4 in parallel after Wave 0; about 19 agent-days)

| Unit | Acceptance (headless, offline, on samples) | Est. |
|---|---|---|
| D1 `word-documents` | A generated report has headings, a contents field, a table, an image, a footnote, a header and footer and two sections: python-docx reopens it, LibreOffice loads it without error, and page 1 renders non-blank. Find and replace across split runs keeps bold and italic. Inserted and deleted tracked changes carry author and date, and accept-all and reject-all give the expected texts. A comment is anchored to the right range. Template fields fill; mail merge writes N files. docx to pdf has the expected page count; .doc to docx works. Markdown extraction keeps headings and tables | L, 5 |
| D2 `spreadsheet-workbooks` | A built workbook has formulas, named ranges, formats, conditional formatting, validation, a chart and freeze panes. Recalculation gives the expected cached values. The error scan finds the planted `#DIV/0!` and `#REF!` with their cells. pandas reads the messy-header sample with the right header row. An `.xlsm` round-trip keeps `vbaProject.bin` byte-identical and never runs it. The model-conventions lint flags hard-coded numbers in formula cells. xlsx to csv and ods work | L, 5 |
| D3 `slide-decks` | An outline JSON gives a deck with title, section, bullets, two-column, chart, table, image and notes layouts. The thumbnail grid PNG has the right tile count. The overflow check flags the planted overflowing box. An edit replaces text and keeps the layout; reorder, duplicate and delete work; the template's layouts are applied. pptx to pdf works. Text and notes are extracted | L, 5 |
| D4 `pdf-documents` | Text and tables are extracted from the samples. Merge, split, rotate and crop give the expected page counts. An AcroForm is filled (text, checkbox, radio, choice) and flattened, and the values are read back. Text is overlaid on the non-fillable form at given coordinates. A reportlab report has a table, an image and non-Latin text. Watermark; AES-256 encrypt and decrypt; metadata and bookmarks; image extraction; page render; text diff of two PDFs. OCR of the scanned page through `marrow-run ocr` finds the expected words; on a machine without the OCR language, the "missing language" path is asserted. A recorded live run covers the real OCR | L, 4 |

**Wave 2: document workflow** (D5 to D12; about 12 agent-days)

| Unit | Acceptance | Est. |
|---|---|---|
| D5 `office-conversions` | A batch over every sample type gives the expected outputs and page counts; HTML to PDF through Edge headless; a corrupt input gives a clear error and no hang | S, 1.5 |
| D6 `document-templates` | Each starter template fills from JSON; a template made from a sample docx, xlsx and pptx keeps its styles | M, 2 |
| D7 `document-drafting` | The outline-to-docx tool builds the skeleton; the section-merge tool keeps order and headings | S, 1 |
| D8 `team-updates` | The report tool renders a status update from JSON to md and docx | S, 1 |
| D9 `visual-themes` | Applying each theme to sample docx, pptx and html changes the expected colours and fonts | S, 1 |
| D10 `python-notebooks` | Create, edit and execute a sample notebook with the bundled kernel, offline; outputs are stored; nbformat validates | M, 2 |
| D11 `latex-papers` | Compiling a sample that uses only Tectonic's cached format works offline. A first-use fetch is asserted to fail cleanly with "needs internet" under the network guard | S, 1.5 |
| D12 `office-desktop-automation` | Tests run against a fake COM server script (no Office in CI); the real Office path is recorded live | M, 2 |

**Wave 3: the default-on coding and design set** (B1 to B4, B9 to B13, B17, B19, V1, V3; 13 skills, about 20 agent-days)
- Each has a fixture git repository for its tools: diff scope, findings to report, CI log parser (GitHub, GitLab and CircleCI log fixtures), ownership map from a fixture history, threat-model skeleton from a fixture repo, a skill validator and eval runner against a fake provider CLI, a plugin packager whose output is checked against both plugin formats, an MCP server scaffold whose stdio handshake test passes offline, poster rendering to PNG and PDF with bundled fonts, and a UI lint.
- `ship-branch` is explicit-only. Its test proves that it builds the commands, runs nothing without being asked, and works against a fake `gh`.

**Wave 4: the rest of the coding set** (B5 to B8, B14 to B16, B18, B20 to B37; 26 skills, about 24 agent-days)
- `browser-testing` drives a fixture local site through playwright-core with the `msedge` channel, which needs Edge on the test machine. It captures a screenshot and console output, and the server-lifecycle tool leaves no process behind.
- `web-research-report`, `ai-provider-apis` and `shadcn-components` test their offline parts (report builder, citation checker) and assert that the network guard is hit cleanly.

**Wave 5: design and media** (V2, V4 to V18; 16 skills, about 24 agent-days)
- `web-app-builder` bundles a fixture React app into one HTML file offline with esbuild.
- `diagrams` renders a fixture diagram to SVG and PNG.
- `chart-building` makes matplotlib and D3 outputs.
- `speech-synthesis` makes a WAV with the installed Windows voice, with a recorded live check.
- `screen-capture` captures the virtual desktop, and the test proves explicit-only.
- `figma-design` (M, 3) is tested against a fake Figma REST server and a fake MCP endpoint. The token never appears in logs or files. The three routes, plus the "no route" message, are each asserted. The live sign-in under Claude Code, Codex and Cursor is recorded in Track P.

**Wave 6: data and business** (A1 to A3, W1 to W17; 20 skills, about 20 agent-days)
- Each ships tools that turn its structured findings (JSON) into its document outputs: pipeline review workbook, contract issue table with redline docx, support macro, KPI scorecard and others. Tests run them on Marrow-made sample data.
- `contract-analysis` and `legal-drafting` assert that the "not legal advice" line is present in every output.

**Wave 7: finance and science** (F1 to F6, S1 to S6; 12 skills, about 18 agent-days)
- The model builders produce workbooks whose recalculated outputs match hand-computed fixtures: the DCF value, the LBO IRR within tolerance, and a balance sheet that balances.
- Deck builders use D3.
- Science skills test their parsers and report tools on Marrow-made sample files: FASTQ QC summary, Allotrope-style JSON. Database clients run against fake HTTP servers.

**Wave 8: integrations** (I1 to I21; 21 skills, about 21 agent-days)
- Each is tested against a fake HTTP server or a fake CLI shaped from the vendor's public API reference. No real calls are made in tests.
- Live checks happen only with the owner's accounts, at wave close, recorded.

**Track P: provider wiring and the app** (runs alongside Waves 1 to 4; about 25 agent-days; reuses PLAN.md units)
- **PLAN.md WU-5:** clash scanner. Simplified, because G1 names cannot clash with vendor names; the user's own same-named skills still win.
- **WU-21 and WU-14:** budget cap and settings model.
- **WU-6 to WU-10:** provider wiring, plus the runtime `PATH` and environment.
- **WU-11 live checks, extended with:**
  - the runtime under Codex's elevated sandbox;
  - each allow-listed CLI's Figma sign-in;
  - **Q-SKILLS-8:** the Marrow-owned plugin in Antigravity's and Cursor's plugin folders. It is installed only after this live test shows that it loads and that removing it leaves nothing behind. Until then those two get skills by explicit pick.
- **WU-12:** Marrow-side loading.
- **WU-13:** the dependency probe, for non-bundled needs only.
- **WU-15 and WU-16:** canvas C1 to C5 first, then the page.
- **WU-17:** notices, now including the runtime.
- **WU-18:** packaging, with a payload size budget from W0-3's measurement. The Velopack installer is **not** run by any agent without separate authorization.

### 6.4 Estimate summary

| Wave | Skills | Agent-days (estimate) |
|---|---|---|
| 0 Foundations | 0 | 18 |
| 1 Core documents | 4 | 19 |
| 2 Document workflow | 8 | 12 |
| 3 Default-on coding and design | 13 | 20 |
| 4 Rest of coding | 26 | 24 |
| 5 Design and media | 16 | 24 |
| 6 Data and business | 20 | 20 |
| 7 Finance and science | 12 | 18 |
| 8 Integrations | 21 | 21 |
| Track P | n/a | 25 |
| **Total** | **120** | **about 200** |

Each skill unit also needs one reader pass, to write its capability spec, before the implementer starts. That is included above at about 0.3 agent-days per skill.

**Order and parallelism:**
- Wave 0 must finish first, except that W0-1 and W0-6 can run alongside W0-3.
- Within a wave, units run in parallel.
- Waves 1 and 2 must precede Waves 6 and 7, because those reuse `marrow_office`.
- Track P can start with Wave 1.

---

## 7. Open questions for the owner

| # | Question | Recommendation |
|---|---|---|
| Q1 | **Licence exceptions for the bundled tools.** The rule is MIT, BSD, Apache-2.0, PSF and file-level MPL-2.0 only. But LibreOffice (which you asked for) contains some libraries under LGPL or other open licences, the fonts are OFL, and a few Python parts (Pillow, the numpy and pandas Windows builds, d3) use other permissive licences or carry a compiler runtime licence. Allow these as unmodified, separately loaded parts, with their notices and the source offer that LGPL asks for? | Yes: allow, on a reviewed list that WU-R1 produces, the permissive variants (ISC, MIT-CMU, Zlib, FTL, IJG, Unicode, BSL-1.0, Matplotlib), OFL-1.1 fonts, and LGPL libraries only when shipped unmodified as separate DLLs. The alternative (strict list) means no LibreOffice, and so no PDF conversion, page previews or formula recalculation for Word, Excel and PowerPoint files |
| Q2 | **Under what licence does Marrow's own skill text ship?** Users can see the files and may copy a skill into their own folder to change it | Marrow's own terms, with explicit permission for users to copy and adapt the skills for their own use |
| Q3 | **When a provider already brings its vendor's own Word, Excel, PowerPoint or PDF skill** (Claude for signed-in claude.ai users; Codex when the Codex desktop app is installed), should Marrow's document skills still show in that provider, or step aside? | Keep Marrow's showing. They work offline with Marrow's bundled tools and on Windows; Anthropic's own document scripts fail on Windows Python (research-anthropic.md 4). The model then sees both, and Marrow's descriptions say they use Marrow's tools |
| Q4 | **Figma: register a Marrow Figma app?** That would let Marrow sign in to Figma itself under every provider, but it needs Figma's review, a client secret inside the app, and joining Figma's waitlist for MCP clients | No. Use Figma's sign-in through Claude Code, Codex and Cursor, the Figma desktop app's server, or a personal token (section 4). Revisit if you want Figma under the API providers without the desktop app |

---

## 8. Sources

- This folder: `research-anthropic.md`, `research-openai.md`, `research-loading.md`, `PLAN.md` (revision 3), `OWNER-BRIEF.md`, `CHALLENGE-1.md`, `CHALLENGE-2.md`. Owner decision OD-0080 in `E:\Droppy\wt\cl\terminal-verify\porting\owner_decisions.md:735-738`.
- **Port code read:**
  - `src\Marrow.Core\Models\Provider.cs:381-494` (Codex skills as slash commands);
  - `src\Marrow.Services\Providers\ClaudeSession.cs:1028`, `CopilotSession.cs:1306`, `CommandCodeSession.cs:831` (Skill rows);
  - `CodexSession.cs:645`;
  - `src\Marrow.Services\MCP\MCPOAuth.cs` (MCP OAuth client).
  - No bundle or runtime code exists yet: no `bundle.json`, store or uninstall hook in `E:\Droppy\wt\integration\src`.
- **Upstream repositories:** read as name and description listings from the earlier research clones in `C:\Users\omera\AppData\Local\Temp\claude\E--Droppy\c13e7b6a-fc79-4b04-a170-a4b2f43ca682\scratchpad\` (`skills`, `knowledge-work-plugins`, `financial-services`, `life-sciences`, `claude-plugins-official`, `claude-code`, `oskills`, `oplugins`, `codex-src`). Remote HEADs were re-checked on 2026-10-04 with `git ls-remote`, and the changed `claude-plugins-official` and `openai/codex` heads through the GitHub API (no skill change).
- **Figma:**
  - https://developers.figma.com/docs/figma-mcp-server/remote-server-installation/
  - https://developers.figma.com/docs/figma-mcp-server/local-server-installation/
  - https://developers.figma.com/docs/figma-mcp-server/rate-limits-access/
  - https://developers.figma.com/docs/rest-api/authentication/
  - https://developers.figma.com/docs/rest-api/personal-access-tokens/
  - https://developers.figma.com/docs/rest-api/oauth-apps/
- **LibreOffice download size:** https://us.libreoffice.org/download/libreoffice-stable/?type=win-x86_64&version=25.8.7&lang=en-US (349 MB installer, via search result).
- **Local, read-only measurements (2026-10-04):**
  - `C:\Users\omera\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\Lib\site-packages` (per-package sizes);
  - `C:\Program Files\nodejs` (98 MB);
  - LibreOffice is not installed on this PC.
