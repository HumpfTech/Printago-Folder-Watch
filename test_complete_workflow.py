#!/usr/bin/env python3
"""Test complete file upload workflow"""

import requests
import os
import json

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID
}

test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"
filename = "Bookmarks.3mf"

print("Complete Upload Workflow Test")
print("=" * 60)

# Step 1: Upload file to /v1/files/upload
print("\n1. Upload file to /v1/files/upload...")
with open(test_file, 'rb') as f:
    files = {'file': (filename, f, 'application/octet-stream')}

    r = requests.post(f'{API_URL}/v1/files/upload', headers=headers, files=files)
    print(f"   Status: {r.status_code}")
    print(f"   Response: {r.text[:500]}")

    if r.status_code in [200, 201]:
        response_data = r.json()
        print(f"   File URI: {response_data.get('uri') or response_data.get('url') or response_data}")
        file_uri = response_data.get('uri') or response_data.get('url')
    else:
        print("   Upload failed!")
        file_uri = None

# Step 2: Create part with fileUri
if file_uri:
    print("\n2. Create part with fileUri...")
    part_payload = {
        'name': 'Bookmarks Test Part',
        'type': '3mf',
        'description': 'Test upload from folder watch',
        'fileUris': [file_uri],
        'parameters': [],
        'printTags': {},
        'overriddenProcessProfileId': None
    }

    r = requests.post(f'{API_URL}/v1/parts',
                     headers={**headers, 'Content-Type': 'application/json'},
                     json=part_payload)
    print(f"   Status: {r.status_code}")
    print(f"   Response: {r.text[:500]}")

    if r.status_code in [200, 201]:
        print("   SUCCESS! Part created!")

print("\n" + "=" * 60)
