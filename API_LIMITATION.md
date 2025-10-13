# Printago API Limitation - File Uploads

## 🔍 What I Discovered

After extensive testing, the **Printago API does NOT support direct file uploads** for creating Parts via the API.

## ✅ What Works
- ✅ Authentication (`ApiKey` + Store ID)
- ✅ GET /v1/parts (list parts)
- ✅ GET /v1/printers (list printers)
- ✅ GET /v1/web-init (get init data)

## ❌ What Doesn't Work
- ❌ POST /v1/parts with multipart/form-data → 400 "Request body type is not application/json"
- ❌ POST /v1/parts with JSON → 400 "fileUris required"
- ❌ POST /v1/files/upload → 404 Not Found

## 📋 Required Part Creation Format

To create a Part, the API requires this JSON structure:

```json
{
  "name": "Part Name",
  "type": "3mf",  // or "gcode3mf", "scad", "step", "stl"
  "description": "",
  "fileUris": ["storeId/parts/partId/filename.3mf"],  // ← THIS IS THE PROBLEM
  "parameters": [],
  "printTags": {},
  "overriddenProcessProfileId": null
}
```

**The Problem**: `fileUris` requires files to already be uploaded and stored in Printago's storage system. There's no API endpoint to upload files and get these URIs.

## 🔍 File URI Format

From existing parts:
```
pvi19n308u4wjk4y82qw5ap8/parts/ncsfkery7tc13jn478i4o38v/ArticulatedHammerheadShark.3mf
```

Format: `{storeId}/parts/{partId}/{filename}`

But we can't get a `partId` without creating the part first, and we can't create the part without the `fileUri`. **Chicken and egg problem**.

## 💡 Possible Solutions

### Option 1: Use Printago Web UI
Upload files manually through the Printago web interface, then use the API to:
- List existing parts
- Manage print jobs
- Update part metadata

**Folder Watch Could:**
- ✅ Monitor folder for changes
- ✅ Show which files are new/changed
- ✅ Provide a list of files to upload
- ❌ Cannot automatically upload (manual step required)

### Option 2: Contact Printago Support
The documentation says:
> "For specific implementation details, join our Discord community"

**Next Step**: Ask in Discord:
1. How to upload 3D model files via API?
2. Is there an undocumented `/v1/files/upload` endpoint?
3. What's the workflow for automated part creation?

### Option 3: Reverse Engineer Web UI
Monitor network requests from Printago web app to find:
- Actual file upload endpoint
- Required headers/authentication
- Upload workflow

**Risky**: May violate terms of service

### Option 4: Watch & Notify Only
Change the app to:
- ✅ Monitor folder for new 3D models
- ✅ Notify you when files change
- ✅ Show list of pending uploads
- ✅ One-click to open Printago web UI
- ❌ Manual drag-and-drop upload

## 🎯 Recommended Next Steps

1. **Ask Printago Support** (Discord/Email)
   - Request API documentation for file uploads
   - Ask about automated part creation workflow

2. **Modified Folder Watch App**
   - Keep file monitoring ✅
   - Add "pending uploads" list ✅
   - Button to open Printago in browser ✅
   - Desktop notifications for new files ✅

3. **Wait for API Update**
   - Printago may add file upload endpoint in future
   - Monitor API changelog

## 📊 Current Capabilities Summary

| Feature | Status |
|---------|--------|
| Monitor folder | ✅ Working |
| Detect new/changed files | ✅ Working |
| API Authentication | ✅ Working |
| List existing parts | ✅ Working |
| Upload new parts | ❌ Not supported by API |
| Update part metadata | ❓ Untested (may work) |
| Create print jobs | ❓ Untested |

## 🔧 What The App CAN Do Right Now

1. **File Monitoring** - Track all changes in your 3DPrinting folder
2. **Inventory Management** - Compare local files vs Printago parts
3. **Change Detection** - Alert when files are added/modified
4. **Part Listing** - Show all parts in your Printago account
5. **Desktop Notifications** - Alert you to upload new files

## ❌ What It CAN'T Do

- Automatically upload 3D model files to Printago
- Create new Parts without manual web UI interaction

## 💬 Need Help?

Contact Printago:
- **Discord**: Join their community (link in API docs)
- **Support**: Ask about programmatic file uploads
- **Feature Request**: Request file upload API endpoint

---

**Bottom Line**: The folder watch app is built and working, but Printago's API doesn't support automated file uploads. You'll need to either:
1. Upload files manually through web UI
2. Contact Printago to request this API feature
3. Use the app as a "file change notifier" instead
