console.log('[DEBUG] Starting main.js...');
console.log('[DEBUG] Process versions:', process.versions);
console.log('[DEBUG] About to require electron...');
console.log('[DEBUG] Module paths:', require.resolve.paths('electron'));
const electron = require('electron');
console.log('[DEBUG] Electron required successfully:', typeof electron);
console.log('[DEBUG] Electron value:', electron);
console.log('[DEBUG] Electron.app:', typeof electron.app);

const { app, BrowserWindow, ipcMain, dialog, Tray, Menu, nativeImage } = electron;
console.log('[DEBUG] Destructured app:', typeof app);

const path = require('path');
const FileWatcher = require('./src/fileWatcher');
const PrintagoClient = require('./src/printagoClient');

let mainWindow;
let fileWatcher;
let printagoClient;
let tray = null;
let store;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 900,
    height: 700,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'src', 'preload.js')
    }
  });

  mainWindow.loadFile('src/index.html');

  if (process.argv.includes('--dev')) {
    mainWindow.webContents.openDevTools();
  }

  // Load saved configuration
  if (store) {
    const config = store.get('config', {});
    if (config.watchPath && config.apiUrl && config.apiKey) {
      mainWindow.webContents.on('did-finish-load', () => {
        mainWindow.webContents.send('config-loaded', config);
      });
    }
  }

  // Don't quit on window close, minimize to tray instead
  mainWindow.on('close', (event) => {
    if (!app.isQuitting) {
      event.preventDefault();
      mainWindow.hide();
    }
  });
}

function createTray() {
  // Create a simple tray icon (text-based for now)
  const icon = nativeImage.createFromDataURL('data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAABHNCSVQICAgIfAhkiAAAAAlwSFlzAAAA7AAAAOwBeShxvQAAABl0RVh0U29mdHdhcmUAd3d3Lmlua3NjYXBlLm9yZ5vuPBoAAAIESURBVFiF7ZbPaxNBFMc/b7PZbDZp0zRtkralVUFQQfCgB0EQBEH/Af+BHvwHxIMX8eBBELx5UhA8eBEvggdBEMGDIIJ4EKzUH7W2SZqkTbLJZmcm+1OzSZs0u0nBg1+YZWeG/X7eezNvZgT/8x8P5P8uQEqZBvIA+aALkFJOAO8A+6ALmAQWgMTgC5BSjgFLQMbr9XpdLpfb4/Fo+v2+3W63O61Wq9Fqtb5bllVxHMcEXkmSZAAvgBvAyUEXsAAU/H7/xNTU1Njc3Nyky+XS+/p7vR6WZWHbNvV6vVqpVD5qmvZRCLEE3Ad+DLIAAQwDp48cOZKfmZkZDofD7h0Fd7lc6LqO3++n2WxSqVQwDGNZCPEcWASqu+0TGAgAmampqaPZbHY0FArta+Oz0DSNcDhMJBIhFotN67r+RgjxBngkpTR34iP2mgBSSg+QA65PTExkstmsHg6HdSFEX4hBsizbQog/QDKZTOTz+XQgEDh1+/btl8AToLSdzy4BWeDa9PR0Lh6PB7xer77xwXF2nuva+hRc1zUcx2E0FkscO3bs/Pnz54+dOHHiFfAQWNlOcM8EpJQBIAcUstlsLh6P+7cSUFUVVVW3/E/XdQzD+FUsFj+tC3gAPJZSNrcT3JaAlPIC8PD48eN5wzACqqpu6aCqKoqi7CigKApCCARQqVTeA99M03wPvN0q+D97gL8sMZbRPcMsIQAAAABJRU5ErkJggg==');

  tray = new Tray(icon);

  const contextMenu = Menu.buildFromTemplate([
    {
      label: 'Show Printago Folder Watch',
      click: () => {
        mainWindow.show();
      }
    },
    {
      label: 'Status',
      enabled: false
    },
    {
      type: 'separator'
    },
    {
      label: 'Quit',
      click: () => {
        app.isQuitting = true;
        app.quit();
      }
    }
  ]);

  tray.setToolTip('Printago Folder Watch');
  tray.setContextMenu(contextMenu);

  tray.on('click', () => {
    mainWindow.show();
  });
}

