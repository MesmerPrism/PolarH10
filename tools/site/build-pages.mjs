import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { marked } from 'marked';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const docsRoot = path.join(repoRoot, 'docs');
const siteRoot = path.join(repoRoot, 'site');
const referenceRoot = path.join(siteRoot, 'reference');
const assetsSource = path.join(docsRoot, 'assets');
const diagramsSource = path.join(docsRoot, 'diagrams');
const repoUrl = 'https://github.com/MesmerPrism/PolarH10';
const assetVersion = '20260319-polaroid-site-8';

const docGroups = [
  {
    title: 'Start Here',
    items: [
      { file: 'index.md', label: 'Documentation Home', description: 'App-first entry point into the reference set.' },
      { file: 'app-overview.md', label: 'App Overview', description: 'What the WPF monitor and CLI are for in practice.' },
      { file: 'ui-preview.md', label: 'WPF UI Preview', description: 'Current operator-facing visual language and layout.' },
      { file: 'getting-started.md', label: 'Getting Started', description: 'Build, run, and verify the toolchain.' },
      { file: 'cli.md', label: 'CLI Reference', description: 'Scan, doctor, record, replay, and stream flows.' },
      { href: '../diagrams/viewer.html', label: 'Diagram Viewer', description: 'Architecture, data path, and runtime maps.' }
    ]
  },
  {
    title: 'Protocol',
    items: [
      { file: 'protocol/overview.md', label: 'Protocol Overview', description: 'Services, characteristics, and data streams.' },
      { file: 'protocol/gatt-map.md', label: 'GATT Map', description: 'Polar service and characteristic layout.' },
      { file: 'protocol/pmd-commands.md', label: 'PMD Commands', description: 'Control point request and response flow.' },
      { file: 'protocol/ecg-format.md', label: 'ECG Format', description: 'Frame structure and unit decoding.' },
      { file: 'protocol/acc-format.md', label: 'ACC Format', description: 'Accelerometer compression and scaling.' },
      { file: 'protocol/hr-measurement.md', label: 'HR Measurement', description: 'Heart rate and RR parsing.' }
    ]
  },
  {
    title: 'Support',
    items: [
      { file: 'platform-guides/index.md', label: 'Platform Guides', description: 'Windows-specific BLE and runtime notes.' },
      { file: 'references.md', label: 'References', description: 'Source material and research links.' }
    ]
  }
];

await fs.rm(siteRoot, { recursive: true, force: true });
await fs.mkdir(siteRoot, { recursive: true });
await fs.mkdir(referenceRoot, { recursive: true });
await copyDir(assetsSource, path.join(siteRoot, 'assets'));
await copyDir(diagramsSource, path.join(siteRoot, 'diagrams'));
await fs.writeFile(path.join(siteRoot, '.nojekyll'), '', 'utf8');

const markdownFiles = await collectMarkdownFiles(docsRoot);
for (const filePath of markdownFiles) {
  const rel = path.relative(docsRoot, filePath).replace(/\\/g, '/');
  const outPath = path.join(referenceRoot, rel.replace(/\.md$/i, '.html'));
  await fs.mkdir(path.dirname(outPath), { recursive: true });
  const markdown = await fs.readFile(filePath, 'utf8');
  const title = extractTitle(markdown) ?? rel.replace(/\.md$/i, '');
  const html = renderMarkdown(markdown, rel);
  const currentOutDir = path.dirname(path.relative(siteRoot, outPath)).replace(/\\/g, '/');
  await fs.writeFile(outPath, renderPage({
    title,
    bodyClass: 'doc-page',
    navKey: 'reference',
    pageTitle: title,
    pageIntro: rel === 'index.md'
      ? 'Start with the app surfaces and operator flow, then drill into protocol, diagnostics, and Mermaid system maps when you need internals.'
      : null,
    sidebar: renderSidebar(rel),
    content: `<article class="prose">${html}</article>`,
    currentDir: currentOutDir
  }), 'utf8');
}

