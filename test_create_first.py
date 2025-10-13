#!/usr/bin/env python3
"""Test creating part WITHOUT file, then updating with file"""

import requests
import uuid

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID,
    'Content-Type': 'application/json'
}

print("Testing: Create part first, then add file")
print("=" * 60)

# Step 1: Create part with dummy/placeholder fileUri
print("\n1. Try creating part with constructed fileUri...")

# Generate a potential part ID (or use placeholder)
filename = "TestBookmarks.3mf"
fake_part_id = str(uuid.uuid4()).replace('-', '')[:24]  # 24 char ID
constructed_uri = f"{STORE_ID}/parts/{fake_part_id}/{filename}"

payload = {
    'name': 'Test Create First',
    'type': '3mf',
    'description': 'Testing creation workflow',
    'fileUris': [constructed_uri],
    'parameters': [],
    'printTags': {},
    'overriddenProcessProfileId': None
}

r = requests.post(f'{API_URL}/v1/parts', headers=headers, json=payload)
print(f"   Status: {r.status_code}")
print(f"   Response: {r.text[:500]}")

if r.status_code in [200, 201]:
    part = r.json()
    print(f"\n   SUCCESS! Created part: {part.get('id')}")
    print(f"   FileUris: {part.get('fileUris')}")

    # Step 2: Now try to upload file to that URI
    print("\n2. Try uploading file to the URI...")
    test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"

    with open(test_file, 'rb') as f:
        # Try different upload endpoints with the part ID
        upload_urls = [
            f'{API_URL}/v1/parts/{part["id"]}/file',
            f'{API_URL}/v1/files/{constructed_uri}',
            f'{API_URL}/v1/storage/{constructed_uri}'
        ]

        for url in upload_urls:
            files = {'file': (filename, f, 'application/octet-stream')}
            r = requests.put(url, headers=headers, files=files)
            print(f"   {url}: {r.status_code}")
            if r.status_code < 400:
                print(f"   SUCCESS! {r.text[:200]}")
                break
            f.seek(0)  # Reset file pointer

# Step 3: Try minimal payload (maybe fileUris is optional?)
print("\n3. Try creating WITHOUT fileUris (minimal)...")
minimal = {
    'name': 'Minimal Test Part',
    'type': '3mf',
    'description': '',
    'parameters': [],
    'printTags': {}
}

r = requests.post(f'{API_URL}/v1/parts', headers=headers, json=minimal)
print(f"   Status: {r.status_code}")
if r.status_code in [200, 201]:
    print(f"   SUCCESS! Part ID: {r.json().get('id')}")
    print(f"   Now we have a part ID, can upload file to it!")
else:
    print(f"   Response: {r.text[:500]}")

print("\n" + "=" * 60)
