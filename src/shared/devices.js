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

// A size: a number with an optional unit ("70kN", "1000L", "1m3"). Model
// numbers ("TH-1728", "TH-1300-F") are not sizes; they tell systems apart.
const isSize = (w) => /^[+-]?\d+(?:[.,]\d+)?(?:kn|n|kg|t|ton|l|lt|m3|kw|w|°c|mm|cm|m)?$/.test(w.toLocaleLowerCase('tr'));

// A name without its size, and without "Elektrodinamik", which every such
// shaker is: "70kN Elektrodinamik Sarsıcı" → "Sarsıcı".
export function baseDeviceName(name) {
  return String(name ?? '')
    .trim()
    .split(/\s+/)
    .filter((w) => w && !isSize(w) && fold(w) !== 'elektrodinamik')
    .join(' ');
}

// Systems that differ only by size, at least two names: [{ to, from: [names] }],
// "to" being the size-less name (as one of them writes it, if any does).
export function sizeVariants(state) {
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

// 1.18.0 merged once, by dropping every word with a digit in it, so model
// numbers went too ("ASLI TH-1728" → "ASLI"). Those merges: aliases that the
// 1.18.0 rule makes but the size rule does not.
const rule118 = (name) =>
  String(name ?? '')
    .trim()
    .split(/\s+/)
    .filter((w) => w && !/\d/.test(w) && fold(w) !== 'elektrodinamik')
    .join(' ');
export const wrongMerges = (state) =>
  state.deviceAliases.filter((a) => fold(rule118(a.from)) === fold(a.to) && fold(baseDeviceName(a.from)) !== fold(a.to));

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

// Suggested merges ("Aynı test sistemi olabilir"); nothing is merged until
// the user says so. Names that differ only by size go under the size-less
// name; names that differ by generic words or punctuation go under a
// default system's name, else the most used one.
export function similarDevices(state) {
  const pairs = [];
  const seen = new Set();
  const push = (p) => {
    if (seen.has(fold(p.from))) return;
    seen.add(fold(p.from));
    pairs.push(p);
  };
  const rows = new Map(deviceNames(state).map((d) => [fold(d.name), d.rows]));
  for (const g of sizeVariants(state)) for (const from of g.from) push({ from, to: g.to, rows: rows.get(fold(from)) ?? 0 });
  for (const p of genericPairs(state)) push(p);
  return pairs;
}

function genericPairs(state) {
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
