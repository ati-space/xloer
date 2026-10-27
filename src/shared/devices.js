// Test system names as used on the cards, and which names are one system
// written two ways. A system is told apart by its capacity and model
// ("70kN", "TH-1728"); words that only describe its kind ("Elektrodinamik",
// "Sarsıcı", "Kabin") and punctuation do not. So "70kN Elektrodinamik
// Sarsıcı" and "70kN Sarsıcı" are one system; "70kN Sarsıcı" and
// "10kN Sarsıcı", or "ASLI TH-1728" and "ASLI TH-1300-F", are two.

import { fold } from './search.js';
import { DEFAULT_DEVICES } from './invoices.js';

// Words that describe the kind of system rather than which one it is.
const GENERIC = new Set(['elektrodinamik', 'sarsici', 'kabin', 'kabini', 'sistemi', 'sistem', 'test']);

// Systems known to be one although their names say otherwise: every
// shaker named 50kN is the 49kN one.
const SAME_AS = [{ test: (f) => /(^|\s)50\s?kn(\s|$)/.test(f) && f.includes('sarsici'), name: '49kN Sarsıcı' }];

export function systemKey(name) {
  const f = fold(name);
  const same = SAME_AS.find((s) => s.test(f));
  if (same) return systemKey(same.name);
  return f
    .split(/[^\p{L}\p{N}]+/u)
    .filter((w) => w && !GENERIC.has(w))
    .join('');
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

// Names of one system: [{ to, from: [names] }]. The name kept ("the main
// test system") is a default system's (70kN / 49kN / 10kN Sarsıcı, Solar
// Kabin), else the most used one.
export function similarGroups(state) {
  const groups = new Map();
  for (const d of deviceNames(state)) {
    const key = systemKey(d.name);
    if (key) groups.set(key, [...(groups.get(key) ?? []), d]);
  }
  const defaults = DEFAULT_DEVICES.map((d) => d.name);
  const out = [];
  for (const [key, group] of groups) {
    const main = defaults.find((n) => systemKey(n) === key);
    if (group.length < 2 && !(main && fold(group[0].name) !== fold(main))) continue;
    const to = main ?? group[0].name;
    const from = group.filter((d) => fold(d.name) !== fold(to));
    if (from.length) out.push({ to, from: from.map((d) => d.name), rows: from.reduce((n, d) => n + d.rows, 0) });
  }
  return out;
}

// The same as pairs, for the "Aynı test sistemi olabilir" suggestions;
// nothing is merged until the user says so.
export function similarDevices(state) {
  const rows = new Map(deviceNames(state).map((d) => [fold(d.name), d.rows]));
  return similarGroups(state).flatMap((g) => g.from.map((from) => ({ from, to: g.to, rows: rows.get(fold(from)) ?? 0 })));
}

// The merges 1.18.0 made: it dropped every word with a digit and
// "Elektrodinamik", so "70kN Sarsıcı" became "Sarsıcı" and "ASLI TH-1728"
// "ASLI". Its aliases are the ones that rule makes.
const rule118 = (name) =>
  String(name ?? '')
    .trim()
    .split(/\s+/)
    .filter((w) => w && !/\d/.test(w) && fold(w) !== 'elektrodinamik')
    .join(' ');
export const merges118 = (state) => state.deviceAliases.filter((a) => fold(rule118(a.from)) === fold(a.to) && fold(a.from) !== fold(a.to));
