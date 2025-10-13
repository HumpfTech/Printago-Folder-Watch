console.log('Starting debug test');

try {
  console.log('Process type:', process.type);
  const electron = require('electron');
  console.log('Electron type:', typeof electron);
  console.log('app type:', typeof electron.app);

  if (electron.app) {
    console.log('SUCCESS: app is available');
    electron.app.whenReady().then(() => {
      console.log('App is ready!');
      electron.app.quit();
    });
  } else {
    console.log('FAIL: app is not available');
  }
} catch (err) {
  console.error('Error:', err);
}
