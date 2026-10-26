// Main window: rail, sidebar (views, lists, stages, tags), the report list
// with its stats, the detail pane, and the Faturalar screen.

import { api } from './api.js';
import { dayIcon, icon } from './icons.js';
import { TaskTree } from './tree.js';
import { createInvoicesView } from './invoices-view.js';
import { changelogUnseen, openChangelog } from './changelog-view.js';
import { PALETTES } from '../../shared/themes.js';
import { calendarAnchor, closeCalendar, openCalendar } from './calendar.js';
import { formatTrDate } from '../../shared/dates.js';
import * as A from './actions.js';
import { PRIORITY_LABELS, PRIORITY_COLORS, applyTheme, confirmModal, dueLabel, esc, formatDateTime, menuAt, modal, promptModal, showMenu, tagFill, toast } from './ui.js';
import { INBOX_ID, MAX_DEPTH, ancestors, buildIndex, counts, depthOf, findTag, getList, subtreeHeight, todayStr } from '../../shared/store.js';
import { fold } from '../../shared/search.js';
import { openStagesDialog, renderStagesDialog } from './stages-dialog.js';
import { buildCompletedRows, buildDueRows, buildListRows, buildSearchRows, buildStageRows, buildTagRows, buildTrashRows } from '../../shared/tree-rows.js';
import { dateRangeLabel, formatEuro, formatEuroShort, shiftLabel, sortedRows, totalAmount } from '../../shared/invoices.js';

const $ = (s) => document.querySelector(s);
const els = {
  layout: $('#app'),
  rail: $('#rail'),
  sidebar: $('#sidebar'),
  header: $('#list-header'),
  addRow: $('#add-row'),
  addInput: $('#add-input'),
  body: $('#list-body'),
  tree: $('#tree'),
  empty: $('#list-empty'),
  detail: $('#detail'),
  invoices: $('#invoices'),
  hdDate: $('#hd-date'),
  hdTitle: $('#hd-title'),
  hdTools: $('#hd-tools'),
  hdNew: $('#hd-new'),
  search: $('#search-input'),
  searchCount: $('#search-count'),
  stats: $('#stats'),
};

const WEEKDAYS = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi'];

let state = null;
let appVersion = '';
let index = null;
let view = loadView();
let selectedId = null;
let query = '';
let headerKey = '';
// The view to go back to when the header search is cleared.
let searchReturn = null;
let detailDirty = false;
const afterState = [];

document.body.classList.add(`os-${api.platform}`);

const invoices = createInvoicesView(els.invoices, { onToggleSidebar: () => toggleSidebar() });

const tree = new TaskTree(els.tree, {
  scroller: els.body,
  subtreeHeight: (id) => subtreeHeight(index, id),
  onSelect: (row) => select(row.id),
  onAction: (act, row, e) => rowAction(act, row, e),
  onContextMenu: (row, x, y) => {
    if (row.kind === 'section') A.showSectionMenu(menuCtx(), row, x, y);
    else A.showTaskMenu(menuCtx(), row, x, y);
  },
  onRename: (row, text, info) => A.rename(state, tree, row, text, info),
  onRenameCancel: (row) => A.cancelNew(row),
  onMove: (action, info) => A.move(state, action, info),
});

function menuCtx() {
  const flat = view.type !== 'list';
  return { state, tree, flat, onReveal: flat ? reveal : null };
}

// ---------------------------------------------------------------- state & views

function loadView() {
  try {
    const v = JSON.parse(localStorage.getItem('xloer-view'));
    if (v && typeof v.type === 'string') return v;
  } catch {}
  return { type: 'list', id: null };
}

function validView(v) {
  if (v.type === 'list') return !!getList(state, v.id);
  if (v.type === 'tag') return state.tags.some((g) => g.name === v.id);
  if (v.type === 'stage') return state.stages.some((st) => st.id === v.id);
  return ['today', 'next7', 'completed', 'trash', 'search', 'invoices'].includes(v.type);
}

function setState(next) {
  state = next;
  index = buildIndex(state);
  applyTheme(state.settings.theme, state.settings.palette);
  if (!validView(view)) view = { type: 'list', id: (state.lists.find((l) => !l.system) ?? state.lists[0]).id };
  if (selectedId && !index.byId.get(selectedId)) selectedId = null;
  render();
  renderStagesDialog(state);
  for (const fn of afterState.splice(0)) fn();
}

// Runs fn once the state this window has passes check (the change that was
// just dispatched may arrive before or after the dispatch call returns).
function whenReady(check, fn) {
  if (check()) fn();
  else afterState.push(() => check() && fn());
}

const whenTask = (id, fn) => whenReady(() => index.byId.has(id), fn);

function go(v, { keepSelection = false } = {}) {
  view = v;
  try {
    localStorage.setItem('xloer-view', JSON.stringify(v));
  } catch {}
  if (!keepSelection) selectedId = null;
  els.body.scrollTop = 0;
  render();
  if (v.type === 'search') els.search.focus();
}

let detailClear = null;

function select(id) {
  if (selectedId === id) return;
  flushDetail();
  selectedId = id;
  tree.select(id);
  renderDetail(true);
}

async function reveal(id) {
  const t = index.byId.get(id);
  if (!t || t.deletedAt) return;
  const list = getList(state, t.listId);
  const jobs = ancestors(index, t)
    .filter((a) => a.collapsed)
    .map((a) => A.run({ type: 'task/update', id: a.id, patch: { collapsed: false } }));
  const sec = list.sections.find((s) => s.id === t.sectionId);
  if (sec?.collapsed) jobs.push(A.run({ type: 'section/update', listId: list.id, id: sec.id, patch: { collapsed: false } }));
  if (t.done && !list.showCompleted) jobs.push(A.run({ type: 'list/update', id: list.id, patch: { showCompleted: true } }));
  await Promise.all(jobs);
  if (view.type === 'search') {
    query = els.search.value = '';
    searchReturn = null;
  }
  go({ type: 'list', id: t.listId }, { keepSelection: true });
  selectedId = id;
  tree.select(id);
  renderDetail(true);
  setTimeout(() => tree.scrollTo(id, { flash: true }), 60);
}

// ---------------------------------------------------------------- render

function render() {
  renderRail();
  renderSidebar();
  const invoicesMode = view.type === 'invoices';
  els.layout.classList.toggle('mode-invoices', invoicesMode);
  if (invoicesMode) return invoices.render(state);
  renderHeader();
  renderStats();
  renderBody();
  renderDetail();
}

function renderRail() {
  for (const b of els.rail.querySelectorAll('[data-rail]')) {
    const k = b.dataset.rail;
    b.classList.toggle('active', (k === 'search' && view.type === 'search') || (k === 'tasks' && view.type !== 'search' && view.type !== 'invoices') || (k === 'invoices' && view.type === 'invoices'));
  }
  // The widget button is a switch, not a place: lit while the widget is shown.
  els.rail.querySelector('[data-rail="widget"]').classList.toggle('on', state.settings.widget.visible);
  const pending = UPDATE_PENDING.includes(update.status);
  const btn = els.rail.querySelector('[data-rail="update"]');
  btn.classList.toggle('has-update', pending);
  btn.title = pending ? `Güncelleme var: v${update.version}` : 'Güncellemeler';
  btn.querySelector('.rail-dot').hidden = !pending;
  // Yenilikler: a dot until it has been opened on this version.
  const news = els.rail.querySelector('[data-rail="changelog"]');
  const unseen = !!appVersion && changelogUnseen(appVersion);
  news.querySelector('.rail-dot').hidden = !unseen;
  news.title = unseen ? `Yenilikler: Xloer ${appVersion} ile gelenler` : 'Yenilikler';
}

const showChangelog = () => openChangelog(appVersion, { onSeen: renderRail });

