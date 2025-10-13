import requests
import time

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

# Find empty folders containing "STL Library V1.4"
print("\nFinding empty folders with 'STL Library V1.4'...")
to_delete = []
for folder in folders:
    folder_id = folder['id']
    has_parts = folder_id in folders_with_parts
    has_children = folder_id in folders_with_children

    if not has_parts and not has_children:
        folder_path = get_folder_path(folder_id)
        if 'STL Library V1.4' in folder_path:
            to_delete.append({
                'id': folder_id,
                'name': folder['name'],
                'path': folder_path,
                'depth': folder_path.count('/')
            })

# Sort by depth (deepest first) to avoid deleting parents before children
to_delete.sort(key=lambda x: x['depth'], reverse=True)

print(f"\nFound {len(to_delete)} empty folders to delete:")
for f in to_delete:
    print(f"  [{f['depth']}] {f['path']}")

print(f"\nStarting deletion...")
deleted = 0
failed = 0

for folder in to_delete:
    print(f"Deleting: {folder['path']}...", end=" ")

    delete_resp = requests.delete(
        f"{BASE_URL}/v1/folders/{folder['id']}",
        headers=headers
    )

    if delete_resp.status_code == 200:
        print("OK")
        deleted += 1
    elif delete_resp.status_code == 404:
        print("Already deleted")
        deleted += 1
    else:
        print(f"FAILED ({delete_resp.status_code}): {delete_resp.text[:100]}")
        failed += 1

    time.sleep(2)  # Rate limiting

print(f"\n=== DONE ===")
print(f"Deleted: {deleted}")
print(f"Failed: {failed}")
