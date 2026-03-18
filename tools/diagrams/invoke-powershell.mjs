import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const [scriptName, ...scriptArgs] = process.argv.slice(2);

if (!scriptName) {
  console.error('Usage: node ./tools/diagrams/invoke-powershell.mjs <script-name> [args...]');
  process.exit(1);
}

const scriptPath = path.join(__dirname, scriptName);
const shells = process.platform === 'win32'
  ? ['pwsh', 'powershell']
  : ['pwsh'];

for (const shell of shells) {
  const args = ['-NoLogo', '-NoProfile'];
  if (process.platform === 'win32') {
    args.push('-ExecutionPolicy', 'Bypass');
  }

  args.push('-File', scriptPath, ...scriptArgs);

  const result = spawnSync(shell, args, { stdio: 'inherit' });

  if (result.error && result.error.code === 'ENOENT') {
    continue;
  }

  if (result.error) {
    console.error(`Failed to start ${shell}: ${result.error.message}`);
    process.exit(1);
  }

  process.exit(result.status ?? 0);
}

console.error('Unable to find a PowerShell executable. Install PowerShell 7 (`pwsh`) or Windows PowerShell.');
process.exit(1);
