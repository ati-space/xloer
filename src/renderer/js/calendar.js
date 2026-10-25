// The Turkish calendar every date field opens: Monday-first weeks, Turkish
// month and day names. It replaces the system date picker, which follows the
// Windows language and so could come up in English.
//
// Clicks inside it never take focus from the field, so a half-typed date can
// still be finished; ↓ from the field moves into the days, where the arrow
// keys, Page Up/Down (month, with Shift a year) and Enter work. Esc closes.

import { TR_MONTHS, TR_WEEKDAYS, calendarWeeks, formatTrDate } from '../../shared/dates.js';
import { todayStr } from '../../shared/store.js';

let current = null;

export function closeCalendar() {
  current?.close();
}

export const calendarAnchor = () => current?.anchor ?? null;

// Follows what is being typed into the open calendar's field.
export function updateCalendar(iso) {
  current?.setValue(iso);
}

// anchor: the field or button it sits under. value / min / rangeStart: ISO
// dates (rangeStart marks the start of a range on an end-date field).
// onPick(iso), or onPick(null) from "Temizle".
export function openCalendar(anchor, { value = null, min = null, rangeStart = null, clearable = true, onPick, onClose }) {
  closeCalendar();
  const today = todayStr();
  let focus = value ?? (min && min > today ? min : today);
  let year = Number(focus.slice(0, 4));
  let month = Number(focus.slice(5, 7));

  const el = document.createElement('div');
  el.className = 'cal';
  el.setAttribute('role', 'dialog');
  el.setAttribute('aria-label', 'Tarih seç');

  function dayClass(c) {
    const cls = ['cal-day'];
    if (!c.inMonth) cls.push('out');
    if (c.iso === today) cls.push('today');
    if (c.iso === value) cls.push('sel');
    if (rangeStart && c.iso === rangeStart) cls.push('start');
    if (rangeStart && value && c.iso > rangeStart && c.iso < value) cls.push('range');
    return cls.join(' ');
  }

  function draw() {
    const weeks = calendarWeeks(year, month);
    if (!weeks.flat().some((c) => c.iso === focus)) focus = weeks.flat().find((c) => c.inMonth).iso;
    el.innerHTML = `
      <div class="cal-head">
        <button type="button" class="cal-nav" data-nav="-12" title="Önceki yıl" aria-label="Önceki yıl">«</button>
        <button type="button" class="cal-nav" data-nav="-1" title="Önceki ay" aria-label="Önceki ay">‹</button>
        <b aria-live="polite">${TR_MONTHS[month - 1]} ${year}</b>
        <button type="button" class="cal-nav" data-nav="1" title="Sonraki ay" aria-label="Sonraki ay">›</button>
        <button type="button" class="cal-nav" data-nav="12" title="Sonraki yıl" aria-label="Sonraki yıl">»</button>
      </div>
      <div class="cal-grid" role="grid">
        <div class="cal-row cal-week" role="row">${TR_WEEKDAYS.map((d, i) => `<span role="columnheader" class="${i > 4 ? 'we' : ''}">${d}</span>`).join('')}</div>
        ${weeks
          .map(
            (w) =>
              `<div class="cal-row" role="row">${w
                .map(
                  (c) =>
                    `<button type="button" role="gridcell" class="${dayClass(c)}" data-iso="${c.iso}" tabindex="${c.iso === focus ? 0 : -1}" aria-label="${formatTrDate(c.iso)}"${c.iso === value ? ' aria-selected="true"' : ''}${min && c.iso < min ? ' disabled' : ''}>${c.day}</button>`,
                )
                .join('')}</div>`,
          )
          .join('')}
      </div>
      <div class="cal-foot">
        <button type="button" class="link-btn" data-today${min && today < min ? ' disabled' : ''}>Bugün</button>
        ${clearable ? '<button type="button" class="link-btn quiet" data-clear>Temizle</button>' : ''}
      </div>`;
  }

  function place() {
    const r = anchor.getBoundingClientRect();
    const w = el.offsetWidth;
    const h = el.offsetHeight;
    const below = r.bottom + 4 + h <= innerHeight - 8 || r.top - 4 - h < 8;
    el.style.left = `${Math.max(8, Math.min(r.left, innerWidth - w - 8))}px`;
    el.style.top = `${below ? r.bottom + 4 : r.top - 4 - h}px`;
  }

  function show(iso) {
    focus = iso;
    year = Number(iso.slice(0, 4));
    month = Number(iso.slice(5, 7));
    draw();
  }

  function focusDay() {
    el.querySelector(`[data-iso="${focus}"]`)?.focus();
  }

  function pick(iso) {
    close();
    onPick?.(iso);
  }

  function step(days) {
    const d = new Date(`${focus}T00:00:00Z`);
    d.setUTCDate(d.getUTCDate() + days);
    show(d.toISOString().slice(0, 10));
    focusDay();
  }

  function moveMonth(n) {
    const d = new Date(Date.UTC(year, month - 1 + n, 1));
    const day = Math.min(Number(focus.slice(8, 10)), new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0)).getUTCDate());
    return `${d.toISOString().slice(0, 8)}${String(day).padStart(2, '0')}`;
  }

  el.addEventListener('pointerdown', (e) => e.preventDefault());
  el.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b || b.disabled) return;
    if (b.dataset.nav) {
      show(moveMonth(Number(b.dataset.nav)));
      place();
    } else if (b.dataset.iso) pick(b.dataset.iso);
    else if ('today' in b.dataset) pick(today);
    else if ('clear' in b.dataset) pick(null);
  });
  el.addEventListener('keydown', (e) => {
    const keys = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    if (e.key in keys) step(keys[e.key]);
    else if (e.key === 'PageUp' || e.key === 'PageDown') {
      show(moveMonth((e.key === 'PageUp' ? -1 : 1) * (e.shiftKey ? 12 : 1)));
      focusDay();
    } else return;
    e.preventDefault();
    e.stopPropagation();
  });

  const outside = (e) => {
    if (!el.contains(e.target) && !anchor.contains(e.target)) close();
  };
  // Scrolling (also the scroll that brings a focused field into view) moves
  // the calendar with its field; it closes once the field is gone.
  const onScroll = (e) => {
    if (el.contains(e.target)) return;
    if (anchor.isConnected) place();
    else close();
  };
  // Esc closes the calendar before anything else sees the key.
  const onKey = (e) => {
    if (e.key !== 'Escape') return;
    e.preventDefault();
    e.stopPropagation();
    const inside = el.contains(document.activeElement);
    close();
    if (inside) anchor.focus();
  };
  document.addEventListener('pointerdown', outside, true);
  document.addEventListener('keydown', onKey, true);
  addEventListener('scroll', onScroll, true);
  addEventListener('resize', closeCalendar);

  function close() {
    if (current?.el !== el) return;
    current = null;
    el.remove();
    document.removeEventListener('pointerdown', outside, true);
    document.removeEventListener('keydown', onKey, true);
    removeEventListener('scroll', onScroll, true);
    removeEventListener('resize', closeCalendar);
    onClose?.();
  }

  draw();
  document.body.append(el);
  place();
  current = {
    el,
    anchor,
    close,
    // Follows what is being typed into the field.
    setValue(iso) {
      value = iso;
      if (iso) show(iso);
      else draw();
      place();
    },
    focusDay,
  };
  return current;
}
