#!/usr/bin/env python3
"""Find the file upload endpoint"""

import requests

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID
}

print("Looking for file upload endpoint...")
print("=" * 60)

# Check existing part to see fileUris format
print("\n1. Check fileUris from existing part...")
r = requests.get(f'{API_URL}/v1/parts', headers=headers)
if r.status_code == 200:
    parts = r.json()
    if parts:
        print(f"   Part: {parts[0]['name']}")
        print(f"   fileUris: {parts[0].get('fileUris', [])}")
        if parts[0].get('fileUris'):
            print(f"   URI format: {parts[0]['fileUris'][0]}")

# Try common upload endpoints
print("\n2. Testing potential upload endpoints...")
endpoints = [
    '/v1/files',
    '/v1/files/upload',
    '/v1/upload',
    '/v1/storage/upload',
    '/v1/parts/upload'
]

for endpoint in endpoints:
    r = requests.options(f'{API_URL}{endpoint}', headers=headers)
    if r.status_code != 404:
        print(f"   {endpoint}: {r.status_code}")

print("\n" + "=" * 60)
