// The colour themes in Ayarlar → Görünüm. Their tokens live in
// renderer/css/base.css (Laboratuvar) and renderer/css/themes.css (the rest).
// The window colours here are each theme's --bg-list, --bg-widget,
// --bg-detail (the Windows title bar) and --text, so a window never shows
// another theme's colour before or behind its page; a test keeps them equal.

export const PALETTES = [
  {
    id: 'laboratuvar',
    name: 'Laboratuvar',
    note: 'Grafit, sinyal sarısı',
    dark: { bg: '#0d0e10', widget: '#0f1013', bar: '#141518', text: '#ecece8', accent: '#f2e14c' },
    light: { bg: '#f4f4f1', widget: '#f7f7f4', bar: '#ffffff', text: '#141516', accent: '#f2e14c' },
  },
  {
    id: 'gece',
    name: 'Gece',
    note: 'Lacivert, camgöbeği',
    dark: { bg: '#0b1020', widget: '#0d1424', bar: '#111a2e', text: '#e6ecf7', accent: '#4cc9f0' },
    light: { bg: '#eef2f8', widget: '#f3f6fb', bar: '#ffffff', text: '#0f1a2e', accent: '#39b6e6' },
  },
  {
    id: 'orman',
    name: 'Orman',
    note: 'Koyu yeşil, limon',
    dark: { bg: '#0b120e', widget: '#0d1611', bar: '#111b15', text: '#e7efe9', accent: '#a3e635' },
    light: { bg: '#f1f5f0', widget: '#f5f8f4', bar: '#ffffff', text: '#102017', accent: '#84cc16' },
  },
  {
    id: 'bakir',
    name: 'Bakır',
    note: 'Kahve, bakır; açıkta krem',
    dark: { bg: '#120d0a', widget: '#15100c', bar: '#1a1310', text: '#f1e9e2', accent: '#f08a4b' },
    light: { bg: '#f7f1ea', widget: '#f9f4ee', bar: '#fffaf5', text: '#21160f', accent: '#e07b3c' },
  },
  {
    id: 'murdum',
    name: 'Mürdüm',
    note: 'Mürdüm, gül pembesi',
    dark: { bg: '#120c14', widget: '#150f18', bar: '#1a121d', text: '#f1e8f3', accent: '#f472b6' },
    light: { bg: '#f8f1f6', widget: '#faf5f9', bar: '#ffffff', text: '#22121d', accent: '#ec4899' },
  },
  {
    id: 'kontrast',
    name: 'Kontrast',
    note: 'Siyah-beyaz, yüksek kontrast',
    dark: { bg: '#0a0a0a', widget: '#0e0e0e', bar: '#121212', text: '#f5f5f5', accent: '#f5f5f5' },
    light: { bg: '#f2f2f2', widget: '#f6f6f6', bar: '#ffffff', text: '#0a0a0a', accent: '#111111' },
  },
];

export const DEFAULT_PALETTE = PALETTES[0].id;

export const paletteOf = (id) => PALETTES.find((p) => p.id === id) ?? PALETTES[0];
