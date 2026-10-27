// Task operations shared by the main window and the widget: dispatching with
// error toasts, context menus, inline creation and keyboard moves.

import { api } from './api.js';
import { PRIORITY_COLORS, PRIORITY_LABELS, confirmModal, dueLabel, promptModal, showMenu, toast } from './ui.js';
import { openCalendar } from './calendar.js';
import { INBOX_ID, MAX_DEPTH, addDays, buildIndex, depthOf, getList, todayStr } from '../../shared/store.js';
import { invoiceCopy, rowDays } from '../../shared/invoices.js';

export async function run(action) {
  const r = await api.dispatch(action);
  if (!r.ok) {
    toast(r.error, { error: true });
    return null;
  }
  return r.result ?? true;
}

const short = (s) => (s.length > 40 ? `${s.slice(0, 38)}…` : s || 'Başlıksız');

// "Rapor yaz #RAPOR #acil" -> { title: 'Rapor yaz', tags: ['RAPOR', 'acil'] }
export function parseQuickAdd(text) {
  const tags = [];
  const title = text
    .replace(/(^|\s)#([\p{L}\p{N}_-]+)/gu, (_m, lead, tag) => {
      tags.push(tag);
      return lead;
    })
    .replace(/\s+/g, ' ')
    .trim();
  return { title: title || text.trim(), tags };
}

function siblingsOf(index, t) {
  return t.parentId ? index.children(t.parentId) : index.roots(t.listId, t.sectionId);
}

// Ticking the last open step of a report completes the report too (see
// task/toggle); "Geri al" reopens both.
export async function toggle(state, task) {
  const done = !task.done;
  const result = await run({ type: 'task/toggle', id: task.id, done });
  if (result && done) {
    const top = state.tasks.find((t) => t.id === result.parents?.at(-1));
    const message = top ? `Tüm adımlar bitti: “${short(top.title)}” tamamlandı` : `“${short(task.title)}” tamamlandı`;
    toast(message, { action: { label: 'Geri al', run: () => run({ type: 'task/toggle', id: task.id, done: false }) } });
  }
}

// Clicking the stage a report is already at clears it.
export function setStage(task, stage) {
  return run({ type: 'task/update', id: task.id, patch: { stage: task.stage === stage ? null : stage } });
}

export async function copyInvoiceTable(invoice) {
  try {
    await api.copyRich(invoiceCopy(invoice));
    const days = invoice.rows.reduce((n, r) => n + rowDays(r), 0);
    toast(`Tablo kopyalandı: ${invoice.code || 'proje'} · ${invoice.rows.length} test · ${days} gün. E-postaya veya Word’e yapıştırabilirsin.`);
  } catch (err) {
    toast(`Kopyalanamadı: ${err.message}`, { error: true });
  }
}

export async function remove(task) {
  const ok = await run({ type: 'task/delete', id: task.id });
  if (ok) toast(`“${short(task.title)}” çöp kutusuna taşındı`, { action: { label: 'Geri al', run: () => run({ type: 'task/restore', id: task.id }) } });
}

export async function addInSection(tree, list, sectionId) {
  const sec = list.sections.find((x) => x.id === sectionId);
  if (sec?.collapsed) await run({ type: 'section/update', listId: list.id, id: sec.id, patch: { collapsed: false } });
  const id = await run({ type: 'task/create', listId: list.id, sectionId, title: '', atStart: true });
  if (id) tree.editWhenReady(id, { isNew: true });
}

export async function addSubtask(ctx, task) {
  if (ctx.flat) {
    const title = await promptModal({ title: 'Alt görev ekle', label: `“${short(task.title)}” altına`, placeholder: 'Alt görev adı', okLabel: 'Ekle' });
    if (title) run({ type: 'task/create', parentId: task.id, title });
    return;
  }
  const id = await run({ type: 'task/create', parentId: task.id, title: '' });
  if (id) ctx.tree.editWhenReady(id, { isNew: true });
}

// Inline edit finished. Enter on a new task opens the next one below it,
// so several reports can be typed in a row.
export async function rename(state, tree, row, text, { isNew, viaEnter }) {
  if (row.kind === 'section') return run({ type: 'section/update', listId: row.listId, id: row.id, patch: { name: text } });
  const { title, tags } = isNew ? parseQuickAdd(text) : { title: text, tags: [] };
  await run({ type: 'task/update', id: row.id, patch: tags.length ? { title, tags: [...row.task.tags, ...tags] } : { title } });
  if (isNew && viaEnter) {
    const t = row.task;
    const sibs = siblingsOf(buildIndex(state), t);
    const next = sibs[sibs.findIndex((x) => x.id === t.id) + 1];
    const id = await run({ type: 'task/create', listId: t.listId, sectionId: t.sectionId, parentId: t.parentId, beforeId: next?.id ?? null, title: '' });
    if (id) tree.editWhenReady(id, { isNew: true });
  }
}

export function cancelNew(row) {
  if (row.kind === 'task') run({ type: 'task/purge', id: row.id });
}

