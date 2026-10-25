import { test } from 'node:test';
import assert from 'node:assert/strict';
import { dispatch, emptyState } from '../src/shared/store.js';
import { invoiceIssues } from '../src/shared/invoice-issues.js';
import { readWorkbook } from '../src/shared/xlsx.js';
import { parseInvoiceWorkbook } from '../src/shared/invoice-import.js';
import zlib from 'node:zlib';
import { buildXlsx } from './xlsx-fixture.js';

const inflate = async (bytes) => new Uint8Array(zlib.inflateRawSync(bytes));
const TODAY = '2026-10-25';

// One block with every kind of trouble the real sheet had.
const sheet = () =>
  buildXlsx({
    10: { A: 'No', B: 'P26-0009-0009-ÖRNEK Çevresel Testler (TEST260909ORN01)' },
    11: { A: 9, B: 'Proje Kodu', D: 'P26-0001-0001' },
    12: { B: 'Test', C: 'Test Sistemi', D: 'Test Tarihi', E: 'Gün / Vardiya' },
    13: { B: 'Titreşim Testi', C: '70kN Sarsıcı', D: '01.06.2026 - 02.06.2026', E: '1/3' },
    14: { B: 'Mekanik Şok Testi', C: '70kN Sarsıcı', D: 46282, E: '3/9' },
    15: { B: 'Korozyon Testi', C: 'Korozyon Kabini', D: '23.03.206 - 27.03.2026', E: '5/15' },
    16: { B: 'İklimlendirme Testi', C: 'Solar Kabin', D: 47390, E: '1/3' },
    17: { C: 'GTL Sıvı testleri', D: '-', E: '-' },
    18: { B: 'Titreşim Testi', C: '70kN Sarsıcı', D: '09.10.2026', E: '1/3' },
  });

async function imported() {
  const s = emptyState();
  dispatch(s, { type: 'invoice/import', projects: parseInvoiceWorkbook(await readWorkbook(sheet(), inflate)) });
  return s;
}

test('Excel dates: a single date with more Gün / Vardiya days gets its end', async () => {
  const [inv] = (await imported()).invoices;
  const row = inv.rows.find((r) => r.test === 'Mekanik Şok Testi');
  assert.deepEqual([row.start, row.end, row.endFromDays, row.sheetDays], ['2026-09-17', '2026-09-19', true, 3]);
  const plain = inv.rows.find((r) => r.start === '2026-10-09');
  assert.deepEqual([plain.end, plain.endFromDays], [null, undefined], 'a one-day test stays one day');
});

test('checks: every kind, each with its fix', async () => {
  const s = await imported();
  const [inv] = s.invoices;
  const issues = invoiceIssues(inv, TODAY);
  const kinds = issues.map((i) => i.key.split(':')[0]);
  assert.deepEqual(kinds.sort(), ['ahead', 'amount', 'code', 'days', 'end', 'undated', 'year']);
  const byKind = (k) => issues.find((i) => i.key.startsWith(k));
  assert.match(byKind('code').text, /P26-0001-0001/);
  assert.match(byKind('days').text, /2 gün, Excel’de Gün \/ Vardiya 1\/3/);
  assert.match(byKind('year').text, /23\.03\.206/);
  assert.match(byKind('undated').text, /“-”/);
  assert.match(byKind('ahead').text, /2029/);

  // One click: the code cell, the end that matches the sheet, a single day.
  for (const k of ['code', 'days', 'end']) for (const a of byKind(k).fixes[0].actions) dispatch(s, a);
  const after = invoiceIssues(s.invoices[0], TODAY).map((i) => i.key.split(':')[0]);
  assert.deepEqual(after.sort(), ['ahead', 'amount', 'undated', 'year']);
  assert.equal(s.invoices[0].code, 'P26-0001-0001');

  // "Yoksay" hides one for good; editing a row's dates drops the year note.
  dispatch(s, { type: 'invoice/dismiss-issue', id: inv.id, key: byKind('ahead').key });
  const yearRow = s.invoices[0].rows.find((r) => r.yearFixed);
  dispatch(s, { type: 'invoice/row-update', id: inv.id, rowId: yearRow.id, patch: { start: '2026-03-23' } });
  assert.deepEqual(invoiceIssues(s.invoices[0], TODAY).map((i) => i.key.split(':')[0]).sort(), ['amount', 'undated']);
});

test('hand-made cards have no Excel checks', () => {
  const s = emptyState();
  const id = dispatch(s, { type: 'invoice/create', code: 'P26-1' });
  dispatch(s, { type: 'invoice/row-add', id, row: { device: 'Solar Kabin', start: '2026-10-01' } });
  assert.deepEqual(invoiceIssues(s.invoices[0], TODAY), []);
});
