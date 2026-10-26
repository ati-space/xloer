// Invoice tracking ("Faturalar"): one entry per project with its test rows
// (test, device, dates). Days are counted from the dates, both ends included;
// shifts are days × 3 because the lab runs three shifts a day.

export const SHIFTS_PER_DAY = 3;

export const DEFAULT_DEVICES = [
  { name: 'Solar Kabin', test: 'İklimlendirme Testi' },
  { name: '70kN Sarsıcı', test: 'Titreşim Testi' },
  { name: '10kN Sarsıcı', test: 'Titreşim Testi' },
];

export const DEFAULT_TEST_TYPES = ['İklimlendirme Testi', 'Titreşim Testi', 'Mekanik Şok Testi', 'Titreşim ve Mekanik Şok Testi'];

const DAY = 24 * 60 * 60 * 1000;
const utc = (iso) => {
  const [y, m, d] = iso.split('-').map(Number);
  return Date.UTC(y, m - 1, d);
};

export function rowDays(row) {
  if (!row?.start) return 0;
  const end = row.end && row.end > row.start ? row.end : row.start;
  return Math.round((utc(end) - utc(row.start)) / DAY) + 1;
}

// "14/42": days / shifts.
export function shiftLabel(row) {
  const days = rowDays(row);
  return days ? `${days}/${days * SHIFTS_PER_DAY}` : '';
}

export function formatDate(iso) {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  return `${d}.${m}.${y}`;
}

// "15.09.2026 - 28.09.2026", or a single date for a one-day test.
export function dateRangeLabel(row) {
  if (!row?.start) return '';
  return row.end && row.end !== row.start ? `${formatDate(row.start)} - ${formatDate(row.end)}` : formatDate(row.start);
}

// Rows in date order; rows without a date go last.
export function sortedRows(invoice) {
  return [...invoice.rows].sort((a, b) => (a.start ?? '9999').localeCompare(b.start ?? '9999'));
}

// The day the project was last tested: the latest end or start of its rows.
export function lastTestDate(invoice) {
  return invoice.rows.reduce((max, r) => {
    const d = r.end ?? r.start;
    return d && (!max || d > max) ? d : max;
  }, null);
}

// The year a project's amount belongs to: the one chosen on its card (as the
// sheet kept a table per year), else the year of its last test. A project
// tested in late 2025 and paid in 2026 is simply set to 2026.
export function billingYear(invoice) {
  if (Number.isInteger(invoice.year)) return invoice.year;
  const d = lastTestDate(invoice);
  return d ? Number(d.slice(0, 4)) : null;
}

// Year and, when its last test fell in that year, the month it counts in.
// Tested in another year or not at all: no month ("Devir" on the Pano).
export function billingPeriod(invoice) {
  const year = billingYear(invoice);
  if (year == null) return null;
  const d = lastTestDate(invoice);
  return { year, month: d && Number(d.slice(0, 4)) === year ? Number(d.slice(5, 7)) : null };
}

export const INVOICE_ORDERS = {
  no: 'No · yeniden eskiye',
  'no-asc': 'No · eskiden yeniye',
  date: 'Test tarihi · yeniden eskiye',
  'date-asc': 'Test tarihi · eskiden yeniye',
};

// Cards in the chosen order. By date, projects without dates go last and
// equal dates fall back to the No, in the same direction.
export function sortInvoices(invoices, order = 'no') {
  const dir = order === 'no-asc' || order === 'date-asc' ? 1 : -1;
  const byNo = (a, b) => dir * (a.no - b.no);
  if (order !== 'date' && order !== 'date-asc') return [...invoices].sort(byNo);
  const keyed = invoices.map((inv) => ({ inv, d: lastTestDate(inv) }));
  keyed.sort((a, b) => {
    if (!a.d || !b.d) return !a.d - !b.d || byNo(a.inv, b.inv);
    return dir * a.d.localeCompare(b.d) || byNo(a.inv, b.inv);
  });
  return keyed.map((k) => k.inv);
}

