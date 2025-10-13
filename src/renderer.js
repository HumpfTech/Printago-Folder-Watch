// UI Elements
const watchPathInput = document.getElementById('watch-path');
const apiUrlInput = document.getElementById('api-url');
const apiKeyInput = document.getElementById('api-key');
const selectDirBtn = document.getElementById('select-dir-btn');
const saveConfigBtn = document.getElementById('save-config-btn');
const startWatchBtn = document.getElementById('start-watch-btn');
const stopWatchBtn = document.getElementById('stop-watch-btn');
const statusDiv = document.getElementById('status');
const activityLog = document.getElementById('activity-log');

let isWatching = false;

// Load saved configuration on startup
window.electronAPI.getConfig().then(config => {
  if (config.watchPath) watchPathInput.value = config.watchPath;
  if (config.apiUrl) apiUrlInput.value = config.apiUrl;
  if (config.apiKey) apiKeyInput.value = config.apiKey;

  if (config.watchPath && config.apiUrl && config.apiKey) {
    startWatchBtn.disabled = false;
  }
});

// Directory selection
selectDirBtn.addEventListener('click', async () => {
  const dirPath = await window.electronAPI.selectDirectory();
  if (dirPath) {
    watchPathInput.value = dirPath;
    showStatus('Directory selected: ' + dirPath, 'info');
  }
});

// Save configuration
saveConfigBtn.addEventListener('click', async () => {
  const config = {
    watchPath: watchPathInput.value.trim(),
    apiUrl: apiUrlInput.value.trim(),
    apiKey: apiKeyInput.value.trim()
  };

  // Validation
  if (!config.watchPath || !config.apiUrl || !config.apiKey) {
    showStatus('Please fill in all fields', 'error');
    return;
  }

  // Disable button during save
  saveConfigBtn.disabled = true;
  saveConfigBtn.textContent = 'Saving...';

  try {
    const result = await window.electronAPI.saveConfig(config);

    if (result.success) {
      showStatus('Configuration saved successfully!', 'success');
      startWatchBtn.disabled = false;
      addLogEntry('Configuration saved and validated', 'success');
    } else {
      showStatus('Error: ' + result.error, 'error');
    }
  } catch (error) {
    showStatus('Error saving configuration: ' + error.message, 'error');
  } finally {
    saveConfigBtn.disabled = false;
    saveConfigBtn.textContent = 'Save Configuration';
  }
});

// Start watching
startWatchBtn.addEventListener('click', async () => {
  startWatchBtn.disabled = true;
  startWatchBtn.textContent = 'Starting...';

  try {
    const result = await window.electronAPI.startWatching();

    if (result.success) {
      isWatching = true;
      showStatus('Watching directory for changes...', 'success');
      startWatchBtn.style.display = 'none';
      stopWatchBtn.disabled = false;
      stopWatchBtn.style.display = 'inline-block';
      addLogEntry('Started watching: ' + watchPathInput.value, 'info');

      // Disable config editing while watching
      watchPathInput.disabled = true;
      apiUrlInput.disabled = true;
      apiKeyInput.disabled = true;
      selectDirBtn.disabled = true;
      saveConfigBtn.disabled = true;
    } else {
      showStatus('Error: ' + result.error, 'error');
      startWatchBtn.disabled = false;
    }
  } catch (error) {
    showStatus('Error starting watcher: ' + error.message, 'error');
    startWatchBtn.disabled = false;
  } finally {
    startWatchBtn.textContent = 'Start Watching';
  }
});

// Stop watching
stopWatchBtn.addEventListener('click', async () => {
  stopWatchBtn.disabled = true;

  try {
    const result = await window.electronAPI.stopWatching();

    if (result.success) {
      isWatching = false;
      showStatus('Stopped watching', 'info');
      startWatchBtn.style.display = 'inline-block';
      startWatchBtn.disabled = false;
      stopWatchBtn.style.display = 'none';
      addLogEntry('Stopped watching', 'info');

      // Re-enable config editing
      watchPathInput.disabled = false;
      apiUrlInput.disabled = false;
      apiKeyInput.disabled = false;
      selectDirBtn.disabled = false;
      saveConfigBtn.disabled = false;
    }
  } catch (error) {
    showStatus('Error stopping watcher: ' + error.message, 'error');
    stopWatchBtn.disabled = false;
  }
});

// Event listeners from main process
window.electronAPI.onFileEvent((data) => {
  const { type, path } = data;
  addLogEntry(`File ${type}: ${path}`, type);
});

window.electronAPI.onUploadSuccess((filePath) => {
  addLogEntry(`✓ Upload successful: ${filePath}`, 'success');
});

window.electronAPI.onUploadError((data) => {
  addLogEntry(`✗ Upload failed: ${data.path} - ${data.error}`, 'error');
});

// Helper functions
function showStatus(message, type) {
  statusDiv.textContent = message;
  statusDiv.className = 'status ' + type;

  // Auto-hide after 5 seconds
  setTimeout(() => {
    statusDiv.style.display = 'none';
  }, 5000);
}

function addLogEntry(message, type) {
  const entry = document.createElement('div');
  entry.className = 'log-entry ' + type;

  const timestamp = new Date().toLocaleTimeString();
  entry.innerHTML = `
    <span class="timestamp">${timestamp}</span>
    <span class="message">${escapeHtml(message)}</span>
  `;

  activityLog.insertBefore(entry, activityLog.firstChild);

  // Limit log entries to 100
  while (activityLog.children.length > 100) {
    activityLog.removeChild(activityLog.lastChild);
  }
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// Initialize UI state
stopWatchBtn.style.display = 'none';
