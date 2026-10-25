// Reads a task list exported as CSV (one row per task, with columns such as
// "List Name", "Title", "Tags", "Due Date", "Priority", "Column Name",
// "taskId", "parentId") into the shape the 'import/merge' action takes.
// Columns are found by header name, so files with or without the optional
// columns work.

export function parseCsv(text) {
  const src = String(text ?? '').replace(/^﻿/, '');
  const rows = [];
  let row = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < src.length; i++) {
    const c = src[i];
    if (quoted) {
      if (c === '"') {
        if (src[i + 1] === '"') {
          field += '"';
          i++;
        } else quoted = false;
      } else field += c;
    } else if (c === '"') quoted = true;
    else if (c === ',') {
      row.push(field);
      field = '';
    } else if (c === '\n' || c === '\r') {
      if (c === '\r' && src[i + 1] === '\n') i++;
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
    } else field += c;
  }
  if (field !== '' || row.length) {
    row.push(field);
    rows.push(row);
  }
  return rows;
}

const PRIORITY = { 0: 0, 1: 1, 3: 2, 5: 3 };

function parseTime(s) {
  const v = String(s ?? '').trim();
  if (!v) return null;
  const ms = Date.parse(v.replace(/([+-]\d{2})(\d{2})$/, '$1:$2'));
  return Number.isFinite(ms) ? ms : null;
}

// All-day dates are exported as local midnight converted to UTC, so the
// calendar day has to be read back in the task's own time zone.
function localDate(s, timeZone) {
  const ms = parseTime(s);
  if (ms == null) return null;
  const opts = { year: 'numeric', month: '2-digit', day: '2-digit' };
  try {
    return new Intl.DateTimeFormat('en-CA', { ...opts, timeZone: timeZone || undefined }).format(ms);
  } catch {
    return new Intl.DateTimeFormat('en-CA', opts).format(ms);
  }
}

export function parseTasksCsv(text) {
  const rows = parseCsv(text);
  const norm = (v) => String(v ?? '').trim().toLowerCase();
  const h = rows.findIndex((r) => r.some((c) => norm(c) === 'list name') && r.some((c) => norm(c) === 'title'));
  if (h < 0) throw new Error('Bu dosya bir görev listesi gibi görünmüyor: "List Name" ve "Title" sütunları bulunamadı.');
  const header = rows[h].map(norm);
  const get = (r, name) => {
    const i = header.indexOf(name);
    return i >= 0 ? String(r[i] ?? '').replace(/\r\n?/g, '\n') : '';
  };

  const lists = new Map();
  let count = 0;
  rows.slice(h + 1).forEach((r, n) => {
    if (r.every((c) => !String(c).trim())) return;
    const listName = get(r, 'list name').trim() || 'Inbox';
    let list = lists.get(listName);
    if (!list) lists.set(listName, (list = { name: listName, sections: new Map(), tasks: [] }));

    const sectionName = get(r, 'column name').trim() || null;
    if (sectionName && !list.sections.has(sectionName)) {
      const order = Number.parseFloat(get(r, 'column order'));
      list.sections.set(sectionName, Number.isFinite(order) ? order : list.sections.size);
    }

    const status = get(r, 'status').trim();
    const isChecklist = /^y/i.test(get(r, 'is check list').trim()) || get(r, 'kind').trim().toUpperCase() === 'CHECKLIST';
    const items = [];
    let notes = get(r, 'content');
    if (isChecklist) {
      const keep = [];
      for (const line of notes.split('\n')) {
        const m = line.match(/^\s*([▫▪□■☐☑✓✔-])\s*(.*)$/u);
        if (m && m[2].trim()) items.push({ title: m[2].trim(), done: /[▪■☑✓✔]/u.test(m[1]) });
        else keep.push(line);
      }
      notes = keep.join('\n');
    }
    const ref = get(r, 'taskid').trim() || `row${n}`;
    const order = Number.parseFloat(get(r, 'order'));
    const tz = get(r, 'timezone').trim();
    list.tasks.push({
      ref,
      parentRef: get(r, 'parentid').trim() || null,
      sectionName,
      title: get(r, 'title').trim(),
      notes: notes.trim(),
      done: status !== '' && status !== '0',
      doneAt: parseTime(get(r, 'completed time')),
      due: localDate(get(r, 'due date') || get(r, 'start date'), tz),
      priority: PRIORITY[Number.parseInt(get(r, 'priority'), 10)] ?? 0,
      tags: get(r, 'tags')
        .split(/[,;]/)
        .map((g) => g.trim().replace(/^#/, ''))
        .filter(Boolean),
      createdAt: parseTime(get(r, 'created time')),
      order: Number.isFinite(order) ? order : n,
    });
    count++;
    items.forEach((item, k) => {
      list.tasks.push({ ref: `${ref}#${k}`, parentRef: ref, sectionName, title: item.title, notes: '', done: item.done, doneAt: null, due: null, priority: 0, tags: [], createdAt: null, order: k });
      count++;
    });
  });

  return {
    count,
    lists: [...lists.values()].map((l) => ({
      name: l.name,
      sections: [...l.sections.entries()].sort((a, b) => a[1] - b[1]).map(([name]) => name),
      tasks: l.tasks,
    })),
  };
}
