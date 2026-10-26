import { test } from 'node:test';
import assert from 'node:assert/strict';
import { dispatch, buildIndex } from '../src/shared/store.js';
import { reportsState as seedState } from './fixtures.js';
import { buildListRows, buildSearchRows, projectTaskDrop, sectionDropIndex } from '../src/shared/tree-rows.js';

function setup() {
  const s = seedState();
  const work = s.lists.find((l) => l.name === 'Work');
  return { s, work, rows: buildListRows(s, work.id) };
}

const without = (rows, id) => {
  const i = rows.findIndex((r) => r.id === id);
  let end = i + 1;
  while (end < rows.length && rows[end].kind === 'task' && rows[end].depth > rows[i].depth) end++;
  return rows.filter((_, k) => k < i || k >= end);
};

const apply = (s, listId, id, p) =>
  dispatch(s, { type: 'task/move', id, listId, sectionId: p.sectionId, parentId: p.parentId, beforeId: p.beforeId });

test('list rows: section header, reports and their subtasks in order', () => {
  const { rows } = setup();
  assert.equal(rows[0].kind, 'section');
  assert.equal(rows[1].task.title, 'ACME Rapor İNG');
  assert.equal(rows[2].depth, 1);
  assert.equal(rows.filter((r) => r.kind === 'task' && r.depth === 0).length, 11);
});

test('dropping a report between two others reorders it with its subtasks', () => {
  const { s, work, rows } = setup();
  const kappa = rows.find((r) => r.task?.title === 'KAPPA Rapor');
  const cands = without(rows, kappa.id);
  // Gap right under the section header: first report.
  const p = projectTaskDrop(cands, { gap: 1 }, { depth: 0, height: 1, dx: 0 });
  assert.equal(p.depth, 0);
  assert.equal(p.parentId, null);
  assert.equal(p.sectionId, work.sections[0].id);
  assert.equal(p.beforeId, rows[1].id);
  apply(s, work.id, kappa.id, p);
  const after = buildListRows(s, work.id);
  assert.equal(after[1].task.title, 'KAPPA Rapor');
  assert.equal(after[2].task.title, 'İNG OLACAK 16.10.2026');
});

test('dragging right nests under the row above; dragging left un-nests', () => {
  const { rows } = setup();
  const lamda = rows.find((r) => r.task?.title === 'Lamda Aydınlatma Rapor İNG');
  const cands = without(rows, lamda.id);
  const gap = cands.findIndex((r) => r.task?.title === 'ZETA Pil ing rapor');
  const prev = cands[gap - 1]; // "İNG OLACAK 16.10.2026" (depth 1 under KAPPA)
  const flat = projectTaskDrop(cands, { gap }, { depth: 0, height: 0, dx: 0 });
  assert.equal(flat.depth, 0);
  const nested = projectTaskDrop(cands, { gap }, { depth: 0, height: 0, dx: 20 });
  assert.equal(nested.depth, 1);
  assert.equal(nested.parentId, prev.task.parentId);
  const deeper = projectTaskDrop(cands, { gap }, { depth: 0, height: 0, dx: 80 });
  assert.equal(deeper.depth, 2);
  assert.equal(deeper.parentId, prev.id);
  const sub = rows.find((r) => r.task?.title === 'Düşürme Testi Kaldı');
  const c2 = without(rows, sub.id);
  const g2 = c2.findIndex((r) => r.task?.title === 'GAMMA SAVUNMA Rapor');
  const out = projectTaskDrop(c2, { gap: g2 }, { depth: 1, height: 0, dx: -18 });
  assert.equal(out.depth, 0);
  assert.equal(out.parentId, null);
  assert.equal(out.beforeId, c2[g2].id);
});

test('dropping on the middle of a row makes it a subtask', () => {
  const { s, work, rows } = setup();
  const zeta = rows.find((r) => r.task?.title === 'ZETA Pil ing rapor');
  const cands = without(rows, zeta.id);
  const into = cands.findIndex((r) => r.task?.title === 'BETA Rapor TR');
  const p = projectTaskDrop(cands, { into, gap: into }, { depth: 0, height: 0 });
  assert.equal(p.mode, 'into');
  apply(s, work.id, zeta.id, p);
  const index = buildIndex(s);
  const beta = s.tasks.find((t) => t.title === 'BETA Rapor TR');
  assert.deepEqual(index.children(beta.id).map((t) => t.title), ['Düşürme Testi Kaldı', 'ZETA Pil ing rapor']);
});