await fs.writeFile(path.join(siteRoot, 'index.html'), renderHomePage(), 'utf8');
await fs.writeFile(path.join(siteRoot, '404.html'), render404Page(), 'utf8');
console.log(`Built GitHub Pages site at ${siteRoot}`);

function renderMarkdown(markdown, sourceRel) {
  const renderer = new marked.Renderer();
  renderer.link = ({ href, title, text }) => {
    const safeHref = href ? rewriteHref(href, sourceRel) : '#';
    const titleAttr = title ? ` title="${escapeHtml(title)}"` : '';
    return `<a href="${escapeHtml(safeHref)}"${titleAttr}>${text}</a>`;
  };
  renderer.image = ({ href, title, text }) => {
    const safeHref = href ? rewriteHref(href, sourceRel) : '';
    const titleAttr = title ? ` title="${escapeHtml(title)}"` : '';
    const alt = text ? escapeHtml(text) : '';
    return `<img src="${escapeHtml(safeHref)}" alt="${alt}"${titleAttr} />`;
  };

  marked.setOptions({
    gfm: true,
    breaks: false,
    renderer
  });

  return marked.parse(markdown);
}

function rewriteHref(href, sourceRel) {
  if (/^(https?:|mailto:|#)/i.test(href)) {
    return href;
  }

  const [rawPath, rawHash] = href.split('#');
  const hash = rawHash ? `#${rawHash}` : '';
  const sourceDir = path.posix.dirname(sourceRel);
  const resolved = path.posix.normalize(path.posix.join(sourceDir, rawPath));

  if (resolved.endsWith('.md')) {
    const currentDir = path.posix.join('reference', path.posix.dirname(sourceRel));
    const targetPath = path.posix.join('reference', resolved.replace(/\.md$/i, '.html'));
    let relativeHref = path.posix.relative(currentDir, targetPath);
    if (!relativeHref) {
      relativeHref = path.posix.basename(targetPath);
    }
    return relativeHref + hash;
  }

  if (resolved.startsWith('assets/') || resolved.startsWith('diagrams/')) {
    const currentDir = path.posix.join('reference', path.posix.dirname(sourceRel));
    let relativeHref = path.posix.relative(currentDir, resolved);
    if (!relativeHref) {
      relativeHref = path.posix.basename(resolved);
    }
    return relativeHref + hash;
  }

  return href + hash;
}

function renderPage({ title, bodyClass, navKey, pageTitle, pageIntro, sidebar, content, currentDir }) {
  const toRoot = currentDir && currentDir !== '.' ? path.posix.relative(currentDir, '.') || '.' : '.';
  const asset = (target) => path.posix.join(toRoot, target).replace(/\\/g, '/');
  const homeHref = asset('index.html');
  const topNav = renderTopNav(navKey, homeHref, asset('reference/index.html'), asset('diagrams/viewer.html'));

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${escapeHtml(title)} | PolarH10</title>
  <meta name="description" content="PolarH10 reference docs and architecture diagrams." />
  <link rel="stylesheet" href="${asset('assets/site.css')}?v=${assetVersion}" />
</head>
<body class="${bodyClass}">
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, homeHref)}
    <div class="page-layout">
      <aside class="panel sidebar">
        ${sidebar}
      </aside>
      <main class="panel content-panel">
        <div class="eyebrow">PolarH10 Reference</div>
        <h1 class="page-title">${escapeHtml(pageTitle)}</h1>
        ${pageIntro ? `<p class="page-intro">${escapeHtml(pageIntro)}</p>` : ''}
        ${content}
      </main>
    </div>
    ${renderFooter()}
  </div>
