// The widget's text card: double-clicking a task shows its whole title and
// description in a small card over the list, so long text can be read
// without opening Xloer; the widget keeps its size. Both fields can be
// edited there and are saved as they are typed. Esc or a click outside
// closes it.

import * as A from './actions.js';
import { esc } from './ui.js';

const SAVE_DELAY = 400;
let current = null;

export function closeTextCard() {
  current?.close();
}

// The task the card shows, so the widget can close it once the task is gone.
export const textCardTaskId = () => current?.id ?? null;

// anchor: the task's row; the card opens over it, inside the window.
// onOpen(id): the "Xloer’de aç" link.
export function openTextCard(task, { anchor, onOpen }) {
  closeTextCard();
  const back = document.createElement('div');
  back.className = 'text-card-back';
  const card = document.createElement('div');
  card.className = 'text-card';
  card.setAttribute('role', 'dialog');
  card.setAttribute('aria-label', 'Görev metni');
  card.innerHTML = `
    <textarea class="tc-title" rows="1" spellcheck="false" aria-label="Başlık" placeholder="Başlıksız">${esc(task.title)}</textarea>
    <textarea class="tc-notes scroll" spellcheck="false" aria-label="Açıklama" placeholder="Açıklama ekle…">${esc(task.notes)}</textarea>
    <div class="tc-foot"><span>Esc · kapat</span><button type="button" class="link-btn" data-open>Xloer’de aç</button></div>`;
  back.append(card);
  document.body.append(back);

  const title = card.querySelector('.tc-title');
  const notes = card.querySelector('.tc-notes');
  const saved = { title: task.title, notes: task.notes };
  let timer = null;

  function save() {
    clearTimeout(timer);
    const patch = {};
    // A title is one line; a pasted line break becomes a space.
    const t = title.value.replace(/\s*\n\s*/g, ' ');
    if (t !== saved.title) patch.title = saved.title = t;
    if (notes.value !== saved.notes) patch.notes = saved.notes = notes.value;
    if (Object.keys(patch).length) A.run({ type: 'task/update', id: task.id, patch });
  }

  // Both fields grow with their text; the card stays within 70% of the
  // widget (the list shows around it) and the description scrolls.
  function fit() {
    title.style.height = 'auto';
    title.style.height = `${title.scrollHeight}px`;
    const room = Math.round(innerHeight * 0.7) - (card.offsetHeight - notes.offsetHeight);
    notes.style.height = 'auto';
    notes.style.height = `${Math.max(72, Math.min(notes.scrollHeight + 2, room))}px`;
    place();
  }

  // Over the row when there is room, otherwise as low as fits.
  function place() {
    const top = anchor?.isConnected ? anchor.getBoundingClientRect().top : 56;
    card.style.top = `${Math.max(8, Math.min(top, innerHeight - card.offsetHeight - 8))}px`;
  }

  const onInput = () => {
    fit();
    clearTimeout(timer);
    timer = setTimeout(save, SAVE_DELAY);
  };
  title.addEventListener('input', onInput);
  notes.addEventListener('input', onInput);
  title.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    e.preventDefault();
    notes.focus();
  });
  back.addEventListener('pointerdown', (e) => {
    if (e.target === back) close();
  });
  card.querySelector('[data-open]').addEventListener('click', () => {
    close();
    onOpen?.(task.id);
  });
  const onKey = (e) => {
    if (e.key !== 'Escape') return;
    e.preventDefault();
    e.stopPropagation();
    close();
  };
  document.addEventListener('keydown', onKey, true);
  addEventListener('resize', fit);

  function close() {
    if (current?.back !== back) return;
    current = null;
    save();
    back.remove();
    document.removeEventListener('keydown', onKey, true);
    removeEventListener('resize', fit);
  }

  current = { id: task.id, back, close };
  fit();
  notes.focus();
  notes.setSelectionRange(notes.value.length, notes.value.length);
  notes.scrollTop = 0;
}
