// Xloer main process: owns the data, the main window, the desktop widget and
// the tray icon. Windows never touch the file; they send actions over IPC and
// get the whole state back after every change, so the main window and the
// widget always show the same thing.

import { app, BrowserWindow, ClipboardItem, Menu, Notification, Tray, clipboard, dialog, ipcMain, nativeImage, nativeTheme, screen, shell } from 'electron';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { dispatch, normalizeState, todayStr } from '../shared/store.js';
import { seedState } from '../shared/seed.js';
import { paletteOf } from '../shared/themes.js';
import { parseTasksCsv } from '../shared/tasks-csv.js';
import { createStorage } from './storage.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const RENDERER = path.join(here, '..', 'renderer');
const PRELOAD = path.join(here, 'preload.cjs');
const ICON = path.join(here, '..', '..', 'build', 'icon.png');
const isWin = process.platform === 'win32';
const autostarted = process.argv.includes('--autostart');
// See self-test.js.
const selfTest = process.env.XLOER_SELF_TEST;


let state;
let storage;
let rev = 0;
let mainWin = null;
let widgetWin = null;
let tray = null;
let updater = null;
let quitting = false;
// What the main window should show once it has loaded: {type:'task', id} or {type:'update'}.
let pendingNav = null;

if (process.env.XLOER_DATA_DIR) app.setPath('userData', process.env.XLOER_DATA_DIR);
else if (selfTest) app.setPath('userData', fs.mkdtempSync(path.join(os.tmpdir(), 'xloer-self-test-')));

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => showMain());
  app.whenReady().then(start);
}

// The main window comes first and shows at once (in its theme's colour);
// the widget, the tray icon and the updater follow once it has painted, so
// they do not hold up the start.
function start() {
  if (isWin) app.setAppUserModelId('com.mericsdc.xloer');
  setupMenu();
  storage = createStorage(app.getPath('userData'));
  state = storage.load();
  if (!state) {
    state = seedState();
    storage.saveNow(state);
  }
  if (!state.settings.devicesRepaired) repairDeviceMerge();
  registerIpc();
  nativeTheme.themeSource = state.settings.theme;
  nativeTheme.on('updated', updateThemeColors);
  if (autostarted) return startRest();
  createMainWindow();
  let started = false;
  const rest = () => {
    if (started) return;
    started = true;
    startRest();
  };
  mainWin.once('ready-to-show', () => setTimeout(rest, 150));
  setTimeout(rest, 2500);
  if (selfTest) {
    import('./self-test.js').then(({ runSelfTest }) =>
      runSelfTest(selfTest, { main: () => mainWin, widget: () => widgetWin, updater: () => updater, state: () => state, apply, storage, loginItemPath }),
    );
  }
}

async function startRest() {
  createTray();
  applySettings(null);
  // electron-updater and its dependencies load only now.
  const { createUpdater } = await import('./updater.js');
  updater = createUpdater({
    broadcast: (update) => {
      sendAll('update:state', update);
      updateTrayMenu();
    },
    beforeInstall: () => {
      quitting = true;
      storage.flush();
    },
    // An older version may not know newer fields; a copy keeps them.
    beforeRollback: (version) => storage.snapshot(state, `surum-donusu-oncesi-v${version}`),
  });
  updater.setAutomatic(state.settings.autoUpdate);
  updateTrayMenu();
  sendAll('update:state', updater.get());
  checkReminders();
  setInterval(checkReminders, REMIND_EVERY);
}

// ---------------------------------------------------------------- notices

// Things to tell the main window once it is up (app:notices).
const notices = [];

