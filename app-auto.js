const https = require('https');
const { URL } = require('url');
const fs = require('fs');
const path = require('path');
const os = require('os');
const chokidar = require('chokidar');
const axios = require('axios');

// Config
const CONFIG_DIR = path.join(os.homedir(), '.printago-folder-watch');
const CONFIG_FILE = path.join(CONFIG_DIR, 'config.json');

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
    const fileName = path.basename(filePath);
    const apiUrl = config.apiUrl.endsWith('/') ? config.apiUrl.slice(0, -1) : config.apiUrl;

    // Step 1: Get signed URL
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
    const storagePath = signedUrlResponse.data.signedUrls[0].path;

    // Step 2: Upload to signed URL
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

    // Step 3: Create part in Printago (only for 3D model files)
    const ext = path.extname(filePath).toLowerCase();
    if (ext === '.3mf' || ext === '.stl') {
      const partName = fileName.replace(/\.(3mf|stl)$/i, '');
      const partType = ext === '.3mf' ? '3mf' : 'stl';

      await axios.post(
        `${apiUrl}/v1/parts`,
        {
          name: partName,
          type: partType,
          description: '',
          fileUris: [storagePath],
          parameters: [],
          printTags: {},
          overriddenProcessProfileId: null
        },
        {
          headers: {
            'authorization': `ApiKey ${config.apiKey}`,
            'x-printago-storeid': config.storeId,
            'content-type': 'application/json'
          }
        }
      );
      log(`Created part: ${partName}`, 'SUCCESS');
    } else {
      log(`Uploaded: ${cloudPath}`, 'SUCCESS');
    }

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

if (!config.watchPath || !config.apiUrl || !config.apiKey || !config.storeId) {
  log('Configuration incomplete - please configure settings', 'ERROR');
  process.exit(1);
}

const watcher = chokidar.watch(config.watchPath, {
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

process.on('SIGINT', () => {
  log('Shutting down...', 'INFO');
  watcher.close();
  process.exit(0);
});

process.on('SIGTERM', () => {
  log('Shutting down...', 'INFO');
  watcher.close();
  process.exit(0);
});
