// A small .xlsx reader: enough to get every worksheet's cell values. An
// .xlsx file is a zip of XML parts; the caller passes the raw-deflate
// inflater for the zip entries (zlib in node, DecompressionStream in a
// window), so this module has no dependencies.

const utf8 = new TextDecoder();

function unzip(bytes) {
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let end = -1;
  for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 65557); i--) {
    if (dv.getUint32(i, true) === 0x06054b50) {
      end = i;
      break;
    }
  }
  if (end < 0) throw new Error('Bu dosya bir Excel (.xlsx) dosyası değil.');
  const count = dv.getUint16(end + 10, true);
  let p = dv.getUint32(end + 16, true);
  const entries = new Map();
  for (let n = 0; n < count && dv.getUint32(p, true) === 0x02014b50; n++) {
    const method = dv.getUint16(p + 10, true);
    const size = dv.getUint32(p + 20, true);
    const nameLength = dv.getUint16(p + 28, true);
    const extraLength = dv.getUint16(p + 30, true);
    const commentLength = dv.getUint16(p + 32, true);
    const local = dv.getUint32(p + 42, true);
    const name = utf8.decode(bytes.subarray(p + 46, p + 46 + nameLength));
    const start = local + 30 + dv.getUint16(local + 26, true) + dv.getUint16(local + 28, true);
    entries.set(name, { method, data: bytes.subarray(start, start + size) });
    p += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

async function readEntry(entries, name, inflateRaw) {
  const entry = entries.get(name);
  if (!entry) return null;
  if (entry.method === 0) return utf8.decode(entry.data);
  if (entry.method === 8) return utf8.decode(await inflateRaw(entry.data));
  throw new Error(`Excel dosyası desteklenmeyen biçimde sıkıştırılmış (${entry.method}).`);
}

const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };

function unescapeXml(s) {
  return s.replace(/&(#x[0-9a-f]+|#\d+|amp|lt|gt|quot|apos);/gi, (_m, e) =>
    e[0] === '#' ? String.fromCodePoint(e[1].toLowerCase() === 'x' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10)) : ENTITIES[e.toLowerCase()],
  );
}

// All <t> runs of a string item (rich text has several), joined.
function textOf(xml) {
  let out = '';
  for (const m of xml.matchAll(/<(?:\w+:)?t(?:\s[^>]*)?>([\s\S]*?)<\/(?:\w+:)?t>/g)) out += m[1];
  return unescapeXml(out);
}

function columnIndex(letters) {
  let n = 0;
  for (const ch of letters) n = n * 26 + (ch.charCodeAt(0) - 64);
  return n - 1;
}

// rows[r][c] = string | number | boolean, zero-based; empty cells are left out.
function parseSheet(xml, shared) {
  const rows = [];
  for (const m of xml.matchAll(/<(?:\w+:)?c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/(?:\w+:)?c>)/g)) {
    const ref = /\br="([A-Z]+)(\d+)"/.exec(m[1]);
    if (!ref) continue;
    const type = /\bt="(\w+)"/.exec(m[1])?.[1] ?? 'n';
    const body = m[2] ?? '';
    const raw = /<(?:\w+:)?v>([\s\S]*?)<\/(?:\w+:)?v>/.exec(body)?.[1];
    let value;
    if (type === 's') value = shared[Number(raw)] ?? '';
    else if (type === 'inlineStr') value = textOf(body);
    else if (type === 'str' || type === 'e') value = raw == null ? '' : unescapeXml(raw);
    else if (type === 'b') value = raw === '1';
    else value = raw == null || raw === '' ? null : Number(raw);
    if (value == null || value === '' || Number.isNaN(value)) continue;
    const r = Number(ref[2]) - 1;
    (rows[r] ??= [])[columnIndex(ref[1])] = value;
  }
  return rows;
}

// [{ name, rows }] for every worksheet, in workbook order.
export async function readWorkbook(bytes, inflateRaw) {
  const entries = unzip(bytes);
  const sharedXml = (await readEntry(entries, 'xl/sharedStrings.xml', inflateRaw)) ?? '';
  const shared = [...sharedXml.matchAll(/<(?:\w+:)?si\b[^>]*>([\s\S]*?)<\/(?:\w+:)?si>/g)].map((m) => textOf(m[1]));
  const workbook = (await readEntry(entries, 'xl/workbook.xml', inflateRaw)) ?? '';
  const rels = (await readEntry(entries, 'xl/_rels/workbook.xml.rels', inflateRaw)) ?? '';
  const targets = new Map([...rels.matchAll(/<Relationship\b[^>]*\bId="([^"]+)"[^>]*\bTarget="([^"]+)"/g)].map((m) => [m[1], m[2]]));
  const sheets = [];
  for (const m of workbook.matchAll(/<(?:\w+:)?sheet\b([^>]*)\/?>/g)) {
    const name = unescapeXml(/\bname="([^"]*)"/.exec(m[1])?.[1] ?? '');
    const target = targets.get(/\br:id="([^"]+)"/.exec(m[1])?.[1]);
    if (!target) continue;
    const path = target.startsWith('/') ? target.slice(1) : `xl/${target.replace(/^\.\//, '')}`;
    const xml = await readEntry(entries, path, inflateRaw);
    if (xml) sheets.push({ name, rows: parseSheet(xml, shared) });
  }
  if (!sheets.length) {
    // No usable workbook index: read the worksheet parts in file order.
    for (const name of [...entries.keys()].filter((n) => /^xl\/worksheets\/sheet\d+\.xml$/.test(n)).sort((a, b) => parseInt(a.slice(19), 10) - parseInt(b.slice(19), 10))) {
      sheets.push({ name, rows: parseSheet(await readEntry(entries, name, inflateRaw), shared) });
    }
  }
  return sheets;
}
