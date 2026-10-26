// "Aşamaları düzenle": add, rename, reorder and remove the report stages.
// Every change is saved at once; the list redraws as the state changes,
// except while a name is being typed.

import { icon } from './icons.js';
import * as A from './actions.js';
import { confirmModal, esc, modal, toast } from './ui.js';

let box = null;
let state = null;
let deferred = false;

// Only a stage name being edited holds the redraw back (not the “new stage” field).
const typing = () => box?.contains(document.activeElement) && document.activeElement.matches('[data-name]');

function reportCounts() {
  const n = new Map();
  for (const t of state.tasks) if (!t.deletedAt && !t.parentId && t.stage) n.set(t.stage, (n.get(t.stage) ?? 0) + 1);
  return n;
}

export function renderStagesDialog(next) {
  if (next) state = next;
  if (!box || !state) return;
  if (typing()) {
    deferred = true;
    return;
  }
  deferred = false;
  const n = reportCounts();
  const last = state.stages.length - 1;
  box.querySelector('.stage-list').innerHTML =
    state.stages
      .map(
        (st, i) => `<li class="stage-row" data-id="${esc(st.id)}">
        <span class="stage-no">${i + 1}</span>
        <input class="input" data-name value="${esc(st.name)}" spellcheck="false" aria-label="${i + 1}. aşamanın adı">
        <span class="muted small stage-count">${n.get(st.id) ? `${n.get(st.id)} rapor` : ''}</span>
        <button type="button" class="icon-btn small up" data-move="-1" title="Yukarı taşı" aria-label="Yukarı taşı"${i === 0 ? ' disabled' : ''}>${icon('chevron', 14)}</button>
        <button type="button" class="icon-btn small" data-move="1" title="Aşağı taşı" aria-label="Aşağı taşı"${i === last ? ' disabled' : ''}>${icon('chevron', 14)}</button>
        <button type="button" class="icon-btn small" data-del title="Aşamayı sil" aria-label="Aşamayı sil">${icon('x', 13)}</button>
      </li>`,
      )
      .join('') || '<li class="muted small">Aşama yok. Aşağıdan ekle.</li>';
}

async function rename(input) {
  const id = input.closest('[data-id]').dataset.id;
  const stage = state.stages.find((st) => st.id === id);
  if (!stage || input.value.trim() === stage.name) return;
  if (!(await A.run({ type: 'stage/rename', id, name: input.value }))) input.value = stage.name;
}

function move(id, step) {
  const ids = state.stages.map((st) => st.id);
  const i = ids.indexOf(id);
  const j = i + step;
  if (i < 0 || j < 0 || j >= ids.length) return;
  [ids[i], ids[j]] = [ids[j], ids[i]];
  return A.run({ type: 'stage/reorder', ids });
}

async function remove(id) {
  const stage = state.stages.find((st) => st.id === id);
  if (!stage) return;
  const used = state.tasks.filter((t) => t.stage === id).map((t) => [t.id, t.stage]);
  if (used.length) {
    const ok = await confirmModal({
      title: `“${stage.name}” silinsin mi?`,
      message: `Bu aşamadaki ${used.length} raporun aşaması boşalır. Bildirimdeki “Geri al” hepsini geri getirir.`,
      okLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
  }
  const before = { stages: structuredClone(state.stages), assignments: used };
  if (await A.run({ type: 'stage/delete', id })) {
    toast(`“${stage.name}” silindi`, { timeout: 9000, action: { label: 'Geri al', run: () => A.run({ type: 'stage/restore', ...before }) } });
  }
}

export function openStagesDialog(current) {
  state = current;
  const body = document.createElement('div');
  body.className = 'stages';
  body.innerHTML = `<p class="hint">Raporun satırındaki çizgi bu sırayla dolar. Adını değiştirdiğin ya da yerini taşıdığın aşamadaki raporlar o aşamada kalır.</p>
    <ol class="stage-list"></ol>
    <form class="stage-add"><input class="input" data-new placeholder="Yeni aşama adı" spellcheck="false" aria-label="Yeni aşama adı"><button type="submit" class="btn">${icon('plus', 14)}Ekle</button></form>`;
  return modal({
    title: 'Aşamalar',
    body,
    onOpen(m) {
      box = body;
      renderStagesDialog();
      m.addEventListener('click', (e) => {
        const b = e.target.closest('button');
        const id = b?.closest('[data-id]')?.dataset.id;
        if (!b || !id) return;
        if (b.dataset.move) move(id, Number(b.dataset.move));
        else if ('del' in b.dataset) remove(id);
      });
      m.addEventListener('change', (e) => {
        if (e.target.matches('[data-name]')) rename(e.target);
      });
      m.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && e.target.matches('[data-name]')) {
          e.preventDefault();
          e.target.blur();
        }
      });
      m.addEventListener('focusout', () =>
        setTimeout(() => {
          if (deferred && !typing()) renderStagesDialog();
        }),
      );
      body.querySelector('.stage-add').addEventListener('submit', async (e) => {
        e.preventDefault();
        const input = body.querySelector('[data-new]');
        if (!input.value.trim()) return input.focus();
        if (await A.run({ type: 'stage/add', name: input.value })) {
          input.value = '';
          input.focus();
        }
      });
    },
  }).then(() => {
    box = null;
  });
}
