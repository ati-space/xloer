// npm run release
// Builds the Windows installers and publishes them as one GitHub release on
// the repository named under build.publish in package.json. Installed copies
// of Xloer read latest.yml from that release to find updates.
// Needs the GitHub CLI (gh) logged in with write access to that repository,
// or GH_TOKEN set to such a token (as in .github/workflows/release.yml).
// Release notes come from this version's entry in src/shared/changelog.js:
// written to build/release-notes.md, where electron-builder puts them into
// latest.yml (the in-app update notes), and used for the GitHub release.

import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { releaseNotes } from '../src/shared/changelog.js';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const { owner, repo } = pkg.build.publish[0];
const target = `${owner}/${repo}`;
const version = pkg.version;
const tag = `v${version}`;
const dist = path.join(root, 'dist');
const run = (cmd, args) => execFileSync(cmd, args, { cwd: root, stdio: 'inherit' });

function fail(message) {
  console.error(`\n✗ ${message}`);
  process.exit(1);
}

// Every update needs a new version number; never replace a published one.
let exists = true;
try {
  execFileSync('gh', ['release', 'view', tag, '--repo', target], { stdio: 'ignore' });
} catch {
  exists = false;
}
if (exists) fail(`${tag} zaten yayında. package.json içindeki "version" değerini artırıp tekrar dene.`);

const notesText = releaseNotes(version);
if (!notesText) fail(`src/shared/changelog.js içinde ${version} kaydı yok. Bu sürümde neler değiştiğini oraya ekle.`);
const notes = path.join(root, 'build', 'release-notes.md');
fs.writeFileSync(notes, notesText);

fs.rmSync(dist, { recursive: true, force: true });
// Through node itself so it runs the same on macOS and on the Windows CI runner.
run(process.execPath, [path.join(root, 'node_modules', 'electron-builder', 'cli.js'), '--win', 'nsis', 'portable', '--x64', '--publish', 'never']);

const setup = `Xloer-Setup-${version}.exe`;
const assets = [setup, `${setup}.blockmap`, 'latest.yml', `Xloer-Portable-${version}.exe`].map((f) => path.join(dist, f));
for (const f of assets) if (!fs.existsSync(f)) fail(`Derleme çıktısı eksik: ${path.relative(root, f)}`);

// latest.yml must describe exactly the installer being uploaded.
const sha512 = crypto.createHash('sha512').update(fs.readFileSync(assets[0])).digest('base64');
if (!fs.readFileSync(path.join(dist, 'latest.yml'), 'utf8').includes(sha512)) fail('latest.yml kurulum dosyasıyla eşleşmiyor.');

run('gh', ['release', 'create', tag, ...assets, '--repo', target, '--title', `Xloer ${version}`, '--notes-file', notes]);
console.log(`\n✓ ${tag} yayında: https://github.com/${target}/releases/tag/${tag}`);
