// "Yenilikler": every version and what it brought, newest first. The rail
// button shows a dot until the window has been opened on this version.

import { esc, modal } from './ui.js';
import { CHANGELOG, KINDS } from '../../shared/changelog.js';
import { TR_MONTHS } from '../../shared/dates.js';

const SEEN_KEY = 'xloer-seen-version';

const longDate = (iso) => {
  const [y, m, d] = iso.split('-').map(Number);
  return `${d} ${TR_MONTHS[m - 1]} ${y}`;
};

export function changelogUnseen(version) {
  try {
    return localStorage.getItem(SEEN_KEY) !== version;
  } catch {
    return false;
  }
}

export function openChangelog(version, { onSeen } = {}) {
  try {
    localStorage.setItem(SEEN_KEY, version);
  } catch {}
  onSeen?.();
  const body = document.createElement('div');
  body.className = 'changelog';
  body.innerHTML = CHANGELOG.map(
    (e) => `<section class="cl-ver${e.version === version ? ' current' : ''}">
      <header class="cl-head">
        <b class="cl-num">v${esc(e.version)}</b>
        <h3>${esc(e.title)}</h3>
        ${e.version === version ? '<span class="cl-now">Kullandığın sürüm</span>' : ''}
        <span class="flex"></span>
        <time datetime="${e.date}">${longDate(e.date)}</time>
      </header>
      ${e.note ? `<p class="cl-note">${esc(e.note)}</p>` : ''}
      <ul>${e.items.map(([kind, text]) => `<li><span class="cl-kind ${kind}">${KINDS[kind]}</span><span>${esc(text)}</span></li>`).join('')}</ul>
    </section>`,
  ).join('');
  return modal({ title: 'Yenilikler', body, className: 'wide changelog-modal' });
}
