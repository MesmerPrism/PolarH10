import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import katex from 'katex';
import { marked } from 'marked';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const docsRoot = path.join(repoRoot, 'docs');
const siteRoot = path.join(repoRoot, 'site');
const referenceRoot = path.join(siteRoot, 'reference');
const assetsSource = path.join(docsRoot, 'assets');
const referenceMarkdownRoot = path.join(siteRoot, 'assets', 'reference-markdown');
const diagramsSource = path.join(docsRoot, 'diagrams');
const diagramManifestPath = path.join(diagramsSource, 'manifest.json');
const katexDistSource = path.join(repoRoot, 'node_modules', 'katex', 'dist');
const assetVersion = '20260321-pages-16';
const searchPagePath = 'reference/search.html';

const siteConfig = {
  repoUrl: 'https://github.com/MesmerPrism/PolarH10',
  baseUrl: 'https://mesmerprism.github.io/PolarH10/',
  siteName: 'PolarH10',
  homeTitle: 'PolarH10 Unofficial Open-Source Telemetry Toolkit',
  referenceTitle: 'PolarH10 Unofficial Open-Source Reference',
  sharedPromise: 'Use a Polar H10 on Windows without the Polar SDK. Scan nearby straps, inspect live HR, ECG, and ACC data, review RR-derived coherence, short-term HRV, and breathing-dynamics entropy, compare multiple active straps, and record reusable sessions from a WPF app or CLI.',
  defaultDescription: 'Unofficial open-source PolarH10 docs, onboarding guides, protocol reference, and Mermaid system diagrams. Not endorsed by or affiliated with Polar Electro Oy.',
  socialImage: 'assets/brutal-tdr-preview.png',
  favicon: 'assets/polarh10-stripe-mark.png',
  themeColor: '#f3eee6',
  navGroups: ['Start Here', 'Task Guides', 'Troubleshooting', 'Internals'],
  diagramViewerLabel: 'Diagram Viewer',
  diagramViewerDescription: 'Browse onboarding, runtime, and architecture Mermaid diagrams.',
  searchTitle: 'Search the PolarH10 reference',
  searchDescription: 'Find the page that explains a term, metric, command, file format, workflow, or diagram topic across the published GitHub Pages site.'
};

const specialNavItems = [
  {
    group: 'Internals',
    order: 999,
    label: siteConfig.diagramViewerLabel,
    description: siteConfig.diagramViewerDescription,
    target: 'diagrams/viewer.html'
  }
];

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function main() {
  const docs = await loadDocs();
  const diagramManifest = await loadDiagramManifest();

  await fs.rm(siteRoot, { recursive: true, force: true });
  await fs.mkdir(referenceRoot, { recursive: true });
  await fs.mkdir(referenceMarkdownRoot, { recursive: true });
  await copyDir(assetsSource, path.join(siteRoot, 'assets'));
  await copyDir(katexDistSource, path.join(siteRoot, 'assets', 'vendor', 'katex'));
  await copyDir(diagramsSource, path.join(siteRoot, 'diagrams'));
  await fs.writeFile(path.join(siteRoot, '.nojekyll'), '', 'utf8');
  await fs.writeFile(
    path.join(siteRoot, 'diagrams', 'manifest.json'),
    JSON.stringify(transformDiagramManifestForSite(diagramManifest), null, 2),
    'utf8'
  );
  await finalizeDiagramViewerPage(diagramManifest);

  for (const doc of docs) {
    const html = renderMarkdown(doc.renderBody, doc.sourceRel);
    const outPath = path.join(siteRoot, doc.outRel);
    await fs.mkdir(path.dirname(outPath), { recursive: true });
    await fs.writeFile(outPath, renderDocPage(doc, docs), 'utf8');
    const downloadMarkdownPath = path.join(referenceMarkdownRoot, doc.sourceRel);
    await fs.mkdir(path.dirname(downloadMarkdownPath), { recursive: true });
    await fs.writeFile(downloadMarkdownPath, doc.downloadMarkdown, 'utf8');
  }

  await fs.writeFile(path.join(siteRoot, 'index.html'), renderHomePage(), 'utf8');
  await fs.writeFile(path.join(referenceRoot, 'search.html'), renderSearchPage(), 'utf8');
  await fs.writeFile(path.join(siteRoot, '404.html'), render404Page(), 'utf8');
  await fs.writeFile(path.join(siteRoot, 'site.webmanifest'), JSON.stringify(renderWebManifest(), null, 2), 'utf8');
  await fs.writeFile(path.join(siteRoot, 'sitemap.xml'), renderSitemap(docs), 'utf8');
  await fs.writeFile(path.join(siteRoot, 'robots.txt'), renderRobotsTxt(), 'utf8');

  console.log(`Built GitHub Pages site at ${siteRoot}`);
}

