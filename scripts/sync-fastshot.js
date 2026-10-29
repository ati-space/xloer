// npm run sync-fastshot [-- <path to FastShot's windows folder>]
// Copies FastShot for Windows into native/fastshot, which scripts/build-fastshot.js
// builds into the exe Xloer ships. FastShot lives in its own (private)
// repository, github.com/Mericsdc/FastShot, under windows/; by default this
// reads a checkout of it next to this one (../FastShot/windows). Commit the
// result with a release of Xloer to ship it.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = path.resolve(process.argv[2] ?? path.join(root, '..', 'FastShot', 'windows'));
const target = path.join(root, 'native', 'fastshot');
const parts = ['global.json', 'Directory.Build.props', 'src/FastShot', 'src/FastShot.Core'];
// Build output is never copied.
const skip = new Set(['bin', 'obj']);

const missing = parts.filter((p) => !fs.existsSync(path.join(source, p)));
if (missing.length) {
  console.error(`✗ ${source} bir FastShot windows klasörü değil (${missing.join(', ')} yok).`);
  console.error('  Mericsdc/FastShot deposunu Xloer’in yanına klonlayın ya da windows klasörünün yolunu verin.');
  process.exit(1);
}

fs.rmSync(path.join(target, 'src'), { recursive: true, force: true });
let files = 0;
for (const part of parts) {
  fs.cpSync(path.join(source, part), path.join(target, part), {
    recursive: true,
    filter: (from) => {
      if (skip.has(path.basename(from)) && fs.statSync(from).isDirectory()) return false;
      if (fs.statSync(from).isFile()) files++;
      return true;
    },
  });
}
console.log(`FastShot kopyalandı: ${files} dosya, ${path.relative(root, source) || source} → native/fastshot`);
