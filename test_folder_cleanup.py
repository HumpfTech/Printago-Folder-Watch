import requests
import json
from collections import defaultdict

# Config
API_KEY = "dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg"
STORE_ID = "sb3bexu83dpm0gry8u265amx"
BASE_URL = "https://new-api.printago.io"

headers = {
    "authorization": f"ApiKey {API_KEY}",
    "x-printago-storeid": STORE_ID
}

print("Fetching all folders...")
folders_resp = requests.get(f"{BASE_URL}/v1/folders?limit=10000", headers=headers)
folders = folders_resp.json()
print(f"Found {len(folders)} total folders")

print("\nFetching all parts...")
parts_resp = requests.get(f"{BASE_URL}/v1/parts?limit=10000", headers=headers)
parts = parts_resp.json()
print(f"Found {len(parts)} total parts")

# Build sets
folders_with_parts = set()
for part in parts:
    if part.get('folderId'):
        folders_with_parts.add(part['folderId'])

folders_with_children = set()
for folder in folders:
    if folder.get('parentId'):
        folders_with_children.add(folder['parentId'])

# Build folder ID to path map
def get_folder_path(folder_id):
    if not folder_id:
        return ""
    folder = next((f for f in folders if f['id'] == folder_id), None)
    if not folder:
        return ""
    if not folder.get('parentId'):
        return folder['name']
    parent_path = get_folder_path(folder['parentId'])
    return f"{parent_path}/{folder['name']}" if parent_path else folder['name']

# Find empty folders
print("\nChecking for empty folders...")
empty_folders = []
for folder in folders:
    folder_id = folder['id']
    has_parts = folder_id in folders_with_parts
    has_children = folder_id in folders_with_children

    if not has_parts and not has_children:
        folder_path = get_folder_path(folder_id)
        empty_folders.append({
            'id': folder_id,
            'name': folder['name'],
            'path': folder_path
        })

print(f"\nFound {len(empty_folders)} empty folders:")
for f in empty_folders[:20]:
    print(f"  - {f['path']}")

if len(empty_folders) > 20:
    print(f"  ... and {len(empty_folders) - 20} more")

# Check which ones contain "STL Library V1.4"
stl_library_folders = [f for f in empty_folders if 'STL Library V1.4' in f['path']]
print(f"\nEmpty folders containing 'STL Library V1.4': {len(stl_library_folders)}")
for f in stl_library_folders[:10]:
    print(f"  - {f['path']}")
