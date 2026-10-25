import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseCsv, parseTasksCsv } from '../src/shared/tasks-csv.js';
import { dispatch, emptyState, buildIndex } from '../src/shared/store.js';
import { highlightRanges, parseQuery } from '../src/shared/search.js';

const BACKUP = [
  '﻿"Date: 2026-10-25+0000"',
  '"Version: 7.1"',
  '"Status: ',
  '0 Normal',
  '1 Completed',
  '2 Archived"',
  '"Folder Name","List Name","Title","Kind","Tags","Content","Is Check list","Start Date","Due Date","Reminder","Repeat","Priority","Status","Created Time","Completed Time","Order","Timezone","Is All Day","Is Floating","Column Name","Column Order","View Mode","taskId","parentId"',
  '"","Work","ACME Rapor İNG","TEXT","RAPOR","Müşteri: ACME ""Ltd""","N","","2026-10-15T21:00:00+0000","","","5","0","2026-10-01T08:00:00+0000","","-1099511627776","Europe/Istanbul","true","false","RAPORLAR","0","list","100",""',
  '"","Work","kullanılan ekipmanların kal belgeleri iletilecek","TEXT","","","N","","","","","0","0","2026-10-01T08:00:00+0000","","1","Europe/Istanbul","true","false","RAPORLAR","0","list","101","100"',
  '"","Work","Kontrol listesi","CHECKLIST","","Açıklama satırı',
  '▫Birinci madde',
  '▪İkinci madde","Y","","","","","3","2","2026-10-02T08:00:00+0000","2026-10-03T08:00:00+0000","2","Europe/Istanbul","true","false","","","list","102",""',
  '"","Inbox","Süt al","TEXT","","","N","","","","","1","0","","","0","Europe/Istanbul","true","false","","","list","103",""',
].join('\r\n');

test('csv parser handles quotes, escaped quotes and newlines inside fields', () => {
  const rows = parseCsv('a,"b ""c""","d\ne"\r\n1,2,3\n');
  assert.deepEqual(rows, [
    ['a', 'b "c"', 'd\ne'],
    ['1', '2', '3'],
  ]);
});

test('CSV export: lists, sections, subtasks, checklists, dates, priority', () => {
  const parsed = parseTasksCsv(BACKUP);
  assert.equal(parsed.count, 6);
  const work = parsed.lists.find((l) => l.name === 'Work');
  assert.deepEqual(work.sections, ['RAPORLAR']);
  const acme = work.tasks.find((t) => t.ref === '100');
  assert.equal(acme.title, 'ACME Rapor İNG');
  assert.equal(acme.due, '2026-10-16');
  assert.equal(acme.priority, 3);
  assert.deepEqual(acme.tags, ['RAPOR']);
  assert.equal(acme.notes, 'Müşteri: ACME "Ltd"');
  assert.equal(work.tasks.find((t) => t.ref === '101').parentRef, '100');
  const check = work.tasks.find((t) => t.ref === '102');
  assert.equal(check.done, true);
  assert.equal(check.notes, 'Açıklama satırı');
  const items = work.tasks.filter((t) => t.parentRef === '102');
  assert.deepEqual(items.map((t) => [t.title, t.done]), [
    ['Birinci madde', false],
    ['İkinci madde', true],
  ]);

  const s = emptyState();
  const r = dispatch(s, { type: 'import/merge', lists: parsed.lists });
  assert.equal(r.created, 6);
  assert.equal(s.tasks.find((t) => t.title === 'Süt al').listId, 'inbox');
  const index = buildIndex(s);
  const acmeTask = s.tasks.find((t) => t.title === 'ACME Rapor İNG');
  assert.equal(index.children(acmeTask.id).length, 1);
  assert.ok(s.lists.find((l) => l.name === 'Work').sections.some((x) => x.name === 'RAPORLAR'));
});

test('not a task list', () => {
  assert.throws(() => parseTasksCsv('a,b\n1,2'), /görev listesi gibi görünmüyor/);
});

test('highlight ranges map folded matches back to the original text', () => {
  const text = 'DELTA Yakıt Tankı İNG';
  const ranges = highlightRanges(text, parseQuery('ing yakit'));
  assert.deepEqual(ranges.map(([a, b]) => text.slice(a, b)), ['Yakıt', 'İNG']);
});
