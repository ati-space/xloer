// Renders rows from shared/tree-rows.js and turns clicks, double-clicks,
// right-clicks and drops into callbacks. Used by the main window and the widget.

import { icon } from './icons.js';
import { esc, dueLabel, tagFill } from './ui.js';
import { highlightRanges } from '../../shared/search.js';
import { attachDnd } from './dnd.js';

function marked(text, terms) {
  if (!terms?.length) return esc(text);
  let out = '';
  let at = 0;
  for (const [s, e] of highlightRanges(text, terms)) {
    out += `${esc(text.slice(at, s))}<mark>${esc(text.slice(s, e))}</mark>`;
    at = e;
  }
  return out + esc(text.slice(at));
}

export class TaskTree {
  // opts: scroller, subtreeHeight(id), openButton, onSelect(row), onAction(act, row, event),
  // onContextMenu(row, x, y), onRename(row, text, info), onRenameCancel(row, info), onMove(action, info)
  constructor(el, opts) {
    this.el = el;
    this.opts = opts;
    this.rows = [];
    this.view = {};
    this.pending = null;
    this.editing = null;
    this.editWhen = null;
    this.dragging = false;
    this.selectedId = null;
    el.classList.add('tree');
    el.setAttribute('role', 'tree');
    el.addEventListener('click', (e) => this.onClick(e));
    el.addEventListener('dblclick', (e) => this.onDblClick(e));
    el.addEventListener('contextmenu', (e) => this.onContextMenu(e));
    attachDnd(this, {
      scroller: opts.scroller,
      getMode: () => this.view.dnd ?? 'none',
      onDrop: (row, target) => this.drop(row, target),
    });
  }

  // Rendering waits while a title is being edited or a row is being dragged.
  setRows(rows, view = {}) {
    this.pending = { rows, view };
    if (!this.editing && !this.dragging) this.flush();
  }

  flush() {
    if (!this.pending) return;
    ({ rows: this.rows, view: this.view } = this.pending);
    this.pending = null;
    if ('selectedId' in this.view) this.selectedId = this.view.selectedId;
    // view.tags: the tags with their colours, for the coloured chips.
    this.tagColors = new Map((this.view.tags ?? []).map((g) => [g.name.toLocaleLowerCase('tr'), g.color]));
    this.el.classList.toggle('has-sections', this.rows.some((r) => r.kind === 'section'));
    this.el.classList.toggle('searching', !!this.view.search);
    this.el.innerHTML = this.rows.map((r, i) => this.rowHtml(r, i)).join('');
    const want = this.editWhen;
    if (want && this.rows.some((r) => r.id === want.id)) {
      this.editWhen = null;
      this.startEdit(want.id, want);
    }
  }

