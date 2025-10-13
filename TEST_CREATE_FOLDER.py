#!/usr/bin/env python3
"""Test creating a folder in Printago API"""

import os
import requests
from dotenv import load_dotenv
import json

load_dotenv()

API_KEY = os.getenv('PRINTAGO_API_KEY')
STORE_ID = os.getenv('PRINTAGO_STORE_ID')
API_URL = 'https://api.printago.io'

session = requests.Session()
session.headers.update({
    'authorization': f'ApiKey {API_KEY}',
    'x-printago-storeid': STORE_ID,
    'Content-Type': 'application/json'
})

print("Testing folder creation...\n")

# Try different payload formats
test_payloads = [
    {'name': 'TestFolder1'},
    {'name': 'TestFolder2', 'description': ''},
    {'folderName': 'TestFolder3'},
    {'title': 'TestFolder4'},
]

for i, payload in enumerate(test_payloads):
    print(f"Test {i+1}: {json.dumps(payload)}")
    try:
        response = session.post(f'{API_URL}/v1/folders', json=payload, timeout=30)
        print(f"  Status: {response.status_code}")
        print(f"  Response: {response.text[:300]}")
        if response.status_code in [200, 201]:
            print(f"  SUCCESS!")
            break
    except Exception as e:
        print(f"  Error: {str(e)}")
    print()

# List existing folders to see their structure
print("\nListing existing folders to see structure...")
try:
    response = session.get(f'{API_URL}/v1/folders', timeout=30)
    print(f"Status: {response.status_code}")
    folders = response.json()
    if folders:
        print(f"Sample folder: {json.dumps(folders[0], indent=2)}")
    else:
        print("No folders exist yet")
except Exception as e:
    print(f"Error: {e}")
