// node scripts/verify-win.js
// Checks the built Windows files before they are published (scripts/release.js
// runs it on the GitHub Actions runner): installs Xloer-Setup silently, then
// starts the installed app once and Xloer-Portable twice (first start unpacks,
// the second runs from the unpacked copy), each in self-test mode
// (src/main/self-test.js) with an empty data folder. Fails when any start
// does not work, the portable unpacks again on its second start or does not
// clear an older build, or a path points to the wrong place. Prints how long
// each start took, also into the job summary.
// Only on a CI runner: the installer would replace an installed Xloer.

import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const { version } = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const dist = path.join(root, 'dist');
const setup = path.join(dist, `Xloer-Setup-${version}.exe`);
const portable = path.join(dist, `Xloer-Portable-${version}.exe`);

if (process.platform !== 'win32' || !process.env.CI) {
  console.error('verify-win.js yalnızca Windows CI üzerinde çalışır (kurulum, yüklü Xloer’in yerine geçer).');
  process.exit(1);
}

const work = fs.mkdtempSync(path.join(os.tmpdir(), 'xloer-verify-'));
const installDir = path.join(work, 'kurulum');
const cache = path.join(process.env.LOCALAPPDATA, 'Xloer', 'portable');
const problems = [];
const rows = [];
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const same = (a, b) => !!a && !!b && path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase();
const inside = (file, dir) => !!file && path.resolve(file).toLowerCase().startsWith(path.resolve(dir).toLowerCase() + path.sep);
const sec = (ms) => (ms / 1000).toFixed(1);

function running() {
  return execFileSync('tasklist', ['/FI', 'IMAGENAME eq Xloer.exe', '/NH'], { encoding: 'utf8' }).includes('Xloer.exe');
}

// Starts exe in self-test mode and waits for its report. The portable exe
// hands over to the unpacked app and exits at once, so the report (not the
// process) says when it is done.
async function selfTest(name, exe) {
  const out = path.join(work, `${name}.json`);
  const env = { ...process.env, XLOER_SELF_TEST: out, XLOER_DATA_DIR: path.join(work, `veri-${name}`) };
  const started = Date.now();
  spawn(exe, [], { env, stdio: 'ignore', detached: true }).unref();
  let report = null;
  while (!report) {
    if (Date.now() - started > 4 * 60 * 1000) {
      problems.push(`${name}: 4 dakikada rapor gelmedi`);
      return null;
    }
    await wait(200);
    try {
      report = JSON.parse(fs.readFileSync(out, 'utf8'));
    } catch {
      // Not written yet.
    }
  }
  for (let i = 0; i < 100 && running(); i++) await wait(200);
  report.wall = report.at.rows ? report.at.rows - started : null;
  if (!report.ok) problems.push(`${name}: öz-test başarısız`);
  for (const e of report.errors) problems.push(`${name}: ${e}`);
  if (report.version !== version) problems.push(`${name}: sürüm ${report.version}, beklenen ${version}`);
  const c = report.checks;
  rows.push([
    name,
    report.wall == null ? '—' : sec(report.wall),
    c.main ? `${c.main.rows} satır, ${c.main.size.join('×')}` : '—',
    c.widget ? `${c.widget.rows} satır` : '—',
    c.saved ? 'evet' : 'HAYIR',
    c.updater ? `${c.updater.status}${c.updater.error ? ` (${c.updater.error})` : ''}` : '—',
  ]);
  return report;
}

console.log(`Kurulum: ${path.basename(setup)} → ${installDir}`);
execFileSync(setup, ['/S', `/D=${installDir}`], { stdio: 'inherit' });
const installed = [path.join(installDir, 'Xloer.exe'), path.join(process.env.LOCALAPPDATA, 'Programs', 'Xloer', 'Xloer.exe')].find((f) => fs.existsSync(f));
if (!installed) problems.push('Kurulum: Xloer.exe kurulmadı');
else {
  const r = await selfTest('kurulum', installed);
  if (r) {
    if (!same(r.paths.exe, installed)) problems.push(`kurulum: ${r.paths.exe} üzerinden çalıştı`);
    if (r.paths.portableFile) problems.push('kurulum: kendini portable sanıyor');
    if (!same(r.paths.loginItem, installed)) problems.push(`kurulum: Windows ile başlatma ${r.paths.loginItem} yolunu kaydederdi`);
  }
}

// An older build's folder must go once a new build has unpacked.
fs.rmSync(cache, { recursive: true, force: true });
const old = path.join(cache, '0.0.0-eski');
fs.mkdirSync(old, { recursive: true });
fs.writeFileSync(path.join(old, 'Xloer.exe'), '');

const first = await selfTest('portable (ilk açılış)', portable);
const marker = () => {
  const dir = fs.existsSync(cache) ? fs.readdirSync(cache).find((d) => d.startsWith(`${version}-`)) : null;
  const file = dir && path.join(cache, dir, '.unpacked');
  return file && fs.existsSync(file) ? { dir: path.join(cache, dir), time: fs.statSync(file).mtimeMs } : null;
};
const unpacked = marker();
if (!unpacked) problems.push(`portable: ${cache} içine açılmadı`);
if (fs.existsSync(old)) problems.push('portable: eski sürümün klasörünü silmedi');
const second = await selfTest('portable (ikinci açılış)', portable);
const after = marker();
if (unpacked && after && after.time !== unpacked.time) problems.push('portable: ikinci açılışta yeniden açtı');
for (const r of [first, second]) {
  if (!r) continue;
  if (!same(r.paths.portableFile, portable)) problems.push(`portable: PORTABLE_EXECUTABLE_FILE = ${r.paths.portableFile}`);
  if (!unpacked || !inside(r.paths.exe, unpacked.dir)) problems.push(`portable: ${r.paths.exe} üzerinden çalıştı`);
  if (!same(r.paths.loginItem, portable)) problems.push(`portable: Windows ile başlatma ${r.paths.loginItem} yolunu kaydederdi`);
  if (r.checks.updater && r.checks.updater.portable !== true) problems.push('portable: güncelleyici portable olduğunu bilmiyor');
}

const table = [
  '| Açılış | Satırlar görünene kadar (sn) | Ana pencere | Widget | Veri kaydı | Güncelleme denetimi |',
  '|---|---|---|---|---|---|',
  ...rows.map((r) => `| ${r.join(' | ')} |`),
].join('\n');
const verdict = problems.length ? `✗ ${problems.length} sorun:\n${problems.map((p) => `- ${p}`).join('\n')}` : '✓ Kurulum ve portable sürüm çalışıyor.';
console.log(`\n${table}\n\n${verdict}`);
if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `### Xloer ${version} Windows denetimi\n\n${table}\n\n${verdict}\n`);
process.exit(problems.length ? 1 : 0);
