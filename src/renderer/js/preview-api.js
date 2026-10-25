// Browser stand-in for the Electron bridge: same calls, state kept in
// localStorage and shared between tabs (index.html and widget.html).
import { dispatch, normalizeState } from '../../shared/store.js';
import { seedState } from '../../shared/seed.js';
import { parseTasksCsv } from '../../shared/tasks-csv.js';

const KEY = 'xloer-preview-state';
const NAV_KEY = 'xloer-preview-nav';

export function createPreviewApi() {
  let state;
  try {
    state = normalizeState(JSON.parse(localStorage.getItem(KEY)));
  } catch {
    state = seedState();
  }
  let rev = 0;
  const stateListeners = new Set();
  const navListeners = new Set();

  const emit = () => {
    const payload = { state: structuredClone(state), rev: ++rev };
    for (const cb of stateListeners) setTimeout(() => cb(payload));
  };
  const save = () => {
    try {
      localStorage.setItem(KEY, JSON.stringify(state));
    } catch {}
  };
  addEventListener('storage', (e) => {
    if (e.key === KEY && e.newValue) {
      try {
        state = normalizeState(JSON.parse(e.newValue));
        emit();
      } catch {}
    } else if (e.key === NAV_KEY && e.newValue) {
      const { target } = JSON.parse(e.newValue);
      for (const cb of navListeners) cb(target);
    }
  });

  const apply = (action) => {
    try {
      const result = dispatch(state, structuredClone(action));
      save();
      emit();
      return { ok: true, result };
    } catch (err) {
      return { ok: false, error: err.message };
    }
  };

  const navigate = (target) => {
    try {
      localStorage.setItem(NAV_KEY, JSON.stringify({ target, at: Date.now() }));
    } catch {}
  };

  // Updates only exist in the installed app. Setting localStorage
  // 'xloer-preview-update' to 'demo' plays a fake update for UI work.
  let demo = false;
  try {
    demo = localStorage.getItem('xloer-preview-update') === 'demo';
  } catch {}
  let update = { status: demo ? 'idle' : 'unsupported', current: '1.1.0', version: null, notes: '', percent: 0, error: null, portable: false, checkedAt: null };
  const updateListeners = new Set();
  const setUpdate = (patch) => {
    update = { ...update, ...patch };
    for (const cb of updateListeners) cb(update);
  };

  const pickFile = (accept) =>
    new Promise((resolve) => {
      const input = Object.assign(document.createElement('input'), { type: 'file', accept });
      input.addEventListener('change', () => resolve(input.files[0] ?? null));
      input.click();
    });

  return {
    platform: 'web',
    getState: async () => ({ state: structuredClone(state), rev }),
    dispatch: async (action) => apply(action),
    onState: (cb) => (stateListeners.add(cb), () => stateListeners.delete(cb)),
    onNavigate: (cb) => (navListeners.add(cb), () => navListeners.delete(cb)),
    takePendingNav: async () => null,
    openTask: async (id) => navigate({ type: 'task', id }),
    showMain: async () => {},
    importCsv: async () => {
      const file = await pickFile('.csv');
      if (!file) return { ok: false, canceled: true };
      try {
        const parsed = parseTasksCsv(await file.text());
        const r = apply({ type: 'import/merge', lists: parsed.lists });
        return r.ok ? { ok: true, result: { ...r.result, rows: parsed.count } } : r;
      } catch (err) {
        return { ok: false, error: err.message };
      }
    },
    exportJson: async () => {
      const a = Object.assign(document.createElement('a'), {
        href: URL.createObjectURL(new Blob([JSON.stringify(state, null, 2)], { type: 'application/json' })),
        download: 'Xloer-yedek.json',
      });
      a.click();
      return { ok: true, path: a.download };
    },
    importJson: async () => {
      const file = await pickFile('.json');
      if (!file) return { ok: false, canceled: true };
      try {
        return apply({ type: 'state/replace', state: JSON.parse(await file.text()) });
      } catch (err) {
        return { ok: false, error: err.message };
      }
    },
    openDataDir: async () => {},
    copyRich: async ({ text, html }) => {
      try {
        await navigator.clipboard.write([
          new ClipboardItem({ 'text/html': new Blob([html], { type: 'text/html' }), 'text/plain': new Blob([text], { type: 'text/plain' }) }),
        ]);
      } catch {
        await navigator.clipboard.writeText(text);
      }
    },
    appInfo: async () => ({ version: 'önizleme', platform: 'web', dataFile: 'Tarayıcı localStorage' }),
    update: {
      get: async () => update,
      check: async () => {
        if (!demo) return update;
        setUpdate({ status: 'checking' });
        setTimeout(() => setUpdate({ status: 'available', version: '1.2.0', notes: '• Örnek değişiklik\n• Başka bir düzeltme', checkedAt: Date.now() }), 500);
        return update;
      },
      download: async () => {
        if (!demo) return update;
        let percent = 0;
        const t = setInterval(() => {
          percent += 25;
          if (percent < 100) return setUpdate({ status: 'downloading', percent });
          clearInterval(t);
          setUpdate({ status: 'downloaded', percent: 100 });
        }, 300);
        return update;
      },
      install: async () => setUpdate({ status: 'none', version: null }),
      openPage: async () => open('https://github.com/ati-space/xloer/releases/latest', '_blank'),
      openPanel: async () => navigate({ type: 'update' }),
      onState: (cb) => (updateListeners.add(cb), () => updateListeners.delete(cb)),
    },
  };
}
