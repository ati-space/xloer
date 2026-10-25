import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deviceUsage, invoiceYears, monthlySummary, periodRange, periodSummary, undatedInvoices, yearlySummary } from '../src/shared/invoice-stats.js';

const inv = (id, amount, rows) => ({ id, no: 1, amount, rows: rows.map(([device, start, end = null]) => ({ device, test: '', start, end })) });

// A: tests across a month end, billed in October. B: two tests on one device
// the same day, billed in September. C: last year. D: no dates.
const invoices = [
  inv('a', 1000, [['Solar Kabin', '2026-09-28', '2026-10-03']]),
  inv('b', 500.5, [['70kN Sarsıcı', '2026-09-10'], ['70kN Sarsıcı', '2026-09-10'], ['solar kabin', '2026-09-01', '2026-09-02']]),
  inv('c', 200, [['70kN Sarsıcı', '2025-12-30', '2026-01-02']]),
  inv('d', 12900, [['GTL', null]]),
];

test('periods: months and years with both ends included', () => {
  assert.deepEqual(periodRange(2026, 2), { from: '2026-02-01', to: '2026-02-28' });
  assert.deepEqual(periodRange(2028, 2), { from: '2028-02-01', to: '2028-02-29' });
  assert.deepEqual(periodRange(2026), { from: '2026-01-01', to: '2026-12-31' });
  assert.deepEqual(invoiceYears(invoices), [2026, 2025]);
});

test('monthly: amount in the month of the last test, test days where they ran', () => {
  const months = monthlySummary(invoices, 2026);
  assert.equal(months.length, 12);
  const pick = (m) => ({ amount: months[m - 1].amount, projects: months[m - 1].projects, days: months[m - 1].days, shifts: months[m - 1].shifts });
  assert.deepEqual(pick(9), { amount: 500.5, projects: 1, days: 3 + 2 + 2, shifts: 21 });
  assert.deepEqual(pick(10), { amount: 1000, projects: 1, days: 3, shifts: 9 });
  assert.deepEqual(pick(1), { amount: 200, projects: 1, days: 2, shifts: 6 }, 'C began in December but its last test, and so its bill, is in January');
  assert.equal(months[0].label, 'Ocak');
});

test('yearly: oldest first; the amount follows the last test date', () => {
  const years = yearlySummary(invoices);
  assert.deepEqual(
    years.map((y) => [y.year, y.amount, y.projects, y.days]),
    [
      [2025, 0, 0, 2],
      [2026, 1700.5, 3, 12],
    ],
  );
  assert.deepEqual(undatedInvoices(invoices), { projects: 1, amount: 12900 });
  assert.equal(periodSummary(invoices, '2026-09-01', '2026-09-30').amount, 500.5);
});

test('device time: calendar days in use, cut to the period, names folded', () => {
  const sept = deviceUsage(invoices, '2026-09-01', '2026-09-30');
  assert.deepEqual(
    sept.map((d) => [d.device, d.days, d.shifts, d.tests, d.projects]),
    [
      ['Solar Kabin', 5, 15, 2, 2],
      ['70kN Sarsıcı', 1, 3, 2, 1],
    ],
    'Solar Kabin: 1–2 and 28–30 September; two tests on one day are one day of use',
  );
  assert.equal(sept[0].share, 5 / 30);
  const open = deviceUsage(invoices, '2026-09-01', '2026-09-30', '2026-09-10');
  assert.equal(open[0].share, 5 / 10, 'an open period counts its days so far');
  assert.equal(deviceUsage(invoices, '2027-01-01', '2027-12-31').length, 0);
});

test('customers: read from titles, shared names grouped, typed ones kept', async () => {
  const { assignCustomers, guessCustomer } = await import('../src/shared/customers.js');
  assert.equal(guessCustomer('ÖRNEK ELEKTRONİK EN 61373 Titreşim Testi'), 'ÖRNEK ELEKTRONİK');
  assert.equal(guessCustomer('DEMO UN38.3 Testleri'), 'DEMO');
  assert.equal(guessCustomer('Deneme IPX1,X2,X3 Testleri'), 'Deneme');
  assert.equal(guessCustomer('ABC Pompa Titreşim Testi'), 'ABC');
  const list = ['ACME INVERTER Titreşim Testleri', 'ACME Titreşim ve Şok Testleri', 'Acme CONVERTER Testleri', 'BETA SAVUNMA Titreşim Testi'].map((title) => ({ title, customer: '' }));
  assignCustomers(list, list);
  assert.deepEqual(list.map((i) => i.customer), ['ACME', 'ACME', 'ACME', 'BETA SAVUNMA']);
});

test('customer summary: amount, projects and days per customer in the period', async () => {
  const { customerSummary } = await import('../src/shared/invoice-stats.js');
  const list = [
    { ...inv('a', 1000, [['Solar Kabin', '2026-09-28', '2026-10-03']]), customer: 'ACME' },
    { ...inv('b', 500, [['70kN Sarsıcı', '2026-10-10']]), customer: 'acme' },
    { ...inv('c', 300, [['70kN Sarsıcı', '2026-10-12']]), customer: 'BETA' },
    { ...inv('d', 900, [['70kN Sarsıcı', '2026-11-12']]), customer: 'BETA' },
  ];
  assert.deepEqual(
    customerSummary(list, '2026-10-01', '2026-10-31').map((c) => [c.name, c.amount, c.projects, c.days]),
    [
      ['ACME', 1500, 2, 4],
      ['BETA', 300, 1, 1],
    ],
  );
});