app.whenReady().then(async () => {
  // Initialize electron-store
  const Store = require('electron-store');
  store = new Store();

  createWindow();
  createTray();
});

app.on('window-all-closed', () => {
  // Don't quit on window close on Windows
  if (process.platform === 'darwin') {
    app.quit();
  }
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
  }
});

app.on('before-quit', () => {
  if (fileWatcher) {
    fileWatcher.stop();
  }
});

// IPC Handlers
ipcMain.handle('select-directory', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    properties: ['openDirectory']
  });

  if (!result.canceled && result.filePaths.length > 0) {
    return result.filePaths[0];
  }
  return null;
});

ipcMain.handle('save-config', async (event, config) => {
  try {
    // Validate inputs
    if (!config.watchPath || !config.apiUrl || !config.apiKey) {
      throw new Error('All fields are required');
    }

    // Validate API URL format
    const urlPattern = /^https?:\/\/.+/i;
    if (!urlPattern.test(config.apiUrl)) {
      throw new Error('Invalid API URL format');
    }

    // Save configuration securely
    store.set('config', config);

    // Initialize Printago client
    printagoClient = new PrintagoClient(config.apiUrl, config.apiKey);

    // Test connection (skip for now if API not ready)
    // const isValid = await printagoClient.testConnection();
    // if (!isValid) {
    //   throw new Error('Failed to connect to Printago API. Please check your credentials.');
    // }

    return { success: true };
  } catch (error) {
    return { success: false, error: error.message };
  }
});

ipcMain.handle('start-watching', async () => {
  try {
    const config = store.get('config');

    if (!config || !config.watchPath) {
      throw new Error('No watch path configured');
    }

    if (!printagoClient) {
      printagoClient = new PrintagoClient(config.apiUrl, config.apiKey);
    }

    // Stop existing watcher if any
    if (fileWatcher) {
      fileWatcher.stop();
    }

    // Create new watcher
    fileWatcher = new FileWatcher(config.watchPath, printagoClient);

    fileWatcher.on('file-added', (filePath) => {
      mainWindow.webContents.send('file-event', { type: 'added', path: filePath });
    });

    fileWatcher.on('file-changed', (filePath) => {
      mainWindow.webContents.send('file-event', { type: 'changed', path: filePath });
    });

    fileWatcher.on('file-deleted', (filePath) => {
      mainWindow.webContents.send('file-event', { type: 'deleted', path: filePath });
    });

    fileWatcher.on('upload-success', (filePath) => {
      mainWindow.webContents.send('upload-success', filePath);
    });

    fileWatcher.on('upload-error', (filePath, error) => {
      mainWindow.webContents.send('upload-error', { path: filePath, error: error.message });
    });

    fileWatcher.start();

    // Update tray menu
    updateTrayMenu(true);

    return { success: true };
  } catch (error) {
    return { success: false, error: error.message };
  }
});

ipcMain.handle('stop-watching', async () => {
  if (fileWatcher) {
    fileWatcher.stop();
  }

  // Update tray menu
  updateTrayMenu(false);

  return { success: true };
});

ipcMain.handle('get-config', async () => {
  return store.get('config', {});
});

function updateTrayMenu(isWatching) {
  if (!tray) return;

  const contextMenu = Menu.buildFromTemplate([
    {
      label: 'Show Printago Folder Watch',
      click: () => {
        mainWindow.show();
      }
    },
    {
      label: isWatching ? 'Status: Watching' : 'Status: Stopped',
      enabled: false
    },
    {
      type: 'separator'
    },
    {
      label: 'Quit',
      click: () => {
        app.isQuitting = true;
        app.quit();
      }
    }
  ]);

  tray.setContextMenu(contextMenu);
}
