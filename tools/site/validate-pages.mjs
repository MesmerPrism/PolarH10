import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parse as parseYaml } from 'yaml';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const argv = process.argv.slice(2);
const rootFlagIndex = argv.indexOf('--root');
const repoRoot = rootFlagIndex >= 0 && argv[rootFlagIndex + 1]
  ? path.resolve(argv[rootFlagIndex + 1])
  : path.resolve(__dirname, '..', '..');
const docsRoot = path.join(repoRoot, 'docs');
const diagramsRoot = path.join(docsRoot, 'diagrams');
const showcaseManifestPath = path.join(docsRoot, 'data', 'synthetic-showcase', 'showcase-manifest.json');
const siteRoot = path.join(repoRoot, 'site');
const readmePath = path.join(repoRoot, 'README.md');
const placeholderPattern = /<your-[^>]+>/i;

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function main() {
  const issues = [];
  const docFiles = await collectMarkdownFiles(docsRoot);
  const markdownFiles = [readmePath, ...docFiles];
  const builtFiles = await exists(siteRoot)
    ? await collectBuiltTextFiles(siteRoot)
    : [];

  for (const filePath of markdownFiles) {
    const raw = await fs.readFile(filePath, 'utf8');
    if (placeholderPattern.test(raw)) {
      issues.push(`${relativeRepoPath(filePath)} contains a placeholder value like <your-...>.`);
    }

    const frontmatterError = validateFrontmatter(raw, filePath);
    if (frontmatterError) {
      issues.push(frontmatterError);
    }
  }

  for (const filePath of builtFiles) {
    const raw = await fs.readFile(filePath, 'utf8');
    if (placeholderPattern.test(raw)) {
      issues.push(`${relativeRepoPath(filePath)} contains a placeholder value like <your-...>.`);
    }

    if (filePath.toLowerCase().endsWith('.html')) {
      if (/language-latex/i.test(raw)) {
        issues.push(`${relativeRepoPath(filePath)} still contains raw LaTeX code blocks.`);
      }

      const targets = extractHtmlTargets(raw);
      for (const target of targets) {
        const error = await validateBuiltTarget(filePath, target);
        if (error) {
          issues.push(error);
        }
      }
    }
  }

  for (const filePath of markdownFiles) {
    const raw = await fs.readFile(filePath, 'utf8');
    const hrefs = extractMarkdownHrefs(raw);
    for (const href of hrefs) {
      const error = await validateHref(filePath, href);
      if (error) {
        issues.push(error);
      }
    }
  }

  const manifest = JSON.parse(await fs.readFile(path.join(diagramsRoot, 'manifest.json'), 'utf8'));
  const seenIds = new Set();
  for (const diagram of manifest.diagrams) {
    if (seenIds.has(diagram.id)) {
      issues.push(`docs/diagrams/manifest.json contains a duplicate diagram id: ${diagram.id}`);
      continue;
    }
    seenIds.add(diagram.id);

    const sourcePath = path.join(diagramsRoot, diagram.source);
    const svgPath = sourcePath.replace(/\.mmd$/i, '.svg');
    if (!await exists(sourcePath)) {
      issues.push(`docs/diagrams/manifest.json references missing source file ${path.basename(sourcePath)}.`);
    }
    if (!await exists(svgPath)) {
      issues.push(`docs/diagrams/manifest.json expects missing SVG ${path.basename(svgPath)}. Run diagram rendering first.`);
    }

    if (Array.isArray(diagram.relatedDocs)) {
      for (const relatedDoc of diagram.relatedDocs) {
        const resolved = path.resolve(diagramsRoot, relatedDoc);
        if (!await exists(resolved)) {
          issues.push(`docs/diagrams/manifest.json has missing relatedDocs target ${relatedDoc} for ${diagram.id}.`);
        }
      }
    }
  }

  await validateSyntheticShowcaseBundle(issues);

  if (issues.length > 0) {
    console.error('Pages validation failed:');
    for (const issue of issues) {
      console.error(`- ${issue}`);
    }
    process.exitCode = 1;
    return;
  }

  console.log('Pages validation passed.');
}

