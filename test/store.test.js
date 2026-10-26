import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildIndex, counts, dispatch, emptyState, normalizeState, INBOX_ID } from '../src/shared/store.js';
import { reportsState as seedState } from './fixtures.js';

const titles = (index, ids) => ids.map((t) => index.byId.get(t.id ?? t).title);

function workList(s) {
  return s.lists.find((l) => l.name === 'Work');
}

test('seed has the reports from the screenshot under RAPORLAR', () => {
  const s = seedState();
  const work = workList(s);
  const [sec] = work.sections;
  assert.equal(sec.name, 'RAPORLAR');
  const index = buildIndex(s);
  const roots = index.roots(work.id, sec.id);
  assert.equal(roots.length, 11);
  assert.equal(roots[0].title, 'ACME Rapor İNG');
  const omega = roots.find((t) => t.title === 'OMEGA Invertör');
  assert.deepEqual(titles(index, index.children(omega.id)), ['Fatura Kesildi.', 'İNG RAPOR 10.10.2026']);
  assert.equal(s.settings.widget.listId, work.id);
});

test('moving a report header carries its subtasks and keeps order dense', () => {
  const s = seedState();
  const work = workList(s);
  const sec = work.sections[0].id;
  let index = buildIndex(s);
  const roots = index.roots(work.id, sec);
  const omega = roots.find((t) => t.title === 'OMEGA Invertör');
  dispatch(s, { type: 'task/move', id: omega.id, listId: work.id, sectionId: sec, beforeId: roots[0].id });
  index = buildIndex(s);
  const after = index.roots(work.id, sec);
  assert.equal(after[0].title, 'OMEGA Invertör');
  assert.deepEqual(after.map((t) => t.order), after.map((_, i) => i));
  assert.equal(index.children(omega.id).length, 2);
});

test('a task can become a subtask and back, and cannot go into itself', () => {
  const s = seedState();
  const work = workList(s);
  const sec = work.sections[0].id;
  let index = buildIndex(s);
  const [a, b] = index.roots(work.id, sec);
  dispatch(s, { type: 'task/move', id: b.id, parentId: a.id });
  index = buildIndex(s);
  assert.equal(index.byId.get(b.id).parentId, a.id);
  assert.ok(index.children(a.id).some((t) => t.id === b.id));
  assert.throws(() => dispatch(s, { type: 'task/move', id: a.id, parentId: b.id }), /kendi alt görevinin/);
  const grandchild = index.children(b.id)[0];
  assert.throws(() => dispatch(s, { type: 'task/move', id: a.id, parentId: grandchild.id }), /kendi alt görevinin/);
  dispatch(s, { type: 'task/move', id: b.id, listId: work.id, sectionId: sec, beforeId: a.id });
  index = buildIndex(s);
  assert.equal(index.roots(work.id, sec)[0].id, b.id);
});

test('moving to another list moves the whole subtree', () => {
  const s = seedState();
  const work = workList(s);
  const index = buildIndex(s);
  const ornek = s.tasks.find((t) => t.title === 'ÖRNEK İklimlendirme Testi');
  dispatch(s, { type: 'task/move', id: ornek.id, listId: INBOX_ID });
  const moved = s.tasks.filter((t) => t.id === ornek.id || t.parentId === ornek.id);
  assert.equal(moved.length, 3);
  assert.ok(moved.every((t) => t.listId === INBOX_ID && t.sectionId === null));
  assert.equal(index.byId.get(ornek.id).listId, INBOX_ID);
  assert.notEqual(work.id, INBOX_ID);
});

test('depth is limited to five levels', () => {
  const s = emptyState();
  let parentId = dispatch(s, { type: 'task/create', listId: INBOX_ID, title: 'L0' });
  for (let i = 1; i <= 4; i++) parentId = dispatch(s, { type: 'task/create', parentId, title: `L${i}` });
  assert.throws(() => dispatch(s, { type: 'task/create', parentId, title: 'L5' }), /5 seviye/);
  const two = dispatch(s, { type: 'task/create', listId: INBOX_ID, title: 'X' });
  dispatch(s, { type: 'task/create', parentId: two, title: 'X1' });
  const l3 = s.tasks.find((t) => t.title === 'L3');
  assert.throws(() => dispatch(s, { type: 'task/move', id: two, parentId: l3.id }), /5 seviye/);
});

