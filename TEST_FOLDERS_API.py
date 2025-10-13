#!/usr/bin/env python3
"""Test Printago API for folders/collections endpoints"""

import os
import requests
from dotenv import load_dotenv

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

print("Testing Printago API for folders/collections...\n")

# Try different possible endpoints
endpoints = [
    '/v1/folders',
    '/v1/collections',
    '/v1/part-folders',
    '/v1/parts/folders',
]

for endpoint in endpoints:
    try:
        print(f"Testing GET {endpoint}")
        response = session.get(f'{API_URL}{endpoint}', timeout=10)
        print(f"  Status: {response.status_code}")
        if response.status_code == 200:
            data = response.json()
            print(f"  Response: {data}")
            print(f"  ✅ FOUND WORKING ENDPOINT!")
        elif response.status_code == 404:
            print(f"  ❌ Not found")
        else:
            print(f"  Response: {response.text[:200]}")
    except Exception as e:
        print(f"  Error: {str(e)}")
    print()

# Also check what fields are available on parts
print("Checking part structure for folder/collection fields...")
try:
    response = session.get(f'{API_URL}/v1/parts', timeout=10)
    if response.status_code == 200:
        parts = response.json()
        if parts and len(parts) > 0:
            print(f"Sample part fields: {list(parts[0].keys())}")
except Exception as e:
    print(f"Error: {e}")
