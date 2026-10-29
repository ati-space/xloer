// Turkish-aware, accent-insensitive matching: "ing" finds "İNG", "ING" and
// "ıng"; "odeme" finds "ödeme".

export function fold(text) {
  return String(text ?? '')
    .toLocaleLowerCase('tr')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/ı/g, 'i');
}

// Folded text plus, for every folded char, the index of the source char it
// came from (and one trailing sentinel), so matches can be mapped back.
function foldWithMap(text) {
  let out = '';
  const map = [];
  let i = 0;
  for (const ch of String(text ?? '')) {
    const f = fold(ch);
    for (let k = 0; k < f.length; k++) map.push(i);
    out += f;
    i += ch.length;
  }
  map.push(i);
  return { text: out, map };
}

// "rapor #ing" -> [{text:'rapor', tag:false}, {text:'ing', tag:true}]
export function parseQuery(query) {
  return String(query ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .map((raw) => {
      const tag = raw.length > 1 && raw.startsWith('#');
      return { text: fold(tag ? raw.slice(1) : raw), tag };
    })
    .filter((t) => t.text);
}

// Every term must match the title, the notes or a tag ("#term" only tags).
export function matchTask(task, terms) {
  if (!terms.length) return null;
  const title = fold(task.title);
  const notes = fold(`${task.notes} ${task.code ?? ''}`);
  const tags = task.tags.map(fold);
  let inTitle = false;
  let inNotes = false;
  for (const term of terms) {
    if (term.tag) {
      if (!tags.some((g) => g.startsWith(term.text))) return null;
      continue;
    }
    const t = title.includes(term.text);
    const n = notes.includes(term.text);
    if (!t && !n && !tags.some((g) => g.includes(term.text))) return null;
    inTitle ||= t;
    inNotes ||= n;
  }
  return { inTitle, inNotes };
}

// [start, end) ranges in the original text, merged and sorted.
export function highlightRanges(text, terms) {
  const { text: f, map } = foldWithMap(text);
  const ranges = [];
  for (const term of terms) {
    if (term.tag || !term.text) continue;
    for (let i = f.indexOf(term.text); i !== -1; i = f.indexOf(term.text, i + term.text.length)) {
      ranges.push([map[i], map[i + term.text.length]]);
    }
  }
  ranges.sort((a, b) => a[0] - b[0]);
  const merged = [];
  for (const r of ranges) {
    const last = merged[merged.length - 1];
    if (last && r[0] <= last[1]) last[1] = Math.max(last[1], r[1]);
    else merged.push([...r]);
  }
  return merged;
}

// A one-line excerpt of `text` around the first match.
export function snippet(text, terms, radius = 28) {
  const flat = String(text ?? '').replace(/\s+/g, ' ').trim();
  const ranges = highlightRanges(flat, terms);
  if (!ranges.length) return null;
  const [s, e] = ranges[0];
  const from = Math.max(0, s - radius);
  const to = Math.min(flat.length, e + radius);
  return { text: flat.slice(from, to), prefix: from > 0, suffix: to < flat.length };
}
