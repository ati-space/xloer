// "Geri bildirim": a short form whose text opens as a new issue on the
// public GitHub repository, filled in; sending it there needs a GitHub
// account. The version and Windows build go along unless unticked.

import { esc, modal, toast } from './ui.js';

const NEW_ISSUE = 'https://github.com/ati-space/xloer/issues/new';
const KINDS = ['Hata', 'Öneri', 'Soru'];
// Browsers and GitHub cut very long addresses; the text is kept below that.
const MAX_BODY = 6000;

export async function openFeedback({ version, platform }) {
  const box = document.createElement('div');
  box.className = 'feedback';
  box.innerHTML = `
    <div class="seg" role="radiogroup" aria-label="Geri bildirim türü">${KINDS.map((k, i) => `<button type="button" role="radio" aria-checked="${i === 0}" data-kind="${k}">${k}</button>`).join('')}</div>
    <textarea class="input" rows="7" placeholder="Ne oldu, ne bekliyordun? Bir hatada: hangi ekranda, neye tıklayınca." spellcheck="true" aria-label="Geri bildirim"></textarea>
    <label class="check-row"><input type="checkbox" checked data-info><span>Sürüm bilgisini ekle (Xloer ${esc(version)} · ${esc(platform)})</span></label>
    <p class="hint">“Gönder” GitHub’da doldurulmuş bir kayıt açar; orada “Create” ile gönderilir (GitHub hesabı gerekir). Kişisel bilgi veya müşteri verisi yazma: kayıtlar herkese açık.</p>`;
  box.addEventListener('click', (e) => {
    const b = e.target.closest('[data-kind]');
    if (!b) return;
    box.querySelectorAll('[data-kind]').forEach((x) => x.setAttribute('aria-checked', String(x === b)));
  });
  const sent = await modal({
    title: 'Geri bildirim',
    body: box,
    onOpen: () => box.querySelector('textarea').focus(),
    buttons: [
      { label: 'Vazgeç', value: null },
      {
        label: 'Gönder',
        primary: true,
        value: (b) => ({
          kind: b.querySelector('[data-kind][aria-checked="true"]').dataset.kind,
          text: b.querySelector('textarea').value.trim(),
          info: b.querySelector('[data-info]').checked,
        }),
      },
    ],
  });
  if (!sent) return;
  if (!sent.text) return toast('Geri bildirim boştu; gönderilmedi.', { error: true });
  const firstLine = sent.text.split('\n')[0].slice(0, 80);
  const body = `${sent.text.slice(0, MAX_BODY)}${sent.info ? `\n\n---\nXloer ${version} · ${platform}` : ''}`;
  window.open(`${NEW_ISSUE}?title=${encodeURIComponent(`[${sent.kind}] ${firstLine}`)}&body=${encodeURIComponent(body)}`);
  toast('Tarayıcıda açıldı: GitHub’da “Create” ile gönder.');
}
