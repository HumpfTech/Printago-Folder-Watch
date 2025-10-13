import sys
import json
import requests
import os
import hashlib
from pathlib import Path

API_KEY = "dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg"
STORE_ID = "sb3bexu83dpm0gry8u265amx"
BASE_URL = "https://new-api.printago.io"
WATCH_PATH = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting"

headers = {
    "authorization": f"ApiKey {API_KEY}",
    "x-printago-storeid": STORE_ID
}

def compute_file_hash(filepath):
    """Compute SHA256 hash of file"""
    sha256 = hashlib.sha256()
    with open(filepath, 'rb') as f:
        while chunk := f.read(8192):
            sha256.update(chunk)
    return sha256.hexdigest()

def find_local_file(part_name, part_hash):
    """Search for file locally by name and verify with hash"""
    # Try both extensions
    for ext in ['.stl', '.3mf', '.STL', '.3MF']:
        # Search recursively
        for filepath in Path(WATCH_PATH).rglob(f"{part_name}{ext}"):
            if part_hash:
                # Verify hash matches
                file_hash = compute_file_hash(str(filepath))
                if file_hash == part_hash:
                    return str(filepath)
            else:
                # No hash to verify, return first match
                return str(filepath)
    return None

def get_or_create_folder(folder_path):
    """Get folder ID for path, creating if needed"""
    if not folder_path or folder_path == "":
        # Root level - find "Local Folder Sync" folder
        folders_resp = requests.get(f"{BASE_URL}/v1/folders", headers=headers)
        folders_data = folders_resp.json()
        folders = folders_data if isinstance(folders_data, list) else folders_data.get('data', [])
        for f in folders:
            if f['name'] == 'Local Folder Sync' and not f.get('parentId'):
                return f['id']
        return None

    # Build folder hierarchy
    parts = folder_path.split('/')
    folders_resp = requests.get(f"{BASE_URL}/v1/folders", headers=headers)
    folders_data = folders_resp.json()
    folders = folders_data if isinstance(folders_data, list) else folders_data.get('data', [])
    folder_map = {f['id']: f for f in folders}

    # Find root "Local Folder Sync"
    parent_id = None
    for f in folders:
        if f['name'] == 'Local Folder Sync' and not f.get('parentId'):
            parent_id = f['id']
            break

    if not parent_id:
        print("ERROR: Could not find Local Folder Sync root")
        return None

    # Walk through path parts
    for part_name in parts:
        found = False
        for f in folders:
            if f['name'] == part_name and f.get('parentId') == parent_id:
                parent_id = f['id']
                found = True
                break

        if not found:
            # Create folder
            create_resp = requests.post(
                f"{BASE_URL}/v1/folders",
                headers=headers,
                json={"name": part_name, "parentId": parent_id, "type": "part"}
            )
            if create_resp.status_code == 200 or create_resp.status_code == 201:
                new_folder = create_resp.json()
                parent_id = new_folder['id']
                folder_map[parent_id] = new_folder
                print(f"  Created folder: {part_name}")
            else:
                print(f"  ERROR creating folder {part_name}: {create_resp.status_code}")
                return None

    return parent_id

# Get all folders
print("Fetching folders...")
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
print("Fetching Parts...")
parts_resp = requests.get(f"{BASE_URL}/v1/parts?limit=1000", headers=headers)
parts_data = parts_resp.json()
parts = parts_data if isinstance(parts_data, list) else parts_data.get('data', [])

print(f"Total Parts in Printago: {len(parts)}")
print("\nFinding misaligned Parts...\n")

misaligned = []
for i, part in enumerate(parts):
    if i % 50 == 0:
        print(f"  Checked {i}/{len(parts)} Parts...")

    folder_path = get_folder_path(part.get('folderId', ''))
    expected_local_path = os.path.join(WATCH_PATH, folder_path, f"{part['name']}.stl").replace('\\', '/')

    # Check if file exists at expected location
    exists = os.path.exists(expected_local_path)
    if not exists:
        exists = os.path.exists(expected_local_path.replace('.stl', '.3mf'))

    if not exists:
        # File not at expected location - search for it
        part_hash = part.get('fileHashes', [])[0] if part.get('fileHashes') else None
        actual_path = find_local_file(part['name'], part_hash)

        if actual_path:
            # Found file at different location
            relative_path = os.path.relpath(actual_path, WATCH_PATH).replace('\\', '/')
            actual_folder = os.path.dirname(relative_path)

            if actual_folder != folder_path:
                misaligned.append({
                    'id': part['id'],
                    'name': part['name'],
                    'current_folder': folder_path,
                    'actual_folder': actual_folder,
                    'actual_path': actual_path
                })

print(f"\nFound {len(misaligned)} misaligned Parts")

if misaligned:
    print("\nMisaligned Parts (showing first 20):")
    for m in misaligned[:20]:
        print(f"  - {m['name']}")
        print(f"    Printago: {m['current_folder']}")
        print(f"    Local:    {m['actual_folder']}")

    response = input(f"\nDo you want to fix these {len(misaligned)} misalignments? (yes/no): ")
    if response.lower() == 'yes':
        print("\nFixing misalignments...")

        # Group by target folder for bulk moves
        moves_by_folder = {}
        for m in misaligned:
            target_folder = m['actual_folder']
            if target_folder not in moves_by_folder:
                moves_by_folder[target_folder] = []
            moves_by_folder[target_folder].append(m)

        fixed_count = 0
        for target_folder, parts_to_move in moves_by_folder.items():
            # Get or create target folder
            target_folder_id = get_or_create_folder(target_folder)

            if not target_folder_id:
                print(f"  ERROR: Could not get/create folder {target_folder}")
                continue

            # Bulk move using new API
            part_ids = [p['id'] for p in parts_to_move]

            # Move in batches of 50
            for i in range(0, len(part_ids), 50):
                batch = part_ids[i:i+50]

                move_resp = requests.patch(
                    f"{BASE_URL}/v1/folders/move",
                    headers=headers,
                    json={
                        "entities": {"partIds": batch},
                        "toFolderId": target_folder_id
                    }
                )

                if move_resp.status_code == 200:
                    fixed_count += len(batch)
                    print(f"  Moved {len(batch)} Parts to {target_folder}")
                else:
                    print(f"  ERROR moving batch: {move_resp.status_code} - {move_resp.text}")

        print(f"\nFixed {fixed_count}/{len(misaligned)} misalignments!")
    else:
        print("\nAborted.")
else:
    print("\nNo misalignments found - everything is aligned!")
