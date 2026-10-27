// Turns the state into the flat row lists the task trees render, and works out
// where a dragged row would land. Pure functions: they are unit-tested in node.

import { MAX_DEPTH, INBOX_ID, addDays, ancestors, buildIndex, getList, todayStr } from './store.js';
import { matchTask, parseQuery, snippet } from './search.js';

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

function taskRow(t, depth, index, extra = {}) {
  const kids = index.children(t.id);
  return {
    kind: 'task',
    key: `t:${t.id}`,
    id: t.id,
    listId: t.listId,
    depth,
    task: t,
    childCount: 0,
    collapsed: false,
    progress: { done: kids.filter((c) => c.done).length, total: kids.length },
    // The first open subtask: what has to happen next for this report.
    nextStep: kids.find((c) => !c.done)?.title ?? '',
    ...extra,
  };
}

function sectionRow(list, sec, count) {
  return { kind: 'section', key: `s:${sec.id}`, id: sec.id, listId: list.id, section: sec, collapsed: sec.collapsed, count };
}

// The tree of one list: unsectioned tasks first, then every section with its
// tasks. A completed step (a done task without subtasks) stays in place,
// struck through, under its open parent. A completed task with subtasks of
// its own (a finished report, at any level) leaves the tree, and so does
// every completed top-level task; when the list shows completed tasks they
// come last, under "Tamamlanan", newest first (rows marked doneGroup, which
// are not dragged or dropped on).
export function buildListRows(state, listId, opts = {}) {
  const list = getList(state, listId);
  if (!list) return [];
  const index = opts.index ?? buildIndex(state);
  const showDone = opts.showCompleted ?? list.showCompleted;
  const finished = [];
  const stays = (t) => !t.done || !index.children(t.id).length;
  const rows = [];
  const push = (t, depth, extra = {}) => {
    const all = index.children(t.id);
    const kids = extra.doneGroup ? all : all.filter(stays);
    if (!extra.doneGroup) finished.push(...all.filter((c) => !stays(c)));
    rows.push(taskRow(t, depth, index, { childCount: kids.length, collapsed: t.collapsed && kids.length > 0, ...extra }));
    if (!t.collapsed) for (const c of kids) push(c, depth + 1, extra);
  };
  const open = (t) => (t.done ? finished.push(t) : push(t, 0));
  for (const t of index.roots(listId, null)) open(t);
  for (const sec of list.sections) {
    const roots = index.roots(listId, sec.id);
    rows.push(sectionRow(list, sec, roots.filter((t) => !t.done).length));
    for (const t of roots) {
      if (!sec.collapsed) open(t);
      else if (t.done) finished.push(t);
    }
  }
  if (showDone && finished.length) {
    finished.sort((a, b) => (b.doneAt ?? 0) - (a.doneAt ?? 0));
    rows.push({ kind: 'group', key: 'g:done', id: 'done', label: 'Tamamlanan', count: finished.length });
    for (const t of finished) push(t, 0, { doneGroup: true });
  }
  return rows;
}

export function taskPath(state, index, t) {
  const list = getList(state, t.listId);
  const parts = [list ? `${list.emoji} ${list.name}` : ''];
  for (const a of ancestors(index, t)) parts.push(a.title || 'Başlıksız');
  return parts.filter(Boolean).join(' › ');
}

// Inbox first, then the lists in sidebar order.
function orderedLists(state) {
  return [...state.lists.filter((l) => l.id === INBOX_ID), ...state.lists.filter((l) => l.id !== INBOX_ID)];
}

// Stable "tree order" across lists, used to sort flat views.
function treeOrder(state, index) {
  const order = new Map();
  let n = 0;
  const walk = (t) => {
    order.set(t.id, n++);
    for (const c of index.children(t.id)) walk(c);
  };
  for (const l of orderedLists(state)) {
    for (const t of index.roots(l.id, null)) walk(t);
    for (const sec of l.sections) for (const t of index.roots(l.id, sec.id)) walk(t);
  }
  return order;
}

const MONTHS = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];
const DAYS = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi'];

export function dayLabel(dateStr, today = todayStr()) {
  if (dateStr === today) return 'Bugün';
  if (dateStr === addDays(today, 1)) return 'Yarın';
  if (dateStr === addDays(today, -1)) return 'Dün';
  const [y, m, d] = dateStr.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const base = `${d} ${MONTHS[m - 1]}`;
  const sameYear = y === Number(today.slice(0, 4));
  return `${base}${sameYear ? '' : ` ${y}`}, ${DAYS[date.getDay()]}`;
}