const isActive = (type, id = null) => view.type === type && (id == null || view.id === id);

// fill: a tag's colour, which fills its whole box.
function navItem(type, id, lead, label, count, { attrs = '', fill = null } = {}) {
  const style = tagFill(fill);
  return `<button type="button" class="nav-item${isActive(type, id) ? ' active' : ''}${style ? ' filled' : ''}" data-view="${type}" ${id != null ? `data-id="${esc(id)}"` : ''} ${attrs}${style}>
    <span class="nav-icon">${lead}</span><span class="nav-label">${esc(label)}</span><span class="nav-count">${count || ''}</span></button>`;
}

function renderSidebar() {
  const c = counts(state);
  const inbox = getList(state, INBOX_ID);
  const lists = state.lists.filter((l) => !l.system);
  const scroll = els.sidebar.querySelector('.side-scroll')?.scrollTop ?? 0;
  const last = state.invoices.reduce((a, b) => (!a || b.no > a.no ? b : a), null);
  els.sidebar.innerHTML = `
    <div class="side-top"></div>
    <div class="side-scroll">
      <div class="side-head"><span>Görünüm</span></div>
      ${navItem('today', null, dayIcon(Number(todayStr().slice(8))), 'Bugün', c.today)}
      ${navItem('next7', null, dayIcon('7'), 'Sonraki 7 Gün', c.next7)}
      <div class="side-head"><span>Listeler</span><button type="button" class="icon-btn small" data-side="add-list" title="Yeni liste">${icon('plus', 15)}</button></div>
      ${navItem('list', INBOX_ID, icon('inbox'), inbox.name, c.lists.get(INBOX_ID), { attrs: `data-drop-list="${INBOX_ID}"` })}
      ${lists.map((l) => navItem('list', l.id, `<span class="emoji">${esc(l.emoji)}</span>`, l.name, c.lists.get(l.id), { attrs: `data-drop-list="${esc(l.id)}"` })).join('')}
      <div class="side-head"><span>Aşama</span><button type="button" class="icon-btn small" data-side="edit-stages" title="Aşamaları düzenle">${icon('edit', 14)}</button></div>
      ${state.stages.map((st, i) => navItem('stage', st.id, '<span class="stage-mark"></span>', st.name, c.stages[i])).join('')}
      ${state.tags.length ? `<div class="side-head"><span>Etiketler</span></div>` : ''}
      ${state.tags.map((g) => navItem('tag', g.name, icon('tag'), g.name, c.tags.get(g.name.toLocaleLowerCase('tr')), { fill: g.color })).join('')}
      <div class="side-sep"></div>
      ${navItem('completed', null, icon('completed'), 'Tamamlanan', 0)}
      ${navItem('trash', null, icon('trash'), 'Çöp Kutusu', c.trash)}
    </div>
    <button type="button" class="side-total" data-view="invoices" title="Faturalar’ı aç">
      <span class="side-total-label">Fatura toplamı</span>
      <span class="side-total-sum">${esc(formatEuro(totalAmount(state.invoices)))}</span>
      <span class="side-total-sub">${state.invoices.length} proje${last ? ` · son No ${last.no}${last.code ? ` · ${esc(last.code)}` : ''}` : ''}</span>
    </button>`;
  els.sidebar.querySelector('.side-scroll').scrollTop = scroll;
}

function viewTitle() {
  switch (view.type) {
    case 'list': {
      const l = getList(state, view.id);
      return { emoji: l.emoji, title: l.name };
    }
    case 'today':
      return { title: 'Bugün' };
    case 'next7':
      return { title: 'Sonraki 7 Gün' };
    case 'tag':
      return { title: `#${view.id}`, kicker: 'Etiket' };
    case 'stage':
      return { title: stageName(view.id), kicker: 'Aşama' };
    case 'completed':
      return { title: 'Tamamlanan' };
    case 'trash':
      return { title: 'Çöp Kutusu' };
    default:
      return { title: 'Arama', kicker: 'Sonuçlar' };
  }
}

function renderHeader() {
  const { emoji, title, kicker } = viewTitle();
  const list = view.type === 'list' ? getList(state, view.id) : null;
  const now = new Date();
  els.hdDate.textContent = kicker ?? `${todayStr(now).split('-').reverse().join('.')} · ${WEEKDAYS[now.getDay()]}`;
  els.hdTitle.innerHTML = `${emoji ? `<span class="emoji">${esc(emoji)}</span>` : ''}<span class="name">${esc(title)}</span>`;
  if (document.activeElement !== els.search && els.search.value !== query) els.search.value = query;
  const key = JSON.stringify([view, list?.showCompleted]);
  if (key !== headerKey) {
    headerKey = key;
    let tools = '';
    if (list) {
      tools = `<button type="button" class="icon-btn" data-h="toggle-done" title="${list.showCompleted ? 'Tamamlananları gizle' : 'Tamamlananları göster'}">${icon(list.showCompleted ? 'eyeOff' : 'eye')}</button>
        <button type="button" class="icon-btn" data-h="list-menu" title="Liste seçenekleri">${icon('more')}</button>`;
    } else if (view.type === 'trash') {
      tools = `<button type="button" class="btn small" data-h="empty-trash">Çöpü boşalt</button>`;
    }
    els.hdTools.innerHTML = tools;
  }
  const addable = ['list', 'today', 'next7', 'tag'].includes(view.type);
  els.hdNew.hidden = !addable;
  els.addRow.hidden = !addable;
  if (addable) {
    els.addInput.placeholder =
      view.type === 'list'
        ? `“${list.name}” listesine görev ekle, Enter’a bas (#etiket kullanılabilir)`
        : view.type === 'tag'
          ? `#${view.id} etiketli görev ekle`
          : 'Bugün için görev ekle (Gelen Kutusu’na)';
  }
}

// The four counters above a list: its open reports and steps, reports
// waiting for the customer, and the invoice total.
const stageName = (id) => state.stages.find((st) => st.id === id)?.name ?? 'Aşama';

function renderStats() {
  const list = view.type === 'list' ? getList(state, view.id) : null;
  els.stats.hidden = !list;
  if (!list) return;
  const open = state.tasks.filter((t) => !t.deletedAt && !t.done && t.listId === list.id);
  const reports = open.filter((t) => !t.parentId);
  const customer = state.stages.find((st) => fold(st.name).startsWith('musteri'));
  const tiles = [
    ['Açık rapor', reports.length],
    ['Bekleyen adım', open.length - reports.length],
    // Reports waiting on the customer, while a stage of that name exists.
    customer && ['Müşteride', reports.filter((t) => t.stage === customer.id).length],
    ['Faturalar', formatEuroShort(totalAmount(state.invoices))],
  ].filter(Boolean);
  els.stats.innerHTML = tiles.map(([label, value]) => `<div class="stat"><span class="stat-label">${esc(label)}</span><span class="stat-value">${esc(String(value))}</span></div>`).join('');
}

