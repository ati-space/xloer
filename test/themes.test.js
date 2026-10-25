import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DEFAULT_PALETTE, PALETTES, paletteOf } from '../src/shared/themes.js';
import { emptyState, normalizeState } from '../src/shared/store.js';

const css = (name) => fs.readFileSync(new URL(`../src/renderer/css/${name}`, import.meta.url), 'utf8');
const tokens = (block) => Object.fromEntries([...block.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map(([, k, v]) => [k, v.trim()]));
const block = (text, selector) => {
  const at = text.indexOf(`${selector} {`);
  assert.ok(at >= 0, `${selector} is missing`);
  return tokens(text.slice(at, text.indexOf('}', at)));
};

// Every colour token the default theme sets (fonts and shapes stay shared).
const base = css('base.css');
const defaultDark = block(base, ":root,\n:root[data-theme='dark']");
const defaultLight = block(base, ":root[data-theme='light']");
const COLOR_TOKENS = Object.keys(defaultDark).filter((k) => !/^--(font|chamfer)/.test(k));

test('every theme sets every colour token, dark and light', () => {
  const themes = css('themes.css');
  for (const p of PALETTES.filter((p) => p.id !== DEFAULT_PALETTE)) {
    for (const mode of ['dark', 'light']) {
      const t = block(themes, `:root[data-palette='${p.id}'][data-theme='${mode}']`);
      const missing = COLOR_TOKENS.filter((k) => !(k in t));
      assert.deepEqual(missing, [], `${p.id} ${mode}`);
    }
  }
});

test('window colours match each theme’s page colours', () => {
  const themes = css('themes.css');
  for (const p of PALETTES) {
    for (const mode of ['dark', 'light']) {
      const t = p.id === DEFAULT_PALETTE ? (mode === 'dark' ? defaultDark : defaultLight) : block(themes, `:root[data-palette='${p.id}'][data-theme='${mode}']`);
      const w = p[mode];
      assert.deepEqual([w.bg, w.widget, w.bar, w.text], [t['--bg-list'], t['--bg-widget'], t['--bg-detail'], t['--text']], `${p.id} ${mode}`);
    }
  }
});

test('the palette setting: default, kept, unknown falls back', () => {
  assert.equal(emptyState().settings.palette, 'laboratuvar');
  const raw = { lists: [], tasks: [], settings: { palette: 'gece' } };
  assert.equal(normalizeState(raw).settings.palette, 'gece');
  assert.equal(normalizeState({ ...raw, settings: { palette: 'yok' } }).settings.palette, 'laboratuvar');
  assert.equal(paletteOf('yok').id, 'laboratuvar');
});
