const chokidar = require('chokidar');
const axios = require('axios');
const https = require('https');
const http = require('http');
const { URL } = require('url');
const fs = require('fs');
const path = require('path');

// Configuration
const CONFIG = {
  watchPath: 'D:\\Onedrive Humpf Tech\\OneDrive - Humpf Tech LLC\\Documents\\3DPrinting\\',
  apiUrl: 'https://new-api.printago.io',
  apiKey: 'dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg',
  storeId: 'sb3bexu83dpm0gry8u265amx'
};

console.log('=== PRINTAGO FOLDER WATCH - COMMAND LINE TEST ===');
console.log('Watching:', CONFIG.watchPath);
console.log('API URL:', CONFIG.apiUrl);
console.log('Store ID:', CONFIG.storeId);
console.log('===============================================\n');

async function uploadFile(filePath) {
  try {
    const fileName = path.basename(filePath);
    const fileBuffer = fs.readFileSync(filePath);

    console.log(`[${new Date().toISOString()}] Uploading: ${fileName}`);

    // Step 1: Get signed upload URL
    console.log('  → Requesting signed URL...');
    const signedUrlResponse = await axios.post(
      `${CONFIG.apiUrl}/v1/storage/signed-upload-urls`,
      { filenames: [fileName] },
      {
        headers: {
          'authorization': `ApiKey ${CONFIG.apiKey}`,
          'x-printago-storeid': CONFIG.storeId,
          'content-type': 'application/json'
        }
      }
    );

    if (!signedUrlResponse.data.signedUrls || signedUrlResponse.data.signedUrls.length === 0) {
      throw new Error('No signed URL returned');
    }

    const uploadUrl = signedUrlResponse.data.signedUrls[0].uploadUrl;
    console.log('  → Got signed URL');

    // Step 2: Upload to signed URL using raw HTTPS to avoid axios adding headers
    console.log('  → Uploading file...');
    await new Promise((resolve, reject) => {
      const parsedUrl = new URL(uploadUrl);
      const requestOptions = {
        hostname: parsedUrl.hostname,
        port: parsedUrl.port || 443,
        path: parsedUrl.pathname + parsedUrl.search,
        method: 'PUT',
        headers: {
          'Content-Length': fileBuffer.length
        }
      };

      const req = https.request(requestOptions, (res) => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          resolve();
        } else {
          let data = '';
          res.on('data', chunk => data += chunk);
          res.on('end', () => reject(new Error(`Upload failed with status ${res.statusCode}: ${data}`)));
        }
      });

      req.on('error', reject);
      req.write(fileBuffer);
      req.end();
    });

    console.log(`  ✓ SUCCESS: ${fileName} uploaded!`);
    return true;
  } catch (error) {
    const errorMsg = error.response?.data?.message || error.message;
    console.error(`  ✗ FAILED: ${errorMsg}`);
    if (error.response) {
      console.error('  Response status:', error.response.status);
      console.error('  Response data:', JSON.stringify(error.response.data, null, 2));
    }
    return false;
  }
}

// Start watching
const watcher = chokidar.watch(CONFIG.watchPath, {
  persistent: true,
  ignoreInitial: true,
  awaitWriteFinish: {
    stabilityThreshold: 2000,
    pollInterval: 100
  }
});

watcher.on('add', async (filePath) => {
  console.log(`\n[${new Date().toISOString()}] File detected: ${path.basename(filePath)}`);
  await uploadFile(filePath);
});

watcher.on('change', async (filePath) => {
  console.log(`\n[${new Date().toISOString()}] File changed: ${path.basename(filePath)}`);
  await uploadFile(filePath);
});

watcher.on('ready', () => {
  console.log('✓ Watcher is ready! Waiting for file changes...\n');
  console.log('Create or modify a file in the watched folder to test upload.');
  console.log('Press Ctrl+C to stop.\n');
});

watcher.on('error', (error) => {
  console.error('[ERROR]', error);
});