function renderBody() {
  let rows = [];
  let opts = {};
  let empty = '';
  switch (view.type) {
    case 'list':
      rows = buildListRows(state, view.id, { index });
      opts = { dnd: 'tree' };
      empty = 'Bu listede görev yok. Yukarıdaki kutudan ekleyebilirsin.';
      break;
    case 'today':
    case 'next7':
      rows = buildDueRows(state, view.type);
      opts = { dnd: 'external' };
      empty = view.type === 'today' ? 'Bugün için planlanmış görev yok.' : 'Önümüzdeki 7 gün için planlanmış görev yok.';
      break;
    case 'tag':
      rows = buildTagRows(state, view.id);
      opts = { dnd: 'external' };
      empty = 'Bu etikete sahip açık görev yok.';
      break;
    case 'stage':
      rows = buildStageRows(state, view.id);
      opts = { dnd: 'external' };
      empty = `“${stageName(view.id)}” aşamasında açık rapor yok. Bir raporun aşamasını satırındaki çizgiye tıklayarak seçebilirsin.`;
      break;
    case 'completed':
      rows = buildCompletedRows(state);
      empty = 'Henüz tamamlanan görev yok.';
      break;
    case 'trash':
      rows = buildTrashRows(state);
      opts = { readonly: true };
      empty = 'Çöp kutusu boş.';
      break;
    case 'search': {
      const r = buildSearchRows(state, query);
      rows = r.rows;
      opts = { search: true, terms: r.terms };
      empty = query.trim()
        ? `“${query.trim()}” için sonuç bulunamadı. Başka bir kelime dene ya da #etiket ile ara.`
        : 'Aramak için yazmaya başla. İpucu: “#RAPOR ing” gibi etiket ve kelimeleri birleştirebilirsin.';
      showSearchCount(r.matches);
      break;
    }
  }
  if (view.type !== 'search') showSearchCount(null);
  // Top-level tasks show as reports: stage pipeline and next step.
  tree.setRows(rows, { ...opts, selectedId, reports: view.type !== 'search' && view.type !== 'trash', stages: state.stages, tags: state.tags });
  els.empty.hidden = rows.length > 0;
  els.empty.textContent = empty;
}

// ---------------------------------------------------------------- detail pane

let pendingPatch = null;
let patchTimer = null;

function queuePatch(id, patch) {
  if (pendingPatch && pendingPatch.id !== id) flushDetail();
  pendingPatch = { id, patch: { ...(pendingPatch?.patch ?? {}), ...patch } };
  clearTimeout(patchTimer);
  patchTimer = setTimeout(flushDetail, 450);
}

function flushDetail() {
  clearTimeout(patchTimer);
  if (!pendingPatch) return;
  const { id, patch } = pendingPatch;
  pendingPatch = null;
  A.run({ type: 'task/update', id, patch });
}

function autosize(ta) {
  if (!ta) return;
  ta.style.height = 'auto';
  ta.style.height = `${ta.scrollHeight}px`;
}

