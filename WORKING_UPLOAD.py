#!/usr/bin/env python3
"""WORKING FILE UPLOAD TO PRINTAGO!"""

import requests
import hashlib

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID,
    'Content-Type': 'application/json'
}

test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"
filename = "BookmarksWORKING.3mf"

print("=" * 80)
print("COMPLETE WORKING UPLOAD WORKFLOW")
print("=" * 80)

# Step 1: Get signed upload URL
print("\nStep 1: Get signed upload URL...")
payload = {'filenames': [filename]}
r = requests.post(f'{API_URL}/v1/storage/signed-upload-urls', headers=headers, json=payload)
print(f"Status: {r.status_code}")

if r.status_code != 201:
    print(f"FAILED: {r.text}")
    exit(1)

signed_data = r.json()
upload_url = signed_data['signedUrls'][0]['uploadUrl']
file_path = signed_data['signedUrls'][0]['path']

print(f"SUCCESS! Got upload URL")
print(f"File path: {file_path}")

# Step 2: Calculate file hash
print("\nStep 2: Calculate file hash...")
with open(test_file, 'rb') as f:
    file_content = f.read()
    file_hash = hashlib.sha256(file_content).hexdigest()

print(f"SHA256: {file_hash}")
print(f"Size: {len(file_content):,} bytes")

# Step 3: Upload file to signed URL
print("\nStep 3: Upload file to Google Cloud Storage...")
r = requests.put(upload_url, data=file_content)
print(f"Status: {r.status_code}")

if r.status_code not in [200, 201, 204]:
    print(f"FAILED: {r.text}")
    exit(1)

print("SUCCESS! File uploaded to cloud storage")

# Step 4: Create part with file path
print("\nStep 4: Create part with uploaded file...")
part_payload = {
    'name': 'Bookmarks WORKING',
    'type': '3mf',
    'description': 'Successfully uploaded via API!',
    'fileUris': [file_path],
    'fileHashes': [file_hash],
    'parameters': [],
    'printTags': {},
    'overriddenProcessProfileId': None
}

r = requests.post(f'{API_URL}/v1/parts', headers=headers, json=part_payload)
print(f"Status: {r.status_code}")

if r.status_code in [200, 201]:
    part = r.json()
    print("\nSUCCESS! Part created!")
    print(f"Part ID: {part['id']}")
    print(f"Name: {part['name']}")
    print(f"FileUris: {part['fileUris']}")
    print(f"FileHashes: {part['fileHashes']}")

    if part['fileHashes']:
        print("\n" + "=" * 80)
        print("COMPLETE SUCCESS! FILE UPLOADED AND PART CREATED WITH FILE!")
        print("=" * 80)
    else:
        print("\nPart created but checking file hashes...")
else:
    print(f"FAILED: {r.text}")

print("\nDone!")
