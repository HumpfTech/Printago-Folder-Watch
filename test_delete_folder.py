import requests
import json

# Config
API_KEY = "dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg"
STORE_ID = "sb3bexu83dpm0gry8u265amx"
BASE_URL = "https://new-api.printago.io"

headers = {
    "authorization": f"ApiKey {API_KEY}",
    "x-printago-storeid": STORE_ID
}

print("=== STEP 1: Get all folders ===")
folders_resp = requests.get(f"{BASE_URL}/v1/folders?limit=10000", headers=headers)
folders = folders_resp.json()
print(f"Total folders: {len(folders)}")

print("\n=== STEP 2: Get all parts ===")
parts_resp = requests.get(f"{BASE_URL}/v1/parts?limit=10000", headers=headers)
parts = parts_resp.json()
print(f"Total parts: {len(parts)}")

# Build sets
folders_with_parts = set()
for part in parts:
    if part.get('folderId'):
        folders_with_parts.add(part['folderId'])

folders_with_children = set()
for folder in folders:
    if folder.get('parentId'):
        folders_with_children.add(folder['parentId'])

# Build folder path function
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

print("\n=== STEP 3: Find empty folders with 'STL Library V1.4' ===")
empty_folders = []
for folder in folders:
    folder_id = folder['id']
    has_parts = folder_id in folders_with_parts
    has_children = folder_id in folders_with_children

    if not has_parts and not has_children:
        folder_path = get_folder_path(folder_id)
        if 'STL Library V1.4' in folder_path:
            empty_folders.append({
                'id': folder_id,
                'name': folder['name'],
                'path': folder_path
            })

print(f"Found {len(empty_folders)} empty folders")
for f in empty_folders[:5]:
    print(f"  - {f['path']} (ID: {f['id']})")

if len(empty_folders) == 0:
    print("\nNo empty folders to test with!")
    exit()

# Pick the first one to test
test_folder = empty_folders[0]
print(f"\n=== STEP 4: Test DELETE on folder ===")
print(f"Folder: {test_folder['path']}")
print(f"ID: {test_folder['id']}")

print("\nSending DELETE request...")
delete_resp = requests.delete(
    f"{BASE_URL}/v1/folders/{test_folder['id']}",
    headers=headers
)

print(f"Status Code: {delete_resp.status_code}")
print(f"Response Body: {delete_resp.text}")

print("\n=== STEP 5: Verify folder is gone ===")
print("Fetching folder by ID...")
verify_resp = requests.get(
    f"{BASE_URL}/v1/folders/{test_folder['id']}",
    headers=headers
)
print(f"Status Code: {verify_resp.status_code}")
print(f"Response Body: {verify_resp.text}")

print("\n=== STEP 6: Check folder list again ===")
folders_resp2 = requests.get(f"{BASE_URL}/v1/folders?limit=10000", headers=headers)
folders2 = folders_resp2.json()
print(f"Total folders now: {len(folders2)}")

still_exists = any(f['id'] == test_folder['id'] for f in folders2)
print(f"Folder still in list: {still_exists}")

if not still_exists:
    print("\nSUCCESS: Folder was actually deleted!")
else:
    print("\nFAILURE: Folder still exists after delete!")