function renderDetail(force = false) {
  const t = selectedId ? index.byId.get(selectedId) : null;
  const active = document.activeElement;
  if (!force && t && els.detail.dataset.id === t.id && els.detail.contains(active) && active.matches('input, textarea')) {
    detailDirty = true;
    return;
  }
  detailDirty = false;
  setPanelOpen(!!t && !t.deletedAt);
  clearTimeout(detailClear);
  if (!t || t.deletedAt) {
    // The panel slides shut with its content still in it, then empties.
    els.detail.dataset.id = '';
    detailClear = setTimeout(() => {
      if (!els.layout.classList.contains('has-selection')) els.detail.innerHTML = '';
    }, 300);
    return;
  }
  els.detail.dataset.id = t.id;
  const list = getList(state, t.listId);
  const sec = list?.sections.find((s) => s.id === t.sectionId);
  const kids = index.children(t.id);
  const due = dueLabel(t.due, t.done);
  const parents = ancestors(index, t);
  const canNest = depthOf(index, t) < MAX_DEPTH;
  const report = !t.parentId;
  els.detail.innerHTML = `
    <div class="detail-head">
      <button type="button" class="check big p${t.priority}" data-d="toggle" role="checkbox" aria-checked="${t.done}" title="Tamamlandı">${icon('checkmark', 13)}</button>
      <span class="d-divider"></span>
      <button type="button" class="date-pill${t.due ? ' set' : ''} ${due?.tone ?? ''}" data-d="due" title="Bitiş tarihi${t.due ? `: ${formatTrDate(t.due)}` : ''}" aria-haspopup="dialog">${icon('calendar', 15)}<span>${due ? esc(due.text) : 'Tarih ekle'}</span></button>
      ${t.due ? `<button type="button" class="icon-btn small" data-d="clear-due" title="Tarihi kaldır">${icon('x', 13)}</button>` : ''}
      <span class="flex"></span>
      <button type="button" class="icon-btn" data-d="priority" title="${PRIORITY_LABELS[t.priority]}" style="color:${PRIORITY_COLORS[t.priority]}">${icon('flag')}</button>
      <button type="button" class="icon-btn detail-close" data-d="close" title="Kapat">${icon('x')}</button>
    </div>
    <div class="detail-scroll">
      ${parents.length ? `<nav class="d-crumbs">${parents.map((p) => `<button type="button" data-d="goto" data-id="${esc(p.id)}">${esc(p.title || 'Başlıksız')}</button>`).join(`<span>${icon('chevronRight', 12)}</span>`)}</nav>` : ''}
      ${report ? `<input class="d-code" data-d="code" value="${esc(t.code)}" placeholder="+ Proje kodu (P26-…)" spellcheck="false" aria-label="Proje kodu">` : ''}
      <textarea class="d-title${t.done ? ' done' : ''}" data-d="title" rows="1" placeholder="Başlık" spellcheck="false">${esc(t.title)}</textarea>
      ${report ? stageHtml(t) : ''}
      <textarea class="d-notes" data-d="notes" placeholder="Açıklama veya not ekle…" spellcheck="false">${esc(t.notes)}</textarea>
      <section class="d-block">
        <h3><span>Adımlar</span>${kids.length ? `<span class="muted">${kids.filter((k) => k.done).length}/${kids.length}</span>` : ''}</h3>
        ${kids
          .map(
            (k) => `<div class="d-sub${k.done ? ' done' : ''}" data-id="${esc(k.id)}">
              <button type="button" class="check p${k.priority}" data-d="sub-toggle" role="checkbox" aria-checked="${k.done}" aria-label="Tamamlandı">${icon('checkmark', 12)}</button>
              <input type="text" data-d="sub-title" value="${esc(k.title)}" spellcheck="false" aria-label="Alt görev adı">
              <button type="button" class="icon-btn small" data-d="sub-open" title="Alt görevi aç">${icon('chevronRight', 14)}</button>
              <button type="button" class="icon-btn small" data-d="sub-delete" title="Sil">${icon('x', 13)}</button>
            </div>`,
          )
          .join('')}
        ${canNest ? `<div class="d-sub add">${icon('plus', 14)}<input type="text" data-d="sub-add" placeholder="Adım ekle, Enter’a bas" spellcheck="false"></div>` : '<p class="muted small">En fazla 5 seviye alt görev olabilir.</p>'}
      </section>
      ${report && t.code ? invoiceHtml(t) : ''}
      <section class="d-block">
        <h3><span>Etiketler</span></h3>
        <div class="d-tags">
          ${t.tags.map((g) => `<span class="tag-chip big"${tagFill(findTag(state, g)?.color)}>#${esc(g)}<button type="button" data-d="tag-remove" data-tag="${esc(g)}" aria-label="#${esc(g)} etiketini kaldır">${icon('x', 11)}</button></span>`).join('')}
          <input type="text" class="tag-input" data-d="tag-add" placeholder="+ Etiket" list="tag-options" spellcheck="false" aria-label="Etiket ekle">
          <datalist id="tag-options">${state.tags.map((g) => `<option value="${esc(g.name)}"></option>`).join('')}</datalist>
        </div>
      </section>
    </div>
    <div class="detail-foot">
      <button type="button" class="foot-btn" data-d="move" title="Başka listeye veya bölüme taşı"><span class="emoji">${esc(list?.emoji ?? '')}</span><span>${esc(list?.name ?? '')}</span>${sec ? `<span class="muted">› ${esc(sec.name)}</span>` : ''}${icon('chevron', 13)}</button>
      <span class="flex"></span>
      <span class="muted small" title="Oluşturulma zamanı">${esc(formatDateTime(t.createdAt))}</span>
      <button type="button" class="icon-btn" data-d="delete" title="Sil">${icon('trash')}</button>
    </div>`;
  autosize(els.detail.querySelector('.d-title'));
}

function stageHtml(t) {
  const at = state.stages.findIndex((st) => st.id === t.stage);
  const steps = state.stages.map((st, i) => {
    const cls = at < 0 ? '' : i < at ? ' done' : i === at ? ' now' : '';
    return `<button type="button" class="d-stage-step${cls}" data-d="stage" data-stage="${esc(st.id)}" aria-pressed="${i === at}"><span class="bar"></span><span class="lbl">${esc(st.name)}</span></button>`;
  });
  if (!steps.length) return '';
  return `<section class="d-block"><h3><span>Aşama</span>${at < 0 ? '<span class="muted">seçilmedi</span>' : ''}</h3><div class="d-stage" role="group" aria-label="Aşama" style="--stages:${steps.length}">${steps.join('')}</div></section>`;
}

// The invoice card that has this report's project code: its tests, amount
// and the copy button, so report and invoice are seen together.
function linkedInvoice(t) {
  const code = t.code.toLocaleLowerCase('tr');
  return state.invoices.find((i) => i.code && i.code.toLocaleLowerCase('tr') === code) ?? null;
}

function invoiceHtml(t) {
  const inv = linkedInvoice(t);
  if (!inv) {
    return `<section class="d-block"><h3><span>Fatura</span></h3>
      <p class="muted small">${esc(t.code)} koduyla bir fatura kartı yok.</p>
      <button type="button" class="btn small" data-d="inv-create">Fatura kartı oluştur</button></section>`;
  }
  const tests = sortedRows(inv)
    .map((r) => `<div class="d-test"><span>${esc(r.test || '—')}</span><span class="mono">${esc(shiftLabel(r))}</span><span class="muted">${esc(r.device || '—')}</span><span class="muted mono">${esc(dateRangeLabel(r))}</span></div>`)
    .join('');
  return `<section class="d-block">
      <h3><span>Testler</span><span class="muted">No ${inv.no}</span></h3>
      ${tests || '<p class="muted small">Fatura kartında henüz test yok.</p>'}
      <div class="d-inv">
        <div class="flex"><div class="d-inv-label">Fatura</div><div class="d-inv-sum">${esc(inv.amount == null ? '—' : formatEuro(inv.amount))}</div></div>
        <button type="button" class="btn small" data-d="inv-copy">Tabloyu kopyala</button>
        <button type="button" class="icon-btn" data-d="inv-open" title="Faturalar’da aç">${icon('external', 15)}</button>
      </div>
    </section>`;
}

function detailTask() {
  return index.byId.get(els.detail.dataset.id);
}

function refocusDetail(selector) {
  renderDetail(true);
  els.detail.querySelector(selector)?.focus();
}

els.detail.addEventListener('click', (e) => {
  const b = e.target.closest('[data-d]');
  const t = detailTask();
  if (!b || !t) return;
  const sub = b.closest('.d-sub')?.dataset.id;
  const subTask = sub && index.byId.get(sub);
  switch (b.dataset.d) {
    case 'toggle':
      return A.toggle(state, t);
    case 'close':
      return select(null);
    case 'clear-due':
      return A.run({ type: 'task/update', id: t.id, patch: { due: null } });
    case 'priority':
      return menuAt(b, A.priorityItems(t));
    case 'stage':
      return A.setStage(t, b.dataset.stage);
    case 'inv-copy':
      return A.copyInvoiceTable(linkedInvoice(t));
    case 'inv-open':
      go({ type: 'invoices' });
      return invoices.showCode(t.code);
    case 'inv-create':
      return createInvoiceFor(t);
    case 'goto':
      return reveal(b.dataset.id);
    case 'sub-toggle':
      return subTask && A.toggle(state, subTask);
    case 'sub-open':
      return subTask && (view.type === 'list' ? reveal(sub) : select(sub));
    case 'sub-delete':
      return subTask && A.remove(subTask);
    case 'tag-remove':
      return A.run({ type: 'task/update', id: t.id, patch: { tags: t.tags.filter((g) => g !== b.dataset.tag) } });
    case 'move':
      return menuAt(b, A.moveTargets(state, t));
    case 'delete':
      selectedId = null;
      return A.remove(t);
  }
});

// The date pill opens the Turkish calendar; a second click closes it.
els.detail.addEventListener('click', (e) => {
  const pill = e.target.closest('.date-pill');
  const t = pill && detailTask();
  if (!t) return;
  if (calendarAnchor() === pill) return closeCalendar();
  openCalendar(pill, { value: t.due, clearable: !!t.due, onPick: (due) => A.run({ type: 'task/update', id: t.id, patch: { due } }) });
});

els.detail.addEventListener('input', (e) => {
  const t = detailTask();
  const d = e.target.dataset.d;
  if (!t) return;
  if (d === 'title') {
    autosize(e.target);
    queuePatch(t.id, { title: e.target.value });
  } else if (d === 'notes') queuePatch(t.id, { notes: e.target.value });
});

els.detail.addEventListener('change', (e) => {
  const t = detailTask();
  if (!t) return;
  const d = e.target.dataset.d;
  if (d === 'code') A.run({ type: 'task/update', id: t.id, patch: { code: e.target.value } });
  else if (d === 'sub-title') {
    const id = e.target.closest('.d-sub').dataset.id;
    if (e.target.value.trim()) A.run({ type: 'task/update', id, patch: { title: e.target.value.trim() } });
  }
});

els.detail.addEventListener('keydown', async (e) => {
  const t = detailTask();
  const d = e.target.dataset.d;
  if (!t) return;
  if (d === 'title' && e.key === 'Enter') {
    e.preventDefault();
    e.target.blur();
  } else if ((d === 'sub-title' || d === 'code') && e.key === 'Enter') e.target.blur();
  else if (d === 'sub-add' && e.key === 'Enter' && e.target.value.trim()) {
    const id = await A.run({ type: 'task/create', parentId: t.id, title: e.target.value.trim() });
    if (id) whenTask(id, () => refocusDetail('[data-d="sub-add"]'));
  } else if (d === 'tag-add' && (e.key === 'Enter' || e.key === ',') && e.target.value.trim()) {
    e.preventDefault();
    const tag = e.target.value.trim().replace(/^#/, '');
    await A.run({ type: 'task/update', id: t.id, patch: { tags: [...t.tags, tag] } });
    refocusDetail('[data-d="tag-add"]');
  }
});

els.detail.addEventListener('focusout', () => {
  flushDetail();
  setTimeout(() => {
    const a = document.activeElement;
    if (detailDirty && !(els.detail.contains(a) && a.matches('input, textarea'))) renderDetail();
  });
});

// ---------------------------------------------------------------- actions

function rowAction(act, row, e) {
  const list = getList(state, row.listId);
  switch (act) {
    case 'collapse':
      if (row.kind === 'section') return A.run({ type: 'section/update', listId: row.listId, id: row.id, patch: { collapsed: !row.collapsed } });
      if (row.childCount) return A.run({ type: 'task/update', id: row.id, patch: { collapsed: !row.collapsed } });
      return;
    case 'toggle':
      return A.toggle(state, row.task);
    case 'stage':
      return A.setStage(row.task, e.target.closest('[data-stage]').dataset.stage);
    case 'add-in-section':
      return A.addInSection(tree, list, row.id);
    case 'section-menu': {
      const r = e.target.closest('button').getBoundingClientRect();
      return A.showSectionMenu(menuCtx(), row, r.left, r.bottom + 4);
    }
    case 'restore':
      return A.run({ type: 'task/restore', id: row.id });
    case 'purge':
      return A.purge(row.task);
  }
}

async function createInvoiceFor(t) {
  const id = await A.run({ type: 'invoice/create', code: t.code, title: t.title });
  if (!id) return;
  await A.run({ type: 'invoice/row-add', id, row: { start: todayStr() } });
  go({ type: 'invoices' });
  invoices.showCode(t.code);
}

async function addTask(text) {
  const { title, tags } = A.parseQuickAdd(text);
  const base = { title, tags, atStart: true };
  let action;
  if (view.type === 'list') action = { type: 'task/create', listId: view.id, ...base };
  else if (view.type === 'tag') action = { type: 'task/create', listId: INBOX_ID, ...base, tags: [...tags, view.id] };
  else action = { type: 'task/create', listId: INBOX_ID, ...base, due: todayStr() };
  const id = await A.run(action);
  if (id) whenTask(id, () => tree.scrollTo(id, { flash: true }));
}

els.addInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && els.addInput.value.trim()) {
    addTask(els.addInput.value);
    els.addInput.value = '';
  } else if (e.key === 'Escape') els.addInput.blur();
});

async function addList() {
  const r = await promptModal({ title: 'Yeni liste', placeholder: 'Liste adı (ör. Work)', okLabel: 'Oluştur', emoji: '📁' });
  if (!r) return;
  const id = await A.run({ type: 'list/create', name: r.text, emoji: r.emoji });
  if (id) whenReady(() => getList(state, id), () => go({ type: 'list', id }));
}

async function editList(list) {
  const r = await promptModal({ title: 'Listeyi düzenle', value: list.name, okLabel: 'Kaydet', emoji: list.emoji });
  if (r) A.run({ type: 'list/update', id: list.id, patch: { name: r.text, emoji: r.emoji } });
}

function listMenuItems(list) {
  const lists = state.lists.filter((l) => !l.system);
  const i = lists.indexOf(list);
  return [
    !list.system && { label: 'Bölüm ekle', icon: 'section', action: () => A.addSection(state, list.id) },
    !list.system && { label: 'Adı ve simgeyi düzenle', icon: 'edit', action: () => editList(list) },
    { label: list.showCompleted ? 'Tamamlananları gizle' : 'Tamamlananları göster', icon: list.showCompleted ? 'eyeOff' : 'eye', action: () => A.run({ type: 'list/update', id: list.id, patch: { showCompleted: !list.showCompleted } }) },
    { label: 'Widget’ta göster', icon: 'widget', checked: state.settings.widget.listId === list.id, action: () => A.run({ type: 'settings/update', patch: { widget: { listId: list.id, visible: true } } }) },
    !list.system && i > 0 && { label: 'Yukarı taşı', icon: 'upload', action: () => A.run({ type: 'list/move', id: list.id, beforeId: lists[i - 1].id }) },
    !list.system && i < lists.length - 1 && { label: 'Aşağı taşı', icon: 'download', action: () => A.run({ type: 'list/move', id: list.id, beforeId: lists[i + 2]?.id ?? null }) },
    !list.system && 'sep',
    !list.system && {
      label: 'Listeyi sil',
      icon: 'trash',
      danger: true,
      action: async () => {
        const ok = await confirmModal({ title: 'Liste silinsin mi?', message: `“${list.name}” listesi silinecek. İçindeki görevler çöp kutusuna taşınır; oradan Gelen Kutusu’na geri yükleyebilirsin.`, okLabel: 'Listeyi sil', danger: true });
        if (ok) A.run({ type: 'list/delete', id: list.id });
      },
    },
  ];
}

els.sidebar.addEventListener('click', (e) => {
  if (e.target.closest('[data-side="add-list"]')) return addList();
  if (e.target.closest('[data-side="edit-stages"]')) return openStagesDialog(state);
  const item = e.target.closest('[data-view]');
  if (!item) return;
  go({ type: item.dataset.view, id: item.dataset.id ?? null });
});

els.sidebar.addEventListener('contextmenu', (e) => {
  const item = e.target.closest('[data-view]');
  if (!item) return;
  e.preventDefault();
  if (item.dataset.view === 'list') showMenu(e.clientX, e.clientY, listMenuItems(getList(state, item.dataset.id)));
  else if (item.dataset.view === 'tag') showMenu(e.clientX, e.clientY, tagMenuItems(item.dataset.id));
});

const TAG_COLORS = [
  ['Sarı', '#e8b931'],
  ['Kırmızı', '#e5484d'],
  ['Turuncu', '#f76b15'],
  ['Yeşil', '#30a46c'],
  ['Mavi', '#3e8ef7'],
  ['Mor', '#8e4ec6'],
  ['Pembe', '#d6409f'],
  ['Gri', '#8b8d98'],
];

function tagMenuItems(name) {
  const tag = state.tags.find((g) => g.name === name);
  return [
    {
      label: 'Yeniden adlandır',
      icon: 'edit',
      action: async () => {
        const next = await promptModal({ title: 'Etiketi yeniden adlandır', value: name });
        if (next && (await A.run({ type: 'tag/update', name, patch: { name: next } })) && view.type === 'tag') go({ type: 'tag', id: next.replace(/^#/, '') });
      },
    },
    {
      label: 'Renk',
      icon: 'tag',
      items: [...TAG_COLORS, ['Renksiz', null]].map(([label, c]) => ({
        label,
        fill: c,
        checked: (tag?.color ?? null) === c,
        action: () => A.run({ type: 'tag/update', name, patch: { color: c } }),
      })),
    },
    'sep',
    {
      label: 'Etiketi sil',
      icon: 'trash',
      danger: true,
      action: async () => {
        if (await confirmModal({ title: 'Etiket silinsin mi?', message: `#${name} etiketi tüm görevlerden kaldırılacak. Görevler silinmez.`, okLabel: 'Etiketi sil', danger: true })) A.run({ type: 'tag/delete', name });
      },
    },
  ];
}

