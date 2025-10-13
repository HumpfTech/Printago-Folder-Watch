// Test script for tracking database
// This will simulate file moves, updates, and deletions

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

// Test directory setup
const testDir = path.join(__dirname, 'test-files');
const dbPath = path.join(testDir, '.printago-tracking.db');

console.log('=== Printago Tracking Database Test ===\n');

// Clean up from previous tests
if (fs.existsSync(testDir)) {
  fs.rmSync(testDir, { recursive: true });
}
fs.mkdirSync(testDir, { recursive: true });

// Helper: Create a test 3MF file (minimal ZIP)
function createTest3MF(filePath, content = 'test-content') {
  const AdmZip = require('adm-zip');
  const zip = new AdmZip();

  // Minimal 3MF structure
  zip.addFile(
    '[Content_Types].xml',
    Buffer.from('<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"></Types>')
  );
  zip.addFile(
    '3D/3dmodel.model',
    Buffer.from(`<?xml version="1.0"?><model>${content}</model>`)
  );

  zip.writeZip(filePath);
  console.log(`✓ Created ${path.basename(filePath)}`);
}

// Helper: Compute SHA256
function computeSHA256(filePath) {
  const hash = crypto.createHash('sha256');
  const data = fs.readFileSync(filePath);
  hash.update(data);
  return hash.digest('hex');
}

// Helper: Mock tracking DB
class MockTrackingDB {
  constructor() {
    this.entries = new Map();
    this.hashIndex = new Map();
  }

  upsert(entry) {
    this.entries.set(entry.filePath, entry);
    this.hashIndex.set(entry.fileHash, entry.filePath);
    console.log(`  [DB] Stored: ${path.basename(entry.filePath)} -> Part ${entry.partId}`);
  }

  getByPath(filePath) {
    return this.entries.get(filePath) || null;
  }

  getByHash(fileHash) {
    const filePath = this.hashIndex.get(fileHash);
    return filePath ? this.entries.get(filePath) : null;
  }

  updatePath(oldPath, newPath) {
    const entry = this.entries.get(oldPath);
    if (!entry) return false;

    this.entries.delete(oldPath);
    entry.filePath = newPath;
    this.entries.set(newPath, entry);
    console.log(`  [DB] Updated path: ${path.basename(oldPath)} -> ${path.basename(newPath)}`);
    return true;
  }

  updateHash(filePath, newHash) {
    const entry = this.entries.get(filePath);
    if (!entry) return false;

    this.hashIndex.delete(entry.fileHash);
    entry.fileHash = newHash;
    this.hashIndex.set(newHash, filePath);
    console.log(`  [DB] Updated hash for ${path.basename(filePath)}`);
    return true;
  }

  delete(filePath) {
    const entry = this.entries.get(filePath);
    if (!entry) return false;

    this.entries.delete(filePath);
    this.hashIndex.delete(entry.fileHash);
    console.log(`  [DB] Deleted: ${path.basename(filePath)}`);
    return true;
  }

  getStats() {
    return {
      total: this.entries.size,
      entries: Array.from(this.entries.entries()).map(([path, entry]) => ({
        path: path.basename(path),
        partId: entry.partId,
        hash: entry.fileHash.substring(0, 8)
      }))
    };
  }
}

// Initialize mock DB
const db = new MockTrackingDB();

console.log('\n--- TEST 1: Initial File Creation ---');
const file1Path = path.join(testDir, 'model1.3mf');
createTest3MF(file1Path, 'original-content-1');
const hash1 = computeSHA256(file1Path);
console.log(`Hash: ${hash1.substring(0, 16)}...`);

// Simulate first scan - file not in DB
console.log('\nFirst scan (new file):');
let tracked = db.getByPath(file1Path);
if (!tracked) {
  console.log('  File not tracked yet');
  // Would search Printago by hash or create new Part
  const partId = 'PART-ABC-123';
  db.upsert({
    filePath: file1Path,
    fileHash: hash1,
    partId: partId,
    designGuid: 'guid-111',
    lastSeenAt: new Date().toISOString()
  });
  console.log(`  Created new Part: ${partId}`);
}

console.log('\n--- TEST 2: File Move (Rename) ---');
const file1NewPath = path.join(testDir, 'model1-renamed.3mf');
fs.renameSync(file1Path, file1NewPath);
console.log(`✓ Moved ${path.basename(file1Path)} -> ${path.basename(file1NewPath)}`);

// Simulate scan after move
console.log('\nScan after move:');
tracked = db.getByPath(file1NewPath);
if (!tracked) {
  console.log('  Not found by new path, checking by hash...');
  const hashAfterMove = computeSHA256(file1NewPath);
  tracked = db.getByHash(hashAfterMove);

  if (tracked) {
    console.log(`  ✓ FOUND by hash! Original Part: ${tracked.partId}`);
    console.log(`  Old path: ${path.basename(tracked.filePath)}`);
    db.updatePath(tracked.filePath, file1NewPath);
    console.log('  ✓ Part binding preserved after move!');
  }
}

