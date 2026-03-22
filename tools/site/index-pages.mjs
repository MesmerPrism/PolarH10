import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { pagefindBundleDir } from './site-config.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const siteRoot = path.join(repoRoot, 'site');
const sourceDir = path.join(siteRoot, 'pagefind');
const targetDir = path.join(siteRoot, pagefindBundleDir);

async function main() {
  await ensureExists(sourceDir);
  await removeOldVersionedBundles();
  await fs.cp(sourceDir, targetDir, { recursive: true });
  console.log(`Copied Pagefind bundle to ${targetDir}`);
}

async function ensureExists(dirPath) {
  try {
    await fs.access(dirPath);
  } catch {
    throw new Error(`Expected Pagefind output at ${dirPath}, but it does not exist.`);
  }
}

async function removeOldVersionedBundles() {
  const entries = await fs.readdir(siteRoot, { withFileTypes: true });
  const removals = entries
    .filter((entry) => entry.isDirectory() && entry.name.startsWith('pagefind-'))
    .map((entry) => fs.rm(path.join(siteRoot, entry.name), { recursive: true, force: true }));

  await Promise.all(removals);
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