</body>
</html>`;
}

function renderHomePage() {
  const topNav = renderTopNav('home', 'index.html', 'reference/index.html', 'diagrams/viewer.html');
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>PolarH10 Windows Telemetry</title>
  <meta name="description" content="Windows-native Polar H10 telemetry monitor, CLI capture tooling, protocol docs, and Mermaid system maps." />
  <link rel="stylesheet" href="assets/site.css?v=${assetVersion}" />
</head>
<body>
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, 'index.html')}
    <section class="hero hero-home">
      <div class="panel hero-copy tone-dark">
        <div class="eyebrow">Windows Telemetry Monitor + Capture Stack</div>
        <h1>Scan.<br />Link.<br />Stream.<br />Record.</h1>
        <p>
          PolarH10 is a Windows-native toolchain for the Polar H10 chest strap. The WPF app and CLI let you
          discover nearby straps over BLE/GATT, inspect live HR, ECG, and ACC data, run diagnostics before capture,
          and record reusable sessions without depending on Polar&apos;s mobile SDK at runtime.
        </p>
        <div class="action-row">
          <a class="button primary" href="reference/app-overview.html">Open App Overview</a>
          <a class="button primary" href="reference/getting-started.html">Get Started</a>
          <a class="button" href="reference/ui-preview.html">View WPF UI</a>
          <a class="button" href="diagrams/viewer.html">System Maps</a>
        </div>
        <div class="stats">
          <div class="stat tone-cool"><strong>Live telemetry</strong><span>HR · RR · ECG · ACC in the WPF monitor.</span></div>
          <div class="stat tone-violet"><strong>Dual surfaces</strong><span>Use the WPF app for operators and the CLI for diagnostics.</span></div>
          <div class="stat tone-warm"><strong>Capture path</strong><span>CSV + JSONL session export, replay, and manifests.</span></div>
          <div class="stat tone-signal"><strong>System maps</strong><span>4 Mermaid diagrams covering repo, architecture, flow, and lifecycle.</span></div>
        </div>
      </div>
      <aside class="panel hero-preview">
        <div class="eyebrow">Reference App // Operator View</div>
        <img src="assets/brutal-tdr-preview.png" alt="PolarH10 WPF application preview" />
        <ul class="note-list feature-list">
          <li>
            <strong>Device control</strong>
            <p>Scan nearby straps, assign aliases, and switch the active unit without losing the current context.</p>
          </li>
          <li>
            <strong>Live telemetry tabs</strong>
            <p>Inspect heart rate, respiratory timing, ECG, and ACC as immediate runtime surfaces instead of abstract metrics.</p>
          </li>
          <li>
            <strong>Diagnostics + capture</strong>
            <p>Use doctor-style validation, runtime logs, and direct recording flows before you commit to a longer session.</p>
          </li>
        </ul>
      </aside>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">First Session Path</h2>
      <p class="section-subtitle">The shortest route from a strap on your desk to usable telemetry on Windows.</p>
      <div class="step-grid">
        <div class="step-card tone-cool">
          <div class="step-no">01</div>
          <h3>Scan nearby straps</h3>
          <p>Find advertisements, check addresses and aliases, and confirm that the intended H10 is actually visible.</p>
        </div>
        <div class="step-card tone-violet">
          <div class="step-no">02</div>
          <h3>Open the link</h3>
          <p>Connect over GATT, verify the service surface, and negotiate PMD settings before you trust the stream.</p>
        </div>
        <div class="step-card tone-signal">
          <div class="step-no">03</div>
          <h3>Inspect live data</h3>
          <p>Use the WPF monitor for charts and diagnostics or the CLI when you need a more direct validation surface.</p>
        </div>
        <div class="step-card tone-warm">
          <div class="step-no">04</div>
          <h3>Capture and replay</h3>
          <p>Write HR, ECG, ACC, and protocol logs to disk, then replay those sessions when you need deterministic review.</p>
        </div>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">What The App Gives You</h2>
      <p class="section-subtitle">This repo is most useful when you read it as an operator tool and a protocol reference at the same time.</p>
      <div class="card-grid feature-grid">
        <a class="path-card tone-cool" href="reference/app-overview.html">
          <div class="kicker">App surface</div>
          <h3>Operator overview</h3>
          <p>See how the WPF monitor is structured and what each major panel, tab, and diagnostic area is for.</p>
        </a>
        <a class="path-card tone-signal" href="reference/ui-preview.html">
          <div class="kicker">Live UI</div>
          <h3>Telemetry monitor</h3>
          <p>Review the current WPF visual system, chart treatment, and app shell before you dive into code.</p>
        </a>
        <a class="path-card tone-warm" href="reference/cli.html">
          <div class="kicker">Direct control</div>
          <h3>CLI diagnostics</h3>
          <p>Use scan, doctor, record, replay, and stream commands when you want a precise, scriptable path.</p>
        </a>
        <a class="path-card tone-violet" href="reference/protocol/overview.html">
          <div class="kicker">Wire format</div>
          <h3>Protocol reference</h3>
          <p>Cross-check services, control points, and frame formats once the app flow makes sense.</p>
        </a>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">System Maps</h2>
      <p class="section-subtitle">Use the diagrams after the app overview. They are there to explain roles, boundaries, and runtime paths, not to replace onboarding.</p>
      <div class="preview-grid">
        <a class="preview-card tone-violet" href="diagrams/viewer.html#repo-structure">
          <div class="meta">Repository structure</div>
          <h3>What belongs where</h3>
          <p>Use this when you need to understand how protocol, app, tooling, docs, and test projects are split.</p>
          <img src="diagrams/repo-structure.svg" alt="Repository structure diagram" />
        </a>
        <a class="preview-card tone-cool" href="diagrams/viewer.html#code-architecture">
          <div class="meta">Code architecture</div>
          <h3>How runtime roles fit together</h3>
          <p>Maps protocol decoders, Windows BLE transport, orchestration, recording, and the user-facing surfaces.</p>
          <img src="diagrams/code-architecture.svg" alt="Code architecture diagram" />
        </a>
        <a class="preview-card tone-warm" href="diagrams/viewer.html#session-lifecycle">
          <div class="meta">Runtime lifecycle</div>
          <h3>What a real session does</h3>
          <p>Follow the route from discovery to PMD start, live telemetry, recording, diagnostics, and teardown.</p>
          <img src="diagrams/session-lifecycle.svg" alt="Session lifecycle diagram" />
        </a>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Reference Paths</h2>
      <div class="card-grid">
        <a class="path-card tone-cool" href="reference/getting-started.html">
          <div class="kicker">Build + run</div>
          <h3>Getting Started</h3>
          <p>Environment prerequisites, first build, and live-device verification.</p>
        </a>
        <a class="path-card tone-signal" href="reference/app-overview.html">
          <div class="kicker">Operator model</div>
          <h3>App Overview</h3>
          <p>Start here if you need to understand what the app is doing before you read the lower-level docs.</p>
        </a>
        <a class="path-card tone-warm" href="reference/ui-preview.html">
          <div class="kicker">Visual system</div>
          <h3>WPF UI Preview</h3>
          <p>Review the current monitor shell, telemetry layout, and chart language in one page.</p>
        </a>
        <a class="path-card tone-violet" href="reference/cli.html">
          <div class="kicker">Command surface</div>
          <h3>CLI Reference</h3>
          <p>Scan, doctor, record, replay, and sessions command behavior.</p>
        </a>
      </div>
    </section>

    ${renderFooter()}
  </div>
</body>
</html>`;
}

