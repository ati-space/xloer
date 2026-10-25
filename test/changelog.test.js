import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { CHANGELOG, KINDS, releaseNotes } from '../src/shared/changelog.js';

const pkg = JSON.parse(fs.readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
// > 0 when version a is newer than b.
function compare(a, b) {
  const x = a.split('.').map(Number);
  const y = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i];
  return 0;
}

test('the changelog starts with the version being built', () => {
  assert.equal(CHANGELOG[0].version, pkg.version, 'add an entry to src/shared/changelog.js for the new version');
});

test('changelog entries: newest first, unique, dated, with known kinds', () => {
  for (let i = 1; i < CHANGELOG.length; i++) assert.ok(compare(CHANGELOG[i - 1].version, CHANGELOG[i].version) > 0, `${CHANGELOG[i - 1].version} before ${CHANGELOG[i].version}`);
  for (const e of CHANGELOG) {
    assert.match(e.date, /^\d{4}-\d{2}-\d{2}$/);
    assert.ok(e.title && e.items.length, e.version);
    for (const [kind, text] of e.items) assert.ok(kind in KINDS && text.trim(), `${e.version}: ${kind}`);
  }
});

test('release notes come from the entry', () => {
  assert.equal(releaseNotes('1.1.1'), '- Sürümler GitHub Actions ile derlenip yayınlanıyor\n- Güncelleyicide kullanılmayan web kurulum desteği kapatıldı\n');
  assert.equal(releaseNotes('0.0.1'), null);
});