function groupedRows(groups) {
  const rows = [];
  for (const g of groups) {
    if (!g.rows.length) continue;
    rows.push({ kind: 'group', key: `g:${g.key}`, id: g.key, label: g.label, tone: g.tone ?? null, count: g.rows.length });
    rows.push(...g.rows);
  }
  return rows;
}

// Today: overdue + today. Next 7 days: overdue + today .. today+6, by day.
export function buildDueRows(state, mode) {
  const index = buildIndex(state);
  const today = todayStr();
  const end = mode === 'today' ? today : addDays(today, 6);
  const order = treeOrder(state, index);
  const tasks = state.tasks
    .filter((t) => !t.deletedAt && !t.done && t.due && t.due <= end)
    // Within a day, tasks with a time first, by time.
    .sort((a, b) => a.due.localeCompare(b.due) || (a.dueTime ?? '~').localeCompare(b.dueTime ?? '~') || b.priority - a.priority || order.get(a.id) - order.get(b.id));
  const flat = (t) => taskRow(t, 0, index, { path: taskPath(state, index, t), flat: true });
  const groups = [{ key: 'overdue', label: 'Gecikmiş', tone: 'overdue', rows: tasks.filter((t) => t.due < today).map(flat) }];
  for (let d = today; d <= end; d = addDays(d, 1)) {
    groups.push({ key: d, label: dayLabel(d, today), rows: tasks.filter((t) => t.due === d).map(flat) });
  }
  return groupedRows(groups);
}

export function buildTagRows(state, tagName) {
  const index = buildIndex(state);
  const key = String(tagName).toLocaleLowerCase('tr');
  const order = treeOrder(state, index);
  const tasks = state.tasks
    .filter((t) => !t.deletedAt && !t.done && t.tags.some((g) => g.toLocaleLowerCase('tr') === key))
    .sort((a, b) => order.get(a.id) - order.get(b.id));
  return tasks.map((t) => taskRow(t, 0, index, { path: taskPath(state, index, t), flat: true }));
}

// Open reports (top-level tasks) at one stage, from every list.
export function buildStageRows(state, stage) {
  const index = buildIndex(state);
  const order = treeOrder(state, index);
  return state.tasks
    .filter((t) => !t.deletedAt && !t.done && !t.parentId && t.stage === stage)
    .sort((a, b) => order.get(a.id) - order.get(b.id))
    .map((t) => taskRow(t, 0, index, { path: taskPath(state, index, t), flat: true }));
}

export function buildCompletedRows(state) {
  const index = buildIndex(state);
  const tasks = state.tasks.filter((t) => !t.deletedAt && t.done).sort((a, b) => (b.doneAt ?? 0) - (a.doneAt ?? 0));
  const groups = [];
  for (const t of tasks) {
    const day = todayStr(new Date(t.doneAt ?? t.updatedAt));
    let g = groups[groups.length - 1];
    if (!g || g.key !== day) groups.push((g = { key: day, label: dayLabel(day), rows: [] }));
    g.rows.push(taskRow(t, 0, index, { path: taskPath(state, index, t), flat: true }));
  }
  return groupedRows(groups);
}

export function buildTrashRows(state) {
  const byId = new Map(state.tasks.map((t) => [t.id, t]));
  const index = buildIndex(state);
  return state.tasks
    .filter((t) => t.deletedAt && !(t.parentId && byId.get(t.parentId)?.deletedAt))
    .sort((a, b) => b.deletedAt - a.deletedAt)
    .map((t) => {
      const list = getList(state, t.listId);
      const subtasks = state.tasks.filter((x) => x.parentId === t.id).length;
      return {
        ...taskRow(t, 0, index, { flat: true, trash: true }),
        path: list ? `${list.emoji} ${list.name}` : '',
        progress: { done: 0, total: subtasks },
      };
    });
}

