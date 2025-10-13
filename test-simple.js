console.log('Loading electron...');
const electron = require('electron');
console.log('Electron type:', typeof electron);
console.log('Electron value:', electron);
console.log('Electron.app:', electron.app);

if (electron.app) {
  electron.app.whenReady().then(() => {
    console.log('Electron is ready!');
    electron.app.quit();
  });
} else {
  console.error('ERROR: electron.app is undefined!');
}