els.header.addEventListener('click', async (e) => {
  const b = e.target.closest('[data-h]');
  if (!b) return;
  const list = view.type === 'list' ? getList(state, view.id) : null;
  switch (b.dataset.h) {
    case 'sidebar':
      return toggleSidebar();
    case 'new':
      return els.addInput.focus();
    case 'toggle-done':
      return A.run({ type: 'list/update', id: list.id, patch: { showCompleted: !list.showCompleted } });
    case 'list-menu':
      return menuAt(b, listMenuItems(list));
    case 'empty-trash':
      if (await confirmModal({ title: 'Çöp kutusu boşaltılsın mı?', message: 'Çöp kutusundaki tüm görevler kalıcı olarak silinecek.', okLabel: 'Boşalt', danger: true })) A.run({ type: 'trash/empty' });
  }
});

// Typing in the header search shows results in place of the list; clearing
// it goes back to where you were.
function setQuery(text) {
  query = text;
  if (query.trim() && view.type !== 'search') {
    searchReturn = view;
    view = { type: 'search' };
    render();
  } else if (!query.trim() && view.type === 'search' && searchReturn) {
    const back = searchReturn;
    searchReturn = null;
    go(back);
  } else renderBody();
}

// Search feedback: a spinner while typing, then "N sonuç" or "Sonuç yok";
// results follow a moment after the last keystroke.
const SEARCH_DELAY = 140;
let searchTimer = null;

function showSearchCount(matches) {
  const el = els.searchCount;
  el.classList.remove('busy', 'none');
  if (matches == null || !query.trim()) el.textContent = '';
  else if (matches) el.textContent = `${matches} sonuç`;
  else {
    el.textContent = 'Sonuç yok';
    el.classList.add('none');
  }
}

function flushSearch() {
  if (!searchTimer) return;
  clearTimeout(searchTimer);
  searchTimer = null;
  setQuery(els.search.value);
}

// A short ring around a box, so a click or a key that moved the focus there shows.
function flash(el, cls = 'pulse') {
  el.classList.remove(cls);
  void el.offsetWidth;
  el.classList.add(cls);
}

