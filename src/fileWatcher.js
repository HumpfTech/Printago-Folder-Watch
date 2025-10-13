const chokidar = require('chokidar');
const { EventEmitter } = require('events');
const path = require('path');
const fs = require('fs').promises;

class FileWatcher extends EventEmitter {
  constructor(watchPath, printagoClient) {
    super();
    this.watchPath = watchPath;
    this.printagoClient = printagoClient;
    this.watcher = null;
    this.uploadQueue = new Map();
    this.isProcessing = false;
  }

  start() {
    // Security: Only watch specified directory, prevent path traversal
    const normalizedPath = path.normalize(this.watchPath);

    this.watcher = chokidar.watch(normalizedPath, {
      ignored: /(^|[\/\\])\../, // ignore dotfiles
      persistent: true,
      ignoreInitial: false,
      awaitWriteFinish: {
        stabilityThreshold: 2000,
        pollInterval: 100
      },
      depth: 10, // limit recursion depth
      followSymlinks: false // security: don't follow symlinks
    });

    this.watcher
      .on('add', (filePath) => this.handleFileAdd(filePath))
      .on('change', (filePath) => this.handleFileChange(filePath))
      .on('unlink', (filePath) => this.handleFileDelete(filePath))
      .on('error', (error) => this.emit('error', error));

    console.log(`Watching directory: ${normalizedPath}`);
  }

  stop() {
    if (this.watcher) {
      this.watcher.close();
      this.watcher = null;
    }
    this.uploadQueue.clear();
  }

  async handleFileAdd(filePath) {
    // Security: Validate file path is within watched directory
    if (!this.isPathSafe(filePath)) {
      console.warn(`Blocked suspicious path: ${filePath}`);
      return;
    }

    this.emit('file-added', filePath);
    await this.queueUpload(filePath);
  }

  async handleFileChange(filePath) {
    if (!this.isPathSafe(filePath)) {
      console.warn(`Blocked suspicious path: ${filePath}`);
      return;
    }

    this.emit('file-changed', filePath);
    await this.queueUpload(filePath);
  }

  async handleFileDelete(filePath) {
    if (!this.isPathSafe(filePath)) {
      return;
    }

    this.emit('file-deleted', filePath);
    // Remove from queue if pending
    this.uploadQueue.delete(filePath);

    // Optionally delete from Printago
    try {
      const relativePath = this.getRelativePath(filePath);
      await this.printagoClient.deleteFile(relativePath);
    } catch (error) {
      console.error(`Error deleting file ${filePath}:`, error.message);
    }
  }

  async queueUpload(filePath) {
    // Add to queue
    this.uploadQueue.set(filePath, Date.now());

    // Process queue if not already processing
    if (!this.isProcessing) {
      await this.processQueue();
    }
  }

  async processQueue() {
    if (this.uploadQueue.size === 0) {
      this.isProcessing = false;
      return;
    }

    this.isProcessing = true;

    // Get oldest file from queue
    const [filePath] = this.uploadQueue.keys();
    this.uploadQueue.delete(filePath);

    try {
      await this.uploadFile(filePath);
      this.emit('upload-success', filePath);
    } catch (error) {
      console.error(`Upload failed for ${filePath}:`, error.message);
      this.emit('upload-error', filePath, error);
    }

    // Process next file
    await this.processQueue();
  }

  async uploadFile(filePath) {
    // Security: Validate file exists and is readable
    const stats = await fs.stat(filePath);

    if (!stats.isFile()) {
      throw new Error('Not a file');
    }

    // Security: Check file size (limit to 100MB)
    const maxSize = 100 * 1024 * 1024;
    if (stats.size > maxSize) {
      throw new Error('File too large (max 100MB)');
    }

    // Get relative path for Printago
    const relativePath = this.getRelativePath(filePath);

    // Read file
    const fileBuffer = await fs.readFile(filePath);

    // Upload to Printago
    await this.printagoClient.uploadFile(relativePath, fileBuffer);
  }

  isPathSafe(filePath) {
    // Security: Ensure file path is within watched directory
    const normalizedWatchPath = path.normalize(this.watchPath);
    const normalizedFilePath = path.normalize(filePath);

    return normalizedFilePath.startsWith(normalizedWatchPath);
  }

  getRelativePath(filePath) {
    return path.relative(this.watchPath, filePath).replace(/\\/g, '/');
  }
}

module.exports = FileWatcher;
