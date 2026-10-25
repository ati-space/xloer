// Writes a real .xlsx (deflated zip, shared strings, merged cells, a date
// cell) shaped like the invoice workbook, with made-up projects.
import zlib from 'node:zlib';

function zip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, text] of Object.entries(files)) {
    const nameBytes = Buffer.from(name);
    const raw = Buffer.from(text);
    const data = zlib.deflateRawSync(raw);
    const crc = zlib.crc32(raw);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(8, 8);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(raw.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(8, 10);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(raw.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += 30 + nameBytes.length + data.length;
  }
  const dir = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(Object.keys(files).length, 8);
  end.writeUInt16LE(Object.keys(files).length, 10);
  end.writeUInt32LE(dir.length, 12);
  end.writeUInt32LE(offset, 16);
  return new Uint8Array(Buffer.concat([...locals, dir, end]));
}

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// rows: { [rowNumber]: { A: value, B: value, ... } }; strings go to the
// shared string table, numbers stay numbers.
export function buildXlsx(rows, { sheetName = 'Faturalar', merges = [] } = {}) {
  const shared = [];
  const sharedIndex = (s) => {
    let i = shared.indexOf(s);
    if (i < 0) i = shared.push(s) - 1;
    return i;
  };
  const rowXml = Object.entries(rows)
    .map(([r, cells]) => {
      const cs = Object.entries(cells)
        .map(([col, v]) => (typeof v === 'number' ? `<c r="${col}${r}" s="2"><v>${v}</v></c>` : `<c r="${col}${r}" t="s"><v>${sharedIndex(v)}</v></c>`))
        .join('');
      return `<row r="${r}">${cs}<c r="G${r}" s="1"/></row>`;
    })
    .join('');
  const ns = 'xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"';
  return zip({
    '[Content_Types].xml': '<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>',
    'xl/workbook.xml': `<?xml version="1.0" encoding="UTF-8"?><workbook ${ns}><sheets><sheet name="${esc(sheetName)}" sheetId="1" r:id="rId1"/></sheets></workbook>`,
    'xl/_rels/workbook.xml.rels': '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>',
    'xl/sharedStrings.xml': `<?xml version="1.0" encoding="UTF-8"?><sst ${ns} count="${shared.length}" uniqueCount="${shared.length}">${shared.map((s) => `<si><t xml:space="preserve">${esc(s)}</t></si>`).join('')}</sst>`,
    'xl/worksheets/sheet1.xml': `<?xml version="1.0" encoding="UTF-8"?><worksheet ${ns}><cols><col min="1" max="6" width="20"/></cols><sheetData>${rowXml}</sheetData><mergeCells>${merges.map((m) => `<mergeCell ref="${m}"/>`).join('')}</mergeCells></worksheet>`,
  });
}

// Three project blocks laid out like the real sheet, with its quirks: a
// "Toplam" label beside the amount, an invoice split in two with blank rows
// before the last total, a code cell copied from the block above, a year typo,
// item columns with numbers, a sub-heading and a note row without a date.
export function invoiceWorkbook() {
  return buildXlsx(
    {
      1: { B: 'TÜM PROJELERDEN KESİLEN TOPLAM TUTAR (€)', D: 23320, F: '€' },
      10: { A: 'No', B: 'P26-0001-0001-ÖRNEK UN38.3 Testleri (TEST260101ORN01REV1)' },
      11: { A: 56, B: 'Proje Kodu', D: 'P26-0001-0001' },
      12: { B: 'Test', C: 'Test Sistemi', D: 'Test Tarihi', E: 'Gün / Vardiya' },
      13: { B: 'İklimlendirme Testi', C: 'Solar Kabin', D: '15.09.2026 - 28.09.2026', E: '14/42' },
      14: { B: 'Titreşim Testi', C: '70kN Sarsıcı', D: '30.09.2026 - 03.10.2026', E: '4/12' },
      15: { B: 'Mekanik Şok Testi', C: '70kN Sarsıcı', D: '08.10.206 – 09.10.2026', E: '2/6' },
      16: { D: 'Toplam Kesilen Fatura (€)', F: 16600 },
      19: { A: 'No', B: 'P26-0002-0002-DENEME CONVERTER Titreşim Testleri (TEST260202DEN01REV0)' },
      20: { A: 57, B: 'Proje Kodu', D: 'P26-0001-0001' },
      21: { B: 'Test', C: 'Test Sistemi', D: 'Test Tarihi', E: 'Gün / Vardiya' },
      22: { B: 'Titreşim  ve Mekanik Şok Testi', C: '35kN Sarsıcı', D: 46288, E: '1/3' },
      23: { B: 'Titreşim  ve Mekanik Şok \r\nTesti', C: '35kN Sarsıcı', D: '09.10.2026, 11.10.2026', E: '2/6' },
      24: { D: 'Toplam Kesilen Fatura (€)', F: 2000 },
      25: { B: 'Titreşim Testi', C: '35kN Sarsıcı', D: '12.10.2026', E: '1/3' },
      29: { F: '€ 720,00' },
      32: { A: 'No', B: 'P26-0003-0003-ÖRNEK KAUÇUK Tüm Testler (TEST260303ORN01)' },
      33: { A: 58, B: 'Proje Kodu', D: 'P26-0003-0003' },
      34: { B: 'Test', C: 'Test Sistemi', D: 'Test Tarihi', E: 'Gün / Vardiya', F: 'Teklif Kalemi', G: 'Kalem Tutarı (€)' },
      35: { A: '1.grup', B: 'Burst Test', C: 'Patlatma Düzeneği', D: '04.02.2026', E: '1/3', F: 1, G: 1500 },
      36: { B: 'Test Raporu', C: 'Her grup için bir rapor', F: 2, G: 500 },
      37: { B: 'Teklife Ek Olarak Gerçekleştirilen Testler' },
      38: { A: '3.grup', B: 'Burst Test', C: 'Patlatma Düzeneği', D: '10.02.2026 - 12.02.2026', E: '3/9', G: 2500 },
      39: { D: 'Toplam Kesilen Fatura (€)', F: 4500 },
    },
    { merges: ['B10:E10', 'A11:A12', 'B11:C11', 'D11:E11', 'B19:E19', 'A20:A21', 'D16:E16', 'D24:E24'] },
  );
}