function focusSearch() {
  if (view.type === 'invoices') go(searchReturn ?? { type: 'list', id: (state.lists.find((l) => !l.system) ?? state.lists[0]).id });
  els.search.focus();
  els.search.select();
  flash(els.search.closest('.search-box'));
}

els.header.addEventListener('input', (e) => {
  if (e.target !== els.search) return;
  clearTimeout(searchTimer);
  if (!e.target.value.trim()) {
    searchTimer = null;
    return setQuery('');
  }
  els.searchCount.textContent = '';
  els.searchCount.classList.remove('none');
  els.searchCount.classList.add('busy');
  searchTimer = setTimeout(() => {
    searchTimer = null;
    setQuery(els.search.value);
  }, SEARCH_DELAY);
});

els.header.addEventListener('keydown', (e) => {
  if (e.target !== els.search) return;
  if (e.key === 'Escape' && query) {
    e.preventDefault();
    e.stopPropagation();
    els.search.value = '';
    setQuery('');
  } else if (e.key === 'ArrowDown' || e.key === 'Enter') {
    flushSearch();
    const first = tree.rows.find((r) => r.kind === 'task' && !r.context);
    if (!first) {
      if (query.trim()) flash(els.search.closest('.search-box'), 'nohit');
      return;
    }
    e.preventDefault();
    if (e.key === 'Enter') reveal(first.id);
    else {
      select(first.id);
      e.target.blur();
    }
  }
});

els.rail.addEventListener('click', (e) => {
  const b = e.target.closest('[data-rail]');
  if (!b) return;
  switch (b.dataset.rail) {
    case 'tasks':
      if (view.type === 'search' || view.type === 'invoices') go({ type: 'list', id: (state.lists.find((l) => !l.system) ?? state.lists[0]).id });
      return;
    case 'invoices':
      return go({ type: 'invoices' });
    case 'search':
      return focusSearch();
    case 'widget':
      return A.run({ type: 'settings/update', patch: { widget: { visible: !state.settings.widget.visible } } });
    case 'changelog':
      return showChangelog();
    case 'update':
      return openUpdateDialog();
    case 'settings':
      return openSettings();
  }
});

// ---------------------------------------------------------------- updates

const UPDATE_PENDING = ['available', 'downloading', 'downloaded'];
let update = { status: 'unsupported' };
let updateBox = null;
let announced = null;

function setUpdate(next) {
  update = next;
  renderRail();
  renderUpdatePanel();
  if (next.status === 'available' && announced !== next.version && !updateBox) {
    announced = next.version;
    toast(`Yeni sürüm var: v${next.version}`, { action: { label: 'Güncelle', run: openUpdateDialog }, timeout: 12000 });
  }
}

function updatePanelHtml() {
  const u = update;
  const button = (act, label, primary = true) => `<button type="button" class="btn${primary ? ' primary' : ''}" data-u="${act}">${esc(label)}</button>`;
  const checked = u.checkedAt ? ` · son denetim ${esc(formatDateTime(u.checkedAt))}` : '';
  let head;
  let body = '';
  let actions = '';
  switch (u.status) {
    case 'unsupported':
      head = 'Güncellemeler yalnızca Windows’a kurulu Xloer’de çalışır.';
      actions = button('page', 'Sürümler sayfasını aç', false);
      break;
    case 'checking':
      head = 'Güncellemeler denetleniyor…';
      break;
    case 'available':
      head = `Yeni sürüm var: <b>v${esc(u.version)}</b>`;
      if (u.notes) body = `<div class="update-notes">${esc(u.notes)}</div>`;
      if (u.portable) body += '<p class="hint">Taşınabilir sürüm kendini güncelleyemez: yeni exe’yi indirip eskisinin yerine koy. Verilerin korunur.</p>';
      actions = u.portable ? button('page', 'İndirme sayfasını aç') : button('download', 'Güncelle');
      break;
    case 'downloading':
      head = `v${esc(u.version)} indiriliyor… %${u.percent}`;
      body = `<div class="progress-bar" role="progressbar" aria-valuenow="${u.percent}" aria-valuemin="0" aria-valuemax="100"><span style="width:${u.percent}%"></span></div>`;
      break;
    case 'downloaded':
      head = `v${esc(u.version)} indirildi.`;
      body = '<p class="hint">Xloer kapanır, yeni sürüm kurulur ve kendiliğinden yeniden açılır. Görevlerin ve ayarların korunur.</p>';
      actions = button('install', 'Yeniden başlat ve kur');
      break;
    case 'error':
      head = u.version ? `v${esc(u.version)} indirilemedi.` : 'Güncellemeler denetlenemedi.';
      body = `<p class="update-error">${esc(u.error ?? '')}</p>`;
      actions = button(u.version ? 'download' : 'check', 'Tekrar dene');
      break;
    default:
      head = u.status === 'none' ? 'Xloer güncel.' : 'Henüz denetlenmedi.';
      actions = button('check', 'Şimdi denetle', false);
  }
  const auto =
    u.status === 'unsupported'
      ? ''
      : `<label class="check-row"><input type="checkbox" data-u="auto" ${state.settings.autoUpdate ? 'checked' : ''}><span>Otomatik denetle (açılışta ve 6 saatte bir)</span></label>`;
  return `<p class="update-head">${head}</p><p class="hint">Yüklü sürüm: v${esc(u.current ?? '')}${checked}</p>${body}${actions ? `<div class="btn-row update-actions">${actions}</div>` : ''}${auto}`;
}

function renderUpdatePanel() {
  const el = updateBox?.querySelector('.update-panel');
  if (el) el.innerHTML = updatePanelHtml();
}

function openUpdateDialog() {
  if (updateBox) return;
  modal({
    title: 'Güncellemeler',
    body: Object.assign(document.createElement('div'), { className: 'update-panel' }),
    onOpen(box) {
      updateBox = box;
      renderUpdatePanel();
      box.addEventListener('click', (e) => {
        const act = e.target.closest('button[data-u]')?.dataset.u;
        if (act === 'check') api.update.check();
        else if (act === 'download') api.update.download();
        else if (act === 'install') api.update.install();
        else if (act === 'page') api.update.openPage();
      });
      box.addEventListener('change', (e) => {
        if (e.target.dataset.u === 'auto') A.run({ type: 'settings/update', patch: { autoUpdate: e.target.checked } });
      });
    },
  }).then(() => {
    updateBox = null;
  });
  // Opening the dialog counts as "check now" unless that just happened.
  const fresh = update.checkedAt && Date.now() - update.checkedAt < 60_000;
  if (['idle', 'none', 'error'].includes(update.status) && !update.version && !fresh) api.update.check();
}

function onNavigate(target) {
  if (target?.type === 'update') openUpdateDialog();
  else if (target?.type === 'task') reveal(target.id);
}

// ---------------------------------------------------------------- settings

