// Who a project is for. The sheet has no customer column, but every title
// starts with the company: "ACME CONVERTER Titreşim Testleri". The guess
// is the title's leading capitalised words (two at most), stopping at a
// standard (EN, ISO, …) or a model number; projects that share a first word
// get what their guesses have in common, so "ACME INVERTER" and "ACME"
// are both "ACME". A name typed on the card always wins.

import { fold } from './search.js';

const STANDARDS = new Set(['EN', 'ISO', 'IEC', 'TS', 'ECE', 'MIL', 'DIN', 'IP', 'IPX', 'UN', 'SAE', 'ASTM', 'RTCA', 'DO']);

const isCaps = (w) => /\p{L}/u.test(w) && !/\d/.test(w) && w === w.toLocaleUpperCase('tr') && !STANDARDS.has(w.replace(/[^\p{L}]/gu, ''));

export function guessCustomer(title) {
  const words = String(title ?? '').trim().split(/\s+/).filter(Boolean);
  const lead = [];
  for (const w of words) {
    if (lead.length === 2 || !isCaps(w)) break;
    lead.push(w);
  }
  return lead.length ? lead.join(' ') : (words[0] ?? '');
}

export const customerOf = (inv) => inv.customer?.trim() || guessCustomer(inv.title);
export const customerKey = (name) => fold(name).replace(/\s+/g, ' ').trim();

// Sets `customer` on each of `targets`, looking at every title in `all` to
// find the shared company name.
export function assignCustomers(targets, all) {
  const groups = new Map();
  for (const inv of all) {
    const words = guessCustomer(inv.title).split(' ').filter(Boolean);
    if (!words.length) continue;
    const key = fold(words[0]);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(words);
  }
  for (const inv of targets) {
    const words = guessCustomer(inv.title).split(' ').filter(Boolean);
    if (!words.length) continue;
    const group = groups.get(fold(words[0])) ?? [words];
    let n = words.length;
    for (const other of group) {
      let k = 0;
      while (k < n && k < other.length && fold(other[k]) === fold(words[k])) k++;
      n = k;
    }
    // Prefer the spelling in capitals when the group mixes "ACME" and "Acme".
    const caps = group.find((w) => w[0] === w[0].toLocaleUpperCase('tr')) ?? words;
    inv.customer = caps.slice(0, Math.max(1, n)).join(' ');
  }
}
