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

print("=== FETCHING FOLDERS ===")
folders_resp = requests.get(f"{BASE_URL}/v1/folders?limit=5", headers=headers)
print(f"Status: {folders_resp.status_code}")
print("\nFirst folder structure:")
folders = folders_resp.json()
if len(folders) > 0:
    print(json.dumps(folders[0], indent=2))

print("\n=== FETCHING PARTS ===")
parts_resp = requests.get(f"{BASE_URL}/v1/parts?limit=5", headers=headers)
print(f"Status: {parts_resp.status_code}")
print("\nFirst part structure:")
parts = parts_resp.json()
if len(parts) > 0:
    print(json.dumps(parts[0], indent=2))

print("\n=== CHECKING FOLDER REFERENCES ===")
print("\nLooking at all folder fields...")
all_folders = requests.get(f"{BASE_URL}/v1/folders?limit=10000", headers=headers).json()
if len(all_folders) > 0:
    sample = all_folders[0]
    print(f"Folder keys: {list(sample.keys())}")
    for key, value in sample.items():
        print(f"  {key}: {type(value).__name__} = {value}")

print("\n=== CHECKING PART FOLDER REFERENCES ===")
all_parts = requests.get(f"{BASE_URL}/v1/parts?limit=10000", headers=headers).json()
if len(all_parts) > 0:
    # Find a part with a folder
    part_with_folder = next((p for p in all_parts if p.get('folderId')), None)
    if part_with_folder:
        print(f"\nPart with folder:")
        print(f"  Part ID: {part_with_folder['id']}")
        print(f"  Part Name: {part_with_folder['name']}")
        print(f"  Folder ID: {part_with_folder.get('folderId')}")

        # Check if there's a folder object embedded
        if 'folder' in part_with_folder:
            print(f"  Has 'folder' object: YES")
            print(f"  Folder object: {json.dumps(part_with_folder['folder'], indent=4)}")
        else:
            print(f"  Has 'folder' object: NO (just folderId)")

print("\n=== CHECKING IF FOLDERS HAVE REFERENCES ===")
# Check if folders have parent objects or just parentId
if len(all_folders) > 0:
    folder_with_parent = next((f for f in all_folders if f.get('parentId')), None)
    if folder_with_parent:
        print(f"\nFolder with parent:")
        print(f"  Folder ID: {folder_with_parent['id']}")
        print(f"  Folder Name: {folder_with_parent['name']}")
        print(f"  Parent ID: {folder_with_parent.get('parentId')}")

        if 'parent' in folder_with_parent:
            print(f"  Has 'parent' object: YES")
            print(f"  Parent object: {json.dumps(folder_with_parent['parent'], indent=4)}")
        else:
            print(f"  Has 'parent' object: NO (just parentId)")

print("\n=== TESTING MOVE API ===")
print("\nLet's see what the move API actually returns...")
# Find a part to test move with (we'll move it to the same folder it's already in)
if len(all_parts) > 0:
    test_part = next((p for p in all_parts if p.get('folderId')), None)
    if test_part:
        print(f"Testing move with part: {test_part['name']} (ID: {test_part['id']})")
        print(f"Current folder: {test_part.get('folderId')}")

        move_payload = {
            "entities": {
                "partIds": [test_part['id']]
            },
            "toFolderId": test_part.get('folderId')  # Move to same folder (no-op)
        }

        print(f"\nSending PATCH request...")
        move_resp = requests.patch(
            f"{BASE_URL}/v1/folders/move",
            headers=headers,
            json=move_payload
        )

        print(f"Status: {move_resp.status_code}")
        print(f"Response: {json.dumps(move_resp.json(), indent=2)}")