async function validateHref(filePath, href) {
  if (/^(https?:|mailto:|tel:|ms-appinstaller:|#)/i.test(href)) {
    return null;
  }

  const [rawPath] = href.split('#');
  if (!rawPath) {
    return null;
  }

  const normalizedRawPath = rawPath.replace(/\\/g, '/');
  if (normalizedRawPath.startsWith('assets/reference-markdown/')) {
    const sourceRel = normalizedRawPath.slice('assets/reference-markdown/'.length);
    const sourcePath = path.join(docsRoot, sourceRel);
    if (await exists(sourcePath)) {
      return null;
    }
  }

  const resolved = path.resolve(path.dirname(filePath), rawPath);
  if (await exists(resolved)) {
    return null;
  }

  if (await exists(`${resolved}.md`)) {
    return null;
  }

  return `${relativeRepoPath(filePath)} links to missing local target ${href}.`;
}

async function validateSyntheticShowcaseBundle(issues) {
  const requiredDocs = [
    path.join(docsRoot, 'synthetic-showcase', 'index.md'),
    path.join(docsRoot, 'synthetic-showcase', 'coherence.md'),
    path.join(docsRoot, 'synthetic-showcase', 'hrv.md'),
    path.join(docsRoot, 'synthetic-showcase', 'dynamics.md')
  ];

  for (const docPath of requiredDocs) {
    if (!await exists(docPath)) {
      issues.push(`${relativeRepoPath(docPath)} is required for the synthetic showcase docs section.`);
    }
  }

  if (!await exists(showcaseManifestPath)) {
    issues.push('docs/data/synthetic-showcase/showcase-manifest.json is missing. Run the SyntheticBio publish-doc-bundle flow.');
    return;
  }

  let manifest;
  try {
    manifest = JSON.parse(await fs.readFile(showcaseManifestPath, 'utf8'));
  } catch (error) {
    issues.push(`docs/data/synthetic-showcase/showcase-manifest.json is invalid JSON: ${error.message}`);
    return;
  }

  const requiredTopLevel = ['presetId', 'generatedAtUtc', 'durationSeconds', 'generatorVersion', 'trackerSettings', 'scenarioIds', 'scenarios', 'assets', 'dataRootPath'];
  for (const key of requiredTopLevel) {
    if (!(key in manifest)) {
      issues.push(`docs/data/synthetic-showcase/showcase-manifest.json is missing top-level property ${key}.`);
    }
  }

  const requiredScenarioFiles = new Set(['analysis.json', 'ecg.csv', 'ground_truth.json', 'hr_rr.csv', 'scenario.json', 'session.json']);
  if (!Array.isArray(manifest.scenarios) || manifest.scenarios.length === 0) {
    issues.push('docs/data/synthetic-showcase/showcase-manifest.json must list at least one scenario entry.');
  } else {
    for (const scenario of manifest.scenarios) {
      if (!scenario.path || !Array.isArray(scenario.files)) {
        issues.push('Every showcase scenario entry must include path and files.');
        continue;
      }

      const docsScenarioPath = path.join(docsRoot, scenario.path);
      if (!await exists(docsScenarioPath)) {
        issues.push(`Synthetic showcase scenario path is missing: ${scenario.path}.`);
      }

      const fileNames = new Set();
      for (const file of scenario.files) {
        if (!file.name || !file.path || !file.sha256 || !file.lastWriteTimeUtc) {
          issues.push(`Synthetic showcase scenario ${scenario.scenarioId ?? '(unknown)'} has an incomplete file entry.`);
          continue;
        }

        fileNames.add(file.name);
        const docsPath = path.join(docsRoot, file.path);
        if (!await exists(docsPath)) {
          issues.push(`Synthetic showcase file is missing: ${file.path}.`);
        }

        if (await exists(siteRoot)) {
          const sitePath = path.join(siteRoot, file.path);
          if (!await exists(sitePath)) {
            issues.push(`Built site is missing synthetic showcase data file ${file.path}.`);
          }
        }
      }

      for (const requiredFile of requiredScenarioFiles) {
        if (!fileNames.has(requiredFile)) {
          issues.push(`Synthetic showcase scenario ${scenario.scenarioId ?? '(unknown)'} is missing required file ${requiredFile}.`);
        }
      }
    }
  }

  const requiredAssetIds = new Set([
    'showcase-overview-svg',
    'showcase-overview-png',
    'coherence-derivation-svg',
    'coherence-derivation-png',
    'hrv-derivation-svg',
    'hrv-derivation-png',
    'dynamics-derivation-svg',
    'dynamics-derivation-png',
    'coherence-appendix-svg',
    'coherence-appendix-png',
    'dynamics-appendix-svg',
    'dynamics-appendix-png',
    'figure-pack-pdf'
  ]);

  if (!Array.isArray(manifest.assets) || manifest.assets.length === 0) {
    issues.push('docs/data/synthetic-showcase/showcase-manifest.json must list synthetic showcase assets.');
  } else {
    const seenAssetIds = new Set();
    for (const asset of manifest.assets) {
      if (!asset.id || !asset.path || !asset.sha256 || !asset.lastWriteTimeUtc || !asset.format) {
        issues.push('Every synthetic showcase asset entry must include id, path, sha256, lastWriteTimeUtc, and format.');
        continue;
      }

      seenAssetIds.add(asset.id);
      const docsPath = path.join(docsRoot, asset.path);
      if (!await exists(docsPath)) {
        issues.push(`Synthetic showcase asset is missing: ${asset.path}.`);
      }

      if (await exists(siteRoot)) {
        const sitePath = path.join(siteRoot, asset.path);
        if (!await exists(sitePath)) {
          issues.push(`Built site is missing synthetic showcase asset ${asset.path}.`);
        }
      }
    }

    for (const assetId of requiredAssetIds) {
      if (!seenAssetIds.has(assetId)) {
        issues.push(`Synthetic showcase manifest is missing required asset id ${assetId}.`);
      }
    }
  }
}
function validateFrontmatter(raw, filePath) {
  const match = raw.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n?/);
  if (!match) {
    return null;
  }

  try {
    const parsed = parseYaml(match[1]);
    if (parsed === null || parsed === undefined) {
      return null;
    }

    if (typeof parsed !== 'object' || Array.isArray(parsed)) {
      return `${relativeRepoPath(filePath)} has invalid front matter: top-level front matter must be key/value pairs.`;
    }

    return null;
  } catch (error) {
    return `${relativeRepoPath(filePath)} has invalid front matter: ${error.message}`;
  }
}

