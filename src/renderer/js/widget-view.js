// Desktop widget: one list as a tree (drag & drop, quick add, check off) with
// a search box over this list or every list.

import { api } from './api.js';
import { icon } from './icons.js';
import { TaskTree } from './tree.js';
import * as A from './actions.js';
import { applyTheme, esc, menuAt, promptModal } from './ui.js';
import { buildIndex, getList, subtreeHeight } from '../../shared/store.js';
import { buildListRows, buildSearchRows } from '../../shared/tree-rows.js';

const $ = (s) => document.querySelector(s);
const els = {
  list: $('#w-list'),
  pin: $('#w-pin'),
  open: $('#w-open'),
  more: $('#w-more'),
  update: $('#w-update'),
  q: $('#w-q'),
  scope: $('#w-scope'),
  clear: $('#w-clear'),
  count: $('#w-count'),
  addRow: $('#w-add-row'),
  add: $('#w-add'),
  body: $('#w-body'),
  tree: $('#w-tree'),
  empty: $('#w-empty'),
};

let state = null;
let index = null;
let query = '';
let selectedId = null;

document.body.classList.add(`os-${api.platform}`);
els.pin.innerHTML = icon('pin', 15);
els.open.innerHTML = icon('external', 15);
els.more.innerHTML = icon('more', 16);
els.clear.innerHTML = icon('x', 13);
els.update.innerHTML = `${icon('update', 14)}<span>Güncelle</span>`;
$('#w-search-icon').innerHTML = icon('search', 14);
$('#w-add-icon').innerHTML = icon('plus', 15);

const tree = new TaskTree(els.tree, {
  scroller: els.body,
  openButton: true,
  subtreeHeight: (id) => subtreeHeight(index, id),
  onSelect: (row) => (selectedId = row.id),
  onAction: (act, row, e) => rowAction(act, row, e),
  onContextMenu: (row, x, y) => {
    const ctx = { state, tree, flat: !!query.trim(), onOpen: (id) => api.openTask(id) };
    if (row.kind === 'section') A.showSectionMenu(ctx, row, x, y);
    else A.showTaskMenu(ctx, row, x, y);
  },
  onRename: (row, text, info) => A.rename(state, tree, row, text, info),
  onRenameCancel: (row) => A.cancelNew(row),
  onMove: (action, info) => A.move(state, action, info),
});

const widget = () => state.settings.widget;
const setWidget = (patch) => A.run({ type: 'settings/update', patch: { widget: patch } });

function currentList() {
  return getList(state, widget().listId) ?? state.lists.find((l) => !l.system) ?? state.lists[0];
}

function setState(next) {
  state = next;
  index = buildIndex(state);
  applyTheme(state.settings.theme);
  render();
}

function render() {
  const list = currentList();
  const w = widget();
  els.list.innerHTML = `<span class="emoji">${esc(list.emoji)}</span><span class="name">${esc(list.name)}</span>${icon('chevron', 14)}`;
  els.pin.classList.toggle('on', w.alwaysOnTop);
  els.pin.title = w.alwaysOnTop ? 'Her zaman üstte: açık' : 'Her zaman üstte: kapalı';
  els.scope.textContent = w.scope === 'all' ? 'Tüm listeler' : 'Bu liste';
  els.scope.setAttribute('aria-pressed', String(w.scope === 'all'));

  const searching = query.trim().length > 0;
  els.addRow.hidden = searching;
  els.clear.hidden = !searching;
  els.add.placeholder = `“${list.name}” listesine görev ekle`;
  let rows;
  if (searching) {
    const r = buildSearchRows(state, query, { listId: w.scope === 'all' ? null : list.id });
    rows = r.rows;
    els.count.textContent = r.matches ? `${r.matches}` : '0';
    tree.setRows(rows, { search: true, terms: r.terms, selectedId });
    els.empty.textContent = w.scope === 'all' ? 'Hiçbir listede sonuç yok.' : 'Bu listede sonuç yok. “Tüm listeler”i dene.';
  } else {
    rows = buildListRows(state, list.id, { index });
    els.count.textContent = '';
    tree.setRows(rows, { dnd: 'tree', selectedId, reports: true });
    els.empty.textContent = 'Bu listede görev yok.';
  }
  els.empty.hidden = rows.length > 0;
}

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
      return A.setStage(row.task, Number(e.target.closest('[data-stage]').dataset.stage));
    case 'open':
      return api.openTask(row.id);
    case 'add-in-section':
      return A.addInSection(tree, list, row.id);
    case 'section-menu': {
      const r = e.target.closest('button').getBoundingClientRect();
      return A.showSectionMenu({ state, tree }, row, r.left, r.bottom + 4);
    }
  }
}

// ---------------------------------------------------------------- header

