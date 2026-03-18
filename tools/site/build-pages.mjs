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
const assetVersion = '20260319-brutal-site-2';

const docGroups = [
  {
    title: 'Start Here',
    items: [
      { file: 'index.md', label: 'Documentation Home', description: 'Overview of the reference set.' },
      { file: 'ui-preview.md', label: 'WPF UI Preview', description: 'Current brutal tDR-inspired redesign pass.' },
      { file: 'getting-started.md', label: 'Getting Started', description: 'Build, run, and verify the toolchain.' },
      { file: 'cli.md', label: 'CLI Reference', description: 'Scan, doctor, record, replay, and stream flows.' },
      { href: '../diagrams/viewer.html', label: 'Diagram Viewer', description: 'Architecture and repo maps rendered from Mermaid.' }
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
    pageIntro: title === 'Polar H10 Direct - Documentation'
      ? 'Reference material for the PolarH10 connector, organized for direct browsing on GitHub Pages.'
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
        <div class="eyebrow">GitHub Pages Reference</div>
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
  <title>PolarH10 GitHub Pages</title>
  <meta name="description" content="Protocol-first Polar H10 tooling, diagrams, and documentation for GitHub Pages." />
  <link rel="stylesheet" href="assets/site.css?v=${assetVersion}" />
</head>
<body>
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, 'index.html')}
    <section class="hero">
      <div class="panel hero-copy">
        <div class="eyebrow">GitHub Pages Site</div>
        <h1>PolarH10<br />stack, docs,<br />and diagrams.</h1>
        <p>
          A Pages-ready front door for the PolarH10 repository: protocol references,
          subsystem maps, Mermaid diagrams, and the fastest path into the CLI,
          transport layer, and WPF reference app.
        </p>
        <div class="action-row">
          <a class="button primary" href="reference/ui-preview.html">Open UI Preview</a>
          <a class="button primary" href="reference/index.html">Browse Docs</a>
          <a class="button" href="diagrams/viewer.html">Open Diagram Viewer</a>
          <a class="button" href="${repoUrl}">View Repository</a>
        </div>
        <div class="stats">
          <div class="stat"><strong>5</strong><span>runtime projects in src/</span></div>
          <div class="stat"><strong>91</strong><span>tests passing in the current suite</span></div>
          <div class="stat"><strong>3</strong><span>Mermaid diagrams shipped into Pages</span></div>
        </div>
      </div>
      <aside class="panel hero-aside">
        <div class="eyebrow">What is here</div>
        <ul class="note-list">
          <li>
            <strong>Repo navigation</strong>
            <p>Jump from the landing page into docs, diagrams, protocol reference, and implementation boundaries.</p>
          </li>
          <li>
            <strong>Architecture at subsystem level</strong>
            <p>The Mermaid set stays focused on transport, session flow, recording, and UI consumers instead of exploding into file-by-file diagrams.</p>
          </li>
          <li>
            <strong>Pages deployment</strong>
            <p>GitHub Actions builds the diagrams, converts markdown into HTML, and deploys the generated static site.</p>
          </li>
        </ul>
      </aside>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Repository Paths</h2>
      <p class="section-subtitle">The codebase is split cleanly between protocol decoding, Windows BLE transport, capture tooling, and the two user-facing surfaces.</p>
      <div class="card-grid">
        <div class="path-card">
          <div class="kicker">src/PolarH10.Protocol</div>
          <h3>Protocol core</h3>
          <p>Pure C# decoders, PMD builders, session export, and capture manifests.</p>
        </div>
        <div class="path-card">
          <div class="kicker">src/PolarH10.Transport.Windows</div>
          <h3>Windows BLE stack</h3>
          <p>Scanner, GATT connection, session orchestration, and multi-device coordination.</p>
        </div>
        <div class="path-card">
          <div class="kicker">src/PolarH10.Cli + src/PolarH10.App</div>
          <h3>Operator surfaces</h3>
          <p>CLI commands for diagnostics and recording, plus the WPF reference monitor.</p>
        </div>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Diagram Previews</h2>
      <p class="section-subtitle">The Pages site publishes the same Mermaid sources used in the repo, with a manifest-driven viewer for direct browsing.</p>
      <div class="preview-grid">
        <a class="preview-card" href="diagrams/viewer.html#repo-structure">
          <div class="meta">Repository structure</div>
          <h3>Top-level code and docs layout</h3>
          <p>Maps the repo across source, tests, docs, tools, and samples.</p>
          <img src="diagrams/repo-structure.svg" alt="Repository structure diagram" />
        </a>
        <a class="preview-card" href="diagrams/viewer.html#code-architecture">
          <div class="meta">Code architecture</div>
          <h3>Protocol to transport to consumers</h3>
          <p>Shows how decoders, BLE transport, sessions, recording, CLI, and WPF fit together.</p>
          <img src="diagrams/code-architecture.svg" alt="Code architecture diagram" />
        </a>
        <a class="preview-card" href="diagrams/viewer.html#session-lifecycle">
          <div class="meta">Runtime lifecycle</div>
          <h3>Operator flow from scan to shutdown</h3>
          <p>Shows the link sequence from discovery through PMD start, recording, and teardown.</p>
          <img src="diagrams/session-lifecycle.svg" alt="Session lifecycle diagram" />
        </a>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Start Navigating</h2>
      <div class="card-grid">
        <a class="path-card" href="reference/getting-started.html">
          <div class="kicker">Build + run</div>
          <h3>Getting Started</h3>
          <p>Environment prerequisites, first build, and live-device verification.</p>
        </a>
        <a class="path-card" href="reference/ui-preview.html">
          <div class="kicker">Design preview</div>
          <h3>WPF UI Preview</h3>
          <p>Review the brutal tDR-inspired monitor redesign before deciding on further iteration.</p>
        </a>
        <a class="path-card" href="reference/cli.html">
          <div class="kicker">Command surface</div>
          <h3>CLI Reference</h3>
          <p>Scan, doctor, record, replay, and sessions command behavior.</p>
        </a>
        <a class="path-card" href="reference/protocol/overview.html">
          <div class="kicker">Wire format</div>
          <h3>Protocol Overview</h3>
          <p>Polar services, PMD control flow, and signal frame decoding.</p>
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

  return `<h2>Documentation</h2><p>Browse the generated Pages version of the repo docs and jump directly into the Mermaid viewer.</p>${groups}`;
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
      <span class="brand-copy"><small>Unofficial Reference</small><strong>PolarH10</strong></span>
    </a>
    <nav class="top-nav" aria-label="Primary">${topNav}</nav>
  </header>`;
}

function renderFooter() {
  return `<footer class="footer">Built from the repo docs, Mermaid sources, and generated SVG output for GitHub Pages.</footer>`;
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
