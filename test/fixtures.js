// Test data: a "Work" list of reports with their next steps, as Xloer is
// used day to day. The names are made up.
import { dispatch, emptyState } from '../src/shared/store.js';

const REPORTS = [
  ['ACME Rapor İNG', ['kullanılan ekipmanların kal belgeleri iletilecek']],
  ['BETA Rapor TR', ['Düşürme Testi Kaldı']],
  ['GAMMA SAVUNMA Rapor', ['Rapor sonucu için müşteriden dönüş bekleniyor']],
  ['DELTA Yakıt Tankı Rapor', ['Ham verileri sisteme yükle']],
  ['ÖRNEK İklimlendirme Testi', ['Rapor istenmiyor.', 'veri ve resim müşteri ile paylaş']],
  ['OMEGA Invertör', ['Fatura Kesildi.', 'İNG RAPOR 10.10.2026']],
  ['SİGMA Termal Şok Testi', ['İng rapor 16.10.2026', 'ödeme yapılmış']],
  ['KAPPA Rapor', ['İNG OLACAK 16.10.2026']],
  ['Lamda Aydınlatma Rapor İNG', []],
  ['ZETA Pil ing rapor', []],
  ['THETA MAST', []],
];

export function reportsState() {
  const s = emptyState();
  dispatch(s, { type: 'tag/create', name: 'RAPOR', color: '#e8b931' });
  const listId = dispatch(s, { type: 'list/create', name: 'Work', emoji: '💼' });
  const sectionId = dispatch(s, { type: 'section/create', listId, name: 'RAPORLAR' });
  for (const [title, subtasks] of REPORTS) {
    const parentId = dispatch(s, { type: 'task/create', listId, sectionId, title });
    for (const sub of subtasks) dispatch(s, { type: 'task/create', parentId, title: sub });
  }
  s.settings.widget.listId = listId;
  return s;
}
