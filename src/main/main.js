// Xloer main process: owns the data, the main window, the desktop widget and
// the tray icon. Windows never touch the file; they send actions over IPC and
// get the whole state back after every change, so the main window and the
// widget always show the same thing.

import { app, BrowserWindow, ClipboardItem, Menu, Tray, clipboard, dialog, ipcMain, nativeImage, nativeTheme, screen, shell } from 'electron';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { dispatch, normalizeState, todayStr } from '../shared/store.js';
import { seedState } from '../shared/seed.js';
import { paletteOf } from '../shared/themes.js';
import { parseTasksCsv } from '../shared/tasks-csv.js';
import { createStorage } from './storage.js';
import { createUpdater } from './updater.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const RENDERER = path.join(here, '..', 'renderer');
const PRELOAD = path.join(here, 'preload.cjs');
const ICON = path.join(here, '..', '..', 'build', 'icon.png');
const isWin = process.platform === 'win32';
const autostarted = process.argv.includes('--autostart');


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

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => showMain());
  app.whenReady().then(start);
}

function start() {
  if (isWin) app.setAppUserModelId('com.mericsdc.xloer');
  setupMenu();
  storage = createStorage(app.getPath('userData'));
  state = storage.load();
  if (!state) {
    state = seedState();
    storage.saveNow(state);
  }
  updater = createUpdater({
    broadcast: (update) => {
      sendAll('update:state', update);
      updateTrayMenu();
    },
    beforeInstall: () => {
      quitting = true;
      storage.flush();
    },
  });
  registerIpc();
  nativeTheme.on('updated', updateThemeColors);
  createTray();
  applySettings(null);
  if (!autostarted) createMainWindow();
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
  if (!prev || prev.autoUpdate !== s.autoUpdate) updater.setAutomatic(s.autoUpdate);
  updateThemeColors();
  updateTrayMenu();
}

function updateLoginItem() {
  if (!isWin || !app.isPackaged) return;
  // The portable build runs from a temp folder; register the real .exe.
  const exe = process.env.PORTABLE_EXECUTABLE_FILE || process.execPath;
  app.setLoginItemSettings({ openAtLogin: state.settings.startWithWindows, path: exe, args: ['--autostart'] });
}

// The window colours of the chosen theme, light or dark (see shared/themes.js).
function colors() {
  const p = paletteOf(state.settings.palette);
  const c = nativeTheme.shouldUseDarkColors ? p.dark : p.light;
  return { bg: c.bg, widget: c.widget, overlay: { color: c.bar, symbolColor: c.text, height: 40 } };
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

function webPreferences() {
  return { preload: PRELOAD, contextIsolation: true, sandbox: true, nodeIntegration: false, spellcheck: false };
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
    show: false,
    title: 'Xloer',
    icon: ICON,
    backgroundColor: c.bg,
    titleBarStyle: 'hidden',
    ...(isWin ? { titleBarOverlay: c.overlay } : { trafficLightPosition: { x: 18, y: 14 } }),
    webPreferences: webPreferences(),
  });
  if (saved.maximized) mainWin.maximize();
  harden(mainWin);
  mainWin.loadFile(path.join(RENDERER, 'index.html'));
  mainWin.once('ready-to-show', () => mainWin?.show());
  mainWin.on('close', (e) => {
    storage.saveWindowState('main', { ...mainWin.getNormalBounds(), maximized: mainWin.isMaximized() });
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
  });
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
}

function updateTrayMenu() {
  if (!tray) return;
  const w = state.settings.widget;
  const u = updater.get();
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

  ipcMain.handle('update:get', () => updater.get());
  ipcMain.handle('update:check', () => updater.check());
  ipcMain.handle('update:download', () => updater.download());
  ipcMain.handle('update:install', () => updater.install());
  ipcMain.handle('update:open-page', () => updater.openPage());
  ipcMain.handle('update:open-panel', () => navigate({ type: 'update' }));

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
