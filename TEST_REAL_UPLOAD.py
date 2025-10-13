#!/usr/bin/env python3
"""
AUTOMATED TEST - Upload a real file from 3DPrinting folder to Printago
This will PROVE the upload workflow works end-to-end
"""

import os
import sys
import hashlib
import requests
from dotenv import load_dotenv
import glob

# Load credentials
load_dotenv()

API_KEY = os.getenv('PRINTAGO_API_KEY')
STORE_ID = os.getenv('PRINTAGO_STORE_ID')
API_URL = os.getenv('PRINTAGO_API_URL', 'https://api.printago.io')

# Find a real .3mf file from the 3DPrinting folder
PRINTING_FOLDER = r"D:\Onedrive Humpf Tech\OneDrive - Humpf Tech LLC\Documents\3DPrinting"
files = glob.glob(os.path.join(PRINTING_FOLDER, "**/*.3mf"), recursive=True)

if not files:
    print("ERROR: No .3mf files found in 3DPrinting folder!")
    sys.exit(1)

# Pick the first file
TEST_FILE = files[0]
print(f"\n{'='*80}")
print(f"TESTING FILE UPLOAD FROM REAL 3DPRINTING FOLDER")
print(f"{'='*80}")
print(f"File: {TEST_FILE}")
print(f"Size: {os.path.getsize(TEST_FILE):,} bytes")
print(f"{'='*80}\n")

# Setup session
session = requests.Session()
session.headers.update({
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID,
    'Content-Type': 'application/json'
})

try:
    # Get filename
    filename = os.path.basename(TEST_FILE)
    name = os.path.splitext(filename)[0]

    print(f"[1/4] Getting signed upload URL for: {filename}")
    signed_response = session.post(
        f'{API_URL}/v1/storage/signed-upload-urls',
        json={'filenames': [filename]},
        timeout=30
    )
    signed_response.raise_for_status()
    signed_data = signed_response.json()
    upload_url = signed_data['signedUrls'][0]['uploadUrl']
    cloud_path = signed_data['signedUrls'][0]['path']
    print(f"    SUCCESS - Cloud path: {cloud_path}")

    print(f"\n[2/4] Reading file and calculating SHA256 hash")
    with open(TEST_FILE, 'rb') as f:
        file_content = f.read()
        file_hash = hashlib.sha256(file_content).hexdigest()
    print(f"    SUCCESS - Hash: {file_hash}")

    print(f"\n[3/4] Uploading {len(file_content):,} bytes to Google Cloud Storage")
    # Use requests.put directly (not session) to avoid auth headers breaking GCS signature
    upload_response = requests.put(upload_url, data=file_content, timeout=120)
    upload_response.raise_for_status()
    print(f"    SUCCESS - Status: {upload_response.status_code}")

    print(f"\n[4/4] Creating part in Printago")
    part_payload = {
        'name': name,
        'type': '3mf',
        'description': 'TEST UPLOAD - Auto-uploaded from folder watch',
        'fileUris': [cloud_path],
        'fileHashes': [file_hash],
        'parameters': [],
        'printTags': {},
        'overriddenProcessProfileId': None
    }

    part_response = session.post(f'{API_URL}/v1/parts', json=part_payload, timeout=60)
    part_response.raise_for_status()
    part_data = part_response.json()
    part_id = part_data.get('id')
    print(f"    SUCCESS - Part created with ID: {part_id}")

    # Wait for processing and verify fileHashes
    import time
    print(f"\n[VERIFY] Waiting 3 seconds for Printago to process file...")
    time.sleep(3)

    verify_response = session.get(f'{API_URL}/v1/parts/{part_id}', timeout=30)
    verify_response.raise_for_status()
    verify_data = verify_response.json()

    print(f"\n{'='*80}")
    print(f"VERIFICATION RESULTS:")
    print(f"{'='*80}")
    print(f"Part ID: {verify_data.get('id')}")
    print(f"Name: {verify_data.get('name')}")
    print(f"Type: {verify_data.get('type')}")
    print(f"FileUris: {verify_data.get('fileUris')}")
    print(f"FileHashes: {verify_data.get('fileHashes')}")
    print(f"{'='*80}")

    if verify_data.get('fileHashes') and len(verify_data.get('fileHashes', [])) > 0:
        print(f"\nSUCCESS!!! File uploaded and hash verified!")
        print(f"Expected hash: {file_hash}")
        print(f"Actual hash:   {verify_data['fileHashes'][0]}")
        if verify_data['fileHashes'][0] == file_hash:
            print(f"\nHASHES MATCH - UPLOAD CONFIRMED WORKING!!!")
        else:
            print(f"\nWARNING: Hashes don't match!")
    else:
        print(f"\nERROR: FileHashes is empty - upload may have failed")

except Exception as e:
    print(f"\nERROR: {str(e)}")
    if hasattr(e, 'response') and e.response is not None:
        print(f"Response: {e.response.text}")
    sys.exit(1)
