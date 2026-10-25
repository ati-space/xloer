// Stroke icons on a 24px grid, drawn with currentColor.
const PATHS = {
  tasks: '<rect x="3.5" y="3.5" width="17" height="17" rx="4"/><path d="m8 12 3 3 5-6"/>',
  search: '<circle cx="11" cy="11" r="6.5"/><path d="m20 20-4.2-4.2"/>',
  inbox: '<path d="M21 13h-5l-1.5 2.5h-5L8 13H3"/><path d="M5.5 5.5 3 13v5a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-5l-2.5-7.5A2 2 0 0 0 16.6 4H7.4a2 2 0 0 0-1.9 1.5z"/>',
  tag: '<path d="M12.6 3.4a2 2 0 0 0-1.4-.6H5a2 2 0 0 0-2 2v6.2a2 2 0 0 0 .6 1.4l8.1 8.1a2.2 2.2 0 0 0 3.1 0l6-6a2.2 2.2 0 0 0 0-3.1z"/><circle cx="7.5" cy="7.5" r="1.3"/>',
  completed: '<circle cx="12" cy="12" r="8.5"/><path d="m8.5 12 2.5 2.5 4.5-5"/>',
  trash: '<path d="M4 6.5h16M9 6.5V4.8c0-.7.6-1.3 1.3-1.3h3.4c.7 0 1.3.6 1.3 1.3v1.7M18.5 6.5l-.8 12.6a2 2 0 0 1-2 1.9H8.3a2 2 0 0 1-2-1.9L5.5 6.5M10 10.5v6M14 10.5v6"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  more: '<circle cx="5.5" cy="12" r="1.3" fill="currentColor" stroke="none"/><circle cx="12" cy="12" r="1.3" fill="currentColor" stroke="none"/><circle cx="18.5" cy="12" r="1.3" fill="currentColor" stroke="none"/>',
  chevron: '<path d="m7 10 5 5 5-5"/>',
  chevronRight: '<path d="m10 7 5 5-5 5"/>',
  flag: '<path d="M5 21V4.5M5 4.5h11.5l-2.2 4.2 2.2 4.3H5"/>',
  widget: '<rect x="4" y="3.5" width="16" height="17" rx="3"/><path d="M8 8.5h8M8 12h8M8 15.5h5"/>',
  settings: '<path d="M4 7h9M17 7h3M4 12h3M11 12h9M4 17h11M19 17h1"/><circle cx="15" cy="7" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="17" r="2"/>',
  pin: '<path d="M9.5 3.5h5l-.8 5.5 3.3 3.5H7l3.3-3.5zM12 12.5V21"/>',
  lock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/>',
  unlock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 7.6-1.8"/>',
  external: '<path d="M14 4h6v6M20 4l-8.5 8.5M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10"/>',
  x: '<path d="M17.5 6.5l-11 11M6.5 6.5l11 11"/>',
  menu: '<path d="M4 6.5h16M4 12h11M4 17.5h16"/>',
  notes: '<path d="M6.5 3.5h7.5l4 4v13h-11.5z"/><path d="M14 3.5v4h4M9.5 12.5h5M9.5 16h5"/>',
  subtasks: '<path d="M6 4v12.5a1.5 1.5 0 0 0 1.5 1.5H11M6 9h5"/><rect x="13" y="6.5" width="7" height="5" rx="1.5"/><rect x="13" y="15.5" width="7" height="5" rx="1.5"/>',
  section: '<rect x="4" y="5" width="16" height="14" rx="2.5"/><path d="M8 10h8M8 14h5"/>',
  checkmark: '<path d="m5.5 12.5 4 4 9-9.5"/>',
  restore: '<path d="M4 5v5h5"/><path d="M4.5 14a8 8 0 1 0 1.9-7.8L4 10"/>',
  download: '<path d="M12 4v11M7.5 10.5 12 15l4.5-4.5M5 20h14"/>',
  upload: '<path d="M12 15V4M7.5 8.5 12 4l4.5 4.5M5 20h14"/>',
  folder: '<path d="M3.5 7.5a2 2 0 0 1 2-2h3.8l2 2h7.2a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/>',
  eye: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="2.8"/>',
  eyeOff: '<path d="M4 4l16 16M9.9 5.8A9.6 9.6 0 0 1 12 5.5c6 0 9.5 6.5 9.5 6.5a17 17 0 0 1-2.6 3.4M6.2 7.4A16 16 0 0 0 2.5 12S6 18.5 12 18.5c1.6 0 3-.4 4.2-1.1"/><path d="M10 10a2.8 2.8 0 0 0 4 4"/>',
  edit: '<path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16z"/><path d="m13.5 6.5 4 4"/>',
  calendar: '<rect x="3.5" y="5" width="17" height="15.5" rx="3"/><path d="M3.5 10h17M8 3v4M16 3v4"/>',
  sun: '<circle cx="12" cy="12" r="3.8"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/>',
  opacity: '<path d="M12 3.5s6 6.4 6 10.8a6 6 0 0 1-12 0C6 9.9 12 3.5 12 3.5z"/><path d="M12 20.3V9"/>',
  grip: '<circle cx="9" cy="6" r="1.2" fill="currentColor" stroke="none"/><circle cx="15" cy="6" r="1.2" fill="currentColor" stroke="none"/><circle cx="9" cy="12" r="1.2" fill="currentColor" stroke="none"/><circle cx="15" cy="12" r="1.2" fill="currentColor" stroke="none"/><circle cx="9" cy="18" r="1.2" fill="currentColor" stroke="none"/><circle cx="15" cy="18" r="1.2" fill="currentColor" stroke="none"/>',
  list: '<path d="M9 6.5h11M9 12h11M9 17.5h11"/><circle cx="4.8" cy="6.5" r="1" fill="currentColor"/><circle cx="4.8" cy="12" r="1" fill="currentColor"/><circle cx="4.8" cy="17.5" r="1" fill="currentColor"/>',
  hide: '<path d="M5 12h14"/>',
  alert: '<path d="M10.3 4.3 2.9 17.5A2 2 0 0 0 4.6 20.5h14.8a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4.5M12 17.2v.1"/>',
  copy: '<rect x="8.5" y="8.5" width="11.5" height="11.5" rx="2"/><path d="M15.5 8.5V6A1.5 1.5 0 0 0 14 4.5H5.5A1.5 1.5 0 0 0 4 6v8.5A1.5 1.5 0 0 0 5.5 16h3"/>',
  update: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5v8.5M8.5 12.5 12 16l3.5-3.5"/>',
};

export function icon(name, size = 16) {
  return `<svg class="i i-${name}" viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${PATHS[name] ?? ''}</svg>`;
}

// A calendar page showing today's day number.
export function dayIcon(text, size = 16) {
  return `<svg class="i i-day" viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3.5" y="4.5" width="17" height="16" rx="3"/><path d="M8 3v3M16 3v3"/><text x="12" y="17.6" text-anchor="middle" font-size="8.5" font-weight="700" fill="currentColor" stroke="none" font-family="Segoe UI, system-ui, sans-serif">${text}</text></svg>`;
}
