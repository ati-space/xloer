// "Faturalar": one card per project, like the invoice sheet. A row only needs
// a device and dates; the test comes from the device and days/shifts are
// computed. The copy button puts the e-mail table on the clipboard. An Excel
// invoice sheet (picked here or dropped on the window) adds its projects.

import { icon } from './icons.js';
import * as A from './actions.js';
import { confirmModal, esc, menuAt, modal, toast } from './ui.js';
import { todayStr } from '../../shared/store.js';
import { fold } from '../../shared/search.js';
import { INVOICE_ORDERS, formatAmount, formatEuro, invoiceTitle, parseAmount, shiftLabel, sortInvoices, sortedRows, totalAmount } from '../../shared/invoices.js';
import { readWorkbook } from '../../shared/xlsx.js';
import { createInvoiceDash } from './invoice-dash.js';
import { calendarAnchor, closeCalendar, openCalendar, updateCalendar } from './calendar.js';
import { formatTrDate, parseTrDate } from '../../shared/dates.js';
import { invoiceIssues } from '../../shared/invoice-issues.js';
import { customerOf } from '../../shared/customers.js';
import { deviceNames, similarDevices } from '../../shared/devices.js';
import { parseInvoiceWorkbook } from '../../shared/invoice-import.js';

const NEW = '__new';
const ORDER_KEY = 'xloer-invoice-order';
const TAB_KEY = 'xloer-invoice-tab';

function loadOrder() {
  try {
    const v = localStorage.getItem(ORDER_KEY);
    if (v in INVOICE_ORDERS) return v;
  } catch {}
  return 'no';
}