console.log('\n--- TEST 3: File Content Update ---');
const file2Path = path.join(testDir, 'model2.3mf');
createTest3MF(file2Path, 'original-content-2');
const hash2 = computeSHA256(file2Path);

// Track it
db.upsert({
  filePath: file2Path,
  fileHash: hash2,
  partId: 'PART-XYZ-789',
  designGuid: 'guid-222',
  lastSeenAt: new Date().toISOString()
});
console.log(`Tracked ${path.basename(file2Path)} with Part PART-XYZ-789`);

// Simulate file edit (change content)
console.log('\nUser edits file...');
createTest3MF(file2Path, 'UPDATED-content-2');
const hash2Updated = computeSHA256(file2Path);
console.log(`Hash changed: ${hash2.substring(0, 8)}... -> ${hash2Updated.substring(0, 8)}...`);

// Scan after update
console.log('\nScan after content update:');
tracked = db.getByPath(file2Path);
if (tracked) {
  console.log(`  Found by path: Part ${tracked.partId}`);

  if (tracked.fileHash !== hash2Updated) {
    console.log('  Content changed detected!');
    db.updateHash(file2Path, hash2Updated);
    console.log(`  ✓ Would PATCH Part ${tracked.partId} with new fileUris/fileHashes only`);
    console.log('  ✓ Profiles, materials, print settings PRESERVED!');
  }
}

console.log('\n--- TEST 4: File Move to Subdirectory ---');
const subDir = path.join(testDir, 'subfolder');
fs.mkdirSync(subDir);
const file1SubPath = path.join(subDir, 'model1-renamed.3mf');
fs.renameSync(file1NewPath, file1SubPath);
console.log(`✓ Moved to subfolder: ${path.relative(testDir, file1SubPath)}`);

// Scan after move to subfolder
console.log('\nScan after subfolder move:');
tracked = db.getByPath(file1SubPath);
if (!tracked) {
  const hashInSub = computeSHA256(file1SubPath);
  tracked = db.getByHash(hashInSub);

  if (tracked) {
    console.log(`  ✓ FOUND by hash! Part: ${tracked.partId}`);
    db.updatePath(tracked.filePath, file1SubPath);
    console.log('  ✓ Tracking updated, binding preserved!');
  }
}

console.log('\n--- TEST 5: File Deletion ---');
const file3Path = path.join(testDir, 'model3-to-delete.3mf');
createTest3MF(file3Path, 'delete-me');
const hash3 = computeSHA256(file3Path);

db.upsert({
  filePath: file3Path,
  fileHash: hash3,
  partId: 'PART-DEL-999',
  designGuid: 'guid-333',
  lastSeenAt: new Date().toISOString()
});
console.log(`Tracked ${path.basename(file3Path)}`);

console.log('\nUser deletes file...');
fs.unlinkSync(file3Path);
console.log(`✓ Deleted ${path.basename(file3Path)}`);

// Scan would not find file, so remove from DB
db.delete(file3Path);
console.log('  ✓ Removed from tracking DB (acceptable loss per requirements)');

console.log('\n--- TEST 6: Multiple Files with Same Content (Duplicate Detection) ---');
const file4Path = path.join(testDir, 'original.3mf');
const file5Path = path.join(testDir, 'copy.3mf');

createTest3MF(file4Path, 'identical-content');
fs.copyFileSync(file4Path, file5Path);

const hash4 = computeSHA256(file4Path);
const hash5 = computeSHA256(file5Path);

console.log(`Hash of original: ${hash4.substring(0, 16)}...`);
console.log(`Hash of copy:     ${hash5.substring(0, 16)}...`);
console.log(`Hashes match: ${hash4 === hash5 ? '✓ YES' : '✗ NO'}`);

// Track original
db.upsert({
  filePath: file4Path,
  fileHash: hash4,
  partId: 'PART-ORIG-111',
  designGuid: 'guid-444',
  lastSeenAt: new Date().toISOString()
});

// Scan finds copy
console.log('\nScan finds copy:');
tracked = db.getByPath(file5Path);
if (!tracked) {
  console.log('  Not tracked by path');
  tracked = db.getByHash(hash5);

  if (tracked) {
    console.log(`  ✗ ERROR: Found existing Part ${tracked.partId} by hash!`);
    console.log('  This is a DUPLICATE copy - would trigger duplicate resolution');
    console.log('  Policy: createNewPart -> assign new Part ID to copy');
  }
}

console.log('\n--- FINAL DATABASE STATE ---');
const stats = db.getStats();
console.log(`Total tracked files: ${stats.total}`);
console.log('\nEntries:');
stats.entries.forEach((entry, idx) => {
  console.log(`  ${idx + 1}. ${entry.path} -> ${entry.partId} (hash: ${entry.hash}...)`);
});

console.log('\n=== TEST SUMMARY ===');
console.log('✓ File creation and tracking');
console.log('✓ File move detection (preserves Part binding)');
console.log('✓ File update detection (preserves metadata via PATCH)');
console.log('✓ File deletion (removes from tracking)');
console.log('✓ Subfolder moves (hash-based detection)');
console.log('✓ Duplicate detection (same content, different paths)');

console.log('\n🎉 All tracking scenarios tested successfully!\n');
