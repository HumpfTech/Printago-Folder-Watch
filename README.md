# Printago Folder Watch

A secure, desktop application for Windows that automatically watches a directory and syncs files to Printago in real-time.

## Features

- 🔒 **Secure**: Built with security best practices
  - Path traversal prevention
  - File size limits (100MB max)
  - Encrypted configuration storage
  - HTTPS API communication
  - No symlink following

- 📁 **Smart File Watching**:
  - Monitors directory for file changes (add, modify, delete)
  - Automatic upload queue management
  - File stability detection before upload
  - Recursive directory watching (10 levels deep)

- 🎨 **User-Friendly GUI**:
  - Easy directory picker
  - Configuration management
  - Real-time activity log
  - Visual status indicators

- 🔄 **Auto-Sync**:
  - Automatically uploads new and modified files
  - Overwrites existing files in Printago
  - Handles file deletions

## Installation

1. **Install Node.js** (if not already installed):
   - Download from https://nodejs.org/
   - Version 16 or higher required

2. **Install Dependencies**:
   ```bash
   npm install
   ```

## Usage

### Development Mode

Run the application in development mode:

```bash
npm run dev
```

### Production Build

Build the application for Windows:

```bash
npm run build:win
```

The installer will be created in the `dist` folder.

### Configuration

1. **Select Watch Directory**: Click "Browse" to select the folder you want to monitor
2. **Enter API URL**: Your Printago API endpoint (e.g., `https://api.printago.com`)
3. **Enter API Key**: Your Printago API authentication key
4. **Save Configuration**: Click "Save Configuration" to validate and save settings
5. **Start Watching**: Click "Start Watching" to begin monitoring

## Security Features

### Path Security
- Validates all file paths to prevent directory traversal attacks
- Normalizes paths to prevent path manipulation
- Restricts watching to specified directory only

### File Security
- Maximum file size limit: 100MB
- Ignores hidden files (dotfiles)
- Does not follow symbolic links
- Sanitizes filenames before upload

### API Security
- Secure credential storage using electron-store encryption
- Bearer token authentication
- HTTPS enforcement for production
- Request timeout protection (30s)

### Input Validation
- URL format validation
- Required field validation
- Path sanitization (removes `..`, illegal characters)

## API Endpoints

The application expects the following Printago API endpoints:

- `GET /api/health` - Connection test
- `POST /api/files/upload` - Upload/overwrite file
- `DELETE /api/files/delete` - Delete file

**Note**: Adjust endpoints in `src/printagoClient.js` to match your Printago API structure.

## File Structure

```
PrintagoFolderWatch/
├── src/
│   ├── main.js           # Electron main process
│   ├── preload.js        # Secure IPC bridge
│   ├── renderer.js       # UI logic
│   ├── fileWatcher.js    # File monitoring logic
│   ├── printagoClient.js # API client
│   ├── index.html        # Main UI
│   └── styles.css        # Styling
├── package.json
└── README.md
```

## Configuration Storage

Configuration is stored securely using electron-store with encryption:
- Location: `%APPDATA%/printago-folder-watch/config.json`
- Encrypted with unique key per installation

## Troubleshooting

### Connection Errors
- Verify your API URL is correct and accessible
- Check your API key is valid
- Ensure your network allows HTTPS connections

### File Upload Failures
- Check file size is under 100MB
- Verify you have read permissions for the watched directory
- Check Printago API logs for server-side errors

### Application Won't Start
- Ensure Node.js 16+ is installed
- Delete `node_modules` and run `npm install` again
- Check for port conflicts if running in dev mode

## Development

### Tech Stack
- **Electron**: Desktop application framework
- **Chokidar**: File system watcher
- **Axios**: HTTP client
- **electron-store**: Secure configuration storage

### Adding Features
1. File watcher logic: Edit `src/fileWatcher.js`
2. API integration: Edit `src/printagoClient.js`
3. UI changes: Edit `src/index.html`, `src/styles.css`, `src/renderer.js`

## License

MIT License - Humpf Tech LLC

## Support

For issues or questions, contact your Printago administrator.