async function loadDocs() {
  const markdownFiles = await collectMarkdownFiles(docsRoot);
  const docs = [];

  for (const filePath of markdownFiles) {
    const sourceRel = path.relative(docsRoot, filePath).replace(/\\/g, '/');
    const raw = await fs.readFile(filePath, 'utf8');
    const stat = await fs.stat(filePath);
    const { data, body } = parseFrontmatter(raw);
    const { heading, markdown } = stripLeadingH1(body);
    const title = heading ?? normalizeString(data.title) ?? deriveTitle(sourceRel);
    const summary = normalizeString(data.summary);
    const description = normalizeString(data.description) ?? summary ?? siteConfig.defaultDescription;
    const navLabel = normalizeString(data.nav_label) ?? title;
    const navGroup = toBoolean(data.hide_in_nav)
      ? null
      : normalizeString(data.nav_group) ?? inferNavGroup(sourceRel);
    const navOrder = toNumber(data.nav_order) ?? inferNavOrder(sourceRel);
    const hasMath = containsMath(markdown);

    docs.push({
      sourceRel,
      outRel: `reference/${sourceRel.replace(/\.md$/i, '.html')}`,
      title,
      summary,
      description,
      navLabel,
      navGroup,
      navOrder,
      hasMath,
      downloadMarkdown: buildDownloadMarkdown(body, heading, title),
      renderBody: markdown.trimStart(),
      updatedAt: stat.mtime.toISOString()
    });
  }

  return docs.sort((left, right) => {
    const leftGroup = groupRank(left.navGroup);
    const rightGroup = groupRank(right.navGroup);
    if (leftGroup !== rightGroup) {
      return leftGroup - rightGroup;
    }

    if (left.navOrder !== right.navOrder) {
      return left.navOrder - right.navOrder;
    }

    return left.navLabel.localeCompare(right.navLabel);
  });
}

async function loadDiagramManifest() {
  const raw = await fs.readFile(diagramManifestPath, 'utf8');
  return JSON.parse(raw);
}

async function finalizeDiagramViewerPage(diagramManifest) {
  const viewerPath = path.join(siteRoot, 'diagrams', 'viewer.html');
  const raw = await fs.readFile(viewerPath, 'utf8');
  const head = renderHead({
    title: `${siteConfig.diagramViewerLabel} | ${siteConfig.referenceTitle}`,
    description: siteConfig.diagramViewerDescription,
    currentDir: 'diagrams',
    canonicalPath: 'diagrams/viewer.html'
  });

  const withHead = raw.replace(/<head>[\s\S]*?<\/head>/i, `<head>\n${head}\n</head>`);
  const withSearchNav = withHead.replace(
    /<a href="\.\.\/reference\/index\.html">Docs<\/a>\s*<a class="active" href="viewer\.html">Diagrams<\/a>/i,
    `<a href="../reference/index.html">Docs</a>
        <a href="../reference/search.html">Search</a>
        <a class="active" href="viewer.html">Diagrams</a>`
  );
  const withSearchableShell = withSearchNav.replace(
    /<section class="panel viewer-shell">/i,
    '<section class="panel viewer-shell" data-pagefind-body>'
  ).replace(
    /<h1 class="page-title">/i,
    '<h1 class="page-title" data-pagefind-meta="title">'
  ).replace(
    /<div id="nav-list"><\/div>/i,
    `<div id="nav-list">${renderStaticDiagramNav(diagramManifest)}</div>`
  );
  await fs.writeFile(viewerPath, withSearchableShell, 'utf8');
}

function transformDiagramManifestForSite(manifest) {
  return {
    ...manifest,
    diagrams: manifest.diagrams.map((diagram) => ({
      ...diagram,
      relatedDocs: Array.isArray(diagram.relatedDocs)
        ? diagram.relatedDocs.map(toBuiltDocHrefFromDiagram)
        : []
    }))
  };
}