function render404Page() {
  const topNav = renderTopNav('', 'index.html', 'reference/index.html', 'diagrams/viewer.html');
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>404 - Page Not Found | PolarH10</title>
  <meta name="description" content="This page could not be found." />
  <link rel="stylesheet" href="assets/site.css?v=${assetVersion}" />
</head>
<body>
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, 'index.html')}
    <section class="panel section-panel four-oh-four">
      <div>
        <h1>404</h1>
        <p>The page you are looking for does not exist or has been moved.</p>
        <div class="action-row" style="justify-content:center">
          <a class="button primary" href="index.html">Back to Home</a>
          <a class="button" href="reference/index.html">Browse Docs</a>
        </div>
      </div>
    </section>
    ${renderFooter()}
  </div>
</body>
</html>`;
}

function renderSidebar(currentRel) {
  const groups = docGroups.map((group) => {
    const items = group.items.map((item) => {
      const href = item.href ?? toReferenceHref(item.file, currentRel);
      const isActive = item.file === currentRel;
      return `<a class="${isActive ? 'nav-item active' : 'nav-item'}" href="${href}"><strong>${escapeHtml(item.label)}</strong><span>${escapeHtml(item.description)}</span></a>`;
    }).join('');

    return `<section><div class="category-label">${escapeHtml(group.title)}</div>${items}</section>`;
  }).join('');

  return `<h2>Documentation</h2><p>Read the app surfaces first, then move into protocol detail, diagnostics, and Mermaid system maps.</p>${groups}`;
}

function renderTopNav(activeKey, homeHref, docsHref, diagramsHref) {
  const items = [
    { key: 'home', label: 'Home', href: homeHref },
    { key: 'reference', label: 'Docs', href: docsHref },
    { key: 'diagrams', label: 'Diagrams', href: diagramsHref },
    { key: 'repo', label: 'GitHub', href: repoUrl }
  ];

  return items.map((item) => {
    const active = item.key === activeKey ? 'active' : '';
    return `<a class="${active}" href="${item.href}">${item.label}</a>`;
  }).join('');
}

function renderHeader(topNav, homeHref) {
  return `<header class="site-header">
    <a class="brand" href="${homeHref}">
      <span class="brand-mark" aria-hidden="true"></span>
      <span class="brand-copy"><small>Windows Telemetry Toolkit</small><strong>PolarH10</strong></span>
    </a>
    <nav class="top-nav" aria-label="Primary">${topNav}</nav>
  </header>`;
}

function renderFooter() {
  return `<footer class="footer">PolarH10 WPF monitor, CLI capture tooling, protocol docs, and Mermaid system maps. Unofficial reference build; not affiliated with Polar Electro Oy.</footer>`;
}

function renderArt() {
  return `<div class="page-art" aria-hidden="true">
    <span class="blob a"></span>
    <span class="blob b"></span>
    <span class="blob c"></span>
    <span class="blob d"></span>
    <span class="blob e"></span>
    <span class="blob f"></span>
    <span class="blob g"></span>
    <span class="blob h"></span>
  </div>`;
}

function toReferenceHref(file, currentRel) {
  const currentDir = path.posix.dirname(currentRel);
  const target = file.replace(/\.md$/i, '.html');
  let href = path.posix.relative(currentDir, target);
  if (!href) {
    href = path.posix.basename(target);
  }
  return href;
}

function extractTitle(markdown) {
  const match = markdown.match(/^#\s+(.+)$/m);
  return match ? match[1].trim() : null;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

async function collectMarkdownFiles(dir) {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    const rel = path.relative(docsRoot, fullPath).replace(/\\/g, '/');

    if (entry.isDirectory()) {
      if (rel === 'assets' || rel === 'diagrams') {
        continue;
      }
      files.push(...await collectMarkdownFiles(fullPath));
      continue;
    }

    if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) {
      files.push(fullPath);
    }
  }

  return files.sort();
}

async function copyDir(source, destination) {
  await fs.mkdir(destination, { recursive: true });
  const entries = await fs.readdir(source, { withFileTypes: true });

  for (const entry of entries) {
    const sourcePath = path.join(source, entry.name);
    const destinationPath = path.join(destination, entry.name);

    if (entry.isDirectory()) {
      await copyDir(sourcePath, destinationPath);
    } else {
      await fs.copyFile(sourcePath, destinationPath);
    }
  }
}
