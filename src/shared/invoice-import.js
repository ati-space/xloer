// Reads the invoice workbook's project blocks:
//
//   No | P26-…-<customer and test name> (<report no>)
//   56 | Proje Kodu |              | P26-0001-0001 |
//      | Test       | Test Sistemi | Test Tarihi   | Gün / Vardiya
//      | <test>     | <device>     | 15.09.2026 - 28.09.2026 | 14/42
//      |            |              | Toplam Kesilen Fatura (€) |  | 16.600,00
//
// Blocks are found by their labels, not by fixed rows or columns, so merged
// cells, blank rows and extra columns do not matter. A block runs until the
// next block's title; inside it every "Toplam" row (or a row holding only a
// number) adds to the amount, so an invoice split in two is summed.
//
// Xloer counts days from the dates; the sheet's Gün / Vardiya is kept on the
// row to check them. A single date whose Gün / Vardiya says more days gets
// its end from that count, so no test is left without its full dates.

import { fold } from './search.js';
import { parseAmount } from './invoices.js';

const CODE_RE = /^P\d{2}-\d{1,5}-\d{1,5}/i;
const DATE_RE = /(\d{1,2})[./](\d{1,2})[./](\d{2,4})(?!\d)/g;

const label = (v) => (typeof v === 'string' ? fold(v).replace(/\s+/g, ' ').trim() : '');
const text = (v) => (v == null || typeof v === 'boolean' ? '' : String(v).replace(/\s+/g, ' ').trim());

// "2026", "26", and the dropped-digit typo "206" (→ 2026, noted in `info`).
function fullYear(y, info) {
  if (y.length === 4) return Number(y);
  if (y.length === 3 && y.startsWith('20')) {
    info.yearFixed = true;
    return 2020 + Number(y[2]);
  }
  if (y.length === 2) return 2000 + Number(y);
  return NaN;
}

const addDays = (iso, n) => new Date(Date.parse(`${iso}T00:00:00Z`) + n * 86400000).toISOString().slice(0, 10);