test('a drop that would nest too deep is refused', () => {
  const s = seedState();
  const inbox = 'inbox';
  let parent = dispatch(s, { type: 'task/create', listId: inbox, title: 'A0' });
  for (let i = 1; i <= 4; i++) parent = dispatch(s, { type: 'task/create', parentId: parent, title: `A${i}` });
  const big = dispatch(s, { type: 'task/create', listId: inbox, title: 'B0' });
  dispatch(s, { type: 'task/create', parentId: big, title: 'B1' });
  const rows = buildListRows(s, inbox);
  const cands = without(rows, big);
  const deepest = cands.findIndex((r) => r.task?.title === 'A4');
  // B0 has a child, so B0 itself can go no deeper than level 3 (B1 at 4).
  const p = projectTaskDrop(cands, { into: deepest, gap: deepest + 1 }, { depth: 0, height: 1, dx: 200 });
  assert.equal(p.mode, 'gap');
  assert.equal(p.depth, 3);
  assert.equal(p.parentId, rows.find((r) => r.task?.title === 'A2').id);
  dispatch(s, { type: 'task/move', id: big, parentId: p.parentId, beforeId: p.beforeId });
  assert.equal(buildIndex(s).byId.get(big).parentId, p.parentId);
});

test('dropping above the first section leaves the task unsectioned', () => {
  const { rows } = setup();
  const t = rows.find((r) => r.task?.title === 'ZETA Pil ing rapor');
  const p = projectTaskDrop(without(rows, t.id), { gap: 0 }, { depth: 0, height: 0 });
  assert.equal(p.sectionId, null);
  assert.equal(p.parentId, null);
});

test('section drop index follows block midpoints', () => {
  const blocks = [
    { top: 0, bottom: 100 },
    { top: 100, bottom: 140 },
  ];
  assert.equal(sectionDropIndex(blocks, 10), 0);
  assert.equal(sectionDropIndex(blocks, 60), 1);
  assert.equal(sectionDropIndex(blocks, 125), 2);
});

test('search: Turkish folding, ancestors as context, notes snippets', () => {
  const s = seedState();
  const odeme = s.tasks.find((t) => t.title === 'ödeme yapılmış');
  odeme.notes = 'Banka dekontu muhasebeye iletildi';
  const { rows, matches } = buildSearchRows(s, 'ing');
  assert.ok(matches >= 5);
  const titles = rows.filter((r) => r.kind === 'task' && !r.context).map((r) => r.task.title);
  assert.ok(titles.includes('ACME Rapor İNG'));
  assert.ok(titles.includes('İng rapor 16.10.2026'));
  assert.ok(titles.includes('ZETA Pil ing rapor'));
  const ctx = rows.find((r) => r.context);
  assert.equal(ctx.task.title, 'OMEGA Invertör');
  assert.equal(rows[0].kind, 'group');

  const r2 = buildSearchRows(s, 'odeme', { listId: odeme.listId });
  assert.equal(r2.matches, 1);
  assert.ok(r2.rows.every((r) => r.kind !== 'group'));
  const r3 = buildSearchRows(s, 'dekont');
  const hit = r3.rows.find((r) => r.id === odeme.id);
  assert.match(hit.snippet.text, /dekontu/);
  assert.equal(buildSearchRows(s, 'tank yakıt').matches, 1);
  assert.equal(buildSearchRows(s, 'zzz').rows.length, 0);
});

test('search: a matching task brings its subtasks, not dimmed', () => {
  const s = seedState();
  const { rows, matches } = buildSearchRows(s, 'omega');
  assert.equal(matches, 1);
  const tasks = rows.filter((r) => r.kind === 'task');
  assert.deepEqual(
    tasks.map((r) => [r.task.title, r.depth, !!r.context]),
    [
      ['OMEGA Invertör', 0, false],
      ['Fatura Kesildi.', 1, false],
      ['İNG RAPOR 10.10.2026', 1, false],
    ],
  );
  assert.equal(tasks[0].childCount, 2);
  // Done subtasks stay out when done tasks are left out.
  const kids = s.tasks.filter((t) => t.parentId === tasks[0].task.id);
  dispatch(s, { type: 'task/toggle', id: kids[0].id });
  assert.equal(buildSearchRows(s, 'omega', { includeDone: false }).rows.filter((r) => r.kind === 'task').length, 2);
});