function extractMarkdownHrefs(markdown) {
  const hrefs = [];
  const linkPattern = /!?\[[^\]]*]\(([^)\s]+(?:\s+"[^"]*")?)\)/g;
  let match;
  while ((match = linkPattern.exec(markdown)) !== null) {
    const rawTarget = match[1].trim();
    const href = rawTarget.replace(/\s+"[^"]*"$/, '');
    hrefs.push(href);
  }
  return hrefs;
}

function extractHtmlTargets(html) {
  const targets = [];
  const attributePattern = /\b(?:href|src)=["']([^"']+)["']/gi;
  let match;
  while ((match = attributePattern.exec(html)) !== null) {
    targets.push(match[1]);
  }
  return targets;
}

function relativeRepoPath(filePath) {
  return path.relative(repoRoot, filePath).replace(/\\/g, '/');
}

async function collectMarkdownFiles(dir) {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);

    if (entry.isDirectory()) {
      if (path.relative(docsRoot, fullPath).replace(/\\/g, '/') === 'assets') {
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

async function collectBuiltTextFiles(dir) {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    const rel = path.relative(siteRoot, fullPath).replace(/\\/g, '/');

    if (entry.isDirectory()) {
      if (rel === 'pagefind') {
        continue;
      }

      files.push(...await collectBuiltTextFiles(fullPath));
      continue;
    }

    if (entry.isFile() && /\.(html|xml|txt|json|webmanifest)$/i.test(entry.name)) {
      files.push(fullPath);
    }
  }

  return files.sort();
}

async function exists(targetPath) {
  try {
    await fs.access(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function validateBuiltTarget(filePath, href) {
  if (/^(https?:|mailto:|tel:|ms-appinstaller:|#|data:)/i.test(href)) {
    return null;
  }

  const [rawPath] = href.split(/[?#]/, 1);
  if (!rawPath) {
    return null;
  }

  const normalizedRawPath = rawPath.replace(/\\/g, '/');
  if (normalizedRawPath.includes('/pagefind/') || normalizedRawPath.startsWith('pagefind/')) {
    return null;
  }

  const resolved = path.resolve(path.dirname(filePath), rawPath);
  if (await exists(resolved)) {
    return null;
  }

  return `${relativeRepoPath(filePath)} references missing built-site target ${href}.`;
}
