// FastShot inside Xloer: screenshots (Print Screen), screen recording
// (Shift+Print Screen), the editor, history and the file wheel (drag files in
// Explorer and tap Shift), for as long as Xloer runs.
//
// It is FastShot.exe started with --embedded <Xloer's pid> (built from
// native/fastshot by scripts/build-fastshot.js, shipped in resources/fastshot).
// It shows no tray icon of its own; the tray menu's FastShot items send it
// commands, one per line on its stdin, and it reports its shortcuts and
// whether it is recording as JSON lines on its stdout. It quits when its
// stdin closes or Xloer's process ends. While a standalone FastShot runs, it
// waits and passes the commands on to that one.

import { app } from 'electron';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
// A FastShot that keeps crashing is left off until Xloer starts again.
const MAX_RESTARTS = 3;

export function createFastShot({ onState } = {}) {
  let child = null;
  let wanted = false;
  let restarts = 0;
  let state = { screenshot: null, record: null, recording: false };

  const exe = () =>
    app.isPackaged
      ? path.join(process.resourcesPath, 'fastshot', 'FastShot.exe')
      : path.join(here, '..', '..', 'native', 'out', 'FastShot.exe');

  function setState(next) {
    state = next;
    onState?.(state);
  }

  function launch() {
    const file = exe();
    if (process.platform !== 'win32' || !fs.existsSync(file)) return;
    try {
      child = spawn(file, ['--embedded', String(process.pid)], { stdio: ['pipe', 'pipe', 'ignore'], windowsHide: true });
    } catch (error) {
      console.error('FastShot başlatılamadı:', error);
      child = null;
      return;
    }
    const current = child;
    let buffer = '';
    current.stdout.setEncoding('utf8');
    current.stdout.on('data', (chunk) => {
      buffer += chunk;
      let end;
      while ((end = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, end).trim();
        buffer = buffer.slice(end + 1);
        try {
          const message = JSON.parse(line);
          if (message.type === 'state') setState({ screenshot: message.screenshot ?? null, record: message.record ?? null, recording: !!message.recording });
        } catch {
          // Not a message for us.
        }
      }
    });
    current.stdin.on('error', () => {});
    current.on('error', (error) => console.error('FastShot:', error));
    current.on('exit', (code) => {
      if (child !== current) return;
      child = null;
      setState({ screenshot: null, record: null, recording: false });
      if (wanted && code !== 0 && restarts++ < MAX_RESTARTS) setTimeout(() => wanted && !child && launch(), 2000);
    });
    onState?.(state);
  }

  function close(old) {
    // Closing stdin lets FastShot finish what it is doing and quit; a stuck one is ended after a while.
    old.stdin.end();
    setTimeout(() => old.exitCode === null && old.kill(), 5000).unref();
  }

  return {
    get running() {
      return !!child;
    },
    get state() {
      return state;
    },
    // On or off, from the FastShot setting.
    set(on) {
      wanted = !!on;
      if (wanted && !child) {
        restarts = 0;
        launch();
      } else if (!wanted && child) {
        const old = child;
        child = null;
        close(old);
        setState({ screenshot: null, record: null, recording: false });
      }
    },
    // screenshot, record, edit-last, history, open, wheel or settings.
    command(name) {
      if (child?.stdin.writable) child.stdin.write(`${name}\n`);
    },
    stop() {
      wanted = false;
      if (child) close(child);
      child = null;
    },
  };
}
