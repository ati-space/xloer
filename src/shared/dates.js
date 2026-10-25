// Turkish dates for the date fields and the calendar: "15.09.2026", weeks
// that start on Monday, Turkish month and day names.

export const TR_MONTHS = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];
export const TR_WEEKDAYS = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];

const pad = (n) => String(n).padStart(2, '0');
const daysIn = (y, m) => new Date(Date.UTC(y, m, 0)).getUTCDate();

export const isoDate = (y, m, d) => `${y}-${pad(m)}-${pad(d)}`;

// "2026-09-15" → "15.09.2026".
export function formatTrDate(iso) {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  return `${d}.${m}.${y}`;
}

// What a person types into a date field → ISO date; '' → null; anything
// that is not a real day → undefined. Accepts 15.09.2026, 15/9/26,
// 15-09-2026, 15092026, 150926 and 15.09 (this year).
export function parseTrDate(text, thisYear = new Date().getFullYear()) {
  const s = String(text ?? '').trim();
  if (!s) return null;
  let d;
  let m;
  let y;
  const parts = s.split(/[./\-\s]+/).filter(Boolean);
  if (parts.length >= 2 && parts.length <= 3 && parts.every((p) => /^\d+$/.test(p))) [d, m, y] = parts;
  else if (/^\d{8}$/.test(s)) [d, m, y] = [s.slice(0, 2), s.slice(2, 4), s.slice(4)];
  else if (/^\d{6}$/.test(s)) [d, m, y] = [s.slice(0, 2), s.slice(2, 4), s.slice(4)];
  else return undefined;
  const year = y == null ? thisYear : y.length === 2 ? 2000 + Number(y) : y.length === 4 ? Number(y) : NaN;
  const month = Number(m);
  const day = Number(d);
  if (!(year >= 2000 && year <= 2100) || month < 1 || month > 12 || day < 1 || day > daysIn(year, month)) return undefined;
  return isoDate(year, month, day);
}

// The month's grid: whole weeks, Monday first, with the days of the months
// around it so every row has seven cells.
export function calendarWeeks(year, month) {
  const first = new Date(Date.UTC(year, month - 1, 1));
  const lead = (first.getUTCDay() + 6) % 7; // Monday = 0
  const start = Date.UTC(year, month - 1, 1 - lead);
  const count = Math.ceil((lead + daysIn(year, month)) / 7) * 7;
  const weeks = [];
  for (let i = 0; i < count; i++) {
    const t = new Date(start + i * 86400000);
    if (i % 7 === 0) weeks.push([]);
    weeks.at(-1).push({ iso: t.toISOString().slice(0, 10), day: t.getUTCDate(), inMonth: t.getUTCMonth() === month - 1 });
  }
  return weeks;
}

export const addDaysIso = (iso, n) => new Date(Date.parse(`${iso}T00:00:00Z`) + n * 86400000).toISOString().slice(0, 10);