export async function move(state, action, { row }) {
  const ok = await run(action);
  if (ok && action.type === 'task/move' && action.listId !== row.listId) {
    const list = getList(state, action.listId);
    toast(`“${short(row.task.title)}” → ${list ? `${list.emoji} ${list.name}` : 'liste'}`, {
      action: { label: 'Geri al', run: () => run({ type: 'task/move', id: row.id, listId: row.listId, sectionId: row.task.sectionId, parentId: row.task.parentId }) },
    });
  }
}

export function moveTargets(state, task) {
  const lists = [...state.lists.filter((l) => l.id === INBOX_ID), ...state.lists.filter((l) => l.id !== INBOX_ID)];
  const to = (listId, sectionId) => () => run({ type: 'task/move', id: task.id, listId, sectionId, parentId: null });
  return lists.map((l) => {
    const here = task.listId === l.id && !task.parentId;
    if (!l.sections.length) return { label: l.name, emoji: l.emoji, checked: here, action: to(l.id, null) };
    return {
      label: l.name,
      emoji: l.emoji,
      items: [
        { label: 'Bölümsüz', checked: here && !task.sectionId, action: to(l.id, null) },
        ...l.sections.map((s) => ({ label: s.name, icon: 'section', checked: here && task.sectionId === s.id, action: to(l.id, s.id) })),
      ],
    };
  });
}

export function priorityItems(task) {
  return [3, 2, 1, 0].map((p) => ({
    label: PRIORITY_LABELS[p],
    icon: 'flag',
    color: PRIORITY_COLORS[p],
    checked: task.priority === p,
    action: () => run({ type: 'task/update', id: task.id, patch: { priority: p } }),
  }));
}

// The date and time picker for a task (calendar.js in time mode), under
// `anchor`. Past moments cannot be picked; Temizle removes both.
export function pickDue(task, anchor) {
  openCalendar(anchor, {
    value: task.due,
    min: todayStr(),
    time: task.dueTime ?? null,
    clearable: !!task.due,
    onPick: (due, dueTime) => run({ type: 'task/update', id: task.id, patch: { due, dueTime } }),
  });
}

// The quick days set a date without a time; "Tarih ve saat…" picks both.
// anchor: the task's row, for the picker to open under.
export function dateItems(task, anchor) {
  const today = todayStr();
  const set = (due) => () => run({ type: 'task/update', id: task.id, patch: { due, dueTime: null } });
  const current = dueLabel(task.due, false, task.dueTime);
  return [
    { label: 'Tarih ve saat…', icon: 'calendar', hint: current?.text ?? '', disabled: !anchor, action: () => pickDue(task, anchor) },
    'sep',
    { label: 'Bugün', icon: 'sun', checked: task.due === today && !task.dueTime, action: set(today) },
    { label: 'Yarın', icon: 'calendar', checked: task.due === addDays(today, 1) && !task.dueTime, action: set(addDays(today, 1)) },
    { label: 'Gelecek hafta', icon: 'calendar', checked: task.due === addDays(today, 7) && !task.dueTime, action: set(addDays(today, 7)) },
    { label: 'Tarihi kaldır', icon: 'x', disabled: !task.due, action: set(null) },
  ];
}

// ctx: { state, tree, flat?, onOpen?(id), onReveal?(id) }
export function showTaskMenu(ctx, row, x, y) {
  const t = row.task;
  if (row.trash) {
    return showMenu(x, y, [
      { label: 'Geri yükle', icon: 'restore', action: () => run({ type: 'task/restore', id: t.id }) },
      { label: 'Kalıcı olarak sil', icon: 'trash', danger: true, action: () => purge(t) },
    ]);
  }
  const index = buildIndex(ctx.state);
  const deep = depthOf(index, t) >= MAX_DEPTH;
  showMenu(x, y, [
    { label: 'Alt görev ekle', icon: 'subtasks', disabled: deep, hint: deep ? 'en fazla 5 seviye' : '', action: () => addSubtask(ctx, t) },
    { label: 'Yeniden adlandır', icon: 'edit', hint: 'F2', action: () => ctx.tree.startEdit(t.id) },
    ctx.onReveal && { label: 'Listede göster', icon: 'list', action: () => ctx.onReveal(t.id) },
    ctx.onOpen && { label: 'Xloer’de aç', icon: 'external', action: () => ctx.onOpen(t.id) },
    'sep',
    { label: 'Tarih', icon: 'calendar', items: dateItems(t, ctx.tree.el.querySelector(`.row[data-id="${CSS.escape(t.id)}"]`)) },
    { label: 'Öncelik', icon: 'flag', items: priorityItems(t) },
    { label: 'Taşı', icon: 'folder', items: moveTargets(ctx.state, t) },
    'sep',
    { label: t.done ? 'Tamamlanmadı olarak işaretle' : 'Tamamlandı olarak işaretle', icon: 'completed', action: () => toggle(ctx.state, t) },
    { label: 'Sil', icon: 'trash', danger: true, hint: 'Del', action: () => remove(t) },
  ]);
}