function toBuiltDocHrefFromDiagram(relativeDocPath) {
  const normalized = path.posix.normalize(path.posix.join('diagrams', relativeDocPath));
  const docPath = normalized.replace(/^diagrams\//, '');
  const target = `reference/${docPath.replace(/\.md$/i, '.html')}`;
  return path.posix.relative('diagrams', target) || path.posix.basename(target);
}

function renderDocPage(doc, docs) {
  const currentDir = path.posix.dirname(doc.outRel);
  const asset = createAssetHelper(currentDir);
  const topNav = renderTopNav('reference', asset('index.html'), asset('reference/index.html'), asset(searchPagePath), asset('diagrams/viewer.html'));
  const sidebar = renderSidebar(doc, docs);
  const articleHtml = renderMarkdown(doc.renderBody, doc.sourceRel);

  return `<!DOCTYPE html>
<html lang="en">
<head>
${renderHead({
  title: `${doc.title} | ${siteConfig.referenceTitle}`,
  description: doc.description,
  currentDir,
  canonicalPath: doc.outRel,
  includeSearch: true,
  includeMathStyles: doc.hasMath,
  updatedAt: doc.updatedAt
})}
</head>
<body class="doc-page">
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, asset('index.html'))}
    <div class="page-layout">
      <aside class="panel sidebar" data-pagefind-ignore>
        ${sidebar}
      </aside>
      <main class="panel content-panel">
        <div class="page-marker">Unofficial Open-Source Reference</div>
        <h1 class="page-title" data-pagefind-meta="title">${escapeHtml(doc.title)}</h1>
        ${doc.summary ? `<p class="page-intro">${escapeHtml(doc.summary)}</p>` : ''}
        <article class="prose" data-pagefind-body>
          ${articleHtml}
        </article>
      </main>
    </div>
    ${renderFooter()}
  </div>
  ${renderSearchBoot(asset)}
</body>
</html>`;
}

function renderHomePage() {
  const asset = createAssetHelper('.');
  const topNav = renderTopNav('home', 'index.html', 'reference/index.html', searchPagePath, 'diagrams/viewer.html');

  return `<!DOCTYPE html>
<html lang="en">
<head>
${renderHead({
  title: siteConfig.homeTitle,
  description: siteConfig.sharedPromise,
  currentDir: '.',
  canonicalPath: 'index.html',
  includeSearch: true
})}
</head>
<body>
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, 'index.html')}
    <section class="section panel section-panel search-section" data-pagefind-ignore>
      <div class="page-marker">Site Search</div>
      <h2 class="section-heading">Search across guides, formulas, protocol notes, and diagrams.</h2>
      <p class="section-subtitle">Type a term like <code>coherence</code>, <code>RMSSD</code>, <code>ECG</code>, <code>ACC</code>, <code>doctor</code>, or <code>entropy</code> and open the page that explains it.</p>
      ${renderSearchPanel({
        title: 'Search the published site',
        description: 'Look up commands, metrics, formats, workflows, and diagram topics without stepping through the nav tree.',
        standalone: true
      })}
    </section>

    <main data-pagefind-body>
    <section class="hero hero-home">
      <div class="panel hero-copy tone-dark">
        <div class="page-marker">Windows-first Polar H10 toolkit</div>
        <h1>Scan.<br />Link.<br />Inspect.<br />Record.</h1>
        <p>${escapeHtml(siteConfig.sharedPromise)}</p>
        <div class="action-row">
          <a class="button primary" href="reference/app-overview.html">Use the WPF app</a>
          <a class="button primary" href="reference/cli.html">Use the CLI</a>
          <a class="button" href="reference/first-recording.html">First recording</a>
          <a class="button" href="reference/protocol/overview.html">Protocol guide</a>
        </div>
      </div>
      <aside class="panel hero-preview">
        <h2 class="section-heading">Choose your path</h2>
        <ul class="note-list feature-list">
          <li>
            <strong>WPF operator flow</strong>
            <p>Scan nearby straps, connect one or more devices, inspect live telemetry, review coherence, short-term HRV, or breathing-dynamics entropy when ready, and capture a reusable session from the desktop app.</p>
          </li>
          <li>
            <strong>CLI diagnostics</strong>
            <p>Run <code>scan</code>, <code>doctor</code>, <code>monitor</code>, <code>record</code>, <code>replay</code>, and <code>sessions</code> when you want a direct, scriptable path without the UI.</p>
          </li>
          <li>
            <strong>Library + protocol study</strong>
            <p>Use the protocol reference, diagrams, and transport notes when you need to understand PMD, GATT, decoding, and recording internals.</p>
          </li>
        </ul>
        <img src="assets/brutal-tdr-preview.png" alt="PolarH10 WPF application preview" />
      </aside>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">First Session Path</h2>
      <p class="section-subtitle">The shortest route from a strap on your desk to saved telemetry on Windows.</p>
      <div class="step-grid">
        <div class="step-card tone-cool">
          <div class="step-no">01</div>
          <h3>Scan nearby straps</h3>
          <p>Find the intended H10, confirm the Bluetooth address, and decide whether you want the WPF app or the CLI for the session.</p>
        </div>
        <div class="step-card tone-violet">
          <div class="step-no">02</div>
          <h3>Connect and validate</h3>
          <p>Open the BLE/GATT link, confirm HR plus ACC are live, and use the diagnostics path before you trust a long capture.</p>
        </div>
        <div class="step-card tone-signal">
          <div class="step-no">03</div>
          <h3>Inspect the live stream</h3>
          <p>Check HR, ECG, and ACC in the app or terminal, open coherence or HRV once RR is stable, and open breathing dynamics only after breathing calibration is already live.</p>
        </div>
        <div class="step-card tone-warm">
          <div class="step-no">04</div>
          <h3>Record and replay</h3>
          <p>Write <code>session.json</code>, CSV sensor output, and <code>protocol.jsonl</code>, then replay or review the capture without hardware attached.</p>
        </div>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Docs That Matter First</h2>
      <div class="card-grid">
        <a class="path-card tone-cool" href="reference/getting-started.html">
          <h3>Getting Started</h3>
          <p>Real clone URL, prerequisites, first build, and the safest path to a successful local run.</p>
        </a>
        <a class="path-card tone-cool" href="reference/first-recording.html">
          <h3>First Recording</h3>
          <p>The first end-to-end WPF and CLI session, including what to save and how to verify the result.</p>
        </a>
        <a class="path-card tone-cool" href="reference/coherence-workflow.html">
          <h3>Coherence Workflow</h3>
          <p>Use the RR-derived coherence window, understand the warmup phase, and read confidence instead of trusting a raw number too early.</p>
        </a>
        <a class="path-card tone-cool" href="reference/hrv-workflow.html">
          <h3>HRV Workflow</h3>
          <p>Use the short-term HRV tab, let the RR window fill, and read RMSSD with SDNN and pNN50 instead of assuming a five-minute solve is instant.</p>
        </a>
        <a class="path-card tone-violet" href="reference/breathing-dynamics-workflow.html">
          <h3>Breathing Dynamics Workflow</h3>
          <p>Use the dedicated interval and amplitude entropy window once breathing calibration is already stable.</p>
        </a>
        <a class="path-card tone-signal" href="reference/formula-sheets.html">
          <h3>Formula Sheets</h3>
          <p>Download the Markdown and PDF method sheets for coherence, HRV, breathing from ACC, and breathing-dynamics entropy.</p>
        </a>
        <a class="path-card tone-violet" href="reference/output-formats.html">
          <h3>Output Formats</h3>
          <p>What each capture file contains, how session folders are named, and when <code>run.json</code> appears.</p>
        </a>
        <a class="path-card tone-warm" href="reference/troubleshooting.html">
          <h3>Troubleshooting</h3>
          <p>Fix the common failure cases first: hidden devices, Windows BLE access issues, stale streams, and blocked app launch.</p>
        </a>
        <a class="path-card tone-signal" href="reference/cli.html">
          <h3>CLI Guide</h3>
          <p>Command-focused workflows for scanning, recording, replaying, and doctor-style validation.</p>
        </a>
        <a class="path-card tone-violet" href="reference/protocol/overview.html">
          <h3>Protocol Internals</h3>
          <p>PMD service layout, measurement formats, and lower-level notes once the operator path already makes sense.</p>
        </a>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Diagram Viewer</h2>
      <p class="section-subtitle">Use the onboarding diagrams first, then move into the runtime and architecture maps when you need deeper internals.</p>
      <div class="preview-grid">
        <a class="preview-card tone-cool" href="diagrams/viewer.html#choose-your-path">
          <div class="meta">Onboarding</div>
          <h3>Choose your path</h3>
          <p>Pick the WPF, CLI, or protocol route based on whether you need raw telemetry only or the derived coherence, HRV, and entropy views.</p>
          <img src="diagrams/choose-your-path.svg" alt="Choose your path diagram" />
        </a>
        <a class="preview-card tone-warm" href="diagrams/viewer.html#first-session-flow">
          <div class="meta">Onboarding</div>
          <h3>First session flow</h3>
          <p>See the scan, connect, inspect, record, and replay loop before you drop into code or protocol details.</p>
          <img src="diagrams/first-session-flow.svg" alt="First session flow diagram" />
        </a>
        <a class="preview-card tone-violet" href="diagrams/viewer.html#code-architecture">
          <div class="meta">Architecture</div>
          <h3>Code architecture</h3>
          <p>Map protocol decoders, Windows BLE transport, orchestration, recording, and the operator surfaces.</p>
          <img src="diagrams/code-architecture.svg" alt="Code architecture diagram" />
        </a>
      </div>
    </section>
    </main>

    ${renderFooter()}
  </div>
  ${renderSearchBoot(asset)}
</body>
</html>`;
}

function renderSearchPage() {
  const currentDir = 'reference';
  const asset = createAssetHelper(currentDir);
  const topNav = renderTopNav('search', asset('index.html'), asset('reference/index.html'), asset(searchPagePath), asset('diagrams/viewer.html'));

  return `<!DOCTYPE html>
<html lang="en">
<head>
${renderHead({
  title: `${siteConfig.searchTitle} | ${siteConfig.referenceTitle}`,
  description: siteConfig.searchDescription,
  currentDir,
  canonicalPath: searchPagePath,
  includeSearch: true
})}
</head>
<body class="doc-page">
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, asset('index.html'))}
    <section class="panel section-panel search-section">
      <div class="page-marker">Search the published site</div>
      <h1 class="page-title">Find where a term is documented.</h1>
      <p class="page-intro">Search the public Pages build for commands, formulas, telemetry terms, protocol notes, troubleshooting steps, and diagram topics. Results take you to the page where that topic is explained.</p>
      ${renderSearchPanel({
        title: 'Search all published pages',
        description: 'Try terms like coherence, RMSSD, entropy, ECG, ACC, doctor, PMD, or replay.',
        standalone: true,
        autofocus: true,
        queryParam: 'q'
      })}
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">What Search Covers</h2>
      <div class="card-grid">
        <a class="path-card tone-cool" href="index.html">
          <h3>Onboarding and workflows</h3>
          <p>Getting started, first recording, coherence, HRV, breathing, troubleshooting, and output-format guides.</p>
        </a>
        <a class="path-card tone-signal" href="formula-sheets.html">
          <h3>Formula sheets</h3>
          <p>Searchable explanations for coherence, HRV, breathing from ACC, and breathing-dynamics metrics.</p>
        </a>
        <a class="path-card tone-violet" href="../diagrams/viewer.html">
          <h3>Diagram topics</h3>
          <p>Manifest-backed onboarding, runtime, and architecture diagrams are discoverable through the same site search.</p>
        </a>
      </div>
    </section>

    ${renderFooter()}
  </div>
  ${renderSearchBoot(asset)}
</body>
</html>`;
}

function render404Page() {
  const topNav = renderTopNav('', 'index.html', 'reference/index.html', searchPagePath, 'diagrams/viewer.html');

  return `<!DOCTYPE html>
<html lang="en">
<head>
${renderHead({
  title: `404 | ${siteConfig.referenceTitle}`,
  description: 'This unofficial PolarH10 reference page could not be found.',
  currentDir: '.',
  canonicalPath: '404.html',
  noIndex: true
})}
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
          <a class="button" href="reference/search.html">Search the Site</a>
        </div>
      </div>
    </section>
    ${renderFooter()}
  </div>
</body>
</html>`;
}

function renderHead({ title, description, currentDir, canonicalPath, includeSearch = false, includeMathStyles = false, updatedAt = null, noIndex = false }) {
  const asset = createAssetHelper(currentDir);
  const canonicalUrl = absoluteUrl(canonicalPath);
  const socialImage = absoluteUrl(siteConfig.socialImage);

  return `  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${escapeHtml(title)}</title>
  <meta name="description" content="${escapeHtml(description)}" />
  <meta name="theme-color" content="${siteConfig.themeColor}" />
  ${noIndex ? '<meta name="robots" content="noindex" />' : ''}
  ${updatedAt ? `<meta property="article:modified_time" content="${escapeHtml(updatedAt)}" />` : ''}
  <link rel="canonical" href="${escapeHtml(canonicalUrl)}" />
  <link rel="icon" href="${asset(siteConfig.favicon)}" />
  <link rel="apple-touch-icon" href="${asset(siteConfig.favicon)}" />
  <link rel="manifest" href="${asset('site.webmanifest')}" />
  <meta property="og:type" content="website" />
  <meta property="og:site_name" content="${escapeHtml(siteConfig.siteName)}" />
  <meta property="og:title" content="${escapeHtml(title)}" />
  <meta property="og:description" content="${escapeHtml(description)}" />
  <meta property="og:url" content="${escapeHtml(canonicalUrl)}" />
  <meta property="og:image" content="${escapeHtml(socialImage)}" />
  <meta name="twitter:card" content="summary_large_image" />
  <meta name="twitter:title" content="${escapeHtml(title)}" />
  <meta name="twitter:description" content="${escapeHtml(description)}" />
  <meta name="twitter:image" content="${escapeHtml(socialImage)}" />
  <link rel="stylesheet" href="${asset('assets/site.css')}?v=${assetVersion}" />
  ${includeMathStyles ? `<link rel="stylesheet" href="${asset('assets/vendor/katex/katex.min.css')}?v=${assetVersion}" />` : ''}
  ${includeSearch ? `<link rel="stylesheet" href="${asset('pagefind/pagefind-ui.css')}" />` : ''}`;
}

function renderSidebar(currentDoc, docs) {
  const groups = buildNavGroups(currentDoc, docs).map((group) => {
    const items = group.items.map((item) => {
      const isActive = item.isActive ? ' active' : '';
      return `<a class="nav-item${isActive}" href="${escapeHtml(item.href)}"><strong>${escapeHtml(item.label)}</strong><span>${escapeHtml(item.description)}</span></a>`;
    }).join('');

    return `<section><h2 class="category-heading">${escapeHtml(group.title)}</h2>${items}</section>`;
  }).join('');

  return `${renderSearchPanel({
    title: 'Search docs',
    description: 'Find commands, formulas, telemetry terms, protocol notes, and diagram topics without digging through the repo tree.'
  })}
  ${groups}`;
}

function buildNavGroups(currentDoc, docs) {
  const currentDir = path.posix.dirname(currentDoc.outRel);
  const groups = [];

  for (const groupName of siteConfig.navGroups) {
    const docItems = docs
      .filter((doc) => doc.navGroup === groupName)
      .map((doc) => ({
        label: doc.navLabel,
        description: doc.description,
        href: relativeHref(currentDir, doc.outRel),
        order: doc.navOrder,
        isActive: doc.sourceRel === currentDoc.sourceRel
      }));

    const extraItems = specialNavItems
      .filter((item) => item.group === groupName)
      .map((item) => ({
        label: item.label,
        description: item.description,
        href: relativeHref(currentDir, item.target),
        order: item.order,
        isActive: false
      }));

    const items = [...docItems, ...extraItems].sort((left, right) => {
      if (left.order !== right.order) {
        return left.order - right.order;
      }
      return left.label.localeCompare(right.label);
    });

    if (items.length > 0) {
      groups.push({ title: groupName, items });
    }
  }

  return groups;
}

function renderTopNav(activeKey, homeHref, docsHref, searchHref, diagramsHref) {
  const items = [
    { key: 'home', label: 'Home', href: homeHref },
    { key: 'reference', label: 'Docs', href: docsHref },
    { key: 'search', label: 'Search', href: searchHref },
    { key: 'diagrams', label: 'Diagrams', href: diagramsHref },
    { key: 'repo', label: 'GitHub', href: siteConfig.repoUrl }
  ];

  return items.map((item) => {
    const active = item.key === activeKey ? 'active' : '';
    return `<a class="${active}" href="${item.href}">${item.label}</a>`;
  }).join('');
}

function renderHeader(topNav, homeHref) {
  return `<header class="site-header" data-pagefind-ignore>
    <a class="brand" href="${homeHref}">
      <span class="brand-mark" aria-hidden="true"></span>
      <span class="brand-copy"><strong>${siteConfig.siteName}</strong><span>Unofficial Open-Source Toolkit</span></span>
    </a>
    <nav class="top-nav" aria-label="Primary">${topNav}</nav>
  </header>`;
}

function renderFooter() {
  return `<footer class="footer" data-pagefind-ignore>PolarH10 WPF monitor, CLI capture tooling, protocol docs, and Mermaid system maps. Unofficial open-source reference build; not endorsed by or affiliated with Polar Electro Oy.</footer>`;
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

function renderSearchPanel({ title, description, standalone = false, autofocus = false, queryParam = '' }) {
  const className = standalone ? 'search-panel search-panel-standalone' : 'search-panel';
  const attrs = [];
  if (autofocus) {
    attrs.push('data-search-autofocus="true"');
  }
  if (queryParam) {
    attrs.push(`data-search-query-param="${escapeHtml(queryParam)}"`);
  }

  return `<section class="${className}">
    <h2>${escapeHtml(title)}</h2>
    <p>${escapeHtml(description)}</p>
    <div id="pagefind-search"${attrs.length ? ` ${attrs.join(' ')}` : ''}></div>
  </section>`;
}

function renderSearchBoot(asset) {
  const jsHref = asset('pagefind/pagefind-ui.js');

  return `<script src="${jsHref}"></script>
<script>
  window.addEventListener('DOMContentLoaded', () => {
    const mount = document.getElementById('pagefind-search');
    if (!mount || typeof window.PagefindUI !== 'function') {
      return;
    }

    new window.PagefindUI({
      element: '#pagefind-search',
      showImages: false,
      resetStyles: false,
      excerptLength: 18,
      showSubResults: true
    });

    const getInput = () => mount.querySelector('.pagefind-ui__search-input');
    const queryKey = mount.dataset.searchQueryParam;
    let initialQuery = '';

    requestAnimationFrame(() => {
      const input = getInput();
      if (!input) {
        return;
      }

      if (queryKey) {
        const value = new URLSearchParams(window.location.search).get(queryKey);
        initialQuery = value ? value.trim() : '';
        if (initialQuery) {
          input.value = initialQuery;
          input.dispatchEvent(new Event('input', { bubbles: true }));
        }
      }

      if (mount.dataset.searchAutofocus === 'true' || initialQuery) {
        input.focus({ preventScroll: true });
      }
    });

    document.addEventListener('keydown', (event) => {
      const input = getInput();
      if (!input) {
        return;
      }

      const target = event.target;
      const tagName = target && target.tagName ? target.tagName.toLowerCase() : '';
      const isEditable = Boolean(target && (target.isContentEditable || tagName === 'input' || tagName === 'textarea' || tagName === 'select'));
      if (isEditable) {
        return;
      }

      const key = event.key ? event.key.toLowerCase() : '';
      const slashShortcut = event.key === '/';
      const paletteShortcut = key === 'k' && (event.ctrlKey || event.metaKey);
      if (!slashShortcut && !paletteShortcut) {
        return;
      }

      event.preventDefault();
      input.focus({ preventScroll: true });
      if (input.value) {
        input.select();
      }
    });
  });
</script>`;
}

function renderWebManifest() {
  return {
    name: siteConfig.homeTitle,
    short_name: siteConfig.siteName,
    start_url: `${siteConfig.baseUrl}index.html`,
    display: 'standalone',
    background_color: siteConfig.themeColor,
    theme_color: siteConfig.themeColor,
    icons: [
      {
        src: absoluteUrl(siteConfig.favicon),
        sizes: '512x512',
        type: 'image/png'
      }
    ]
  };
}

function renderSitemap(docs) {
  const pages = [
    { path: 'index.html', lastmod: new Date().toISOString() },
    { path: 'reference/index.html', lastmod: docs.find((doc) => doc.sourceRel === 'index.md')?.updatedAt ?? new Date().toISOString() },
    { path: searchPagePath, lastmod: new Date().toISOString() },
    { path: 'diagrams/viewer.html', lastmod: new Date().toISOString() },
    ...docs.map((doc) => ({ path: doc.outRel, lastmod: doc.updatedAt }))
  ];

  const uniquePages = dedupeBy(pages, (page) => page.path);
  const urls = uniquePages.map((page) => `  <url>
    <loc>${escapeHtml(absoluteUrl(page.path))}</loc>
    <lastmod>${escapeHtml(page.lastmod)}</lastmod>
  </url>`).join('\n');

  return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${urls}
</urlset>`;
}

function renderRobotsTxt() {
  return `User-agent: *
Allow: /
Sitemap: ${absoluteUrl('sitemap.xml')}
`;
}

function renderMarkdown(markdown, sourceRel) {
  const renderer = new marked.Renderer();
  const defaultCode = renderer.code.bind(renderer);
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
  renderer.code = (token) => {
    const lang = normalizeString(token.lang)?.toLowerCase();
    if (lang === 'latex') {
      return renderLatexBlock(token.text, sourceRel);
    }

    return defaultCode(token);
  };

  const html = marked.parse(markdown, {
    gfm: true,
    breaks: false,
    renderer
  });

  return rewriteRelativeHtmlAttributes(html, sourceRel);
}

function rewriteHref(href, sourceRel) {
  if (/^(https?:|mailto:|tel:|#)/i.test(href)) {
    return href;
  }

  const [rawPath, rawHash] = href.split('#');
  const hash = rawHash ? `#${rawHash}` : '';
  const sourceDir = path.posix.dirname(sourceRel);
  const resolved = path.posix.normalize(path.posix.join(sourceDir, rawPath));

  if (resolved.startsWith('assets/') || resolved.startsWith('diagrams/')) {
    return relativeHref(`reference/${sourceDir}`, resolved) + hash;
  }

  if (resolved.endsWith('.md')) {
    const target = `reference/${resolved.replace(/\.md$/i, '.html')}`;
    return relativeHref(`reference/${sourceDir}`, target) + hash;
  }

  return href + hash;
}

function rewriteRelativeHtmlAttributes(html, sourceRel) {
  return html.replace(/(\b(?:href|src))=(["'])([^"']+)\2/gi, (match, attribute, quote, value) => {
    const rewritten = rewriteHref(value, sourceRel);
    return `${attribute}=${quote}${escapeHtml(rewritten)}${quote}`;
  });
}

function renderStaticDiagramNav(manifest) {
  const groups = new Map();

  for (const diagram of manifest.diagrams) {
    const category = normalizeString(diagram.category) ?? 'other';
    if (!groups.has(category)) {
      groups.set(category, []);
    }
    groups.get(category).push(diagram);
  }

  return [...groups.entries()].map(([category, diagrams]) => {
    const items = diagrams.map((diagram) => `<a class="nav-item" href="#${escapeHtml(diagram.id)}"><strong>${escapeHtml(diagram.title)}</strong><span>${escapeHtml(diagram.description ?? 'Manifest-driven Mermaid diagram entry.')}</span></a>`).join('');
    return `<section><div class="category-label">${escapeHtml(startCase(category))}</div>${items}</section>`;
  }).join('');
}

function startCase(value) {
  return value
    .replace(/[-_]+/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

function renderLatexBlock(formula, sourceRel) {
  const normalized = formula.trim();
  if (!normalized) {
    return '';
  }

  try {
    const rendered = katex.renderToString(normalized, {
      displayMode: true,
      output: 'htmlAndMathml',
      throwOnError: true,
      strict: 'error',
      trust: false
    });

    return `<div class="formula-block" data-formula-block>${rendered}</div>`;
  } catch (error) {
    throw new Error(`Failed to render LaTeX block in ${sourceRel}: ${error.message}`);
  }
}

function createAssetHelper(currentDir) {
  const toRoot = currentDir && currentDir !== '.'
    ? path.posix.relative(currentDir, '.') || '.'
    : '.';

  return (target) => path.posix.join(toRoot, target).replace(/\\/g, '/');
}

function absoluteUrl(sitePath) {
  return new URL(sitePath, siteConfig.baseUrl).toString();
}

function relativeHref(fromDir, targetPath) {
  let href = path.posix.relative(fromDir, targetPath);
  if (!href) {
    href = path.posix.basename(targetPath);
  }
  return href.replace(/\\/g, '/');
}

function parseFrontmatter(raw) {
  if (!raw.startsWith('---\n') && !raw.startsWith('---\r\n')) {
    return { data: {}, body: raw };
  }

  const match = raw.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n?/);
  if (!match) {
    return { data: {}, body: raw };
  }

  const data = {};
  for (const line of match[1].split(/\r?\n/)) {
    if (!line.trim() || line.trimStart().startsWith('#')) {
      continue;
    }

    const separator = line.indexOf(':');
    if (separator === -1) {
      continue;
    }

    const key = line.slice(0, separator).trim();
    const rawValue = line.slice(separator + 1).trim();
    data[key] = parseFrontmatterValue(rawValue);
  }

  return {
    data,
    body: raw.slice(match[0].length)
  };
}

function containsMath(markdown) {
  return /```latex\b/i.test(markdown);
}

function parseFrontmatterValue(rawValue) {
  if (!rawValue) {
    return '';
  }

  if ((rawValue.startsWith('"') && rawValue.endsWith('"')) || (rawValue.startsWith("'") && rawValue.endsWith("'"))) {
    return rawValue.slice(1, -1);
  }

  if (/^(true|false)$/i.test(rawValue)) {
    return rawValue.toLowerCase() === 'true';
  }

  if (/^-?\d+(\.\d+)?$/.test(rawValue)) {
    return Number(rawValue);
  }

  return rawValue;
}

function stripLeadingH1(markdown) {
  const trimmed = markdown.replace(/^\uFEFF/, '');
  const match = trimmed.match(/^\s*#\s+(.+?)\s*(?:\r?\n|$)/);

  if (!match) {
    return { heading: null, markdown: trimmed };
  }

  return {
    heading: match[1].trim(),
    markdown: trimmed.slice(match[0].length).replace(/^\s+/, '')
  };
}

function buildDownloadMarkdown(body, heading, title) {
  const trimmedBody = body.replace(/^\uFEFF/, '').trimStart();
  if (heading) {
    return trimmedBody;
  }

  return `# ${title}\n\n${trimmedBody}`;
}

function inferNavGroup(sourceRel) {
  if (
    sourceRel === 'index.md' ||
    sourceRel === 'app-overview.md' ||
    sourceRel === 'getting-started.md' ||
    sourceRel === 'cli.md' ||
    sourceRel === 'ui-preview.md'
  ) {
    return 'Start Here';
  }

  if (
    sourceRel === 'first-recording.md' ||
    sourceRel === 'breathing-workflow.md' ||
    sourceRel === 'output-formats.md'
  ) {
    return 'Task Guides';
  }

  if (
    sourceRel === 'troubleshooting.md' ||
    sourceRel === 'faq.md' ||
    sourceRel.startsWith('platform-guides/')
  ) {
    return 'Troubleshooting';
  }

  return 'Internals';
}

function inferNavOrder(sourceRel) {
  const orderMap = new Map([
    ['index.md', 10],
    ['app-overview.md', 20],
    ['getting-started.md', 30],
    ['cli.md', 40],
    ['ui-preview.md', 50],
    ['first-recording.md', 10],
    ['breathing-workflow.md', 20],
    ['output-formats.md', 30],
    ['troubleshooting.md', 10],
    ['faq.md', 20],
    ['platform-guides/index.md', 30],
    ['protocol/overview.md', 10],
    ['protocol/gatt-map.md', 20],
    ['protocol/pmd-commands.md', 30],
    ['protocol/ecg-format.md', 40],
    ['protocol/acc-format.md', 50],
    ['protocol/hr-measurement.md', 60],
    ['references.md', 90]
  ]);

  return orderMap.get(sourceRel) ?? 999;
}

function groupRank(groupName) {
  const rank = siteConfig.navGroups.indexOf(groupName);
  return rank === -1 ? 999 : rank;
}

function deriveTitle(sourceRel) {
  const fileName = path.posix.basename(sourceRel, '.md');
  return fileName
    .split(/[-_]/g)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function normalizeString(value) {
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

function toNumber(value) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function toBoolean(value) {
  return typeof value === 'boolean' ? value : false;
}

function dedupeBy(items, keySelector) {
  const seen = new Set();
  const result = [];

  for (const item of items) {
    const key = keySelector(item);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    result.push(item);
  }

  return result;
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
