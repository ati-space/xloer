import { test } from 'node:test';
import assert from 'node:assert/strict';
import zlib from 'node:zlib';
import { readWorkbook } from '../src/shared/xlsx.js';
import { parseInvoiceWorkbook, parseTestDates } from '../src/shared/invoice-import.js';
import { dispatch, emptyState } from '../src/shared/store.js';
import { buildXlsx, invoiceWorkbook } from './xlsx-fixture.js';

const inflate = async (bytes) => new Uint8Array(zlib.inflateRawSync(bytes));

test('xlsx reader: shared strings, numbers, entities and empty cells', async () => {
  const bytes = buildXlsx({ 1: { A: 'Ölçüm & Rapor <1>', B: 42.5 }, 3: { C: 'son' } });
  const [sheet] = await readWorkbook(bytes, inflate);
  assert.equal(sheet.name, 'Faturalar');
  assert.deepEqual(sheet.rows[0].slice(0, 2), ['Ölçüm & Rapor <1>', 42.5]);
  assert.equal(sheet.rows[1], undefined);
  assert.equal(sheet.rows[2][2], 'son');
  assert.equal(sheet.rows[0][6], undefined, 'a styled empty cell is skipped');
});

test('xlsx reader rejects files that are not workbooks', async () => {
  await assert.rejects(readWorkbook(new TextEncoder().encode('hello'), inflate), /Excel/);
});

test('test dates: ranges, single days, comma lists, real date cells and year typos', () => {
  assert.deepEqual(parseTestDates('15.09.2026 - 28.09.2026'), [{ start: '2026-09-15', end: '2026-09-28' }]);
  assert.deepEqual(parseTestDates('9.10.2026'), [{ start: '2026-10-09', end: null }]);
  assert.deepEqual(parseTestDates('09.10.2026, 11.10.2026'), [
    { start: '2026-10-09', end: null },
    { start: '2026-10-11', end: null },
  ]);
  assert.deepEqual(parseTestDates(46288), [{ start: '2026-09-23', end: null }]);
  assert.deepEqual(parseTestDates('25.12.205 – 26.12.2025'), [{ start: '2025-12-25', end: '2025-12-26' }]);
  assert.deepEqual(parseTestDates('2.04.2026-03.04.2026'), [{ start: '2026-04-02', end: '2026-04-03' }]);
  assert.deepEqual(parseTestDates('22.05.206'), [{ start: '2026-05-22', end: null }]);
  assert.deepEqual(parseTestDates('-'), []);
  assert.deepEqual(parseTestDates('belirsiz'), []);
});

test('invoice workbook: every block with its code, No, title, report no, tests and summed amount', async () => {
  const projects = parseInvoiceWorkbook(await readWorkbook(invoiceWorkbook(), inflate));
  assert.equal(projects.length, 3);
  const [a, b, c] = projects;
  assert.deepEqual(
    { no: a.no, code: a.code, title: a.title, ref: a.ref, amount: a.amount },
    { no: 56, code: 'P26-0001-0001', title: 'ÖRNEK UN38.3 Testleri', ref: 'TEST260101ORN01REV1', amount: 16600 },
  );
  assert.deepEqual(
    a.rows.map((r) => [r.test, r.device, r.start, r.end]),
    [
      ['İklimlendirme Testi', 'Solar Kabin', '2026-09-15', '2026-09-28'],
      ['Titreşim Testi', '70kN Sarsıcı', '2026-09-30', '2026-10-03'],
      ['Mekanik Şok Testi', '70kN Sarsıcı', '2026-10-08', '2026-10-09'],
    ],
    'the total row is not a test row',
  );
  assert.equal(b.no, 57);
  assert.equal(b.code, 'P26-0002-0002', 'the title’s code beats a code cell copied from the block above');
  assert.equal(b.amount, 2720, 'both totals of a split invoice, the last after blank rows');
  assert.deepEqual(
    b.rows.map((r) => [r.test, r.start]),
    [
      ['Titreşim ve Mekanik Şok Testi', '2026-09-23'],
      ['Titreşim ve Mekanik Şok Testi', '2026-10-09'],
      ['Titreşim ve Mekanik Şok Testi', '2026-10-11'],
      ['Titreşim Testi', '2026-10-12'],
    ],
  );
  assert.equal(c.amount, 4500, 'item numbers and item prices are not the total');
  assert.deepEqual(
    c.rows.map((r) => [r.test, r.device, r.start, r.end]),
    [
      ['Burst Test', 'Patlatma Düzeneği', '2026-02-04', null],
      ['Test Raporu', 'Her grup için bir rapor', null, null],
      ['Burst Test', 'Patlatma Düzeneği', '2026-02-10', '2026-02-12'],
    ],
    'the sub-heading is skipped, the undated note is kept',
  );
});

test('importing adds new projects, updates matching ones and learns devices from dated rows', async () => {
  const projects = parseInvoiceWorkbook(await readWorkbook(invoiceWorkbook(), inflate));
  const s = emptyState();
  const existing = dispatch(s, { type: 'invoice/create', code: 'p26-0001-0001', title: 'eski' });
  dispatch(s, { type: 'invoice/update', id: existing, patch: { no: 3, amount: 1 } });
  const r = dispatch(s, { type: 'invoice/import', projects });
  assert.deepEqual(r, { created: 2, updated: 1, devicesAdded: 3, testsAdded: 1 });
  const updated = s.invoices.find((i) => i.id === existing);
  assert.equal(updated.title, 'ÖRNEK UN38.3 Testleri');
  assert.equal(updated.amount, 16600);
  assert.equal(updated.no, 56, 'the sheet’s No wins');
  assert.equal(updated.rows.length, 3);
  assert.ok(s.devices.some((d) => d.name === '35kN Sarsıcı' && d.test === 'Titreşim ve Mekanik Şok Testi'));
  assert.ok(s.devices.some((d) => d.name === 'Patlatma Düzeneği'));
  assert.ok(!s.devices.some((d) => d.name === 'Her grup için bir rapor'), 'a note is not a device');
  assert.deepEqual(s.testTypes.slice(emptyState().testTypes.length), ['Burst Test'], 'a double space is not a new test type');
  const again = dispatch(s, { type: 'invoice/import', projects });
  assert.deepEqual(again, { created: 0, updated: 3, devicesAdded: 0, testsAdded: 0 });
  assert.equal(s.invoices.length, 3);
});

test('an import can be undone', async () => {
  const projects = parseInvoiceWorkbook(await readWorkbook(invoiceWorkbook(), inflate));
  const s = emptyState();
  const id = dispatch(s, { type: 'invoice/create', code: 'P26-0001-0001', title: 'eski' });
  const before = structuredClone({ invoices: s.invoices, devices: s.devices, testTypes: s.testTypes });
  dispatch(s, { type: 'invoice/import', projects });
  dispatch(s, { type: 'invoice/import-undo', ...before });
  assert.deepEqual(s.invoices.map((i) => [i.id, i.title, i.rows.length]), [[id, 'eski', 0]]);
  assert.deepEqual(s.devices, before.devices);
});
