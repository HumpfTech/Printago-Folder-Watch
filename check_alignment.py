import sys
import json
import requests
import os

API_KEY = "dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg"
STORE_ID = "sb3bexu83dpm0gry8u265amx"
BASE_URL = "https://new-api.printago.io"
WATCH_PATH = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting"

headers = {
    "authorization": f"ApiKey {API_KEY}",
    "x-printago-storeid": STORE_ID
}

# Get all folders
folders_resp = requests.get(f"{BASE_URL}/v1/folders", headers=headers)
folders_data = folders_resp.json()
folders = folders_data if isinstance(folders_data, list) else folders_data.get('data', [])
folder_map = {f['id']: f for f in folders}

def get_folder_path(folder_id):
    """Build folder path from ID"""
    path_parts = []
    current_id = folder_id
    while current_id and current_id in folder_map:
        folder = folder_map[current_id]
        if folder['name'] != 'Local Folder Sync':
            path_parts.insert(0, folder['name'])
        current_id = folder.get('parentId')
    return '/'.join(path_parts)

# Get all Parts
parts_resp = requests.get(f"{BASE_URL}/v1/parts?limit=1000", headers=headers)
parts_data = parts_resp.json()
parts = parts_data if isinstance(parts_data, list) else parts_data.get('data', [])

print(f"Total Parts in Printago: {len(parts)}")
print("\nChecking alignment (first 20 Parts):\n")

misaligned = []
for i, part in enumerate(parts[:20]):
    folder_path = get_folder_path(part.get('folderId', ''))
    expected_local_path = os.path.join(WATCH_PATH, folder_path, f"{part['name']}.stl").replace('\\', '/')

    # Check if file exists
    exists = os.path.exists(expected_local_path)
    status = "OK" if exists else "MISSING"

    print(f"{status:7} {part['name'][:40]:40} | {folder_path[:50]:50}")

    if not exists:
        # Try with .3mf extension
        expected_3mf = expected_local_path.replace('.stl', '.3mf')
        if os.path.exists(expected_3mf):
            print(f"  --> Found as .3mf instead")
        else:
            misaligned.append({
                'name': part['name'],
                'id': part['id'],
                'expected': expected_local_path,
                'folder_path': folder_path
            })

print(f"\n\nMisaligned count: {len(misaligned)}")
if misaligned:
    print("\nMisaligned Parts:")
    for m in misaligned[:10]:
        print(f"  - {m['name']}")
        print(f"    Expected: {m['expected']}")
        print(f"    Part ID: {m['id']}")