export function createInvoicesView(el, { onToggleSidebar }) {
  let state = null;
  let filter = '';
  let deferred = false;
  let focusAfter = null;
  let manageBox = null;
  let order = loadOrder();
  let tab = 'cards';
  try {
    if (localStorage.getItem(TAB_KEY) === 'dash') tab = 'dash';
  } catch {}
  let shownIds = [];
  // Ticked cards ("Seçilenleri kaldır"); kept while the search changes, so
  // several searches can build up one selection.
  const selected = new Set();
  let lastPicked = null;
  // Cards with their checks list open, and the "Uyarılı" filter.
  const openIssues = new Set();
  let onlyIssues = false;
  const issuesOf = (inv) => invoiceIssues(inv, todayStr());

  el.innerHTML = `
    <header class="inv-head drag">
      <button type="button" class="icon-btn" data-i="sidebar" title="Kenar çubuğunu aç/kapat">${icon('menu')}</button>
      <div class="hd-title"><div class="hd-date">Test faturaları</div><h1 class="list-title"><span class="name">Faturalar</span></h1></div>
      <div class="seg inv-tabs" role="radiogroup" aria-label="Görünüm">
        <button type="button" role="radio" data-tab="cards">Projeler</button>
        <button type="button" role="radio" data-tab="dash">Pano</button>
      </div>
      <div class="inv-total" title="Tüm projelerden kesilen toplam tutar"><span>Kesilen toplam</span><b id="inv-total"></b></div>
      <button type="button" class="btn primary hd-new" data-i="new">+ Yeni proje</button>
      <button type="button" class="icon-btn" data-i="menu" title="Diğer">${icon('more')}</button>
    </header>
    <div class="inv-tools">
      <label class="inv-pick inv-pick-all" title="Görünenlerin hepsini seç"><input type="checkbox" id="inv-pick-all" aria-label="Görünenlerin hepsini seç"></label>
      <label class="search-box">${icon('search')}<input id="inv-filter" type="search" placeholder="Proje kodu, müşteri, rapor no veya cihaz ara" spellcheck="false" autocomplete="off"></label>
      <span class="muted small" id="inv-count"></span>
      <button type="button" class="inv-chip" data-i="only-issues" aria-pressed="false" hidden></button>
      <button type="button" class="icon-btn small" data-i="pick-none" title="Seçimi temizle (Esc)" aria-label="Seçimi temizle" hidden>${icon('x', 13)}</button>
      <span class="flex"></span>
      <label class="inv-order" title="Kartların sırası"><span>Sırala</span><select id="inv-order">${Object.entries(INVOICE_ORDERS)
        .map(([k, v]) => `<option value="${k}"${k === order ? ' selected' : ''}>${esc(v)}</option>`)
        .join('')}</select></label>
      <button type="button" class="link-btn inv-xlsx" data-i="xlsx" title="Excel fatura dosyasını seç ya da pencereye sürükle">${icon('download', 14)}Excel’den al</button>
      <button type="button" class="link-btn danger inv-clear" data-i="clear">${icon('trash', 14)}<span></span></button>
      <input type="file" id="inv-file" accept=".xlsx,.xlsm" hidden>
    </div>
    <div class="inv-body scroll" id="inv-body"></div>
    <div class="dash" id="inv-dash"></div>`;
  const body = el.querySelector('#inv-body');
  const filterInput = el.querySelector('#inv-filter');
  const fileInput = el.querySelector('#inv-file');
  const dash = createInvoiceDash(el.querySelector('#inv-dash'));

  function setTab(next) {
    tab = next;
    try {
      localStorage.setItem(TAB_KEY, tab);
    } catch {}
    render(null, { force: true });
  }

  // Typing in a field must not be interrupted by a re-render; it catches up
  // when the field loses focus.
  const typing = (a) => el.contains(a) && a !== filterInput && a.matches('input:not([type="checkbox"])');

  function invoice(id) {
    return state.invoices.find((i) => i.id === id);
  }

  function matches(inv) {
    if (onlyIssues && !issuesOf(inv).length) return false;
    if (!filter.trim()) return true;
    const hay = fold([inv.no, inv.code, inv.title, inv.ref, customerOf(inv), ...inv.rows.flatMap((r) => [r.device, r.test])].join(' '));
    return fold(filter)
      .split(/\s+/)
      .filter(Boolean)
      .every((t) => hay.includes(t));
  }

  function option(value, current, label = value) {
    return `<option value="${esc(value)}"${value === current ? ' selected' : ''}>${esc(label)}</option>`;
  }

  // A one-day test shows its day in both date fields, so no field is left
  // empty; the sheet's own Gün / Vardiya is shown under ours when they differ.
  function rowHtml(r, issues) {
    const tests = r.test && !state.testTypes.includes(r.test) ? [r.test, ...state.testTypes] : state.testTypes;
    const devices = state.devices.map((d) => d.name);
    if (r.device && !devices.includes(r.device)) devices.unshift(r.device);
    const mine = issues.filter((i) => i.rowId === r.id);
    const sheetDays = mine.some((i) => i.key.startsWith('days:')) ? `<small class="inv-sheet" title="Excel’deki Gün / Vardiya">Excel ${r.sheetDays}/${r.sheetDays * 3}</small>` : '';
    return `<tr data-row="${esc(r.id)}"${mine.length ? ` class="warn" title="${esc(mine.map((i) => i.text).join('\n'))}"` : ''}>
      <td><select data-f="test" aria-label="Test" title="${esc(r.test)}">${r.test ? '' : '<option value="" selected>Test seç</option>'}${tests.map((t) => option(t, r.test)).join('')}<option value="${NEW}">＋ Yeni test türü…</option></select></td>
      <td><select data-f="device" aria-label="Test sistemi" title="${esc(r.device)}">${r.device ? '' : '<option value="" selected>Cihaz seç</option>'}${devices.map((d) => option(d, r.device)).join('')}<option value="${NEW}">＋ Yeni cihaz…</option></select></td>
      <td class="inv-dates"><input class="inv-date" data-f="start" data-date value="${formatTrDate(r.start)}" placeholder="gg.aa.yyyy" inputmode="numeric" maxlength="10" autocomplete="off" spellcheck="false" aria-label="Başlangıç tarihi"><span>–</span><input class="inv-date" data-f="end" data-date value="${formatTrDate(r.end ?? r.start)}" placeholder="gg.aa.yyyy" inputmode="numeric" maxlength="10" autocomplete="off" spellcheck="false" aria-label="Bitiş tarihi" title="Bitiş tarihi (tek günlük testte başlangıçla aynı)"></td>
      <td class="num inv-shift"><span>${esc(shiftLabel(r)) || '–'}</span>${sheetDays}</td>
      <td class="inv-row-tools"><button type="button" class="icon-btn small" data-i="row-del" title="Satırı sil">${icon('x', 13)}</button></td>
    </tr>`;
  }

  function issuesHtml(inv, issues) {
    return `<div class="inv-issues">${issues
      .map(
        (i) => `<div class="inv-issue" data-key="${esc(i.key)}">${icon('alert', 14)}<span>${esc(i.text)}</span>
          ${i.fixes.map((f, n) => `<button type="button" class="btn small" data-fix="${n}">${esc(f.label)}</button>`).join('')}
          <button type="button" class="link-btn quiet" data-dismiss>${i.fixes.length ? 'Yoksay' : 'Tamam'}</button>
        </div>`,
      )
      .join('')}</div>`;
  }

  function cardHtml(inv) {
    const rows = sortedRows(inv);
    const on = selected.has(inv.id);
    const issues = issuesOf(inv);
    const open = issues.length > 0 && openIssues.has(inv.id);
    return `<article class="inv-card${on ? ' picked' : ''}" data-id="${esc(inv.id)}">
      <header class="inv-card-head">
        <label class="inv-pick" title="Seç (Shift ile aralık)"><input type="checkbox" data-pick${on ? ' checked' : ''} aria-label="${esc(`No ${inv.no} seç`)}"></label>
        <label class="inv-no" title="Sıra numarası"><span>No</span><input type="number" min="1" data-f="no" value="${inv.no || ''}"></label>
        <input class="inv-code" data-f="code" value="${esc(inv.code)}" placeholder="Proje kodu (P26-…)" spellcheck="false" aria-label="Proje kodu">
        <input class="inv-name" data-f="title" value="${esc(inv.title)}" placeholder="Müşteri ve test adı" spellcheck="false" aria-label="Müşteri ve test adı">
        <input class="inv-ref" data-f="ref" value="${esc(inv.ref)}" placeholder="Rapor no (TEST…)" spellcheck="false" aria-label="Rapor numarası">
        ${issues.length ? `<button type="button" class="inv-warn" data-i="issues" aria-expanded="${open}" title="Kontrol edilecek ${issues.length} nokta">${icon('alert', 14)}${issues.length}</button>` : ''}
        <button type="button" class="icon-btn" data-i="copy" title="Tabloyu kopyala (e-posta / Word)">${icon('copy')}</button>
        <button type="button" class="icon-btn" data-i="card-menu" title="Diğer">${icon('more')}</button>
      </header>
      ${open ? issuesHtml(inv, issues) : ''}
      <div class="inv-table-wrap">
        <table class="inv-table">
          <thead><tr><th>Test</th><th>Test Sistemi</th><th>Test Tarihi</th><th class="num">Gün / Vardiya</th><th></th></tr></thead>
          <tbody>${rows.map((r) => rowHtml(r, issues)).join('') || '<tr class="inv-empty"><td colspan="5">Henüz test yok. “Test ekle” ile cihaz ve tarih seç.</td></tr>'}</tbody>
        </table>
      </div>
      <footer class="inv-card-foot">
        <button type="button" class="link-btn" data-i="row-add">${icon('plus', 14)}Test ekle</button>
        <label class="inv-customer" title="Müşteri (boşsa başlıktan okunur)"><span>Müşteri</span><input data-f="customer" value="${esc(inv.customer)}" placeholder="${esc(customerOf(inv) || 'Müşteri')}" spellcheck="false" aria-label="Müşteri"></label>
        <span class="flex"></span>
        <label class="inv-amount"><span>Tutar</span><span class="cur">€</span><input data-f="amount" inputmode="decimal" value="${formatAmount(inv.amount)}" placeholder="0,00" aria-label="Tutar (€)"></label>
      </footer>
    </article>`;
  }

  const pickedInvoices = () => state.invoices.filter((i) => selected.has(i.id));

  // Count, remove button and the "all" box follow the selection: with ticks
  // the button removes the ticked cards, else what the search shows, else all.
  function renderTotals(shown) {
    el.querySelector('#inv-total').textContent = formatEuro(totalAmount(state.invoices));
    const picked = pickedInvoices();
    const clear = el.querySelector('.inv-clear');
    const filtered = shown.length < state.invoices.length;
    clear.hidden = !shown.length && !picked.length;
    clear.querySelector('span').textContent = picked.length ? `Seçilenleri kaldır (${picked.length})` : filtered ? `Görünenleri kaldır (${shown.length})` : 'Hepsini kaldır';
    clear.title = picked.length ? 'Seçili projeleri sil' : filtered ? 'Aramaya uyan projeleri sil' : 'Tüm projeleri sil';
    const count = el.querySelector('#inv-count');
    count.textContent = picked.length
      ? `${picked.length} seçili · ${formatEuro(totalAmount(picked))}`
      : filter.trim()
        ? `${shown.length} / ${state.invoices.length} proje · ${formatEuro(totalAmount(shown))}`
        : `${state.invoices.length} proje`;
    count.classList.toggle('picking', picked.length > 0);
    const flagged = state.invoices.filter((i) => issuesOf(i).length).length;
    const chip = el.querySelector('[data-i="only-issues"]');
    chip.hidden = !flagged && !onlyIssues;
    chip.setAttribute('aria-pressed', String(onlyIssues));
    chip.innerHTML = `${icon('alert', 13)}Uyarılı (${flagged})`;
    chip.title = onlyIssues ? 'Tüm projeleri göster' : 'Yalnız kontrol edilecek projeleri göster';
    el.querySelector('[data-i="pick-none"]').hidden = !picked.length;
    const all = el.querySelector('#inv-pick-all');
    const onScreen = shown.filter((i) => selected.has(i.id)).length;
    all.checked = shown.length > 0 && onScreen === shown.length;
    all.indeterminate = onScreen > 0 && onScreen < shown.length;
    all.disabled = !shown.length;
  }

  // Ticks change without a re-render, so typing elsewhere is never disturbed.
  function syncPicks() {
    for (const card of body.querySelectorAll('.inv-card')) {
      const on = selected.has(card.dataset.id);
      card.classList.toggle('picked', on);
      card.querySelector('[data-pick]').checked = on;
    }
    renderTotals(state.invoices.filter((i) => shownIds.includes(i.id)));
  }

  function clearPicks() {
    selected.clear();
    lastPicked = null;
    syncPicks();
  }

  function render(next, { force = false } = {}) {
    if (next) state = next;
    if (!state) return;
    for (const id of selected) if (!state.invoices.some((i) => i.id === id)) selected.delete(id);
    const shown = sortInvoices(state.invoices.filter(matches), order);
    shownIds = shown.map((i) => i.id);
    renderTotals(shown);
    renderManage();
    el.classList.toggle('tab-dash', tab === 'dash');
    el.querySelectorAll('[data-tab]').forEach((b) => b.setAttribute('aria-checked', String(b.dataset.tab === tab)));
    if (tab === 'dash') return dash.render(state);
    if (!force && typing(document.activeElement)) {
      deferred = true;
      return;
    }
    deferred = false;
    const a = document.activeElement;
    const keep = el.contains(a) && a.dataset.f ? { id: a.closest('[data-id]')?.dataset.id, row: a.closest('[data-row]')?.dataset.row, f: a.dataset.f } : null;
    const scroll = body.scrollTop;
    if (el.contains(calendarAnchor())) closeCalendar();
    body.innerHTML = shown.length
      ? shown.map(cardHtml).join('')
      : `<p class="empty">${state.invoices.length ? 'Aramaya uyan proje yok.' : 'Henüz proje yok. “Yeni proje” ile başla: proje kodunu yaz, testlerin cihazını ve tarihini seç.<br>Fatura Excel’in varsa dosyayı bu pencereye sürükle; projeler kendiliğinden gelir.'}</p>`;
    body.scrollTop = scroll;
    const target = focusAfter ?? keep;
    if (target) {
      const card = body.querySelector(`[data-id="${CSS.escape(target.id ?? '')}"]`);
      const scope = target.row ? card?.querySelector(`[data-row="${CSS.escape(target.row)}"]`) : card;
      const field = scope?.querySelector(`[data-f="${target.f}"]`);
      if (field) {
        field.focus();
        if (focusAfter) card.scrollIntoView({ block: 'nearest' });
        focusAfter = null;
      }
    }
  }

  el.addEventListener('focusout', () =>
    setTimeout(() => {
      if (deferred && !typing(document.activeElement)) render();
    }),
  );

  // ---------------------------------------------------------------- editing

  async function newProject() {
    if (tab !== 'cards') setTab('cards');
    const id = await A.run({ type: 'invoice/create' });
    if (!id) return;
    await A.run({ type: 'invoice/row-add', id, row: { start: todayStr() } });
    focusAfter = { id, f: 'code' };
    if (state.invoices.some((i) => i.id === id)) render(null, { force: true });
  }

  async function addDevice() {
    const box = document.createElement('div');
    box.innerHTML = `<label class="field-label">Cihaz adı</label>
      <input class="input" data-k="name" placeholder="ör. 35kN Sarsıcı" autofocus spellcheck="false">
      <label class="field-label spaced">Genelde yapılan test</label>
      <select class="input" data-k="test">${state.testTypes.map((t) => option(t, '')).join('')}</select>`;
    const r = await modal({
      title: 'Yeni cihaz',
      body: box,
      buttons: [
        { label: 'Vazgeç', value: null },
        { label: 'Ekle', primary: true, value: (b) => ({ name: b.querySelector('[data-k="name"]').value.trim(), test: b.querySelector('[data-k="test"]').value }) },
      ],
    });
    if (!r?.name) return null;
    return (await A.run({ type: 'device/add', name: r.name, test: r.test })) ? r.name : null;
  }

  async function addTestType() {
    const box = document.createElement('div');
    box.innerHTML = `<label class="field-label">Test adı</label><input class="input" data-k="name" placeholder="ör. Tuz Sisi Testi" autofocus spellcheck="false">`;
    const name = await modal({
      title: 'Yeni test türü',
      body: box,
      buttons: [
        { label: 'Vazgeç', value: null },
        { label: 'Ekle', primary: true, value: (b) => b.querySelector('[data-k="name"]').value.trim() },
      ],
    });
    if (!name) return null;
    return (await A.run({ type: 'testtype/add', name })) ? name : null;
  }

  async function onFieldChange(field) {
    const id = field.closest('[data-id]')?.dataset.id;
    const rowId = field.closest('[data-row]')?.dataset.row;
    const inv = id && invoice(id);
    if (!inv) return;
    const f = field.dataset.f;
    if (!rowId) {
      if (f === 'no') {
        const no = Number.parseInt(field.value, 10);
        if (no > 0) return A.run({ type: 'invoice/update', id, patch: { no } });
        field.value = inv.no || '';
        return;
      }
      if (f === 'amount') {
        const amount = parseAmount(field.value);
        if (field.value.trim() && amount == null) {
          toast('Tutar anlaşılamadı. Örnek: 16.600,00', { error: true });
          field.value = formatAmount(inv.amount);
          return;
        }
        field.value = formatAmount(amount);
        return A.run({ type: 'invoice/update', id, patch: { amount } });
      }
      return A.run({ type: 'invoice/update', id, patch: { [f]: field.value } });
    }
    const row = inv.rows.find((r) => r.id === rowId);
    if (f === 'device' || f === 'test') {
      let value = field.value;
      if (value === NEW) {
        value = f === 'device' ? await addDevice() : await addTestType();
        if (!value) {
          field.value = row[f];
          return;
        }
      }
      return A.run({ type: 'invoice/row-update', id, rowId, patch: { [f]: value } });
    }
    // Date fields take Turkish dates (15.09.2026); one that is not a day is
    // put back as it was.
    // The end of a one-day test is its start, shown but not stored.
    const shown = (value) => formatTrDate(f === 'end' ? (value ?? row.start) : value);
    const iso = parseTrDate(field.value);
    if (iso === undefined) {
      toast('Tarih anlaşılamadı. Örnek: 15.09.2026', { error: true });
      field.value = shown(row[f]);
      return liveShift(field.closest('tr'));
    }
    const value = f === 'end' && iso === row.start ? null : iso;
    field.value = shown(value);
    if (value === (row[f] ?? null)) return;
    return A.run({ type: 'invoice/row-update', id, rowId, patch: { [f]: value } });
  }

  body.addEventListener('change', (e) => {
    if (e.target.matches('[data-f]')) onFieldChange(e.target);
  });

  // A tick; with Shift, every card between it and the last tick follows it.
  body.addEventListener('click', (e) => {
    const box = e.target.closest('input[data-pick]');
    if (!box) return;
    const id = box.closest('[data-id]').dataset.id;
    const ids = e.shiftKey && lastPicked && shownIds.includes(lastPicked) ? shownIds.slice(Math.min(shownIds.indexOf(lastPicked), shownIds.indexOf(id)), Math.max(shownIds.indexOf(lastPicked), shownIds.indexOf(id)) + 1) : [id];
    for (const x of ids) box.checked ? selected.add(x) : selected.delete(x);
    lastPicked = id;
    syncPicks();
  });

  // Live days/shifts while a date is being typed.
  function liveShift(tr) {
    const start = parseTrDate(tr.querySelector('[data-f="start"]').value) || null;
    const end = parseTrDate(tr.querySelector('[data-f="end"]').value) || null;
    tr.querySelector('.inv-shift').textContent = shiftLabel({ start, end }) || '–';
  }

  // The Turkish calendar under a date field; a picked day is saved at once.
  function openDate(field) {
    if (calendarAnchor() === field) return;
    const tr = field.closest('tr');
    const start = parseTrDate(tr.querySelector('[data-f="start"]').value) || null;
    const isEnd = field.dataset.f === 'end';
    openCalendar(field, {
      value: parseTrDate(field.value) || null,
      min: isEnd ? start : null,
      rangeStart: isEnd ? start : null,
      onPick: (iso) => {
        field.value = formatTrDate(iso);
        liveShift(tr);
        onFieldChange(field);
        field.blur();
      },
    });
  }

  body.addEventListener('input', (e) => {
    const tr = e.target.closest('tr[data-row]');
    if (!tr || !e.target.matches('[data-date]')) return;
    liveShift(tr);
    const iso = parseTrDate(e.target.value);
    if (iso && calendarAnchor() === e.target) updateCalendar(iso);
  });

  body.addEventListener('focusin', (e) => {
    if (e.target.matches('[data-date]')) openDate(e.target);
  });
  body.addEventListener('click', (e) => {
    if (e.target.matches('[data-date]')) openDate(e.target);
  });
  body.addEventListener('focusout', (e) => {
    if (e.target.matches('[data-date]') && calendarAnchor() === e.target && !e.relatedTarget?.closest('.cal')) closeCalendar();
  });

  body.addEventListener('keydown', (e) => {
    if (e.target.matches('[data-date]') && e.key === 'ArrowDown' && calendarAnchor() === e.target) {
      e.preventDefault();
      document.querySelector('.cal [tabindex="0"]')?.focus();
    } else if (e.key === 'Enter' && e.target.matches('input')) e.target.blur();
  });

  async function remove(inv) {
    const ok = await confirmModal({
      title: 'Proje silinsin mi?',
      message: `“${invoiceTitle(inv) || `No ${inv.no}`}” ve ${inv.rows.length} test satırı silinecek.`,
      okLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    const snapshot = structuredClone(inv);
    if (await A.run({ type: 'invoice/delete', id: inv.id })) {
      toast(`No ${inv.no} silindi`, { action: { label: 'Geri al', run: () => A.run({ type: 'invoice/restore', invoice: snapshot }) } });
    }
  }

  // A check's fix: run its actions, or put the cursor where the fix goes.
  async function runFix(inv, issue, fix) {
    if (fix.focus) {
      const card = body.querySelector(`[data-id="${CSS.escape(inv.id)}"]`);
      const scope = fix.focus.row ? card?.querySelector(`[data-row="${CSS.escape(fix.focus.row)}"]`) : card;
      const field = scope?.querySelector(`[data-f="${fix.focus.f}"]`);
      field?.focus();
      field?.select?.();
      if (field?.matches('[data-date]')) openDate(field);
      return;
    }
    for (const action of fix.actions) if (!(await A.run(action))) return;
  }

  body.addEventListener('click', (e) => {
    const line = e.target.closest('.inv-issue');
    if (line && e.target.closest('[data-fix], [data-dismiss]')) {
      const inv = invoice(line.closest('[data-id]').dataset.id);
      const issue = inv && issuesOf(inv).find((i) => i.key === line.dataset.key);
      if (!issue) return;
      const fix = e.target.closest('[data-fix]');
      if (fix) return runFix(inv, issue, issue.fixes[Number(fix.dataset.fix)]);
      return A.run({ type: 'invoice/dismiss-issue', id: inv.id, key: issue.key });
    }
    const b = e.target.closest('[data-i]');
    const id = b?.closest('[data-id]')?.dataset.id;
    const inv = id && invoice(id);
    if (!b || !inv) return;
    switch (b.dataset.i) {
      case 'issues':
        if (openIssues.has(id)) openIssues.delete(id);
        else openIssues.add(id);
        return render(null, { force: true });
      case 'copy':
        return A.copyInvoiceTable(inv);
      case 'row-add': {
        const last = sortedRows(inv).at(-1);
        return A.run({ type: 'invoice/row-add', id, row: { start: last?.end ?? last?.start ?? todayStr() } });
      }
      case 'row-del':
        return A.run({ type: 'invoice/row-delete', id, rowId: b.closest('[data-row]').dataset.row });
      case 'card-menu':
        return menuAt(b, [
          { label: 'Tabloyu kopyala', icon: 'copy', action: () => A.copyInvoiceTable(inv) },
          'sep',
          { label: 'Projeyi sil', icon: 'trash', danger: true, action: () => remove(inv) },
        ]);
    }
  });

  // ---------------------------------------------------------------- Excel

  const inflateRaw = async (bytes) =>
    new Uint8Array(await new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('deflate-raw'))).arrayBuffer());

  // Reads the sheet's project blocks and adds them straight away; a project
  // whose code is already here is updated. The toast can undo it all.
  async function importFile(file) {
    if (/\.xls$/i.test(file.name)) return toast('Eski .xls dosyası okunamıyor. Excel’de .xlsx olarak kaydedip tekrar dene.', { error: true, timeout: 7000 });
    if (!/\.xls[xm]$/i.test(file.name)) return toast('Yalnızca Excel (.xlsx) dosyaları içe aktarılır.', { error: true });
    let projects;
    try {
      projects = parseInvoiceWorkbook(await readWorkbook(new Uint8Array(await file.arrayBuffer()), inflateRaw));
    } catch (err) {
      return toast(err.message || 'Excel dosyası okunamadı.', { error: true });
    }
    if (!projects.length) {
      return toast('Dosyada proje bulunamadı: “Proje Kodu” ve “Test / Test Sistemi / Test Tarihi” başlıkları olan bloklar aranıyor.', { error: true, timeout: 8000 });
    }
    const before = structuredClone({ invoices: state.invoices, devices: state.devices, testTypes: state.testTypes });
    const r = await A.run({ type: 'invoice/import', projects });
    if (!r) return;
    filter = filterInput.value = '';
    body.scrollTop = 0;
    render(null, { force: true });
    const parts = [r.created && `${r.created} proje eklendi`, r.updated && `${r.updated} proje güncellendi`, r.devicesAdded && `${r.devicesAdded} yeni cihaz`].filter(Boolean);
    toast(`${file.name}: ${parts.join(' · ')}`, { timeout: 9000, action: { label: 'Geri al', run: () => A.run({ type: 'invoice/import-undo', ...before }) } });
  }

  function pickFile() {
    fileInput.value = '';
    fileInput.click();
  }

  fileInput.addEventListener('change', () => {
    if (fileInput.files[0]) importFile(fileInput.files[0]);
  });

  // Bulk delete: every card, the ticked ones, or (under a search) the cards
  // the search left on screen.
  async function removeCards(gone, { picked = false } = {}) {
    if (!gone.length) return;
    const all = gone.length === state.invoices.length && !picked;
    const rows = gone.reduce((n, i) => n + i.rows.length, 0);
    const ok = await confirmModal({
      title: all ? 'Tüm projeler silinsin mi?' : `${gone.length} ${picked ? 'seçili ' : ''}proje silinsin mi?`,
      message: `${gone.length} proje ve ${rows} test satırı silinecek (${formatEuro(totalAmount(gone))}). Bildirimdeki “Geri al” hepsini geri getirir.`,
      okLabel: all ? 'Hepsini kaldır' : 'Kaldır',
      danger: true,
    });
    if (!ok) return;
    const snapshot = structuredClone(gone);
    if (await A.run({ type: 'invoice/delete', ids: gone.map((i) => i.id) })) {
      for (const i of gone) selected.delete(i.id);
      lastPicked = null;
      syncPicks();
      toast(`${gone.length} proje kaldırıldı`, { timeout: 10000, action: { label: 'Geri al', run: () => A.run({ type: 'invoice/restore', invoices: snapshot }) } });
    }
  }

  el.querySelector('.inv-tools').addEventListener('click', (e) => {
    if (e.target.closest('[data-i="xlsx"]')) pickFile();
    else if (e.target.closest('[data-i="pick-none"]')) clearPicks();
    else if (e.target.closest('[data-i="only-issues"]')) {
      onlyIssues = !onlyIssues;
      body.scrollTop = 0;
      render(null, { force: true });
    }
    else if (e.target.closest('[data-i="clear"]')) {
      const picked = pickedInvoices();
      if (picked.length) removeCards(picked, { picked: true });
      else removeCards(state.invoices.filter((i) => shownIds.includes(i.id)));
    }
  });

  // The "all" box ticks every card on screen, or clears them when all are.
  el.querySelector('#inv-pick-all').addEventListener('change', (e) => {
    for (const id of shownIds) e.target.checked ? selected.add(id) : selected.delete(id);
    lastPicked = null;
    syncPicks();
  });

  el.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape' || !selected.size || e.defaultPrevented) return;
    if (e.target.matches('input:not([type="checkbox"]), select, textarea') || document.querySelector('.modal-back, .menu')) return;
    e.preventDefault();
    clearPicks();
  });

  el.querySelector('#inv-order').addEventListener('change', (e) => {
    order = e.target.value;
    try {
      localStorage.setItem(ORDER_KEY, order);
    } catch {}
    body.scrollTop = 0;
    render(null, { force: true });
  });

  // ---------------------------------------------------------------- header

  // Devices are every name in the list or on a row; a pair that looks like
  // one system written two ways is offered for merging at the top.
  function manageHtml() {
    const names = deviceNames(state);
    const pairs = similarDevices(state);
    const testOf = (name) => state.devices.find((d) => fold(d.name) === fold(name))?.test ?? '';
    return `${
      pairs.length
        ? `<section class="manage-block manage-suggest">
        <h3>${icon('alert', 13)} Aynı cihaz olabilir</h3>
        ${pairs
          .map(
            (p) => `<div class="manage-row" data-from="${esc(p.from)}" data-to="${esc(p.to)}"><span><span class="m-from">${esc(p.from)}</span> <span class="muted">→</span> <b class="m-to">${esc(p.to)}</b></span>
              <button type="button" class="icon-btn small" data-swap title="Hangi ad kalsın: yönü değiştir" aria-label="Yönü değiştir">⇄</button>
              <button type="button" class="btn small" data-merge-pair>Birleştir</button></div>`,
          )
          .join('')}
      </section>`
        : ''
    }
      <section class="manage-block">
        <h3>Cihazlar</h3>
        ${
          names
            .map(
              (d) => `<div class="manage-row"><span>${esc(d.name)}</span><span class="muted small">${[testOf(d.name), d.rows ? `${d.rows} satır` : 'kullanılmıyor'].filter(Boolean).map(esc).join(' · ')}</span>
                <button type="button" class="link-btn" data-merge="${esc(d.name)}" title="Başka bir cihazla aynıysa birleştir">Birleştir…</button>
                ${d.listed ? `<button type="button" class="icon-btn small" data-rm-device="${esc(d.name)}" title="Listeden kaldır">${icon('x', 13)}</button>` : '<span class="manage-gap"></span>'}</div>`,
            )
            .join('') || '<p class="muted small">Cihaz yok.</p>'
        }
        <button type="button" class="link-btn" data-add="device">${icon('plus', 14)}Cihaz ekle</button>
      </section>
      ${
        state.deviceAliases.length
          ? `<section class="manage-block">
        <h3>Birleştirilen adlar</h3>
        ${state.deviceAliases.map((a) => `<div class="manage-row"><span>${esc(a.from)} <span class="muted">→</span> ${esc(a.to)}</span><button type="button" class="icon-btn small" data-unalias="${esc(a.from)}" title="Bu eşleştirmeyi kaldır (kartlar değişmez)">${icon('x', 13)}</button></div>`).join('')}
        <p class="hint">Excel’den soldaki ad gelince sağdaki yazılır.</p>
      </section>`
          : ''
      }
      <section class="manage-block">
        <h3>Test türleri</h3>
        ${state.testTypes.map((t) => `<div class="manage-row"><span>${esc(t)}</span><button type="button" class="icon-btn small" data-rm-test="${esc(t)}" title="Kaldır">${icon('x', 13)}</button></div>`).join('') || '<p class="muted small">Test türü yok.</p>'}
        <button type="button" class="link-btn" data-add="test">${icon('plus', 14)}Test türü ekle</button>
      </section>
      <p class="hint">Kaldırılan cihaz veya test, onu kullanan eski satırlarda olduğu gibi kalır.</p>`;
  }

  async function mergeDevices(from, to) {
    const snapshot = structuredClone({ invoices: state.invoices, devices: state.devices, testTypes: state.testTypes, deviceAliases: state.deviceAliases });
    const r = await A.run({ type: 'device/merge', from, to });
    if (r) toast(`“${from}” → “${to}” · ${r.rows} satır`, { timeout: 9000, action: { label: 'Geri al', run: () => A.run({ type: 'invoice/import-undo', ...snapshot }) } });
  }

  async function mergeDialog(from) {
    const others = deviceNames(state).filter((d) => fold(d.name) !== fold(from));
    const rows = deviceNames(state).find((d) => fold(d.name) === fold(from))?.rows ?? 0;
    const box = document.createElement('div');
    box.innerHTML = `<p class="modal-text">“${esc(from)}” hangi cihazla aynı?</p>
      <select class="input" data-k="to">${others.map((d) => `<option value="${esc(d.name)}">${esc(d.name)}${d.rows ? ` (${d.rows} satır)` : ''}</option>`).join('')}</select>
      <p class="hint">${rows} satır seçilen ada geçer; Excel’den “${esc(from)}” gelince de seçilen ad yazılır.</p>`;
    const to = await modal({
      title: 'Cihazı birleştir',
      body: box,
      buttons: [
        { label: 'Vazgeç', value: null },
        { label: 'Birleştir', primary: true, value: (b) => b.querySelector('[data-k="to"]').value },
      ],
    });
    if (to) await mergeDevices(from, to);
  }

  function renderManage() {
    const target = manageBox?.querySelector('.manage');
    if (target) target.innerHTML = manageHtml();
  }

  function openManage() {
    const box = Object.assign(document.createElement('div'), { className: 'manage' });
    modal({
      title: 'Cihazlar ve test türleri',
      body: box,
      className: 'wide',
      onOpen(m) {
        manageBox = m;
        renderManage();
        m.addEventListener('click', (e) => {
          const b = e.target.closest('button');
          if (!b) return;
          if (b.dataset.rmDevice) A.run({ type: 'device/remove', name: b.dataset.rmDevice });
          else if ('mergePair' in b.dataset) {
            const row = b.closest('[data-from]');
            mergeDevices(row.dataset.from, row.dataset.to);
          } else if ('swap' in b.dataset) {
            // The name on the right is the one kept.
            const row = b.closest('[data-from]');
            [row.dataset.from, row.dataset.to] = [row.dataset.to, row.dataset.from];
            row.querySelector('.m-from').textContent = row.dataset.from;
            row.querySelector('.m-to').textContent = row.dataset.to;
          }
          else if (b.dataset.merge) mergeDialog(b.dataset.merge);
          else if (b.dataset.unalias) A.run({ type: 'device/unalias', from: b.dataset.unalias });
          else if (b.dataset.rmTest) A.run({ type: 'testtype/remove', name: b.dataset.rmTest });
          else if (b.dataset.add === 'device') addDevice();
          else if (b.dataset.add === 'test') addTestType();
        });
      },
    }).then(() => {
      manageBox = null;
    });
  }

  el.querySelector('.inv-head').addEventListener('click', (e) => {
    const b = e.target.closest('[data-i], [data-tab]');
    if (!b) return;
    if (b.dataset.tab) setTab(b.dataset.tab);
    else if (b.dataset.i === 'new') newProject();
    else if (b.dataset.i === 'menu')
      menuAt(b, [
        { label: 'Excel’den içe aktar…', icon: 'download', action: pickFile },
        { label: 'Cihazlar ve test türleri…', icon: 'settings', action: openManage, hint: similarDevices(state).length ? `${similarDevices(state).length} öneri` : undefined },
        'sep',
        { label: 'Hepsini kaldır…', icon: 'trash', danger: true, disabled: !state.invoices.length, action: () => removeCards(state.invoices) },
      ]);
    else if (b.dataset.i === 'sidebar') onToggleSidebar();
  });

  filterInput.addEventListener('input', () => {
    filter = filterInput.value;
    body.scrollTop = 0;
    render(null, { force: true });
  });
  filterInput.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && filter) {
      e.preventDefault();
      e.stopPropagation();
      filter = filterInput.value = '';
      render(null, { force: true });
    }
  });

  return {
    render,
    newProject,
    importFile,
    // Opens the screen filtered to one project code.
    showCode: (code) => {
      filter = filterInput.value = code;
      tab = 'cards';
      render(null, { force: true });
    },
    focusFilter: () => {
      if (tab !== 'cards') setTab('cards');
      filterInput.focus();
      filterInput.select();
    },
  };
}
