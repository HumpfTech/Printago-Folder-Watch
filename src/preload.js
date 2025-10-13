const { contextBridge, ipcRenderer } = require('electron');

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  selectDirectory: () => ipcRenderer.invoke('select-directory'),
  saveConfig: (config) => ipcRenderer.invoke('save-config', config),
  startWatching: () => ipcRenderer.invoke('start-watching'),
  stopWatching: () => ipcRenderer.invoke('stop-watching'),
  getConfig: () => ipcRenderer.invoke('get-config'),

  // Event listeners
  onConfigLoaded: (callback) => ipcRenderer.on('config-loaded', (event, config) => callback(config)),
  onFileEvent: (callback) => ipcRenderer.on('file-event', (event, data) => callback(data)),
  onUploadSuccess: (callback) => ipcRenderer.on('upload-success', (event, filePath) => callback(filePath)),
  onUploadError: (callback) => ipcRenderer.on('upload-error', (event, data) => callback(data))
});
