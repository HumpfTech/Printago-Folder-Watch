const https = require('https');
const { URL } = require('url');
const fs = require('fs');
const path = require('path');
const os = require('os');
const chokidar = require('chokidar');
const axios = require('axios');
const readline = require('readline');

// Config
const CONFIG_DIR = path.join(os.homedir(), '.printago-folder-watch');
const CONFIG_FILE = path.join(CONFIG_DIR, 'config.json');

if (!fs.existsSync(CONFIG_DIR)) {
  fs.mkdirSync(CONFIG_DIR, { recursive: true });
}

function loadConfig() {
  try {
    if (fs.existsSync(CONFIG_FILE)) {
      return JSON.parse(fs.readFileSync(CONFIG_FILE, 'utf8'));
    }
  } catch (error) {
    console.error('Error loading config:', error);
  }
  return { watchPath: '', apiUrl: '', apiKey: '', storeId: '' };
}

function saveConfig(configData) {
  try {
    fs.writeFileSync(CONFIG_FILE, JSON.stringify(configData, null, 2), 'utf8');
    return true;
  } catch (error) {
    console.error('Error saving config:', error);
    return false;
  }
}

let watcher = null;
let config = loadConfig();
const uploadQueue = [];
let isUploading = false;

function log(message, type = 'INFO') {
  const timestamp = new Date().toLocaleTimeString();
  console.log(`[${timestamp}] [${type}] ${message}`);
}

async function uploadFile(filePath) {
  try {
    const fileBuffer = fs.readFileSync(filePath);
    const watchPath = config.watchPath.endsWith('\\') || config.watchPath.endsWith('/')
      ? config.watchPath.slice(0, -1)
      : config.watchPath;
    const relativePath = path.relative(watchPath, filePath);
    const cloudPath = relativePath.replace(/\\/g, '/');
    const apiUrl = config.apiUrl.endsWith('/') ? config.apiUrl.slice(0, -1) : config.apiUrl;

    const signedUrlResponse = await axios.post(
      `${apiUrl}/v1/storage/signed-upload-urls`,
      { filenames: [cloudPath] },
      {
        headers: {
          'authorization': `ApiKey ${config.apiKey}`,
          'x-printago-storeid': config.storeId,
          'content-type': 'application/json'
        }
      }
    );

    if (!signedUrlResponse.data.signedUrls || signedUrlResponse.data.signedUrls.length === 0) {
      throw new Error('No signed URL returned');
    }

    const uploadUrl = signedUrlResponse.data.signedUrls[0].uploadUrl;

    await new Promise((resolve, reject) => {
      const parsedUrl = new URL(uploadUrl);
      const req = https.request({
        hostname: parsedUrl.hostname,
        port: parsedUrl.port || 443,
        path: parsedUrl.pathname + parsedUrl.search,
        method: 'PUT',
        headers: { 'Content-Length': fileBuffer.length }
      }, (res) => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          resolve();
        } else {
          let data = '';
          res.on('data', chunk => data += chunk);
          res.on('end', () => reject(new Error(`Upload failed: ${res.statusCode}`)));
        }
      });
      req.on('error', reject);
      req.write(fileBuffer);
      req.end();
    });

    log(`Uploaded: ${cloudPath}`, 'SUCCESS');
    return true;
  } catch (error) {
    const watchPath = config.watchPath.endsWith('\\') || config.watchPath.endsWith('/')
      ? config.watchPath.slice(0, -1)
      : config.watchPath;
    const relativePath = path.relative(watchPath, filePath);
    const cloudPath = relativePath.replace(/\\/g, '/');
    log(`Failed: ${cloudPath} - ${error.message}`, 'ERROR');
    return false;
  }
}

async function processUploadQueue() {
  if (isUploading || uploadQueue.length === 0) return;

  isUploading = true;
  const filePath = uploadQueue.shift();

  try {
    await new Promise(resolve => setTimeout(resolve, 1000));
    if (fs.existsSync(filePath)) {
      await uploadFile(filePath);
    }
  } catch (error) {
    log(`Error: ${error.message}`, 'ERROR');
  }

  isUploading = false;
  if (uploadQueue.length > 0) {
    processUploadQueue();
  }
}

