try {
  const electron = require('electron');
  console.log('Electron module:', typeof electron);
  console.log('Electron:', Object.keys(electron));

  if (electron.app) {
    console.log('app found');
  } else {
    console.log('app is undefined - this is the problem');
  }
} catch (error) {
  console.error('Error:', error);
}
