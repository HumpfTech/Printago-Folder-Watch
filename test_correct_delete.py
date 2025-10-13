import requests
import json

# Config
API_KEY = "dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg"
STORE_ID = "sb3bexu83dpm0gry8u265amx"
BASE_URL = "https://new-api.printago.io"

headers = {
    "authorization": f"ApiKey {API_KEY}",
    "x-printago-storeid": STORE_ID,
    "content-type": "application/json"
}

print("=== Getting empty folders ===")
folders_resp = requests.get(f"{BASE_URL}/v1/folders?limit=10000", headers=headers)
folders = folders_resp.json()

parts_resp = requests.get(f"{BASE_URL}/v1/parts?limit=10000", headers=headers)
parts = parts_resp.json()

folders_with_parts = set(p.get('folderId') for p in parts if p.get('folderId'))
folders_with_children = set(f.get('parentId') for f in folders if f.get('parentId'))

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

empty_folders = []
for folder in folders:
    folder_id = folder['id']
    if folder_id not in folders_with_parts and folder_id not in folders_with_children:
        folder_path = get_folder_path(folder_id)
        if 'STL Library V1.4' in folder_path:
            empty_folders.append({
                'id': folder_id,
                'name': folder['name'],
                'path': folder_path,
                'type': folder.get('type', 'part')
            })

print(f"Found {len(empty_folders)} empty folders")

if len(empty_folders) == 0:
    print("No folders to delete!")
    exit()

# Test with first folder
test_folder = empty_folders[0]
print(f"\n=== Testing DELETE on: {test_folder['path']} ===")
print(f"Folder ID: {test_folder['id']}")
print(f"Folder type: {test_folder['type']}")

# Use the CORRECT API endpoint
delete_body = {
    "folderIds": [test_folder['id']],
    "type": test_folder['type']
}

print(f"\nRequest body: {json.dumps(delete_body, indent=2)}")
print(f"\nSending DELETE to /v1/folders/delete...")

delete_resp = requests.delete(
    f"{BASE_URL}/v1/folders/delete",
    headers=headers,
    json=delete_body
)

print(f"Status Code: {delete_resp.status_code}")
print(f"Response: {delete_resp.text}")

# Verify it's gone
print(f"\n=== Verifying folder is deleted ===")
verify_resp = requests.get(
    f"{BASE_URL}/v1/folders/{test_folder['id']}",
    headers=headers
)
print(f"GET /v1/folders/{test_folder['id']} Status: {verify_resp.status_code}")

if verify_resp.status_code == 404:
    print("SUCCESS: Folder not found (deleted!)")
elif verify_resp.status_code == 200:
    print(f"FAILURE: Folder still exists: {verify_resp.text}")
