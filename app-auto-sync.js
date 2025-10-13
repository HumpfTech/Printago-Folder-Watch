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
const folderCache = {}; // Cache folder IDs to avoid recreating

function log(message, type = 'INFO') {
  const timestamp = new Date().toLocaleTimeString();
  console.log(`[${timestamp}] [${type}] ${message}`);
}

async function getOrCreateFolder(folderPath, apiUrl) {
  if (!folderPath || folderPath === '.') return null;
  if (folderCache[folderPath]) return folderCache[folderPath];

  const folderParts = folderPath.split('/').filter(p => p);
  let parentId = null;
  let currentPath = '';

  for (const folderName of folderParts) {
    currentPath = currentPath ? `${currentPath}/${folderName}` : folderName;

    if (folderCache[currentPath]) {
      parentId = folderCache[currentPath];
      continue;
    }

    // Check if folder exists
    const foldersResponse = await axios.get(`${apiUrl}/v1/folders`, {
      headers: {
        'authorization': `ApiKey ${config.apiKey}`,
        'x-printago-storeid': config.storeId
      }
    });

    const existingFolder = foldersResponse.data.find(
      f => f.name === folderName && f.parentId === parentId
    );

    if (existingFolder) {
      folderCache[currentPath] = existingFolder.id;
      parentId = existingFolder.id;
    } else {
      // Create folder
      const createResponse = await axios.post(
        `${apiUrl}/v1/folders`,
        {
          name: folderName,
          type: 'part',
          parentId: parentId
        },
        {
          headers: {
            'authorization': `ApiKey ${config.apiKey}`,
            'x-printago-storeid': config.storeId,
            'content-type': 'application/json'
          }
        }
      );
      folderCache[currentPath] = createResponse.data.id;
      parentId = createResponse.data.id;
      log(`Created folder: ${currentPath}`, 'INFO');
    }
  }

  return parentId;
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
    const folderPath = path.dirname(cloudPath).replace(/\\/g, '/');
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

      // Get or create folder
      const folderId = await getOrCreateFolder(folderPath, apiUrl);

      await axios.post(
        `${apiUrl}/v1/parts`,
        {
          name: partName,
          type: partType,
          description: '',
          fileUris: [storagePath],
          parameters: [],
          printTags: {},
          overriddenProcessProfileId: null,
          folderId: folderId
        },
        {
          headers: {
            'authorization': `ApiKey ${config.apiKey}`,
            'x-printago-storeid': config.storeId,
            'content-type': 'application/json'
          }
        }
      );
      const folderInfo = folderId ? ` in folder ${folderPath}` : '';
      log(`Created part: ${partName}${folderInfo}`, 'SUCCESS');
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

async function syncParts() {
  try {
    const apiUrl = config.apiUrl.endsWith('/') ? config.apiUrl.slice(0, -1) : config.apiUrl;
    const watchPath = config.watchPath.endsWith('\\') || config.watchPath.endsWith('/')
      ? config.watchPath.slice(0, -1)
      : config.watchPath;

    // Get all local 3D files
    const localFiles = new Set();
    function scanDir(dir) {
      const files = fs.readdirSync(dir);
      for (const file of files) {
        const fullPath = path.join(dir, file);
        const stat = fs.statSync(fullPath);
        if (stat.isDirectory()) {
          scanDir(fullPath);
        } else {
          const ext = path.extname(file).toLowerCase();
          if (ext === '.3mf' || ext === '.stl') {
            const relativePath = path.relative(watchPath, fullPath).replace(/\\/g, '/');
            const partName = path.basename(file, ext);
            localFiles.add(partName);
          }
        }
      }
    }
    scanDir(watchPath);

    // Get all parts from Printago
    const partsResponse = await axios.get(`${apiUrl}/v1/parts?limit=10000`, {
      headers: {
        'authorization': `ApiKey ${config.apiKey}`,
        'x-printago-storeid': config.storeId
      }
    });

    // Delete parts that don't exist locally
    let deletedCount = 0;
    for (const part of partsResponse.data) {
      if (!localFiles.has(part.name)) {
        await axios.delete(`${apiUrl}/v1/parts/${part.id}`, {
          headers: {
            'authorization': `ApiKey ${config.apiKey}`,
            'x-printago-storeid': config.storeId
          }
        });
        log(`Deleted part: ${part.name} (not found locally)`, 'INFO');
        deletedCount++;
      }
    }

    if (deletedCount > 0) {
      log(`Sync complete: deleted ${deletedCount} parts`, 'SUCCESS');
    }
  } catch (error) {
    log(`Sync error: ${error.message}`, 'ERROR');
  }
}

async function processUploadQueue() {
  if (isUploading || uploadQueue.length === 0) return;

  isUploading = true;
  const filePath = uploadQueue.shift();

  try {
    await new Promise(resolve => setTimeout(resolve, 3000)); // 3 second delay to avoid rate limiting
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

watcher.on('unlink', async (filePath) => {
  const ext = path.extname(filePath).toLowerCase();
  if (ext === '.3mf' || ext === '.stl') {
    log(`File deleted locally: ${path.basename(filePath)}`, 'INFO');
    // Trigger sync to delete from Printago
    setTimeout(() => syncParts(), 5000);
  }
});

watcher.on('error', (error) => {
  log(`Watcher error: ${error.message}`, 'ERROR');
});

log(`Started watching: ${config.watchPath}`, 'SUCCESS');

// Run initial sync after all files are detected
watcher.on('ready', () => {
  setTimeout(() => {
    log('Running initial sync...', 'INFO');
    syncParts();
  }, 10000); // Wait 10 seconds for initial uploads to process
});

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