// "P26-0001-0001-MÜŞTERİ Test Adı (TEST260101ABC01REV0)", as in the sheet.
export function invoiceTitle(invoice) {
  const head = [invoice.code, invoice.title].filter(Boolean).join('-');
  return invoice.ref ? `${head} (${invoice.ref})` : head;
}

// The card's one heading field back into its parts: the project code at the
// start, the report no in the closing brackets (it starts with TEST, so
// "Yağmur Testi (IPX2)" stays a title), the rest is customer and test name.
export function parseInvoiceTitle(text) {
  let rest = String(text ?? '').replace(/\s+/g, ' ').trim();
  let ref = '';
  const paren = /\((TEST[^()]*)\)\s*$/i.exec(rest);
  if (paren) {
    ref = paren[1].trim();
    rest = rest.slice(0, paren.index).trim();
  }
  const code = /^P\d{2}-\d{1,5}-\d{1,5}/i.exec(rest)?.[0] ?? '';
  if (code) rest = rest.slice(code.length).replace(/^[\s\-–:]+/, '');
  return { code: code.toUpperCase(), title: rest, ref };
}

const amountFormat = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const formatAmount = (n) => (n == null ? '' : amountFormat.format(n));
export const formatEuro = (n) => `€ ${amountFormat.format(n ?? 0)}`;
const roundFormat = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 });
// Whole euros, for tight spots such as the counters above a list.
export const formatEuroShort = (n) => `€ ${roundFormat.format(Math.round(n ?? 0))}`;

// Accepts "16.600,00", "16600", "16600.5", "€ 1.620": returns euros or null.
export function parseAmount(text) {
  let s = String(text ?? '').replace(/[€\s]/g, '');
  if (!s) return null;
  if (s.includes(',')) s = s.replace(/\./g, '').replace(',', '.');
  else if ((s.match(/\./g) ?? []).length > 1 || /\.\d{3}$/.test(s)) s = s.replace(/\./g, '');
  const n = Number(s);
  return Number.isFinite(n) && n >= 0 ? Math.round(n * 100) / 100 : null;
}

export function totalAmount(invoices) {
  return Math.round(invoices.reduce((sum, i) => sum + (i.amount ?? 0), 0) * 100) / 100;
}

const INTRO = 'Gerçekleştirilen testler ve tarihleri aşağıdaki gibidir;';
const HEADERS = ['Test', 'Test Sistemi', 'Test Tarihi', 'Gün / Vardiya'];
const WIDTHS = ['160pt', '230pt', '200pt', '75pt'];

const escHtml = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

// What the copy button puts on the clipboard: the sentence and the table the
// reports e-mail uses (yellow header rows), as HTML for Outlook/Word and as
// tab-separated text for everything else.
export function invoiceCopy(invoice) {
  const rows = sortedRows(invoice).map((r) => [r.test, r.device, dateRangeLabel(r), shiftLabel(r)]);
  const font = 'font-family:Calibri,Arial,sans-serif;font-size:11pt';
  const cell = (text, i, { head = false, center = false } = {}) =>
    `<td style="border:1px solid #000;padding:1pt 5pt;${head ? 'background:#FFFF00;font-weight:bold;' : ''}${center ? 'text-align:center;' : ''}width:${WIDTHS[i]}">${escHtml(text)}</td>`;
  const html = [
    `<p style="margin:0 0 8pt;${font};font-weight:bold">${INTRO}</p>`,
    `<table border="1" cellspacing="0" cellpadding="0" style="border-collapse:collapse;${font}">`,
    `<tr>${cell('Proje Kodu', 0, { head: true, center: true })}${cell('', 1, { head: true })}${cell(invoice.code, 2, { head: true, center: true })}${cell('', 3, { head: true })}</tr>`,
    `<tr>${HEADERS.map((h, i) => cell(h, i, { head: true })).join('')}</tr>`,
    ...rows.map((r) => `<tr>${r.map((v, i) => cell(v, i)).join('')}</tr>`),
    '</table>',
  ].join('\n');
  const text = [INTRO, '', `Proje Kodu\t\t${invoice.code}\t`, HEADERS.join('\t'), ...rows.map((r) => r.join('\t'))].join('\n');
  return { html, text };
}