async function openSettings() {
  const info = await api.appInfo();
  const s = state.settings;
  const w = s.widget;
  const checked = (v) => (v ? 'checked' : '');
  const body = document.createElement('div');
  body.className = 'settings';
  body.innerHTML = `
    <section>
      <h3>Görünüm</h3>
      <div class="seg" role="radiogroup" aria-label="Tema">${[
        ['dark', 'Koyu'],
        ['light', 'Açık'],
        ['system', 'Sistem'],
      ]
        .map(([v, l]) => `<button type="button" role="radio" aria-checked="${s.theme === v}" data-theme-opt="${v}">${l}</button>`)
        .join('')}</div>
      <div class="palette-grid" role="radiogroup" aria-label="Renk teması">${PALETTES.map(
        (p) => `<button type="button" role="radio" aria-checked="${s.palette === p.id}" data-palette-opt="${p.id}">
          <span class="pal-sw" aria-hidden="true">${[p.dark.bg, p.dark.bar, p.dark.accent, p.light.bg].map((c) => `<i style="background:${c}"></i>`).join('')}</span>
          <b>${esc(p.name)}</b><span>${esc(p.note)}</span>
        </button>`,
      ).join('')}</div>
    </section>
    <section>
      <h3>Uygulama</h3>
      <label class="check-row"><input type="checkbox" data-set="closeToTray" ${checked(s.closeToTray)}><span>Pencere kapatılınca sistem tepsisinde çalışmaya devam et</span></label>
      <label class="check-row"><input type="checkbox" data-set="startWithWindows" ${checked(s.startWithWindows)}><span>Windows açılışında başlat (yalnızca widget açılır)</span></label>
    </section>
    <section>
      <h3>Masaüstü widget’ı</h3>
      <label class="check-row"><input type="checkbox" data-w="visible" ${checked(w.visible)}><span>Widget’ı göster</span></label>
      <label class="check-row"><input type="checkbox" data-w="locked" ${checked(w.locked)}><span>Masaüstündeki yerini ve boyutunu sabitle (widget’taki iğne)</span></label>
      <label class="check-row"><input type="checkbox" data-w="alwaysOnTop" ${checked(w.alwaysOnTop)}><span>Diğer pencerelerin üstünde dur</span></label>
      <label class="range-row"><span>Opaklık</span><input type="range" min="30" max="100" step="5" data-w="opacity" value="${Math.round(w.opacity * 100)}"><output>${Math.round(w.opacity * 100)}%</output></label>
    </section>
    <section>
      <h3>Güncellemeler</h3>
      <div class="btn-row"><button type="button" class="btn" data-data="update">${icon('update', 15)}Güncellemeleri denetle</button></div>
      <label class="check-row"><input type="checkbox" data-set="autoUpdate" ${checked(s.autoUpdate)}><span>Güncellemeleri otomatik denetle</span></label>
    </section>
    <section>
      <h3>Veri</h3>
      <div class="btn-row">
        <button type="button" class="btn" data-data="csv">${icon('download', 15)}CSV’den içe aktar</button>
        <button type="button" class="btn" data-data="export">${icon('upload', 15)}Yedek al (JSON)</button>
        <button type="button" class="btn" data-data="import">${icon('restore', 15)}Yedekten geri yükle</button>
        <button type="button" class="btn" data-data="folder">${icon('folder', 15)}Veri klasörünü aç</button>
      </div>
      <p class="hint">Başka bir görev uygulamasından taşımak için oradan aldığın .csv yedeğini seç. Listeler, bölümler, alt görevler, tarihler ve etiketler aktarılır; zaten olan görevler ikinci kez eklenmez.</p>
      <p class="hint mono" title="Veri dosyası">${esc(info.dataFile)}</p>
    </section>
    <p class="hint settings-foot">Xloer ${esc(info.version)} · <button type="button" class="link-btn" data-data="changelog">Yenilikler</button></p>`;
  modal({
    title: 'Ayarlar',
    body,
    className: 'wide',
    onOpen(box, close) {
      box.addEventListener('click', async (e) => {
        const theme = e.target.closest('[data-theme-opt]');
        if (theme) {
          box.querySelectorAll('[data-theme-opt]').forEach((b) => b.setAttribute('aria-checked', String(b === theme)));
          return A.run({ type: 'settings/update', patch: { theme: theme.dataset.themeOpt } });
        }
        const palette = e.target.closest('[data-palette-opt]');
        if (palette) {
          box.querySelectorAll('[data-palette-opt]').forEach((b) => b.setAttribute('aria-checked', String(b === palette)));
          return A.run({ type: 'settings/update', patch: { palette: palette.dataset.paletteOpt } });
        }
        const data = e.target.closest('[data-data]')?.dataset.data;
        if (data === 'changelog') return showChangelog();
        if (data === 'csv') {
          const r = await api.importCsv();
          if (r.canceled) return;
          if (!r.ok) return toast(r.error, { error: true });
          toast(`${r.result.created} görev aktarıldı${r.result.skipped ? `, ${r.result.skipped} görev zaten vardı` : ''}${r.result.newLists ? `, ${r.result.newLists} yeni liste` : ''}.`, { timeout: 7000 });
        } else if (data === 'export') {
          const r = await api.exportJson();
          if (r.ok) toast(`Yedek kaydedildi: ${r.path}`, { timeout: 7000 });
        } else if (data === 'import') {
          const ok = await confirmModal({ title: 'Yedekten geri yüklensin mi?', message: 'Şu anki bütün veriler seçeceğin yedekteki verilerle değiştirilecek. (Mevcut veriler önce yedekler klasörüne kopyalanır.)', okLabel: 'Dosya seç', danger: true });
          if (!ok) return;
          const r = await api.importJson();
          if (r.ok) toast('Yedek geri yüklendi.');
          else if (!r.canceled) toast(r.error, { error: true });
        } else if (data === 'folder') api.openDataDir();
        else if (data === 'update') {
          close(null);
          openUpdateDialog();
        }
      });
      box.addEventListener('change', (e) => {
        const el = e.target;
        if (el.dataset.set) A.run({ type: 'settings/update', patch: { [el.dataset.set]: el.checked } });
        else if (el.dataset.w && el.type === 'checkbox') A.run({ type: 'settings/update', patch: { widget: { [el.dataset.w]: el.checked } } });
        else if (el.dataset.w === 'opacity') A.run({ type: 'settings/update', patch: { widget: { opacity: Number(el.value) / 100 } } });
      });
      box.addEventListener('input', (e) => {
        if (e.target.dataset.w === 'opacity') e.target.nextElementSibling.textContent = `${e.target.value}%`;
      });
    },
  });
}

// ---------------------------------------------------------------- Excel drop

// Dropping an Excel file anywhere on the window opens Faturalar and imports it.
const dropHint = document.createElement('div');
dropHint.className = 'file-drop';
dropHint.innerHTML = `<div class="file-drop-box">${icon('download', 22)}<b>Excel dosyasını bırak</b><span>Projeler Faturalar’a eklenir; aynı proje kodu varsa güncellenir.</span></div>`;
document.body.append(dropHint);

const draggingFiles = (e) => [...(e.dataTransfer?.types ?? [])].includes('Files');
let fileDepth = 0;
const endFileDrag = () => {
  fileDepth = 0;
  document.body.classList.remove('file-over');
};
addEventListener('dragenter', (e) => {
  if (!draggingFiles(e)) return;
  fileDepth++;
  document.body.classList.add('file-over');
});
addEventListener('dragleave', (e) => {
  if (draggingFiles(e) && --fileDepth <= 0) endFileDrag();
});
addEventListener('dragover', (e) => {
  if (!draggingFiles(e)) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = 'copy';
});
addEventListener('drop', (e) => {
  if (!draggingFiles(e)) return;
  e.preventDefault();
  endFileDrag();
  const files = [...e.dataTransfer.files];
  const file = files.find((f) => /\.xls[xm]$/i.test(f.name)) ?? files[0];
  if (!file) return;
  if (view.type !== 'invoices') go({ type: 'invoices' });
  invoices.importFile(file);
});

// ---------------------------------------------------------------- sidebar and panel sizes

// The lines on the left edge of the list and of the task panel size the
// sidebar and the panel; double-click puts them back. Both widths are kept
// for the next start.
const SIDE_KEY = 'xloer-side-w';
const SIDE = { default: 240, min: 180, max: 420 };
const DETAIL_KEY = 'xloer-detail-w';
const DETAIL = { default: 420, min: 320, max: 960 };
const LIST_MIN = 280;
const sideLine = $('#side-resizer');
const detailLine = $('#detail-resizer');
const listPane = $('.pane-list');
const width = (el) => el.getBoundingClientRect().width;
const outward = () => els.layout.classList.contains('outward');
let sideW = SIDE.default;
let detailW = DETAIL.default;
// The list's width while the panel opens outward (see below).
let listW = 0;

