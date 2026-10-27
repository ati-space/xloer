// Small UI building blocks shared by the main window and the widget:
// context menus, dialogs, toasts, theme and date labels.

import { icon } from './icons.js';
import { addDays, todayStr } from '../../shared/store.js';

export function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

export const PRIORITY_LABELS = ['Öncelik yok', 'Düşük öncelik', 'Orta öncelik', 'Yüksek öncelik'];
export const PRIORITY_COLORS = ['var(--text-3)', 'var(--p1)', 'var(--p2)', 'var(--p3)'];

// A coloured tag fills its whole box; its text is dark or white, whichever
// reads better on that colour. Returns a style attribute, or '' for a tag
// without a colour (it keeps the plain outlined box).
const INK = ['#141516', '#ffffff'];
function luminance(hex) {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
export function inkOn(hex) {
  const l = luminance(hex);
  const contrast = (ink) => (Math.max(l, luminance(ink)) + 0.05) / (Math.min(l, luminance(ink)) + 0.05);
  return contrast(INK[0]) >= contrast(INK[1]) ? INK[0] : INK[1];
}
export function tagFill(color) {
  return /^#[0-9a-f]{6}$/i.test(color ?? '') ? ` style="--tag:${color};--tag-ink:${inkOn(color)}"` : '';
}

// ---------------------------------------------------------------- theme

let themeSetting = 'dark';
let themePalette = 'laboratuvar';
const darkQuery = matchMedia('(prefers-color-scheme: dark)');
darkQuery.addEventListener('change', () => applyTheme(themeSetting));

// Light or dark (or the system's), and the colour theme from Ayarlar.
export function applyTheme(setting, palette = themePalette) {
  themeSetting = setting;
  themePalette = palette;
  const dark = setting === 'system' ? darkQuery.matches : setting !== 'light';
  const root = document.documentElement;
  root.dataset.theme = dark ? 'dark' : 'light';
  if (palette && palette !== 'laboratuvar') root.dataset.palette = palette;
  else delete root.dataset.palette;
}

// ---------------------------------------------------------------- dates

const MONTHS_SHORT = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara'];
const DAYS_SHORT = ['Paz', 'Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt'];

// time: "HH:MM" or null. Past once the day, or that time today, has gone.
export function dueLabel(due, done = false, time = null) {
  if (!due) return null;
  const today = todayStr();
  const [y, m, d] = due.split('-').map(Number);
  let text;
  if (due === today) text = 'Bugün';
  else if (due === addDays(today, 1)) text = 'Yarın';
  else if (due === addDays(today, -1)) text = 'Dün';
  else if (due > today && due <= addDays(today, 6)) text = DAYS_SHORT[new Date(y, m - 1, d).getDay()];
  else text = `${d} ${MONTHS_SHORT[m - 1]}${String(y) === today.slice(0, 4) ? '' : ` ${y}`}`;
  if (time) text += ` ${time}`;
  const now = new Date();
  const past = due < today || (due === today && !!time && time <= `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`);
  const tone = done ? '' : past ? 'overdue' : due === today ? 'today' : '';
  return { text, tone };
}

export function formatDateTime(ms) {
  if (!ms) return '';
  const d = new Date(ms);
  return `${d.getDate()} ${MONTHS_SHORT[d.getMonth()]} ${d.getFullYear()} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

// ---------------------------------------------------------------- menus

const openMenus = [];

export function closeMenus(fromLevel = 0) {
  while (openMenus.length > fromLevel) openMenus.pop().remove();
}

// items: {label, icon?, emoji?, color?, fill?, hint?, checked?, danger?, disabled?, action?, items?} | 'sep'
// fill: the label sits in a box of that colour (tag colours); null for an outlined one.
export function showMenu(x, y, items, level = 0) {
  closeMenus(level);
  const menu = document.createElement('div');
  menu.className = 'menu';
  menu.setAttribute('role', 'menu');
  for (const it of items) {
    if (!it) continue;
    if (it === 'sep') {
      menu.append(Object.assign(document.createElement('div'), { className: 'menu-sep' }));
      continue;
    }
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `menu-item${it.danger ? ' danger' : ''}${'fill' in it ? ' swatch' : ''}`;
    b.disabled = !!it.disabled;
    b.setAttribute('role', 'menuitem');
    const lead = it.icon ? icon(it.icon) : it.emoji ? `<span class="mi-emoji">${esc(it.emoji)}</span>` : '';
    const label = 'fill' in it ? `<span class="mi-label mi-fill"${tagFill(it.fill)}>${esc(it.label)}</span>` : `<span class="mi-label">${esc(it.label)}</span>`;
    b.innerHTML = `${'fill' in it ? '' : `<span class="mi-icon">${lead}</span>`}${label}${it.hint ? `<span class="mi-hint">${esc(it.hint)}</span>` : ''}${it.checked ? `<span class="mi-check">${icon('checkmark', 14)}</span>` : ''}${it.items ? `<span class="mi-sub">${icon('chevronRight', 14)}</span>` : ''}`;
    if (it.color) b.style.setProperty('--mi-color', it.color);
    if (it.items) {
      const open = () => {
        const r = b.getBoundingClientRect();
        const sub = showMenu(r.right - 2, r.top - 5, it.items, level + 1);
        if (r.right + sub.offsetWidth > innerWidth - 4) sub.style.left = `${Math.max(4, r.left - sub.offsetWidth + 2)}px`;
      };
      b.addEventListener('mouseenter', open);
      b.addEventListener('click', open);
    } else {
      b.addEventListener('mouseenter', () => closeMenus(level + 1));
      b.addEventListener('click', () => {
        closeMenus();
        it.action?.();
      });
    }
    menu.append(b);
  }
  document.body.append(menu);
  const w = menu.offsetWidth;
  const h = menu.offsetHeight;
  menu.style.left = `${Math.max(4, Math.min(x, innerWidth - w - 4))}px`;
  menu.style.top = `${Math.max(4, Math.min(y, innerHeight - h - 4))}px`;
  openMenus.push(menu);
  return menu;
}

export function menuAt(el, items) {
  const r = el.getBoundingClientRect();
  return showMenu(r.left, r.bottom + 4, items);
}

addEventListener(
  'pointerdown',
  (e) => {
    if (openMenus.length && !e.target.closest('.menu')) closeMenus();
  },
  true,
);
addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && openMenus.length) {
    e.stopPropagation();
    closeMenus();
  }
}, true);
addEventListener('blur', () => closeMenus());

// ---------------------------------------------------------------- dialogs

// Resolves with the clicked button's value, or null when dismissed.
export function modal({ title, body = '', buttons = [], onOpen, className = '' }) {
  return new Promise((resolve) => {
    closeMenus();
    const back = document.createElement('div');
    back.className = 'modal-back';
    back.innerHTML = `<div class="modal ${className}" role="dialog" aria-modal="true" aria-label="${esc(title)}">
      <div class="modal-head"><h2>${esc(title)}</h2><button type="button" class="icon-btn" data-close aria-label="Kapat">${icon('x')}</button></div>
      <div class="modal-body"></div>
      ${buttons.length ? '<div class="modal-foot"></div>' : ''}
    </div>`;
    const box = back.querySelector('.modal');
    const bodyEl = back.querySelector('.modal-body');
    if (typeof body === 'string') bodyEl.innerHTML = body;
    else bodyEl.append(body);
    let finished = false;
    const finish = (value) => {
      if (finished) return;
      finished = true;
      removeEventListener('keydown', onKey, true);
      back.remove();
      resolve(value);
    };
    const foot = back.querySelector('.modal-foot');
    for (const btn of buttons) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = `btn${btn.primary ? ' primary' : ''}${btn.danger ? ' danger' : ''}`;
      b.textContent = btn.label;
      b.addEventListener('click', () => finish(typeof btn.value === 'function' ? btn.value(box) : btn.value));
      foot.append(b);
    }
    const onKey = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        finish(null);
      } else if (e.key === 'Enter' && e.target.tagName === 'INPUT') {
        const primary = buttons.find((b) => b.primary);
        if (primary) {
          e.preventDefault();
          finish(typeof primary.value === 'function' ? primary.value(box) : primary.value);
        }
      }
    };
    addEventListener('keydown', onKey, true);
    back.addEventListener('pointerdown', (e) => {
      if (e.target === back) finish(null);
    });
    back.querySelector('[data-close]').addEventListener('click', () => finish(null));
    document.body.append(back);
    onOpen?.(box, finish);
    (box.querySelector('[autofocus]') ?? box.querySelector('input, .btn.primary'))?.focus();
  });
}

const EMOJIS = ['💼', '📁', '📋', '📊', '📈', '🧪', '🔬', '⚙️', '🛠️', '🏭', '🚚', '💡', '📌', '⭐', '🔥', '✅', '📝', '📅', '💰', '🧾', '🏠', '🛒', '❤️', '🎯', '📦', '✈️', '📚', '🔧'];

export async function promptModal({ title, label = '', value = '', placeholder = '', okLabel = 'Kaydet', emoji = null }) {
  const body = document.createElement('div');
  body.innerHTML = `${label ? `<label class="field-label">${esc(label)}</label>` : ''}
    <div class="prompt-row">${emoji ? `<button type="button" class="emoji-btn" title="Simge seç">${esc(emoji)}</button>` : ''}<input class="input" type="text" autofocus value="${esc(value)}" placeholder="${esc(placeholder)}" spellcheck="false"></div>
    ${emoji ? `<div class="emoji-grid" hidden>${EMOJIS.map((e) => `<button type="button" data-emoji="${e}">${e}</button>`).join('')}</div>` : ''}`;
  let chosen = emoji;
  const result = await modal({
    title,
    body,
    buttons: [
      { label: 'Vazgeç', value: null },
      { label: okLabel, primary: true, value: (box) => ({ text: box.querySelector('.input').value.trim(), emoji: chosen }) },
    ],
    onOpen(box) {
      const input = box.querySelector('.input');
      input.select();
      const grid = box.querySelector('.emoji-grid');
      const btn = box.querySelector('.emoji-btn');
      btn?.addEventListener('click', () => (grid.hidden = !grid.hidden));
      grid?.addEventListener('click', (e) => {
        const b = e.target.closest('[data-emoji]');
        if (!b) return;
        chosen = b.dataset.emoji;
        btn.textContent = chosen;
        grid.hidden = true;
        input.focus();
      });
    },
  });
  if (!result || !result.text) return null;
  return emoji ? result : result.text;
}

export function confirmModal({ title, message, okLabel = 'Tamam', danger = false }) {
  return modal({
    title,
    body: `<p class="modal-text">${esc(message)}</p>`,
    buttons: [
      { label: 'Vazgeç', value: false },
      { label: okLabel, value: true, primary: true, danger },
    ],
  }).then((v) => v === true);
}

// ---------------------------------------------------------------- toasts

export function toast(message, { action, error = false, timeout = 4500 } = {}) {
  let host = document.querySelector('.toasts');
  if (!host) {
    host = document.createElement('div');
    host.className = 'toasts';
    host.setAttribute('aria-live', 'polite');
    document.body.append(host);
  }
  const t = document.createElement('div');
  t.className = `toast${error ? ' error' : ''}`;
  t.innerHTML = `<span>${esc(message)}</span>`;
  if (action) {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = action.label;
    b.addEventListener('click', () => {
      t.remove();
      action.run();
    });
    t.append(b);
  }
  host.append(t);
  setTimeout(() => t.remove(), timeout);
}