// 1.18.0 merged some test systems by mistake ("ASLI TH-1728" → "ASLI").
// Once: every row gets its name back from the newest backup made before
// that version, and the window lists what came back.
function repairDeviceMerge() {
  let before = null;
  if (state.settings.devicesMergedByName) {
    for (const file of storage.backupFiles()) {
      try {
        const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
        if (raw?.settings?.devicesMergedByName) continue;
        before = { rows: {}, devices: Array.isArray(raw.devices) ? raw.devices : [] };
        for (const inv of raw.invoices ?? []) for (const r of inv.rows ?? []) before.rows[`${inv.id}:${r.id}`] = r.device;
        break;
      } catch {
        // An unreadable backup: try the next one.
      }
    }
  }
  const result = apply({ type: 'device/unmerge-wrong', before });
  if (result.restored.length) notices.push({ type: 'unmerge', ...result, fromBackup: !!before });
}

// ---------------------------------------------------------------- reminders

// A task with a date and a time reminds once when that moment comes: a
// Windows notification, or a tray balloon in the portable build (it has no
// Start menu entry, which notifications need). Clicking it opens the task.
// A moment that passed while Xloer was closed reminds at the next start,
// up to a week late.
const REMIND_EVERY = 20 * 1000;
const REMIND_LATE = 7 * 24 * 60 * 60 * 1000;
let balloonTask = null;
// Held until closed, so a notification is not collected before its click.
const notifications = new Set();

function checkReminders() {
  const now = Date.now();
  for (const t of state.tasks) {
    if (t.done || t.deletedAt || !t.due || !t.dueTime) continue;
    const at = `${t.due} ${t.dueTime}`;
    const time = new Date(`${t.due}T${t.dueTime}:00`).getTime();
    if (t.remindedFor === at || time > now || now - time > REMIND_LATE) continue;
    apply({ type: 'task/reminded', id: t.id, at });
    remind(t);
  }
}

function remind(t) {
  const title = `Xloer · ${t.dueTime}`;
  const body = t.title || 'Başlıksız görev';
  if (process.env.PORTABLE_EXECUTABLE_FILE || !Notification.isSupported()) {
    if (!tray) return;
    balloonTask = t.id;
    tray.displayBalloon({ iconType: 'info', title, content: body });
    return;
  }
  const n = new Notification({ title, body, icon: ICON });
  notifications.add(n);
  n.on('click', () => navigate({ type: 'task', id: t.id }));
  n.on('close', () => notifications.delete(n));
  n.show();
}

app.on('before-quit', () => {
  quitting = true;
  storage?.flush();
});
// The tray keeps the app alive; quitting goes through the tray menu.
app.on('window-all-closed', () => {});
app.on('activate', () => showMain());

// ---------------------------------------------------------------- state

function apply(action) {
  const before = JSON.stringify(state.settings);
  const result = dispatch(state, action);
  rev++;
  storage.scheduleSave(state);
  sendAll('state:changed', { state, rev });
  if (JSON.stringify(state.settings) !== before) applySettings(JSON.parse(before));
  return result;
}

function sendAll(channel, payload) {
  for (const w of [mainWin, widgetWin]) if (w && !w.isDestroyed()) w.webContents.send(channel, payload);
}

// Shows the main window and has it open a task or the update dialog.
function navigate(target) {
  if (mainWin && !mainWin.webContents.isLoading()) mainWin.webContents.send('nav', target);
  else pendingNav = target;
  showMain();
}

function setWidget(patch) {
  apply({ type: 'settings/update', patch: { widget: patch } });
}

function applySettings(prev) {
  const s = state.settings;
  nativeTheme.themeSource = s.theme;
  if (s.widget.visible && !widgetWin) createWidget();
  if (!s.widget.visible && widgetWin) widgetWin.close();
  if (widgetWin) {
    widgetWin.setAlwaysOnTop(s.widget.alwaysOnTop, 'floating');
    widgetWin.setOpacity(s.widget.opacity);
    widgetWin.setMovable(!s.widget.locked);
    widgetWin.setResizable(!s.widget.locked);
  }
  if (!prev || prev.startWithWindows !== s.startWithWindows) updateLoginItem();
  if (!prev || prev.autoUpdate !== s.autoUpdate) updater?.setAutomatic(s.autoUpdate);
  updateThemeColors();
  updateTrayMenu();
}

