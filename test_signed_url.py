#!/usr/bin/env python3
"""Check if Printago provides signed upload URLs"""

import requests

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID
}

part_id = "dcklbkt7js771qdbp8ldpjrf"  # The part we just created

print("Looking for signed URL / upload endpoint")
print("=" * 60)

# Check existing parts to see if they have upload URLs
print("\n1. Check existing part for upload URL patterns...")
r = requests.get(f'{API_URL}/v1/parts/{part_id}', headers=headers)
if r.status_code == 200:
    part = r.json()
    print(f"   Keys: {list(part.keys())}")
    for key in ['uploadUrl', 'signedUrl', 'putUrl', 'storageUrl']:
        if key in part:
            print(f"   Found {key}: {part[key]}")

# Try common signed URL endpoints
print("\n2. Try requesting upload URL...")
endpoints = [
    f'/v1/parts/{part_id}/upload-url',
    f'/v1/parts/{part_id}/signed-url',
    f'/v1/storage/upload-url',
    f'/v1/files/signed-url'
]

for endpoint in endpoints:
    r = requests.post(f'{API_URL}{endpoint}', headers=headers, json={'filename': 'TestBookmarks.3mf'})
    if r.status_code < 400:
        print(f"   {endpoint}: {r.status_code}")
        print(f"   Response: {r.text[:300]}")
        break
    elif r.status_code != 404:
        print(f"   {endpoint}: {r.status_code} - {r.text[:100]}")

# Try PATCH to update part with file
print("\n3. Try PATCH to update part...")
import base64
test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"
with open(test_file, 'rb') as f:
    file_b64 = base64.b64encode(f.read()).decode('utf-8')

patch_payloads = [
    {'fileData': file_b64},
    {'file': file_b64},
    {'fileContent': file_b64}
]

for payload in patch_payloads:
    r = requests.patch(f'{API_URL}/v1/parts/{part_id}',
                      headers={**headers, 'Content-Type': 'application/json'},
                      json=payload)
    print(f"   PATCH with {list(payload.keys())[0]}: {r.status_code}")
    if r.status_code < 400:
        print(f"   SUCCESS! {r.text[:200]}")
        break

print("\n" + "=" * 60)
