#!/usr/bin/env python3
import requests
import os

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

print("Testing Printago API Authentication")
print(f"API URL: {API_URL}")
print(f"API Key: {API_KEY[:20]}...")
print("-" * 60)

# Test 1: Bearer token
print("\n1. Testing Bearer token auth...")
headers = {'Authorization': f'Bearer {API_KEY}'}
try:
    r = requests.get(f'{API_URL}/v1/web-init', headers=headers)
    print(f"   Status: {r.status_code}")
    if r.status_code == 200:
        print("   SUCCESS!")
    else:
        print(f"   Response: {r.text[:200]}")
except Exception as e:
    print(f"   Error: {e}")

# Test 2: X-API-Key header
print("\n2. Testing X-API-Key header...")
headers = {'X-API-Key': API_KEY}
try:
    r = requests.get(f'{API_URL}/v1/web-init', headers=headers)
    print(f"   Status: {r.status_code}")
    if r.status_code == 200:
        print("   SUCCESS!")
    else:
        print(f"   Response: {r.text[:200]}")
except Exception as e:
    print(f"   Error: {e}")

# Test 3: api_key parameter
print("\n3. Testing api_key query parameter...")
try:
    r = requests.get(f'{API_URL}/v1/web-init?api_key={API_KEY}')
    print(f"   Status: {r.status_code}")
    if r.status_code == 200:
        print("   SUCCESS!")
    else:
        print(f"   Response: {r.text[:200]}")
except Exception as e:
    print(f"   Error: {e}")

# Test 4: Different endpoints
print("\n4. Testing /v1/parts endpoint...")
headers = {'Authorization': f'Bearer {API_KEY}'}
try:
    r = requests.get(f'{API_URL}/v1/parts', headers=headers)
    print(f"   Status: {r.status_code}")
    print(f"   Response: {r.text[:200]}")
except Exception as e:
    print(f"   Error: {e}")

print("\n" + "=" * 60)
print("If all tests show 401, the API key may need to be activated")
print("or might require a different format/endpoint.")