// The portable build runs from its unpacked copy; Windows starts the .exe itself.
const loginItemPath = () => process.env.PORTABLE_EXECUTABLE_FILE || process.execPath;

function updateLoginItem() {
  if (!isWin || !app.isPackaged || selfTest) return;
  app.setLoginItemSettings({ openAtLogin: state.settings.startWithWindows, path: loginItemPath(), args: ['--autostart'] });
}

// The window colours of the chosen theme, light or dark (see shared/themes.js).
function colors() {
  const p = paletteOf(state.settings.palette);
  const c = nativeTheme.shouldUseDarkColors ? p.dark : p.light;
  // The window buttons sit in the 32px title bar, on its colour (--bg-rail).
  return { bg: c.bg, widget: c.widget, overlay: { color: c.bg, symbolColor: c.text, height: 32 } };
}

function updateThemeColors() {
  const c = colors();
  if (mainWin && !mainWin.isDestroyed()) {
    mainWin.setBackgroundColor(c.bg);
    if (isWin) mainWin.setTitleBarOverlay(c.overlay);
  }
  if (widgetWin && !widgetWin.isDestroyed()) widgetWin.setBackgroundColor(c.widget);
}

// ---------------------------------------------------------------- windows

// v8CacheOptions: compiled page scripts are cached from the first run, so
// later starts skip compiling them again.
function webPreferences() {
  return { preload: PRELOAD, contextIsolation: true, sandbox: true, nodeIntegration: false, spellcheck: false, v8CacheOptions: 'bypassHeatCheck' };
}

function onScreen(b) {
  if (!b || !Number.isFinite(b.x) || !Number.isFinite(b.y)) return false;
  return screen.getAllDisplays().some(({ workArea: a }) => b.x < a.x + a.width - 40 && b.x + b.width > a.x + 40 && b.y >= a.y - 10 && b.y < a.y + a.height - 40);
}

function harden(win) {
  win.webContents.on('will-navigate', (e) => e.preventDefault());
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:\/\//.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  if (!app.isPackaged) {
    win.webContents.on('before-input-event', (_e, input) => {
      if (input.type === 'keyDown' && input.key === 'F12') win.webContents.toggleDevTools();
    });
  }
}

function createMainWindow() {
  const saved = storage.windowState().main ?? {};
  const c = colors();
  mainWin = new BrowserWindow({
    width: saved.width ?? 1180,
    height: saved.height ?? 760,
    ...(onScreen(saved) ? { x: saved.x, y: saved.y } : {}),
    minWidth: 720,
    minHeight: 460,
    // Shown at once in its theme's colour; a maximised one after maximising.
    show: !saved.maximized,
    title: 'Xloer',
    icon: ICON,
    backgroundColor: c.bg,
    titleBarStyle: 'hidden',
    ...(isWin ? { titleBarOverlay: c.overlay } : { trafficLightPosition: { x: 16, y: 10 } }),
    webPreferences: webPreferences(),
  });
  if (saved.maximized) {
    mainWin.maximize();
    mainWin.show();
  }
  harden(mainWin);
  mainWin.loadFile(path.join(RENDERER, 'index.html'));
  for (const name of ['maximize', 'unmaximize', 'enter-full-screen', 'leave-full-screen']) {
    mainWin.on(name, () => mainWin.webContents.send('win:state', winState()));
  }
  mainWin.on('close', (e) => {
    stopResize();
    storage.saveWindowState('main', { ...baseBounds(), maximized: mainWin.isMaximized() });
    if (quitting) return;
    if (state.settings.closeToTray) {
      e.preventDefault();
      mainWin.hide();
      trayHint();
    } else {
      quitting = true;
      app.quit();
    }
  });
  mainWin.on('closed', () => {
    mainWin = null;
    grown = { width: 0, shift: 0, x: null };
  });
}

// ---------------------------------------------------------------- detail panel room

// The task panel opens outward (see main-view.js): the window grows to the
// right by the panel's width, moving left only as far as the screen edge
// needs, and gives that room back when the panel closes. A maximised or
// full-screen window keeps its size. grown is the room taken so far; the
// saved window size leaves it out.
let grown = { width: 0, shift: 0, x: null };
let resizing = null;

