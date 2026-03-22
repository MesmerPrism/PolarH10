import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import katex from 'katex';
import { marked } from 'marked';
import { parse as parseYaml } from 'yaml';
import { assetVersion, pagefindBundleDir, searchPagePath } from './site-config.mjs';

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

const siteConfig = {
  repoUrl: 'https://github.com/MesmerPrism/PolarH10',
  baseUrl: 'https://mesmerprism.github.io/PolarH10/',
  siteName: 'PolarH10',
  brandTagline: 'Windows-first telemetry toolkit',
  homeTitle: 'Windows-first Polar H10 telemetry toolkit',
  referenceTitle: 'PolarH10 Developer Reference',
  sharedPromise: 'Academic-friendly Windows-first Polar H10 telemetry toolkit for HR, RR, ECG, ACC, multi-device comparison, and ACC-based breathing views without the Polar SDK.',
  defaultDescription: 'Windows-first Polar H10 docs, onboarding guides, protocol reference, and Mermaid system diagrams. Unofficial project; not endorsed by or affiliated with Polar Electro Oy.',
  socialImage: 'assets/brutal-tdr-preview.png',
  favicon: 'assets/polarh10-stripe-mark.png',
  themeColor: '#f3eee6',
  navGroups: ['Start Here', 'Task Guides', 'Troubleshooting', 'Internals'],
  diagramViewerLabel: 'Diagram Viewer',
  diagramViewerDescription: 'Browse onboarding, runtime, and architecture Mermaid diagrams.',
  searchTitle: 'Search the PolarH10 docs',
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
    const { data, body } = parseFrontmatter(raw, sourceRel);
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
  const withSearchableShell = withHead.replace(
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
  const topNav = renderTopNav('reference', asset('index.html'), asset('reference/index.html'), asset('diagrams/viewer.html'));
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
  includeMathStyles: doc.hasMath,
  updatedAt: doc.updatedAt
})}
</head>
<body class="doc-page">
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, asset('index.html'), asset(searchPagePath))}
    <div class="page-layout">
      <aside class="panel sidebar" data-pagefind-ignore>
        ${sidebar}
      </aside>
      <main class="panel content-panel">
        <div class="page-marker">PolarH10 Developer Reference</div>
        <h1 class="page-title" data-pagefind-meta="title">${escapeHtml(doc.title)}</h1>
        ${doc.summary ? `<p class="page-intro">${escapeHtml(doc.summary)}</p>` : ''}
        <article class="prose" data-pagefind-body>
          ${articleHtml}
        </article>
      </main>
    </div>
    ${renderFooter()}
  </div>
  ${renderHeaderBoot()}
</body>
</html>`;
}

function renderHomePage() {
  const topNav = renderTopNav('home', 'index.html', 'reference/index.html', 'diagrams/viewer.html');

  return `<!DOCTYPE html>
