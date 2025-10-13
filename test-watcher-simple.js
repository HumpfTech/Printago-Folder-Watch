const chokidar = require('chokidar');
const watchPath = 'D:\\Onedrive Humpf Tech\\OneDrive - Humpf Tech LLC\\Documents\\3DPrinting\\';

console.log('Starting watcher on:', watchPath);

const watcher = chokidar.watch(watchPath, {
  persistent: true,
  ignoreInitial: true,
  awaitWriteFinish: {
    stabilityThreshold: 2000,
    pollInterval: 100
  }
});

watcher.on('add', (path) => {
  console.log('[ADD]', path);
});

watcher.on('change', (path) => {
  console.log('[CHANGE]', path);
});

watcher.on('ready', () => {
  console.log('Watcher is ready and watching!');
});

watcher.on('error', (error) => {
  console.error('[ERROR]', error);
});

console.log('Watcher configured. Waiting for file changes...');
