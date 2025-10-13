#!/usr/bin/env python3
"""Test the WORKING upload method!"""

import requests
import base64
import os
import uuid

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID,
    'Content-Type': 'application/json'
}

test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"
filename = "BookmarksRealTest.3mf"

print("Testing COMPLETE working upload workflow")
print("=" * 60)

# Read and encode file
print(f"\n1. Reading file: {test_file}")
with open(test_file, 'rb') as f:
    file_content = f.read()
    file_b64 = base64.b64encode(file_content).decode('utf-8')
    file_size = len(file_content)

print(f"   File size: {file_size} bytes")
print(f"   Base64 size: {len(file_b64)} chars")

# Create part with fileData in initial POST
print("\n2. POST /v1/parts with fileData...")
fake_id = str(uuid.uuid4()).replace('-', '')[:24]
payload = {
    'name': 'Bookmarks WORKING Upload',
    'type': '3mf',
    'description': 'Testing with fileData',
    'fileUris': [f"{STORE_ID}/parts/{fake_id}/{filename}"],
    'fileData': file_b64,  # Include file in initial POST!
    'parameters': [],
    'printTags': {},
    'overriddenProcessProfileId': None
}

r = requests.post(f'{API_URL}/v1/parts', headers=headers, json=payload)
print(f"   Status: {r.status_code}")

if r.status_code in [200, 201]:
    part = r.json()
    print(f"   SUCCESS! Created part: {part['id']}")
    print(f"   FileHashes: {part.get('fileHashes')}")

    if part.get('fileHashes'):
        print("\n   🎉 FILE UPLOADED SUCCESSFULLY!")
    else:
        print("\n   ⚠️ Part created but no file hash yet")

    # Verify by getting the part
    print("\n3. Verify by GET /v1/parts/{id}...")
    r = requests.get(f'{API_URL}/v1/parts/{part["id"]}', headers=headers)
    if r.status_code == 200:
        verified = r.json()
        print(f"   FileHashes: {verified.get('fileHashes')}")
        print(f"   FileUris: {verified.get('fileUris')}")

        if verified.get('fileHashes'):
            print("\n   ✅ CONFIRMED: File is fully uploaded and stored!")
        else:
            print("\n   File may be processing...")

else:
    print(f"   FAILED: {r.text[:500]}")

print("\n" + "=" * 60)