const winState = () => ({ maximized: mainWin.isMaximized(), fullScreen: mainWin.isFullScreen() });
const canResize = () => mainWin && !mainWin.isMaximized() && !mainWin.isFullScreen();

function baseBounds() {
  const b = mainWin.getNormalBounds();
  const [minWidth] = mainWin.getMinimumSize();
  return { ...b, x: b.x === grown.x ? b.x + grown.shift : b.x, width: Math.max(minWidth, b.width - grown.width) };
}

function stopResize() {
  if (!resizing) return;
  clearInterval(resizing.timer);
  if (mainWin && !mainWin.isDestroyed()) mainWin.setBounds(resizing.to);
  resizing = null;
}

// Moves the window's edges to `to` over 200 ms, easing out.
function resizeTo(to) {
  stopResize();
  const from = mainWin.getBounds();
  const start = Date.now();
  const timer = setInterval(() => {
    if (!mainWin || mainWin.isDestroyed()) return clearInterval(timer);
    const k = Math.min(1, (Date.now() - start) / 200);
    const e = 1 - (1 - k) ** 3;
    const at = (a, b) => Math.round(a + (b - a) * e);
    mainWin.setBounds({ x: at(from.x, to.x), y: to.y, width: at(from.width, to.width), height: to.height });
    if (k === 1) {
      clearInterval(timer);
      resizing = null;
    }
  }, 16);
  resizing = { timer, to };
}

// Returns how much wider the window gets (0 when it cannot grow).
function growWindow(by) {
  if (!canResize() || !(by > 0)) return 0;
  stopResize();
  const b = mainWin.getBounds();
  const area = screen.getDisplayMatching(b).workArea;
  const width = Math.min(b.width + by, Math.max(b.width, area.width));
  const x = Math.max(area.x, Math.min(b.x, area.x + area.width - width));
  const added = width - b.width;
  if (added <= 0) return 0;
  grown = { width: grown.width + added, shift: grown.shift + b.x - x, x };
  resizeTo({ ...b, x, width });
  return added;
}

// Returns how much narrower the window gets; never below what it had
// before the panel took room, nor below its minimum width.
function shrinkWindow(by) {
  if (!canResize() || !(by > 0)) return 0;
  stopResize();
  const b = mainWin.getBounds();
  const [minWidth] = mainWin.getMinimumSize();
  const width = Math.max(minWidth, b.width - Math.min(by, grown.width));
  const removed = b.width - width;
  // Moved left to make room: back to where it was, unless moved since.
  const back = b.x === grown.x ? grown.shift : 0;
  grown = { width: grown.width - removed, shift: 0, x: null };
  if (removed > 0 || back) resizeTo({ ...b, x: b.x + back, width });
  return removed;
}

function showMain() {
  if (!mainWin) return createMainWindow();
  if (mainWin.isMinimized()) mainWin.restore();
  mainWin.show();
  mainWin.focus();
}

function createWidget() {
  const saved = storage.windowState().widget;
  const area = screen.getPrimaryDisplay().workArea;
  const bounds = onScreen(saved) ? saved : { width: 340, height: 560, x: area.x + area.width - 360, y: area.y + 24 };
  const w = state.settings.widget;
  widgetWin = new BrowserWindow({
    ...bounds,
    minWidth: 260,
    minHeight: 280,
    frame: false,
    show: false,
    skipTaskbar: true,
    resizable: !w.locked,
    movable: !w.locked,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    alwaysOnTop: w.alwaysOnTop,
    title: 'Xloer Widget',
    icon: ICON,
    backgroundColor: colors().widget,
    webPreferences: webPreferences(),
  });
  widgetWin.setOpacity(w.opacity);
  harden(widgetWin);
  widgetWin.loadFile(path.join(RENDERER, 'widget.html'));
  widgetWin.once('ready-to-show', () => widgetWin?.showInactive());
  let timer = null;
  const remember = () => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      if (widgetWin && !widgetWin.isDestroyed()) storage.saveWindowState('widget', widgetWin.getBounds());
    }, 400);
  };
  widgetWin.on('moved', remember);
  widgetWin.on('resized', remember);
  widgetWin.on('close', () => storage.saveWindowState('widget', widgetWin.getBounds()));
  widgetWin.on('closed', () => {
    widgetWin = null;
    // Closed some other way (Alt+F4): remember it as hidden.
    if (!quitting && state.settings.widget.visible) setWidget({ visible: false });
  });
}

