#!/usr/bin/env python3
import requests

API_URL = "https://api.printago.io"
API_KEY = "v5v1djw0abk4dxbum058pug5aofe26w210jjik1eal6qq70coxzifoa3s4781mpefj0auv5v"
STORE_ID = "pvi19n308u4wjk4y82qw5ap8"

headers = {
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID
}

print("Testing Printago API with correct auth format")
print("=" * 60)

print("\n1. GET /v1/web-init...")
r = requests.get(f'{API_URL}/v1/web-init', headers=headers)
print(f"   Status: {r.status_code}")
if r.status_code == 200:
    print("   SUCCESS! Authentication working!")
else:
    print(f"   Response: {r.text[:200]}")

print("\n2. GET /v1/parts...")
r = requests.get(f'{API_URL}/v1/parts', headers=headers)
print(f"   Status: {r.status_code}")
if r.status_code == 200:
    parts = r.json()
    print(f"   SUCCESS! Found {len(parts)} parts")
else:
    print(f"   Response: {r.text[:200]}")

print("\n3. GET /v1/printers...")
r = requests.get(f'{API_URL}/v1/printers', headers=headers)
print(f"   Status: {r.status_code}")
if r.status_code == 200:
    printers = r.json()
    print(f"   SUCCESS! Found {len(printers)} printers")
    for p in printers:
        print(f"     - {p.get('name')}")
else:
    print(f"   Response: {r.text[:200]}")

print("\n" + "=" * 60)