test('sections can be reordered and deleting one keeps its tasks', () => {
  const s = seedState();
  const work = workList(s);
  const second = dispatch(s, { type: 'section/create', listId: work.id, name: 'FATURA' });
  dispatch(s, { type: 'section/move', listId: work.id, id: second, beforeId: work.sections[0].id });
  assert.deepEqual(work.sections.map((x) => x.name), ['FATURA', 'RAPORLAR']);
  const raporlar = work.sections[1].id;
  dispatch(s, { type: 'section/delete', listId: work.id, id: raporlar });
  const index = buildIndex(s);
  assert.equal(index.roots(work.id, null).length, 11);
  assert.ok(s.tasks.every((t) => t.sectionId !== raporlar));
});

test('completing a parent completes subtasks; reopening a subtask reopens the parent', () => {
  const s = seedState();
  const sigma = s.tasks.find((t) => t.title === 'SİGMA Termal Şok Testi');
  dispatch(s, { type: 'task/toggle', id: sigma.id });
  const kids = s.tasks.filter((t) => t.parentId === sigma.id);
  assert.ok(sigma.done && kids.every((t) => t.done));
  dispatch(s, { type: 'task/toggle', id: kids[0].id, done: false });
  assert.equal(sigma.done, false);
  assert.equal(kids[1].done, true);
});

test('delete, restore and purge work on whole subtrees', () => {
  const s = seedState();
  const work = workList(s);
  const t = s.tasks.find((x) => x.title === 'OMEGA Invertör');
  dispatch(s, { type: 'task/delete', id: t.id });
  assert.ok(s.tasks.filter((x) => x.id === t.id || x.parentId === t.id).every((x) => x.deletedAt));
  assert.equal(counts(s).trash, 1);
  dispatch(s, { type: 'task/restore', id: t.id });
  assert.ok(s.tasks.filter((x) => x.id === t.id || x.parentId === t.id).every((x) => !x.deletedAt));
  assert.equal(t.listId, work.id);
  const before = s.tasks.length;
  dispatch(s, { type: 'task/purge', id: t.id });
  assert.equal(s.tasks.length, before - 3);
});

test('normalizeState repairs dangling parents, cycles and unknown lists', () => {
  const s = normalizeState({
    lists: [{ id: 'l1', name: 'A', sections: [{ id: 's1', name: 'S' }] }],
    tasks: [
      { id: 'a', listId: 'l1', sectionId: 's1', title: 'a', parentId: 'b' },
      { id: 'b', listId: 'l1', title: 'b', parentId: 'a' },
      { id: 'c', listId: 'nope', title: 'c', parentId: 'zzz' },
      { id: 'd', listId: 'l1', sectionId: 'gone', title: 'd', tags: ['#X', 'x', 'Y'] },
    ],
  });
  assert.ok(s.lists.some((l) => l.id === INBOX_ID));
  const byId = new Map(s.tasks.map((t) => [t.id, t]));
  assert.ok(!byId.get('a').parentId || !byId.get('b').parentId);
  assert.equal(byId.get('c').listId, INBOX_ID);
  assert.equal(byId.get('c').parentId, null);
  assert.equal(byId.get('d').sectionId, null);
  assert.deepEqual(byId.get('d').tags, ['X', 'Y']);
  assert.deepEqual(s.tags.map((g) => g.name), ['X', 'Y']);
});

