// Test system names as used on the cards, and pairs that look like one
// system written two ways ("70kN Elektrodinamik Sarsıcı" / "70kN Sarsıcı").

import { fold } from './search.js';
import { DEFAULT_DEVICES } from './invoices.js';

// Words that describe the kind of system rather than which one it is.
const GENERIC = new Set(['elektrodinamik', 'sarsici', 'kabin', 'kabini', 'sistemi', 'sistem', 'test']);

const similarKey = (name) =>
  fold(name)
    .split(/[^\p{L}\p{N}]+/u)
    .filter((w) => w && !GENERIC.has(w))
    .join('');

// A name without its size: words with a digit go ("70kN Sarsıcı" → "Sarsıcı"),
// and so does "Elektrodinamik", which every such shaker is.
export function baseDeviceName(name) {
  return String(name ?? '')
    .trim()
    .split(/\s+/)
    .filter((w) => w && !/\d/.test(w) && fold(w) !== 'elektrodinamik')
    .join(' ');
}

// Systems that differ only by size, at least two of them: [{ to, from: [names] }].
// "to" is the size-less name, as one of them already writes it if any does.
export function sameBaseDevices(state) {
  const groups = new Map();
  for (const d of deviceNames(state)) {
    const base = baseDeviceName(d.name);
    if (!base) continue;
    const key = fold(base);
    if (!groups.has(key)) groups.set(key, { base, names: [] });
    groups.get(key).names.push(d.name);
  }
  return [...groups.values()]
    .filter((g) => g.names.length > 1)
    .map((g) => {
      const to = g.names.find((n) => fold(n) === fold(g.base)) ?? g.base;
      return { to, from: g.names.filter((n) => fold(n) !== fold(to)) };
    });
}

// Every name in the device list or on a row, with how many rows use it.
export function deviceNames(state) {
  const byKey = new Map();
  const add = (name, rows) => {
    const key = fold(name).replace(/\s+/g, ' ').trim();
    if (!key) return;
    const d = byKey.get(key) ?? byKey.set(key, { name: name.trim(), rows: 0, listed: false }).get(key);
    d.rows += rows;
    return d;
  };
  for (const d of state.devices) add(d.name, 0).listed = true;
  for (const inv of state.invoices) for (const r of inv.rows) if (r.device) add(r.device, 1);
  return [...byKey.values()].sort((a, b) => b.rows - a.rows || a.name.localeCompare(b.name, 'tr'));
}

// Suggested merges: names that only differ by generic words or punctuation.
// The name kept is a default device's, else the most used one.
export function similarDevices(state) {
  const groups = new Map();
  for (const d of deviceNames(state)) {
    const key = similarKey(d.name);
    if (key) groups.set(key, [...(groups.get(key) ?? []), d]);
  }
  const defaults = new Set(DEFAULT_DEVICES.map((d) => fold(d.name)));
  const pairs = [];
  for (const group of groups.values()) {
    if (group.length < 2) continue;
    const keep = group.find((d) => defaults.has(fold(d.name))) ?? group[0];
    for (const d of group) if (d !== keep) pairs.push({ from: d.name, to: keep.name, rows: d.rows });
  }
  return pairs;
}
