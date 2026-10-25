// The only bridge between the windows and the main process.
const { contextBridge, ipcRenderer } = require('electron');

function listen(channel, cb) {
  const handler = (_event, payload) => cb(payload);
  ipcRenderer.on(channel, handler);
  return () => ipcRenderer.removeListener(channel, handler);
}

contextBridge.exposeInMainWorld('xloer', {
  platform: process.platform,
  getState: () => ipcRenderer.invoke('state:get'),
  dispatch: (action) => ipcRenderer.invoke('dispatch', action),
  onState: (cb) => listen('state:changed', cb),
  onNavigate: (cb) => listen('nav', cb),
  takePendingNav: () => ipcRenderer.invoke('nav:take'),
  openTask: (id) => ipcRenderer.invoke('app:open-task', id),
  showMain: () => ipcRenderer.invoke('app:show-main'),
  importCsv: () => ipcRenderer.invoke('data:import-csv'),
  exportJson: () => ipcRenderer.invoke('data:export-json'),
  importJson: () => ipcRenderer.invoke('data:import-json'),
  openDataDir: () => ipcRenderer.invoke('data:open-dir'),
  appInfo: () => ipcRenderer.invoke('app:info'),
  copyRich: (content) => ipcRenderer.invoke('clipboard:write', content),
  update: {
    get: () => ipcRenderer.invoke('update:get'),
    check: () => ipcRenderer.invoke('update:check'),
    download: () => ipcRenderer.invoke('update:download'),
    install: () => ipcRenderer.invoke('update:install'),
    openPage: () => ipcRenderer.invoke('update:open-page'),
    openPanel: () => ipcRenderer.invoke('update:open-panel'),
    onState: (cb) => listen('update:state', cb),
  },
});