test('import/merge does not duplicate tasks that already exist', () => {
  const s = seedState();
  const lists = [
    {
      name: 'Work',
      sections: ['RAPORLAR'],
      tasks: [
        { ref: '1', parentRef: null, sectionName: 'RAPORLAR', title: 'BETA Rapor TR', order: 1 },
        { ref: '2', parentRef: '1', sectionName: 'RAPORLAR', title: 'Düşürme Testi Kaldı', order: 1 },
        { ref: '3', parentRef: '1', sectionName: 'RAPORLAR', title: 'Kalibrasyon belgesi', order: 2, done: true },
        { ref: '4', parentRef: null, sectionName: 'RAPORLAR', title: 'YENİ Rapor', order: 2, due: '2026-10-30', priority: 3, tags: ['RAPOR'] },
      ],
    },
    { name: 'Kişisel', sections: [], tasks: [{ ref: '5', parentRef: null, sectionName: null, title: 'Spor', order: 0 }] },
  ];
  const r1 = dispatch(s, { type: 'import/merge', lists });
  assert.deepEqual(r1, { created: 3, skipped: 2, newLists: 1 });
  const beta = s.tasks.find((t) => t.title === 'BETA Rapor TR');
  const kids = s.tasks.filter((t) => t.parentId === beta.id);
  assert.deepEqual(kids.map((t) => t.title).sort(), ['Düşürme Testi Kaldı', 'Kalibrasyon belgesi']);
  assert.equal(kids.find((t) => t.title === 'Kalibrasyon belgesi').done, true);
  const r2 = dispatch(s, { type: 'import/merge', lists });
  assert.deepEqual(r2, { created: 0, skipped: 5, newLists: 0 });
});

test('first-run data has the example list and no customer names', async () => {
  const { seedState: firstRun } = await import('../src/shared/seed.js');
  const s = firstRun();
  const work = s.lists.find((l) => l.name === 'Work');
  assert.deepEqual(work.sections.map((x) => x.name), ['RAPORLAR']);
  assert.ok(s.tasks.length > 0);
  const text = JSON.stringify(s);
  for (const t of seedState().tasks) assert.ok(!text.includes(t.title), `first run has only the examples, not “${t.title}”`);
});

test('reports carry a stage and a project code; counts and rows follow them', async () => {
  const { buildListRows, buildStageRows, buildSearchRows } = await import('../src/shared/tree-rows.js');
  const s = seedState();
  const kappa = s.tasks.find((t) => t.title === 'KAPPA Rapor');
  dispatch(s, { type: 'task/update', id: kappa.id, patch: { stage: 2, code: '  P26-0001-0001 ' } });
  assert.equal(kappa.stage, 2);
  assert.equal(kappa.code, 'P26-0001-0001');
  dispatch(s, { type: 'task/update', id: kappa.id, patch: { stage: 9 } });
  assert.equal(kappa.stage, null, 'an unknown stage clears it');
  dispatch(s, { type: 'task/update', id: kappa.id, patch: { stage: 2 } });
  assert.deepEqual(counts(s).stages, [0, 0, 1, 0, 0]);
  assert.deepEqual(buildStageRows(s, 2).map((r) => r.task.title), ['KAPPA Rapor']);
  const row = buildListRows(s, kappa.listId).find((r) => r.id === kappa.id);
  assert.equal(row.nextStep, 'İNG OLACAK 16.10.2026');
  assert.equal(buildSearchRows(s, '0001-0001').matches, 1);
  assert.equal(normalizeState({ lists: [], tasks: [{ id: 'x', title: 'a', stage: '2', code: 5 }] }).tasks[0].stage, null);
});

test('widget pin: fixes the place; a pin from before 1.12 stops floating', () => {
  const old = normalizeState({ lists: [], tasks: [], settings: { widget: { alwaysOnTop: true, locked: false } } }).settings.widget;
  assert.deepEqual([old.locked, old.alwaysOnTop, old.pinLocks], [true, false, true]);
  const chosen = normalizeState({ lists: [], tasks: [], settings: { widget: { alwaysOnTop: true, pinLocks: true } } }).settings.widget;
  assert.deepEqual([chosen.locked, chosen.alwaysOnTop], [false, true], 'staying on top chosen after 1.12 is kept');
  const s = emptyState();
  dispatch(s, { type: 'settings/update', patch: { widget: { locked: true } } });
  assert.deepEqual([s.settings.widget.locked, s.settings.widget.alwaysOnTop], [true, false]);
});
