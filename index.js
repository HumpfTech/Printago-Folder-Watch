const { app, BrowserWindow } = require('electron');

app.whenReady().then(() => {
  const win = new BrowserWindow({ width: 800, height: 600 });
  win.loadURL('https://google.com');
});

app.on('window-all-closed', () => {
  app.quit();
});