<html lang="en">
<head>
${renderHead({
  title: siteConfig.homeTitle,
  description: siteConfig.sharedPromise,
  currentDir: '.',
  canonicalPath: 'index.html'
})}
</head>
<body>
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, 'index.html', searchPagePath)}
    <main data-pagefind-body>
    <section class="hero hero-home">
      <div class="panel hero-copy tone-dark">
        <div class="page-marker">Unofficial Windows-first Polar H10 toolkit for academic workflows</div>
        <h1>Record and compare Polar H10 telemetry on Windows.</h1>
        <p class="hero-lede">PolarH10 is designed first for academics who need a practical Windows workflow without the Polar SDK. Use the WPF app or CLI to inspect HR, RR, ECG, and ACC live, track multiple H10 devices in parallel, record reusable sessions, and review derived views such as coherence, short-term HRV, ACC-based breathing-volume approximation, and breathing-dynamics features.</p>
        <div class="stats">
          <div class="stat">
            <strong>Parallel live monitoring</strong>
            <span>Track one device or compare multiple active straps in the same live workspace when you need side-by-side research sessions.</span>
          </div>
          <div class="stat">
            <strong>Cardiac plus breathing views</strong>
            <span>Review HR, RR, ECG, ACC, and ACC-based breathing output within the same session instead of splitting those signals across separate tools.</span>
          </div>
          <div class="stat">
            <strong>Methods and implementation notes</strong>
            <span>Read the workflow guides, formula sheets, output formats, protocol reference, and diagrams when validation or extension matters.</span>
          </div>
        </div>
        <div class="action-row">
          <a class="button primary" href="reference/getting-started.html">Get started</a>
          <a class="button" href="reference/first-recording.html">Record a first session</a>
          <a class="button" href="reference/formula-sheets.html">Read the formulas</a>
          <a class="button" href="reference/protocol/overview.html">Browse internals</a>
        </div>
      </div>
      <aside class="panel hero-preview">
        <h2 class="section-heading">For studies, methods, and implementation</h2>
        <p>This site is organized around three common academic tasks: running a Windows collection session, checking how derived values are computed, and tracing the implementation when reproducibility or extension matters.</p>
        <img src="assets/brutal-tdr-preview.png" alt="PolarH10 WPF application preview with multiple tracked devices" />
        <p class="hero-preview-note">The preview shows parallel device tracking in the live workspace. If you already know the term you need, search for <code>doctor</code>, <code>RR</code>, <code>breathing</code>, or <code>protocol.jsonl</code> and jump straight to the matching guide.</p>
      </aside>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Start with your research task</h2>
      <p class="section-subtitle">Most visitors need one of three things: a reliable collection workflow, a readable method reference for derived measures, or implementation detail for validation and extension.</p>
      <div class="audience-grid">
        <article class="audience-card tone-cool">
          <h3>Running sessions and comparing devices</h3>
          <p>Start here if you need to connect straps, monitor live telemetry, compare multiple active H10 units, or collect reusable Windows sessions for later analysis.</p>
          <ul class="audience-link-list">
            <li><a class="audience-link" href="reference/getting-started.html"><strong>Getting started</strong><span>Clone, build, and reach a safe first run.</span></a></li>
            <li><a class="audience-link" href="reference/first-recording.html"><strong>First recording</strong><span>Run the first end-to-end session and save reusable output.</span></a></li>
            <li><a class="audience-link" href="reference/app-overview.html"><strong>App overview</strong><span>See the tracked-device workflow, live views, and operator-facing surfaces.</span></a></li>
            <li><a class="audience-link" href="reference/cli.html"><strong>CLI guide</strong><span>Use the terminal workflow for scan, doctor, record, and replay.</span></a></li>
            <li><a class="audience-link" href="reference/troubleshooting.html"><strong>Troubleshooting</strong><span>Fix the common Windows BLE and device-discovery failures first.</span></a></li>
          </ul>
        </article>
        <article class="audience-card tone-signal">
          <h3>Reading the derived measures</h3>
          <p>Start here if you need the method context behind coherence, short-term HRV, ACC-based breathing-volume approximation, or the breathing-dynamics feature family.</p>
          <ul class="audience-link-list">
            <li><a class="audience-link" href="reference/formula-sheets.html"><strong>Formula sheets</strong><span>Download the Markdown and PDF method notes.</span></a></li>
            <li><a class="audience-link" href="reference/hrv-workflow.html"><strong>HRV workflow</strong><span>Read RMSSD, SDNN, and pNN50 in the context of a short-term window.</span></a></li>
            <li><a class="audience-link" href="reference/coherence-workflow.html"><strong>Coherence workflow</strong><span>See the RR warmup, confidence handling, and expected caveats.</span></a></li>
            <li><a class="audience-link" href="reference/breathing-workflow.html"><strong>Breathing workflow</strong><span>Follow the ACC breathing calibration flow and inspect live breathing output.</span></a></li>
            <li><a class="audience-link" href="reference/breathing-formulas.html"><strong>Breathing from ACC formula sheet</strong><span>Review the repository-specific breathing-volume approximation and its limits.</span></a></li>
            <li><a class="audience-link" href="reference/breathing-dynamics-workflow.html"><strong>Breathing dynamics</strong><span>Understand when the entropy views are valid and how calibration affects them.</span></a></li>
          </ul>
        </article>
        <article class="audience-card tone-violet">
          <h3>Validating or extending the implementation</h3>
          <p>Start here if you need repo structure, saved record formats, protocol details, or the diagrams that explain how the Windows BLE and analysis pipeline fits together.</p>
          <ul class="audience-link-list">
            <li><a class="audience-link" href="reference/protocol/overview.html"><strong>Protocol overview</strong><span>Map PMD, GATT, decoding, and the lower-level data path.</span></a></li>
            <li><a class="audience-link" href="reference/output-formats.html"><strong>Output formats</strong><span>Inspect the session files, manifests, and recorded artifacts.</span></a></li>
            <li><a class="audience-link" href="reference/references.html"><strong>References and provenance</strong><span>Trace the source material and repo-specific adaptation notes.</span></a></li>
            <li><a class="audience-link" href="diagrams/viewer.html#code-architecture"><strong>Diagram viewer</strong><span>Open the onboarding, runtime, and architecture maps.</span></a></li>
          </ul>
        </article>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Typical session flow</h2>
      <p class="section-subtitle">If you are collecting lab data on Windows, this is the order of operations that keeps the session trustworthy.</p>
      <div class="step-grid">
        <div class="step-card tone-cool">
          <div class="step-no">01</div>
          <h3>Choose the devices and tool</h3>
          <p>Find the intended H10 units, confirm the Bluetooth addresses, and decide whether the session belongs in the WPF app or the CLI.</p>
        </div>
        <div class="step-card tone-violet">
          <div class="step-no">02</div>
          <h3>Connect and validate</h3>
          <p>Open the BLE/GATT link, confirm HR plus ACC are live, and use the diagnostics path before you trust a long recording.</p>
        </div>
        <div class="step-card tone-signal">
          <div class="step-no">03</div>
          <h3>Inspect cardiac and breathing signals</h3>
          <p>Check HR, RR, ECG, and ACC first, enable multi-device comparison if needed, and open the breathing view once the ACC calibration is ready.</p>
        </div>
        <div class="step-card tone-warm">
          <div class="step-no">04</div>
          <h3>Record and review</h3>
          <p>Write <code>session.json</code>, CSV sensor output, and <code>protocol.jsonl</code>, then replay or inspect the capture without hardware attached.</p>
        </div>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Feedback and contributions</h2>
      <p class="section-subtitle">Issues are the public path for onboarding friction, device compatibility quirks, protocol questions, and small doc fixes.</p>
      <div class="action-row">
        <a class="button primary" href="${siteConfig.repoUrl}/issues">Open an issue</a>
        <a class="button" href="${siteConfig.repoUrl}/blob/main/CONTRIBUTING.md">Read contributing guide</a>
      </div>
    </section>

    <section class="section panel section-panel">
      <h2 class="section-heading">Onboarding and architecture diagrams</h2>
      <p class="section-subtitle">Use the diagrams when you want the shortest visual explanation of the workflows, records, and code layout.</p>
      <div class="preview-grid">
        <a class="preview-card tone-cool" href="diagrams/viewer.html#choose-your-path">
          <div class="meta">Orientation</div>
          <h3>App, formulas, or internals</h3>
          <p>Use the audience map when you need to decide whether to start with capture workflows, metric explanations, or implementation details.</p>
          <img src="diagrams/choose-your-path.svg" alt="Audience map diagram" />
        </a>
        <a class="preview-card tone-warm" href="diagrams/viewer.html#first-session-flow">
          <div class="meta">Workflow</div>
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
  ${renderHeaderBoot()}