function startWatching() {
  if (!config.watchPath || !config.apiUrl || !config.apiKey || !config.storeId) {
    log('Cannot start - config incomplete', 'ERROR');
    return false;
  }

  if (watcher) stopWatching();

  try {
    watcher = chokidar.watch(config.watchPath, {
      persistent: true,
      ignoreInitial: false,
      awaitWriteFinish: { stabilityThreshold: 2000, pollInterval: 100 }
    });

    watcher.on('add', (filePath) => {
      log(`Detected: ${path.basename(filePath)}`, 'INFO');
      uploadQueue.push(filePath);
      processUploadQueue();
    });

    watcher.on('change', (filePath) => {
      log(`Changed: ${path.basename(filePath)}`, 'INFO');
      uploadQueue.push(filePath);
      processUploadQueue();
    });

    watcher.on('error', (error) => {
      log(`Watcher error: ${error.message}`, 'ERROR');
    });

    log(`Started watching: ${config.watchPath}`, 'SUCCESS');
    return true;
  } catch (error) {
    log(`Failed to start: ${error.message}`, 'ERROR');
    return false;
  }
}

function stopWatching() {
  if (watcher) {
    watcher.close();
    watcher = null;
    log('Stopped watching', 'INFO');
  }
}

function showMenu() {
  console.log('\n╔════════════════════════════════════════╗');
  console.log('║     PRINTAGO FOLDER WATCH - MENU       ║');
  console.log('╠════════════════════════════════════════╣');
  console.log('║  1. Start Watching                     ║');
  console.log('║  2. Stop Watching                      ║');
  console.log('║  3. Configure Settings                 ║');
  console.log('║  4. Show Current Config                ║');
  console.log('║  5. Exit                               ║');
  console.log('╚════════════════════════════════════════╝');
  console.log('');
}

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout
});

function askQuestion(question) {
  return new Promise((resolve) => {
    rl.question(question, (answer) => {
      resolve(answer);
    });
  });
}

async function configure() {
  console.log('\n--- Configuration ---');
  const watchPath = await askQuestion(`Watch Path [${config.watchPath}]: `);
  const apiUrl = await askQuestion(`API URL [${config.apiUrl}]: `);
  const apiKey = await askQuestion(`API Key [${config.apiKey ? '***' : ''}]: `);
  const storeId = await askQuestion(`Store ID [${config.storeId}]: `);

  config = {
    watchPath: watchPath || config.watchPath,
    apiUrl: apiUrl || config.apiUrl,
    apiKey: apiKey || config.apiKey,
    storeId: storeId || config.storeId
  };

  if (saveConfig(config)) {
    log('Configuration saved', 'SUCCESS');
  }
}

function showConfig() {
  console.log('\n--- Current Configuration ---');
  console.log(`Watch Path: ${config.watchPath}`);
  console.log(`API URL: ${config.apiUrl}`);
  console.log(`API Key: ${config.apiKey ? '***' + config.apiKey.slice(-8) : 'Not set'}`);
  console.log(`Store ID: ${config.storeId}`);
}

async function mainLoop() {
  while (true) {
    showMenu();
    const choice = await askQuestion('Enter choice (1-5): ');

    switch (choice.trim()) {
      case '1':
        startWatching();
        break;
      case '2':
        stopWatching();
        break;
      case '3':
        await configure();
        break;
      case '4':
        showConfig();
        break;
      case '5':
        stopWatching();
        rl.close();
        process.exit(0);
        break;
      default:
        console.log('Invalid choice');
    }
  }
}

console.log('\n╔════════════════════════════════════════╗');
console.log('║   PRINTAGO FOLDER WATCH - STARTED      ║');
console.log('╚════════════════════════════════════════╝\n');

mainLoop();

process.on('SIGINT', () => {
  console.log('\nShutting down...');
  stopWatching();
  rl.close();
  process.exit(0);
});
