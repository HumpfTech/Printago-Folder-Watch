const https = require('https');
const fs = require('fs');
const path = require('path');

const config = {
  apiUrl: 'https://new-api.printago.io',
  apiKey: 'dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg',
  storeId: 'sb3bexu83dpm0gry8u265amx'
};

const testFile = 'D:\\Onedrive Humpf Tech\\OneDrive - Humpf Tech LLC\\Documents\\3DPrinting\\Night Spirit_v1_2_og.3mf';

async function uploadAndCreatePart(filePath) {
  const fileName = path.basename(filePath);
  const fileBuffer = fs.readFileSync(filePath);

  console.log(`\n[1/3] Getting signed URL for: ${fileName}`);

  // Step 1: Get signed URL
  const signedUrlData = await new Promise((resolve, reject) => {
    const postData = JSON.stringify({ filenames: [fileName] });
    const options = {
      hostname: 'new-api.printago.io',
      path: '/v1/storage/signed-upload-urls',
      method: 'POST',
      headers: {
        'authorization': `ApiKey ${config.apiKey}`,
        'x-printago-storeid': config.storeId,
        'content-type': 'application/json',
        'Content-Length': postData.length
      }
    };

    const req = https.request(options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          resolve(JSON.parse(data));
        } else {
          reject(new Error(`Failed to get signed URL: ${res.statusCode} - ${data}`));
        }
      });
    });
    req.on('error', reject);
    req.write(postData);
    req.end();
  });

  const uploadUrl = signedUrlData.signedUrls[0].uploadUrl;
  const storagePath = signedUrlData.signedUrls[0].path;
  console.log(`✓ Got signed URL`);
  console.log(`  Storage path: ${storagePath}`);

  // Step 2: Upload to signed URL
  console.log(`\n[2/3] Uploading file to Google Cloud Storage...`);
  await new Promise((resolve, reject) => {
    const parsedUrl = new URL(uploadUrl);
    const uploadReq = https.request({
      hostname: parsedUrl.hostname,
      port: 443,
      path: parsedUrl.pathname + parsedUrl.search,
      method: 'PUT',
      headers: { 'Content-Length': fileBuffer.length }
    }, (uploadRes) => {
      if (uploadRes.statusCode >= 200 && uploadRes.statusCode < 300) {
        resolve();
      } else {
        let data = '';
        uploadRes.on('data', chunk => data += chunk);
        uploadRes.on('end', () => reject(new Error(`Upload failed: ${uploadRes.statusCode} - ${data}`)));
      }
    });
    uploadReq.on('error', reject);
    uploadReq.write(fileBuffer);
    uploadReq.end();
  });
  console.log(`✓ File uploaded to Google Cloud Storage`);

  // Step 3: Create part in Printago
  console.log(`\n[3/3] Creating part in Printago...`);
  const partName = fileName.replace(/\.3mf$/i, '');
  const partData = await new Promise((resolve, reject) => {
    const postData = JSON.stringify({
      name: partName,
      type: '3mf',
      description: '',
      fileUris: [storagePath],
      parameters: [],
      printTags: {},
      overriddenProcessProfileId: null
    });

    const options = {
      hostname: 'new-api.printago.io',
      path: '/v1/parts',
      method: 'POST',
      headers: {
        'authorization': `ApiKey ${config.apiKey}`,
        'x-printago-storeid': config.storeId,
        'content-type': 'application/json',
        'Content-Length': postData.length
      }
    };

    const req = https.request(options, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          resolve(JSON.parse(data));
        } else {
          reject(new Error(`Failed to create part: ${res.statusCode} - ${data}`));
        }
      });
    });
    req.on('error', reject);
    req.write(postData);
    req.end();
  });

  console.log(`✓ Part created successfully!`);
  console.log(`  Part ID: ${partData.id}`);
  console.log(`  Part Name: ${partData.name}`);
  console.log(`\n✅ COMPLETE! Part should now be visible in Printago GUI.`);
}

uploadAndCreatePart(testFile).catch(err => {
  console.error('❌ ERROR:', err.message);
  process.exit(1);
});
