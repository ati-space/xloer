// FastShot's file wheel, running beside Xloer: drag files in Explorer or on
// the desktop and tap Shift; a wheel opens around the pointer to convert them
// (Dönüştür), work on them (Araçlar: crop, compress, metadata, merge PDFs…) or
// act on them (İşlemler: ZIP, share, copy, terminal). Tapping Shift again goes
// to the next wheel.
//
// It is FastShot.exe started with --wheel-host <Xloer's pid> (built from
// native/fastshot by scripts/build-wheel.js and shipped in resources/fastshot).
// It has no window of its own, quits by itself when Xloer's process ends, and
// steps aside while the full FastShot app runs, which has the same wheel.

import { app } from 'electron';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
// A wheel that keeps crashing is left off until Xloer starts again.
const MAX_RESTARTS = 3;

export function createFileWheel() {
  let child = null;
  let wanted = false;
  let restarts = 0;

  const exe = () =>
    app.isPackaged
      ? path.join(process.resourcesPath, 'fastshot', 'FastShot.exe')
      : path.join(here, '..', '..', 'native', 'out', 'FastShot.exe');

  function launch() {
    const file = exe();
    if (process.platform !== 'win32' || !fs.existsSync(file)) return;
    try {
      child = spawn(file, ['--wheel-host', String(process.pid)], { stdio: 'ignore', windowsHide: true });
    } catch (error) {
      console.error('Dosya çarkı başlatılamadı:', error);
      child = null;
      return;
    }
    const current = child;
    current.on('error', (error) => console.error('Dosya çarkı:', error));
    current.on('exit', (code) => {
      if (child !== current) return;
      child = null;
      if (wanted && code !== 0 && restarts++ < MAX_RESTARTS) setTimeout(() => wanted && !child && launch(), 2000);
    });
  }

  return {
    // On or off, from the "Dosya çarkı" setting.
    set(on) {
      wanted = !!on;
      if (wanted && !child) {
        restarts = 0;
        launch();
      } else if (!wanted && child) {
        const old = child;
        child = null;
        old.kill();
      }
    },
    stop() {
      wanted = false;
      child?.kill();
      child = null;
    },
  };
}