  rowHtml(r, i) {
    const v = this.view;
    if (r.kind === 'group') {
      return `<div class="row group${r.tone ? ` tone-${r.tone}` : ''}" data-index="${i}" data-kind="group" data-id="${esc(r.id)}"><span class="title">${esc(r.label)}</span><span class="count">${r.count ?? ''}</span></div>`;
    }
    if (r.kind === 'section') {
      const tools = v.search
        ? ''
        : `<span class="row-tools"><button type="button" class="icon-btn" data-act="add-in-section" title="Bu bölüme görev ekle">${icon('plus', 15)}</button><button type="button" class="icon-btn" data-act="section-menu" title="Bölüm seçenekleri">${icon('more', 15)}</button></span>`;
      return `<div class="row section${r.collapsed ? ' collapsed' : ''}" data-index="${i}" data-kind="section" data-id="${esc(r.id)}"${v.dnd === 'tree' ? ' data-drag' : ''} role="treeitem" aria-expanded="${!r.collapsed}">
        <button type="button" class="caret${r.collapsed ? ' closed' : ''}${v.search ? ' leaf' : ''}" data-act="collapse" tabindex="-1" aria-label="${r.collapsed ? 'Bölümü aç' : 'Bölümü kapat'}">${icon('chevron', 14)}</button>
        <span class="sec-icon">${icon('section', 15)}</span>
        <span class="title">${esc(r.section.name)}</span>
        ${r.count ? `<span class="count">${r.count}</span>` : ''}${tools}
      </div>`;
    }
    const t = r.task;
    const cls = ['row', 'task', `p${t.priority}`];
    const report = v.reports && !t.parentId && !r.trash;
    if (report) cls.push('report');
    if (t.done) cls.push('done');
    if (r.context) cls.push('context');
    if (r.flat) cls.push('flat');
    if (r.id === this.selectedId) cls.push('selected');
    const meta = [];
    if (r.path) meta.push(`<span class="path" title="${esc(r.path)}">${esc(r.path)}</span>`);
    for (const g of t.tags) meta.push(`<span class="tag-chip"${tagFill(this.tagColors.get(g.toLocaleLowerCase('tr')))}>#${esc(g)}</span>`);
    const due = dueLabel(t.due, t.done, t.dueTime);
    if (due) meta.push(`<span class="due ${due.tone}">${esc(due.text)}</span>`);
    if (r.progress.total) meta.push(`<span class="progress" title="Alt görevler">${icon('subtasks', 13)}${r.progress.done}/${r.progress.total}</span>`);
    if (t.notes.trim()) meta.push(`<span class="has-notes" title="Açıklama var">${icon('notes', 13)}</span>`);
    const title = t.title ? marked(t.title, v.terms) : '<span class="untitled">Başlıksız</span>';
    const snip = r.snippet ? `<span class="snippet">${r.snippet.prefix ? '…' : ''}${marked(r.snippet.text, v.terms)}${r.snippet.suffix ? '…' : ''}</span>` : '';
    const code = report && t.code ? `<span class="code">${esc(t.code)}</span>` : '';
    const stage = report ? this.pipeHtml(t, r.nextStep) : '';
    const tools = r.trash
      ? `<span class="row-tools always"><button type="button" class="icon-btn" data-act="restore" title="Geri yükle">${icon('restore', 15)}</button><button type="button" class="icon-btn" data-act="purge" title="Kalıcı olarak sil">${icon('x', 15)}</button></span>`
      : this.opts.openButton
        ? `<button type="button" class="icon-btn row-open" data-act="open" title="Xloer’de aç">${icon('external', 14)}</button>`
        : '';
    const caret = r.flat
      ? ''
      : `<button type="button" class="caret${r.childCount && !v.search ? '' : ' leaf'}${r.collapsed ? ' closed' : ''}" data-act="collapse" tabindex="-1" aria-label="Alt görevleri ${r.collapsed ? 'göster' : 'gizle'}">${icon('chevron', 14)}</button>`;
    return `<div class="${cls.join(' ')}" style="--depth:${r.depth}" data-index="${i}" data-kind="task" data-id="${esc(r.id)}"${v.dnd && v.dnd !== 'none' && !r.trash && !r.doneGroup ? ' data-drag' : ''} role="treeitem" aria-level="${r.depth + 1}" aria-selected="${r.id === this.selectedId}"${r.childCount ? ` aria-expanded="${!r.collapsed}"` : ''}>
      ${caret}<button type="button" class="check" data-act="toggle" role="checkbox" aria-checked="${t.done}" aria-label="Tamamlandı"${r.trash ? ' disabled' : ''}>${icon('checkmark', 12)}</button>
      <span class="title-wrap"><span class="title">${title}</span>${code}${snip}</span>${stage}
      <span class="meta">${meta.join('')}</span>${tools}
    </div>`;
  }

  // Stage pipeline (click a segment to set the stage) and the next open step.
  // The stages are the user's list, passed in with the rows.
  pipeHtml(t, nextStep) {
    const stages = this.view.stages ?? [];
    const next = `<span class="next" title="${esc(nextStep)}">${esc(nextStep)}</span>`;
    if (!stages.length) return next;
    const at = stages.findIndex((st) => st.id === t.stage);
    const segs = stages.map((st, i) => {
      const mark = at < 0 ? '' : i < at ? ' done' : i === at ? ' now' : '';
      return `<button type="button" class="pipe-seg${mark}" data-act="stage" data-stage="${esc(st.id)}" tabindex="-1" title="Aşama: ${esc(st.name)}" aria-label="Aşama: ${esc(st.name)}"></button>`;
    });
    const label = at < 0 ? '<span class="pipe-label unset">Aşama seç</span>' : `<span class="pipe-label">${esc(stages[at].name)}</span>`;
    return `<span class="pipe"><span class="pipe-segs">${segs.join('')}</span>${label}</span>${next}`;
  }

  rowFor(target) {
    const el = target.closest?.('.row');
    if (!el || !this.el.contains(el)) return {};
    return { el, row: this.rows[Number(el.dataset.index)] };
  }

  onClick(e) {
    if (e.target.closest('.inline-edit')) return;
    const { el, row } = this.rowFor(e.target);
    if (!row) return;
    const act = e.target.closest('[data-act]')?.dataset.act;
    if (act === 'toggle') return this.animateToggle(el, row);
    if (act) return this.opts.onAction?.(act, row, e);
    if (row.kind === 'task') {
      this.select(row.id);
      this.opts.onSelect?.(row);
    } else if (row.kind === 'section' && !this.view.search && !e.target.closest('.title')) {
      // The name itself is left for double-click renaming.
      this.opts.onAction?.('collapse', row, e);
    }
  }