function remember(key, value) {
  try {
    localStorage.setItem(key, String(value));
  } catch {}
}

function setListWidth(w) {
  listW = Math.max(0, Math.round(w));
  els.layout.style.setProperty('--list-w', `${listW}px`);
}

function setSideWidth(w, save = false) {
  const next = Math.round(Math.min(Math.max(w, SIDE.min), SIDE.max));
  // With the panel open outward the list gives or takes the difference, so
  // the panel keeps its width.
  if (outward() && !els.layout.classList.contains('no-sidebar')) setListWidth(width(listPane) - (next - sideW));
  sideW = next;
  els.layout.style.setProperty('--side-user', `${sideW}px`);
  if (save) remember(SIDE_KEY, sideW);
}

function toggleSidebar() {
  const hide = !els.layout.classList.contains('no-sidebar');
  if (outward()) setListWidth(width(listPane) + (hide ? width(els.sidebar) : -sideW));
  els.layout.classList.toggle('no-sidebar', hide);
}

function setDetailWidth(w, save = false) {
  detailW = Math.round(Math.min(Math.max(w, DETAIL.min), DETAIL.max));
  els.layout.style.setProperty('--detail-w', `${detailW}px`);
  if (save) remember(DETAIL_KEY, detailW);
}

try {
  const side = Number(localStorage.getItem(SIDE_KEY));
  if (side) setSideWidth(side);
  const detail = Number(localStorage.getItem(DETAIL_KEY));
  if (detail) setDetailWidth(detail);
} catch {}

function dragLine(line, e, move, done) {
  if (e.button !== 0) return;
  e.preventDefault();
  line.setPointerCapture(e.pointerId);
  line.classList.add('active');
  els.layout.classList.add('resizing');
  const onMove = (ev) => move(ev.clientX);
  // The capture ends on release, and also when the release is lost
  // (another window took the mouse), so a drag never stays stuck.
  const onEnd = () => {
    line.removeEventListener('pointermove', onMove);
    line.removeEventListener('lostpointercapture', onEnd);
    line.classList.remove('active');
    els.layout.classList.remove('resizing');
    done();
  };
  line.addEventListener('pointermove', onMove);
  line.addEventListener('lostpointercapture', onEnd);
}

sideLine.addEventListener('pointerdown', (e) => {
  const left = els.sidebar.getBoundingClientRect().left;
  dragLine(sideLine, e, (x) => setSideWidth(x - left), () => remember(SIDE_KEY, sideW));
});
sideLine.addEventListener('dblclick', () => setSideWidth(SIDE.default, true));

detailLine.addEventListener('pointerdown', (e) => {
  const box = els.layout.getBoundingClientRect();
  const left = listPane.getBoundingClientRect().left;
  dragLine(
    detailLine,
    e,
    (x) => {
      // Open outward, the window keeps its size: the line moves between list and panel.
      if (outward()) setListWidth(Math.min(Math.max(x - left, LIST_MIN, box.right - DETAIL.max - left), box.right - DETAIL.min - left));
      else setDetailWidth(Math.min(box.right - x, box.right - left - LIST_MIN));
    },
    () => setDetailWidth(outward() ? width(els.detail) : detailW, true),
  );
});
detailLine.addEventListener('dblclick', () => {
  if (outward()) setListWidth(width(listPane) + width(els.detail) - DETAIL.default);
  setDetailWidth(DETAIL.default, true);
});

// The task panel opens outward: the window grows to the right by the
// panel's width and shrinks back when it closes (main.js moves the window),
// so the list keeps its place and width. Meanwhile the list is a fixed
// column (--list-w) and the panel takes the rest of the window; dragging the
// window's right edge then sizes the panel. Room the screen does not have
// comes from the list. A maximised or full-screen window, a narrow one (the
// panel slides over the list) and the browser preview keep their size and
// open the panel inside, as before.
const narrow = matchMedia('(max-width: 980px)');
let win = { maximized: false, fullScreen: false };
const fixedWindow = () => !api.win || win.maximized || win.fullScreen || narrow.matches;
let panelWant = false;
let panelBusy = false;

function setPanelOpen(open) {
  panelWant = open;
  if (!panelBusy) runPanel();
}

// One change at a time, so each one measures a settled window.
async function runPanel() {
  panelBusy = true;
  while (panelWant !== els.layout.classList.contains('has-selection')) await movePanel(panelWant);
  panelBusy = false;
}

async function movePanel(open) {
  const L = els.layout.classList;
  if (fixedWindow()) {
    // Inside the window the panel leaves the list at least LIST_MIN.
    if (open) els.layout.style.setProperty('--detail-w', `${Math.max(DETAIL.min, Math.min(detailW, width(listPane) - LIST_MIN))}px`);
    L.remove('outward', 'growing');
    L.toggle('has-selection', open);
    return;
  }
  els.layout.style.setProperty('--detail-w', `${detailW}px`);
  setListWidth(width(listPane));
  L.add('outward', 'growing');
  L.toggle('has-selection', open);
  const room = open ? detailW : width(els.detail);
  const got = await (open ? api.win.grow(room) : api.win.shrink(room));
  // What the window did not give or take, the list does (keeping LIST_MIN when opening).
  setListWidth(open ? Math.max(Math.min(LIST_MIN, listW), listW + got - room) : listW + room - got);
  await new Promise((r) => setTimeout(r, 260));
  L.remove('growing');
  if (!open && !L.contains('has-selection')) L.remove('outward');
}

if (api.win) {
  api.win.state().then((s) => {
    if (s) win = s;
  });
  api.win.onState((s) => {
    win = s;
    if (fixedWindow()) {
      // Maximised with the panel open: the panel keeps its width, the list takes the rest.
      els.layout.classList.remove('outward', 'growing');
    } else if (!els.layout.classList.contains('has-selection')) {
      // Back from maximised after the panel closed there: give its room back.
      api.win.shrink(Infinity);
    }
  });
}

// Dragging the window's right edge sizes an open panel; keep that width.
let resizeTimer = null;
addEventListener('resize', () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    const L = els.layout.classList;
    if (outward() && L.contains('has-selection') && !L.contains('growing') && !panelBusy) setDetailWidth(width(els.detail), true);
  }, 300);
});

// ---------------------------------------------------------------- keyboard

const typing = (el) => el?.matches?.('input, textarea, select, [contenteditable="true"]');

addEventListener('keydown', (e) => {
  const mod = e.ctrlKey || e.metaKey;
  if (view.type === 'invoices') {
    if (mod && (e.key === 'f' || e.key === 'n')) {
      e.preventDefault();
      if (e.key === 'f') invoices.focusFilter();
      else invoices.newProject();
    }
    return;
  }
  if (mod && (e.key === 'f' || e.key === 'k')) {
    e.preventDefault();
    return focusSearch();
  }
  if (mod && e.key === 'n') {
    e.preventDefault();
    if (els.addRow.hidden) go({ type: 'list', id: INBOX_ID });
    els.addInput.focus();
    return;
  }
  if (typing(e.target) || document.querySelector('.modal-back, .menu')) return;
  if (e.key === 'Escape' && selectedId) return select(null);
  if (A.handleTreeKey(e, { state, tree, selectedId, select, canReorder: view.type === 'list' })) e.preventDefault();
});

// ---------------------------------------------------------------- start

appVersion = (await api.appInfo()).version;
const first = await api.getState();
setState(first.state);
api.onState((payload) => setState(payload.state));
setUpdate(await api.update.get());
api.update.onState(setUpdate);
api.onNavigate(onNavigate);
onNavigate(await api.takePendingNav());
