// npm run release [-- --build-only]
// Builds the Windows installer and the portable exe and publishes them as one
// GitHub release on the repository named under build.publish in package.json.
// Installed copies of Xloer read latest.yml from that release to find updates.
// The portable exe is our own (build/portable.nsi), built with the makensis
// electron-builder downloads. On the Windows CI runner, scripts/verify-win.js
// starts both and nothing is published unless they work. --build-only stops
// before publishing.
// Needs the GitHub CLI (gh) logged in with write access to that repository,
// or GH_TOKEN set to such a token (as in .github/workflows/release.yml).
// Release notes come from this version's entry in src/shared/changelog.js:
// written to build/release-notes.md, where electron-builder puts them into
// latest.yml (the in-app update notes), and used for the GitHub release.

import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import { createRequire } from 'node:module';
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
const buildOnly = process.argv.includes('--build-only');
const run = (cmd, args, env) => execFileSync(cmd, args, { cwd: root, stdio: 'inherit', env: { ...process.env, ...env } });

function fail(message) {
  console.error(`\n✗ ${message}`);
  process.exit(1);
}

// Every update needs a new version number; never replace a published one.
let exists = !buildOnly;
try {
  if (exists) execFileSync('gh', ['release', 'view', tag, '--repo', target], { stdio: 'ignore' });
} catch {
  exists = false;
}
if (exists) fail(`${tag} zaten yayında. package.json içindeki "version" değerini artırıp tekrar dene.`);

const notesText = releaseNotes(version);
if (!notesText) fail(`src/shared/changelog.js içinde ${version} kaydı yok. Bu sürümde neler değiştiğini oraya ekle.`);
const notes = path.join(root, 'build', 'release-notes.md');
fs.writeFileSync(notes, notesText);

fs.rmSync(dist, { recursive: true, force: true });
// FastShot (src/main/fastshot.js), shipped in resources/fastshot.
run(process.execPath, [path.join(root, 'scripts', 'build-fastshot.js')]);
// Through node itself so it runs the same on macOS and on the Windows CI runner.
run(process.execPath, [path.join(root, 'node_modules', 'electron-builder', 'cli.js'), '--win', 'nsis', '--x64', '--publish', 'never']);

const setup = `Xloer-Setup-${version}.exe`;
const portable = `Xloer-Portable-${version}.exe`;
const unpacked = path.join(dist, 'win-unpacked');
const icon = path.join(dist, '.icon-ico', 'icon.ico');
for (const f of [path.join(unpacked, 'Xloer.exe'), path.join(unpacked, 'resources', 'app.asar'), path.join(unpacked, 'resources', 'fastshot', 'FastShot.exe'), icon]) {
  if (!fs.existsSync(f)) fail(`Derleme çıktısı eksik: ${path.relative(root, f)}`);
}

// The portable exe unpacks each build into its own folder, named after it.
const build = crypto.createHash('sha256');
for (const f of ['Xloer.exe', 'resources/app.asar']) build.update(fs.readFileSync(path.join(unpacked, f)));
const { getMakeNsisPath } = createRequire(import.meta.url)('app-builder-lib/out/toolsets/windows.js');
const makensis = await getMakeNsisPath(pkg.build.toolsets?.nsis);
const defines = {
  VERSION: version,
  BUILD_ID: build.digest('hex').slice(0, 8),
  APP_DIR: unpacked,
  ICON: icon,
  OUT_FILE: path.join(dist, portable),
  COPYRIGHT: pkg.build.copyright,
};
// makensis on macOS needs a UTF-8 locale for a Unicode installer.
run(makensis.path, ['-V2', '-WX', '-INPUTCHARSET', 'UTF8', ...Object.entries(defines).map(([k, v]) => `-D${k}=${v}`), path.join(root, 'build', 'portable.nsi')], {
  LC_ALL: process.env.LC_ALL || 'en_US.UTF-8',
  ...makensis.env,
});

const assets = [setup, 'latest.yml', portable].map((f) => path.join(dist, f));
for (const f of assets) if (!fs.existsSync(f)) fail(`Derleme çıktısı eksik: ${path.relative(root, f)}`);

// latest.yml must describe exactly the installer being uploaded.
const sha512 = crypto.createHash('sha512').update(fs.readFileSync(assets[0])).digest('base64');
if (!fs.readFileSync(path.join(dist, 'latest.yml'), 'utf8').includes(sha512)) fail('latest.yml kurulum dosyasıyla eşleşmiyor.');

if (process.platform === 'win32' && process.env.CI) run(process.execPath, [path.join(root, 'scripts', 'verify-win.js')]);
else console.log('\nKurulum ve portable sürüm yalnızca Windows CI üzerinde açılıp denetlenir (scripts/verify-win.js).');

if (buildOnly) {
  for (const f of assets) console.log(`${path.basename(f)}  ${(fs.statSync(f).size / 1e6).toFixed(1)} MB`);
  process.exit(0);
}

run('gh', ['release', 'create', tag, ...assets, '--repo', target, '--title', `Xloer ${version}`, '--notes-file', notes]);
console.log(`\n✓ ${tag} yayında: https://github.com/${target}/releases/tag/${tag}`);
