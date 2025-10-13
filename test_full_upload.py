#!/usr/bin/env python3
"""Test complete upload workflow to Printago"""

import requests
import os

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID
}

print("Testing Full Upload Workflow")
print("=" * 60)

# Test 1: List existing parts
print("\n1. GET /v1/parts (list existing parts)...")
r = requests.get(f'{API_URL}/v1/parts', headers=headers)
print(f"   Status: {r.status_code}")
if r.status_code == 200:
    parts = r.json()
    print(f"   Existing parts: {len(parts)}")
    for p in parts[:3]:
        print(f"     - {p.get('name', 'N/A')}")
else:
    print(f"   Error: {r.text}")

# Test 2: Try uploading a test file
print("\n2. POST /v1/parts (upload test file)...")
test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"

if os.path.exists(test_file):
    print(f"   File: {test_file}")
    print(f"   Size: {os.path.getsize(test_file)} bytes")

    try:
        with open(test_file, 'rb') as f:
            files = {'file': ('Bookmarks.3mf', f, 'application/octet-stream')}
            data = {
                'name': 'Bookmarks Test Upload',
                'fileName': 'Bookmarks.3mf'
            }

            r = requests.post(f'{API_URL}/v1/parts', headers=headers, files=files, data=data)
            print(f"   Status: {r.status_code}")
            print(f"   Response: {r.text[:500]}")

            if r.status_code in [200, 201]:
                print("   SUCCESS! File uploaded as Part")
            else:
                print("   FAILED!")

    except Exception as e:
        print(f"   Error: {e}")
else:
    print(f"   Test file not found!")

# Test 3: Verify it appears in parts list
print("\n3. Verify upload...")
r = requests.get(f'{API_URL}/v1/parts', headers=headers)
if r.status_code == 200:
    parts = r.json()
    print(f"   Total parts now: {len(parts)}")
    bookmarks = [p for p in parts if 'Bookmarks' in p.get('name', '')]
    if bookmarks:
        print(f"   Found Bookmarks part: {bookmarks[0].get('name')}")

print("\n" + "=" * 60)
print("Test complete!")
