# Printago Folder Watch (Python Version)

Secure Windows desktop application for automatically watching and syncing files to Printago.

## Features

✅ **Working GUI** with folder picker, API configuration
✅ **Secure** - Path traversal prevention, file size limits, input validation
✅ **Real-time file watching** - Monitors directory for changes
✅ **Automatic upload** - Syncs files to Printago API
✅ **Activity logging** - See all file changes and uploads

## Installation

1. **Install Python 3.8+** from https://www.python.org/downloads/

2. **Install dependencies**:
```bash
pip install -r requirements.txt
```

## Usage

**Run the application**:
```bash
python printago_watch.py
```

**Configure**:
1. Click "Browse" to select a folder to watch
2. Enter your Printago API URL (e.g., `https://api.printago.com`)
3. Enter your API Key
4. Click "Save Configuration"
5. Click "Start Watching"

The app will now monitor the selected folder and automatically upload files to Printago!

## Creating a Windows Executable

To create a standalone `.exe` file:

```bash
pip install pyinstaller
pyinstaller --onefile --windowed --name="PrintagoFolderWatch" printago_watch.py
```

The executable will be in the `dist` folder.

## Security Features

- Path traversal prevention
- File size limit: 100MB
- Input sanitization
- HTTPS API support
- Secure configuration storage

## API Endpoints

The app expects these Printago API endpoints:
- `GET /api/health` - Connection test
- `POST /api/files/upload` - Upload file
- `DELETE /api/files/delete` - Delete file

Update the endpoints in `printago_watch.py` to match your actual Printago API.