</body>
</html>`;
}

function renderSearchPage() {
  const currentDir = 'reference';
  const asset = createAssetHelper(currentDir);
  const topNav = renderTopNav('reference', asset('index.html'), asset('reference/index.html'), asset('diagrams/viewer.html'));

  return `<!DOCTYPE html>
<html lang="en">
<head>
${renderHead({
  title: `${siteConfig.searchTitle} | ${siteConfig.referenceTitle}`,
  description: siteConfig.searchDescription,
  currentDir,
  canonicalPath: searchPagePath
})}
</head>
<body class="doc-page">
  ${renderArt()}
  <div class="site-shell">
    ${renderHeader(topNav, asset('index.html'), asset(searchPagePath))}
    <section class="panel section-panel search-section">
      <div class="page-marker">Search results</div>
      <h1 class="page-title">Find where a term is documented.</h1>
      <p class="page-intro">Use the header search to jump here from anywhere on the site. This page keeps the full result list, excerpts, and follow-on refinement tools in one place. Try <code>doctor</code>, <code>RR</code>, <code>protocol.jsonl</code>, or <code>PMD</code>.</p>
      ${renderSearchPanel({
        title: 'Results',
        description: 'Use the header search to change the query. The list below updates to the pages that best match it.',
        standalone: true,
        resultsOnly: true,
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
  ${renderHeaderBoot()}
  ${renderSearchBoot(asset)}
</body>
</html>`;
}

function render404Page() {
  const topNav = renderTopNav('', 'index.html', 'reference/index.html', 'diagrams/viewer.html');

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
    ${renderHeader(topNav, 'index.html', 'reference/search.html')}
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
  ${renderHeaderBoot()}
</body>
</html>`;
}

function renderHead({ title, description, currentDir, canonicalPath, includeMathStyles = false, updatedAt = null, noIndex = false }) {
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
  ${includeMathStyles ? `<link rel="stylesheet" href="${asset('assets/vendor/katex/katex.min.css')}?v=${assetVersion}" />` : ''}`;
}

function renderSidebar(currentDoc, docs) {
  const groups = buildNavGroups(currentDoc, docs).map((group) => {
    const items = group.items.map((item) => {
      const isActive = item.isActive ? ' active' : '';
      return `<a class="nav-item${isActive}" href="${escapeHtml(item.href)}"><strong>${escapeHtml(item.label)}</strong><span>${escapeHtml(item.description)}</span></a>`;
    }).join('');

    return `<section><h2 class="category-heading">${escapeHtml(group.title)}</h2>${items}</section>`;
  }).join('');

  return groups;
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

function renderTopNav(activeKey, homeHref, docsHref, diagramsHref) {
  const items = [
    { key: 'home', label: 'Home', href: homeHref },
    { key: 'reference', label: 'Docs', href: docsHref },
    { key: 'diagrams', label: 'Diagrams', href: diagramsHref },
    { key: 'repo', label: 'GitHub', href: siteConfig.repoUrl }
  ];

  return items.map((item) => {
    const active = item.key === activeKey ? 'active' : '';
    return `<a class="${active}" href="${item.href}">${item.label}</a>`;
  }).join('');
}

function renderHeader(topNav, homeHref, searchHref) {
  return `<header class="site-header" data-pagefind-ignore>
    <a class="brand" href="${homeHref}">
      <span class="brand-mark" aria-hidden="true"></span>
      <span class="brand-copy"><strong>${siteConfig.siteName}</strong><span>${siteConfig.brandTagline}</span></span>
    </a>
    <div class="header-tools">
      <form class="header-search" action="${searchHref}" method="get" role="search" data-header-search-form>
        <label class="sr-only" for="site-search-input">Search the site</label>
        <input id="site-search-input" class="header-search-input" data-header-search-input type="search" name="q" placeholder="Search docs" autocomplete="off" />
        <button class="header-search-button" type="submit">Search</button>
      </form>
      <nav class="top-nav" aria-label="Primary">${topNav}</nav>
    </div>
  </header>`;
}

function renderFooter() {
  return `<footer class="footer" data-pagefind-ignore>PolarH10 WPF app, CLI capture tooling, protocol docs, and Mermaid diagrams. Unofficial project; not endorsed by or affiliated with Polar Electro Oy.</footer>`;
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

function renderSearchPanel({ title, description, standalone = false, autofocus = false, resultsOnly = false, queryParam = '' }) {
  const className = [
    'search-panel',
    standalone ? 'search-panel-standalone' : '',
    resultsOnly ? 'search-panel-results-only' : ''
  ].filter(Boolean).join(' ');
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
      <p class="search-status" data-search-status hidden></p>
      <div id="pagefind-search"${attrs.length ? ` ${attrs.join(' ')}` : ''}></div>
      <noscript><p class="search-status" data-state="error">Search needs JavaScript. If you are using Brave Shields or a script blocker, allow scripts for this site and reload.</p></noscript>
    </section>`;
}

function renderHeaderBoot() {
  return `<script>
  window.addEventListener('DOMContentLoaded', () => {
    const headerInput = document.querySelector('[data-header-search-input]');
    if (!headerInput) {
      return;
    }

    const initialQuery = new URLSearchParams(window.location.search).get('q');
    if (initialQuery && !headerInput.value) {
      headerInput.value = initialQuery;
    }

    document.addEventListener('keydown', (event) => {
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
      headerInput.focus({ preventScroll: true });
      if (headerInput.value) {
        headerInput.select();
      }
    });
  });
</script>`;
}

function renderSearchBoot(asset) {
  const jsHref = asset(`${pagefindBundleDir}/pagefind.js`);
  const bundlePath = `${asset(pagefindBundleDir)}/`;

  return `<script type="module">
  import { options as configurePagefind, search as runSearch } from ${JSON.stringify(jsHref)};

  window.addEventListener('DOMContentLoaded', () => {
    const mount = document.getElementById('pagefind-search');
    const status = document.querySelector('[data-search-status]');
    const headerForm = document.querySelector('[data-header-search-form]');
    const headerInput = document.querySelector('[data-header-search-input]');
    const showStatus = (message, state = 'pending') => {
      if (!status) {
        return;
      }

      status.hidden = false;
      status.dataset.state = state;
      status.textContent = message;
    };

    const clearStatus = () => {
      if (!status) {
        return;
      }

      status.hidden = true;
      status.textContent = '';
      delete status.dataset.state;
    };

    if (!mount) {
      return;
    }

    const queryKey = mount.dataset.searchQueryParam;
    const maxResults = 20;
    let configured = false;
    let activeRequest = 0;

    const syncHeader = (value) => {
      if (headerInput && headerInput.value !== value) {
        headerInput.value = value;
      }
    };

    const syncUrl = (value) => {
      if (!queryKey) {
        return;
      }

      const nextUrl = new URL(window.location.href);
      if (value) {
        nextUrl.searchParams.set(queryKey, value);
      } else {
        nextUrl.searchParams.delete(queryKey);
      }

      history.replaceState(null, '', nextUrl);
    };

    const make = (tagName, className, text = '') => {
      const element = document.createElement(tagName);
      if (className) {
        element.className = className;
      }
      if (text) {
        element.textContent = text;
      }
      return element;
    };

    const pluralize = (count, singular, plural = singular + 's') => count === 1 ? singular : plural;

    const replaceMount = (...nodes) => {
      mount.replaceChildren(...nodes.filter(Boolean));
    };

    const renderIdleState = () => {
      replaceMount(
        make('p', 'search-results-note', 'Use the header search to run a query. Try doctor, RR, protocol.jsonl, or PMD.')
      );
    };

    const renderNoResults = (query) => {
      replaceMount(
        make('p', 'search-results-summary', 'No results for "' + query + '".'),
        make('p', 'search-results-note', 'Try a broader term, a protocol field name, or a shorter acronym.')
      );
    };

    const appendExcerpt = (parent, className, html) => {
      if (!html) {
        return;
      }

      const excerpt = make('p', className);
      excerpt.innerHTML = html;
      parent.append(excerpt);
    };

    const buildSubResults = (fragmentUrl, subResults) => {
      const items = Array.isArray(subResults)
        ? subResults.filter((item) => item && item.url && item.title && item.url !== fragmentUrl).slice(0, 3)
        : [];

      if (!items.length) {
        return null;
      }

      const section = make('div', 'search-result-subsection');
      section.append(make('p', 'search-result-subheading', 'Within this page'));
      const list = make('ul', 'search-result-sublist');

      for (const item of items) {
        const entry = make('li', 'search-subresult');
        const link = make('a', 'search-subresult-link', item.title);
        link.href = item.url;
        entry.append(link);
        appendExcerpt(entry, 'search-subresult-excerpt', item.excerpt);
        list.append(entry);
      }

      section.append(list);
      return section;
    };

    const renderResults = (query, searchResult, fragments) => {
      const totalResults = searchResult?.results?.length ?? 0;
      const resultsShown = fragments.length;
      const wrapper = make('div', 'search-results');
      const summary = make(
        'p',
        'search-results-summary',
        totalResults > resultsShown
          ? 'Showing the first ' + resultsShown + ' of ' + totalResults + ' ' + pluralize(totalResults, 'result') + ' for "' + query + '".'
          : totalResults + ' ' + pluralize(totalResults, 'result') + ' for "' + query + '".'
      );
      const list = make('ol', 'search-results-list');

      for (const fragment of fragments) {
        const item = make('li', 'search-result');
        const heading = make('h3', 'search-result-title');
        const link = make('a', 'search-result-link', fragment?.meta?.title || fragment?.url || 'Untitled result');
        link.href = fragment?.url || '#';
        heading.append(link);
        item.append(heading);
        appendExcerpt(item, 'search-result-excerpt', fragment?.excerpt);

        const subSection = buildSubResults(fragment?.url, fragment?.sub_results);
        if (subSection) {
          item.append(subSection);
        }

        list.append(item);
      }

      wrapper.append(summary);
      wrapper.append(list);
      replaceMount(wrapper);
    };

    const ensureConfigured = async () => {
      if (configured) {
        return;
      }

      showStatus('Loading search index…');
      await configurePagefind({
        basePath: ${JSON.stringify(bundlePath)},
        excerptLength: 18
      });
      configured = true;
    };

    const runQuery = async (rawValue, { updateUrl = false } = {}) => {
      const query = rawValue.trim();
      const requestId = ++activeRequest;
      syncHeader(query);

      if (updateUrl) {
        syncUrl(query);
      }

      if (!query) {
        clearStatus();
        renderIdleState();
        return;
      }

      try {
        await ensureConfigured();
        showStatus('Searching for "' + query + '"…');
        const searchResult = await runSearch(query);
        if (requestId !== activeRequest) {
          return;
        }

        const matches = searchResult?.results ?? [];
        if (!matches.length) {
          clearStatus();
          renderNoResults(query);
          return;
        }

        const fragments = (await Promise.all(
          matches.slice(0, maxResults).map(async (result) => {
            try {
              return await result.data();
            } catch (error) {
              console.error(error);
              return null;
            }
          })
        )).filter(Boolean);

        if (requestId !== activeRequest) {
          return;
        }

        if (!fragments.length) {
          showStatus('Search found matches but could not load their excerpts. Reload the page and try again.', 'error');
          replaceMount();
          return;
        }

        clearStatus();
        renderResults(query, searchResult, fragments);
      } catch (error) {
        console.error(error);
        showStatus('Search failed to load results. Reload the page and, if you are using Brave Shields or an extension blocker, allow scripts for this site.', 'error');
        replaceMount();
      }
    };

    if (headerForm && headerInput) {
      headerForm.addEventListener('submit', (event) => {
        event.preventDefault();
        runQuery(headerInput.value, { updateUrl: true });
      });

      headerInput.addEventListener('search', () => {
        if (headerInput.value) {
          return;
        }

        runQuery('', { updateUrl: true });
      });
    }

    window.addEventListener('popstate', () => {
      const query = queryKey
        ? new URLSearchParams(window.location.search).get(queryKey)?.trim() ?? ''
        : '';
      runQuery(query);
    });

    const initialQuery = queryKey
      ? new URLSearchParams(window.location.search).get(queryKey)?.trim() ?? ''
      : '';

    if (initialQuery) {
      runQuery(initialQuery);
      return;
    }

    renderIdleState();
    if (mount.dataset.searchAutofocus === 'true') {
      headerInput?.focus({ preventScroll: true });
    }
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

function parseFrontmatter(raw, sourceLabel = 'document') {
  if (!raw.startsWith('---\n') && !raw.startsWith('---\r\n')) {
    return { data: {}, body: raw };
  }

  const match = raw.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n?/);
  if (!match) {
    return { data: {}, body: raw };
  }

  let data = {};
  try {
    const parsed = parseYaml(match[1]);
    if (parsed === null || parsed === undefined) {
      data = {};
    } else if (typeof parsed === 'object' && !Array.isArray(parsed)) {
      data = parsed;
    } else {
      throw new Error('front matter must be a mapping of key/value pairs.');
    }
  } catch (error) {
    throw new Error(`Invalid front matter in ${sourceLabel}: ${error.message}`);
  }

  return {
    data,
    body: raw.slice(match[0].length)
  };
}

function containsMath(markdown) {
  return /```latex\b/i.test(markdown);
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
