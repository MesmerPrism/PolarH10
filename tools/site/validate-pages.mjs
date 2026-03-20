import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const argv = process.argv.slice(2);
const rootFlagIndex = argv.indexOf('--root');
const repoRoot = rootFlagIndex >= 0 && argv[rootFlagIndex + 1]
  ? path.resolve(argv[rootFlagIndex + 1])
  : path.resolve(__dirname, '..', '..');
const docsRoot = path.join(repoRoot, 'docs');
const diagramsRoot = path.join(docsRoot, 'diagrams');
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

  for (const filePath of markdownFiles) {
    const raw = await fs.readFile(filePath, 'utf8');
    if (placeholderPattern.test(raw)) {
      issues.push(`${relativeRepoPath(filePath)} contains a placeholder value like <your-...>.`);
    }
  }

  if (await exists(siteRoot)) {
    const builtFiles = await collectBuiltTextFiles(siteRoot);
    for (const filePath of builtFiles) {
      const raw = await fs.readFile(filePath, 'utf8');
      if (placeholderPattern.test(raw)) {
        issues.push(`${relativeRepoPath(filePath)} contains a placeholder value like <your-...>.`);
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
  if (/^(https?:|mailto:|tel:|#)/i.test(href)) {
    return null;
  }

  const [rawPath] = href.split('#');
  if (!rawPath) {
    return null;
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
