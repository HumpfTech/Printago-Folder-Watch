#!/usr/bin/env python3
"""Test Printago API connection and upload functionality"""

import requests
import os

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

print(f"Testing Printago API")
print(f"API URL: {API_URL}")
print(f"API Key: {API_KEY[:20]}..." if len(API_KEY) > 20 else f"API Key: {API_KEY}")
print("-" * 60)

# Test connection
headers = {
    'Authorization': f'Bearer {API_KEY}',
    'User-Agent': 'PrintagoFolderWatch/1.0'
}

print("\n1. Testing /v1/web-init endpoint...")
try:
    response = requests.get(f'{API_URL}/v1/web-init', headers=headers, timeout=10)
    print(f"   Status: {response.status_code}")
    if response.status_code == 200:
        print(f"   ✅ Connection successful!")
        data = response.json()
        print(f"   Response keys: {list(data.keys())}")
    else:
        print(f"   ❌ Failed: {response.text}")
except Exception as e:
    print(f"   ❌ Error: {e}")

print("\n2. Testing /v1/parts endpoint (GET)...")
try:
    response = requests.get(f'{API_URL}/v1/parts', headers=headers, timeout=10)
    print(f"   Status: {response.status_code}")
    if response.status_code == 200:
        parts = response.json()
        print(f"   ✅ Found {len(parts)} parts")
        if parts:
            print(f"   First part: {parts[0].get('name', 'N/A')}")
    else:
        print(f"   ❌ Failed: {response.text}")
except Exception as e:
    print(f"   ❌ Error: {e}")

print("\n3. Testing /v1/printers endpoint...")
try:
    response = requests.get(f'{API_URL}/v1/printers', headers=headers, timeout=10)
    print(f"   Status: {response.status_code}")
    if response.status_code == 200:
        printers = response.json()
        print(f"   ✅ Found {len(printers)} printers")
        for p in printers:
            print(f"     - {p.get('name', 'N/A')} ({p.get('machineModel', 'N/A')})")
    else:
        print(f"   ❌ Failed: {response.text}")
except Exception as e:
    print(f"   ❌ Error: {e}")

print("\n4. Testing file path...")
test_path = "D:/Onedrive Humpf Tech/OneDrive - Humpf Tech LLC/Documents/3DPrinting"
if os.path.exists(test_path):
    files = [f for f in os.listdir(test_path) if os.path.isfile(os.path.join(test_path, f))]
    print(f"   ✅ Path exists with {len(files)} files")
    print(f"   Sample files: {files[:5]}")
else:
    print(f"   ❌ Path does not exist!")

print("\n" + "=" * 60)
print("Test complete!")
