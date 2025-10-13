#!/usr/bin/env python3
"""MANUAL TEST - Upload Bookmarks.3mf file"""

import requests
import uuid
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

# Pick the Bookmarks.3mf file
test_file = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"
filename = "Bookmarks.3mf"
name = "Bookmarks"

print("=" * 80)
print("MANUAL UPLOAD TEST - Bookmarks.3mf")
print("=" * 80)

# Step 1: Check file exists
print(f"\nStep 1: Check file exists...")
print(f"File path: {test_file}")
if os.path.exists(test_file):
    file_size = os.path.getsize(test_file)
    print(f"✓ File exists! Size: {file_size:,} bytes ({file_size/1024:.1f} KB)")
else:
    print("✗ FILE NOT FOUND!")
    exit(1)

# Step 2: Read and encode file
print(f"\nStep 2: Read and encode file...")
with open(test_file, 'rb') as f:
    file_content = f.read()
    file_b64 = base64.b64encode(file_content).decode('utf-8')

print(f"✓ File read successfully")
print(f"  Original size: {len(file_content):,} bytes")
print(f"  Base64 size: {len(file_b64):,} characters")

# Step 3: Generate part ID and fileUri
print(f"\nStep 3: Generate part ID and fileUri...")
part_id = str(uuid.uuid4()).replace('-', '')[:24]
file_uri = f"{STORE_ID}/parts/{part_id}/{filename}"
print(f"  Part ID: {part_id}")
print(f"  File URI: {file_uri}")

# Step 4: Create payload
print(f"\nStep 4: Create payload...")
payload = {
    'name': name,
    'type': '3mf',
    'description': 'Manual test upload from folder watch',
    'fileUris': [file_uri],
    'fileData': file_b64,
    'parameters': [],
    'printTags': {},
    'overriddenProcessProfileId': None
}
print(f"✓ Payload created")
print(f"  Payload keys: {list(payload.keys())}")
print(f"  Payload size: {len(str(payload)):,} characters")

# Step 5: POST to /v1/parts
print(f"\nStep 5: POST to /v1/parts...")
print(f"  URL: {API_URL}/v1/parts")
print(f"  Sending request...")

try:
    response = requests.post(
        f'{API_URL}/v1/parts',
        headers=headers,
        json=payload,
        timeout=60
    )

    print(f"\n  Response Status: {response.status_code}")
    print(f"  Response Headers: {dict(response.headers)}")
    print(f"\n  Response Body:")
    print("  " + "-" * 70)

    if response.status_code in [200, 201]:
        part = response.json()
        print(f"  ✓ SUCCESS! Part created!")
        print(f"\n  Part Details:")
        print(f"    ID: {part.get('id')}")
        print(f"    Name: {part.get('name')}")
        print(f"    Type: {part.get('type')}")
        print(f"    FileUris: {part.get('fileUris')}")
        print(f"    FileHashes: {part.get('fileHashes')}")

        if part.get('fileHashes'):
            print(f"\n  ✓✓ FILE HASHES PRESENT - FILE UPLOADED!")
        else:
            print(f"\n  ⚠ Part created but fileHashes empty - checking if processing...")

            # Wait a moment and check again
            import time
            time.sleep(2)

            verify = requests.get(f'{API_URL}/v1/parts/{part["id"]}', headers=headers)
            if verify.status_code == 200:
                verified_part = verify.json()
                print(f"\n  After 2 seconds:")
                print(f"    FileHashes: {verified_part.get('fileHashes')}")

                if verified_part.get('fileHashes'):
                    print(f"  ✓ FILE PROCESSED AND UPLOADED!")
                else:
                    print(f"  ✗ Still no file hashes - file may not be uploading")

    else:
        print(f"  ✗ FAILED!")
        print(f"  Error: {response.text}")

except Exception as e:
    print(f"\n  ✗ EXCEPTION: {e}")
    import traceback
    traceback.print_exc()

print("\n" + "=" * 80)
print("TEST COMPLETE")
print("=" * 80)