  // opts.onOpenText(row, rowEl), when given, takes a task's double-click
  // (the widget shows the task's text); otherwise it renames.
  onDblClick(e) {
    const { row, el } = this.rowFor(e.target);
    if (!row || e.target.closest('[data-act]') || this.view.readonly || row.trash) return;
    if (row.kind === 'task' && this.opts.onOpenText) {
      window.getSelection()?.removeAllRanges();
      return this.opts.onOpenText(row, el);
    }
    if (row.kind === 'task' || (row.kind === 'section' && !this.view.search)) {
      window.getSelection()?.removeAllRanges();
      this.startEdit(row.id);
    }
  }

  onContextMenu(e) {
    const { row } = this.rowFor(e.target);
    if (!row || row.kind === 'group' || e.target.closest('.inline-edit')) return;
    e.preventDefault();
    if (row.kind === 'task') {
      this.select(row.id);
      this.opts.onSelect?.(row);
    }
    this.opts.onContextMenu?.(row, e.clientX, e.clientY);
  }

  // Ticks the box at once, then reports it a moment later so the tick is seen
  // before a completed task leaves the list.
  animateToggle(el, row) {
    if (row.task.done) return this.opts.onAction?.('toggle', row);
    el.classList.add('done', 'just-done');
    setTimeout(() => this.opts.onAction?.('toggle', row), 260);
  }

  select(id) {
    this.selectedId = id;
    for (const el of this.el.querySelectorAll(':scope > .row')) {
      const on = el.dataset.id === id && el.dataset.kind === 'task';
      el.classList.toggle('selected', on);
      if (el.dataset.kind === 'task') el.setAttribute('aria-selected', String(on));
    }
  }

  taskIds() {
    return this.rows.filter((r) => r.kind === 'task').map((r) => r.id);
  }

  scrollTo(id, { flash = false } = {}) {
    const el = [...this.el.querySelectorAll(':scope > .row')].find((x) => x.dataset.id === id);
    if (!el) return;
    el.scrollIntoView({ block: 'nearest' });
    if (flash) {
      el.classList.add('flash');
      setTimeout(() => el.classList.remove('flash'), 1400);
    }
  }

  editWhenReady(id, opts = {}) {
    this.editWhen = { id, ...opts };
    if (!this.pending && this.rows.some((r) => r.id === id)) {
      this.editWhen = null;
      this.startEdit(id, opts);
    }
  }

  startEdit(id, { isNew = false } = {}) {
    if (this.editing) return false;
    const el = [...this.el.querySelectorAll(':scope > .row')].find((x) => x.dataset.id === id);
    const row = el && this.rows[Number(el.dataset.index)];
    if (!row || (row.kind !== 'task' && row.kind !== 'section')) return false;
    const original = row.kind === 'section' ? row.section.name : row.task.title;
    const titleEl = el.querySelector('.title');
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'inline-edit';
    input.value = original;
    input.spellcheck = false;
    input.placeholder = row.kind === 'section' ? 'Bölüm adı' : 'Görev adı';
    titleEl.replaceWith(input);
    el.classList.add('editing');
    input.focus();
    input.select();
    el.scrollIntoView({ block: 'nearest' });
    this.editing = { id, input };

    let finished = false;
    const finish = (commit, viaEnter = false) => {
      if (finished) return;
      finished = true;
      this.editing = null;
      const text = input.value.trim();
      if (commit && text) {
        if (isNew || text !== original) this.opts.onRename?.(row, text, { isNew, viaEnter });
      } else if (isNew && !text) this.opts.onRenameCancel?.(row, { isNew });
      if (this.pending) this.flush();
      else {
        input.replaceWith(titleEl);
        if (commit && text) titleEl.textContent = text;
        el.classList.remove('editing');
      }
    };
    input.addEventListener('keydown', (e) => {
      e.stopPropagation();
      if (e.key === 'Enter') {
        e.preventDefault();
        finish(true, true);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        finish(isNew);
      }
    });
    input.addEventListener('blur', () => finish(true));
    return true;
  }

  drop(row, target) {
    let action;
    if (target.type === 'external') {
      action = { type: 'task/move', id: row.id, listId: target.listId, sectionId: null, parentId: null, beforeId: null };
    } else if (target.type === 'section') {
      action = { type: 'section/move', listId: row.listId, id: row.id, beforeId: target.beforeId };
    } else {
      action = { type: 'task/move', id: row.id, listId: row.listId, sectionId: target.sectionId, parentId: target.parentId, beforeId: target.beforeId };
    }
    this.opts.onMove?.(action, { row, target });
  }
}