export function showSectionMenu(ctx, row, x, y) {
  const list = getList(ctx.state, row.listId);
  showMenu(x, y, [
    { label: 'Bu bölüme görev ekle', icon: 'plus', action: () => addInSection(ctx.tree, list, row.id) },
    { label: 'Yeniden adlandır', icon: 'edit', action: () => ctx.tree.startEdit(row.id) },
    { label: 'Altına yeni bölüm ekle', icon: 'section', action: () => addSection(ctx.state, list.id, row.id) },
    'sep',
    {
      label: 'Bölümü sil',
      icon: 'trash',
      danger: true,
      action: async () => {
        const ok = await confirmModal({
          title: 'Bölüm silinsin mi?',
          message: `“${row.section.name}” bölümü silinecek. İçindeki görevler silinmez, listenin en üstüne taşınır.`,
          okLabel: 'Bölümü sil',
          danger: true,
        });
        if (ok) run({ type: 'section/delete', listId: list.id, id: row.id });
      },
    },
  ]);
}

export async function addSection(state, listId, afterSectionId = null) {
  const list = getList(state, listId);
  const name = await promptModal({ title: 'Yeni bölüm', label: `${list.emoji} ${list.name}`, placeholder: 'Bölüm adı (ör. RAPORLAR)', okLabel: 'Ekle' });
  if (!name) return;
  const i = afterSectionId ? list.sections.findIndex((s) => s.id === afterSectionId) : -1;
  const beforeId = i >= 0 ? list.sections[i + 1]?.id ?? null : null;
  return run({ type: 'section/create', listId, name, beforeId });
}

export async function purge(task) {
  const ok = await confirmModal({ title: 'Kalıcı olarak silinsin mi?', message: `“${short(task.title)}” ve alt görevleri geri getirilemeyecek şekilde silinecek.`, okLabel: 'Kalıcı olarak sil', danger: true });
  if (ok) run({ type: 'task/purge', id: task.id });
}

// Keyboard moves inside a list: Alt+↑/↓ reorder, Tab nests under the task
// above, Shift+Tab moves out one level.
export function keyMove(state, task, kind) {
  const index = buildIndex(state);
  const list = getList(state, task.listId);
  const visible = (x) => x === task || list?.showCompleted || !x.done;
  const sibs = siblingsOf(index, task).filter(visible);
  const i = sibs.indexOf(task);
  const base = { type: 'task/move', id: task.id, listId: task.listId, sectionId: task.sectionId, parentId: task.parentId };
  if (kind === 'up' && i > 0) return run({ ...base, beforeId: sibs[i - 1].id });
  if (kind === 'down' && i < sibs.length - 1) return run({ ...base, beforeId: sibs[i + 2]?.id ?? null });
  if (kind === 'indent' && i > 0) return run({ ...base, parentId: sibs[i - 1].id, beforeId: null });
  if (kind === 'outdent' && task.parentId) {
    const parent = index.byId.get(task.parentId);
    const outer = siblingsOf(index, parent);
    const next = outer[outer.indexOf(parent) + 1];
    return run({ ...base, sectionId: parent.sectionId, parentId: parent.parentId, beforeId: next?.id ?? null });
  }
  return null;
}

// Shared row keys. Returns true when the key was used.
export function handleTreeKey(e, { state, tree, selectedId, select, canReorder }) {
  // A focused button already reacts to Space/Enter itself.
  if ((e.key === ' ' || e.key === 'Enter') && e.target.closest?.('button, a')) return false;
  const ids = tree.taskIds();
  const task = selectedId && state.tasks.find((t) => t.id === selectedId && !t.deletedAt);
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    if (e.altKey && task && canReorder) keyMove(state, task, e.key === 'ArrowUp' ? 'up' : 'down');
    else {
      const i = ids.indexOf(selectedId);
      const next = ids[i < 0 ? 0 : Math.max(0, Math.min(ids.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))];
      if (next) {
        select(next);
        tree.scrollTo(next);
      }
    }
    return true;
  }
  if (!task) return false;
  if (e.key === 'Tab' && canReorder) {
    keyMove(state, task, e.shiftKey ? 'outdent' : 'indent');
    return true;
  }
  if (e.key === 'F2' || e.key === 'Enter') return tree.startEdit(task.id) || true;
  if (e.key === ' ') {
    toggle(state, task);
    return true;
  }
  if (e.key === 'Delete') {
    // Select the next row that is not one of the deleted task's subtasks.
    const rows = tree.rows;
    const i = rows.findIndex((r) => r.kind === 'task' && r.id === task.id);
    let j = i + 1;
    while (i >= 0 && j < rows.length && rows[j].kind === 'task' && rows[j].depth > rows[i].depth) j++;
    const next = rows.slice(j).find((r) => r.kind === 'task') ?? rows.slice(0, Math.max(i, 0)).reverse().find((r) => r.kind === 'task');
    remove(task);
    select(next?.id ?? null);
    return true;
  }
  return false;
}
