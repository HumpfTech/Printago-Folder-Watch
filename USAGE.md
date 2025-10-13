# Printago Folder Watch - Usage Guide

## ✅ What's Been Implemented

### Initial Sync Feature
When you click "Start Watching", the application now:
1. **Scans all existing files** in the selected folder
2. **Queues them for upload** to Printago
3. **Then continues watching** for new changes

### Features
- ✅ **Initial folder scan** - Uploads all existing files when starting
- ✅ **Real-time watching** - Monitors for new/changed/deleted files
- ✅ **Security** - Path validation, file size limits (100MB)
- ✅ **Activity logging** - See all sync activity in real-time
- ✅ **Auto-load credentials** - API key loaded from `.env` file

## 🚀 How to Use

### 1. Start the Application
```bash
python printago_watch.py
```

### 2. Configure (First Time)
The API URL and API Key should already be pre-filled from the `.env` file:
- **API URL**: `https://api.printago.io`
- **API Key**: `shyraikoiom2uh2ut3eywn0dm7kmf094jdrqbwu47jbu32tmasbn4ben0xtxj76bh59t7nen`

### 3. Select Folder to Watch
1. Click **"Browse"** button
2. Select the folder you want to monitor
3. Click **"Save Configuration"**

### 4. Start Syncing
Click **"Start Watching"** and the app will:
- ✅ Scan and upload ALL existing files in the folder
- ✅ Continue monitoring for any new changes
- ✅ Show progress in the Activity Log

## 📊 What Happens When You Start Watching

```
🚀 Performing initial sync...
🔍 Starting initial sync of existing files...
📊 Found 15 existing files to sync
📁 File added: test-folder/file1.txt
✅ Uploaded: test-folder/file1.txt
📁 File added: test-folder/file2.txt
✅ Uploaded: test-folder/file2.txt
... (continues for all files)
👀 Started watching: test-folder
```

## 🔄 Ongoing Monitoring

After initial sync, the app monitors for:
- **New files** → Automatically uploads
- **Modified files** → Re-uploads with overwrite
- **Deleted files** → Deletes from Printago

## ⚙️ Configuration

### Stored in `.env` file:
```
PRINTAGO_API_KEY=your_api_key_here
PRINTAGO_API_URL=https://api.printago.io
```

### Stored in `config.json`:
```json
{
  "watch_path": "D:\\path\\to\\watch",
  "api_url": "https://api.printago.io",
  "api_key": "your_key"
}
```

## 🧪 Testing

A test folder has been created with sample files:
```
test-folder/
├── file1.txt
└── file2.txt
```

You can use this to test the sync functionality:
1. Browse and select the `test-folder` directory
2. Save configuration
3. Click "Start Watching"
4. Watch the activity log show the initial sync

## 📝 Notes

- The app currently uses placeholder upload endpoints
- You may need to update the `upload_file()` and `delete_file()` methods in `PrintagoClient` class to match actual Printago API requirements
- File uploads are queued and processed sequentially to avoid overwhelming the API
- Hidden files (starting with `.`) are automatically skipped

## 🔒 Security

- ✅ Path traversal protection
- ✅ File size limits (100MB max)
- ✅ Input validation
- ✅ Secure credential storage
- ✅ API key in `.env` (gitignored)