els.list.addEventListener('click', () => {
  const current = currentList();
  const lists = [...state.lists.filter((l) => l.system), ...state.lists.filter((l) => !l.system)];
  menuAt(els.list, [
    ...lists.map((l) => ({ label: l.name, emoji: l.emoji, checked: l.id === current.id, action: () => setWidget({ listId: l.id }) })),
    'sep',
    {
      label: 'Yeni liste…',
      icon: 'plus',
      action: async () => {
        const r = await promptModal({ title: 'Yeni liste', placeholder: 'Liste adı', okLabel: 'Oluştur', emoji: '📁' });
        if (!r) return;
        const id = await A.run({ type: 'list/create', name: r.text, emoji: r.emoji });
        if (id) setWidget({ listId: id });
      },
    },
  ]);
});

els.pin.addEventListener('click', () => setWidget({ alwaysOnTop: !widget().alwaysOnTop }));
els.update.addEventListener('click', () => api.update.openPanel());

function showUpdate(u) {
  const pending = ['available', 'downloading', 'downloaded'].includes(u.status);
  els.update.hidden = !pending;
  els.update.title = pending ? `Yeni sürüm: v${u.version}` : '';
}
els.open.addEventListener('click', () => api.showMain());

els.more.addEventListener('click', () => {
  const w = widget();
  const list = currentList();
  menuAt(els.more, [
    { label: 'Her zaman üstte', icon: 'pin', checked: w.alwaysOnTop, action: () => setWidget({ alwaysOnTop: !w.alwaysOnTop }) },
    { label: 'Konumu kilitle', icon: w.locked ? 'lock' : 'unlock', checked: w.locked, action: () => setWidget({ locked: !w.locked }) },
    {
      label: 'Opaklık',
      icon: 'opacity',
      items: [1, 0.9, 0.8, 0.7, 0.6, 0.5].map((o) => ({ label: `%${Math.round(o * 100)}`, checked: Math.abs(w.opacity - o) < 0.01, action: () => setWidget({ opacity: o }) })),
    },
    'sep',
    { label: list.showCompleted ? 'Tamamlananları gizle' : 'Tamamlananları göster', icon: list.showCompleted ? 'eyeOff' : 'eye', action: () => A.run({ type: 'list/update', id: list.id, patch: { showCompleted: !list.showCompleted } }) },
    !list.system && { label: 'Bölüm ekle', icon: 'section', action: () => A.addSection(state, list.id) },
    'sep',
    { label: 'Xloer’i aç', icon: 'external', action: () => api.showMain() },
    { label: 'Widget’ı gizle', icon: 'hide', hint: 'tepsi menüsünden geri açılır', action: () => setWidget({ visible: false }) },
  ]);
});

// ---------------------------------------------------------------- search

els.q.addEventListener('input', () => {
  query = els.q.value;
  els.body.scrollTop = 0;
  render();
});

els.q.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    e.preventDefault();
    e.stopPropagation();
    if (query) clearSearch();
    else els.q.blur();
  } else if (e.key === 'Enter' || e.key === 'ArrowDown') {
    const first = tree.rows.find((r) => r.kind === 'task' && !r.context);
    if (!first) return;
    e.preventDefault();
    if (e.key === 'Enter') api.openTask(first.id);
    else {
      selectedId = first.id;
      tree.select(first.id);
      els.q.blur();
    }
  }
});

function clearSearch() {
  query = els.q.value = '';
  render();
}

els.clear.addEventListener('click', () => {
  clearSearch();
  els.q.focus();
});

els.scope.addEventListener('click', () => setWidget({ scope: widget().scope === 'all' ? 'list' : 'all' }));

// ---------------------------------------------------------------- add

els.add.addEventListener('keydown', async (e) => {
  if (e.key === 'Escape') return els.add.blur();
  if (e.key !== 'Enter' || !els.add.value.trim()) return;
  const { title, tags } = A.parseQuickAdd(els.add.value);
  els.add.value = '';
  const id = await A.run({ type: 'task/create', listId: currentList().id, title, tags, atStart: true });
  if (id) setTimeout(() => tree.scrollTo(id, { flash: true }), 60);
});

// ---------------------------------------------------------------- keyboard

addEventListener('keydown', (e) => {
  if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'k')) {
    e.preventDefault();
    els.q.focus();
    els.q.select();
    return;
  }
  if (e.target.matches?.('input, textarea') || document.querySelector('.modal-back, .menu')) return;
  if (e.key === 'Escape' && query) return clearSearch();
  if (A.handleTreeKey(e, { state, tree, selectedId, select: (id) => ((selectedId = id), tree.select(id)), canReorder: !query.trim() })) e.preventDefault();
});

// ---------------------------------------------------------------- start

const first = await api.getState();
setState(first.state);
api.onState((payload) => setState(payload.state));
showUpdate(await api.update.get());
api.update.onState(showUpdate);