// Search results as a filtered tree: matching tasks with their subtasks and,
// as dimmed context, their ancestors, inside their sections; grouped by list
// when searching every list.
export function buildSearchRows(state, query, { listId = null, includeDone = true } = {}) {
  const terms = parseQuery(query);
  if (!terms.length) return { rows: [], matches: 0, terms };
  const index = buildIndex(state);
  const hits = new Map();
  for (const t of state.tasks) {
    if (t.deletedAt || (!includeDone && t.done) || (listId && t.listId !== listId)) continue;
    const m = matchTask(t, terms);
    if (m) hits.set(t.id, m);
  }
  const keep = new Set();
  for (const id of hits.keys()) {
    keep.add(id);
    for (const a of ancestors(index, index.byId.get(id))) keep.add(a.id);
  }
  // A matching task comes with all its subtasks: searching for a project is
  // looking for what is under it.
  const below = new Set();
  const addBelow = (id) => {
    for (const c of index.children(id)) {
      if ((!includeDone && c.done) || below.has(c.id)) continue;
      below.add(c.id);
      keep.add(c.id);
      addBelow(c.id);
    }
  };
  for (const id of hits.keys()) addBelow(id);
  const lists = listId ? [getList(state, listId)].filter(Boolean) : orderedLists(state);
  const rows = [];
  for (const list of lists) {
    const out = [];
    let found = 0;
    const push = (t, depth) => {
      const hit = hits.get(t.id);
      if (hit) found++;
      const kids = index.children(t.id).filter((c) => keep.has(c.id));
      out.push(
        taskRow(t, depth, index, {
          childCount: kids.length,
          context: !hit && !below.has(t.id),
          snippet: hit && !hit.inTitle && hit.inNotes ? snippet(t.notes, terms) : null,
        }),
      );
      for (const c of kids) push(c, depth + 1);
    };
    for (const t of index.roots(list.id, null)) if (keep.has(t.id)) push(t, 0);
    for (const sec of list.sections) {
      const roots = index.roots(list.id, sec.id).filter((t) => keep.has(t.id));
      if (!roots.length) continue;
      out.push({ ...sectionRow(list, sec, 0), collapsed: false, count: null });
      for (const t of roots) push(t, 0);
    }
    if (!out.length) continue;
    if (!listId) rows.push({ kind: 'group', key: `g:${list.id}`, id: list.id, label: `${list.emoji} ${list.name}`, count: found });
    rows.push(...out);
  }
  return { rows, matches: hits.size, terms };
}

// ---------------------------------------------------------------- drag & drop

// Where a dragged task lands. `cands` are the visible rows without the dragged
// block. `hit` is {gap} (insert between cands[gap-1] and cands[gap]) or
// {into, gap} (pointer over the middle of cands[into]: becomes its subtask;
// `gap` is the fallback when that would nest too deep). `drag.dx` is the
// horizontal pointer travel: every `indent` pixels is one level in or out,
// within what the neighbouring rows allow.
export function projectTaskDrop(cands, hit, drag) {
  const { depth: startDepth = 0, height = 0, dx = 0, indent = 18 } = drag;
  const deepest = MAX_DEPTH - height;
  if (hit.into != null) {
    const target = cands[hit.into];
    if (target?.kind === 'task' && target.depth + 1 <= deepest) {
      return { mode: 'into', index: hit.into, depth: target.depth + 1, parentId: target.id, sectionId: target.task.sectionId, beforeId: null };
    }
  }
  const gap = clamp(hit.gap ?? 0, 0, cands.length);
  const prev = cands[gap - 1] ?? null;
  const next = cands[gap] ?? null;
  let minDepth = 0;
  let maxDepth = 0;
  if (prev?.kind === 'task') {
    maxDepth = Math.min(prev.depth + 1, deepest);
    minDepth = next?.kind === 'task' ? next.depth : 0;
  }
  if (minDepth > maxDepth) return null;
  const depth = clamp(startDepth + Math.round(dx / indent), minDepth, maxDepth);
  let parentId = null;
  let sectionId = null;
  for (let i = gap - 1; i >= 0; i--) {
    const r = cands[i];
    if (depth > 0) {
      if (r.kind === 'task' && r.depth === depth - 1) {
        parentId = r.id;
        sectionId = r.task.sectionId;
        break;
      }
    } else if (r.kind === 'section') {
      sectionId = r.id;
      break;
    } else if (r.kind === 'task') {
      sectionId = r.task.sectionId;
      break;
    }
  }
  if (depth > 0 && !parentId) return null;
  const beforeId = next?.kind === 'task' && next.depth === depth ? next.id : null;
  return { mode: 'gap', gap, depth, parentId, sectionId, beforeId };
}

// Sections are dropped between other sections: `blocks` are the other
// sections' extents (header to last task) in order; returns the insert index.
export function sectionDropIndex(blocks, y) {
  let i = 0;
  while (i < blocks.length && (blocks[i].top + blocks[i].bottom) / 2 < y) i++;
  return i;
}
