# Printago Folder Watch

A Windows desktop application that automatically syncs local 3D print files (3MF, STL) with Printago's cloud platform in real-time.

## Features

- **Real-time File Monitoring**: Automatically detects file changes, additions, and deletions
- **Smart Atomic Save Detection**: Handles Bambu Studio and other slicer save patterns without losing metadata
- **Metadata Preservation**: Updates existing Parts without deleting materials, print settings, or configurations
- **Folder Hierarchy Sync**: Maintains your local folder structure in Printago
- **File Move/Rename Detection**: Tracks Part IDs across file operations
- **System Tray Application**: Runs quietly in the background with status window access
- **Upload Progress Tracking**: Real-time visibility into upload queue and progress
- **Concurrent Uploads**: Handles up to 10 simultaneous uploads efficiently

## Installation

### Download

Download the latest installer from the [Releases](https://github.com/HumpfTech/Win-Folder-Watch_Printago/releases) page:

**`PrintagoFolderWatch-Setup.exe`**

### Install

1. Run `PrintagoFolderWatch-Setup.exe`
2. Follow the installation wizard
3. The application will launch automatically after installation

### First-Time Setup

1. **Right-click the system tray icon** (Printago logo) and select "Settings"
2. **Configure your settings**:
   - **Watch Path**: Select the local folder containing your 3D print files
   - **API URL**: Your Printago API endpoint (e.g., `https://api.printago.io`)
   - **API Key**: Your Printago API authentication key
   - **Store ID**: Your Printago store identifier
3. Click **"Save Configuration"**
4. Click **"Start Watching"** to begin syncing

## Usage

### System Tray

The application runs in the system tray with these options:
- **Show Status**: View upload progress and queue
- **Show Logs**: View detailed activity logs
- **Settings**: Configure API and folder settings
- **Sync Now**: Manually trigger a full sync
- **Exit**: Close the application

### Status Window

Shows real-time information:
- **Queue**: Files waiting to be uploaded
- **Uploading**: Active uploads with progress bars
- **Synced Count**: Total files synced in current session
- **Folders**: Number of folders synced

### Logs Window

Detailed activity log showing:
- File change detection (created, modified, deleted, renamed)
- Upload/update operations
- Folder operations
- Error messages and warnings

## How It Works

### Atomic Save Detection

When you save a file in Bambu Studio or other slicers, they use an "atomic save" pattern:
1. Write to temporary file
2. Delete original file
3. Rename temp to original

Printago Folder Watch detects this pattern with a 1-second grace period, treating it as an **update** instead of a **delete + create**. This preserves all your Part metadata in Printago.

### Metadata Preservation

When a file is modified:
- The app uses **PATCH** to update only the file content
- **Preserves**: Part ID, materials, print settings, geometry settings, process profiles
- **Updates**: File content and hash only

### File Tracking

Uses SQLite database to track:
- Part ID → Local file path mapping
- File hashes for change detection
- Last seen timestamps

This allows the app to maintain Part IDs even when files are moved or renamed.

## Technical Details

### Requirements

- **OS**: Windows 10 or later
- **Runtime**: .NET 9.0 (included in installer)
- **Permissions**: Read access to watch folder, network access to Printago API

### File Support

- **3MF** files (3D Manufacturing Format)
- **STL** files (Stereolithography)

### API Integration

Uses Printago REST API:
- `GET /v1/folders` - Folder hierarchy
- `POST /v1/folders` - Create folders
- `GET /v1/parts` - List Parts
- `POST /v1/parts` - Create new Parts
- `PATCH /v1/parts/{id}` - Update existing Parts
- `DELETE /v1/parts/{id}` - Delete Parts
- `POST /v1/storage/signed-upload-urls` - Get upload URLs

### Configuration Storage

Settings are stored in:
```
%APPDATA%\PrintagoFolderWatch\config.json
```

Tracking database:
```
%APPDATA%\PrintagoFolderWatch\file-tracking.db
```

Logs:
```
%APPDATA%\PrintagoFolderWatch\logs\printago-YYYY-MM-DD.log
```

## Troubleshooting

### Files Not Syncing

1. Check the **Logs window** for error messages
2. Verify your **API credentials** in Settings
3. Click **"Sync Now"** to trigger a manual sync
4. Ensure files are **.3mf** or **.stl** format

### Metadata Being Lost

This was fixed in version 1.0+. If you're still experiencing issues:
1. Ensure you're on the latest version
2. Check logs for "atomic save detected" messages
3. Report the issue with log excerpts

### Connection Errors

- Verify API URL is correct and accessible
- Check your internet connection
- Ensure API key and Store ID are valid
- Check firewall isn't blocking the application

### Duplicate Parts

If you see duplicate Parts in Printago:
1. Stop the watch service
2. Delete the tracking database: `%APPDATA%\PrintagoFolderWatch\file-tracking.db`
3. Click "Sync Now" to rebuild tracking

## Building from Source

### Prerequisites

- Visual Studio 2022 or later
- .NET 9.0 SDK
- Inno Setup 6 (for installer)

### Build Steps

```bash
# Restore dependencies
dotnet restore

# Build release
dotnet build -c Release

# Create installer (requires Inno Setup)
"C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer.iss
```

Installer will be created at: `dist\PrintagoFolderWatch-Setup.exe`

## Architecture

### Key Components

- **FileWatcherServiceV2.cs**: Core sync engine with atomic save handling
- **FileTrackingDb.cs**: SQLite-based Part ID persistence
- **StatusForm.cs**: Real-time UI with upload progress
- **ConfigForm.cs**: Settings management
- **TrayApplicationContext.cs**: System tray integration

### Design Patterns

- **Hash-based change detection**: SHA256 for file integrity
- **Grace period deletion**: 1-second delay for atomic save detection
- **PATCH-based updates**: Preserve metadata on file changes
- **Concurrent uploads**: Semaphore-controlled (max 10)
- **Iterative folder deletion**: Handles cascading folder operations

## License

Copyright © 2024 Humpf Tech LLC. All rights reserved.

## Support

For issues, questions, or feature requests:
- Open an issue on [GitHub](https://github.com/HumpfTech/Win-Folder-Watch_Printago/issues)
- Contact: support@humpf.tech

---

*Built with Claude Code - https://claude.com/claude-code*