// ---------------------------------------------------------------- tray & menu

function trayImage() {
  const base = nativeImage.createFromPath(ICON);
  if (base.isEmpty()) return base;
  const img = base.resize({ width: 16, height: 16, quality: 'best' });
  img.addRepresentation({ scaleFactor: 2, dataURL: base.resize({ width: 32, height: 32, quality: 'best' }).toDataURL() });
  return img;
}

function createTray() {
  tray = new Tray(trayImage());
  tray.setToolTip('Xloer');
  tray.on('click', () => showMain());
  tray.on('balloon-click', () => (balloonTask ? navigate({ type: 'task', id: balloonTask }) : showMain()));
}

function updateTrayMenu() {
  if (!tray) return;
  const w = state.settings.widget;
  const u = updater?.get() ?? { status: 'idle' };
  const updateItem =
    u.status === 'downloaded'
      ? { label: `Yeniden başlat ve güncelle (v${u.version})`, click: () => updater.install() }
      : u.status === 'available'
        ? { label: `Güncelleme var: v${u.version}`, click: () => navigate({ type: 'update' }) }
        : { label: 'Güncellemeleri denetle', enabled: u.status !== 'unsupported', click: () => navigate({ type: 'update' }) };
  tray.setToolTip(u.status === 'available' || u.status === 'downloaded' ? `Xloer — güncelleme var (v${u.version})` : 'Xloer');
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: 'Xloer’i aç', click: () => showMain() },
      updateItem,
      { type: 'separator' },
      { label: 'Widget’ı göster', type: 'checkbox', checked: w.visible, click: (item) => setWidget({ visible: item.checked }) },
      { label: 'Widget her zaman üstte', type: 'checkbox', checked: w.alwaysOnTop, click: (item) => setWidget({ alwaysOnTop: item.checked }) },
      { label: 'Widget konumunu kilitle', type: 'checkbox', checked: w.locked, click: (item) => setWidget({ locked: item.checked }) },
      { type: 'separator' },
      {
        label: 'Çıkış',
        click: () => {
          quitting = true;
          app.quit();
        },
      },
    ]),
  );
}

function trayHint() {
  if (state.settings.trayHintShown || !isWin || !tray) return;
  balloonTask = null;
  tray.displayBalloon({
    iconType: 'info',
    title: 'Xloer arka planda çalışıyor',
    content: 'Açmak için sistem tepsisindeki Xloer simgesine tıkla. Tamamen kapatmak için sağ tık → Çıkış.',
  });
  apply({ type: 'settings/update', patch: { trayHintShown: true } });
}

// Windows gets no menu bar. macOS (development) needs the Edit menu for
// copy/paste shortcuts in text fields.
function setupMenu() {
  if (process.platform !== 'darwin') return Menu.setApplicationMenu(null);
  Menu.setApplicationMenu(Menu.buildFromTemplate([{ role: 'appMenu' }, { role: 'editMenu' }, { role: 'viewMenu' }, { role: 'windowMenu' }]));
}

// ---------------------------------------------------------------- ipc

