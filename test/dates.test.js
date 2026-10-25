import { test } from 'node:test';
import assert from 'node:assert/strict';
import { calendarWeeks, formatTrDate, parseTrDate } from '../src/shared/dates.js';

test('typed Turkish dates', () => {
  assert.equal(parseTrDate('15.09.2026'), '2026-09-15');
  assert.equal(parseTrDate('15/9/26'), '2026-09-15');
  assert.equal(parseTrDate('5-1-2026'), '2026-01-05');
  assert.equal(parseTrDate('15092026'), '2026-09-15');
  assert.equal(parseTrDate('150926'), '2026-09-15');
  assert.equal(parseTrDate('15.09', 2027), '2027-09-15');
  assert.equal(parseTrDate(' '), null);
  assert.equal(parseTrDate('31.02.2026'), undefined);
  assert.equal(parseTrDate('29.02.2028'), '2028-02-29');
  assert.equal(parseTrDate('yarın'), undefined);
  assert.equal(parseTrDate('15.09.206'), undefined);
  assert.equal(formatTrDate('2026-09-15'), '15.09.2026');
  assert.equal(formatTrDate(null), '');
});

test('calendar weeks start on Monday', () => {
  const oct = calendarWeeks(2026, 10); // 1 October 2026 is a Thursday
  assert.equal(oct[0][0].iso, '2026-09-28');
  assert.equal(oct[0][3].iso, '2026-10-01');
  assert.equal(oct[0][0].inMonth, false);
  assert.ok(oct.every((w) => w.length === 7));
  assert.equal(oct.at(-1).at(-1).iso, '2026-11-01');
  const feb = calendarWeeks(2027, 2); // 1 February 2027 is a Monday, 28 days
  assert.equal(feb.length, 4);
  assert.equal(feb[0][0].iso, '2027-02-01');
});
