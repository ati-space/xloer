// Xloer state: a plain JSON document. The main process owns it and applies
// actions through dispatch(); windows receive the whole state after each change.
// This module has no Electron or DOM dependencies so it runs in node tests,
// the main process and (for the browser preview) in a renderer.

import { assignCustomers } from './customers.js';
import { DEFAULT_PALETTE, PALETTES } from './themes.js';
import { DEFAULT_DEVICES, DEFAULT_TEST_TYPES } from './invoices.js';
import { merges118, similarGroups } from './devices.js';

export const SCHEMA_VERSION = 1;
export const INBOX_ID = 'inbox';
// Root tasks are depth 0, so MAX_DEPTH 4 allows five levels in total.
export const MAX_DEPTH = 4;
export const PRIORITIES = [0, 1, 2, 3]; // none, low, medium, high
// Where a report stands; a task's `stage` is an index into this or null.
// Report stages are the user's own list (state.stages, edited under Aşama);
// these are the first ones. A task keeps its stage's id, so renaming and
// reordering stages leaves reports where they are.
export const DEFAULT_STAGES = ['Test', 'Veri', 'Rapor', 'Müşteri', 'Fatura'];
const MAX_STAGES = 12;
const defaultStages = () => DEFAULT_STAGES.map((name, i) => ({ id: `s${i}`, name }));
const isStage = (state, id) => typeof id === 'string' && state.stages.some((st) => st.id === id);

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;
const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/;