function isoDate(y, m, d) {
  if (!(y >= 2000 && y <= 2100) || m < 1 || m > 12 || d < 1 || d > 31) return null;
  return `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

// Excel stores a real date cell as days since 1899-12-30.
function serialDate(n) {
  const d = new Date(Math.round((n - 25569) * 86400000));
  return isoDate(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate());
}

// "15.09.2026 - 28.09.2026", "09.10.2026" or a date cell → [{start, end}].
// Dates separated by commas are separate test days, so separate rows.
// `info.yearFixed` is set when a mistyped year was read.
export function parseTestDates(value, info = {}) {
  if (typeof value === 'number') {
    const start = value > 20000 && value < 80000 ? serialDate(value) : null;
    return start ? [{ start, end: null }] : [];
  }
  const out = [];
  for (const part of text(value).split(/[,;\n]|\s+ve\s+/i)) {
    const dates = [...part.matchAll(DATE_RE)].map(([, d, m, y]) => isoDate(fullYear(y, info), Number(m), Number(d))).filter(Boolean);
    if (dates.length) out.push({ start: dates[0], end: dates.length > 1 && dates.at(-1) !== dates[0] ? dates.at(-1) : null });
  }
  return out;
}

function headerColumns(row) {
  if (!row) return null;
  const at = (name) => row.findIndex((v) => label(v).replace(/\s+/g, '') === name);
  const cols = { test: at('test'), device: at('testsistemi'), date: at('testtarihi'), days: at('gun/vardiya') };
  return cols.test >= 0 && cols.device >= 0 && cols.date >= 0 ? cols : null;
}

const isTitleRow = (row) => (row ?? []).some((v) => label(v) === 'no');
const isCodeRow = (row) => (row ?? []).some((v) => label(v) === 'proje kodu');

// The block's first row: its "No" title row, else its "Proje Kodu" row.
function blockTop(rows, h) {
  for (let k = h - 1; k >= Math.max(0, h - 3); k--) if (isTitleRow(rows[k])) return k;
  for (let k = h - 1; k >= Math.max(0, h - 3); k--) if (isCodeRow(rows[k])) return k;
  return h;
}

// The money in a total row: its last number, or a "€ 1.620,00" text.
function rowAmount(row) {
  let amount = null;
  for (const v of row) {
    if (typeof v === 'number') amount = v;
    else if (typeof v === 'string' && /\d/.test(v) && /€|eur/i.test(v)) amount = parseAmount(v) ?? amount;
  }
  return amount;
}

function readBlock(rows, h, cols, end) {
  let code = '';
  let no = null;
  let title = '';
  let noColumn = -1;
  for (let k = h - 1; k >= Math.max(0, h - 3); k--) {
    const row = rows[k] ?? [];
    const codeAt = row.findIndex((v) => label(v) === 'proje kodu');
    if (codeAt >= 0 && !code) code = text(row.slice(codeAt + 1).find((v) => text(v)));
    if (isTitleRow(row)) {
      noColumn = row.findIndex((v) => label(v) === 'no');
      title = text(row.filter((v) => typeof v === 'string' && label(v) !== 'no').sort((a, b) => b.length - a.length)[0]);
      break;
    }
  }
  // The number sits under "No" (often a merged cell spanning the code row).
  for (let k = h - 2; k <= h && no == null; k++) {
    const v = (rows[k] ?? [])[noColumn >= 0 ? noColumn : 0];
    const n = typeof v === 'number' ? v : Number.parseInt(text(v), 10);
    if (Number.isInteger(n) && n > 0) no = n;
  }

  const tests = [];
  let amount = null;
  for (let k = h + 1; k < end; k++) {
    const row = rows[k] ?? [];
    const test = text(row[cols.test]);
    const device = text(row[cols.device]);
    const info = {};
    const ranges = parseTestDates(row[cols.date], info);
    const total = row.some((v) => label(v).startsWith('toplam'));
    const bare = !total && !test && !device && !text(row[cols.date]);
    if (total || bare) {
      const n = rowAmount(row);
      if (n != null) amount = (amount ?? 0) + n;
      continue;
    }
    // A test row has a date or a test system; a lone label is a sub-heading.
    if (!ranges.length && !device) continue;
    const days = cols.days >= 0 ? /^(\d+)\s*\/\s*\d+$/.exec(text(row[cols.days])) : null;
    const sheetDays = days ? Number(days[1]) : null;
    const dateText = typeof row[cols.date] === 'string' ? text(row[cols.date]) : '';
    for (const range of ranges.length ? ranges : [{ start: null, end: null }]) {
      const r = { test, device, ...range };
      // Gün / Vardiya belongs to the cell; it can only be checked on one range.
      if (ranges.length === 1 && sheetDays != null) r.sheetDays = sheetDays;
      if (ranges.length === 1 && !r.end && sheetDays > 1) {
        r.end = addDays(r.start, sheetDays - 1);
        r.endFromDays = true;
      }
      if (info.yearFixed) r.yearFixed = true;
      if ((info.yearFixed || !ranges.length) && dateText) r.sheetDate = dateText;
      tests.push(r);
    }
  }

  let ref = '';
  const paren = /\(([^()]+)\)\s*$/.exec(title);
  if (paren) {
    ref = paren[1].trim();
    title = title.slice(0, paren.index).trim();
  }
  // The title starts with the code. When it disagrees with the "Proje Kodu"
  // cell, the title wins: that cell is the one copied from the block above.
  const titleCode = CODE_RE.exec(title)?.[0] ?? '';
  let sheetCode = null;
  if (titleCode) {
    title = title.slice(titleCode.length).replace(/^[\s\-–:]+/, '');
    if (code && code.toUpperCase() !== titleCode.toUpperCase()) sheetCode = code;
    code = titleCode.toUpperCase();
  }
  if (!code && !title && !tests.length) return null;
  return { no, code, sheetCode, title, ref, amount: amount != null && amount >= 0 ? Math.round(amount * 100) / 100 : null, rows: tests };
}

// Every project block on every sheet, in sheet order.
export function parseInvoiceWorkbook(sheets) {
  const projects = [];
  for (const { rows } of sheets) {
    const heads = [];
    for (let r = 0; r < rows.length; r++) {
      const cols = headerColumns(rows[r]);
      if (cols) heads.push({ r, cols });
    }
    heads.forEach(({ r, cols }, i) => {
      const end = i + 1 < heads.length ? blockTop(rows, heads[i + 1].r) : rows.length;
      const project = readBlock(rows, r, cols, end);
      if (project) projects.push(project);
    });
  }
  return projects;
}
