#!/usr/bin/env python3
"""Test nested folder creation"""

import os
import sys
sys.path.insert(0, os.path.dirname(__file__))

from printago_watch import PrintagoClient
from dotenv import load_dotenv

load_dotenv()

API_KEY = os.getenv('PRINTAGO_API_KEY')
STORE_ID = os.getenv('PRINTAGO_STORE_ID')
API_URL = 'https://api.printago.io'

client = PrintagoClient(API_URL, API_KEY, STORE_ID)

print("Testing nested folder creation...")
print()

# Test creating a 3-level nested path
test_path = "Level1/Level2/Level3"
print(f"Creating: {test_path}")

try:
    folder_id = client.create_folder(test_path)
    print(f"SUCCESS! Created folder with ID: {folder_id}")
    print()
    print("Folder cache:")
    for path, fid in client.folder_cache.items():
        print(f"  {path} -> {fid}")
    print()

    # Verify in API
    print("Verifying folders in Printago API:")
    response = client.session.get(f'{API_URL}/v1/folders', timeout=30)
    folders = response.json()
    for f in folders:
        print(f"  Name: '{f['name']}', ParentId: {f.get('parentId')}, ID: {f['id']}")

except Exception as e:
    print(f"ERROR: {e}")
    import traceback
    traceback.print_exc()
