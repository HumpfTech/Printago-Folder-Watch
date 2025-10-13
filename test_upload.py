#!/usr/bin/env python3
"""Test uploading a file to Printago API"""

import requests
import os
import sys

# Load from .env
def load_env():
    env_vars = {}
    if os.path.exists('.env'):
        with open('.env', 'r') as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith('#') and '=' in line:
                    key, value = line.split('=', 1)
                    env_vars[key.strip()] = value.strip()
    return env_vars

ENV = load_env()
API_URL = ENV.get('PRINTAGO_API_URL', 'https://api.printago.io')
API_KEY = ENV.get('PRINTAGO_API_KEY', '')

headers = {
    'Authorization': f'Bearer {API_KEY}',
    'User-Agent': 'PrintagoFolderWatch/1.0'
}

print("Testing Printago /v1/parts endpoint")
print(f"API URL: {API_URL}")
print("-" * 60)

# Test GET first
print("\n1. GET /v1/parts (list existing parts)...")
try:
    response = requests.get(f'{API_URL}/v1/parts', headers=headers)
    print(f"   Status: {response.status_code}")
    print(f"   Response: {response.text[:200]}")
except Exception as e:
    print(f"   Error: {e}")

# Test POST with minimal data
print("\n2. POST /v1/parts (create test part)...")
try:
    # Try with JSON first
    test_data = {
        'name': 'Test Part from Folder Watch',
        'fileName': 'test.3mf'
    }
    response = requests.post(f'{API_URL}/v1/parts', headers=headers, json=test_data)
    print(f"   Status: {response.status_code}")
    print(f"   Response: {response.text[:500]}")
except Exception as e:
    print(f"   Error: {e}")

print("\n3. Testing with multipart/form-data...")
test_file_path = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting/Bookmarks.3mf"
if os.path.exists(test_file_path):
    try:
        with open(test_file_path, 'rb') as f:
            files = {'file': ('Bookmarks.3mf', f, 'application/octet-stream')}
            data = {'name': 'Bookmarks Test'}

            response = requests.post(f'{API_URL}/v1/parts', headers=headers, files=files, data=data)
            print(f"   Status: {response.status_code}")
            print(f"   Response: {response.text[:500]}")
    except Exception as e:
        print(f"   Error: {e}")
else:
    print(f"   Test file not found: {test_file_path}")

print("\n" + "=" * 60)
