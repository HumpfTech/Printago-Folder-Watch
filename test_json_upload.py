#!/usr/bin/env python3
"""Test JSON upload format for Printago Parts"""

import requests
import base64
import os

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID,
    'Content-Type': 'application/json'
}

print("Testing JSON Part Creation")
print("=" * 60)

test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"

# Try 1: JSON with base64 encoded file
print("\n1. Try with base64 encoded file...")
if os.path.exists(test_file):
    with open(test_file, 'rb') as f:
        file_content = base64.b64encode(f.read()).decode('utf-8')

    payload = {
        'name': 'Bookmarks Test',
        'fileName': 'Bookmarks.3mf',
        'fileData': file_content,
        'fileContent': file_content
    }

    r = requests.post(f'{API_URL}/v1/parts', headers=headers, json=payload)
    print(f"   Status: {r.status_code}")
    print(f"   Response: {r.text[:500]}")

# Try 2: Minimal JSON (just name)
print("\n2. Try minimal JSON (name only)...")
payload = {
    'name': 'Bookmarks Minimal Test'
}

r = requests.post(f'{API_URL}/v1/parts', headers=headers, json=payload)
print(f"   Status: {r.status_code}")
print(f"   Response: {r.text[:500]}")

# Try 3: Check what fields existing parts have
print("\n3. Inspect existing part structure...")
r = requests.get(f'{API_URL}/v1/parts', headers=headers)
if r.status_code == 200:
    parts = r.json()
    if parts:
        print(f"   First part keys: {list(parts[0].keys())}")
        print(f"   Part details:")
        for key in ['name', 'fileName', 'fileUrl', 'id', 'materials']:
            if key in parts[0]:
                print(f"     {key}: {parts[0][key]}")

print("\n" + "=" * 60)
