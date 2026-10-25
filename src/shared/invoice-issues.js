// Checks shown on a project card, mostly for what came from Excel: a date
// the sheet's Gün / Vardiya disagrees with, a mistyped year that was read
// anyway, an end worked out from Gün / Vardiya, a "Proje Kodu" cell that
// disagrees with the title, a date far ahead, a row or project missing its
// date or amount. They are worked out from the card as it is now, so fixing
// the card clears them; "Yoksay" hides one for good.
//
// A fix is store actions to run in order, or a field to put the cursor in.

import { fold } from './search.js';
import { SHIFTS_PER_DAY, dateRangeLabel, rowDays } from './invoices.js';
import { addDaysIso, formatTrDate } from './dates.js';

// Further ahead than this is most likely a typo (29.09.2029 for 2026).
const AHEAD_DAYS = 120;

const shifts = (days) => `${days}/${days * SHIFTS_PER_DAY}`;

export function invoiceIssues(inv, today) {
  const out = [];
  const add = (key, issue) => {
    if (!inv.dismissed?.includes(key)) out.push({ key, ...issue });
  };
  if (inv.sheetCode && fold(inv.sheetCode) !== fold(inv.code)) {
    add('code', {
      text: `Excel’deki “Proje Kodu” hücresi ${inv.sheetCode}, başlıkta ${inv.code || 'kod yok'} yazıyor; başlıktaki kullanıldı.`,
      fixes: [{ label: `${inv.sheetCode} kullan`, actions: [{ type: 'invoice/update', id: inv.id, patch: { code: inv.sheetCode } }] }],
    });
  }
  if (inv.fromSheet && inv.amount == null) {
    add('amount', { text: 'Excel’de bu projenin tutarı bulunamadı.', fixes: [{ label: 'Tutar gir', focus: { f: 'amount' } }] });
  }
  for (const r of inv.rows) {
    const what = r.test || r.device || 'Satır';
    const rowAction = (patch) => ({ type: 'invoice/row-update', id: inv.id, rowId: r.id, patch });
    if (!r.start) {
      add(`undated:${r.id}`, {
        rowId: r.id,
        text: r.sheetDate ? `${what}: Excel’deki tarih okunamadı (“${r.sheetDate}”).` : `${what}: test tarihi yok.`,
        fixes: [{ label: 'Tarih gir', focus: { row: r.id, f: 'start' } }],
      });
      continue;
    }
    if (r.yearFixed) {
      add(`year:${r.id}`, { rowId: r.id, text: `${what}: Excel’de “${r.sheetDate}” yazıyordu; ${dateRangeLabel(r)} olarak okundu.`, fixes: [] });
    }
    if (r.endFromDays) {
      add(`end:${r.id}`, {
        rowId: r.id,
        text: `${what}: Excel’de yalnız ${formatTrDate(r.start)} var, Gün / Vardiya ${shifts(r.sheetDays)}; bitiş ${formatTrDate(r.end)} olarak hesaplandı.`,
        // Chosen against the sheet on purpose, so its day count is not raised again.
        fixes: [{ label: 'Tek gün yap', actions: [rowAction({ end: null }), { type: 'invoice/dismiss-issue', id: inv.id, key: `days:${r.id}` }] }],
      });
    } else if (r.sheetDays > 0 && rowDays(r) !== r.sheetDays) {
      const end = r.sheetDays > 1 ? addDaysIso(r.start, r.sheetDays - 1) : null;
      add(`days:${r.id}`, {
        rowId: r.id,
        text: `${what} (${dateRangeLabel(r)}): tarihler ${rowDays(r)} gün, Excel’de Gün / Vardiya ${shifts(r.sheetDays)}.`,
        fixes: [{ label: end ? `Bitişi ${formatTrDate(end)} yap` : 'Tek gün yap', actions: [rowAction({ end })] }],
      });
    }
    const last = r.end ?? r.start;
    if (last > addDaysIso(today, AHEAD_DAYS)) {
      add(`ahead:${r.id}`, { rowId: r.id, text: `${what}: test tarihi çok ileride (${formatTrDate(last)}).`, fixes: [{ label: 'Tarihi düzelt', focus: { row: r.id, f: r.end ? 'end' : 'start' } }] });
    }
  }
  return out;
}
