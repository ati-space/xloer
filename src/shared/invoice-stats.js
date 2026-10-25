// Numbers behind the Faturalar dashboard ("Pano"). The sheet has no invoice
// date, so a project's amount counts in the month of its last test. Test days
// and device time count on the days the tests ran, cut to the period.

import { fold } from './search.js';
import { SHIFTS_PER_DAY, lastTestDate } from './invoices.js';
import { customerKey, customerOf } from './customers.js';

export const MONTHS = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];

const DAY = 24 * 60 * 60 * 1000;
const toTime = (iso) => Date.UTC(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)) - 1, Number(iso.slice(8, 10)));
const toIso = (t) => new Date(t).toISOString().slice(0, 10);
const round2 = (n) => Math.round(n * 100) / 100;

// { from, to } (ISO dates, both included) for a year, or one month of it.
export function periodRange(year, month = null) {
  if (month == null) return { from: `${year}-01-01`, to: `${year}-12-31` };
  const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const mm = String(month).padStart(2, '0');
  return { from: `${year}-${mm}-01`, to: `${year}-${mm}-${last}` };
}

// Every day a row's test ran inside [from, to].
function rowDates(row, from, to) {
  if (!row.start) return [];
  const end = row.end && row.end > row.start ? row.end : row.start;
  const a = Math.max(toTime(row.start), toTime(from));
  const b = Math.min(toTime(end), toTime(to));
  const out = [];
  for (let t = a; t <= b; t += DAY) out.push(toIso(t));
  return out;
}

// Test days in the range, summed over rows (two tests on one day count twice,
// as on the invoice).
function testDays(invoices, from, to) {
  return invoices.reduce((n, inv) => n + inv.rows.reduce((m, r) => m + rowDates(r, from, to).length, 0), 0);
}

// Amount and projects billed in [from, to], and the test days run in it.
export function periodSummary(invoices, from, to) {
  const billed = invoices.filter((inv) => {
    const d = lastTestDate(inv);
    return d && d >= from && d <= to;
  });
  const days = testDays(invoices, from, to);
  return {
    from,
    to,
    amount: round2(billed.reduce((n, inv) => n + (inv.amount ?? 0), 0)),
    projects: billed.length,
    days,
    shifts: days * SHIFTS_PER_DAY,
  };
}

// Years that have a test date, newest first.
export function invoiceYears(invoices) {
  const years = new Set();
  for (const inv of invoices) for (const r of inv.rows) for (const d of [r.start, r.end]) if (d) years.add(Number(d.slice(0, 4)));
  return [...years].sort((a, b) => b - a);
}

export function monthlySummary(invoices, year) {
  return MONTHS.map((label, i) => {
    const { from, to } = periodRange(year, i + 1);
    return { month: i + 1, label, ...periodSummary(invoices, from, to) };
  });
}

// Oldest year first, like the months.
export function yearlySummary(invoices) {
  return invoiceYears(invoices)
    .reverse()
    .map((year) => {
      const { from, to } = periodRange(year);
      return { year, label: String(year), ...periodSummary(invoices, from, to) };
    });
}

// Projects with no test date: their amount is in no month.
export function undatedInvoices(invoices) {
  const list = invoices.filter((inv) => !lastTestDate(inv));
  return { projects: list.length, amount: round2(list.reduce((n, inv) => n + (inv.amount ?? 0), 0)) };
}

// How long each test system was in use in [from, to]: calendar days with at
// least one test on it (two tests on one day are one day of use), shifts,
// and the share of the period's days so far (`today` caps an open period).
export function deviceUsage(invoices, from, to, today = null) {
  const byDevice = new Map();
  for (const inv of invoices) {
    for (const r of inv.rows) {
      const name = r.device?.trim();
      if (!name) continue;
      const dates = rowDates(r, from, to);
      if (!dates.length) continue;
      const key = fold(name);
      let d = byDevice.get(key);
      if (!d) byDevice.set(key, (d = { device: name, dates: new Set(), tests: 0, projects: new Set() }));
      for (const x of dates) d.dates.add(x);
      d.tests++;
      d.projects.add(inv.id);
    }
  }
  const end = today && today < to ? today : to;
  const span = end >= from ? Math.round((toTime(end) - toTime(from)) / DAY) + 1 : 0;
  return [...byDevice.values()]
    .map((d) => ({
      device: d.device,
      days: d.dates.size,
      shifts: d.dates.size * SHIFTS_PER_DAY,
      tests: d.tests,
      projects: d.projects.size,
      share: span ? Math.min(1, d.dates.size / span) : null,
    }))
    .sort((a, b) => b.days - a.days || a.device.localeCompare(b.device, 'tr'));
}

// Per customer, the same figures as a period: amount and projects billed in
// [from, to], test days run in it. Customers with nothing in it are left out.
export function customerSummary(invoices, from, to) {
  const groups = new Map();
  for (const inv of invoices) {
    const name = customerOf(inv);
    if (!name) continue;
    const key = customerKey(name);
    if (!groups.has(key)) groups.set(key, { key, name, invoices: [] });
    groups.get(key).invoices.push(inv);
  }
  return [...groups.values()]
    .map((g) => ({ key: g.key, name: g.name, ...periodSummary(g.invoices, from, to) }))
    .filter((c) => c.amount || c.days)
    .sort((a, b) => b.amount - a.amount || b.days - a.days || a.name.localeCompare(b.name, 'tr'));
}
