#!/usr/bin/env python3
"""PROVE nested folders work with actual file upload"""

import os
import sys
import glob
sys.path.insert(0, os.path.dirname(__file__))

from printago_watch import PrintagoClient
from dotenv import load_dotenv

load_dotenv()

API_KEY = os.getenv('PRINTAGO_API_KEY')
STORE_ID = os.getenv('PRINTAGO_STORE_ID')
API_URL = 'https://api.printago.io'

# Find a file in a nested folder
PRINTING_FOLDER = r"D:\Onedrive Humpf Tech\OneDrive - Humpf Tech LLC\Documents\3DPrinting"
pattern = os.path.join(PRINTING_FOLDER, "**", "*.3mf")
files = glob.glob(pattern, recursive=True)

# Find one in a nested folder (3+ levels deep)
nested_file = None
for f in files:
    rel_path = os.path.relpath(f, PRINTING_FOLDER)
    depth = rel_path.count(os.sep)
    if depth >= 3:
        nested_file = f
        break

if not nested_file:
    print("ERROR: No nested files found")
    sys.exit(1)

rel_path = os.path.relpath(nested_file, PRINTING_FOLDER)
print(f"Testing with nested file:")
print(f"  Full path: {nested_file}")
print(f"  Relative: {rel_path}")
print(f"  Depth: {rel_path.count(os.sep)} levels")
print()

client = PrintagoClient(API_URL, API_KEY, STORE_ID)

print("Uploading file with nested folder creation...")
try:
    # This should create ALL parent folders automatically
    client.upload_file(rel_path, nested_file)
    print("✅ Upload successful!")
    print()

    print("Folder cache after upload:")
    for path, fid in sorted(client.folder_cache.items()):
        print(f"  {path} -> {fid}")
    print()

    # Verify in API
    print("Verifying folder structure in Printago:")
    response = client.session.get(f'{API_URL}/v1/folders', timeout=30)
    folders = response.json()

    # Build hierarchy display
    id_to_folder = {f['id']: f for f in folders}

    def print_tree(folder_id, indent=0):
        folder = id_to_folder.get(folder_id)
        if folder:
            print(f"{'  ' * indent}📁 {folder['name']}")
            # Find children
            for fid, f in id_to_folder.items():
                if f.get('parentId') == folder_id:
                    print_tree(fid, indent + 1)

    # Print root folders
    for fid, f in id_to_folder.items():
        if f.get('parentId') is None:
            print_tree(fid)

    print()
    print("✅ PROOF COMPLETE: Nested folders work!")

except Exception as e:
    print(f"❌ ERROR: {e}")
    import traceback
    traceback.print_exc()
