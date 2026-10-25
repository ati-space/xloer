// First-run data: a "Work" list whose example rows explain how the tree
// works. Real tasks come in by typing or through Settings → CSV import.
import { dispatch, emptyState } from './store.js';

const EXAMPLES = [
  ['Örnek rapor: başlığı tutup sürükleyerek taşı', ['Alt görevler başlıkla birlikte taşınır', 'Sağa çekince alt görev, sola çekince üst seviye olur']],
  ['Bir satırı başka bir satırın ortasına bırakınca alt görevi olur', []],
  ['Başka bir uygulamadaki görevlerini getirmek için: Ayarlar → CSV’den içe aktar', []],
];

export function seedState() {
  const s = emptyState();
  dispatch(s, { type: 'tag/create', name: 'RAPOR', color: '#e8b931' });
  const listId = dispatch(s, { type: 'list/create', name: 'Work', emoji: '💼' });
  const sectionId = dispatch(s, { type: 'section/create', listId, name: 'RAPORLAR' });
  for (const [title, subtasks] of EXAMPLES) {
    const parentId = dispatch(s, { type: 'task/create', listId, sectionId, title });
    for (const sub of subtasks) dispatch(s, { type: 'task/create', parentId, title: sub });
  }
  s.settings.widget.listId = listId;
  return s;
}
