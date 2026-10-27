import { test } from 'node:test';
import assert from 'node:assert/strict';
import { dispatch, emptyState, normalizeState } from '../src/shared/store.js';
import { dateRangeLabel, formatEuro, invoiceCopy, invoiceTitle, lastTestDate, parseAmount, parseInvoiceTitle, rowDays, shiftLabel, sortInvoices, totalAmount } from '../src/shared/invoices.js';

test('days count both ends; shifts are days × 3', () => {
  assert.equal(shiftLabel({ start: '2026-09-15', end: '2026-09-28' }), '14/42');
  assert.equal(shiftLabel({ start: '2026-09-30', end: '2026-10-03' }), '4/12');
  assert.equal(shiftLabel({ start: '2026-10-08', end: '2026-10-09' }), '2/6');
  assert.equal(shiftLabel({ start: '2026-10-09', end: null }), '1/3');
  assert.equal(shiftLabel({ start: '2026-10-09', end: '2026-10-09' }), '1/3');
  assert.equal(rowDays({ start: '2026-02-27', end: '2026-03-02' }), 4);
  assert.equal(shiftLabel({ start: null }), '');
});

test('dates, titles and euro amounts look like the sheet', () => {
  assert.equal(dateRangeLabel({ start: '2026-09-24', end: '2026-09-25' }), '24.09.2026 - 25.09.2026');
  assert.equal(dateRangeLabel({ start: '2026-10-09', end: null }), '09.10.2026');
  assert.equal(invoiceTitle({ code: 'P26-0001-0001', title: 'ÖRNEK Titreşim Testi', ref: 'TEST260101ORN01REV0' }), 'P26-0001-0001-ÖRNEK Titreşim Testi (TEST260101ORN01REV0)');
  assert.equal(formatEuro(373173.46), '€ 373.173,46');
  assert.equal(parseAmount('16.600,00'), 16600);
  assert.equal(parseAmount('€ 1.620'), 1620);
  assert.equal(parseAmount('930'), 930);
  assert.equal(parseAmount('2720.5'), 2720.5);
  assert.equal(parseAmount(''), null);
  assert.equal(parseAmount('abc'), null);
  assert.equal(totalAmount([{ amount: 16600 }, { amount: 2720 }, { amount: null }, { amount: 0.1 }, { amount: 0.2 }]), 19320.3);
});

