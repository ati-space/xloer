// XLOER_SELF_TEST=<file.json>: the app starts as usual, checks that it really
// works (the main window and its page, the styles and fonts, saving to the
// data file, typing a customer on an invoice card, the widget, the paths it
// runs from, the updater), writes what it
// found to that file and quits. It never touches the real data: main.js gives
// it an empty data folder unless XLOER_DATA_DIR names one.
// scripts/verify-win.js runs the built installer and the portable exe this
// way on Windows before every release.

import { app } from 'electron';
import fs from 'node:fs';

const TIMEOUT = 60 * 1000;
const CUSTOMER = 'ÖZ TEST MÜŞTERİ';
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

async function until(what, test, ms = 20 * 1000) {
  const end = Date.now() + ms;
  for (;;) {
    const value = await test();
    if (value) return value;
    if (Date.now() > end) throw new Error(`${what}: zaman aşımı`);
    await wait(50);
  }
}

// Everything a page reports as an error, or a load or crash of it.
function watch(name, wc, errors) {
  wc.on('console-message', (e) => {
    if (e.level === 'error') errors.push(`${name}: ${e.message} (${e.sourceId}:${e.lineNumber})`);
  });
  wc.on('preload-error', (_e, file, err) => errors.push(`${name} preload: ${err.message}`));
  wc.on('did-fail-load', (_e, code, desc, url) => errors.push(`${name} yüklenemedi: ${desc} (${code}) ${url}`));
  wc.on('render-process-gone', (_e, d) => errors.push(`${name} çöktü: ${d.reason}`));
}

function loaded(wc) {
  return wc.isLoading() ? new Promise((r) => wc.once('did-finish-load', r)) : Promise.resolve();
}

// What the page shows once it has drawn its rows.
const PAGE_PROBE = (rowSelector) => `(async () => {
  await document.fonts.ready;
  const css = getComputedStyle(document.body);
  return {
    rows: document.querySelectorAll(${JSON.stringify(rowSelector)}).length,
    background: css.backgroundColor,
    font: css.fontFamily,
    fonts: [...new Set([...document.fonts].filter((f) => f.status === 'loaded').map((f) => f.family.replace(/"/g, '')))],
    icons: document.querySelectorAll('svg').length,
    size: [innerWidth, innerHeight],
    scale: devicePixelRatio,
  };
})()`;

// ctx: { main(), widget(), updater(), state(), apply, storage, loginItemPath() }
export async function runSelfTest(file, ctx) {
  const t = () => Math.round(performance.now());
  const report = {
    version: app.getVersion(),
    ok: false,
    packaged: app.isPackaged,
    times: { ready: t() },
    at: {},
    paths: {
      exe: app.getPath('exe'),
      portableFile: process.env.PORTABLE_EXECUTABLE_FILE ?? null,
      cwd: process.cwd(),
      userData: app.getPath('userData'),
      dataFile: ctx.storage.file,
      loginItem: ctx.loginItemPath(),
    },
    checks: {},
    errors: [],
  };
  const errors = report.errors;
  process.on('uncaughtException', (err) => errors.push(`ana süreç: ${err.stack ?? err}`));
  process.on('unhandledRejection', (err) => errors.push(`ana süreç: ${err?.stack ?? err}`));
  const fail = (err) => errors.push(String(err?.message ?? err));
  const timer = setTimeout(() => finish('genel zaman aşımı'), TIMEOUT);

  function finish(reason) {
    clearTimeout(timer);
    if (reason) errors.push(reason);
    const c = report.checks;
    report.ok =
      !errors.length && c.main?.rows > 0 && c.main.fonts.includes('Space Grotesk') && c.saved === true && c.customer === CUSTOMER && c.widget?.rows > 0 && !!c.updater;
    report.times.total = t();
    ctx.storage.flush();
    fs.writeFileSync(file, JSON.stringify(report, null, 2));
    app.exit(report.ok ? 0 : 1);
  }

  try {
    const win = await until('ana pencere', () => ctx.main());
    watch('ana pencere', win.webContents, errors);
    await new Promise((r) => (win.isVisible() ? r() : win.once('show', r)));
    report.times.shown = t();
    await loaded(win.webContents);
    report.times.loaded = t();
    await until('görev satırları', () => win.webContents.executeJavaScript(`document.querySelectorAll('#tree .row').length > 0`));
    report.times.rows = t();
    report.at.rows = Date.now();
    report.checks.main = await win.webContents.executeJavaScript(PAGE_PROBE('#tree .row'));

    // A change goes through the store and reaches the data file.
    const s = ctx.state();
    const listId = s.lists[0]?.id;
    const title = `self-test ${Date.now()}`;
    ctx.apply({ type: 'task/create', listId, title });
    ctx.storage.flush();
    report.checks.saved = fs.readFileSync(ctx.storage.file, 'utf8').includes(title);

    // A customer typed on an invoice card (with keys, as a person would) is saved.
    const invoice = ctx.apply({ type: 'invoice/create', code: 'P26-0000-0000', title: 'ÖZTEST Kontrol' });
    const js = (code) => win.webContents.executeJavaScript(code);
    await js(`document.querySelector('[data-rail="invoices"]').click(); document.querySelector('[data-tab="cards"]')?.click();`);
    const field = `document.querySelector('[data-id="${invoice}"] [data-f="customer"]')`;
    await until('fatura kartı', () => js(`!!${field}`));
    await js(`(() => { const f = ${field}; f.scrollIntoView({ block: 'center' }); f.focus(); f.select(); })()`);
    win.webContents.insertText(CUSTOMER);
    await until('müşteri yazımı', () => js(`${field}.value === ${JSON.stringify(CUSTOMER)}`), 5000);
    win.webContents.sendInputEvent({ type: 'keyDown', keyCode: 'Return' });
    win.webContents.sendInputEvent({ type: 'keyUp', keyCode: 'Return' });
    const customer = () => ctx.state().invoices.find((i) => i.id === invoice)?.customer;
    await until('müşteri kaydı', () => customer() === CUSTOMER, 5000).catch(fail);
    report.checks.customer = customer();
    await js(`document.querySelector('[data-rail="tasks"]').click()`);

    ctx.apply({ type: 'settings/update', patch: { widget: { visible: true } } });
    const widget = await until('widget', () => ctx.widget());
    watch('widget', widget.webContents, errors);
    await loaded(widget.webContents);
    await until('widget satırları', () => widget.webContents.executeJavaScript(`document.querySelectorAll('#w-tree .row').length > 0`));
    report.times.widget = t();
    report.checks.widget = await widget.webContents.executeJavaScript(PAGE_PROBE('#w-tree .row'));

    // The updater loads after the window; on Windows it asks GitHub once.
    const updater = await until('güncelleyici', () => ctx.updater());
    report.checks.updater = { ...updater.get() };
    if (updater.get().status !== 'unsupported') {
      await updater.check();
      await until('güncelleme denetimi', () => !['idle', 'checking'].includes(updater.get().status), 30 * 1000).catch(fail);
      const u = updater.get();
      report.checks.updater = { status: u.status, version: u.version, portable: u.portable, error: u.error };
    }
    await wait(300);
    finish();
  } catch (err) {
    finish(String(err?.message ?? err));
  }
}
