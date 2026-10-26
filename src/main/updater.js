// Update checks against the public ati-space/xloer repository
// (configured under build.publish in package.json). An installed copy
// downloads the new installer and swaps itself on restart; the portable exe
// can only be told about a new version and sent to the download page.

import { app, shell } from 'electron';
import updaterPackage from 'electron-updater';

const { autoUpdater } = updaterPackage;

export const RELEASES_URL = 'https://github.com/ati-space/xloer/releases/latest';
const FIRST_CHECK_DELAY = 15 * 1000;
const CHECK_EVERY = 6 * 60 * 60 * 1000;

// GitHub sends release notes as HTML; the windows show them as plain text.
function plainNotes(notes) {
  const html = Array.isArray(notes) ? notes.map((n) => n.note ?? '').join('\n') : String(notes ?? '');
  return html
    .replace(/<br\s*\/?>|<\/(p|li|h\d|div)>/gi, '\n')
    .replace(/<li[^>]*>/gi, '• ')
    .replace(/<[^>]+>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/\n{3,}/g, '\n\n')
    .trim()
    .slice(0, 3000);
}

function friendlyError(err) {
  const msg = String(err?.message ?? err);
  if (/ENOTFOUND|ETIMEDOUT|ECONNRESET|ECONNREFUSED|ERR_INTERNET_DISCONNECTED|ERR_NAME_NOT_RESOLVED|ERR_NETWORK/i.test(msg)) {
    return 'İnternet bağlantısı yok ya da GitHub’a ulaşılamadı.';
  }
  if (/404/.test(msg)) return 'Sürüm bilgisi bulunamadı. GitHub’daki son sürümde latest.yml dosyası olmalı.';
  if (/sha512 checksum mismatch/i.test(msg)) return 'İndirilen dosya bozuk geldi. Tekrar dene.';
  return msg.split('\n')[0].slice(0, 200);
}

// broadcast(state) reaches every window; beforeInstall() runs just before
// the app quits to let the installer replace it.
export function createUpdater({ broadcast, beforeInstall }) {
  const portable = !!process.env.PORTABLE_EXECUTABLE_FILE;
  const supported = app.isPackaged && process.platform === 'win32';
  let state = {
    status: supported ? 'idle' : 'unsupported', // idle checking none available downloading downloaded error unsupported
    current: app.getVersion(),
    version: null,
    notes: '',
    percent: 0,
    error: null,
    portable,
    checkedAt: null,
  };
  let timers = [];

  const set = (patch) => {
    state = { ...state, ...patch };
    broadcast(state);
  };

  if (supported) {
    autoUpdater.autoDownload = false;
    autoUpdater.autoInstallOnAppQuit = !portable;
    autoUpdater.disableWebInstaller = true;
    // Releases carry no .blockmap: an update downloads the whole installer.
    autoUpdater.disableDifferentialDownload = true;
    autoUpdater.logger = { info() {}, debug() {}, warn: (m) => console.warn('[update]', m), error: (m) => console.error('[update]', m) };
    autoUpdater.on('checking-for-update', () => set({ status: 'checking', error: null }));
    autoUpdater.on('update-available', (info) => set({ status: 'available', version: info.version, notes: plainNotes(info.releaseNotes), checkedAt: Date.now() }));
    autoUpdater.on('update-not-available', () => set({ status: 'none', version: null, notes: '', checkedAt: Date.now() }));
    autoUpdater.on('download-progress', (p) => set({ status: 'downloading', percent: Math.floor(p.percent) }));
    autoUpdater.on('update-downloaded', (info) => set({ status: 'downloaded', version: info.version, percent: 100 }));
    autoUpdater.on('error', (err) => set({ status: 'error', error: friendlyError(err), checkedAt: Date.now() }));
  }

  async function check() {
    if (!supported || state.status === 'checking' || state.status === 'downloading' || state.status === 'downloaded') return state;
    try {
      await autoUpdater.checkForUpdates();
    } catch {
      // Reported through the 'error' event.
    }
    return state;
  }

  async function download() {
    if (!supported || !state.version || !['available', 'error'].includes(state.status)) return state;
    if (portable) {
      shell.openExternal(RELEASES_URL);
      return state;
    }
    set({ status: 'downloading', percent: 0, error: null });
    try {
      await autoUpdater.downloadUpdate();
    } catch {
      // Reported through the 'error' event.
    }
    return state;
  }

  function install() {
    if (state.status !== 'downloaded') return;
    beforeInstall();
    // Silent install into the same folder, then start the new version.
    setImmediate(() => autoUpdater.quitAndInstall(true, true));
  }

  // Background checks: shortly after start, then every six hours.
  function setAutomatic(enabled) {
    for (const t of timers) clearTimeout(t);
    timers = [];
    if (!supported || !enabled) return;
    timers.push(setTimeout(check, FIRST_CHECK_DELAY));
    timers.push(setInterval(check, CHECK_EVERY));
  }

  return {
    get: () => state,
    check,
    download,
    install,
    setAutomatic,
    openPage: () => shell.openExternal(RELEASES_URL),
  };
}
