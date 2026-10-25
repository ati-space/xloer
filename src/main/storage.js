// Data lives in %APPDATA%/Xloer/xloer-data.json. Writes are atomic
// (temp file + rename) and a copy is kept per day in backups/ (last 14 days).

import fs from 'node:fs';
import path from 'node:path';
import { normalizeState, todayStr } from '../shared/store.js';

const KEEP_BACKUPS = 14;

export function createStorage(dir) {
  const file = path.join(dir, 'xloer-data.json');
  const windowFile = path.join(dir, 'window-state.json');
  const backupDir = path.join(dir, 'backups');
  let timer = null;
  let pending = null;

  const readJson = (f) => JSON.parse(fs.readFileSync(f, 'utf8'));

  function writeAtomic(f, text) {
    fs.mkdirSync(path.dirname(f), { recursive: true });
    const tmp = `${f}.tmp`;
    fs.writeFileSync(tmp, text);
    try {
      fs.renameSync(tmp, f);
    } catch {
      // Windows can refuse the rename while a scanner holds the file open.
      fs.writeFileSync(f, text);
      fs.rmSync(tmp, { force: true });
    }
  }

  function backups() {
    try {
      return fs
        .readdirSync(backupDir)
        .filter((n) => /^xloer-\d{4}-\d{2}-\d{2}\.json$/.test(n))
        .sort()
        .reverse()
        .map((n) => path.join(backupDir, n));
    } catch {
      return [];
    }
  }

  function dailyBackup() {
    const target = path.join(backupDir, `xloer-${todayStr()}.json`);
    if (fs.existsSync(target)) return;
    fs.mkdirSync(backupDir, { recursive: true });
    fs.copyFileSync(file, target);
    for (const old of backups().slice(KEEP_BACKUPS)) fs.rmSync(old, { force: true });
  }

  return {
    file,
    dir,

    // null when there is no data yet. A damaged file is set aside and the
    // newest readable backup is used instead.
    load() {
      if (!fs.existsSync(file)) return null;
      try {
        return normalizeState(readJson(file));
      } catch (err) {
        console.error('Xloer: veri dosyası okunamadı', err);
        try {
          fs.renameSync(file, `${file}.bozuk-${Date.now()}`);
        } catch {}
        for (const b of backups()) {
          try {
            return normalizeState(readJson(b));
          } catch {}
        }
        return null;
      }
    },

    scheduleSave(state) {
      pending = state;
      clearTimeout(timer);
      timer = setTimeout(() => this.flush(), 300);
    },

    flush() {
      clearTimeout(timer);
      timer = null;
      if (!pending) return;
      const state = pending;
      pending = null;
      writeAtomic(file, JSON.stringify(state));
      try {
        dailyBackup();
      } catch (err) {
        console.error('Xloer: yedek alınamadı', err);
      }
    },

    saveNow(state) {
      pending = state;
      this.flush();
    },

    // A one-off copy before an import or restore replaces data.
    snapshot(state, label) {
      writeAtomic(path.join(backupDir, `${label}-${Date.now()}.json`), JSON.stringify(state));
    },

    windowState() {
      try {
        return readJson(windowFile);
      } catch {
        return {};
      }
    },

    saveWindowState(key, value) {
      const all = this.windowState();
      all[key] = value;
      try {
        writeAtomic(windowFile, JSON.stringify(all));
      } catch (err) {
        console.error('Xloer: pencere konumu kaydedilemedi', err);
      }
    },
  };
}