test('copy: intro line, yellow header rows and one row per test, in date order', () => {
  const inv = {
    code: 'P26-0001-0001',
    rows: [
      { test: 'Titreşim Testi', device: '10kN Sarsıcı', start: '2026-10-09', end: null },
      { test: 'Titreşim Testi', device: '10kN Sarsıcı', start: '2026-09-24', end: '2026-09-25' },
    ],
  };
  const { text, html } = invoiceCopy(inv);
  assert.equal(
    text,
    [
      'Gerçekleştirilen testler ve tarihleri aşağıdaki gibidir;',
      '',
      'Proje Kodu\t\tP26-0001-0001\t',
      'Test\tTest Sistemi\tTest Tarihi\tGün / Vardiya',
      'Titreşim Testi\t10kN Sarsıcı\t24.09.2026 - 25.09.2026\t2/6',
      'Titreşim Testi\t10kN Sarsıcı\t09.10.2026\t1/3',
    ].join('\n'),
  );
  assert.match(html, /^<p [^>]*font-weight:bold">Gerçekleştirilen testler ve tarihleri aşağıdaki gibidir;<\/p>/);
  assert.equal((html.match(/background:#FFFF00/g) ?? []).length, 8);
  assert.equal((html.match(/<tr>/g) ?? []).length, 4);
  assert.ok(html.indexOf('24.09.2026 - 25.09.2026') < html.indexOf('09.10.2026<'));
});

test('invoice actions: numbering, rows, device → test, swapped dates, undo delete', () => {
  const s = emptyState();
  assert.deepEqual(s.devices.map((d) => d.name), ['Solar Kabin', 'Sarsıcı']);
  const a = dispatch(s, { type: 'invoice/create', code: 'P26-0001-0001', title: 'ÖRNEK' });
  dispatch(s, { type: 'invoice/update', id: a, patch: { no: 59 } });
  const b = dispatch(s, { type: 'invoice/create' });
  assert.equal(s.invoices.find((i) => i.id === b).no, 60);

  const r1 = dispatch(s, { type: 'invoice/row-add', id: a, row: { device: 'Solar Kabin', start: '2026-09-15', end: '2026-09-28' } });
  const inv = s.invoices.find((i) => i.id === a);
  assert.equal(inv.rows[0].test, 'İklimlendirme Testi');
  const r2 = dispatch(s, { type: 'invoice/row-add', id: a, row: { start: '2026-09-30' } });
  assert.equal(inv.rows.find((r) => r.id === r2).device, 'Solar Kabin');
  dispatch(s, { type: 'invoice/row-update', id: a, rowId: r2, patch: { device: 'Sarsıcı' } });
  assert.equal(inv.rows.find((r) => r.id === r2).test, 'Titreşim Testi');
  dispatch(s, { type: 'invoice/row-update', id: a, rowId: r2, patch: { test: 'Mekanik Şok Testi' } });
  dispatch(s, { type: 'invoice/row-update', id: a, rowId: r2, patch: { device: '10kN Sarsıcı' } });
  assert.equal(inv.rows.find((r) => r.id === r2).test, 'Mekanik Şok Testi', 'a hand-picked test stays');
  dispatch(s, { type: 'invoice/row-update', id: a, rowId: r1, patch: { start: '2026-10-03', end: '2026-09-30' } });
  assert.deepEqual([inv.rows[0].start, inv.rows[0].end], ['2026-09-30', '2026-10-03']);

  dispatch(s, { type: 'invoice/update', id: a, patch: { amount: 16600 } });
  const snapshot = structuredClone(inv);
  dispatch(s, { type: 'invoice/delete', id: a });
  assert.equal(s.invoices.length, 1);
  dispatch(s, { type: 'invoice/restore', invoice: snapshot });
  assert.equal(s.invoices.find((i) => i.id === a).amount, 16600);

  dispatch(s, { type: 'device/add', name: '35kN Sarsıcı', test: 'Titreşim Testi' });
  assert.throws(() => dispatch(s, { type: 'device/add', name: '35KN sarsıcı' }), /zaten var/);
  dispatch(s, { type: 'testtype/add', name: 'Tuz Sisi Testi' });
  dispatch(s, { type: 'device/remove', name: 'Solar Kabin' });
  assert.ok(!s.devices.some((d) => d.name === 'Solar Kabin'));
});

test('older data files get the default devices; saved lists are kept', () => {
  const old = normalizeState({ lists: [], tasks: [] });
  assert.equal(old.devices.length, 2);
  assert.equal(old.testTypes.length, 4);
  assert.deepEqual(old.invoices, []);
  const saved = normalizeState({
    lists: [],
    tasks: [],
    devices: [{ name: 'Tek Cihaz', test: 'X' }],
    testTypes: ['X'],
    invoices: [{ id: 'f1', no: 3, code: 'K', rows: [{ id: 'r1', device: 'Tek Cihaz', start: '2026-01-05', end: '2026-01-02' }] }],
  });
  assert.deepEqual(saved.devices, [{ name: 'Tek Cihaz', test: 'X' }]);
  assert.equal(saved.invoices[0].rows[0].end, null, 'an end before the start is dropped');
});

test('remove all: several projects go at once and come back with one undo', () => {
  const s = emptyState();
  const ids = ['a', 'b', 'c'].map((code) => dispatch(s, { type: 'invoice/create', code }));
  dispatch(s, { type: 'invoice/row-add', id: ids[0], row: { device: 'Solar Kabin', start: '2026-09-15' } });
  const snapshot = structuredClone(s.invoices);
  dispatch(s, { type: 'invoice/delete', ids });
  assert.equal(s.invoices.length, 0);
  dispatch(s, { type: 'invoice/restore', invoices: snapshot });
  assert.deepEqual(s.invoices, snapshot);
  assert.throws(() => dispatch(s, { type: 'invoice/delete', ids: [ids[0], 'yok'] }), /bulunamadı/);
  assert.equal(s.invoices.length, 3, 'nothing is removed when one id is unknown');
});

test('sorting cards by No or by the last test date', () => {
  const inv = (no, ...rows) => ({ no, rows: rows.map(([start, end = null]) => ({ start, end })) });
  const list = [inv(1, ['2026-03-01', '2026-03-20']), inv(2, ['2026-05-01']), inv(3), inv(4, ['2026-01-10'], ['2026-02-01']), inv(5, ['2026-05-01'])];
  assert.equal(lastTestDate(list[0]), '2026-03-20');
  assert.equal(lastTestDate(list[2]), null);
  const nos = (order) => sortInvoices(list, order).map((i) => i.no);
  assert.deepEqual(nos('no'), [5, 4, 3, 2, 1]);
  assert.deepEqual(nos('no-asc'), [1, 2, 3, 4, 5]);
  assert.deepEqual(nos('date'), [5, 2, 1, 4, 3]);
  assert.deepEqual(nos('date-asc'), [4, 1, 2, 5, 3]);
});

test('device merge: rows renamed, list cleaned, later imports follow', async () => {
  const { deviceNames, similarDevices } = await import('../src/shared/devices.js');
  const s = emptyState();
  const id = dispatch(s, { type: 'invoice/create', code: 'A' });
  for (const device of ['70kN Elektrodinamik Sarsıcı', '70kN Sarsıcı', 'Servo Hidrolik Sarsıcı', 'Servohidrolik', 'ASLI TH-1728'])
    dispatch(s, { type: 'invoice/row-add', id, row: { device, start: '2026-10-01' } });
  dispatch(s, { type: 'device/add', name: '70kN Elektrodinamik Sarsıcı', test: 'Titreşim Testi' });
  assert.deepEqual(
    similarDevices(s).map((p) => [p.from, p.to]),
    [
      ['70kN Elektrodinamik Sarsıcı', 'Sarsıcı'],
      ['70kN Sarsıcı', 'Sarsıcı'],
      ['Servohidrolik', 'Servo Hidrolik Sarsıcı'],
    ],
  );
  assert.deepEqual(dispatch(s, { type: 'device/merge', from: '70kN Elektrodinamik Sarsıcı', to: '70kN Sarsıcı' }), { rows: 1 });
  assert.equal(s.invoices[0].rows.filter((r) => r.device === '70kN Sarsıcı').length, 2);
  assert.ok(!s.devices.some((d) => d.name === '70kN Elektrodinamik Sarsıcı'));
  assert.equal(deviceNames(s).find((d) => d.name === '70kN Sarsıcı').rows, 2);
  dispatch(s, { type: 'invoice/import', projects: [{ no: 9, code: 'B', title: 'X', rows: [{ test: 'Titreşim Testi', device: '70KN elektrodinamik sarsıcı', start: '2026-10-02', end: null }] }] });
  assert.equal(s.invoices.find((i) => i.code === 'B').rows[0].device, '70kN Sarsıcı', 'the merged name is read as the kept one');
  // Merging into a third name re-points the old alias too.
  dispatch(s, { type: 'device/merge', from: '70kN Sarsıcı', to: '70 kN Sarsıcı' });
  assert.deepEqual(s.deviceAliases.map((a) => [a.from, a.to]), [['70kN Elektrodinamik Sarsıcı', '70 kN Sarsıcı'], ['70kN Sarsıcı', '70 kN Sarsıcı']]);
  dispatch(s, { type: 'device/unalias', from: '70kN Sarsıcı' });
  assert.equal(s.deviceAliases.length, 1);
  assert.throws(() => dispatch(s, { type: 'device/merge', from: 'a', to: 'A' }), /iki farklı/);
});

test('size variants are suggested, never merged on their own; model numbers are not sizes', async () => {
  const { similarDevices } = await import('../src/shared/devices.js');
  const s = emptyState();
  const id = dispatch(s, { type: 'invoice/create', code: 'A' });
  for (const device of ['70kN Sarsıcı', '49kN Sarsıcı', '10kN Elektrodinamik Sarsıcı', 'ASLI TH-1728', 'ASLI TH-1300-F', '1000L Kabin'])
    dispatch(s, { type: 'invoice/row-add', id, row: { device, start: '2026-10-01' } });
  assert.deepEqual(
    similarDevices(s).map((p) => [p.from, p.to]),
    [
      ['10kN Elektrodinamik Sarsıcı', 'Sarsıcı'],
      ['49kN Sarsıcı', 'Sarsıcı'],
      ['70kN Sarsıcı', 'Sarsıcı'],
    ],
  );
  assert.deepEqual(s.invoices[0].rows.map((r) => r.device).slice(0, 2), ['70kN Sarsıcı', '49kN Sarsıcı'], 'nothing changed');
});

test('the wrong 1.18.0 merges are undone from the backup, once; right ones stay', () => {
  const s = emptyState();
  const id = dispatch(s, { type: 'invoice/create', code: 'A' });
  const names = ['ASLI TH-1728', 'ASLI TH-1300-F', 'ASLI TH-1728', '70kN Sarsıcı'];
  const rowIds = names.map((device) => dispatch(s, { type: 'invoice/row-add', id, row: { device, start: '2026-10-01' } }));
  const before = { rows: Object.fromEntries(rowIds.map((r, i) => [`${id}:${r}`, names[i]])), devices: [{ name: 'ASLI TH-1728', test: 'İklimlendirme Testi' }] };
  // What 1.18.0 did:
  dispatch(s, { type: 'device/merge', from: 'ASLI TH-1728', to: 'ASLI' });
  dispatch(s, { type: 'device/merge', from: 'ASLI TH-1300-F', to: 'ASLI' });
  dispatch(s, { type: 'device/merge', from: '70kN Sarsıcı', to: 'Sarsıcı' });
  const extra = dispatch(s, { type: 'invoice/row-add', id, row: { device: 'ASLI', start: '2026-10-02' } });
  s.settings.devicesMergedByName = true;

  const r = dispatch(s, { type: 'device/unmerge-wrong', before });
  assert.deepEqual(r.restored, [
    { from: 'ASLI TH-1728', to: 'ASLI', rows: 2 },
    { from: 'ASLI TH-1300-F', to: 'ASLI', rows: 1 },
  ]);
  assert.equal(r.unknown, 1, 'the row added after the merge has no earlier name');
  const inv = s.invoices[0];
  assert.deepEqual(inv.rows.filter((x) => x.id !== extra).map((x) => x.device), ['ASLI TH-1728', 'ASLI TH-1300-F', 'ASLI TH-1728', 'Sarsıcı']);
  assert.deepEqual(s.deviceAliases.map((a) => [a.from, a.to]), [['70kN Sarsıcı', 'Sarsıcı']]);
  assert.ok(s.devices.some((d) => d.name === 'ASLI TH-1728' && d.test === 'İklimlendirme Testi'));
  assert.equal(s.settings.devicesRepaired, true);
  assert.deepEqual(dispatch(s, { type: 'device/unmerge-wrong', before }).restored, []);
});

test('one heading field: code, title and report no in and out', () => {
  const full = { code: 'P26-0001-0001', title: 'ÖRNEK Titreşim (Kombine) Testi', ref: 'TEST260101ORN01REV0' };
  assert.deepEqual(parseInvoiceTitle(invoiceTitle(full)), full);
  assert.deepEqual(parseInvoiceTitle(' p26-12-345 –  ÖRNEK  Test '), { code: 'P26-12-345', title: 'ÖRNEK Test', ref: '' });
  assert.deepEqual(parseInvoiceTitle('ÖRNEK Yağmur Testi (IPX2)'), { code: '', title: 'ÖRNEK Yağmur Testi (IPX2)', ref: '' }, 'only TEST… in brackets is a report no');
  assert.deepEqual(parseInvoiceTitle('P26-0001-0001 (TEST1)'), { code: 'P26-0001-0001', title: '', ref: 'TEST1' });
  assert.deepEqual(parseInvoiceTitle(''), { code: '', title: '', ref: '' });
});