function registerIpc() {
  ipcMain.handle('state:get', () => ({ state, rev }));

  ipcMain.handle('dispatch', (_e, action) => {
    try {
      return { ok: true, result: apply(action) };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });

  ipcMain.handle('nav:take', () => {
    const target = pendingNav;
    pendingNav = null;
    return target;
  });

  ipcMain.handle('app:open-task', (_e, id) => navigate({ type: 'task', id }));

  ipcMain.handle('app:show-main', () => {
    showMain();
  });

  // The updater loads a moment after start; until then it is "idle".
  ipcMain.handle('update:get', () => updater?.get() ?? { status: 'idle' });
  ipcMain.handle('update:check', () => updater?.check());
  ipcMain.handle('update:versions', async () => {
    try {
      return { ok: true, versions: (await updater?.versions()) ?? [] };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });
  ipcMain.handle('update:install-version', (_e, version) => updater?.installVersion(version));
  ipcMain.handle('update:download', () => updater?.download());
  ipcMain.handle('update:install', () => updater?.install());
  ipcMain.handle('update:open-page', () => updater?.openPage());
  ipcMain.handle('update:open-panel', () => navigate({ type: 'update' }));

  const fromMain = (e) => mainWin && e.sender === mainWin.webContents;
  ipcMain.handle('win:state', (e) => (fromMain(e) ? winState() : null));
  ipcMain.handle('win:grow', (e, by) => (fromMain(e) ? growWindow(Number(by)) : 0));
  ipcMain.handle('win:shrink', (e, by) => (fromMain(e) ? shrinkWindow(Number(by)) : 0));

  ipcMain.handle('app:notices', () => notices.splice(0));

  ipcMain.handle('app:info', () => ({
    version: app.getVersion(),
    platform: process.platform,
    dataFile: storage.file,
  }));

  ipcMain.handle('data:open-dir', () => shell.openPath(app.getPath('userData')));

  // Rich copy: Outlook and Word take the HTML table, other apps the text.
  ipcMain.handle('clipboard:write', async (_e, { text, html }) => {
    await clipboard.write([new ClipboardItem({ 'text/plain': String(text ?? ''), 'text/html': String(html ?? '') })]);
  });

  ipcMain.handle('data:import-csv', async (e) => {
    const { canceled, filePaths } = await dialog.showOpenDialog(BrowserWindow.fromWebContents(e.sender), {
      title: 'İçe aktarılacak CSV dosyasını seç',
      filters: [{ name: 'Görev listesi (CSV)', extensions: ['csv'] }],
      properties: ['openFile'],
    });
    if (canceled || !filePaths.length) return { ok: false, canceled: true };
    try {
      const parsed = parseTasksCsv(fs.readFileSync(filePaths[0], 'utf8'));
      storage.snapshot(state, 'ice-aktarma-oncesi');
      return { ok: true, result: { ...apply({ type: 'import/merge', lists: parsed.lists }), rows: parsed.count } };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  });

  ipcMain.handle('data:export-json', async (e) => {
    const { canceled, filePath } = await dialog.showSaveDialog(BrowserWindow.fromWebContents(e.sender), {
      title: 'Xloer yedeğini kaydet',
      defaultPath: path.join(app.getPath('documents'), `Xloer-yedek-${todayStr()}.json`),
      filters: [{ name: 'Xloer yedeği', extensions: ['json'] }],
    });
    if (canceled || !filePath) return { ok: false, canceled: true };
    fs.writeFileSync(filePath, JSON.stringify(state, null, 2));
    return { ok: true, path: filePath };
  });

  ipcMain.handle('data:import-json', async (e) => {
    const { canceled, filePaths } = await dialog.showOpenDialog(BrowserWindow.fromWebContents(e.sender), {
      title: 'Xloer yedeğini geri yükle',
      filters: [{ name: 'Xloer yedeği', extensions: ['json'] }],
      properties: ['openFile'],
    });
    if (canceled || !filePaths.length) return { ok: false, canceled: true };
    try {
      const next = normalizeState(JSON.parse(fs.readFileSync(filePaths[0], 'utf8')));
      storage.snapshot(state, 'geri-yukleme-oncesi');
      apply({ type: 'state/replace', state: next });
      return { ok: true };
    } catch (err) {
      return { ok: false, error: `Yedek okunamadı: ${err.message}` };
    }
  });
}
