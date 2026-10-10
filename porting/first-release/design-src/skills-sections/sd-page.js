// Skills downloads, round 5, all picks made (OD-0099: Page D, Offer A, Words S1; separate Plugins, Skills and Tools pages with a
// Download all each; Minimize A; Invoke A; Custom C; the owner's points of 2026-10-04), the interactive decision page. One model (every item's
// state and progress, every skill's Enable switch, the Download all run, the add sheet's installs, the chat) drives three
// Settings windows, the main window a hidden download goes on in, and the chat window. Every change goes through a
// transition (OD-0076). No eval, no network; storage only through Kit.store (guarded).
(function () {
  const D = JSON.parse(document.getElementById('sddata').textContent);
  const ICON = D.icon;
  const K = window.Kit;
  const S = K.store;
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const reduce = !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const IMG = () => '<img alt="" src="' + ICON + '">';
  // the kit crossfades a theme change with a View Transition; in a hidden document the browser aborts it and rejects
  // its promises (InvalidStateError). The theme still changes; only that one benign rejection is silenced here.
  window.addEventListener('unhandledrejection', (e) => {
    const r = e.reason;
    if (r && r.name === 'InvalidStateError' && /Transition/.test(String(r.message))) e.preventDefault();
  });

  // ------------------------------------------------------------------ glyphs (Marrow's own line glyphs; no vendor marks)
  const GL = {
    doc: '<path d="M4 2h5.5L12 4.5V14H4Z"/><path d="M9.5 2v2.5H12M6 8h4M6 10.5h4"/>',
    spark: '<path d="M6.5 2.5 7.6 5.4 10.5 6.5 7.6 7.6 6.5 10.5 5.4 7.6 2.5 6.5 5.4 5.4Z"/><path d="M11.5 9.5l.6 1.4 1.4.6-1.4.6-.6 1.4-.6-1.4-1.4-.6 1.4-.6Z"/>',
    stack: '<path d="M8 2 14 5 8 8 2 5Z"/><path d="M2 8l6 3 6-3M2 11l6 3 6-3"/>',
    brief: '<rect x="2.5" y="5" width="11" height="8" rx="1.5"/><path d="M6 5V3.6h4V5M2.5 8.5h11"/>',
    code: '<path d="m5.5 4.5-3 3.5 3 3.5M10.5 4.5l3 3.5-3 3.5M9 3 7 13"/>',
    pkg: '<path d="M8 2 13.5 5v6L8 14 2.5 11V5Z"/><path d="M2.5 5 8 8l5.5-3M8 8v6"/>',
    node: '<path d="M8 1.8 13.4 4.9v6.2L8 14.2 2.6 11.1V4.9Z"/><path d="M8 5.2v5.6M5.6 8h4.8"/>',
    office: '<rect x="2.5" y="2.5" width="11" height="11" rx="2"/><path d="M5 6h6M5 8.5h6M5 11h3.5"/>',
    pdf: '<path d="M4 2h5.5L12 4.5V14H4Z"/><path d="M6 12V8.4h1.3a1.1 1.1 0 0 1 0 2.2H6"/>',
    plug: '<path d="M6 2v3M10 2v3M4.5 5h7v2.5a3.5 3.5 0 0 1-7 0Z"/><path d="M8 11v3"/>',
    chart: '<path d="M2.5 13.5h11M4.5 11V8M8 11V4.5M11.5 11V6.5"/>',
    flask: '<path d="M6.5 2h3M7 2v4L3.4 12.3A1.2 1.2 0 0 0 4.5 14h7a1.2 1.2 0 0 0 1.1-1.7L9 6V2"/><path d="M5 10h6"/>',
    git: '<circle cx="5" cy="4" r="1.6"/><circle cx="5" cy="12" r="1.6"/><circle cx="11" cy="6.5" r="1.6"/><path d="M5 5.6v4.8M11 8.1c0 2-2.5 2.2-6 2.6"/>',
    term: '<rect x="2" y="3" width="12" height="10" rx="2"/><path d="m4.8 6.5 2 1.5-2 1.5M8.5 10h3"/>',
    globe: '<circle cx="8" cy="8" r="5.6"/><path d="M2.4 8h11.2M8 2.4c1.8 1.8 1.8 9.4 0 11.2M8 2.4c-1.8 1.8-1.8 9.4 0 11.2"/>',
    bolt: '<path d="M9 1.8 3.8 9h3.6L7 14.2 12.2 7H8.6Z"/>',
    img: '<rect x="2.5" y="3" width="11" height="10" rx="1.5"/><circle cx="6" cy="6.5" r="1.2"/><path d="m2.5 11.5 3.5-3 2.5 2 2-1.5 3 2.5"/>',
    dl: '<path d="M8 2.5v7.5M4.8 7 8 10.2 11.2 7M3 13h10"/>',
    plus: '<path d="M8 3v10M3 8h10"/>',
    folder: '<path d="M2 4.5A1.5 1.5 0 0 1 3.5 3h2.8l1.4 1.5h4.8A1.5 1.5 0 0 1 14 6v5.5a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 11.5Z"/>',
    zip: '<path d="M4 2h5.5L12 4.5V14H4Z"/><path d="M7 3v1M7 5v1M7 7v1M6.2 9.5h1.6v2H6.2Z"/>',
    link: '<path d="M6.8 9.2a2.6 2.6 0 0 0 3.7 0l2-2a2.6 2.6 0 0 0-3.7-3.7l-.6.6M9.2 6.8a2.6 2.6 0 0 0-3.7 0l-2 2a2.6 2.6 0 0 0 3.7 3.7l.6-.6"/>',
  };
  const g = (k, cls) => '<span class="ig' + (cls ? ' ' + cls : '') + '"><svg viewBox="0 0 16 16" aria-hidden="true">' + GL[k] + '</svg></span>';
  const sv16 = (k, cls) => '<svg class="' + (cls || 'ic16') + '" viewBox="0 0 16 16" aria-hidden="true">' + GL[k] + '</svg>';
  const IC = {
    general: '<circle cx="8" cy="8" r="2.2"/><path d="M8 1.8v1.8M8 12.4v1.8M1.8 8h1.8M12.4 8h1.8M3.6 3.6l1.3 1.3M11.1 11.1l1.3 1.3M3.6 12.4l1.3-1.3M11.1 4.9l1.3-1.3"/>',
    hydra: '<circle cx="5" cy="5.5" r="2"/><circle cx="11" cy="5.5" r="2"/><circle cx="8" cy="11" r="2"/><path d="M6.3 7 7.2 9.3M9.7 7 8.8 9.3"/>',
    appearance: '<circle cx="8" cy="8" r="5.6"/><path d="M8 2.4v11.2A5.6 5.6 0 0 0 8 2.4Z"/>',
    custom: '<path d="M3 4.5h6M11.5 4.5H13M3 11.5h1.5M7 11.5h6"/><circle cx="10" cy="4.5" r="1.4"/><circle cx="5.6" cy="11.5" r="1.4"/>',
    voice: '<rect x="6" y="2.2" width="4" height="7.6" rx="2"/><path d="M3.8 8a4.2 4.2 0 0 0 8.4 0M8 12.2v1.8"/>',
    providers: '<rect x="2.5" y="3" width="11" height="4" rx="1.2"/><rect x="2.5" y="9" width="11" height="4" rx="1.2"/><path d="M5 5h.1M5 11h.1"/>',
    tools: '<path d="M10.4 2.6a3 3 0 0 0-3.7 3.9L2.8 10.4a1.3 1.3 0 0 0 1.8 1.8l3.9-3.9a3 3 0 0 0 3.9-3.7l-1.8 1.8-1.6-.4-.4-1.6Z"/>',
    mcp: '<path d="M3 4h6l4 4-4 4H3Z"/>',
    skills: GL.spark, plugins: GL.plug,
    models: '<path d="M3 5h10M3 8h10M3 11h10"/><circle cx="6" cy="5" r="1.2"/><circle cx="10" cy="8" r="1.2"/><circle cx="7" cy="11" r="1.2"/>',
    source: '<path d="M4 3v3.5C4 8 8 8 8 10v3M12 3v3.5C12 8 8 8 8 10"/>',
    shortcuts: '<rect x="2.2" y="4" width="11.6" height="8" rx="1.6"/><path d="M4.6 6.6h.1M7 6.6h.1M9.4 6.6h.1M11.6 6.6h.1M5.4 9.4h5.2"/>',
    archive: '<rect x="2.6" y="3.4" width="10.8" height="3" rx=".8"/><path d="M3.6 6.4v6h8.8v-6M6.6 9h2.8"/>',
    about: '<circle cx="8" cy="8" r="5.6"/><path d="M8 7.2v3.6M8 5.2v.1"/>',
    gear: '<circle cx="8" cy="8" r="2.2"/><path d="M8 1.8v1.8M8 12.4v1.8M1.8 8h1.8M12.4 8h1.8M3.6 3.6l1.3 1.3M11.1 11.1l1.3 1.3M3.6 12.4l1.3-1.3M11.1 4.9l1.3-1.3"/>',
  };
  const ic = (k) => '<svg class="ic16" viewBox="0 0 16 16" aria-hidden="true">' + IC[k] + '</svg>';
  const OKC = '<svg class="okc" viewBox="0 0 14 14" aria-hidden="true"><circle cx="7" cy="7" r="7"/><path d="m4 7.2 2 2 4-4.4"/></svg>';
  const WRN = '<svg class="wrn" viewBox="0 0 14 14" aria-hidden="true"><path d="M7 1.2 13.4 12.6H.6Z"/><path d="M7 5.4v3.2M7 10.6v.1"/></svg>';
  const SPN = '<span class="spn" aria-hidden="true">' + [0, 1, 2, 3, 4, 5, 6, 7].map((k) => '<i style="--k:' + k + '"></i>').join('') + '</span>';
  const RING = '<svg class="ring" viewBox="0 0 20 20" aria-hidden="true"><circle class="t" cx="10" cy="10" r="7.5" pathLength="100"/><circle class="a" cx="10" cy="10" r="7.5" pathLength="100"/></svg>';
  const XB = (act) => '<span class="xb" data-act="' + act + '" role="button" aria-label="Cancel"><svg viewBox="0 0 8 8"><path d="m1 1 6 6M7 1 1 7"/></svg></span>';
  const TRASH = (act) => '<span class="tb" data-act="' + act + '" role="button" aria-label="Remove"><svg viewBox="0 0 16 16"><path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5M7 7v4M9 7v4"/></svg></span>';
  const BAR = '<span class="bar"><i></i></span>';
  const CAP3 = '<div class="cap3"><span><svg viewBox="0 0 10 10"><path d="M1 5h8"/></svg></span><span><svg viewBox="0 0 10 10"><rect x="1" y="1" width="8" height="8" rx="1.5"/></svg></span><span><svg viewBox="0 0 10 10"><path d="m1.5 1.5 7 7M8.5 1.5l-7 7"/></svg></span></div>';

  // ------------------------------------------------------------------ the catalogue (pinned 2026-10-04; ONDEMAND-PLAN.md 2)
  // mb = download (SF); disk = on disk once installed (UN: the plan's unpacked estimates, 2.5x for Python package sets).
  // sk = the skills inside a set with their default switch (Q-SKILLS-1 A, ED) and an example listing cost in tokens.
  const tok = (n) => { let h = 7; for (const c of n) h = (h * 31 + c.charCodeAt(0)) % 997; return 46 + (h % 52); };
  const SK = (spec) => spec.split(' ').map((x) => { const off = x.startsWith('-'); const n = off ? x.slice(1) : x; return { n, on: !off, t: tok(n) }; });
  const PL = (spec) => spec.split(' ').map((x) => { const [n, c] = x.split(':'); return { n, c: +c, on: false, t: +c * 64, pl: 1 }; });
  const ITEMS = [
    { id: 'akw', sec: 'plug', name: 'Knowledge work plugins', sum: 'Sales, legal, finance, data, support and more', gl: 'brief', mb: 8.1, ver: '18 plugins · 252 skills', host: 'github.com', src: 'anthropics/knowledge-work-plugins', lic: 'Apache-2.0 · MIT (partners)', srcFull: 'github.com/anthropics/knowledge-work-plugins at 8444efc',
      sk: PL('sales:36 small-business:44 partner-built:71 data:10 engineering:10 legal:9 human-resources:9 operations:9 finance:8 marketing:8 product-management:8 design:7 bio-research:6 customer-support:5 enterprise-search:5 productivity:4 cowork-plugin-management:2 pdf-viewer:1') },
    { id: 'afs', sec: 'plug', name: 'Financial services plugins', sum: 'Agent, vertical and partner plugins for finance work', gl: 'chart', mb: 2.0, ver: '3 groups · 112 skills', host: 'github.com', src: 'anthropics/financial-services', lic: 'Apache-2.0', srcFull: 'github.com/anthropics/financial-services at 574ed36',
      sk: PL('agent-plugins:51 vertical-plugins:49 partner-built:11 msft-365-verify:1') },
    { id: 'oplg', sec: 'plug', name: 'OpenAI plugins', sum: 'Web apps, data visualisation, games, Drive, Zotero', gl: 'plug', mb: 3.9, ver: '9 plugins · 65 skills', host: 'github.com', src: 'openai/plugins', lic: 'MIT', srcFull: 'github.com/openai/plugins at 5fd93af',
      sk: PL('build-web-apps:6 build-web-data-visualization:18 game-studio:9 plugin-eval:5 test-android-apps:2 google-drive:5 ngs-analysis:18 zotero:1 sentry:1') },
    { id: 'oprp', sec: 'plug', name: 'OpenAI plugins, proprietary-labelled', sum: 'Security, data analytics, research, design, investing', gl: 'plug', mb: 18.2, ver: '8 plugins · 124 skills', host: 'github.com', src: 'openai/plugins', lic: 'Proprietary (plugin.json); one says internal use only', srcFull: 'github.com/openai/plugins at 5fd93af, licence files kept',
      sk: PL('codex-security:15 data-analytics:18 life-science-research:50 product-design:10 public-equity-investing:23 openai-developers:5 creative-production:2 openai-ads-conversions:1') },
    { id: 'adoc', sec: 'skill', name: 'Anthropic document skills', sum: 'Word, Excel, PowerPoint and PDF, and co-authoring', gl: 'doc', mb: 3.5, ver: '5 skills', host: 'github.com', src: 'anthropics/skills', lic: 'Proprietary (Anthropic’s terms); doc-coauthoring: none stated', srcFull: 'github.com/anthropics/skills at 8a1541c, licence files kept',
      needs: 'Python and its Office and PDF packages, Node.js document packages, LibreOffice, Poppler, pandoc, qpdf. On Windows their LibreOffice calls fail (AF_UNIX).',
      sk: SK('docx xlsx pptx pdf doc-coauthoring') },
    { id: 'odoc', sec: 'skill', name: 'OpenAI document skills', sum: 'Word, Excel, PowerPoint and PDF files', gl: 'doc', mb: 0.24, ver: '4 skills', host: 'github.com', src: 'openai/skills', lic: 'Apache-2.0', srcFull: 'github.com/openai/skills, pinned commits', needs: 'Python, its Office and PDF packages, LibreOffice, Poppler',
      sk: SK('doc spreadsheet slides pdf') },
    { id: 'aex', sec: 'skill', name: 'Anthropic example skills', sum: 'Design, art, web testing, MCP servers, skill building', gl: 'spark', mb: 8.1, ver: '13 skills', host: 'github.com', src: 'anthropics/skills', lic: 'Apache-2.0', srcFull: 'github.com/anthropics/skills at 8a1541c', needs: 'Python, Node.js; Playwright’s Chromium for webapp-testing',
      sk: SK('-academy-guide -algorithmic-art canvas-design -claude-api -discernment-nudge frontend-design -internal-comms -mcp-builder skill-creator -slack-gif-creator theme-factory web-artifacts-builder webapp-testing') },
    { id: 'ocur', sec: 'skill', name: 'OpenAI curated skills', sum: 'GitHub, security reviews, notebooks, deploys', gl: 'stack', mb: 3.3, ver: '30 skills', host: 'github.com', src: 'openai/skills', lic: 'Apache-2.0 · MIT', srcFull: 'github.com/openai/skills at 49f948f', needs: 'Python, GitHub CLI, uv; keys and logins for the deploy skills',
      sk: SK('gh-address-comments gh-fix-ci -yeet jupyter-notebook security-best-practices security-threat-model security-ownership-map aspnet-core cli-creator define-goal -screenshot'), more: 19 },
    { id: 'ocx', sec: 'skill', name: 'Codex’s built-in skills', sum: 'For providers other than Codex: images, OpenAI docs, review', gl: 'code', mb: 0.28, ver: '4 skills', host: 'github.com', src: 'openai/codex', lic: 'Apache-2.0', srcFull: 'github.com/openai/codex at 2739e82', needs: 'Python, Node.js; an OpenAI key for imagegen',
      sk: SK('-imagegen openai-docs review-agent skill-creator') },
    { id: 'als', sec: 'skill', name: 'Anthropic life sciences skills', sum: 'Clinical trials, single-cell RNA, lab data', gl: 'flask', mb: 1.1, ver: '6 skills', host: 'github.com', src: 'anthropics/life-sciences', lic: 'Apache-2.0', srcFull: 'github.com/anthropics/life-sciences at e96556b', needs: 'Python; science packages named in the skills',
      sk: SK('-clinical-trial-protocol-skill -instrument-data-to-allotrope -nextflow-development -scientific-problem-selection -scvi-tools -single-cell-rna-qc') },
    { id: 'py', sec: 'tool', name: 'Python', sum: 'Runs the Python parts of skills, with pip', gl: 'code', mb: 14.4, disk: 33, ver: '3.14.8', host: 'python.org', src: 'python.org', lic: 'PSF-2.0 · MIT', srcFull: 'python.org, Windows embeddable x64; pip 26.2.1 from pypi.org' },
    { id: 'pyd', sec: 'tool', name: 'Python: Office and PDF', sum: 'python-docx, openpyxl, python-pptx, pypdf, reportlab…', gl: 'pkg', mb: 22.5, disk: 56.3, ver: '2 package sets', host: 'pypi.org', src: 'pypi.org', lic: 'MIT · BSD · Apache-2.0', srcFull: 'pypi.org, pinned wheels' },
    { id: 'pyi', sec: 'tool', name: 'Python: images and data', sum: 'pillow, numpy, pandas, matplotlib', gl: 'img', mb: 42.9, disk: 107.3, ver: 'package set', host: 'pypi.org', src: 'pypi.org', lic: 'MIT-CMU · BSD', srcFull: 'pypi.org, pinned wheels' },
    { id: 'pyb', sec: 'tool', name: 'Python: browser testing', sum: 'playwright, greenlet, pyee', gl: 'pkg', mb: 38.9, disk: 97.3, ver: 'package set', host: 'pypi.org', src: 'pypi.org', lic: 'Apache-2.0 · MIT', srcFull: 'pypi.org, pinned wheels' },
    { id: 'pyg', sec: 'tool', name: 'Python: GIF making', sum: 'imageio, imageio-ffmpeg', gl: 'img', mb: 31.6, disk: 79, ver: 'package set', host: 'pypi.org', src: 'pypi.org', lic: 'BSD-2 · FFmpeg GPL', srcFull: 'pypi.org, pinned wheels' },
    { id: 'node', sec: 'tool', name: 'Node.js', sum: 'Runs the JavaScript parts of skills', gl: 'node', mb: 38, disk: 95, ver: '24.21.0', host: 'nodejs.org', src: 'nodejs.org', lic: 'MIT', srcFull: 'nodejs.org, Windows x64 zip' },
    { id: 'lo', sec: 'tool', name: 'LibreOffice', sum: 'Converts and renders Office files', gl: 'office', mb: 356, disk: 1050, ver: '26.2.6', host: 'libreoffice.org', src: 'libreoffice.org', lic: 'MPL-2.0', fw: 'sum', srcFull: 'libreoffice.org, Windows x64 MSI' },
    { id: 'pop', sec: 'tool', name: 'Poppler', sum: 'Turns PDF pages into images', gl: 'pdf', mb: 43.7, disk: 110, ver: '26.09.0', host: 'github.com', src: 'conda-forge build', lic: 'GPL-2.0', srcFull: 'conda-forge build, via poppler-windows' },
    { id: 'qpdf', sec: 'tool', name: 'qpdf', sum: 'Splits, merges and repairs PDFs', gl: 'pdf', mb: 28.2, disk: 70, ver: '12.4.2', host: 'github.com', src: 'qpdf/qpdf', lic: 'Apache-2.0', srcFull: 'github.com/qpdf/qpdf releases' },
    { id: 'pandoc', sec: 'tool', name: 'pandoc', sum: 'Converts between document formats', gl: 'doc', mb: 42.1, disk: 200, ver: '3.12', host: 'github.com', src: 'jgm/pandoc', lic: 'GPL-2.0', srcFull: 'github.com/jgm/pandoc releases' },
    { id: 'git', sec: 'tool', name: 'Git for Windows', sum: 'Git Bash, for skills’ .sh scripts', gl: 'git', mb: 60, disk: 350, ver: '2.56.0', host: 'github.com', src: 'git-for-windows', lic: 'GPL-2.0', srcFull: 'github.com/git-for-windows, portable' },
    { id: 'gh', sec: 'tool', name: 'GitHub CLI', sum: 'For the GitHub skills; you sign in yourself', gl: 'term', mb: 15.5, disk: 45, ver: '2.102.0', host: 'github.com', src: 'cli/cli', lic: 'MIT', srcFull: 'github.com/cli/cli releases' },
    { id: 'uv', sec: 'tool', name: 'uv', sum: 'Runs Python projects and notebooks', gl: 'bolt', mb: 18, disk: 45, ver: '0.12.23', host: 'github.com', src: 'astral-sh/uv', lic: 'MIT · Apache-2.0', srcFull: 'github.com/astral-sh/uv releases' },
    { id: 'chr', sec: 'tool', name: 'Playwright Chromium', sum: 'A headless browser for web-app testing', gl: 'globe', mb: 325.3, disk: 600, ver: '153', host: 'cdn.playwright.dev', src: 'cdn.playwright.dev', lic: 'BSD-3 and notices', srcFull: 'cdn.playwright.dev, the build playwright 1.63.0 pins' },
  ];
  ITEMS.forEach((i) => { if (i.disk == null) i.disk = i.mb; });
  const IT = Object.fromEntries(ITEMS.map((i) => [i.id, i]));
  const SECS = { plug: 'Plugins', skill: 'Skills', tool: 'Tools' };
  const SECL = { plug: 'plugins', skill: 'skills', tool: 'tools' };
  const bySec = (s) => ITEMS.filter((i) => i.sec === s);
  const USED = { py: 'Every skill set and plugin with scripts', pyd: 'OpenAI document skills', pyi: 'Document and data skills', pyb: 'webapp-testing', pyg: 'slack-gif-creator', node: 'Anthropic example skills, OpenAI plugins for the web',
    lo: 'OpenAI document skills', pop: 'OpenAI document skills', qpdf: 'PDF skills', pandoc: 'Document skills', git: 'Skills with .sh scripts', gh: 'gh-address-comments, gh-fix-ci, yeet', uv: 'jupyter-notebook', chr: 'webapp-testing' };


  // where to get each item by hand when its download keeps failing (ONDEMAND-PLAN.md 2.1 and 2.2: the pinned files)
  const REPO = (r, c) => ['the repository at commit ' + c + ', as a .zip', 'github.com/' + r + '/tree/' + c];
  const WHEELS = ['the pinned wheels named in wheels.txt in that folder', 'pypi.org'];
  const FB = {
    akw: REPO('anthropics/knowledge-work-plugins', '8444efc'), afs: REPO('anthropics/financial-services', '574ed36'), oplg: REPO('openai/plugins', '5fd93af'), oprp: REPO('openai/plugins', '5fd93af'),
    adoc: REPO('anthropics/skills', '8a1541c'), aex: REPO('anthropics/skills', '8a1541c'), ocur: REPO('openai/skills', '49f948f'), ocx: REPO('openai/codex', '2739e82'), als: REPO('anthropics/life-sciences', 'e96556b'),
    odoc: ['the three skill folders at their pinned commits (45d05d7, e6afb0d, 49f948f)', 'github.com/openai/skills'],
    py: ['python-3.14.8-embed-amd64.zip and pip-26.2.1-py3-none-any.whl', 'python.org/ftp/python/3.14.8/'], pyd: WHEELS, pyi: WHEELS, pyb: WHEELS, pyg: WHEELS,
    node: ['node-v24.21.0-win-x64.zip', 'nodejs.org/dist/v24.21.0/'], lo: ['LibreOffice_26.2.6_Win_x86-64.msi', 'download.documentfoundation.org/libreoffice/stable/26.2.6/win/x86_64/'],
    pop: ['Release-26.09.0-0.zip', 'github.com/oschwartz10612/poppler-windows/releases'], qpdf: ['qpdf-12.4.2-msvc64.zip', 'github.com/qpdf/qpdf/releases'],
    pandoc: ['pandoc-3.12-windows-x86_64.zip', 'github.com/jgm/pandoc/releases'], git: ['PortableGit-2.56.0-64-bit.7z.exe', 'github.com/git-for-windows/git/releases'],
    gh: ['gh_2.102.0_windows_amd64.zip', 'github.com/cli/cli/releases'], uv: ['uv-x86_64-pc-windows-msvc.zip', 'github.com/astral-sh/uv/releases'],
    chr: ['chrome-win64.zip and chrome-headless-shell-win64.zip', 'cdn.playwright.dev/builds/cft/153.0.8010.12/win64/'],
  };
  const MANUAL = '%LOCALAPPDATA%\\nth\\addons\\downloads\\manual\\';

  // ------------------------------------------------------------------ your own skills, plugins and tools (the Custom question)
  // what the add sheet finds, per page (illustrative): a GitHub repo of six skills (one invalid), a repo of three plugins
  // (one without a manifest), one tool. Each found item becomes an entry of its own once it is added.
  const FOUND = {
    skill: [['release-notes', 'Writes release notes from merged pull requests'], ['changelog', 'Keeps CHANGELOG.md in the house style'], ['api-docs', 'Documents an HTTP API from its routes'],
      ['test-plan', 'Drafts a test plan from a feature spec'], ['sql-review', 'Reviews SQL migrations for locking and data loss'], ['scratch', '', 'SKILL.md has no description']],
    plug: [['northwind-agents', 'Support triage and reply drafting · 3 skills'], ['northwind-data', 'Weekly metrics and dashboards · 2 skills'], ['old-tools', '', 'No .claude-plugin\\plugin.json or .codex-plugin\\plugin.json']],
    tool: [['magick', 'ImageMagick 7.1 · provides the magick command']],
  };
  const OWN = [];
  Object.keys(FOUND).forEach((sec) => FOUND[sec].forEach((f) => {
    const id = 'u-' + sec + '-' + f[0];
    f.id = id;
    if (f[2]) return;
    const it = { id, sec, own: true, name: sec === 'tool' ? 'ImageMagick' : f[0], sum: f[1], gl: sec === 'tool' ? 'img' : sec === 'plug' ? 'plug' : 'spark', mb: 0.02, ver: sec === 'tool' ? 'provides magick' : sec === 'plug' ? 'your plugin' : '1 skill',
      src: sec === 'tool' ? 'C:\\Tools\\ImageMagick' : sec === 'plug' ? 'github.com/northwind/agent-plugins' : 'github.com/northwind/team-skills', lic: 'Yours', srcFull: '', needs: 'Whatever its own files say' };
    it.srcFull = it.src + (sec === 'tool' ? ', magick.exe' : ', copied into nth’s folder');
    if (sec === 'skill') it.sk = [{ n: f[0], on: true, t: tok(f[0]) }];
    if (sec === 'plug') it.sk = PL(f[0] + ':' + (f[0] === 'northwind-agents' ? 3 : 2));
    OWN.push(it);
  }));
  const ALL = ITEMS.concat(OWN);
  OWN.forEach((i) => { i.disk = i.mb; IT[i.id] = i; });
  const ownOf = (sec) => OWN.filter((i) => i.sec === sec);

  // ------------------------------------------------------------------ the owner's picks (OD-0099, 2026-10-04)
  // Separate Plugins, Skills and Tools pages in layout D, each with its own Download all (the confirmation of "Download all
  // A"), no Download everything; Minimize A (the sidebar pill and its popover, OD-0100 place D); Invoke A. Custom C (OD-0099,
  // owner, 2026-10-04): one "Add" button with a chevron in each page's title row (Plugins, Skills, Tools), drawn in the
  // page's own glass button style; its menu (From a folder…, From a .zip…, From GitHub…) opens the add sheet. Custom A (a
  // button under the list) and B (a drop target) were not picked and are gone. All the picks are made.
  const st = {};
  ALL.forEach((i) => { st[i.id] = { s: i.own ? 'in' : 'av', p: 0 }; });
  const en = {};
  ALL.forEach((i) => (i.sk || []).forEach((s) => { en[i.id + ':' + s.n] = s.on; }));
  // GitHub is the one source typed (a link); a folder and a .zip are chosen with Browse… (owner amendment, OD-0099, 2026-10-04)
  const GH = { skill: 'northwind/team-skills', plug: 'northwind/agent-plugins', tool: 'ImageMagick/ImageMagick' };
  const GHRE = /^[\w.-]+\/[\w.-]+(\/\S+)?$/;
  const mkView = (sec, sel) => ({ sec, sel, last: { plug: 'akw', skill: 'odoc', tool: 'lo' }, own: {}, gh: Object.assign({}, GH), cu: { s: 'closed', src: 'folder', pick: null, pk: false, hl: -1, rows: {} } });
  const VIEWS = { S: mkView('skill', 'odoc'), M: mkView('tool', 'lo'), U: mkView('skill', 'odoc') };
  const pop = {};
  const bulk = { ph: 'none', scope: 'tool', items: [], skip: [], min: false, fbOpen: null };
  const chat = { s: 'model' };
  const stg = { m: 'set' };
  let scenario = 'browse', scope = 'tool';
  const keyOf = (el) => { const w = el && el.closest && el.closest('[data-w]'); return w ? w.dataset.w : 'S'; };
  const vOf = (el) => VIEWS[keyOf(el)] || VIEWS.S;

  // ------------------------------------------------------------------ numbers
  const nf1 = (x) => x.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  const fmtMb = (mb) => (mb >= 1000 ? (mb / 1000).toFixed(2) + ' GB' : mb < 1 ? mb.toFixed(2) + ' MB' : mb < 10 ? mb.toFixed(1) + ' MB' : Math.round(mb) + ' MB');
  const fmtExact = (mb) => (mb >= 1000 ? (mb / 1000).toFixed(2) + ' GB (' + nf1(mb) + ' MB)' : mb < 1 ? mb.toFixed(2) + ' MB' : nf1(mb) + ' MB');
  const fmtDisk = (mb) => 'about ' + (mb >= 1000 ? (mb / 1000).toFixed(1) + ' GB' : fmtMb(mb));
  const tokS = (n) => n.toLocaleString('en-US');
  const inl = (id) => ['in', 'rm', 'del'].includes(st[id].s);
  const busy = (id) => ['q', 'dl', 'ck'].includes(st[id].s);
  const list = (a) => (a.length < 2 ? a.join('') : a.slice(0, -1).join(', ') + ' and ' + a[a.length - 1]);
  const BIG = 100; // MB: above this the confirmation shows the large-download warning (owner, 2026-10-04)
  function listedOf(it, v) {
    if (!it.sk || !inl(it.id) || (it.own && !(v && v.own[it.id]))) return { n: 0, t: 0 };
    let n = 0, t = 0;
    it.sk.forEach((s) => { if (en[it.id + ':' + s.n]) { n += s.pl ? s.c : 1; t += s.t; } });
    return { n, t };
  }
  function listedAll(v) { let n = 0, t = 0; ALL.forEach((i) => { const l = listedOf(i, v); n += l.n; t += l.t; }); return { n, t }; }
  function defOf(it) { let n = 0, t = 0; (it.sk || []).forEach((s) => { if (s.on) { n += 1; t += s.t; } }); return { n, t }; }

  // ------------------------------------------------------------------ Download all: what is in scope (one page at a time)
  const bySecAll = (s) => ITEMS.filter((i) => i.sec === s);
  const scopeItems = () => bySecAll(bulk.scope);
  const want = () => bulk.items.filter((id) => !bulk.skip.includes(id));
  function totals() {
    const ids = bulk.ph === 'none' || bulk.ph === 'confirm' ? scopeItems().filter((i) => !inl(i.id)).map((i) => i.id) : want();
    const mb = ids.reduce((s, id) => s + IT[id].mb, 0), disk = ids.reduce((s, id) => s + IT[id].disk, 0);
    const hosts = Array.from(new Set(ids.map((id) => IT[id].host)));
    return { ids, mb, disk, hosts };
  }
  const sectionRemain = (sec) => bySecAll(sec).filter((i) => !inl(i.id));
  const fails = () => want().filter((id) => st[id].s === 'fail');
  function doneMb() { let d = 0; want().forEach((id) => { const s = st[id].s; d += s === 'in' || s === 'ck' ? IT[id].mb : s === 'dl' || s === 'fail' ? st[id].p * IT[id].mb : 0; }); return d; }

  // ------------------------------------------------------------------ the add sheet's texts
  const KIND = { skill: 'skill', plug: 'plugin', tool: 'tool' };
  const KINDS = { skill: 'skills', plug: 'plugins', tool: 'tools' };
  // what the stand-in for the Windows picker lists (illustrative): [name, full path, date modified, usable?]
  const PK = {
    skill: { folder: { crumb: 'This PC \u203a Data (D:)', items: [['Notes', 'D:\\Notes', '12/09/2026 16:40', false], ['team-skills', 'D:\\team-skills', '30/09/2026 09:12', true]] },
      zip: { crumb: 'This PC \u203a Downloads', items: [['holiday-photos.zip', 'D:\\Downloads\\holiday-photos.zip', '02/10/2026 18:05', false], ['team-skills.zip', 'D:\\Downloads\\team-skills.zip', '03/10/2026 11:22', true]] } },
    plug: { folder: { crumb: 'This PC \u203a Data (D:)', items: [['Photos', 'D:\\Photos', '21/08/2026 20:15', false], ['agent-plugins', 'D:\\agent-plugins', '01/10/2026 14:03', true]] },
      zip: { crumb: 'This PC \u203a Downloads', items: [['holiday-photos.zip', 'D:\\Downloads\\holiday-photos.zip', '02/10/2026 18:05', false], ['agent-plugins.zip', 'D:\\Downloads\\agent-plugins.zip', '03/10/2026 11:30', true]] } },
    tool: { folder: { crumb: 'This PC \u203a Windows (C:) \u203a Tools', items: [['Docs', 'C:\\Tools\\Docs', '05/06/2026 10:00', false], ['ImageMagick', 'C:\\Tools\\ImageMagick', '28/09/2026 08:47', true]] },
      zip: { crumb: 'This PC \u203a Downloads', items: [['holiday-photos.zip', 'D:\\Downloads\\holiday-photos.zip', '02/10/2026 18:05', false], ['ImageMagick-portable.zip', 'D:\\Downloads\\ImageMagick-portable.zip', '03/10/2026 11:41', true]] } },
  };
  const NONE = { folder: 'No folder chosen', zip: 'No .zip chosen' };
  const NOPE = {
    folder: { skill: 'No SKILL.md found in this folder. Pick a skill\u2019s own folder, or a folder of skill folders.', plug: 'No plugin manifest found in this folder (.claude-plugin\\plugin.json or .codex-plugin\\plugin.json).', tool: 'No .exe found in this folder.' },
    zip: { skill: 'No SKILL.md found in this .zip. Pick a .zip with a skill\u2019s own folder, or a folder of skill folders.', plug: 'No plugin manifest found in this .zip (.claude-plugin\\plugin.json or .codex-plugin\\plugin.json).', tool: 'No .exe found in this .zip.' },
  };
  const NEED = { skill: 'Each skill needs a SKILL.md with a name and a description.', plug: 'Each plugin needs its manifest: .claude-plugin\\plugin.json or .codex-plugin\\plugin.json.', tool: 'A tool needs an .exe, a name and the command it provides.' };
  const cuRows = (v) => FOUND[v.sec].filter((f) => !f[2]);
  const ticked = (v) => cuRows(v).filter((f) => (v.cu.rows[f.id] || {}).tick);
  const local = (v) => v.cu.src !== 'gh';
  const canCheck = (v) => (local(v) ? !!v.cu.pick : GHRE.test(v.gh[v.sec] || ''));
  const pkList = (v) => (local(v) && PK[v.sec] ? PK[v.sec][v.cu.src] : null);

  // ------------------------------------------------------------------ texts (words S1, Plain)
  function TX(k, v) {
    const [a, b] = k.split(':');
    if (a === '@ptitle') return SECS[v.sec];
    if (a === 'b') {
      const T = totals(), what = 'all ' + SECL[bulk.scope];
      const F = fails(), doneN = want().filter((id) => inl(id)).length;
      if (b === 'title') return { confirm: 'Download ' + what + '?', dl: 'Downloading ' + what, paused: 'Paused', done: 'Downloaded', partial: doneN + ' of ' + want().length + ' downloaded', none: '' }[bulk.ph];
      if (b === 'sub') return { confirm: T.ids.length + (T.ids.length === 1 ? ' item' : ' items') + ' from ' + list(T.hosts.length > 3 ? T.hosts.slice(0, 3).concat([(T.hosts.length - 3) + ' more']) : T.hosts) + '.', dl: 'One at a time. Hide it and keep using nth.', paused: 'Nothing is downloading. Resume carries on where it stopped.', done: 'All ' + want().length + ' are on this PC: ' + fmtDisk(T.disk) + '.', partial: F.length + ' didn’t download. The rest are on this PC.', none: '' }[bulk.ph];
      if (b === 'disc') return 'This can be a large download: ' + fmtExact(T.mb) + ', and it takes ' + fmtDisk(T.disk) + ' of disk space once installed.';
      if (b === 'small') return T.ids.length ? 'About ' + fmtMb(T.mb) + ' to download, ' + fmtDisk(T.disk) + ' on disk.' : 'Everything on this page is already here.';
      if (b === 'foot') return { confirm: 'Nothing downloads until you press Continue.', dl: 'Cancel stops the rest. What has already downloaded stays.', paused: 'Cancel stops the rest. What has already downloaded stays.', done: bulk.scope === 'tool' ? 'Tools are used when a step needs them.' : 'New skills start as listed in each detail; switch them there.', partial: 'Retry tries the failed ones again. Or download them yourself.', none: '' }[bulk.ph];
      if (b === 'retry') return 'Retry ' + F.length;
    }
    if (a === 'm') {
      if (b === 'title') return { dl: 'Downloading all ' + SECL[bulk.scope], paused: 'Paused', done: 'All ' + SECL[bulk.scope] + ' downloaded', partial: fails().length + ' didn’t download', confirm: '', none: '' }[bulk.ph];
    }
    if (a === 'cost') {
      const it = IT[b];
      if (!inl(b)) return 'Not listed: not downloaded';
      const l = listedOf(it, it.own ? Object.assign({}, v, { own: { [b]: true } }) : v);
      return (it.sec === 'plug' ? l.n + ' skills listed' : l.n + ' of ' + it.sk.length + (it.more ? '+' + it.more : '') + ' listed') + ' · about ' + tokS(l.t) + ' tokens';
    }
    if (a === 'costn') {
      const it = IT[b];
      if (!inl(b)) { const d = defOf(it); return it.sec === 'plug' ? 'Once downloaded, its plugins start switched off, so nothing is listed until you switch one on.' : 'Once downloaded, ' + d.n + ' start switched on: about ' + tokS(d.t) + ' tokens in every session.'; }
      return 'Every session lists each switched-on skill’s name and description, so the model knows it exists and calls it when a task needs it. Its full instructions load only then. Switches apply to new chats.';
    }
    if (a === 'cu') {
      const c = v.cu, sec = v.sec;
      if (b === 'title') return 'Add ' + KIND[sec] + 's from ' + { folder: 'a folder', zip: 'a .zip', gh: 'GitHub' }[c.src];
      if (b === 'chk') return { folder: 'Looking in the chosen folder for ', zip: 'Opening the chosen .zip and looking for ', gh: 'Fetching the repository and looking for ' }[c.src] + { skill: 'SKILL.md files', plug: 'plugin manifests', tool: 'an .exe' }[sec] + '…';
      if (b === 'flabel') return { folder: 'Folder on this PC', zip: '.zip file on this PC', gh: 'GitHub link (owner/repo, optional path)' }[c.src];
      if (b === 'path') return c.src === 'gh' ? '' : c.pick ? c.pick.p : NONE[c.src];
      if (b === 'ptitle') return c.src === 'zip' ? 'Open' : 'Select folder';
      if (b === 'pbtn') return c.src === 'zip' ? 'Open' : 'Select Folder';
      if (b === 'plab') return c.src === 'zip' ? 'File name:' : 'Folder:';
      if (b === 'pname') { const L = pkList(v); return L && c.hl >= 0 ? L.items[c.hl][0] : ''; }
      if (b === 'pcrumb') { const L = pkList(v); return L ? L.crumb : ''; }
      if (b === 'pfilt') return c.src === 'zip' ? 'Zip files (*.zip)' : 'Folders';
      if (b === 'need') return NEED[sec];
      if (b === 'fail') return NOPE[c.src === 'zip' ? 'zip' : 'folder'][sec];
      if (b === 'found') { const r = FOUND[sec], ok = r.filter((f) => !f[2]).length, bad = r.length - ok; return 'Found ' + ok + ' ' + (ok === 1 ? KIND[sec] : KINDS[sec]) + (bad ? ' and ' + bad + ' that can’t be added' : '') + '.'; }
      if (b === 'add') { const n = ticked(v).length; return 'Add ' + n + ' ' + (n === 1 ? KIND[sec] : KINDS[sec]); }
      if (b === 'foot') {
        const rs = cuRows(v).filter((f) => (c.rows[f.id] || {}).go), done = rs.filter((f) => c.rows[f.id].s === 'in').length, bad = rs.filter((f) => c.rows[f.id].s === 'fail').length;
        if (c.s === 'chk') return 'Checking what is there.';
        if (c.s === 'list') return 'Ticked ones are added side by side.';
        if (c.s === 'inst') return done + ' of ' + rs.length + ' added' + (bad ? ' · ' + bad + ' didn’t' : '');
        if (c.s === 'done') return done + ' of ' + rs.length + ' added' + (bad ? ' · ' + bad + ' didn’t' : '') + '. ' + (done ? 'The added ones are under Your own.' : 'Retry tries the failed ones again.');
        if (c.s === 'fail') return 'Nothing was added.';
        if (local(v) && !c.pick) return 'Choose it with Browse\u2026, then press Check.';
        return 'Nothing is added until you check it and press Add.';
      }
    }
    return '';
  }
  function LV(k, v) {
    const [a, b, c] = k.split(':');
    if (IT[a]) { const it = IT[a]; return Math.round(st[a].p * 100) + '% · ' + Math.round(st[a].p * it.mb) + ' of ' + fmtMb(it.mb); }
    if (a === 'pc') return Math.round(st[b].p * 100) + '%';
    if (a === 'listed') { if (v.sec === 'tool') return 'Tools aren’t listed to the model'; const L = listedAll(v); return 'Listed in every session: ' + L.n + ' skills · about ' + tokS(L.t) + ' tokens'; }
    if (a === 'sdlb') { const r = sectionRemain(b); return r.length ? 'Download all ' + SECL[b] + ' · ' + fmtMb(r.reduce((s, i) => s + i.mb, 0)) : 'All ' + SECL[b] + ' are here'; }
    if (a === 'b') {
      const T = totals();
      if (b === 'dl') return fmtExact(T.mb);
      if (b === 'disk') return fmtDisk(T.disk);
      if (b === 'hosts') return T.hosts.join(', ') || '–';
      if (b === 'inc') { const n = (bulk.ph === 'confirm' ? scopeItems().map((i) => i.id) : bulk.items).length, k2 = (bulk.ph === 'confirm' ? scopeItems().filter((i) => inl(i.id)) : bulk.skip).length; return 'Included · ' + n + (k2 ? ' · ' + k2 + ' already here, skipped' : ''); }
      if (b === 'prog') {
        const w = want(), d = doneMb(), pct = T.mb ? Math.floor((d / T.mb) * 100) : 0, i = w.filter((id) => ['in', 'ck'].includes(st[id].s)).length;
        return (bulk.ph === 'paused' ? 'Paused · ' : '') + pct + '% · ' + (T.mb >= 10 ? Math.round(d) + ' MB' : d.toFixed(1) + ' MB') + ' of ' + fmtMb(T.mb) + ' · ' + i + ' of ' + w.length + ' done';
      }
    }
    if (a === 'm') {
      const w = want(), i = w.filter((id) => ['in', 'ck'].includes(st[id].s)).length;
      if (b === 'pill') return { dl: 'Downloading ' + Math.min(w.length, i + 1) + ' of ' + w.length, paused: 'Paused · ' + i + ' of ' + w.length, done: 'Downloaded', partial: fails().length + ' didn’t download', confirm: '', none: '' }[bulk.ph];
      if (b === 'cur') { const id = w.find((x) => ['dl', 'ck'].includes(st[x].s)) || w.find((x) => st[x].s === 'q'); return id ? IT[id].name + ' · ' + Math.round(st[id].p * 100) + '%' : ''; }
    }
    if (a === 'tk') return '~' + c + ' tokens';
    return '';
  }

  // text: crossfade, while the box eases its width (inline) or height (block) to the new text
  function xt(el, text) {
    text = text == null ? '' : String(text);
    const blk = el.classList.contains('xtb');
    if (!el._x) { el._x = 1; el.innerHTML = '<span class="on"></span><span></span>'; el.firstChild.textContent = text; el._on = el.firstChild; return; }
    const on = el._on;
    if (on.textContent === text) return;
    const off = on === el.firstChild ? el.lastChild : el.firstChild;
    const prop = blk ? 'height' : 'width';
    const s0 = blk ? el.offsetHeight : el.offsetWidth;
    off.textContent = text;
    on.classList.remove('on'); off.classList.add('on'); el._on = off;
    const s1 = blk ? off.offsetHeight : off.offsetWidth;
    clearTimeout(el._t);
    if (!reduce && s0 !== s1 && el.offsetParent) { el.style[prop] = s0 + 'px'; void el.offsetWidth; el.style[prop] = s1 + 'px'; }
    el._t = setTimeout(() => { if (el._on === off) on.textContent = ''; el.style.width = ''; el.style.height = ''; }, 380);
  }
  const X = (k, cls) => '<span class="xt' + (cls ? ' ' + cls : '') + '" data-t="' + k + '"></span>';
  const L = (k, cls) => '<span class="lv' + (cls ? ' ' + cls : '') + '" data-lv="' + k + '"></span>';

  // ------------------------------------------------------------------ the pill and its popover (Minimize A, OD-0100 place D)
  const PAUSEG = '<svg class="pz" viewBox="0 0 12 12"><path d="M4 2.5v7M8 2.5v7"/></svg>';
  const PLAYG = '<svg class="pz f" viewBox="0 0 12 12"><path d="M3.5 2.2 9.8 6 3.5 9.8Z"/></svg>';
  const bgroups = (hide) => '<span class="bbtn">' +
    '<span data-bp="confirm"><span class="gbtn" data-act="bx:cancel">Cancel</span><span class="gbtn pri" data-act="bx:go">Continue</span></span>' +
    '<span data-bp="dl">' + (hide ? '<span class="gbtn" data-act="bx:hide">Hide</span>' : '') + '<span class="gbtn" data-act="bx:stop">Cancel</span><span class="gbtn acc" data-act="bx:pause">' + PAUSEG + 'Pause</span></span>' +
    '<span data-bp="paused">' + (hide ? '<span class="gbtn" data-act="bx:hide">Hide</span>' : '') + '<span class="gbtn" data-act="bx:stop">Cancel</span><span class="gbtn pri" data-act="bx:resume">' + PLAYG + 'Resume</span></span>' +
    '<span data-bp="done"><span class="gbtn pri" data-act="bx:close">Close</span></span>' +
    '<span data-bp="partial"><span class="gbtn" data-act="bx:close">Close</span><span class="gbtn pri" data-act="bx:retry">' + X('b:retry') + '</span></span></span>';
  function pillParts() {
    const fl = ITEMS.filter((i) => i.sec === 'tool' || true).map((i) => '<div class="fold" data-f="mf:' + i.id + '"><div><div class="mfr">' + WRN + '<b>' + esc(i.name) + '</b><span>' + (i.fw === 'sum' ? 'didn’t match its checksum' : 'couldn’t reach ' + esc(i.host)) + '</span></div></div></div>').join('');
    return '<span class="mpill glass" data-act="m:pop" role="button"><span class="lead"><span class="lr">' + RING + '</span><span class="lp2">' + PAUSEG + '</span><span class="lk">' + OKC + '</span><span class="lw">' + WRN + '</span></span>' + L('m:pill') + '</span>' +
      '<div class="mpop glass"><div class="mph"><b>' + X('m:title') + '</b><span class="exp" data-act="m:full" role="button" title="Open in Settings"><svg viewBox="0 0 12 12"><path d="M7 2h3v3M10 2 6 6M5 3H3v6h6V7"/></svg></span></div>' +
      '<div class="fold" data-f="bph:dl,paused"><div><div class="ovr"><span class="bar big2"><i></i></span>' + L('b:prog', 'tnum') + '</div><div class="mcur"><span class="mr">' + RING + '</span>' + L('m:cur') + '</div></div></div>' +
      '<div class="fold" data-f="bph:done"><div><div class="mdone">' + OKC + X('b:sub') + '</div></div></div>' +
      '<div class="fold" data-f="bph:partial"><div><div class="mfl">' + fl + '</div></div></div>' +
      '<div class="mft">' + bgroups(false) + '</div></div>';
  }

  // ------------------------------------------------------------------ the Settings window
  const NAV = ['general', 'hydra', 'appearance', 'custom', 'voice', 'providers', 'tools', 'mcp', 'plugins', 'skills', 'models', 'source', 'shortcuts', 'archive', 'about'];
  const NAVN = { general: 'General', hydra: 'Hydra', appearance: 'Appearance', custom: 'Customization', voice: 'Voice typing', providers: 'Providers', tools: 'Tools', mcp: 'MCP', plugins: 'Plugins', skills: 'Skills', models: 'Models', source: 'Source control', shortcuts: 'Shortcuts', archive: 'Archive', about: 'About' };
  const NAVSEC = { plugins: 'plug', skills: 'skill', tools: 'tool' };
  const side = () => '<div class="side"><div class="srch"><svg viewBox="0 0 16 16"><circle cx="7" cy="7" r="4.6"/><path d="m10.4 10.4 3.4 3.4"/></svg>Search</div><div class="nav">' +
    NAV.map((k) => '<span class="ni" ' + (NAVSEC[k] ? 'data-nsec="' + NAVSEC[k] + '" data-act="sec:' + NAVSEC[k] + '"' : '') + '>' + ic(k) + '<span>' + esc(NAVN[k]) + '</span></span>').join('') + '</div></div>';
  function acs(it) {
    const id = it.id;
    return '<span class="acs">' +
      '<span data-s="av"><span class="gbtn pri big" data-act="dl:' + id + '">Download&nbsp;· ' + fmtMb(it.mb) + '</span></span>' +
      '<span data-s="q"><span class="sm">Waiting</span>' + XB('cancel:' + id) + '</span>' +
      '<span data-s="dl">' + BAR + L(id, 'tnum') + XB('cancel:' + id) + '</span>' +
      '<span data-s="ck">' + SPN + '<span class="sm">Checking…</span></span>' +
      '<span data-s="in">' + OKC + '<span class="sm">' + (it.own ? 'Added' : 'Installed') + '</span>' + TRASH('rm:' + id) + '</span>' +
      '<span data-s="fail">' + WRN + '<span class="wk">Didn’t download</span><span class="gbtn acc" data-act="dl:' + id + '">Retry</span></span>' +
      '<span data-s="rm"><span class="gbtn red" data-act="rmok:' + id + '">Remove</span>' + XB('rmx:' + id) + '</span>' +
      '<span data-s="del">' + SPN + '<span class="sm">Removing…</span></span></span>';
  }
  const cacs = (it) => '<span class="acs"><span data-s="av"><span class="sm">' + fmtMb(it.mb) + '</span></span><span data-s="q"><span class="sm">Waiting</span></span><span data-s="dl">' + RING + L('pc:' + it.id, 'tnum') + '</span>' +
    '<span data-s="ck">' + SPN + '</span><span data-s="in">' + OKC + '</span><span data-s="fail">' + WRN + '</span><span data-s="rm">' + OKC + '</span><span data-s="del">' + SPN + '</span></span>';
  const li = (it) => '<div class="li" data-li="' + it.id + '" data-act="sel:' + it.id + '"><span data-it="' + it.id + '" class="lir">' + g(it.gl, it.sec !== 'tool' ? 'set' : '') + '<b>' + esc(it.name) + '</b>' + cacs(it) + '</span></div>';
  function costBox(it) {
    if (it.sec === 'tool') return '<div class="cost t"><div class="ch"><b>Not listed to the model</b></div><div class="cn">Tools add nothing to a session. They sit on every chat’s PATH, and a command finds them when a step needs them.</div></div>';
    const rows = it.sk.map((s) => '<div class="sr" data-sk="' + it.id + ':' + s.n + '"><span class="tg" data-act="en:' + it.id + ':' + s.n + '" role="switch"><i></i></span><code>' + (it.sec === 'plug' ? esc(s.n) : '/' + esc(s.n)) + '</code>' +
      (s.pl ? '<span class="d">' + s.c + (s.c === 1 ? ' skill' : ' skills') + '</span>' : '<span class="d"></span>') + '<span class="tk">' + L('tk:' + it.id + ':' + s.t) + '</span></div>').join('') +
      (it.more ? '<div class="sr more"><span class="d">and ' + it.more + ' more, each with its switch</span></div>' : '');
    return '<div class="cost"><div class="ch"><b>In every session</b>' + X('cost:' + it.id) + '</div><div class="cl">' + rows + '</div><div class="cn">' + X('costn:' + it.id, 'xtb') + '</div></div>';
  }
  // the manual fallback for an item that keeps failing (owner, 2026-10-04)
  function fbBlock(id) {
    const f = FB[id], it = IT[id];
    return '<div class="fb"><div class="fbh">Download it yourself</div>' +
      '<div class="fbs"><span class="n">1</span><span class="t">Get <code>' + esc(f[0]) + '</code><i>' + esc(f[1]) + '</i></span><span class="gbtn" data-act="fb:page:' + id + '">Open download page ↗</span></div>' +
      '<div class="fbs"><span class="n">2</span><span class="t">Put it in <code>' + esc(MANUAL) + '</code><span class="tag">ED</span></span><span class="gbtn" data-act="fb:folder:' + id + '">Open folder</span></div>' +
      '<div class="fbs"><span class="n">3</span><span class="t">' + (it.sec === 'tool' ? 'nth checks its SHA-256 against the pinned one and installs it.' : 'nth checks every file against its pinned hash and installs it.') + '</span>' +
      '<span class="acs" data-it="' + id + '"><span data-s="fail av q"><span class="gbtn acc" data-act="fb:check:' + id + '">Check again</span></span><span data-s="ck dl">' + SPN + '<span class="sm">Checking…</span></span><span data-s="in">' + OKC + '<span class="sm">Installed</span></span></span></div></div>';
  }
  const whyFail = (id) => '<div class="fold" data-f="fail:' + id + '"><div><div class="why w">' + WRN + '<span class="ft">' + (IT[id].fw === 'sum' ? 'The file didn’t match its checksum. Retry downloads it again.' : 'Couldn’t reach ' + esc(IT[id].host) + '. Retry picks up where it stopped.') + '</span></div>' + fbBlock(id) + '</div></div>';
  const whyRm = (id) => '<div class="fold" data-f="rm:' + id + '"><div><div class="why r"><span class="ft">' + (IT[id].own ? 'Removes it from nth’s folder. Your original stays where it was.' : IT[id].sec === 'tool' ? 'Skills that run it will ask for it again in a chat.' : 'Its skills stop being listed in new chats.') + '</span></div></div></div>';
  function dpane(it) {
    const k1 = it.sec === 'tool' ? (it.own ? 'Provides' : 'Used by') : 'Needs', v1 = it.own && it.sec === 'tool' ? 'magick' : it.sec === 'tool' ? USED[it.id] : (it.needs || 'Varies per plugin; connectors are named in the text');
    return '<div class="dp" data-dp="' + it.id + '"><div data-it="' + it.id + '" class="dpi">' +
      '<div class="dh">' + g(it.gl, it.sec !== 'tool' ? 'set' : '') + '<span class="tx"><b>' + esc(it.name) + '</b><span>' + esc(it.sum) + '</span><span>' + esc(it.own ? 'Your own · ' + it.ver + ' · ' + it.src : it.ver + ' · ' + fmtMb(it.mb) + ' · ' + it.src + ' · ' + it.lic) + '</span></span></div>' +
      '<div class="act">' + acs(it) + '</div>' + (it.own ? '' : whyFail(it.id)) + whyRm(it.id) + costBox(it) +
      '<div class="det"><span class="k">' + k1 + '</span><span>' + esc(v1) + '</span><span class="k">Source</span><span>' + esc(it.srcFull) + '</span><span class="k">Licence</span><span>' + esc(it.lic) + '</span></div></div></div>';
  }
  function incRow(it) {
    return '<div class="fold" data-f="inc:' + it.id + '"><div><div class="ir2" data-it="' + it.id + '">' + g(it.gl, it.sec !== 'tool' ? 'set' : '') + '<span class="nm2">' + esc(it.name) + '</span>' +
      '<span class="acs"><span data-s="av"><span class="sm">' + fmtMb(it.mb) + '</span></span><span data-s="q"><span class="sm">Waiting · ' + fmtMb(it.mb) + '</span></span><span data-s="dl">' + RING + L('pc:' + it.id, 'tnum') + '</span>' +
      '<span data-s="ck">' + SPN + '<span class="sm">Checking…</span></span><span data-s="in"><span class="dn">' + OKC + '<span class="sm">Downloaded</span></span><span class="ah sm">Already here</span></span>' +
      '<span data-s="fail">' + WRN + '<span class="wk">Didn’t download</span></span><span data-s="rm">' + OKC + '</span><span data-s="del">' + SPN + '</span></span></div></div></div>';
  }
  const pfCard = (it) => '<div class="fold" data-f="pf:' + it.id + '"><div><div class="pfc" data-it="' + it.id + '"><div class="pfh">' + WRN + '<span class="ft"><b>' + esc(it.name) + ':</b> ' +
    (it.fw === 'sum' ? 'the file didn’t match its checksum, so it wasn’t used.' : 'couldn’t reach ' + esc(it.host) + '.') + '</span><span class="lnk" data-act="fbo:' + it.id + '">Download it yourself</span></div>' +
    '<div class="fold" data-f="fbo:' + it.id + '"><div>' + fbBlock(it.id) + '</div></div></div></div></div>';
  function bulkBody() {
    return '<div class="bk"><div class="bh"><span class="bic">' + sv16('dl') + '</span><span class="tx"><b>' + X('b:title') + '</b>' + X('b:sub', 'xtb') + '</span></div>' +
      '<div class="fold" data-f="bph:confirm"><div><div class="bconf">' +
      '<div class="fold" data-f="big"><div><div class="disc">' + WRN + X('b:disc', 'xtb') + '</div></div></div>' +
      '<div class="fold" data-f="small"><div><div class="plain">' + X('b:small', 'xtb') + '</div></div></div>' +
      '<div class="facts"><span class="k">Download</span>' + L('b:dl') + '<span class="k">On disk</span>' + L('b:disk') + '<span class="k">Free on C:</span><span>212 GB</span><span class="k">From</span>' + L('b:hosts') + '</div></div></div></div>' +
      '<div class="fold" data-f="bph:dl,paused"><div><div class="ovr"><span class="bar big2"><i></i></span>' + L('b:prog', 'tnum') + '</div></div></div>' +
      '<div class="fold" data-f="bph:partial"><div><div class="pfl">' + ITEMS.map(pfCard).join('') + '</div></div></div>' +
      '<div class="inc"><div class="ih">' + L('b:inc') + '</div><div class="il">' + ITEMS.map(incRow).join('') + '</div></div>' +
      '<div class="bf"><span class="bnote">' + X('b:foot', 'xtb') + '</span>' + bgroups(true) + '</div></div>';
  }
  // the add sheet (Custom C, picked): the source the menu chose, the path, the validation, what was found (a ticked list
  // with Select all), parallel installs, each row's ring turning into the green check
  const CKB = '<i class="cb"><svg viewBox="0 0 12 12"><path d="m2.5 6.3 2.4 2.3 4.6-5"/></svg></i>';
  function sheet() {
    const rows = (sec) => '<div class="fold" data-f="cusec:' + sec + '"><div><div class="cul">' +
      '<div class="cur all" data-act="cu:all" data-cuall="' + sec + '">' + CKB + '<b>Select all</b></div>' +
      FOUND[sec].map((f) => '<div class="cur' + (f[2] ? ' bad' : '') + '" data-ci="' + f.id + '" ' + (f[2] ? '' : 'data-act="cu:tick:' + f.id + '"') + '>' + CKB + '<span class="cn2"><b>' + esc(f[0]) + '</b><i>' + esc(f[2] || f[1]) + '</i></span>' +
        '<span class="cst"><span data-c="av"></span><span data-c="dl">' + RING + '</span><span data-c="in">' + OKC + '</span><span data-c="fail">' + WRN + '<span class="gbtn acc" data-act="cu:retry:' + f.id + '">Retry</span></span><span data-c="bad"><span class="sm">Can’t be added</span></span></span></div>').join('') + '</div></div></div>';
    return '<div class="cscrim" data-act="cu:scrim"></div><div class="sheet glass" role="dialog" aria-label="Add"><div class="bh"><span class="bic">' + sv16('plus') + '</span><span class="tx"><b>' + X('cu:title') + '</b><span class="sb">Choose a folder or a .zip with Browse\u2026, or give a GitHub link.</span></span></div>' +
      '<div class="srcs"><span data-src="folder" data-act="cu:src:folder" role="button" tabindex="0">' + sv16('folder', 'ic14') + 'Folder</span><span data-src="zip" data-act="cu:src:zip" role="button" tabindex="0">' + sv16('zip', 'ic14') + '.zip</span><span data-src="gh" data-act="cu:src:gh" role="button" tabindex="0">' + sv16('link', 'ic14') + 'GitHub</span></div>' +
      '<div class="fld"><span class="fl">' + X('cu:flabel') + '</span><div class="frow"><div class="fvs"><span class="fv ro" data-ro="1">' + X('cu:path') + '</span><input class="fv ghin" type="text" spellcheck="false" autocomplete="off" aria-label="GitHub link" placeholder="owner/repo"></div>' +
      '<span class="brw"><span><span class="gbtn" data-act="cu:browse" role="button" tabindex="0">' + sv16('folder', 'ic14') + 'Browse\u2026</span></span></span></div></div>' +
      '<div class="fold" data-f="cutool"><div><div class="fld2"><span class="fl">Name</span><span class="fv">ImageMagick</span><span class="fl">Command it provides</span><span class="fv"><code>magick</code></span></div></div></div>' +
      '<div class="need">' + X('cu:need', 'xtb') + '</div>' +
      '<div class="fold" data-f="cus:chk"><div><div class="chkr">' + SPN + X('cu:chk', 'xtb') + '</div></div></div>' +
      '<div class="fold" data-f="cus:fail"><div><div class="why w">' + WRN + '<span class="ft">' + X('cu:fail', 'xtb') + '</span></div></div></div>' +
      '<div class="fold" data-f="cus:list,inst,done"><div><div class="cfound">' + X('cu:found', 'xtb') + '</div>' + Object.keys(FOUND).map(rows).join('') + '</div></div>' +
      '<div class="bf"><span class="bnote">' + X('cu:foot', 'xtb') + '</span><span class="cbtn">' +
      '<span data-cb="open"><span class="gbtn" data-act="cu:close">Cancel</span><span class="gbtn pri" data-act="cu:check" role="button" tabindex="0">Check</span></span>' +
      '<span data-cb="chk"><span class="gbtn" data-act="cu:close">Cancel</span></span>' +
      '<span data-cb="fail"><span class="gbtn" data-act="cu:close">Cancel</span><span class="gbtn pri" data-act="cu:again">Choose another</span></span>' +
      '<span data-cb="list"><span class="gbtn" data-act="cu:close">Cancel</span><span class="gbtn pri" data-act="cu:add">' + X('cu:add') + '</span></span>' +
      '<span data-cb="inst"><span class="sm">Adding…</span></span>' +
      '<span data-cb="done"><span class="gbtn pri" data-act="cu:done">Done</span></span></span></div></div>' + sysPicker();
  }
  // the stand-in for the system's own dialog (a web page cannot open the real one; the app opens the Windows picker)
  function sysPicker() {
    const lists = Object.keys(PK).map((sec) => ['folder', 'zip'].map((src) => '<div class="fold" data-f="pkl:' + sec + '-' + src + '"><div>' +
      PK[sec][src].items.map((it, i) => '<div class="sdr" data-pi="' + i + '" data-act="cu:psel:' + i + '" role="option" tabindex="0">' + sv16(src === 'zip' ? 'zip' : 'folder', 'ic14') + '<span class="sn">' + esc(it[0]) + '</span><span class="sd">' + esc(it[2]) + '</span><span class="st">' + (src === 'zip' ? 'Compressed folder' : 'File folder') + '</span></div>').join('') + '</div></div>').join('')).join('');
    return '<div class="pscrim"></div><div class="sysdlg" role="dialog" aria-label="System dialog">' +
      '<div class="sdcap">System dialog: the Windows picker. Drawn here as a stand-in; the app opens the real one.</div>' +
      '<div class="sdf"><div class="sdt">' + sv16('folder', 'ic14') + '<b>' + X('cu:ptitle') + '</b><span class="sdx" data-act="cu:pcancel" role="button" aria-label="Close" tabindex="0"><svg viewBox="0 0 10 10"><path d="m1.5 1.5 7 7M8.5 1.5l-7 7"/></svg></span></div>' +
      '<div class="sda"><span class="nv">\u2190</span><span class="nv">\u2192</span><span class="nv">\u2191</span><span class="crumb">' + X('cu:pcrumb') + '</span></div>' +
      '<div class="sdb"><div class="sdn"><span class="g">Quick access</span><span>Desktop</span><span>Downloads</span><span class="g">This PC</span><span>Windows (C:)</span><span>Data (D:)</span></div>' +
      '<div class="sdl"><div class="sdh"><span>Name</span><span>Date modified</span><span>Type</span></div><div class="sdrows" role="listbox">' + lists + '</div></div></div>' +
      '<div class="sdfoot"><span class="lab">' + X('cu:plab') + '</span><span class="sdfv">' + X('cu:pname') + '</span><span class="sdfl">' + X('cu:pfilt') + '</span>' +
      '<span class="sbtn pri" data-act="cu:pok" role="button" tabindex="0">' + X('cu:pbtn') + '</span><span class="sbtn" data-act="cu:pcancel" role="button" tabindex="0">Cancel</span></div></div></div>';
  }
  function libSec(sec) {
    return '<div class="lp" data-lp="' + sec + '"><div class="lrows">' + bySecAll(sec).map(li).join('') +
      '<div class="fold" data-f="owng:' + sec + '"><div><div class="lg2">Your own</div></div></div>' + ownOf(sec).map((it) => '<div class="fold" data-f="own:' + it.id + '"><div>' + li(it) + '</div></div>').join('') + '</div>' +
      '<div class="lfoot"><span class="gbtn acc wide" data-act="all:' + sec + '" role="button" tabindex="0">' + sv16('dl', 'ic14') + L('sdlb:' + sec) + '</span></div></div>';
  }
  function buildSet(w) {
    // Custom C: one Add button with a chevron, in the page's own glass button style, at the right end of the title row
    const add = '<span class="gbtn add" data-act="cu:menu" role="button" tabindex="0" aria-haspopup="menu" aria-expanded="false"><span class="al">Add</span><svg class="chev" viewBox="0 0 12 12" aria-hidden="true"><path d="m3 4.6 3 3 3-3"/></svg></span>';
    const menu = '<div class="amenu glass" role="menu"><span data-act="cu:from:folder" role="menuitem" tabindex="0">' + sv16('folder', 'ic14') + 'From a folder…</span><span data-act="cu:from:zip" role="menuitem" tabindex="0">' + sv16('zip', 'ic14') + 'From a .zip…</span><span data-act="cu:from:gh" role="menuitem" tabindex="0">' + sv16('link', 'ic14') + 'From GitHub…</span></div>';
    return '<div class="mw set" data-w="' + w + '" data-sec="skill" data-bph="none">' + side() + '<div class="cnt">' +
      '<div class="ptitle"><span class="pt">' + X('@ptitle') + '</span>' + L('listed', 'lst') + '<span class="tact">' + add + menu + '</span></div>' +
      '<div class="lib"><div class="lps">' + Object.keys(SECS).map(libSec).join('') + '</div></div>' +
      '<div class="pane2"><div class="dps">' + ALL.map(dpane).join('') + '</div></div>' +
      '</div><div class="scrim"></div><div class="dlg glass">' + bulkBody() + '</div>' + sheet() + pillParts() + CAP3 + '</div>';
  }

  // ------------------------------------------------------------------ the chat windows
  const PICK = [['doc', 'Word documents', 'OpenAI document skills'], ['spreadsheet', 'Excel workbooks', 'OpenAI document skills'], ['slides', 'PowerPoint decks', 'OpenAI document skills'], ['pdf', 'PDF files', 'OpenAI document skills'], ['frontend-design', 'Web page design', 'Anthropic example skills'], ['skill-creator', 'Build a new skill', 'Anthropic example skills']];
  const picker = () => '<div class="skp glass"><div class="sph"><code>/do</code><span class="sm">typing filters the list</span></div>' +
    '<div class="spl"><div class="spg">Skills</div>' + PICK.map((p, i) => '<div class="spi' + (i === 0 ? ' hl' : '') + '" ' + (i === 0 ? 'data-act="c:choose"' : '') + '>' + sv16('spark', 'ic14') + '<code>/' + p[0] + '</code><span class="d">' + p[1] + '</span><span class="f">' + p[2] + '</span></div>').join('') +
    '<div class="spg">Plugins</div><div class="spi none"><span class="d">No plugin is switched on. Their skills show here once one is.</span></div></div>' +
    '<div class="spf">10 skills switched on · switched-off ones aren’t listed</div></div>';
  const SKROW = (who) => '<div class="skrow">' + sv16('spark', 'ic14') + '<span class="sl">Skill</span><code>doc</code><span class="sf">OpenAI document skills</span><span class="who">' + who + '</span></div>';
  const CK = '<span class="tk ok"><svg viewBox="0 0 12 12"><path d="m2.5 6.3 2.4 2.3 4.6-5"/></svg></span>';
  const mainSide = (title, threads) => '<div class="side"><div class="srch"><svg viewBox="0 0 16 16"><circle cx="7" cy="7" r="4.6"/><path d="m10.4 10.4 3.4 3.4"/></svg>Search</div>' +
    '<div class="proj"><span class="ph">northwind</span>' + threads.map((t, i) => '<span class="th' + (i === 0 ? ' on run' : '') + '"><i></i>' + esc(t) + '</span>').join('') + '</div><div class="foot">' + ic('gear') + 'Settings</div></div>';
  function buildChat() {
    const ans = (who) => SKROW(who) + '<div class="am"><p>I’ll build it with python-docx from your notes.</p></div><div class="trow">' + CK + '<span>Ran</span><code class="cm">python make_proposal.py notes.md proposal.docx</code></div>' +
      '<div class="am" style="margin-top:6px"><p>Done. <b>proposal.docx</b> is in the project folder: one page, four sections.</p></div>';
    return '<div class="mw main" data-w="C" data-cs="model">' + mainSide('', ['Proposal draft', 'Clean up the invoices sheet', 'Slides for Monday', 'Fix the footer dates']) +
      '<div class="pane"><div class="ttl">Proposal draft</div><div class="msgs">' +
      '<div class="fold" data-f="cs:sent,model"><div><div class="um"><span class="fold inl" data-f="cs:sent"><span><span class="uchip">' + sv16('spark', 'ic12') + '/doc</span></span></span>Draft a one-page proposal as proposal.docx from notes.md.</div></div></div>' +
      '<div class="fold d1" data-f="cs:model"><div>' + ans('Claude invoked it') + '</div></div>' +
      '<div class="fold d1" data-f="cs:sent"><div>' + ans('you invoked it') + '</div></div></div>' +
      '<div class="cmp glass"><div class="ta"><span class="tline"><span class="fold inl" data-f="cs:chosen"><span><span class="cchip">' + sv16('spark', 'ic12') + '/doc<span class="xb2" data-act="c:unchoose">×</span></span></span></span>' +
      '<span class="ttx" data-ttx="1"></span><span class="caret"></span></span><div class="chips"><span>Opus 5.5</span><span>High</span></div>' +
      '<span class="send" data-act="c:send"><svg viewBox="0 0 16 16"><path d="M8 13V3.5M4 7.5l4-4 4 4"/></svg></span></div></div>' +
      picker() + '</div>' + pillParts() + CAP3 + '</div>';
  }
  // the main window while a hidden Download all runs (Minimize A)
  const buildAway = () => '<div class="mw main away" data-w="A">' + mainSide('', ['Quarterly numbers', 'Proposal draft', 'Slides for Monday', 'Fix the footer dates']) +
    '<div class="pane"><div class="ttl">Quarterly numbers</div><div class="msgs">' +
    '<div class="um">Sum the Q3 column in sales.xlsx and tell me the top three regions.</div>' +
    '<div class="am"><p>The Q3 total is <b>4.82 M</b>. The top three regions are North (1.31 M), Coast (0.97 M) and Lakes (0.88 M).</p></div></div>' +
    '<div class="cmp glass"><div class="ta"><span class="tline"><span class="ttx ph">Ask Claude for anything</span></span><div class="chips"><span>Opus 5.5</span><span>High</span></div>' +
    '<span class="send"><svg viewBox="0 0 16 16"><path d="M8 13V3.5M4 7.5l4-4 4 4"/></svg></span></div></div></div>' + pillParts() + CAP3 + '</div>';

  // ------------------------------------------------------------------ render
  const slotsS = $('#slotsS'), slotsM = $('#slotsM'), slotsU = $('#slotsU'), slotsC = $('#slotsC');
  const mkSlot = (host, k, html) => { const d = document.createElement('div'); d.className = 'slot'; d.dataset.k = k; d.innerHTML = html; host.appendChild(d); return d; };
  mkSlot(slotsS, 'S', buildSet('S'));
  mkSlot(slotsM, 'set', buildSet('M'));
  mkSlot(slotsM, 'main', buildAway());
  mkSlot(slotsU, 'U', buildSet('U'));
  mkSlot(slotsC, 'C', buildChat());

  function cond(f, el) {
    const i = f.indexOf(':'), k = i < 0 ? f : f.slice(0, i), a = i < 0 ? '' : f.slice(i + 1);
    const v = vOf(el);
    if (k === 'fail') return st[a].s === 'fail' || (st[a].s === 'ck' && bulk.ph === 'none');
    if (k === 'rm') return st[a].s === 'rm';
    if (k === 'bph') return a.split(',').includes(bulk.ph);
    if (k === 'inc') return bulk.ph === 'confirm' ? scopeItems().some((x) => x.id === a) : bulk.items.includes(a);
    if (k === 'pf' || k === 'mf') return bulk.ph === 'partial' && bulk.items.includes(a) && (st[a].s === 'fail' || st[a].s === 'ck' || (k === 'pf' && st[a].s === 'in' && bulk.fixed && bulk.fixed.includes(a)));
    if (k === 'fbo') return bulk.fbOpen === a;
    if (k === 'big') return totals().mb > BIG;
    if (k === 'small') return totals().mb <= BIG;
    if (k === 'cs') return a.split(',').includes(chat.s);
    if (k === 'own') return !!v.own[a];
    if (k === 'owng') return ownOf(a).some((it) => v.own[it.id]);
    if (k === 'cus') return a.split(',').includes(v.cu.s);
    if (k === 'cusec') return v.sec === a;
    if (k === 'cutool') return v.sec === 'tool';
    if (k === 'pkl') return v.sec + '-' + v.cu.src === a;
    return false;
  }
  function renderLive() {
    $$('[data-it]').forEach((el) => { el.style.setProperty('--p', st[el.dataset.it].p.toFixed(4)); });
    const T = totals(), bp = T.mb ? doneMb() / T.mb : 0;
    const curId = want().find((x) => ['dl', 'ck'].includes(st[x].s));
    $$('.mw').forEach((w) => { w.style.setProperty('--bp', (bulk.ph === 'done' ? 1 : bp).toFixed(4)); w.style.setProperty('--cp', curId ? st[curId].p.toFixed(4) : '0'); });
    $$('.mw.set').forEach((w) => { const v = VIEWS[w.dataset.w]; $$('[data-ci]', w).forEach((r) => { const s = v.cu.rows[r.dataset.ci]; r.style.setProperty('--p', s ? s.p.toFixed(4) : '0'); }); });
    $$('[data-lv]').forEach((el) => { const t = LV(el.dataset.lv, vOf(el)); if (el.textContent !== t) el.textContent = t; });
  }
  function render() {
    $$('[data-it]').forEach((el) => { const s = st[el.dataset.it].s; if (el.dataset.st !== s) el.dataset.st = s; });
    $$('.fold[data-f]').forEach((el) => el.classList.toggle('open', cond(el.dataset.f, el)));
    // only the Download all windows (the Settings stage, the minimize stage's two) show the run; the add and chat stages don't
    $$('.mw').forEach((w) => { const on = ['S', 'M', 'A'].includes(w.dataset.w); w.dataset.bph = on ? bulk.ph : 'none'; w.dataset.min = on && bulk.min ? '1' : '0'; w.dataset.pop = on && pop[w.dataset.w] ? '1' : '0'; });
    $$('.mw.set').forEach((w) => {
      const v = VIEWS[w.dataset.w];
      w.dataset.sec = v.sec; w.dataset.cus = v.cu.s; w.dataset.src = v.cu.src;
      const ab = $('.gbtn.add', w); if (ab) ab.setAttribute('aria-expanded', String(v.cu.s === 'entry'));
      w.dataset.pk = v.cu.pk ? '1' : '0';
      $$('[data-act="cu:check"]', w).forEach((b) => { const ok = canCheck(v); b.classList.toggle('dis', !ok); b.setAttribute('aria-disabled', String(!ok)); });
      $$('[data-act="cu:pok"]', w).forEach((b) => { const ok = v.cu.hl >= 0; b.classList.toggle('dis', !ok); b.setAttribute('aria-disabled', String(!ok)); });
      $$('.fv.ro', w).forEach((f) => f.classList.toggle('ph', !v.cu.pick));
      $$('.sdr', w).forEach((r) => r.classList.toggle('hl', +r.dataset.pi === v.cu.hl));
      const gi = $('.ghin', w); if (gi && document.activeElement !== gi && gi.value !== (v.gh[v.sec] || '')) gi.value = v.gh[v.sec] || '';
      const br = $('.gbtn[data-act="cu:browse"]', w); if (br) br.setAttribute('aria-hidden', String(!local(v)));
      $$('[data-nsec]', w).forEach((n) => n.classList.toggle('on', n.dataset.nsec === v.sec));
      $$('.li', w).forEach((el) => el.classList.toggle('on', el.dataset.li === v.sel));
      $$('.dp', w).forEach((el) => el.classList.toggle('on', el.dataset.dp === v.sel));
      $$('.sr[data-sk]', w).forEach((el) => { const id = el.dataset.sk.split(':')[0]; el.classList.toggle('off', !en[el.dataset.sk]); el.classList.toggle('na', !inl(id)); });
      $$('[data-ci]', w).forEach((r) => { const s = v.cu.rows[r.dataset.ci]; const ss = r.classList.contains('bad') ? 'bad' : s ? s.s : 'av'; if (r.dataset.cst !== ss) r.dataset.cst = ss; r.classList.toggle('on', !!(s && s.tick)); });
      $$('[data-cuall]', w).forEach((r) => { const rs = cuRows(v); r.classList.toggle('on', rs.length > 0 && rs.every((f) => (v.cu.rows[f.id] || {}).tick)); });
    });
    $$('.ir2').forEach((el) => el.classList.toggle('skp2', bulk.ph === 'confirm' ? inl(el.dataset.it) : bulk.skip.includes(el.dataset.it)));
    $$('.mw.main').forEach((w) => { if (w.dataset.w === 'C' && w.dataset.cs !== chat.s) w.dataset.cs = chat.s; });
    $$('.slot', slotsM).forEach((s) => s.classList.toggle('off', s.dataset.k !== stg.m));
    $$('[data-t]').forEach((el) => xt(el, TX(el.dataset.t, vOf(el))));
    renderLive();
  }

  // ------------------------------------------------------------------ the Download all run (one item at a time)
  let runQ = [], active = null, epoch = 0;
  const timers = [];
  const later = (ms, f) => { const t = setTimeout(f, ms); timers.push(t); return t; };
  // progress ticks on timers, not animation frames, so a run keeps going in a background tab (the rings and bars ease
  // between ticks with their own CSS transition)
  const tick = (f) => setTimeout(() => f(performance.now()), 33);
  const durFor = (it) => (260 + 5200 * (it.mb / 1077)) * (bulk.slow || 1);
  const failAt = {};
  function enqueue(id) { if (busy(id) || inl(id)) return; st[id].s = 'q'; runQ.push(id); }
  function pump() {
    if (active || bulk.ph === 'paused') return;
    if (!runQ.length) { if (bulk.ph === 'dl') { bulk.ph = fails().length ? 'partial' : 'done'; if (bulk.ph === 'partial') bulk.fbOpen = fails()[0]; render(); phase(); } return; }
    const id = runQ.shift();
    if (st[id].s !== 'q') { pump(); return; }
    active = id; st[id].s = 'dl'; render();
    run(id);
  }
  function run(id) {
    const it = IT[id], dur = durFor(it), p0 = st[id].p, t0 = performance.now(), ep = epoch;
    const step = (now) => {
      if (ep !== epoch || active !== id || st[id].s !== 'dl' || bulk.ph === 'paused') return;
      const p = Math.min(1, p0 + (now - t0) / dur);
      if (failAt[id] && p >= failAt[id]) { st[id].p = failAt[id]; delete failAt[id]; st[id].s = 'fail'; active = null; render(); pump(); return; }
      st[id].p = p; renderLive();
      if (p >= 1) { st[id].s = 'ck'; render(); setTimeout(() => { if (ep !== epoch || st[id].s !== 'ck') return; st[id].s = 'in'; st[id].p = 0; active = null; render(); pump(); }, 280); return; }
      tick(step);
    };
    tick(step);
  }
  function cancel(id) { if (active === id) active = null; runQ = runQ.filter((q) => q !== id); st[id].s = 'av'; st[id].p = 0; render(); pump(); }
  function scrollLib() { /* each page's list fits; nothing to scroll */ }
  function openBulk(v, sc) { bulk.scope = sc; bulk.ph = 'confirm'; bulk.min = false; Object.keys(pop).forEach((k) => { pop[k] = false; }); v.sec = sc; render(); phase(); }
  function closeBulk() { bulk.ph = 'none'; bulk.min = false; bulk.fixed = []; Object.keys(pop).forEach((k) => { pop[k] = false; }); render(); phase(); }
  function checkAgain(id) {
    st[id].s = 'ck'; st[id].p = 0; render();
    const ep = epoch;
    setTimeout(() => {
      if (ep !== epoch) return; st[id].s = 'in'; bulk.fixed = (bulk.fixed || []).concat([id]);
      if (bulk.ph === 'partial' && !fails().length) { bulk.ph = 'done'; phase(); }
      else if (bulk.fbOpen === id) bulk.fbOpen = fails()[0] || null;
      render();
    }, 900);
  }
  function bx(a) {
    if (a === 'cancel' || a === 'close') closeBulk();
    else if (a === 'go') {
      const sc = scopeItems(); bulk.items = sc.map((i) => i.id); bulk.skip = sc.filter((i) => inl(i.id)).map((i) => i.id); bulk.fixed = [];
      if (!want().length) { closeBulk(); return; }
      bulk.ph = 'dl'; want().forEach(enqueue); render(); phase(); pump();
    } else if (a === 'pause') { if (bulk.ph === 'dl') { bulk.ph = 'paused'; render(); phase(); } }
    else if (a === 'resume') { if (bulk.ph === 'paused') { bulk.ph = 'dl'; render(); phase(); if (active) run(active); else pump(); } }
    else if (a === 'stop') { epoch++; if (active) { st[active].s = 'av'; st[active].p = 0; active = null; } runQ.forEach((id) => { st[id].s = 'av'; st[id].p = 0; }); runQ = []; closeBulk(); }
    else if (a === 'retry') { bulk.ph = 'dl'; fails().forEach((id) => { st[id].s = 'q'; runQ.push(id); }); render(); phase(); pump(); }
    else if (a === 'hide') { bulk.min = true; render(); }
  }

  // ------------------------------------------------------------------ the add sheet: checking, the list, parallel installs
  let cuEpoch = 0;
  function cuReset(v) { v.cu = { s: 'closed', src: v.cu.src || 'folder', pick: null, pk: false, hl: -1, rows: {} }; }
  function cuList(v) { v.cu.s = 'list'; v.cu.rows = {}; cuRows(v).forEach((f) => { v.cu.rows[f.id] = { s: 'av', p: 0, tick: true, go: false }; }); }
  function cuInstall(v, ids, failIds) {
    v.cu.s = 'inst';
    const ep = cuEpoch;
    ids.forEach((id, i) => {
      const r = v.cu.rows[id]; r.s = 'dl'; r.p = 0; r.go = true;
      const dur = 1100 + ((i * 677) % 5) * 380, t0 = performance.now(), failP = (failIds || []).includes(id) ? 0.55 : 0;
      const step = (now) => {
        if (ep !== cuEpoch || r.s !== 'dl') return;
        r.p = Math.min(1, (now - t0) / dur);
        if (failP && r.p >= failP) { r.s = 'fail'; finish(); return; }
        if (r.p >= 1) { r.s = 'in'; finish(); return; }
        renderLive(); tick(step);
      };
      tick(step);
    });
    const finish = () => { const rs = Object.values(v.cu.rows).filter((r) => r.go); if (rs.every((r) => r.s !== 'dl')) v.cu.s = 'done'; render(); };
    render();
  }
  function cuAct(v, a, b) {
    const sec = v.sec;
    if (a === 'menu') { const was = v.cu.s === 'entry'; cuReset(v); v.cu.s = was ? 'closed' : 'entry'; }
    else if (a === 'from') { cuReset(v); v.cu.src = b; v.cu.s = 'open'; v.cu.pk = b !== 'gh'; }
    else if (a === 'src') { if (['open', 'fail', 'list'].includes(v.cu.s) && !v.cu.pk) { v.cu.src = b; v.cu.s = 'open'; v.cu.rows = {}; v.cu.pick = null; v.cu.hl = -1; } }
    else if (a === 'browse') { if (v.cu.s === 'open' && local(v) && !v.cu.pk) { const L = pkList(v); v.cu.hl = v.cu.pick ? L.items.findIndex((it) => it[1] === v.cu.pick.p) : -1; v.cu.pk = true; } }
    else if (a === 'psel') { if (v.cu.pk) v.cu.hl = +b; }
    else if (a === 'pok') { if (v.cu.pk && v.cu.hl >= 0) { const it = pkList(v).items[v.cu.hl]; v.cu.pick = { n: it[0], p: it[1], ok: it[3] }; v.cu.pk = false; } }
    else if (a === 'pcancel') { v.cu.pk = false; }
    else if (a === 'close') { cuEpoch++; cuReset(v); }
    else if (a === 'check') {
      // validation: a moment of "Checking…", then either the found-items checklist or what is wrong
      if (!canCheck(v) || v.cu.pk) { render(); return; }
      const ep = cuEpoch, bad = local(v) && !v.cu.pick.ok; v.cu.s = 'chk';
      setTimeout(() => { if (ep !== cuEpoch || v.cu.s !== 'chk') return; if (bad) v.cu.s = 'fail'; else cuList(v); render(); }, reduce ? 0 : 900);
    }
    else if (a === 'scrim') { if (['open', 'fail', 'list'].includes(v.cu.s) && !v.cu.pk) { cuEpoch++; cuReset(v); } }
    else if (a === 'again') { v.cu.pick = null; v.cu.hl = -1; v.cu.s = 'open'; v.cu.pk = local(v); }
    else if (a === 'tick') { const r = v.cu.rows[b]; if (v.cu.s === 'list' && r) r.tick = !r.tick; }
    else if (a === 'all') { if (v.cu.s === 'list') { const rs = cuRows(v), all = rs.every((f) => v.cu.rows[f.id].tick); rs.forEach((f) => { v.cu.rows[f.id].tick = !all; }); } }
    else if (a === 'add') { const ids = ticked(v).map((f) => f.id); if (ids.length) cuInstall(v, ids, v._failNext || []); v._failNext = null; }
    else if (a === 'retry') { const r = v.cu.rows[b]; if (r && r.s === 'fail') cuInstall(v, [b]); }
    else if (a === 'done') {
      Object.keys(v.cu.rows).forEach((id) => { if (v.cu.rows[id].s === 'in') v.own[id] = true; });
      const first = Object.keys(v.cu.rows).find((id) => v.cu.rows[id].s === 'in');
      cuReset(v); if (first) { v.sel = first; }
    }
    render();
  }

  // ------------------------------------------------------------------ actions
  function act(a, el) {
    const p = a.split(':'), k = p[0], id = p[1];
    const v = vOf(el), key = keyOf(el);
    if (k === 'dl') { if (st[id].s === 'fail') { st[id].s = 'q'; runQ.push(id); } else enqueue(id); render(); pump(); }
    else if (k === 'cancel') cancel(id);
    else if (k === 'rm') { st[id].s = 'rm'; render(); }
    else if (k === 'rmx') { st[id].s = 'in'; render(); }
    else if (k === 'rmok') {
      if (IT[id].own) { v.own[id] = false; st[id].s = 'in'; v.sel = v.last[v.sec]; render(); return; }
      st[id].s = 'del'; render(); const ep = epoch; setTimeout(() => { if (ep !== epoch) return; if (st[id].s === 'del') { st[id].s = 'av'; st[id].p = 0; } render(); }, 750);
    }
    else if (k === 'sel') { v.sel = id; if (!IT[id].own) v.last[IT[id].sec] = id; render(); }
    else if (k === 'sec') { v.sec = id; v.sel = v.last[id]; if (v.cu.s === 'entry') cuReset(v); render(); }
    else if (k === 'en') { const kk = id + ':' + p[2]; if (inl(id)) { en[kk] = !en[kk]; render(); } }
    else if (k === 'all') openBulk(v, id);
    else if (k === 'bx') bx(id);
    else if (k === 'fbo') { bulk.fbOpen = bulk.fbOpen === id ? null : id; render(); }
    else if (k === 'fb') { if (id === 'check') checkAgain(p[2]); }
    else if (k === 'm') {
      if (id === 'pop') { pop[key] = !pop[key]; render(); }
      else if (id === 'full') {
        // enlarge: Settings opens on the page doing the download, with the full dialog
        bulk.min = false; Object.keys(pop).forEach((x) => { pop[x] = false; });
        if (key === 'A') { stg.m = 'set'; VIEWS.M.sec = bulk.scope; VIEWS.M.sel = VIEWS.M.last[bulk.scope]; }
        else if (VIEWS[key]) { v.sec = bulk.scope; v.sel = v.last[bulk.scope]; }
        render();
      }
    }
    else if (k === 'cu') cuAct(v, id, p[2]);
    else if (k === 'c') chatAct(id);
  }

  // ------------------------------------------------------------------ scenarios
  const BASE = ['py', 'pyd', 'odoc', 'aex'];
  const FAILS = { tool: ['lo', 'git'], skill: ['ocur'], plug: ['afs'] };
  const PHASE = { browse: 'Browsing', confirm: 'Confirm', dl: 'Downloading', paused: 'Paused', done: 'Done', partial: 'Partly failed' };
  function resetModel() {
    epoch++; runQ = []; active = null; Object.keys(failAt).forEach((k) => delete failAt[k]);
    ITEMS.forEach((i) => { st[i.id] = { s: 'av', p: 0 }; });
    BASE.forEach((id) => { st[id].s = 'in'; });
    ITEMS.forEach((i) => (i.sk || []).forEach((s) => { en[i.id + ':' + s.n] = s.on; }));
    Object.keys(pop).forEach((k) => { pop[k] = false; });
    bulk.min = false; bulk.fixed = []; bulk.fbOpen = null; bulk.slow = 1;
  }
  function setBulk(name, sc) {
    resetModel();
    bulk.scope = sc; bulk.ph = name === 'browse' ? 'none' : name;
    if (name !== 'browse' && name !== 'confirm') {
      const items = scopeItems(); bulk.items = items.map((i) => i.id); bulk.skip = items.filter((i) => inl(i.id)).map((i) => i.id);
      const w = want(), F = FAILS[sc];
      if (name === 'done') w.forEach((id) => { st[id].s = 'in'; });
      if (name === 'partial') { w.forEach((id) => { if (F.includes(id)) { st[id].s = 'fail'; st[id].p = IT[id].fw === 'sum' ? 0.64 : 0.3; } else st[id].s = 'in'; }); bulk.fbOpen = F[0]; }
      if (name === 'dl' || name === 'paused') {
        const at = w.indexOf('lo') >= 0 ? w.indexOf('lo') : Math.floor(w.length * 0.4);
        w.forEach((id, i) => { if (i < at) st[id].s = 'in'; else if (i === at) { st[id].s = 'dl'; st[id].p = 0.42; active = id; } else { st[id].s = 'q'; runQ.push(id); } });
      }
    } else { bulk.items = []; bulk.skip = []; }
  }
  function scen(name) {
    stopRun();
    scenario = name;
    setBulk(name, scope);
    const v = VIEWS.S; v.sec = name === 'browse' ? 'skill' : scope; v.sel = name === 'browse' ? 'odoc' : v.last[v.sec];
    $('#phS').textContent = PHASE[name]; if (segS) segS.set(name);
    render();
    if (name === 'dl' && active) run(active);
  }
  function phase() {
    const m = { none: 'browse', confirm: 'confirm', dl: 'dl', paused: 'paused', done: 'done', partial: 'partial' }[bulk.ph];
    $('#phS').textContent = PHASE[m]; if (segS) segS.set(m);
  }
  // the minimize stage (Minimize A): the Settings window and the main window, one model
  const MPH = { dlg: 'Dialog open', setpill: 'Hidden: pill in Settings', setpop: 'Settings: popover', main: 'In a chat: pill', mainpop: 'In a chat: popover', paused: 'Paused', done: 'Done on the pill', partial: 'Partly failed', full: 'Enlarged in Settings' };
  const MMAP = { dlg: ['dl', false, 'set', 0], setpill: ['dl', true, 'set', 0], setpop: ['dl', true, 'set', 1], main: ['dl', true, 'main', 0], mainpop: ['dl', true, 'main', 1], paused: ['paused', true, 'main', 1], done: ['done', true, 'main', 0], partial: ['partial', true, 'main', 1], full: ['partial', false, 'set', 0] };
  function mscen(name) {
    stopRun();
    const m = MMAP[name];
    setBulk(m[0], 'tool');
    bulk.min = m[1]; stg.m = m[2]; pop[m[2] === 'set' ? 'M' : 'A'] = !!m[3];
    VIEWS.M.sec = 'tool'; VIEWS.M.sel = 'lo';
    $('#phM').textContent = MPH[name]; if (segM) segM.set(name);
    render();
    if (m[0] === 'dl' && active) run(active);
  }
  // the add stage (Custom C): the page the Add button is on, the source its menu chose, and the sheet's states
  const UPH = { entry: 'Add menu open', sheet: 'Sheet', browse: 'Browsing: system dialog', chosen: 'Chosen', chk: 'Checking', fail: 'Nothing valid found', list: 'Found: pick what to add', inst: 'Adding side by side', done: 'All added', added: 'Under Your own' };
  const UPG = { plug: 'Plugins', skill: 'Skills', tool: 'Tools' };
  const USRC = { folder: 'From a folder\u2026', zip: 'From a .zip\u2026', gh: 'From GitHub\u2026' };
  const ustate = { pg: 'skill', src: 'folder' };
  const failId = (v) => { const rs = cuRows(v); return rs.length ? rs[Math.min(2, rs.length - 1)].id : null; };
  function uscen(name) {
    stopRunU(); cuEpoch++;
    const v = VIEWS.U; v.sec = ustate.pg; v.sel = v.last[v.sec]; v.own = {}; cuReset(v);
    // browsing and choosing only exist for a folder and a .zip: GitHub has nothing local to browse
    if (['browse', 'chosen'].includes(name) && ustate.src === 'gh') { ustate.src = 'folder'; if (segUS) segUS.set('folder'); }
    v.cu.src = ustate.src;
    const L = pkList(v), good = L ? L.items.findIndex((it) => it[3]) : -1, bad = L ? L.items.findIndex((it) => !it[3]) : -1;
    const choose = (i) => { const it = L.items[i]; v.cu.pick = { n: it[0], p: it[1], ok: it[3] }; };
    if (name === 'entry') v.cu.s = 'entry';
    if (name === 'sheet') v.cu.s = 'open';
    if (name === 'browse') { v.cu.s = 'open'; v.cu.pk = true; v.cu.hl = good; }
    if (name === 'chosen') { v.cu.s = 'open'; choose(good); }
    if (name === 'chk') { v.cu.s = 'chk'; if (L) choose(good); }
    if (name === 'fail') { v.cu.s = 'fail'; if (L) choose(bad); }
    if (['list', 'inst', 'done', 'added'].includes(name) && L) choose(good);
    if (name === 'list') cuList(v);
    if (name === 'inst' || name === 'done') {
      cuList(v);
      if (name === 'done') { v.cu.s = 'done'; Object.values(v.cu.rows).forEach((r) => { r.s = 'in'; r.go = true; r.p = 1; }); }
      else { const ids = Object.keys(v.cu.rows); cuInstall(v, ids, [failId(v)]); }
    }
    if (name === 'added') { cuList(v); Object.keys(v.cu.rows).forEach((id) => { v.cu.rows[id].s = 'in'; }); cuAct(v, 'done'); }
    $('#phU').textContent = UPH[name]; if (segU) segU.set(name);
    render();
  }

  // ------------------------------------------------------------------ the chat (Invoke A; the model calls skills by itself)
  const TYPED = 'Draft a one-page proposal as proposal.docx from notes.md.';
  const CPH = { model: 'The model invokes it', idle: 'Composer', pick: 'Picker open', chosen: 'Skill picked', sent: 'Sent: you invoked it' };
  function setTyped(t) { $$('.mw.main[data-w="C"] [data-ttx]').forEach((el) => { el.textContent = t || 'Ask Claude for anything'; el.classList.toggle('ph', !t); }); }
  function chatTo(s, typed) {
    chat.s = s; $('#phC').textContent = CPH[s] || CPH.idle; if (segC && CPH[s]) segC.set(s);
    $$('.mw.main[data-w="C"]').forEach((w) => w.classList.toggle('pk', s === 'pick'));
    setTyped(s === 'chosen' ? TYPED : s === 'pick' ? '/do' : typed || '');
    render();
  }
  function chatAct(a) {
    if (a === 'choose') chatTo('chosen');
    else if (a === 'unchoose') chatTo('idle');
    else if (a === 'send') { if (chat.s === 'chosen') chatTo('sent'); else if (chat.s === 'typed') chatTo('model'); }
  }

  // ------------------------------------------------------------------ clicks inside the windows, with a press
  function visible(el, root) {
    for (let n = el; n && n !== root; n = n.parentElement) {
      if (n.classList.contains('fold') && !n.classList.contains('open')) return false;
      const c = getComputedStyle(n);
      if (+c.opacity === 0 || c.display === 'none' || c.pointerEvents === 'none' && n.classList.contains('slot')) return false;
    }
    return true;
  }
  const PRESSABLE = '.gbtn,.tb,.xb,.li,.tg,.ni,.spi,.send,.xb2,.mpill,.exp,.cur,.lnk,.srcs>span,.amenu>span,.sdr,.sbtn,.sdx';
  function pressEl(el) { const b = el.closest(PRESSABLE) || el; b.classList.add('press'); setTimeout(() => b.classList.remove('press'), 170); }
  function press(a, root, after) {
    const el = $$('[data-act="' + a + '"]', root).find((e) => visible(e, root));
    if (el) pressEl(el);
    setTimeout(() => { act(a, el || (root.dataset && root.dataset.w ? root : root.querySelector('[data-w]'))); if (after) after(); }, reduce ? 0 : 190);
  }
  // a click outside the Add button and its menu closes the menu (a menu never stays open behind a click elsewhere)
  function closeMenus(target) {
    Object.keys(VIEWS).forEach((k) => {
      const v = VIEWS[k];
      if (v.cu.s !== 'entry') return;
      const w = $('.mw.set[data-w="' + k + '"]');
      if (w && target && target.closest && target.closest('.tact') && w.contains(target)) return;
      cuReset(v);
    });
  }
  [slotsS, slotsM, slotsU, slotsC].forEach((sl) => sl.addEventListener('click', (e) => {
    closeMenus(e.target);
    const el = e.target.closest('[data-act]');
    if (!el || !sl.contains(el)) { render(); return; }
    if (sl === slotsU) stopRunU(); else if (sl === slotsC) stopRunC(); else stopRun();
    if (el.classList.contains('dis')) { render(); return; }
    pressEl(el);
    act(el.dataset.act, el);
  }));
  // the GitHub link is the one typed field; Check wakes up when it looks like owner/repo
  [slotsS, slotsM, slotsU].forEach((sl) => sl.addEventListener('input', (e) => {
    if (!e.target.classList || !e.target.classList.contains('ghin')) return;
    const v = vOf(e.target); v.gh[v.sec] = e.target.value.trim(); stopRunU(); render();
  }));
  // keyboard: Enter or Space presses the focused button, Esc closes the menu or the sheet
  [slotsS, slotsM, slotsU].forEach((sl) => sl.addEventListener('keydown', (e) => {
    const el = e.target.closest && e.target.closest('[data-act]');
    if (e.key === 'Enter' && e.target.classList && e.target.classList.contains('ghin')) { e.preventDefault(); const v = vOf(e.target); if (canCheck(v) && v.cu.s === 'open') { cuAct(v, 'check'); } return; }
    if ((e.key === 'Enter' || e.key === ' ') && el && el.getAttribute('role')) { e.preventDefault(); el.click(); return; }
    if (e.key !== 'Escape') return;
    // Esc closes the picker first, then the menu or the sheet
    Object.keys(VIEWS).forEach((k) => { const v = VIEWS[k]; if (v.cu.pk) v.cu.pk = false; else if (['entry', 'open', 'fail', 'list', 'chk'].includes(v.cu.s)) { cuEpoch++; cuReset(v); } });
    render();
  }));

  // ------------------------------------------------------------------ Play
  let runTok = 0, runTokU = 0, runTokC = 0;
  function stopRun() { runTok++; timers.splice(0).forEach(clearTimeout); }
  function stopRunU() { runTokU++; }
  function stopRunC() { runTokC++; }
  const waitT = (get) => (ms, tok) => new Promise((res, rej) => setTimeout(() => (get() === tok ? res() : rej()), ms));
  const wait = waitT(() => runTok), waitU = waitT(() => runTokU), waitC = waitT(() => runTokC);
  async function until(f, tok, max) { for (let i = 0; i < (max || 300); i++) { if (f()) return; await wait(100, tok); } }
  async function playS() {
    scope = 'tool'; segScope.set('tool');
    scen('browse');
    const tok = runTok, root = slotsS;
    try {
      await wait(1600, tok);
      press('en:odoc:slides', root); await wait(1500, tok);
      press('sec:plug', root); await wait(1000, tok);
      press('all:plug', root); await wait(2200, tok);
      press('bx:cancel', root); await wait(700, tok);
      press('sec:tool', root); await wait(1000, tok);
      press('all:tool', root); await wait(2400, tok);
      failAt.lo = 0.64; failAt.git = 0.3;
      press('bx:go', root); await wait(600, tok);
      await until(() => st.lo.s === 'dl' && st.lo.p > 0.3, tok);
      press('bx:pause', root); await wait(1500, tok);
      press('bx:resume', root); await wait(300, tok);
      await until(() => bulk.ph === 'partial' || bulk.ph === 'done', tok);
      await wait(2200, tok);
      press('fb:check:lo', root); await wait(1600, tok);
      press('bx:retry', root); await wait(300, tok);
      await until(() => bulk.ph === 'done', tok);
    } catch (e) { /* stopped */ }
  }
  async function playM() {
    mscen('dlg');
    bulk.slow = 2.6; failAt.git = 0.3;
    const tok = runTok, setW = $('#slotsM .mw.set'), awayW = $('#slotsM .mw.away');
    const ph = (k) => { $('#phM').textContent = MPH[k] || k; if (MPH[k]) segM.set(k); };
    try {
      await wait(1500, tok);
      press('bx:hide', setW); ph('setpill'); await wait(1500, tok);
      press('m:pop', setW); ph('setpop'); await wait(1800, tok);
      press('m:pop', setW); await wait(500, tok);
      stg.m = 'main'; render(); ph('main'); await wait(1300, tok);
      press('m:pop', awayW); ph('mainpop'); await wait(1300, tok);
      press('bx:pause', awayW); ph('paused'); await wait(1300, tok);
      press('bx:resume', awayW); await wait(500, tok);
      press('m:pop', awayW); ph('main');
      await until(() => bulk.ph === 'partial' || bulk.ph === 'done', tok);
      ph(bulk.ph === 'done' ? 'done' : 'Partly failed, on the pill');
      await wait(1600, tok);
      press('m:pop', awayW); if (bulk.ph === 'partial') ph('partial'); await wait(1600, tok);
      press('m:full', awayW); ph('full'); await wait(2000, tok);
      if (bulk.ph === 'partial') { press('bx:retry', setW); await until(() => bulk.ph === 'done', tok); }
    } catch (e) { /* stopped */ }
  }
  // Play: on each page in turn the Add button opens its menu and a menu item opens the system picker (the stand-in dialog).
  // Skills, From a folder: a folder with no SKILL.md is chosen first (nothing valid), then Choose another and the right
  // folder; six skills are found, one invalid, one install fails and is retried. Then Plugins from a .zip and Tools from a
  // folder (the same steps), and last Skills from GitHub, the one typed link.
  async function playU() {
    const tok = runTokU, root = slotsU, v = VIEWS.U;
    const P = (a) => new Promise((res) => press(a, root, res));
    const setPh = (k) => { $('#phU').textContent = UPH[k]; segU.set(k); };
    const page = (pg) => { ustate.pg = pg; segUP.set(pg); };
    const via = (src) => { ustate.src = src; segUS.set(src); };
    const goodI = () => pkList(v).items.findIndex((it) => it[3]), badI = () => pkList(v).items.findIndex((it) => !it[3]);
    const drive = async (pg, src) => {
      page(pg); via(src); await P('sec:' + pg); await waitU(900, tok);
      await P('cu:menu'); setPh('entry'); await waitU(1000, tok);
      await P('cu:from:' + src); setPh('browse'); await waitU(1500, tok);
    };
    try {
      page('skill'); via('folder'); uscen('entry'); await waitU(1200, tok);
      await P('cu:from:folder'); setPh('browse'); await waitU(1500, tok);
      await P('cu:psel:' + badI()); await waitU(900, tok);
      await P('cu:pok'); setPh('chosen'); await waitU(1300, tok);
      await P('cu:check'); setPh('chk'); await waitU(1500, tok);
      setPh('fail'); await waitU(1800, tok);
      await P('cu:again'); setPh('browse'); await waitU(1000, tok);
      await P('cu:psel:' + goodI()); await waitU(800, tok);
      await P('cu:pok'); setPh('chosen'); await waitU(1200, tok);
      await P('cu:check'); setPh('chk'); await waitU(1500, tok);
      setPh('list'); await waitU(1500, tok);
      await P('cu:all'); await waitU(900, tok);
      await P('cu:all'); await waitU(700, tok);
      v._failNext = [failId(v)];
      await P('cu:add'); setPh('inst');
      for (let i = 0; i < 80 && v.cu.s !== 'done'; i++) await waitU(100, tok);
      await waitU(1300, tok);
      await P('cu:retry:' + failId(v));
      for (let i = 0; i < 80 && v.cu.s !== 'done'; i++) await waitU(100, tok);
      await waitU(1500, tok);
      setPh('done'); await waitU(900, tok);
      await P('cu:done'); setPh('added'); await waitU(1700, tok);
      // the other two pages, then the typed link
      for (const [pg, src] of [['plug', 'zip'], ['tool', 'folder']]) {
        await drive(pg, src);
        await P('cu:psel:' + goodI()); await waitU(800, tok);
        await P('cu:pok'); setPh('chosen'); await waitU(1100, tok);
        await P('cu:check'); setPh('chk'); await waitU(1400, tok);
        setPh('list'); await waitU(1100, tok);
        await P('cu:add'); setPh('inst');
        for (let i = 0; i < 80 && v.cu.s !== 'done'; i++) await waitU(100, tok);
        await waitU(1100, tok);
        await P('cu:done'); setPh('added'); await waitU(1500, tok);
      }
      page('skill'); via('gh'); await P('sec:skill'); await waitU(900, tok);
      await P('cu:menu'); setPh('entry'); await waitU(1000, tok);
      await P('cu:from:gh'); setPh('sheet'); await waitU(1300, tok);
      await P('cu:check'); setPh('chk'); await waitU(1400, tok);
      setPh('list'); await waitU(1400, tok);
      await P('cu:close'); setPh('entry'); await waitU(900, tok);
    } catch (e) { /* stopped */ }
  }
  async function playC() {
    stopRunC(); chatTo('idle');
    const tok = runTokC, root = slotsC;
    try {
      // first: the model calls the skill by itself
      await waitC(700, tok);
      for (let i = 1; i <= TYPED.length; i += 3) { chatTo('typed', TYPED.slice(0, i)); await waitC(40, tok); }
      chatTo('typed', TYPED); await waitC(800, tok);
      press('c:send', root); await waitC(250, tok); chatTo('model');
      await waitC(3200, tok);
      // then the optional way: you name it with "/"
      chatTo('idle'); await waitC(900, tok);
      for (const t of ['/', '/d', '/do']) { setTyped(t); await waitC(260, tok); }
      chatTo('pick'); await waitC(1700, tok);
      press('c:choose', root); await waitC(250, tok);
      for (let i = 1; i <= TYPED.length; i += 3) { $$('.mw.main[data-w="C"] [data-ttx]').forEach((el) => { el.textContent = TYPED.slice(0, i); el.classList.remove('ph'); }); await waitC(40, tok); }
      await waitC(800, tok);
      press('c:send', root); await waitC(250, tok);
    } catch (e) { /* stopped */ }
  }
  $('#playS').addEventListener('click', playS);
  $('#playM').addEventListener('click', playM);
  $('#playU').addEventListener('click', playU);
  $('#playC').addEventListener('click', playC);

  // ------------------------------------------------------------------ controls (every pick is made, so there is no picks bar)
  const segS = K.seg($('#segS'), Object.keys(PHASE).map((k) => ({ v: k, t: PHASE[k] })), 'browse', (v) => scen(v), { wrap: true });
  const segScope = K.seg($('#segScope'), [{ v: 'plug', t: 'Plugins · small' }, { v: 'skill', t: 'Skills · small' }, { v: 'tool', t: 'Tools · large' }], scope, (v) => { scope = v; scen(scenario === 'browse' ? 'confirm' : scenario); }, { wrap: true });
  const segM = K.seg($('#segM'), Object.keys(MPH).map((k) => ({ v: k, t: MPH[k] })), 'dlg', (v) => mscen(v), { wrap: true });
  const segU = K.seg($('#segU'), Object.keys(UPH).map((k) => ({ v: k, t: UPH[k] })), 'entry', (v) => uscen(v), { wrap: true });
  const segUP = K.seg($('#segUP'), Object.keys(UPG).map((k) => ({ v: k, t: UPG[k] })), ustate.pg, (v) => { ustate.pg = v; uscen(segU.get()); }, { wrap: true });
  const segUS = K.seg($('#segUS'), Object.keys(USRC).map((k) => ({ v: k, t: USRC[k] })), ustate.src, (v) => { ustate.src = v; uscen(segU.get() === 'fail' && v !== 'folder' ? 'sheet' : segU.get()); }, { wrap: true });
  const segC = K.seg($('#segC'), Object.keys(CPH).map((k) => ({ v: k, t: CPH[k] })), 'model', (v) => { stopRunC(); chatTo(v); }, { wrap: true });
  K.seg($('#segT'), [{ v: 'auto', t: 'Auto' }, { v: 'light', t: 'Light' }, { v: 'dark', t: 'Dark' }], K.themeMode(), (m) => K.theme(m));
  const applyTheme = (dark) => $$('.stage').forEach((s) => s.classList.toggle('lt', !dark));
  K.onTheme(applyTheme); applyTheme(K.isDark());

  // ------------------------------------------------------------------ "All picks made": the list
  const PICKED = [
    ['Layout', 'D', 'A library list with a detail pane'],
    ['Sections', 'C', 'Separate Plugins and Skills pages, Plugins above Skills; Tools on the Tools page'],
    ['Download all', 'per page', 'Its own confirmation on each page; no “Download everything”'],
    ['Large download', '> 100 MB', 'The amber warning shows only above 100 MB'],
    ['Minimize', 'A', 'A glass pill in the sidebar with a popover'],
    ['If it fails', 'manual', 'Download it yourself, then Check again'],
    ['In-chat offer', 'A', 'The model calls an enabled skill by itself'],
    ['Invoke', 'A', 'One timeline row names the skill and who invoked it'],
    ['Words', 'S1 Plain', 'Plain wording throughout'],
    ['Custom', 'C', 'An Add button with a chevron in each title row, opening From a folder…, From a .zip… and From GitHub…'],
  ];
  $('#picked').innerHTML = PICKED.map((r) => '<div class="pr"><span class="pk2">' + esc(r[0]) + '</span><b>' + esc(r[1]) + '</b><span class="pd">' + esc(r[2]) + '</span></div>').join('');

  // ------------------------------------------------------------------ the providers table
  const PROV = [
    ['Claude Code', 'The chat’s folder as a plugin, <code>--plugin-dir</code>; skills show as <code>marrow:doc</code>. Claude lists them and runs one through its Skill tool, which loads the full text then.', 'Listing tested on 2.1.287; a model turn using one not yet run', 'SF'],
    ['Codex', '<code>skills/extraRoots/set</code> with the chat’s folder, right after <code>initialize</code> (0.136.0 and later). Codex lists names and descriptions within 2% of the context and reads a chosen skill in full.', 'Listing tested on 0.159.3', 'SF'],
    ['Copilot', '<code>skillDirectories</code> in <code>session.create</code> and <code>session.resume</code>.', 'Source and docs; not installed here', 'UN'],
    ['OpenCode', '<code>skills.paths</code> in <code>OPENCODE_CONFIG_CONTENT</code>, with your own paths kept.', 'Source; not installed here', 'UN'],
    ['Pi · Command Code', '<code>--skill &lt;folder&gt;</code> for the session (Command Code: each turn).', 'Docs; not installed here', 'UN'],
    ['Grok', '<code>--plugin-dir</code>, its own folder, after a live check.', 'Docs; flag position unchecked', 'UN'],
    ['Antigravity · Cursor', 'Q-SKILLS-8 A: one Marrow plugin in their plugin folders after a live test. Until then a skill reaches them only when you name it (its text goes in your message).', 'Docs only', 'UN'],
    ['Devin', 'No folder option documented: a skill reaches it only when you name it.', 'Docs', 'ED'],
    ['DeepSeek, Meta, Z.ai, OpenRouter, OpenAI-compatible', 'Marrow runs the loop: the enabled skills’ names and descriptions go in the system prompt, held to the same budget, and a <code>use_skill</code> tool returns a skill’s full text when the model calls it.', 'Marrow’s own design', 'ED'],
  ];
  $('#prov').innerHTML = '<div class="cr h"><span>Provider</span><span>How it gets the chat’s enabled skills, and loads one on use</span><span>Evidence</span></div>' +
    PROV.map((r) => '<div class="cr p3"><span><b>' + esc(r[0]) + '</b></span><span><span class="lb">How</span>' + r[1] + '</span><span><span class="lb">Evidence</span><span class="tg' + (r[3] === 'UN' ? ' un' : '') + '">' + r[3] + '</span>' + esc(r[2]) + '</span></div>').join('');

  // ------------------------------------------------------------------ fit the windows to the width (no sideways scroll at 390 px)
  function fit() { $$('.stage').forEach((s) => s.style.setProperty('--sc', Math.min(1, s.clientWidth / 1008).toFixed(4))); }
  if (window.ResizeObserver) { const ro = new ResizeObserver(fit); $$('.stage').forEach((st2) => ro.observe(st2)); }
  window.addEventListener('resize', fit);
  fit();

  scen('browse');
  mscen('dlg');
  uscen('entry');
  chatTo('model');
  window.__sd = { st, bulk, VIEWS, chat, scen, mscen, uscen, chatTo, act: (a, w) => act(a, $('[data-w="' + (w || 'S') + '"]')), en, ustate, pop, stg };
})();
