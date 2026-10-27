// Pointer-driven drag and drop for a TaskTree.
//
// Dragging a task carries its subtasks. Between rows a line shows where it
// lands; its indent follows the pointer (drag right = deeper, left = shallower).
// Over the middle of a row the row lights up: the task becomes its subtask.
// Dropping on a sidebar list ([data-drop-list]) moves it to that list.
// Dragging a section header moves the whole section between sections.

import { projectTaskDrop, sectionDropIndex } from '../../shared/tree-rows.js';

const THRESHOLD = 5;
const EDGE = 36;

export function attachDnd(tree, { scroller, getMode, onDrop }) {
  let press = null;
  let drag = null;

  tree.el.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || getMode() === 'none' || tree.editing) return;
    if (e.target.closest('input, textarea, button, a')) return;
    const rowEl = e.target.closest('.row[data-drag]');
    if (!rowEl) return;
    press = { x: e.clientX, y: e.clientY, rowEl, pointerId: e.pointerId };
    addEventListener('pointermove', onMove, true);
    addEventListener('pointerup', onUp, true);
    addEventListener('pointercancel', teardown, true);
    addEventListener('keydown', onKey, true);
  });

  function onMove(e) {
    if (!drag) {
      if (!press || Math.hypot(e.clientX - press.x, e.clientY - press.y) < THRESHOLD) return;
      if (!begin()) return teardown();
    }
    e.preventDefault();
    drag.pointer = { x: e.clientX, y: e.clientY };
    drag.ghost.style.transform = `translate(${e.clientX - drag.offset.x}px, ${e.clientY - drag.offset.y}px)`;
    update();
  }

  function begin() {
    const { rowEl } = press;
    const rows = tree.rows;
    const i = Number(rowEl.dataset.index);
    const row = rows[i];
    if (!rowEl.isConnected || !row || (row.kind !== 'task' && row.kind !== 'section')) return false;
    const mode = getMode();
    if (row.kind === 'section' && mode !== 'tree') return false;

    let end = i + 1;
    if (row.kind === 'task') while (end < rows.length && rows[end].kind === 'task' && rows[end].depth > row.depth) end++;
    else while (end < rows.length && rows[end].kind === 'task') end++;

    const cands = [];
    const blocks = [];
    tree.el.querySelectorAll(':scope > .row').forEach((el, k) => {
      if (k >= i && k < end) {
        el.classList.add('drag-source');
        return;
      }
      // The "Tamamlanan" group at the end is not a place to drop on.
      if (rows[k].kind === 'group' || rows[k].doneGroup) return;
      const r = el.getBoundingClientRect();
      cands.push({ row: rows[k], el, top: r.top, bottom: r.bottom });
      if (rows[k].kind === 'section') blocks.push({ id: rows[k].id, el, top: r.top, bottom: r.bottom });
      else if (blocks.length) blocks[blocks.length - 1].bottom = r.bottom;
    });

    const rect = rowEl.getBoundingClientRect();
    const ghost = rowEl.cloneNode(true);
    ghost.classList.add('drag-ghost');
    ghost.classList.remove('selected', 'drag-source');
    ghost.style.width = `${Math.min(rect.width, 460)}px`;
    if (end - i > 1) ghost.insertAdjacentHTML('beforeend', `<span class="drag-count">+${end - i - 1}</span>`);
    document.body.append(ghost);

    const indicator = document.createElement('div');
    indicator.className = 'drop-indicator';
    indicator.hidden = true;
    tree.el.append(indicator);

    try {
      tree.el.setPointerCapture(press.pointerId);
    } catch {}

    const css = getComputedStyle(tree.el);
    drag = {
      row,
      mode,
      cands,
      blocks,
      ghost,
      indicator,
      start: { x: press.x, y: press.y },
      offset: { x: press.x - rect.left, y: press.y - rect.top },
      pointer: { x: press.x, y: press.y },
      scroll0: scroller.scrollTop,
      indent: parseFloat(css.getPropertyValue('--indent')) || 18,
      pad: parseFloat(css.getPropertyValue('--pad')) || 6,
      sectioned: tree.el.classList.contains('has-sections'),
      height: row.kind === 'task' ? tree.opts.subtreeHeight?.(row.id) ?? 0 : 0,
      target: null,
      hover: null,
    };
    tree.dragging = true;
    document.body.classList.add('is-dragging');
    drag.raf = requestAnimationFrame(autoScroll);
    return true;
  }

  function setHover(el, cls) {
    drag.hover?.el.classList.remove(drag.hover.cls);
    drag.hover = el ? { el, cls } : null;
    el?.classList.add(cls);
  }

  function showLine(top, left) {
    drag.indicator.hidden = false;
    drag.indicator.style.top = `${top - 1}px`;
    drag.indicator.style.left = `${left}px`;
  }

  function update() {
    const d = drag;
    const { x, y } = d.pointer;
    d.target = null;
    d.indicator.hidden = true;
    setHover(null);

    if (d.row.kind === 'task') {
      const ext = document.elementFromPoint(x, y)?.closest('[data-drop-list]');
      if (ext && !tree.el.contains(ext)) {
        if (ext.dataset.dropList !== d.row.listId) {
          d.target = { type: 'external', listId: ext.dataset.dropList };
          setHover(ext, 'drop-hover');
        }
        return;
      }
    }
    if (d.mode !== 'tree') return;
    const box = scroller.getBoundingClientRect();
    if (x < box.left - 24 || x > box.right + 24 || y < box.top - 24 || y > box.bottom + 24) return;
    // Row rects were measured at drag start; shift the pointer by the scroll since.
    const cy = y + (scroller.scrollTop - d.scroll0);

    if (d.row.kind === 'section') {
      if (!d.blocks.length) return;
      const i = sectionDropIndex(d.blocks, cy);
      d.target = { type: 'section', beforeId: d.blocks[i]?.id ?? null };
      const last = d.cands[d.cands.length - 1].el;
      showLine(d.blocks[i] ? d.blocks[i].el.offsetTop : last.offsetTop + last.offsetHeight, d.pad);
      return;
    }

    const c = d.cands;
    const k = c.findIndex((r) => cy < r.bottom);
    let hit;
    if (k === -1) hit = { gap: c.length };
    else {
      const rel = (cy - c[k].top) / (c[k].bottom - c[k].top);
      const gap = rel < 0.5 ? k : k + 1;
      hit = c[k].row.kind === 'task' && rel > 0.28 && rel < 0.72 ? { into: k, gap } : { gap };
    }
    const p = projectTaskDrop(
      c.map((r) => r.row),
      hit,
      { depth: d.row.depth, height: d.height, dx: x - d.start.x, indent: d.indent },
    );
    if (!p) return;
    d.target = { type: 'task', ...p };
    if (p.mode === 'into') return setHover(c[p.index].el, 'drop-into');
    const ref = c[p.gap]?.el;
    const last = c[c.length - 1]?.el;
    const top = ref ? ref.offsetTop : last ? last.offsetTop + last.offsetHeight : 0;
    showLine(top, d.pad + (p.depth + (d.sectioned ? 1 : 0)) * d.indent + 20);
  }

  function autoScroll() {
    if (!drag) return;
    const { x, y } = drag.pointer;
    const box = scroller.getBoundingClientRect();
    if (x >= box.left && x <= box.right) {
      let dy = 0;
      if (y < box.top + EDGE) dy = -Math.ceil((box.top + EDGE - y) / 3);
      else if (y > box.bottom - EDGE) dy = Math.ceil((y - (box.bottom - EDGE)) / 3);
      if (dy) {
        const before = scroller.scrollTop;
        scroller.scrollTop += Math.max(-24, Math.min(24, dy));
        if (scroller.scrollTop !== before) update();
      }
    }
    drag.raf = requestAnimationFrame(autoScroll);
  }

  function onUp() {
    const d = drag;
    teardown();
    if (!d) return;
    // The browser follows pointerup with a click on the row; it is not one.
    const swallow = (ev) => {
      ev.stopPropagation();
      ev.preventDefault();
    };
    addEventListener('click', swallow, true);
    setTimeout(() => removeEventListener('click', swallow, true), 0);
    if (d.target) onDrop(d.row, d.target);
  }

  function onKey(e) {
    if (e.key === 'Escape' && drag) {
      e.preventDefault();
      e.stopPropagation();
      teardown();
    }
  }

  function teardown() {
    removeEventListener('pointermove', onMove, true);
    removeEventListener('pointerup', onUp, true);
    removeEventListener('pointercancel', teardown, true);
    removeEventListener('keydown', onKey, true);
    press = null;
    if (!drag) return;
    cancelAnimationFrame(drag.raf);
    setHover(null);
    drag.ghost.remove();
    drag.indicator.remove();
    tree.el.querySelectorAll('.drag-source').forEach((el) => el.classList.remove('drag-source'));
    document.body.classList.remove('is-dragging');
    drag = null;
    tree.dragging = false;
    tree.flush();
  }
}