export function uid(prefix) {
  return `${prefix}_${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
}

export function defaultSettings() {
  return {
    theme: 'dark', // dark | light | system
    palette: DEFAULT_PALETTE, // a colour theme from themes.js
    closeToTray: true,
    startWithWindows: false,
    autoUpdate: true,
    // FastShot's file wheel while Xloer runs (src/main/file-wheel.js).
    fileWheel: true,
    trayHintShown: false,
    // 1.18.0 merged test systems once; 1.19.0 undid some of it, 1.20.0 the
    // rest, putting names of one system under its main name (device/repair-names).
    devicesMergedByName: false,
    devicesRepaired: false,
    devicesNamesFixed: false,
    widget: {
      visible: true,
      listId: null,
      opacity: 1,
      alwaysOnTop: false,
      locked: false,
      scope: 'list', // search scope: list | all
      pinLocks: true, // the pin fixes the place (before 1.12 it kept the widget on top)
    },
  };
}

function inboxList() {
  return { id: INBOX_ID, name: 'Gelen Kutusu', emoji: '📥', color: null, system: true, showCompleted: false, sections: [] };
}

export function emptyState() {
  return {
    version: SCHEMA_VERSION,
    lists: [inboxList()],
    tasks: [],
    tags: [],
    invoices: [],
    stages: defaultStages(),
    devices: DEFAULT_DEVICES.map((d) => ({ ...d })),
    testTypes: [...DEFAULT_TEST_TYPES],
    // Merged device names: an imported "from" becomes "to".
    deviceAliases: [],
    settings: defaultSettings(),
  };
}

// ---------------------------------------------------------------- dates

export function todayStr(date = new Date()) {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function addDays(dateStr, n) {
  const [y, m, d] = dateStr.split('-').map(Number);
  return todayStr(new Date(y, m - 1, d + n));
}

// ---------------------------------------------------------------- normalisation

const str = (v, fallback) => (typeof v === 'string' && v.trim() ? v : fallback);
const num = (v) => (Number.isFinite(v) ? v : null);
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

function uniqueStrings(arr) {
  if (!Array.isArray(arr)) return [];
  const out = [];
  for (const v of arr) {
    const s = typeof v === 'string' ? v.trim().replace(/^#/, '') : '';
    if (s && !out.some((o) => o.toLocaleLowerCase('tr') === s.toLocaleLowerCase('tr'))) out.push(s);
  }
  return out;
}

function normalizeSettings(raw = {}) {
  const d = defaultSettings();
  const w = { ...d.widget, ...(raw.widget && typeof raw.widget === 'object' ? raw.widget : {}) };
  // Until 1.12 the widget's pin kept it above every window; now the pin only
  // fixes its place on the desktop. A widget pinned before keeps its place
  // and stops floating over other apps.
  const oldPin = raw.widget?.pinLocks !== true && w.alwaysOnTop === true;
  return {
    theme: ['dark', 'light', 'system'].includes(raw.theme) ? raw.theme : d.theme,
    palette: PALETTES.some((p) => p.id === raw.palette) ? raw.palette : d.palette,
    closeToTray: typeof raw.closeToTray === 'boolean' ? raw.closeToTray : d.closeToTray,
    startWithWindows: !!raw.startWithWindows,
    autoUpdate: typeof raw.autoUpdate === 'boolean' ? raw.autoUpdate : d.autoUpdate,
    fileWheel: typeof raw.fileWheel === 'boolean' ? raw.fileWheel : d.fileWheel,
    trayHintShown: !!raw.trayHintShown,
    devicesMergedByName: !!raw.devicesMergedByName,
    devicesRepaired: !!raw.devicesRepaired,
    devicesNamesFixed: !!raw.devicesNamesFixed,
    widget: {
      visible: typeof w.visible === 'boolean' ? w.visible : d.widget.visible,
      listId: typeof w.listId === 'string' ? w.listId : null,
      opacity: clamp(Number.isFinite(w.opacity) ? w.opacity : 1, 0.3, 1),
      alwaysOnTop: oldPin ? false : !!w.alwaysOnTop,
      locked: oldPin || !!w.locked,
      scope: w.scope === 'all' ? 'all' : 'list',
      pinLocks: true,
    },
  };
}

function normalizeTask(t) {
  const createdAt = num(t.createdAt) ?? Date.now();
  return {
    id: t.id,
    listId: str(t.listId, INBOX_ID),
    sectionId: typeof t.sectionId === 'string' ? t.sectionId : null,
    parentId: typeof t.parentId === 'string' ? t.parentId : null,
    order: num(t.order) ?? 0,
    title: typeof t.title === 'string' ? t.title : '',
    notes: typeof t.notes === 'string' ? t.notes : '',
    done: !!t.done,
    doneAt: num(t.doneAt),
    due: typeof t.due === 'string' && DATE_RE.test(t.due) ? t.due : null,
    // "HH:MM" on the due date: Xloer reminds then (main.js). remindedFor is
    // the "date time" it last reminded of, so each moment reminds once.
    dueTime: typeof t.dueTime === 'string' && TIME_RE.test(t.dueTime) && DATE_RE.test(t.due ?? '') ? t.dueTime : null,
    remindedFor: typeof t.remindedFor === 'string' ? t.remindedFor : null,
    priority: PRIORITIES.includes(t.priority) ? t.priority : 0,
    // An id from state.stages; files from before 1.15 hold a position (0–4),
    // turned into an id by normalizeState.
    stage: typeof t.stage === 'string' || Number.isInteger(t.stage) ? t.stage : null,
    code: typeof t.code === 'string' ? t.code.trim() : '',
    tags: uniqueStrings(t.tags),
    collapsed: !!t.collapsed,
    createdAt,
    updatedAt: num(t.updatedAt) ?? createdAt,
    deletedAt: num(t.deletedAt),
  };
}

// Repairs anything a hand-edited or older file could get wrong: unknown lists,
// dangling parents, parent cycles, subtasks living in another list than their root.
export function normalizeState(raw) {
  if (!raw || typeof raw !== 'object' || !Array.isArray(raw.lists) || !Array.isArray(raw.tasks)) {
    throw new Error('Geçersiz Xloer verisi');
  }
  const s = emptyState();
  s.lists = [];
  const seen = new Set();
  for (const l of raw.lists) {
    if (!l || typeof l.id !== 'string' || seen.has(l.id)) continue;
    seen.add(l.id);
    const sections = (Array.isArray(l.sections) ? l.sections : [])
      .filter((x) => x && typeof x.id === 'string')
      .map((x) => ({ id: x.id, name: str(x.name, 'Bölüm'), collapsed: !!x.collapsed }));
    s.lists.push({
      id: l.id,
      name: str(l.name, l.id === INBOX_ID ? 'Gelen Kutusu' : 'Liste'),
      emoji: str(l.emoji, l.id === INBOX_ID ? '📥' : '📁'),
      color: typeof l.color === 'string' ? l.color : null,
      system: l.id === INBOX_ID,
      showCompleted: !!l.showCompleted,
      sections: l.id === INBOX_ID ? [] : sections,
    });
  }
  if (!seen.has(INBOX_ID)) s.lists.unshift(inboxList());

  const byId = new Map();
  for (const t of raw.tasks) {
    if (t && typeof t.id === 'string' && !byId.has(t.id)) byId.set(t.id, normalizeTask(t));
  }
  s.tasks = [...byId.values()];
  const lists = new Map(s.lists.map((l) => [l.id, l]));
  for (const t of s.tasks) {
    if (!lists.has(t.listId)) {
      t.listId = INBOX_ID;
      t.sectionId = null;
    }
    if (t.parentId && (t.parentId === t.id || !byId.has(t.parentId))) t.parentId = null;
  }
  for (const t of s.tasks) {
    const chain = new Set([t.id]);
    for (let p = t.parentId && byId.get(t.parentId); p; p = p.parentId && byId.get(p.parentId)) {
      if (chain.has(p.id)) {
        t.parentId = null;
        break;
      }
      chain.add(p.id);
    }
  }
  for (const t of s.tasks) {
    let root = t;
    while (root.parentId) root = byId.get(root.parentId);
    if (root !== t) {
      t.listId = root.listId;
      t.sectionId = root.sectionId;
    }
    const list = lists.get(t.listId);
    if (t.sectionId && !list.sections.some((x) => x.id === t.sectionId)) t.sectionId = null;
  }

  const tags = [];
  for (const g of Array.isArray(raw.tags) ? raw.tags : []) {
    const name = g && typeof g.name === 'string' ? g.name.trim().replace(/^#/, '') : '';
    if (name && !findTag({ tags }, name)) tags.push({ name, color: typeof g.color === 'string' ? g.color : null });
  }
  s.tags = tags;
  for (const t of s.tasks) for (const name of t.tags) ensureTag(s, name);
  normalizeInvoiceData(s, raw);
  s.stages = normalizeStages(raw.stages);
  for (const t of s.tasks) {
    if (Number.isInteger(t.stage)) t.stage = Array.isArray(raw.stages) ? null : (s.stages[t.stage]?.id ?? null);
    else if (!isStage(s, t.stage)) t.stage = null;
  }
  s.settings = normalizeSettings(raw.settings);
  return s;
}

// A one-day test has no end. Rows from Excel also keep what the sheet said,
// for the checks on the card: its Gün/Vardiya days, the date text when it was
// mistyped or unreadable, and whether the year or the end was worked out.
function normalizeRow(r) {
  if (!r || typeof r.id !== 'string') return null;
  const start = typeof r.start === 'string' && DATE_RE.test(r.start) ? r.start : null;
  let end = typeof r.end === 'string' && DATE_RE.test(r.end) ? r.end : null;
  if (!start || (end && end <= start)) end = null;
  const row = { id: r.id, test: str(r.test, ''), device: str(r.device, ''), start, end };
  if (Number.isInteger(r.sheetDays) && r.sheetDays >= 0) row.sheetDays = r.sheetDays;
  if (typeof r.sheetDate === 'string' && r.sheetDate) row.sheetDate = r.sheetDate;
  if (r.yearFixed === true) row.yearFixed = true;
  if (r.endFromDays === true) row.endFromDays = true;
  return row;
}

function normalizeInvoice(i) {
  if (!i || typeof i.id !== 'string') return null;
  const createdAt = num(i.createdAt) ?? Date.now();
  const ids = new Set();
  return {
    id: i.id,
    no: Number.isInteger(i.no) && i.no > 0 ? i.no : 0,
    code: str(i.code, '').trim(),
    title: str(i.title, '').trim(),
    ref: str(i.ref, '').trim(),
    amount: Number.isFinite(i.amount) && i.amount >= 0 ? i.amount : null,
    rows: (Array.isArray(i.rows) ? i.rows : []).map(normalizeRow).filter((r) => r && !ids.has(r.id) && ids.add(r.id)),
    customer: str(i.customer, '').trim(),
    // "Açıklama": free text about the project or its invoice, kept on import.
    notes: str(i.notes, '').trimEnd(),
    // The year the amount counts in, chosen on the card; null: the last test's.
    year: Number.isInteger(i.year) && i.year >= 2000 && i.year <= 2100 ? i.year : null,
    // From Excel: the "Proje Kodu" cell when it disagreed with the title.
    ...(i.fromSheet === true && { fromSheet: true }),
    ...(typeof i.sheetCode === 'string' && i.sheetCode && { sheetCode: i.sheetCode }),
    dismissed: Array.isArray(i.dismissed) ? [...new Set(i.dismissed.filter((k) => typeof k === 'string'))] : [],
    createdAt,
    updatedAt: num(i.updatedAt) ?? createdAt,
  };
}

// Files written before invoices existed get the default devices and tests.
function normalizeInvoiceData(s, raw) {
  const ids = new Set();
  s.invoices = (Array.isArray(raw.invoices) ? raw.invoices : []).map(normalizeInvoice).filter((i) => i && !ids.has(i.id) && ids.add(i.id));
  if (Array.isArray(raw.devices)) {
    s.devices = [];
    for (const d of raw.devices) {
      const name = d && typeof d.name === 'string' ? d.name.trim() : '';
      if (name && !findNamed(s.devices.map((x) => x.name), name)) s.devices.push({ name, test: str(d.test, '').trim() });
    }
  }
  if (Array.isArray(raw.testTypes)) s.testTypes = uniqueNames(raw.testTypes);
  s.deviceAliases = normalizeAliases(raw.deviceAliases);
  // Files from before the customer field: read customers from the titles.
  const missing = new Set((Array.isArray(raw.invoices) ? raw.invoices : []).filter((i) => typeof i?.customer !== 'string').map((i) => i?.id));
  assignCustomers(s.invoices.filter((i) => missing.has(i.id)), s.invoices);
}

function normalizeAliases(list) {
  const out = [];
  for (const a of Array.isArray(list) ? list : []) {
    const from = typeof a?.from === 'string' ? a.from.trim() : '';
    const to = typeof a?.to === 'string' ? a.to.trim() : '';
    if (from && to && foldName(from) !== foldName(to) && !out.some((x) => foldName(x.from) === foldName(from))) out.push({ from, to });
  }
  return out;
}

// The name a device goes by after merges.
export function deviceAlias(state, name) {
  const hit = (state.deviceAliases ?? []).find((a) => foldName(a.from) === foldName(name));
  return hit ? hit.to : name;
}

function normalizeStages(list) {
  if (!Array.isArray(list)) return defaultStages();
  const out = [];
  for (const st of list) {
    const id = typeof st?.id === 'string' ? st.id : '';
    const name = typeof st?.name === 'string' ? st.name.trim() : '';
    if (id && name && !out.some((x) => x.id === id) && out.length < MAX_STAGES) out.push({ id, name });
  }
  return out;
}

const foldName = (v) => String(v).trim().replace(/\s+/g, ' ').toLocaleLowerCase('tr');
const findNamed = (names, name) => names.find((n) => foldName(n) === foldName(name)) ?? null;

function uniqueNames(arr) {
  const out = [];
  for (const v of arr) if (typeof v === 'string' && v.trim() && !findNamed(out, v)) out.push(v.trim());
  return out;
}

// ---------------------------------------------------------------- queries

export const byOrder = (a, b) => a.order - b.order;

// Groups live tasks by their sibling group so tree walks are O(n).
export function buildIndex(state) {
  const byId = new Map();
  const groups = new Map();
  for (const t of state.tasks) {
    byId.set(t.id, t);
    if (t.deletedAt) continue;
    const key = groupKey(t.listId, t.sectionId, t.parentId);
    let arr = groups.get(key);
    if (!arr) groups.set(key, (arr = []));
    arr.push(t);
  }
  for (const arr of groups.values()) arr.sort(byOrder);
  return {
    byId,
    roots: (listId, sectionId = null) => groups.get(groupKey(listId, sectionId, null)) ?? [],
    children: (id) => groups.get(`c:${id}`) ?? [],
  };
}

function groupKey(listId, sectionId, parentId) {
  return parentId ? `c:${parentId}` : `r:${listId}:${sectionId ?? ''}`;
}

export function getList(state, id) {
  return state.lists.find((l) => l.id === id) ?? null;
}

export function findTag(state, name) {
  const key = String(name).toLocaleLowerCase('tr');
  return state.tags.find((g) => g.name.toLocaleLowerCase('tr') === key) ?? null;
}

export function descendants(index, id) {
  const out = [];
  const walk = (pid) => {
    for (const c of index.children(pid)) {
      out.push(c);
      walk(c.id);
    }
  };
  walk(id);
  return out;
}

export function depthOf(index, task) {
  let d = 0;
  for (let p = task.parentId && index.byId.get(task.parentId); p; p = p.parentId && index.byId.get(p.parentId)) d++;
  return d;
}

// Levels below the task: 0 for a leaf, 1 if it only has children, ...
export function subtreeHeight(index, id) {
  let h = 0;
  for (const c of index.children(id)) h = Math.max(h, 1 + subtreeHeight(index, c.id));
  return h;
}

export function ancestors(index, task) {
  const out = [];
  for (let p = task.parentId && index.byId.get(task.parentId); p; p = p.parentId && index.byId.get(p.parentId)) out.unshift(p);
  return out;
}

export function counts(state) {
  const today = todayStr();
  const weekEnd = addDays(today, 6);
  const lists = new Map();
  const tags = new Map();
  let todayCount = 0;
  let next7 = 0;
  let trash = 0;
  const stageAt = new Map(state.stages.map((st, i) => [st.id, i]));
  const stages = state.stages.map(() => 0);
  const byId = new Map(state.tasks.map((t) => [t.id, t]));
  for (const t of state.tasks) {
    if (t.deletedAt) {
      const parent = t.parentId && byId.get(t.parentId);
      if (!parent || !parent.deletedAt) trash++;
      continue;
    }
    if (t.done) continue;
    lists.set(t.listId, (lists.get(t.listId) ?? 0) + 1);
    for (const g of t.tags) {
      const key = g.toLocaleLowerCase('tr');
      tags.set(key, (tags.get(key) ?? 0) + 1);
    }
    if (t.due && t.due <= today) todayCount++;
    if (t.due && t.due <= weekEnd) next7++;
    if (!t.parentId && stageAt.has(t.stage)) stages[stageAt.get(t.stage)]++;
  }
  return { lists, tags, today: todayCount, next7, trash, stages };
}

// ---------------------------------------------------------------- mutations

function mustList(state, id) {
  const list = getList(state, id);
  if (!list) throw new Error('Liste bulunamadı');
  return list;
}

function mustTask(state, id) {
  const t = state.tasks.find((x) => x.id === id);
  if (!t) throw new Error('Görev bulunamadı');
  return t;
}

function ensureTag(state, name) {
  if (!findTag(state, name)) state.tags.push({ name, color: null });
}

function cleanName(v, fallback) {
  const s = typeof v === 'string' ? v.trim() : '';
  return s || fallback;
}

function liveGroup(state, listId, sectionId, parentId, except) {
  return state.tasks
    .filter(
      (t) =>
        t !== except &&
        !t.deletedAt &&
        (parentId ? t.parentId === parentId : !t.parentId && t.listId === listId && (t.sectionId ?? null) === (sectionId ?? null)),
    )
    .sort(byOrder);
}

// Puts the task into its (already assigned) sibling group, before `beforeId`
// or at the end, and renumbers the group so orders stay small and dense.
function place(state, task, { beforeId = null, atStart = false } = {}) {
  const sibs = liveGroup(state, task.listId, task.sectionId, task.parentId, task);
  let i = sibs.length;
  if (atStart) i = 0;
  else if (beforeId) {
    const j = sibs.findIndex((x) => x.id === beforeId);
    if (j >= 0) i = j;
  }
  sibs.splice(i, 0, task);
  sibs.forEach((x, k) => {
    x.order = k;
  });
}

function cleanPatch(state, task, patch = {}) {
  if ('title' in patch) task.title = typeof patch.title === 'string' ? patch.title.replace(/\s*\n\s*/g, ' ') : task.title;
  if ('notes' in patch) task.notes = typeof patch.notes === 'string' ? patch.notes : task.notes;
  if ('due' in patch) task.due = typeof patch.due === 'string' && DATE_RE.test(patch.due) ? patch.due : null;
  if ('dueTime' in patch) task.dueTime = typeof patch.dueTime === 'string' && TIME_RE.test(patch.dueTime) ? patch.dueTime : null;
  if (!task.due) task.dueTime = null;
  if ('priority' in patch && PRIORITIES.includes(patch.priority)) task.priority = patch.priority;
  if ('collapsed' in patch) task.collapsed = !!patch.collapsed;
  if ('stage' in patch) task.stage = isStage(state, patch.stage) ? patch.stage : null;
  if ('code' in patch) task.code = typeof patch.code === 'string' ? patch.code.trim() : task.code;
  if ('tags' in patch) {
    task.tags = uniqueStrings(patch.tags);
    for (const g of task.tags) ensureTag(state, g);
  }
}

const handlers = {
  'list/create'(s, { name, emoji, color = null }) {
    const list = {
      id: uid('l'),
      name: cleanName(name, 'Yeni liste'),
      emoji: cleanName(emoji, '📁'),
      color,
      system: false,
      showCompleted: false,
      sections: [],
    };
    s.lists.push(list);
    return list.id;
  },

  'list/update'(s, { id, patch = {} }) {
    const list = mustList(s, id);
    if ('name' in patch && !list.system) list.name = cleanName(patch.name, list.name);
    if ('emoji' in patch) list.emoji = cleanName(patch.emoji, list.emoji);
    if ('color' in patch) list.color = typeof patch.color === 'string' ? patch.color : null;
    if ('showCompleted' in patch) list.showCompleted = !!patch.showCompleted;
  },

  // Tasks of a deleted list go to the trash; restoring them lands them in the inbox.
  'list/delete'(s, { id }) {
    const list = mustList(s, id);
    if (list.system) throw new Error('Gelen Kutusu silinemez');
    const now = Date.now();
    for (const t of s.tasks) {
      if (t.listId !== id) continue;
      t.listId = INBOX_ID;
      t.sectionId = null;
      if (!t.deletedAt) t.deletedAt = now;
    }
    s.lists = s.lists.filter((l) => l !== list);
    if (s.settings.widget.listId === id) s.settings.widget.listId = null;
  },

  'list/move'(s, { id, beforeId = null }) {
    const list = mustList(s, id);
    const rest = s.lists.filter((l) => l !== list);
    const i = beforeId ? rest.findIndex((l) => l.id === beforeId) : -1;
    rest.splice(i >= 0 ? i : rest.length, 0, list);
    s.lists = rest;
  },

  'section/create'(s, { listId, name, beforeId = null }) {
    const list = mustList(s, listId);
    if (list.system) throw new Error('Gelen Kutusu’na bölüm eklenemez');
    const sec = { id: uid('s'), name: cleanName(name, 'Yeni bölüm'), collapsed: false };
    const i = beforeId ? list.sections.findIndex((x) => x.id === beforeId) : -1;
    list.sections.splice(i >= 0 ? i : list.sections.length, 0, sec);
    return sec.id;
  },

  'section/update'(s, { listId, id, patch = {} }) {
    const sec = mustList(s, listId).sections.find((x) => x.id === id);
    if (!sec) throw new Error('Bölüm bulunamadı');
    if ('name' in patch) sec.name = cleanName(patch.name, sec.name);
    if ('collapsed' in patch) sec.collapsed = !!patch.collapsed;
  },

  // The section's tasks are kept: they move to the unsectioned top of the list.
  'section/delete'(s, { listId, id }) {
    const list = mustList(s, listId);
    if (!list.sections.some((x) => x.id === id)) throw new Error('Bölüm bulunamadı');
    const moved = s.tasks.filter((t) => t.listId === listId && t.sectionId === id);
    const tail = liveGroup(s, listId, null, null).length;
    for (const t of moved) {
      t.sectionId = null;
      if (!t.parentId) t.order += tail;
    }
    list.sections = list.sections.filter((x) => x.id !== id);
  },

  'section/move'(s, { listId, id, beforeId = null }) {
    const list = mustList(s, listId);
    const sec = list.sections.find((x) => x.id === id);
    if (!sec) throw new Error('Bölüm bulunamadı');
    const rest = list.sections.filter((x) => x !== sec);
    const i = beforeId ? rest.findIndex((x) => x.id === beforeId) : -1;
    rest.splice(i >= 0 ? i : rest.length, 0, sec);
    list.sections = rest;
  },

  'task/create'(s, a) {
    let listId = a.listId ?? INBOX_ID;
    let sectionId = a.sectionId ?? null;
    const parentId = a.parentId ?? null;
    if (parentId) {
      const parent = mustTask(s, parentId);
      if (depthOf(buildIndex(s), parent) + 1 > MAX_DEPTH) throw new Error('En fazla 5 seviye alt görev olabilir');
      listId = parent.listId;
      sectionId = parent.sectionId;
      parent.collapsed = false;
    } else {
      const list = mustList(s, listId);
      if (sectionId && !list.sections.some((x) => x.id === sectionId)) sectionId = null;
    }
    const now = Date.now();
    const task = normalizeTask({ id: uid('t'), listId, sectionId, parentId, createdAt: now, updatedAt: now });
    cleanPatch(s, task, { title: a.title ?? '', notes: a.notes, due: a.due, priority: a.priority, tags: a.tags ?? [] });
    s.tasks.push(task);
    place(s, task, { beforeId: a.beforeId, atStart: a.atStart });
    return task.id;
  },

  'task/update'(s, { id, patch }) {
    const task = mustTask(s, id);
    cleanPatch(s, task, patch);
    task.updatedAt = Date.now();
  },

  // The reminder for `at` ("date time") has been shown.
  'task/reminded'(s, { id, at }) {
    mustTask(s, id).remindedFor = typeof at === 'string' ? at : null;
  },

  // Completing a task completes its subtasks, and completing the last open
  // subtask completes its parent (and so on upwards). Reopening a task
  // reopens its parents, so it does not stay hidden under a completed one,
  // and leaves its subtasks as they are. Returns the parents it completed.
  'task/toggle'(s, { id, done }) {
    const task = mustTask(s, id);
    const value = typeof done === 'boolean' ? done : !task.done;
    const index = buildIndex(s);
    const now = Date.now();
    const complete = (t) => {
      t.done = true;
      t.doneAt = now;
      t.updatedAt = now;
    };
    if (value) {
      for (const t of [task, ...descendants(index, id)]) if (!t.done) complete(t);
      const parents = [];
      for (const p of ancestors(index, task).reverse()) {
        if (p.done || !index.children(p.id).every((c) => c.done)) break;
        complete(p);
        parents.push(p.id);
      }
      return { parents };
    } else {
      for (const t of [task, ...ancestors(index, task)]) {
        if (t.done) {
          t.done = false;
          t.doneAt = null;
          t.updatedAt = now;
        }
      }
    }
  },

  // Moves a task with its whole subtree. `parentId` wins over list/section.
  'task/move'(s, { id, listId, sectionId = null, parentId = null, beforeId = null }) {
    const task = mustTask(s, id);
    const index = buildIndex(s);
    if (parentId) {
      const parent = index.byId.get(parentId);
      if (!parent || parent.deletedAt) throw new Error('Hedef görev bulunamadı');
      for (let a = parent; a; a = a.parentId && index.byId.get(a.parentId)) {
        if (a.id === id) throw new Error('Bir görev kendi alt görevinin içine taşınamaz');
      }
      if (depthOf(index, parent) + 1 + subtreeHeight(index, id) > MAX_DEPTH) {
        throw new Error('En fazla 5 seviye alt görev olabilir');
      }
      listId = parent.listId;
      sectionId = parent.sectionId;
      parent.collapsed = false;
    } else {
      const list = mustList(s, listId ?? task.listId);
      listId = list.id;
      if (sectionId && !list.sections.some((x) => x.id === sectionId)) sectionId = null;
    }
    const subtree = descendants(index, id);
    task.parentId = parentId;
    task.listId = listId;
    task.sectionId = sectionId;
    task.updatedAt = Date.now();
    for (const d of subtree) {
      d.listId = listId;
      d.sectionId = sectionId;
    }
    place(s, task, { beforeId });
  },

  'task/delete'(s, { id }) {
    const task = mustTask(s, id);
    const now = Date.now();
    for (const t of [task, ...descendants(buildIndex(s), id)]) t.deletedAt ??= now;
  },

  'task/restore'(s, { id }) {
    const task = mustTask(s, id);
    const byId = new Map(s.tasks.map((t) => [t.id, t]));
    const parent = task.parentId && byId.get(task.parentId);
    if (!parent || parent.deletedAt) {
      task.parentId = null;
      const list = getList(s, task.listId);
      if (!list) task.listId = INBOX_ID;
      if (!list || (task.sectionId && !list.sections.some((x) => x.id === task.sectionId))) task.sectionId = null;
    } else {
      task.listId = parent.listId;
      task.sectionId = parent.sectionId;
    }
    const subtree = [];
    const walk = (pid) => {
      for (const t of s.tasks) {
        if (t.parentId === pid) {
          subtree.push(t);
          walk(t.id);
        }
      }
    };
    walk(task.id);
    task.deletedAt = null;
    for (const t of subtree) {
      t.deletedAt = null;
      t.listId = task.listId;
      t.sectionId = task.sectionId;
    }
    place(s, task);
  },

  'task/purge'(s, { id }) {
    const doomed = new Set([id]);
    let grew = true;
    while (grew) {
      grew = false;
      for (const t of s.tasks) {
        if (t.parentId && doomed.has(t.parentId) && !doomed.has(t.id)) {
          doomed.add(t.id);
          grew = true;
        }
      }
    }
    s.tasks = s.tasks.filter((t) => !doomed.has(t.id));
  },

  'trash/empty'(s) {
    s.tasks = s.tasks.filter((t) => !t.deletedAt);
    const ids = new Set(s.tasks.map((t) => t.id));
    for (const t of s.tasks) if (t.parentId && !ids.has(t.parentId)) t.parentId = null;
  },

  'tag/create'(s, { name, color = null }) {
    const clean = cleanName(name, '').replace(/^#/, '');
    if (!clean) throw new Error('Etiket adı boş olamaz');
    if (!findTag(s, clean)) s.tags.push({ name: clean, color });
    return clean;
  },

  'tag/update'(s, { name, patch = {} }) {
    const tag = findTag(s, name);
    if (!tag) throw new Error('Etiket bulunamadı');
    if ('color' in patch) tag.color = typeof patch.color === 'string' ? patch.color : null;
    if ('name' in patch) {
      const next = cleanName(patch.name, tag.name).replace(/^#/, '');
      const clash = findTag(s, next);
      if (clash && clash !== tag) throw new Error('Bu isimde bir etiket zaten var');
      const old = tag.name.toLocaleLowerCase('tr');
      for (const t of s.tasks) t.tags = t.tags.map((g) => (g.toLocaleLowerCase('tr') === old ? next : g));
      tag.name = next;
    }
  },

  'tag/delete'(s, { name }) {
    const key = String(name).toLocaleLowerCase('tr');
    s.tags = s.tags.filter((g) => g.name.toLocaleLowerCase('tr') !== key);
    for (const t of s.tasks) t.tags = t.tags.filter((g) => g.toLocaleLowerCase('tr') !== key);
  },

  'settings/update'(s, { patch = {} }) {
    s.settings = normalizeSettings({
      ...s.settings,
      ...patch,
      widget: { ...s.settings.widget, ...(patch.widget ?? {}) },
    });
  },

  // Brings in lists parsed from a CSV export. Lists and sections are
  // matched by name; a task already present (same section, parent and title)
  // is not duplicated, so importing twice or over the seed data is harmless.
  'import/merge'(s, { lists = [] }) {
    let created = 0;
    let skipped = 0;
    let newLists = 0;
    const fold = (v) => String(v ?? '').trim().toLocaleLowerCase('tr');
    for (const src of lists) {
      const isInbox = String(src.name).trim().toLowerCase() === 'inbox';
      let list = s.lists.find((l) => (isInbox ? l.system : fold(l.name) === fold(src.name)));
      if (!list) {
        list = getList(s, handlers['list/create'](s, { name: src.name, emoji: src.emoji }));
        newLists++;
      }
      const sectionIds = new Map();
      for (const name of src.sections ?? []) {
        if (list.system) break;
        const sec = list.sections.find((x) => fold(x.name) === fold(name));
        sectionIds.set(fold(name), sec ? sec.id : handlers['section/create'](s, { listId: list.id, name }));
      }

      const index = buildIndex(s);
      const keyOf = (sectionName, parentTitle, title) => `${fold(sectionName)}\u0000${fold(parentTitle)}\u0000${fold(title)}`;
      const existing = new Map();
      for (const t of s.tasks) {
        if (t.deletedAt || t.listId !== list.id) continue;
        const sec = list.sections.find((x) => x.id === t.sectionId);
        const parent = t.parentId && index.byId.get(t.parentId);
        existing.set(keyOf(sec?.name, parent?.title, t.title), t.id);
      }

      const srcByRef = new Map(src.tasks.map((t) => [t.ref, t]));
      const depth = (t) => {
        let d = 0;
        const chain = new Set();
        for (let p = t.parentRef && srcByRef.get(t.parentRef); p && !chain.has(p); p = p.parentRef && srcByRef.get(p.parentRef)) {
          chain.add(p);
          d++;
        }
        return d;
      };
      const ordered = [...src.tasks].map((t) => ({ t, d: depth(t) })).sort((a, b) => a.d - b.d || (a.t.order ?? 0) - (b.t.order ?? 0));
      const idForRef = new Map();
      for (const { t, d } of ordered) {
        let parentRef = t.parentRef && srcByRef.has(t.parentRef) ? t.parentRef : null;
        // Deeper than Xloer allows: hang it under the deepest allowed ancestor.
        for (let extra = d - MAX_DEPTH; extra > 0 && parentRef; extra--) parentRef = srcByRef.get(parentRef).parentRef ?? null;
        const parentId = parentRef ? idForRef.get(parentRef) : null;
        if (parentRef && !parentId) continue;
        const parentSrc = parentRef ? srcByRef.get(parentRef) : null;
        const sectionName = parentRef ? null : t.sectionName;
        const key = keyOf(parentSrc ? rootSection(srcByRef, parentSrc) : sectionName, parentSrc?.title, t.title);
        if (existing.has(key)) {
          idForRef.set(t.ref, existing.get(key));
          skipped++;
          continue;
        }
        const id = handlers['task/create'](s, {
          listId: list.id,
          sectionId: sectionName ? sectionIds.get(fold(sectionName)) ?? null : null,
          parentId,
          title: t.title,
          notes: t.notes,
          due: t.due,
          priority: t.priority,
          tags: t.tags,
        });
        const task = mustTask(s, id);
        if (t.done) {
          task.done = true;
          task.doneAt = t.doneAt ?? Date.now();
        }
        if (Number.isFinite(t.createdAt)) task.createdAt = t.createdAt;
        idForRef.set(t.ref, id);
        existing.set(key, id);
        created++;
      }
    }
    return { created, skipped, newLists };
  },

  'state/replace'(s, { state }) {
    const next = normalizeState(state);
    for (const k of Object.keys(s)) delete s[k];
    Object.assign(s, next);
  },

  // ---------------------------------------------------------------- invoices

  'invoice/create'(s, { code = '', title = '', ref = '', year = null } = {}) {
    const now = Date.now();
    const invoice = normalizeInvoice({ id: uid('f'), no: Math.max(0, ...s.invoices.map((i) => i.no)) + 1, code, title, ref, year, rows: [], createdAt: now });
    s.invoices.push(invoice);
    return invoice.id;
  },

  'invoice/update'(s, { id, patch = {} }) {
    const inv = mustInvoice(s, id);
    for (const k of ['code', 'title', 'ref', 'customer']) if (k in patch) inv[k] = typeof patch[k] === 'string' ? patch[k].trim() : inv[k];
    if ('notes' in patch && typeof patch.notes === 'string') inv.notes = patch.notes.trimEnd();
    if ('year' in patch) inv.year = normalizeInvoice({ id: inv.id, year: patch.year }).year;
    if ('no' in patch && Number.isInteger(patch.no) && patch.no > 0) inv.no = patch.no;
    if ('amount' in patch) inv.amount = Number.isFinite(patch.amount) && patch.amount >= 0 ? Math.round(patch.amount * 100) / 100 : null;
    inv.updatedAt = Date.now();
  },

  // One customer name on several projects.
  'invoice/set-customer'(s, { ids = [], customer = '' }) {
    const name = String(customer).trim();
    for (const inv of s.invoices) if (ids.includes(inv.id)) inv.customer = name;
  },

  // Puts projects under a year (null: the year of their last test again).
  'invoice/set-year'(s, { ids = [], year = null }) {
    const value = normalizeInvoice({ id: 'x', year }).year;
    if (year != null && value == null) throw new Error('Geçersiz yıl');
    for (const id of ids) mustInvoice(s, id).year = value;
    return ids.length;
  },

  // One id, or several at once ("Hepsini kaldır").
  'invoice/delete'(s, { id, ids = [id] }) {
    for (const x of ids) mustInvoice(s, x);
    const gone = new Set(ids);
    s.invoices = s.invoices.filter((i) => !gone.has(i.id));
  },

  // Undo for a delete: puts the removed entries back as they were.
  'invoice/restore'(s, { invoice, invoices = [invoice] }) {
    const back = invoices.map(normalizeInvoice);
    if (back.some((inv) => !inv)) throw new Error('Geçersiz proje');
    for (const inv of back) if (!s.invoices.some((i) => i.id === inv.id)) s.invoices.push(inv);
  },

  // A new row repeats the last row's device and test unless told otherwise.
  'invoice/row-add'(s, { id, row = {} }) {
    const inv = mustInvoice(s, id);
    const last = sortedByDate(inv.rows).at(-1);
    const device = 'device' in row ? row.device : last?.device ?? '';
    const r = normalizeRow({
      id: uid('r'),
      device,
      test: 'test' in row ? row.test : last && last.device === device ? last.test : defaultTest(s, device),
      start: row.start,
      end: row.end,
    });
    inv.rows.push(r);
    inv.updatedAt = Date.now();
    return r.id;
  },

  // Picking another device switches the test to that device's usual test,
  // unless the row's test was chosen by hand.
  'invoice/row-update'(s, { id, rowId, patch = {} }) {
    const inv = mustInvoice(s, id);
    const row = inv.rows.find((r) => r.id === rowId);
    if (!row) throw new Error('Satır bulunamadı');
    const next = { ...row };
    if ('device' in patch) {
      next.device = str(patch.device, '').trim();
      if (!('test' in patch) && (!row.test || row.test === defaultTest(s, row.device))) next.test = defaultTest(s, next.device) || row.test;
    }
    if ('test' in patch) next.test = str(patch.test, '').trim();
    if ('start' in patch) next.start = patch.start;
    if ('end' in patch) next.end = patch.end;
    if (next.start && next.end && next.end < next.start) [next.start, next.end] = [next.end, next.start];
    // Dates set by hand: what was worked out from the sheet no longer applies.
    if ('start' in patch || 'end' in patch) {
      delete next.yearFixed;
      delete next.endFromDays;
    }
    inv.rows[inv.rows.indexOf(row)] = normalizeRow(next);
    inv.updatedAt = Date.now();
  },

  'invoice/row-delete'(s, { id, rowId }) {
    const inv = mustInvoice(s, id);
    inv.rows = inv.rows.filter((r) => r.id !== rowId);
    inv.updatedAt = Date.now();
  },

  // Projects read from the invoice workbook. A project already here is
  // brought in line with the workbook (its test rows replaced); devices and
  // tests the lists do not know yet are added.
  // `year`: the year the sheet is for (from its file name), set on every
  // project in it; without one a project keeps the year it had.
  'invoice/import'(s, { projects = [], year = null }) {
    const before = { devices: s.devices.length, tests: s.testTypes.length };
    const used = new Set();
    const fresh = [];
    let created = 0;
    let updated = 0;
    const now = Date.now();
    for (const p of projects) {
      const rows = (Array.isArray(p.rows) ? p.rows : []).map((r) => ({ ...r, id: uid('r'), test: str(r.test, '').trim(), device: deviceAlias(s, str(r.device, '').trim()) }));
      // Known devices and tests keep their stored spelling; new ones are learnt
      // from dated rows only, so notes such as "Yatırım" stay out of the lists.
      for (const r of rows) {
        const device = r.device && findNamed(s.devices.map((d) => d.name), r.device);
        const test = r.test && findNamed(s.testTypes, r.test);
        if (test) r.test = test;
        else if (r.test && r.start) s.testTypes.push(r.test);
        if (device) r.device = device;
        else if (r.device && r.start) s.devices.push({ name: r.device, test: r.test });
      }
      const fields = { code: p.code ?? '', title: p.title ?? '', ref: p.ref ?? '', amount: p.amount ?? null, rows, fromSheet: true, sheetCode: p.sheetCode ?? null, dismissed: [], ...(year != null && { year }) };
      const match = findInvoiceMatch(s, p, used);
      if (match) {
        used.add(match.id);
        // Replaced, not merged, so facts from the last import do not linger.
        const next = normalizeInvoice({ ...match, ...fields, no: Number.isInteger(p.no) && p.no > 0 ? p.no : match.no, updatedAt: now });
        s.invoices[s.invoices.indexOf(match)] = next;
        if (!next.customer) fresh.push(next);
        updated++;
      } else {
        const no = Number.isInteger(p.no) && p.no > 0 ? p.no : Math.max(0, ...s.invoices.map((i) => i.no)) + 1;
        const inv = normalizeInvoice({ id: uid('f'), ...fields, no, createdAt: now });
        s.invoices.push(inv);
        used.add(inv.id);
        fresh.push(inv);
        created++;
      }
    }
    // A customer typed on a card stays; the rest are read from the titles.
    assignCustomers(fresh, s.invoices);
    return { created, updated, devicesAdded: s.devices.length - before.devices, testsAdded: s.testTypes.length - before.tests };
  },

  // Undo for an import: puts back what was there before it.
  'invoice/import-undo'(s, { invoices, devices, testTypes, deviceAliases }) {
    s.invoices = invoices.map(normalizeInvoice).filter(Boolean);
    s.devices = devices;
    s.testTypes = testTypes;
    if (deviceAliases) s.deviceAliases = normalizeAliases(deviceAliases);
  },

  // A check on the card that is fine as it is.
  'invoice/dismiss-issue'(s, { id, key }) {
    const inv = mustInvoice(s, id);
    if (!inv.dismissed.includes(key)) inv.dismissed.push(key);
  },

  // Two names for one test system: every row with "from" now says "to", and
  // "from" in a later Excel import is read as "to" too.
  'device/merge'(s, { from, to }) {
    const target = cleanName(to, '');
    if (!target || !from || foldName(from) === foldName(target)) throw new Error('Birleştirilecek iki farklı cihaz seç');
    let rows = 0;
    for (const inv of s.invoices) {
      for (const r of inv.rows) {
        if (r.device && foldName(r.device) === foldName(from)) {
          r.device = target;
          rows++;
        }
      }
    }
    const source = s.devices.find((d) => foldName(d.name) === foldName(from));
    if (!findNamed(s.devices.map((d) => d.name), target)) s.devices.push({ name: target, test: source?.test ?? '' });
    s.devices = s.devices.filter((d) => foldName(d.name) !== foldName(from));
    for (const a of s.deviceAliases) if (foldName(a.to) === foldName(from)) a.to = target;
    s.deviceAliases = normalizeAliases([...s.deviceAliases, { from, to: target }]);
    return { rows };
  },

  // Puts test system names right after 1.18.0, once: every merge it made is
  // undone, each row getting back the name it had in `before` (rows:
  // {"invoiceId:rowId": name}, devices: the list then; from the newest
  // backup made before 1.18.0), and then the names of one system go under
  // its main name (devices.js similarGroups: "70kN Elektrodinamik Sarsıcı"
  // → "70kN Sarsıcı", any 50kN shaker → "49kN Sarsıcı"). Only data 1.18.0
  // touched; elsewhere such names stay suggestions. Returns what came back
  // ([{ from, to, rows }]), the rows that had no earlier name, and what was
  // merged ([{ to, from, rows }]).
  'device/repair-names'(s, { before = null } = {}) {
    s.settings.devicesNamesFixed = true;
    if (!s.settings.devicesMergedByName) return { restored: [], unknown: 0, merged: [] };
    const undone = merges118(s);
    const restored = undone.map((a) => ({ from: a.from, to: a.to, rows: 0 }));
    const targets = new Set(undone.map((a) => foldName(a.to)));
    let unknown = 0;
    for (const inv of s.invoices) {
      for (const r of inv.rows) {
        if (!r.device || !targets.has(foldName(r.device))) continue;
        const was = before?.rows?.[`${inv.id}:${r.id}`];
        const hit = was && restored.find((x) => foldName(x.from) === foldName(was) && foldName(x.to) === foldName(r.device));
        if (hit) {
          r.device = hit.from;
          hit.rows++;
        } else if (foldName(was ?? '') !== foldName(r.device)) unknown++;
      }
    }
    const fromNames = new Set(undone.map((a) => foldName(a.from)));
    s.deviceAliases = s.deviceAliases.filter((a) => !fromNames.has(foldName(a.from)));
    for (const x of restored) {
      const old = before?.devices?.find((d) => foldName(d.name) === foldName(x.from));
      if ((old || x.rows) && !findNamed(s.devices.map((d) => d.name), x.from)) s.devices.push({ name: x.from, test: old?.test ?? '' });
    }
    // A name only the merge made ("Sarsıcı", "ASLI") goes once nothing uses it.
    for (const t of targets) {
      const used = s.invoices.some((i) => i.rows.some((r) => foldName(r.device) === t)) || s.deviceAliases.some((a) => foldName(a.to) === t);
      const listedBefore = before?.devices?.some((d) => foldName(d.name) === t);
      if (!used && !listedBefore) s.devices = s.devices.filter((d) => foldName(d.name) !== t);
    }
    const merged = [];
    for (const g of similarGroups(s)) {
      let rows = 0;
      for (const from of g.from) rows += handlers['device/merge'](s, { from, to: g.to }).rows;
      merged.push({ to: g.to, from: g.from, rows });
    }
    return { restored: restored.filter((x) => x.rows), unknown, merged };
  },

  'device/unalias'(s, { from }) {
    s.deviceAliases = s.deviceAliases.filter((a) => foldName(a.from) !== foldName(from));
  },

  // ---- stages (Aşama): added, renamed, removed and reordered by the user

  'stage/add'(s, { name }) {
    const clean = cleanName(name, '');
    if (!clean) throw new Error('Aşama adı boş olamaz');
    if (findNamed(s.stages.map((st) => st.name), clean)) throw new Error('Bu aşama zaten var');
    if (s.stages.length >= MAX_STAGES) throw new Error(`En fazla ${MAX_STAGES} aşama olabilir`);
    const stage = { id: uid('st'), name: clean };
    s.stages.push(stage);
    return stage.id;
  },

  'stage/rename'(s, { id, name }) {
    const stage = s.stages.find((st) => st.id === id);
    if (!stage) throw new Error('Aşama bulunamadı');
    const clean = cleanName(name, '');
    if (!clean) throw new Error('Aşama adı boş olamaz');
    if (s.stages.some((st) => st.id !== id && foldName(st.name) === foldName(clean))) throw new Error('Bu aşama zaten var');
    stage.name = clean;
  },

  // Reports in a removed stage are left with none.
  'stage/delete'(s, { id }) {
    if (!s.stages.some((st) => st.id === id)) throw new Error('Aşama bulunamadı');
    s.stages = s.stages.filter((st) => st.id !== id);
    let tasks = 0;
    for (const t of s.tasks) {
      if (t.stage === id) {
        t.stage = null;
        tasks++;
      }
    }
    return { tasks };
  },

  'stage/reorder'(s, { ids = [] }) {
    if (ids.length !== s.stages.length || !s.stages.every((st) => ids.includes(st.id))) throw new Error('Aşama sırası geçersiz');
    s.stages = ids.map((id) => s.stages.find((st) => st.id === id));
  },

  // Undo for a removal: the stages as they were and the reports that had it.
  'stage/restore'(s, { stages, assignments = [] }) {
    s.stages = normalizeStages(stages);
    for (const [taskId, stage] of assignments) {
      const t = s.tasks.find((x) => x.id === taskId);
      if (t && isStage(s, stage)) t.stage = stage;
    }
  },

  'device/add'(s, { name, test = '' }) {
    const clean = cleanName(name, '');
    if (!clean) throw new Error('Cihaz adı boş olamaz');
    if (findNamed(s.devices.map((d) => d.name), clean)) throw new Error('Bu cihaz zaten var');
    s.devices.push({ name: clean, test: str(test, '').trim() });
    return clean;
  },

  'device/remove'(s, { name }) {
    s.devices = s.devices.filter((d) => foldName(d.name) !== foldName(name));
  },

  'testtype/add'(s, { name }) {
    const clean = cleanName(name, '');
    if (!clean) throw new Error('Test adı boş olamaz');
    if (findNamed(s.testTypes, clean)) throw new Error('Bu test türü zaten var');
    s.testTypes.push(clean);
    return clean;
  },

  'testtype/remove'(s, { name }) {
    s.testTypes = s.testTypes.filter((t) => foldName(t) !== foldName(name));
  },
};

// The card an imported project belongs to: the same project code; without a
// code, the same No and title.
export function findInvoiceMatch(state, project, skip = new Set()) {
  const code = foldName(project.code ?? '');
  const free = state.invoices.filter((i) => !skip.has(i.id));
  if (!code) return free.find((i) => project.no && i.no === project.no && foldName(i.title) === foldName(project.title ?? '')) ?? null;
  // The code names the project; the No only picks between two blocks of it.
  const same = free.filter((i) => foldName(i.code) === code);
  return same.find((i) => i.no === project.no) ?? same[0] ?? null;
}

function mustInvoice(state, id) {
  const inv = state.invoices.find((i) => i.id === id);
  if (!inv) throw new Error('Proje bulunamadı');
  return inv;
}

function defaultTest(state, device) {
  return state.devices.find((d) => foldName(d.name) === foldName(device ?? ''))?.test ?? '';
}

const sortedByDate = (rows) => [...rows].sort((a, b) => (a.start ?? '9999').localeCompare(b.start ?? '9999'));

function rootSection(srcByRef, t) {
  let root = t;
  while (root.parentRef && srcByRef.has(root.parentRef)) root = srcByRef.get(root.parentRef);
  return root.sectionName;
}

export function dispatch(state, action) {
  const fn = handlers[action?.type];
  if (!fn) throw new Error(`Bilinmeyen işlem: ${action?.type}`);
  return fn(state, action) ?? null;
}
